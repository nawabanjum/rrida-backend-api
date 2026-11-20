using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    // Day 2 / Day 5 onboarding nudges to a new team's manager.
    //
    // Both are time-delayed relative to team creation, so nothing here is
    // triggered by the upsert-team endpoint — a daily background service calls
    // RunAsync and this decides which teams are due.
    public interface ITeamOnboardingNotificationService
    {
        // One pass over every team currently due a nudge. Returns how many were
        // sent, across both days. Never throws for a delivery failure: an
        // unreachable mail provider must not stop the remaining teams.
        Task<int> RunAsync(CancellationToken ct = default);

        // "This team is now yours" — sent immediately when a manager is
        // assigned to a team, from CompanyTeamService.AssignUserToTeamAsync.
        //
        // A team is created with no manager, so this is the first moment there
        // is anybody to email; it is also when the day 2 / day 5 clock starts.
        // Best-effort by contract: the assignment is already committed and must
        // never be undone by a mail failure.
        Task<bool> NotifyManagerAssignedAsync(int managerUserId, uint unitId, CancellationToken ct = default);

        // "You have been unassigned from this team" — sent when a manager is
        // removed from a team, and to the outgoing manager when a team is
        // reassigned to somebody else. Best-effort, same contract as above: the
        // unassignment is already committed.
        Task<bool> NotifyManagerUnassignedAsync(int managerUserId, uint unitId, CancellationToken ct = default);

        // Marks an assignment as "already announced" without sending anything, so
        // neither the in-request hook nor the background catch-up mails about it.
        //
        // For the one case where the manager has genuinely already been told:
        // they were created together with this team, and the invitation email
        // names it. They cannot sign in yet, so an assignment notice would be a
        // second message about an account they have not set up.
        //
        // Deliberately a marker rather than a rule change, so every existing
        // assignment path keeps behaving exactly as it does today. Best-effort:
        // the assignment is already committed and a failure costs one extra email
        // at worst.
        Task<bool> SuppressAssignmentNotificationAsync(int managerUserId, uint unitId, string reason, CancellationToken ct = default);
    }
}
