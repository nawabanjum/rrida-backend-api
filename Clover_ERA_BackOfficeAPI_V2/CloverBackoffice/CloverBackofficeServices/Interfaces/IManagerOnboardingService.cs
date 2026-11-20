using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    // Creating a Team Manager who must have a team, and working out where that
    // manager lands after accepting the invitation.
    //
    // Deliberately separate from IAccountRegistrationService. The general user
    // upsert is shared by the user list, the partner screens and the mobile
    // registration flows; making a team mandatory inside it would have changed
    // behaviour for all of them. This service composes the existing pieces —
    // user creation, team creation, the manager-to-team link — without altering
    // any of them.
    //
    // The company a manager joins is NOT a parameter anywhere here: it is read
    // off their email domain. The team always is, and is always required.
    public interface IManagerOnboardingService
    {
        // Creates the manager and settles their team as one unit of work. Either
        // the whole thing lands — company, team, user, assignment — or nothing
        // does.
        //
        // Does NOT send the invitation. The caller sends it once this has
        // committed, so a mail failure cannot roll back a saved manager and a
        // failed creation cannot leave an invitation already in the inbox.
        Task<CreateManagerWithTeamResultDto> CreateManagerWithTeamAsync(
            CreateManagerWithTeamRequestDto dto, CancellationToken ct = default);

        // Which company an address puts a manager in, and which teams could
        // therefore be chosen for them. Read-only, and creates nothing. Returns
        // null when the address names no organisation.
        //
        // The caller is passed in so the answer can be scoped: somebody outside
        // the resolved company is told the organisation exists and what it is
        // called, but not its id or its teams.
        Task<ResolveManagerOrganizationDto?> ResolveOrganizationAsync(
            string email, User? caller, CancellationToken ct = default);

        // Where the holder of an invitation link goes once their password is set.
        // A Team Manager with a team goes to that team's Team Health page;
        // anybody else goes to sign-in. Returns null when the parameter cannot be
        // read.
        Task<PostPasswordLandingDto?> GetPostPasswordLandingAsync(
            string encryptedParam, CancellationToken ct = default);
    }
}
