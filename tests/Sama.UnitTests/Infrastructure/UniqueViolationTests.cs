using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Kernel.Results;

namespace Sama.UnitTests.Infrastructure;

/// <summary>
/// A duplicate SKU used to escape the catalogue handler as an unhandled
/// <see cref="DbUpdateException"/>, so an author who reused a code got a 500 and a
/// stack trace instead of being told which field to change.
/// </summary>
public sealed class UniqueViolationTests
{
    private static readonly Error SkuTaken =
        Error.Conflict("catalog.product.skuTaken", "Another product already uses that SKU.");

    private static readonly Error SlugTaken =
        Error.Conflict("catalog.product.slugTaken", "Another product already uses that web address.");

    private static readonly Dictionary<string, Error> Conflicts = new()
    {
        ["ux_products_tenant_sku"] = SkuTaken,
        ["ux_products_tenant_slug"] = SlugTaken,
    };

    /// <summary>
    /// PostgresException's constructor does not take a constraint name, and the
    /// property is init-only on the Data dictionary, so the fields have to be set
    /// the same way Npgsql populates them from the server's error response.
    /// </summary>
    private static DbUpdateException Violation(string constraintName)
    {
        var postgres = new PostgresException(
            messageText: "duplicate key value violates unique constraint",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "23505",
            constraintName: constraintName);

        return new DbUpdateException("save failed", postgres);
    }

    [Fact]
    public void Maps_a_violated_constraint_to_its_conflict()
    {
        var mapped = UniqueViolation.TryMap(Violation("ux_products_tenant_sku"), Conflicts, out var error);

        Assert.True(mapped);
        Assert.Equal(SkuTaken.Code, error.Code);
    }

    [Fact]
    public void Distinguishes_the_sku_index_from_the_slug_index()
    {
        // Both are unique indexes on the same table; telling the author "that SKU is
        // taken" when the slug collided would send them to change the wrong field.
        UniqueViolation.TryMap(Violation("ux_products_tenant_slug"), Conflicts, out var error);

        Assert.Equal(SlugTaken.Code, error.Code);
    }

    [Fact]
    public void Leaves_an_unmapped_constraint_alone()
    {
        // An index nobody has written a message for is still a genuine surprise, and
        // reporting it as a resolvable conflict would hide it.
        var mapped = UniqueViolation.TryMap(Violation("ux_products_something_new"), Conflicts, out _);

        Assert.False(mapped);
    }

    [Fact]
    public void Ignores_failures_that_are_not_unique_violations()
    {
        var foreignKey = new PostgresException(
            messageText: "insert or update violates foreign key constraint",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "23503",
            constraintName: "ux_products_tenant_sku");

        var mapped = UniqueViolation.TryMap(
            new DbUpdateException("save failed", foreignKey), Conflicts, out _);

        Assert.False(mapped);
    }

    [Fact]
    public void Ignores_a_save_failure_with_no_postgres_cause()
    {
        // A timeout or a disposed connection must not be reported as "that SKU is taken".
        var mapped = UniqueViolation.TryMap(
            new DbUpdateException("save failed", new TimeoutException()), Conflicts, out _);

        Assert.False(mapped);
    }
}
