using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Sama.Catalog.Persistence.Migrations
{
    /// <summary>
    /// Adds the Arabic name and description, and folds them into the full-text
    /// index so an Arabic search term matches.
    /// </summary>
    /// <remarks>
    /// The statement order here is deliberate and differs from what EF scaffolded.
    ///
    /// search_vector is a generated column whose expression names the columns it
    /// reads. EF emitted the AlterColumn that adds name_ar and description_ar to
    /// that expression *before* the AddColumn statements that create them, so
    /// PostgreSQL rejected the migration with "column name_ar does not exist".
    /// Down had the mirror fault: it dropped the columns while the generated
    /// expression still depended on them.
    ///
    /// Columns are therefore created before the expression references them, and
    /// the reference is removed before they are dropped.
    /// </remarks>
    public partial class AddArabicProductText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_ar",
                schema: "catalog",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description_ar",
                schema: "catalog",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_vector",
                schema: "catalog",
                table: "products",
                type: "tsvector",
                nullable: false,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector")
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "name", "name_ar", "brand", "description", "description_ar" })
                .OldAnnotation("Npgsql:TsVectorConfig", "english")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "name", "brand", "description" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_vector",
                schema: "catalog",
                table: "products",
                type: "tsvector",
                nullable: false,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector")
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "name", "brand", "description" })
                .OldAnnotation("Npgsql:TsVectorConfig", "english")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "name", "name_ar", "brand", "description", "description_ar" });

            migrationBuilder.DropColumn(
                name: "description_ar",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "name_ar",
                schema: "catalog",
                table: "products");
        }
    }
}
