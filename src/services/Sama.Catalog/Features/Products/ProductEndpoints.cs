using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sama.Catalog.Domain;
using Sama.Catalog.Persistence;
using Sama.Shared.Infrastructure.Caching;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Kernel.Paging;
using Sama.Shared.Kernel.Primitives;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Security.Permissions;
using Sama.Shared.Security.Tenancy;

namespace Sama.Catalog.Features.Products;

public static class ProductEndpoints
{
    private static readonly CacheKey Keys = new("catalog");

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/v1/products").WithTags("Products");

        // Public: the storefront must be browsable before anyone signs in.
        group.MapGet("/", ListAsync)
            .AllowAnonymous()
            .RateLimit("read")
            .WithSummary("Lists published products, filtered and paged.");

        group.MapGet("/{slug}", GetBySlugAsync)
            .AllowAnonymous()
            .RateLimit("read")
            .WithSummary("Returns one published product by slug.");

        group.MapGet("/manage", ListForManagementAsync)
            .RequirePermission(SamaPermissions.Catalog.WriteProduct)
            .RateLimit("read")
            .WithSummary("Lists products in every status, for the back office.");

        group.MapGet("/manage/{id:guid}", GetForManagementAsync)
            .RequirePermission(SamaPermissions.Catalog.WriteProduct)
            .RateLimit("read")
            .WithSummary("Returns one product in any status, for editing.");

        // Service-to-service: Basket and Ordering re-price lines against the catalogue. Batch,
        // because checkout needs every line at once and N round trips would put the catalogue
        // on the critical path N times.
        group.MapPost("/lookup", LookupAsync)
            .RequirePermission(SamaPermissions.Catalog.ReadProduct)
            .RateLimit("read")
            .WithSummary("Returns current catalogue data for a set of product ids.");

