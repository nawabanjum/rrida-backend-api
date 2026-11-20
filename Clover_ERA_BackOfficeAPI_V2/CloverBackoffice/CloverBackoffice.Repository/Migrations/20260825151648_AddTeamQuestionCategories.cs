using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamQuestionCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TeamDailyQuestionSpecifications_SubCategory_Variant_Ordinal_Culture",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.AddColumn<long>(
                name: "CategoryId",
                table: "TeamDailyQuestionSpecifications",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "TeamQuestionCategories",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamQuestionCategories", x => x.Id);
                });

            // The general / default category, seeded at Id 0.
            //
            // This MUST run before the foreign key below: the column added above defaults
            // every existing specification to CategoryId = 0, so without this row the FK
            // would be violated by the rows already in the table.
            //
            // The Id is written explicitly, which the identity column accepts because it is
            // BY DEFAULT rather than ALWAYS. The sequence still starts at 1, so no
            // user-created category can ever collide with it.
            migrationBuilder.Sql(@"
                INSERT INTO public.""TeamQuestionCategories""
                    (""Id"", ""Name"", ""Description"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"", ""IsDel"")
                VALUES
                    (0, 'General', 'Default category. Questions here are served to every team.', TRUE, NOW(), NOW(), FALSE)
                ON CONFLICT (""Id"") DO NOTHING;");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionSpecifications_Category",
                table: "TeamDailyQuestionSpecifications",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyQuestionSpecifications_Category_SubCategory_Variant_Ordinal_Culture",
                table: "TeamDailyQuestionSpecifications",
                columns: new[] { "CategoryId", "SubCategory", "Variant", "Ordinal", "ContentCultureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamQuestionCategories_IsActive",
                schema: "public",
                table: "TeamQuestionCategories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "UX_TeamQuestionCategories_Name",
                schema: "public",
                table: "TeamQuestionCategories",
                column: "Name",
                unique: true,
                filter: "\"IsDel\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_TeamQuestionCategories_Category",
                table: "TeamDailyQuestionSpecifications",
                column: "CategoryId",
                principalSchema: "public",
                principalTable: "TeamQuestionCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_TeamQuestionCategories_Category",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropTable(
                name: "TeamQuestionCategories",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestionSpecifications_Category",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "UX_TeamDailyQuestionSpecifications_Category_SubCategory_Variant_Ordinal_Culture",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyQuestionSpecifications_SubCategory_Variant_Ordinal_Culture",
                table: "TeamDailyQuestionSpecifications",
                columns: new[] { "SubCategory", "Variant", "Ordinal", "ContentCultureId" },
                unique: true);
        }
    }
}
