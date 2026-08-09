using Microsoft.EntityFrameworkCore;

namespace Mosaic.ServiceDefaults.Data;

/// <summary>
/// Renames every table and column in a model to snake_case.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL folds unquoted identifiers to lower case, so a table EF Core
/// calls <c>StockLevels</c> has to be quoted in every hand-written query.
/// Renaming once means the SQL in the logs is the SQL you would type, which
/// matters a great deal in a book that spends its time reading generated
/// queries.
/// </para>
/// <para>
/// Chapter 4 wrote this inside <c>MosaicDbContext</c>. Chapter 12 made six
/// contexts out of one, and a naming convention that differed between two
/// services would be six different-looking logs for no reason. It is a platform
/// concern rather than a contract between services, which is the test for what
/// belongs in this project.
/// </para>
/// </remarks>
public static class SnakeCaseNaming
{
    public static void UseSnakeCaseNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            // Complex properties are not in GetProperties, and forgetting them
            // is only visible in the SQL: Money's two columns arrive as
            // "Amount_Amount" and "Amount_Currency", quoted, in the middle of
            // an otherwise lower-case statement.
            foreach (var complex in entity.GetComplexProperties())
            {
                foreach (var property in complex.ComplexType.GetProperties())
                {
                    property.SetColumnName(ToSnakeCase(property.GetColumnName()));
                }
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (char.IsUpper(c) && i > 0 && name[i - 1] != '_')
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