        group.MapPost("/", CreateAsync)
            .RequirePermission(SamaPermissions.Catalog.WriteProduct)
            .RateLimit("write")
            .WithSummary("Creates a product in draft status.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(SamaPermissions.Catalog.WriteProduct)
            .RateLimit("write")
            .WithSummary("Updates a product's descriptive fields.");

        group.MapPatch("/{id:guid}/price", ChangePriceAsync)
            .RequirePermission(SamaPermissions.Catalog.WriteProduct)
            .RateLimit("write")
            .WithSummary("Changes a product's price and publishes a price-changed event.");

        group.MapPost("/{id:guid}/publish", PublishAsync)
            .RequirePermission(SamaPermissions.Catalog.WriteProduct)
            .RateLimit("write")
            .WithSummary("Publishes a draft product to the storefront.");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(SamaPermissions.Catalog.DeleteProduct)
            .RateLimit("write")
            .WithSummary("Soft-deletes a product.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        CatalogDbContext database,
        ISamaCache cache,
        ITenantContext tenant,
        IRateLimiter limiter,
        CancellationToken cancellationToken,
        Guid? categoryId = null,
        string? q = null,
        string? sort = null,
        int page = 1,
        int pageSize = 20)
    {
        var tenantId = tenant.RequireTenantId();
        var request = new PageRequest(page, pageSize);

        // Browsing and searching are charged differently on purpose. A paged listing is served
        // from cache and costs almost nothing; a search term reaches the full-text index on
        // every distinct phrase, so it earns the far tighter bucket. Metering both the same
        // way would either throttle ordinary browsing or leave search effectively unprotected.
        if (!string.IsNullOrWhiteSpace(q))
        {
            var partition = RateLimitPartitionKey.Resolve(context, RateLimitPartition.User);
            var decision = await limiter.AcquireAsync("search", partition, cancellationToken);

            if (!decision.IsAllowed)
            {
                RateLimitResponse.WriteHeaders(context, decision);
                await RateLimitResponse.WriteTooManyRequestsAsync(context, decision);
                return Results.Empty;
            }
        }

        // The cache key carries every input that changes the result. Omitting one — the tenant
        // especially — is how one storefront ends up serving another's catalogue.
        var cacheKey = Keys.ForTenant(
            tenantId, "products",
            categoryId?.ToString("N") ?? "all",
            Normalise(q),
            sort ?? "default",
            request.Page,
            request.PageSize);

        var result = await cache.GetOrCreateAsync(
            cacheKey,
            async ct => await QueryAsync(database, categoryId, q, sort, request, ct),
            CacheDuration.Standard,
            tags: [Keys.TagAll("product")],
            cancellationToken);

        // 60s in the browser, 5 min at the edge, and up to 10 further minutes served stale
        // while one request refreshes it — so a burst of traffic costs the origin one query.
        context.Response.SetPublicCache(
            browserMaxAge: TimeSpan.FromSeconds(60),
            sharedMaxAge: TimeSpan.FromMinutes(5),
            staleWhileRevalidate: TimeSpan.FromMinutes(10));

        context.Response.SetCacheTags($"tenant-{tenantId:N}", "product-list");

        return context.TryCompleteWithETag(result) ? Results.Empty : Results.Ok(result);
    }

    private static async Task<PagedResult<ProductSummary>> QueryAsync(
        CatalogDbContext database,
        Guid? categoryId,
        string? q,
        string? sort,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        // The tenant filter is applied by the model, so it is deliberately absent here.
        var query = database.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Published);

        if (categoryId is { } category)
        {
            query = query.Where(p => p.CategoryId == category);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Full-text match against the generated tsvector, which is GIN-indexed. An ILIKE
            // '%term%' would be a sequential scan on every keystroke.
            query = query.Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", q)));
        }

        query = sort switch
        {
            "price_asc" => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            "price_desc" => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            "newest" => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),

            // Every branch has a tiebreaker on Id: without a total order, two pages can repeat
            // or skip a row when several share a sort value.
            _ => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(p => new ProductSummary(
                p.Id, p.Sku, p.Name, p.Slug, p.Brand, p.Price, p.Currency, p.CategoryId,
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductSummary>(items, request.Page, request.PageSize, total);
    }

    private static async Task<IResult> GetBySlugAsync(
        string slug,
        HttpContext context,
        CatalogDbContext database,
        ISamaCache cache,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenantId();

        var product = await cache.GetOrCreateAsync(
            Keys.ForTenant(tenantId, "product", slug),
            async ct => await database.Products
                .AsNoTracking()
                .Where(p => p.Slug == slug && p.Status == ProductStatus.Published)
                .Select(p => new ProductDetail(
                    p.Id, p.Sku, p.Name, p.Slug, p.Description, p.Brand, p.Price, p.Currency,
                    p.Status.ToString(), p.CategoryId,
                    p.Images.OrderBy(i => i.SortOrder)
                        .Select(i => new ProductImageDto(i.Url, i.AltText, i.SortOrder))
                        .ToList()))
                .FirstOrDefaultAsync(ct),
            CacheDuration.Standard,
            tags: [Keys.TagAll("product")],
            cancellationToken);

        if (product is null)
        {
            return Error.NotFound("catalog.product.notFound", "No such product.").ToProblem();
        }

        context.Response.SetPublicCache(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        context.Response.SetCacheTags($"tenant-{tenantId:N}", $"product-{product.Id:N}");

        return context.TryCompleteWithETag(product) ? Results.Empty : Results.Ok(product);
    }

    private static async Task<IResult> ListForManagementAsync(
        HttpContext context,
        CatalogDbContext database,
        CancellationToken cancellationToken,
        string? q = null,
        ProductStatus? status = null,
        int page = 1,
        int pageSize = 20)
    {
        // Never cached, and never served from the shared cache the storefront uses: this view
        // includes drafts, and leaking one into a public cache entry would publish it.
        context.Response.SetNoStore();

        var request = new PageRequest(page, pageSize);
        var query = database.Products.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(p => p.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p =>
                p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", term)) || p.Sku.Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(p => new ManagedProductSummary(
                p.Id, p.Sku, p.Name, p.Slug, p.Brand, p.Price, p.Currency, p.Status.ToString(), p.CategoryId,
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                p.CreatedAt, p.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<ManagedProductSummary>(items, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetForManagementAsync(
        Guid id,
        HttpContext context,
        CatalogDbContext database,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var product = await database.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDetail(
                p.Id, p.Sku, p.Name, p.Slug, p.Description, p.Brand, p.Price, p.Currency,
                p.Status.ToString(), p.CategoryId,
                p.Images.OrderBy(i => i.SortOrder)
                    .Select(i => new ProductImageDto(i.Url, i.AltText, i.SortOrder))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return product is null
            ? Error.NotFound("catalog.product.notFound", "No such product.").ToProblem()
            : Results.Ok(product);
    }

    private static async Task<IResult> LookupAsync(
        ProductLookupRequest request,
        CatalogDbContext database,
        CancellationToken cancellationToken)
    {
        if (request.ProductIds.Count == 0)
        {
            return Results.Ok(Array.Empty<ProductPricing>());
        }

        if (request.ProductIds.Count > 200)
        {
            return Error.Validation("catalog.lookup.tooMany", "At most 200 product ids per lookup.").ToProblem();
        }

        // Distinct: a caller repeating an id should not widen the IN list or the response.
        var ids = request.ProductIds.Distinct().ToArray();

        var products = await database.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ProductPricing(
                p.Id, p.Sku, p.Name, p.Price, p.Currency, p.Status == ProductStatus.Published,
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return Results.Ok(products);
    }

    private static async Task<IResult> CreateAsync(
        CreateProductRequest request,
        IValidator<CreateProductRequest> validator,
        CatalogDbContext database,
        ISamaCache cache,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        var price = Money.Create(request.Price, request.Currency);
        if (price.IsFailure)
        {
            return price.Error.ToProblem();
        }

        var tenantId = tenant.RequireTenantId();

        var categoryExists = await database.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (!categoryExists)
        {
            return Error.Validation("catalog.category.notFound", "The specified category does not exist.").ToProblem();
        }

        var product = Product.Create(
            tenantId, request.Sku, request.Name, request.Slug, request.Description,
            request.CategoryId, price.Value, request.Brand);

        foreach (var image in request.Images ?? [])
        {
            product.AddImage(image.Url, image.AltText, image.SortOrder);
        }

        database.Products.Add(product);
        await database.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(cache, cancellationToken);

        return Results.Created($"/api/v1/products/{product.Slug}", new { id = product.Id, slug = product.Slug });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        IValidator<UpdateProductRequest> validator,
        CatalogDbContext database,
        ISamaCache cache,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        var product = await database.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.notFound", "No such product.").ToProblem();
        }

        product.UpdateDetails(request.Name, request.Slug, request.Description, request.CategoryId, request.Brand);

        if (request.Images is not null)
        {
            product.ClearImages();
            foreach (var image in request.Images)
            {
                product.AddImage(image.Url, image.AltText, image.SortOrder);
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(cache, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ChangePriceAsync(
        Guid id,
        ChangePriceRequest request,
        IValidator<ChangePriceRequest> validator,
        CatalogDbContext database,
        ISamaCache cache,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        var price = Money.Create(request.Price, request.Currency);
        if (price.IsFailure)
        {
            return price.Error.ToProblem();
        }

        var product = await database.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.notFound", "No such product.").ToProblem();
        }

        product.ChangePrice(price.Value);

        // The price-changed event was raised on the aggregate and is written to the outbox by
        // this same SaveChanges, so the announcement cannot outlive a rolled-back price.
        await database.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(cache, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> PublishAsync(
        Guid id,
        CatalogDbContext database,
        ISamaCache cache,
        CancellationToken cancellationToken)
    {
        var product = await database.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.notFound", "No such product.").ToProblem();
        }

        try
        {
            product.Publish();
        }
        catch (InvalidOperationException ex)
        {
            return Error.Conflict("catalog.product.notPublishable", ex.Message).ToProblem();
        }

        await database.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(cache, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        CatalogDbContext database,
        ISamaCache cache,
        CancellationToken cancellationToken)
    {
        var product = await database.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.notFound", "No such product.").ToProblem();
        }

        // Soft delete: the context's SaveChanges converts this to an update, because orders
        // and inventory reference this id and a hard delete would strand them.
        database.Products.Remove(product);
        await database.SaveChangesAsync(cancellationToken);
        await InvalidateAsync(cache, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Drops every cached product view.
    /// <para>
    /// Invalidating by tag rather than by key is what makes this tractable: one product's price
    /// appears in its detail page and in an unbounded number of filtered, sorted, paged list
    /// responses, and enumerating those keys is not possible at write time.
    /// </para>
    /// </summary>
    private static ValueTask InvalidateAsync(ISamaCache cache, CancellationToken cancellationToken)
        => cache.RemoveByTagAsync(Keys.TagAll("product"), cancellationToken);

    private static string Normalise(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToLowerInvariant();

    private static IResult ToValidationProblem(FluentValidation.Results.ValidationResult validation)
    {
        var details = validation.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return Error.Validation("request.invalid", "One or more fields are invalid.", details).ToProblem();
    }
}
