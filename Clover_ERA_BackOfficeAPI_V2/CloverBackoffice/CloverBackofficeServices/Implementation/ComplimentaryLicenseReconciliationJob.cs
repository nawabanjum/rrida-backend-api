using CloverBackoffice.Services.Interfaces;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    // Safety-net for complimentary-license coverage. The event-driven hooks in
    // the manager assign/unassign paths revoke stale grants immediately, but a
    // direct DB edit — or any future manager-mutation path that forgets to call
    // the hook — could leave a grant whose two teams no longer share a manager.
    // This recurring job sweeps every active grant and revokes the stale ones.
    public class ComplimentaryLicenseReconciliationJob
    {
        private readonly ICompanyLicenseService _licenseService;
        private readonly IAuditLogsService _auditLogsService;

        public ComplimentaryLicenseReconciliationJob(
            ICompanyLicenseService licenseService,
            IAuditLogsService auditLogsService)
        {
            _licenseService = licenseService;
            _auditLogsService = auditLogsService;
        }

        public async Task RunAsync()
        {
            const string jobName = "ComplimentaryLicenseReconciliation";
            try
            {
                await _auditLogsService.LogHangfireJobAsync(0, jobName, "Started",
                    $"Sweep at {DateTime.UtcNow:O}");

                // System-driven sweep → null actor.
                var revoked = await _licenseService.ReconcileAllStaleGrantsAsync(null);

                await _auditLogsService.LogHangfireJobAsync(0, jobName, "Finished",
                    $"Revoked {revoked} stale complimentary grant(s).");
            }
            catch (Exception ex)
            {
                await _auditLogsService.LogHangfireJobAsync(0, jobName, "FatalError", ex.Message);
            }
        }
    }
}
