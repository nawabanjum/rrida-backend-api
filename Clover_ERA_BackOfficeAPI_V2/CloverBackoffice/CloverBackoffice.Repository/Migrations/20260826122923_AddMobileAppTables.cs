using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Brings the four tables the mobile app owns into the EF model.
    ///
    /// DailyReflections, TimeAllocation, FeedBack and Subscriptions were all created
    /// by the Azure Functions API and already hold live data, so the generated
    /// CreateTable operations have been removed — running them would fail on the first
    /// statement in every environment. They remain in the model snapshot, which is
    /// what stops the next migration re-emitting them; this is the same treatment the
    /// other hand-applied objects in this repository get.
    ///
    /// What IS left is the part the old API never created: an index on each foreign
    /// key, plus the two composite indexes the reporting queries need. Without them
    /// every lookup of a reflection's time allocations is a sequential scan, and that
    /// table already has hundreds of rows per team.
    ///
    /// Written as raw SQL with IF NOT EXISTS rather than CreateIndex so the migration
    /// is safe to run against an environment where some of this has been applied by
    /// hand. Constraint names are deliberately not touched: the live primary keys are
    /// called DailyReflections_pkey and PK_TimeEntryBand rather than what EF would
    /// generate, and renaming them buys nothing — EF never refers to them at runtime.
    /// </summary>
    public partial class AddMobileAppTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_DailyReflections_Unit_Date""
                    ON public.""DailyReflections"" (""BusinessUnitId"", ""ReflectionLocalDate"");

                CREATE INDEX IF NOT EXISTS ""IX_DailyReflections_BusinessUnitId""
                    ON public.""DailyReflections"" (""BusinessUnitId"");

                CREATE INDEX IF NOT EXISTS ""IX_TimeAllocation_DailyReflectionId""
                    ON public.""TimeAllocation"" (""DailyReflectionId"");

                CREATE INDEX IF NOT EXISTS ""IX_FeedBack_Unit""
                    ON public.""FeedBack"" (""BusinessUnitId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS public.""IX_DailyReflections_Unit_Date"";
                DROP INDEX IF EXISTS public.""IX_DailyReflections_BusinessUnitId"";
                DROP INDEX IF EXISTS public.""IX_TimeAllocation_DailyReflectionId"";
                DROP INDEX IF EXISTS public.""IX_FeedBack_Unit"";
            ");
        }
    }
}
