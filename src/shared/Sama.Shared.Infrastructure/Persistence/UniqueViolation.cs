using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sama.Shared.Kernel.Results;

namespace Sama.Shared.Infrastructure.Persistence;

/// <summary>
/// Turns a PostgreSQL unique-index violation into a domain <see cref="Error"/>.
/// </summary>
/// <remarks>
/// Checking for a duplicate with a SELECT before inserting looks tidier but does
/// not actually prevent one: two requests can both find nothing and both insert.
/// The unique index is the only authority that holds under concurrency, so the
/// correct place to handle a collision is where the database reports it.
///
/// Without this the exception escapes the handler and the caller receives a 500
/// carrying an EF Core stack trace, which reads as "the server is broken" rather
/// than "that SKU is already taken".
/// </remarks>
public static class UniqueViolation
{
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    /// The violated constraint's name, or null when the failure was something else
    /// and therefore genuinely unexpected.
    /// </summary>
    public static string? ConstraintName(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState } postgres
            ? postgres.ConstraintName
            : null;

    /// <summary>
    /// Maps a save failure onto one of <paramref name="conflicts"/>, keyed by
    /// constraint name. Returns false for anything unmapped so a genuinely
    /// unexpected failure keeps propagating instead of being reported as a
    /// conflict the caller could resolve.
    /// </summary>
    public static bool TryMap(
        DbUpdateException exception,
        IReadOnlyDictionary<string, Error> conflicts,
        out Error error)
    {
        var constraint = ConstraintName(exception);

        if (constraint is not null && conflicts.TryGetValue(constraint, out var mapped))
        {
            error = mapped;
            return true;
        }

        error = default!;
        return false;
    }
}
