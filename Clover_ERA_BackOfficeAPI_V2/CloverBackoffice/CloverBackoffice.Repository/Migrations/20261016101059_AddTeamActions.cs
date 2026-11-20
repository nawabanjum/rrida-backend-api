using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamActionAudits",
                schema: "public",
                columns: table => new
                {
                    AuditId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfileId = table.Column<long>(type: "bigint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CycleKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rung = table.Column<int>(type: "integer", nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CommentId = table.Column<long>(type: "bigint", nullable: true),
                    ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ByUserId = table.Column<int>(type: "integer", nullable: true),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamActionAudits", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "TeamActionComments",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfileId = table.Column<long>(type: "bigint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CycleKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rung = table.Column<int>(type: "integer", nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamActionComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamActionComments_BusinessUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamActionComments_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamActionStates",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfileId = table.Column<long>(type: "bigint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CycleKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rung = table.Column<int>(type: "integer", nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamActionStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamActionStates_BusinessUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamActionStates_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamActionAudits_Unit_Cycle_Rung",
                schema: "public",
                table: "TeamActionAudits",
                columns: new[] { "UnitId", "CycleKey", "Rung" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamActionComments_ProfileId",
                schema: "public",
                table: "TeamActionComments",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamActionComments_Unit_Cycle_Rung",
                schema: "public",
                table: "TeamActionComments",
                columns: new[] { "UnitId", "CycleKey", "Rung" },
                filter: "\"IsDel\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_TeamActionStates_ProfileId",
                schema: "public",
                table: "TeamActionStates",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "UX_TeamActionStates_Unit_Cycle_Rung",
                schema: "public",
                table: "TeamActionStates",
                columns: new[] { "UnitId", "CycleKey", "Rung" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamActionAudits",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TeamActionComments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TeamActionStates",
                schema: "public");
        }
    }
}
