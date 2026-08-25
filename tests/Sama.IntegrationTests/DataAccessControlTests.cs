using Microsoft.EntityFrameworkCore;
using Sama.Catalog.Domain;
using Sama.Shared.Kernel.Primitives;
using Shouldly;

namespace Sama.IntegrationTests;

/// <summary>
/// Tenant isolation and soft deletion, against real PostgreSQL.
///
/// These run against a database on purpose. The filters are EF Core model-level query filters,
/// and the only thing that proves they work is watching them become a WHERE clause — an
/// in-memory provider would happily pass while the generated SQL leaked every tenant's rows.
/// </summary>
[Collection(SamaCollection.Name)]
public class DataAccessControlTests(SamaFixture fixture)
{
    /// <summary>
    /// The running test's cancellation token. Threading it through every awaited call is what
    /// lets a suite backed by containers actually stop when it is cancelled, rather than
    /// blocking on a database that is going away.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Category> SeedCategoryAsync(SamaFixture fixture, Guid tenantId, string slug)
    {
        await using var context = fixture.CreateCatalogContext(tenantId);
        var category = Category.Create(tenantId, "Coffee", slug, parentId: null);
        context.Categories.Add(category);
        await context.SaveChangesAsync(Ct);
        return category;
    }

    private static async Task<Product> SeedProductAsync(
        SamaFixture fixture, Guid tenantId, Guid categoryId, string sku, string slug)
    {
        await using var context = fixture.CreateCatalogContext(tenantId);

        var product = Product.Create(
            tenantId, sku, "Espresso", slug, "A dark roast.",
            categoryId, Money.Create(19.99m, "USD").Value);

        product.AddImage("/media/a.jpg", "a", 0);
        product.Publish();

        context.Products.Add(product);
        await context.SaveChangesAsync(Ct);
        return product;
    }

    [Fact]
    public async Task A_query_without_a_tenant_predicate_still_cannot_see_another_tenants_rows()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var acmeCategory = await SeedCategoryAsync(fixture, TestTenants.Acme, $"acme-{stamp}");
        var globexCategory = await SeedCategoryAsync(fixture, TestTenants.Globex, $"globex-{stamp}");

        await SeedProductAsync(fixture, TestTenants.Acme, acmeCategory.Id, $"ACME-{stamp}", $"acme-p-{stamp}");
        await SeedProductAsync(fixture, TestTenants.Globex, globexCategory.Id, $"GLOBEX-{stamp}", $"globex-p-{stamp}");

        await using var context = fixture.CreateCatalogContext(TestTenants.Acme);

        // Deliberately no tenant predicate: this is the mistake the model-level filter exists
        // to survive.
        var skus = await context.Products.Select(p => p.Sku).ToListAsync(Ct);

