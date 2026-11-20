using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLimitExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLimitExtensions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfileId = table.Column<long>(type: "bigint", nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompanyLicenseId = table.Column<int>(type: "integer", nullable: true),
                    ManagerUserId = table.Column<int>(type: "integer", nullable: true),
                    AdditionalTeams = table.Column<int>(type: "integer", nullable: false),
                    PreviousLimit = table.Column<int>(type: "integer", nullable: false),
                    NewLimit = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtendedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLimitExtensions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLimitExtensions_CompanyLicenseId",
                schema: "public",
                table: "TeamLimitExtensions",
                column: "CompanyLicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLimitExtensions_ProfileId",
                schema: "public",
                table: "TeamLimitExtensions",
                column: "ProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamLimitExtensions",
                schema: "public");
        }
    }
}
