using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Sama.Shared.Infrastructure.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Rewrites every table, column, key, index and constraint name to snake_case.
    /// <para>
    /// PostgreSQL folds unquoted identifiers to lower case, so EF's default PascalCase names
    /// end up permanently quoted — every hand-written query, every psql session and every
    /// migration script has to say <c>"ProductId"</c>. snake_case avoids that for good.
    /// Call this last, after all entity configuration has been applied.
    /// </para>
    /// </summary>
    public static ModelBuilder UseSnakeCaseNames(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is not null)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                var name = key.GetName();
                if (name is not null)
                {
                    key.SetName(ToSnakeCase(name));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var name = foreignKey.GetConstraintName();
                if (name is not null)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(name));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                var name = index.GetDatabaseName();
                if (name is not null)
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }

        return modelBuilder;
    }

    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                // Break before an upper-case run's start, and before its final letter when the
                // next char is lower-case: "OrderID" -> order_id, "HTTPStatus" -> http_status.
                var previousIsLower = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                var previousIsUpper = i > 0 && char.IsUpper(name[i - 1]);

                if (i > 0 && (previousIsLower || (previousIsUpper && nextIsLower)) && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