        skus.ShouldContain($"ACME-{stamp}");
        skus.ShouldNotContain($"GLOBEX-{stamp}");
    }

    [Fact]
    public async Task A_by_id_lookup_across_tenants_returns_nothing()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var category = await SeedCategoryAsync(fixture, TestTenants.Globex, $"g-cat-{stamp}");
        var product = await SeedProductAsync(fixture, TestTenants.Globex, category.Id, $"G-{stamp}", $"g-{stamp}");

        await using var context = fixture.CreateCatalogContext(TestTenants.Acme);

        // Guessing an id from another tenant must find nothing, not a 403 that confirms it exists.
        (await context.Products.FirstOrDefaultAsync(p => p.Id == product.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_tenant_filter_appears_in_the_generated_sql()
    {
        await using var context = fixture.CreateCatalogContext(TestTenants.Acme);

        var sql = context.Products.ToQueryString();

        sql.ShouldContain("tenant_id", Case.Insensitive);
        sql.ShouldContain("is_deleted", Case.Insensitive);
    }

    [Fact]
    public async Task A_background_context_with_no_tenant_sees_nothing_until_it_opts_out()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var category = await SeedCategoryAsync(fixture, TestTenants.Acme, $"bg-cat-{stamp}");
        await SeedProductAsync(fixture, TestTenants.Acme, category.Id, $"BG-{stamp}", $"bg-{stamp}");

        await using var context = fixture.CreateCatalogContext(tenantId: null);

        // No tenant resolves to Guid.Empty, which matches no row. A worker must opt out of the
        // filter deliberately rather than inherit an arbitrary tenant.
        (await context.Products.CountAsync(Ct)).ShouldBe(0);

        context.TenantFilterEnabled = false;
        (await context.Products.CountAsync(Ct)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Removing_a_soft_deletable_entity_updates_it_instead_of_deleting_the_row()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var category = await SeedCategoryAsync(fixture, TestTenants.Acme, $"sd-cat-{stamp}");
        var product = await SeedProductAsync(fixture, TestTenants.Acme, category.Id, $"SD-{stamp}", $"sd-{stamp}");

        await using (var context = fixture.CreateCatalogContext(TestTenants.Acme))
        {
            var tracked = await context.Products.FirstAsync(p => p.Id == product.Id, Ct);
            context.Products.Remove(tracked);
            await context.SaveChangesAsync(Ct);
        }

        await using var verify = fixture.CreateCatalogContext(TestTenants.Acme);

        // Gone from ordinary queries...
        (await verify.Products.FirstOrDefaultAsync(p => p.Id == product.Id, Ct)).ShouldBeNull();

        // ...but the row survives, because orders and other services reference this id.
        var retained = await verify.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == product.Id, Ct);
        retained.ShouldNotBeNull();
        retained!.IsDeleted.ShouldBeTrue();
        retained.DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Soft_deleting_a_product_frees_its_slug_for_reuse()
    {
        // The unique index is filtered on is_deleted, so a withdrawn product does not
        // permanently reserve its URL.
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var slug = $"reuse-{stamp}";
        var category = await SeedCategoryAsync(fixture, TestTenants.Acme, $"re-cat-{stamp}");

        var first = await SeedProductAsync(fixture, TestTenants.Acme, category.Id, $"RE1-{stamp}", slug);

        await using (var context = fixture.CreateCatalogContext(TestTenants.Acme))
        {
            context.Products.Remove(await context.Products.FirstAsync(p => p.Id == first.Id, Ct));
            await context.SaveChangesAsync(Ct);
        }

        var second = await SeedProductAsync(fixture, TestTenants.Acme, category.Id, $"RE2-{stamp}", slug);

        second.Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public async Task The_same_slug_is_allowed_in_two_different_tenants()
    {
        // Uniqueness is per tenant: two storefronts may each sell an "espresso".
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var slug = $"shared-{stamp}";

        var acmeCategory = await SeedCategoryAsync(fixture, TestTenants.Acme, $"sa-{stamp}");
        var globexCategory = await SeedCategoryAsync(fixture, TestTenants.Globex, $"sg-{stamp}");

        await SeedProductAsync(fixture, TestTenants.Acme, acmeCategory.Id, $"SA-{stamp}", slug);
        await SeedProductAsync(fixture, TestTenants.Globex, globexCategory.Id, $"SG-{stamp}", slug);

        await using var context = fixture.CreateCatalogContext(TestTenants.Acme);
        (await context.Products.CountAsync(p => p.Slug == slug, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Audit_columns_are_stamped_on_save_without_the_caller_setting_them()
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var actor = Guid.CreateVersion7();

        await using var context = fixture.CreateCatalogContext(TestTenants.Acme, actor);
        var category = Category.Create(TestTenants.Acme, "Audited", $"audited-{stamp}", null);
        context.Categories.Add(category);
        await context.SaveChangesAsync(Ct);

        category.CreatedAt.ShouldBe(fixture.Clock.UtcNow);
        category.CreatedBy.ShouldBe(actor);
    }
}
