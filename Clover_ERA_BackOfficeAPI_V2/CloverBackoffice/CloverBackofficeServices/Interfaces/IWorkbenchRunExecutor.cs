using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Services.Implementation;

namespace CloverBackoffice.Services.Interfaces
{
    // Executes ONE workbench run against an existing WorkbenchRunStatus row:
    // Running → engine → ResultJson → Completed / Failed.
    //
    // This is the body the workbench queue consumer used to own inline. It was
    // lifted out so the report scheduler can raise a run through exactly the
    // same code instead of calling the engine on its own terms — the run row,
    // the ResultJson wrapper and the detailed logs then come out identical
    // whichever trigger started it.
    //
    // The queue consumer (CloverEraBackgroundService) still calls it from the
    // channel loop; the scheduler calls it inline and awaits the result.
    public interface IWorkbenchRunExecutor
    {
        // True when the run finished as "Completed". The caller reads the run
        // row for the payload — nothing about the contract is in-memory only.
        Task<bool> ExecuteAsync(CloverEraWorkItem workItem, CancellationToken ct = default);
    }
}
