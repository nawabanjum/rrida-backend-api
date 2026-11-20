using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// A manager licence now covers up to 3 teams (the purchased team plus up to
    /// 2 complimentary same-manager teams), so the unique "one active grant per
    /// licence" index on ComplimentaryLicenseGrants is replaced by a plain one.
    /// The limit (LicenseTeamLimits.MaxTeamsPerLicense) is enforced in
    /// CompanyLicenseRepository.AddComplimentaryGrantAsync under an advisory lock.
    ///
    /// Written by hand like the other migrations here. Mirrors
    /// Scripts/Licenses/01_AllowThreeTeamsPerLicense.idempotent.sql.
    /// </summary>
    public partial class AllowThreeTeamsPerLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS public."IX_ComplimentaryLicenseGrants_CompanyLicenseId_IsActive";

                CREATE INDEX IF NOT EXISTS "IX_ComplimentaryLicenseGrants_CompanyLicenseId_IsActive"
                    ON public."ComplimentaryLicenseGrants" ("CompanyLicenseId", "IsActive")
                    WHERE "IsActive" = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails if a licence already holds more than one active grant.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS public."IX_ComplimentaryLicenseGrants_CompanyLicenseId_IsActive";

                CREATE UNIQUE INDEX "IX_ComplimentaryLicenseGrants_CompanyLicenseId_IsActive"
                    ON public."ComplimentaryLicenseGrants" ("CompanyLicenseId", "IsActive")
                    WHERE "IsActive" = true;
                """);
        }
    }
}
