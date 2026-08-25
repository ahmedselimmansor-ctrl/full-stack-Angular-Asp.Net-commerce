using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Catalog.Domain;
using Sama.Catalog.Persistence;
using Sama.Shared.Infrastructure.Caching;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Security.Permissions;
using Sama.Shared.Security.Tenancy;

namespace Sama.Catalog.Features.Categories;

public sealed record CategoryNode(Guid Id, string Name, string Slug, int SortOrder, IReadOnlyList<CategoryNode> Children);

public sealed record CreateCategoryRequest(string Name, string Slug, Guid? ParentId, int SortOrder);

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Slug).NotEmpty().MaximumLength(200)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("A slug must be lower-case words separated by single hyphens.");
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10_000);
    }
}

public static class CategoryEndpoints
{
    private static readonly CacheKey Keys = new("catalog");

    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/v1/categories").WithTags("Categories");

        group.MapGet("/", GetTreeAsync)
            .AllowAnonymous()
            .RateLimit("read")
            .WithSummary("Returns the category tree for the storefront.");

        group.MapPost("/", CreateAsync)
            .RequirePermission(SamaPermissions.Catalog.WriteCategory)
            .RateLimit("write")
            .WithSummary("Creates a category.");

        return routes;
    }

    private static async Task<IResult> GetTreeAsync(
        HttpContext context,
        CatalogDbContext database,
        ISamaCache cache,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenantId();

        var tree = await cache.GetOrCreateAsync(
            Keys.ForTenant(tenantId, "category-tree"),
            async ct =>
            {
                // The whole tree is loaded in one query and assembled in memory. A recursive
                // CTE would also work, but a storefront's category count is in the hundreds,
                // and one flat read beats N round trips for depth.
                var flat = await database.Categories
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                    .Select(c => new { c.Id, c.Name, c.Slug, c.SortOrder, c.ParentId })
                    .ToListAsync(ct);

                var byParent = flat
                    .GroupBy(c => c.ParentId)
                    .ToDictionary(g => g.Key ?? Guid.Empty, g => g.ToList());

                List<CategoryNode> Build(Guid parentKey)
                    => byParent.TryGetValue(parentKey, out var children)
                        ? [.. children.Select(c => new CategoryNode(c.Id, c.Name, c.Slug, c.SortOrder, Build(c.Id)))]
                        : [];

                return Build(Guid.Empty);
            },
            // Categories change far less often than products, so they earn a longer TTL.
            CacheDuration.Extended,
            tags: [Keys.TagAll("category")],
            cancellationToken);

        context.Response.SetPublicCache(TimeSpan.FromMinutes(5), TimeSpan.FromHours(1), TimeSpan.FromHours(6));
        context.Response.SetCacheTags($"tenant-{tenantId:N}", "category-tree");

        return context.TryCompleteWithETag(tree) ? Results.Empty : Results.Ok(tree);
    }

    private static async Task<IResult> CreateAsync(
        CreateCategoryRequest request,
        IValidator<CreateCategoryRequest> validator,
        CatalogDbContext database,
        ISamaCache cache,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var details = validation.Errors
                .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal);

            return Error.Validation("request.invalid", "One or more fields are invalid.", details).ToProblem();
        }

        if (request.ParentId is { } parentId
            && !await database.Categories.AnyAsync(c => c.Id == parentId, cancellationToken))
        {
            return Error.Validation("catalog.category.parentNotFound", "The parent category does not exist.").ToProblem();
        }

        var category = Category.Create(
            tenant.RequireTenantId(), request.Name, request.Slug, request.ParentId, request.SortOrder);

        database.Categories.Add(category);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (UniqueViolation.TryMap(exception, CategoryConflicts, out var conflict))
        {
            return conflict.ToProblem();
        }

        await cache.RemoveByTagAsync(Keys.TagAll("category"), cancellationToken);

        return Results.Created($"/api/v1/categories/{category.Slug}", new { id = category.Id, slug = category.Slug });
    }

    private static readonly Dictionary<string, Error> CategoryConflicts = new()
    {
        ["ux_categories_tenant_slug"] = Error.Conflict(
            "catalog.category.slugTaken", "Another category already uses that web address."),
    };
}
