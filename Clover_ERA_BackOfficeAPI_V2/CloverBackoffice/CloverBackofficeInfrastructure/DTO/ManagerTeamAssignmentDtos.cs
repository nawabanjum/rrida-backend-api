using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Manager onboarding — API contracts.
    //
    // Creating a Team Manager who must have a team is a different operation from
    // the general user upsert, and it lives on its own endpoints
    // (api/manager-onboarding/*) rather than as a mode of upsert-user. The user
    // list, the partner screens and the mobile registration flows all share
    // upsert-user; making a team mandatory there would have changed behaviour for
    // every one of them.
    //
    // Two things decide where a manager lands, and they are decided differently:
    //
    //   * the COMPANY is never chosen by the caller. It is read off the manager's
    //     email domain — "abc" for manager@abc.com, manager@abc.co.uk and
    //     manager@abc.co.us alike — and created only when that organisation has
    //     never been onboarded;
    //   * the TEAM is always chosen by the caller, and choosing one is mandatory.
    //     An existing team of that company, or a new team created in the same
    //     request. A manager is never created without one.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Body of <c>POST api/manager-onboarding/create-manager</c>.
    ///
    /// The role is not a field. This endpoint creates Team Managers and nothing
    /// else, so it sets the group itself — a caller cannot use the mandatory-team
    /// path to create an admin, and cannot create a manager without a team by
    /// choosing a different role.
    ///
    /// Neither is the profile. It comes from <see cref="Email"/>, and it must
    /// already exist: this endpoint attaches a manager and a team to a registered
    /// profile and never creates one. An unrecognised domain is refused. A
    /// <c>profileId</c> in the body is ignored rather than rejected — the field is
    /// gone and unknown JSON properties are dropped — so a client still sending
    /// one keeps working and simply gets the profile its manager's domain names.
    ///
    /// The team is chosen by <see cref="IsNewTeam"/> plus, for an existing team,
    /// <see cref="ExistingUnitId"/>, resolved INSIDE the profile the email domain
    /// names:
    ///
    ///     existing team   isNewTeam = false, existingUnitId = &lt;team id&gt;
    ///     new team        isNewTeam = true,  no existingUnitId
    ///
    /// <see cref="IsNewTeam"/> is the ONLY flag that decides which of the two this
    /// is. There is no second way to say it and nothing else to send: the body is
    /// the manager's details plus those two fields, and that is the whole contract.
    /// </summary>
    public class CreateManagerWithTeamRequestDto
    {
        // Optional: the plan of the licence the new Team Manager is given ("light" / "pro").
        // Null = the only free plan; with Light and Pro both free, none (assign it later).
        public string? PlanCode { get; set; }

        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        /// <summary>
        /// True to create a team for this manager as part of the same request,
        /// under the profile their email domain names; false to put them on the
        /// team named by <see cref="ExistingUnitId"/>.
        ///
        /// A new team is always top-level and is created with a name and nothing
        /// else — the name is generated from the manager's own name, and Size,
        /// type, context and goals are NOT collected here. The Company Admin
        /// completes those separately through team setup, which is exactly where
        /// the client goes next when the landing call reports isNewTeam true. The
        /// profile itself is never created here.
        /// </summary>
        public bool IsNewTeam { get; set; }

        /// <summary>
        /// Assign the new manager to this existing team. REQUIRED when
        /// <see cref="IsNewTeam"/> is false, and ignored when it is true.
        ///
        /// It must be a live team of the profile the manager's email domain
        /// resolves to — call <c>resolve-organization</c> with the address to list
        /// those.
        ///
        /// A team has ONE manager. If the team already has one, the new manager
        /// REPLACES them: the incumbent is unassigned and sent the standard "you
        /// have been unassigned" message. A manager may hold several teams, but a
        /// team never has more than one.
        /// </summary>
        public long? ExistingUnitId { get; set; }

        /// <summary>Taken from the caller's token, never from the request body.</summary>
        [JsonIgnore]
        public int CreatedBy { get; set; }
    }

    /// <summary>
    /// What <c>create-manager</c> returns on success: the manager, the company and
    /// team they are on, and what happened along the way.
    /// </summary>
    public class CreateManagerWithTeamResponseDto
    {
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        /// <summary>The company resolved from the email domain — created by this request or already there.</summary>
        public int ProfileId { get; set; }

        /// <summary>Always the Team Manager group; echoed so the client can display the role.</summary>
        public int UserGroupId { get; set; }

        /// <summary>
        /// The company's display name. Needed for the invitation email template
        /// and nothing else, so it is not serialised back to the client.
        /// </summary>
        [JsonIgnore]
        public string? CompanyName { get; set; }

        public long UnitId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        /// <summary>
        /// The organisation read out of the manager's email address ("abc"), and
        /// the key the company was matched on. Echoed so a caller can see why this
        /// company was the one chosen.
        /// </summary>
        public string OrganizationKey { get; set; } = string.Empty;

        /// <summary>True when this request created the team, false when it assigned an existing one.</summary>
        public bool TeamWasCreated { get; set; }

        /// <summary>
        /// The same thing as <see cref="TeamWasCreated"/>, under the name the
        /// request uses, so the client reads back exactly what it sent.
        /// </summary>
        public bool IsNewTeam { get; set; }

        /// <summary>
        /// The existing team this manager was put on, echoed back. Null when the
        /// team was created by this request — in which case <see cref="UnitId"/> is
        /// the id of the team that was just created.
        /// </summary>
        public long? ExistingUnitId { get; set; }

        /// <summary>
        /// The manager who held this team before and has just been replaced, when
        /// there was one. They are unassigned and sent the standard "you have been
        /// unassigned" message, so they know they have stopped receiving the
        /// team’s reports and alerts.
        ///
        /// Null when the team had no manager, or when the team was created here.
        /// </summary>
        public int? ReplacedManagerUserId { get; set; }

        /// <summary>
        /// Whether the invitation actually went out. False means the manager and
        /// the team are saved but the mail provider refused — the admin should
        /// re-send from the user list rather than create the manager again.
        /// </summary>
        public bool InvitationSent { get; set; }

        /// <summary>
        /// True when the company now has exactly 2 users — the Company Admin
        /// created at registration plus this one. Computed the same way
        /// <c>upsert-user</c> and <c>get-users</c> compute it, so the "Pending
        /// Account Verification" popup behaves identically however the user was
        /// created.
        /// </summary>
        public bool FirstTimeUserAdded { get; set; }
    }

    /// <summary>
    /// Outcome of the service call behind <c>create-manager</c>.
    ///
    /// A failure carries the reason and no data, and means nothing at all was
    /// written: no company, no team, no user row, no assignment, and no invitation
    /// email.
    /// </summary>
    public class CreateManagerWithTeamResultDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public CreateManagerWithTeamResponseDto? Data { get; set; }
    }

    /// <summary>
    /// Body of <c>POST api/manager-onboarding/resolve-organization</c> — the
    /// address the admin has typed for the manager they are about to create.
    /// </summary>
    public class ResolveManagerOrganizationRequestDto
    {
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>
    /// What the manager-creation screen needs before it can ask the admin to pick
    /// a team.
    ///
    /// The company is decided by the manager's email domain, so the teams that may
    /// be chosen are that company's teams — not the caller's. Until the address is
    /// typed the screen does not know which company it is filling in for, which is
    /// what this answers.
    ///
    /// <see cref="CompanyExists"/> false means the domain has no registered
    /// profile. No manager can be onboarded for it: this flow no longer creates a
    /// profile, so the screen should send the admin to profile registration rather
    /// than offering to create the manager with a new team.
    /// </summary>
    public class ResolveManagerOrganizationDto
    {
        /// <summary>The organisation read out of the address ("abc").</summary>
        public string OrganizationKey { get; set; } = string.Empty;

        /// <summary>Whether that organisation already has a company.</summary>
        public bool CompanyExists { get; set; }

        /// <summary>The company's id when it exists, null when it would be created.</summary>
        public int? ProfileId { get; set; }

        /// <summary>
        /// The company's name when it exists; otherwise the name it would be
        /// created with ("Abc").
        /// </summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the caller may choose from this company's existing teams.
        ///
        /// False when the company belongs to a tenant the caller is not part of:
        /// its team names and ids are withheld, and the manager must be created
        /// with a new team. Distinguishes "I cannot see the teams" from "there
        /// are no teams", which the client would otherwise have to guess at.
        /// </summary>
        public bool CanSelectTeams { get; set; }

        /// <summary>
        /// The company's live teams, oldest first. Empty when the company does not
        /// exist yet or has no teams — in both cases the admin must create one.
        /// </summary>
        public List<ManagerOnboardingTeamOptionDto> Teams { get; set; } = new();
    }

    /// <summary>One selectable team on the manager-creation screen.</summary>
    public class ManagerOnboardingTeamOptionDto
    {
        public long UnitId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the team already has a manager. Picking it is a TAKEOVER —
        /// that manager loses the team — so warn before letting the admin choose it.
        /// </summary>
        public bool HasManager { get; set; }
    }

    /// <summary>
    /// Body of <c>POST api/manager-onboarding/post-password-landing</c> — the same
    /// encrypted parameter the invitation link carried and the password reset
    /// used, so no second token is minted.
    /// </summary>
    public class PostPasswordLandingRequestDto
    {
        public string Param { get; set; } = string.Empty;

        /// <summary>
        /// The password the manager has just chosen. Supply it and this endpoint
        /// SETS it before answering, so accepting an invitation is one call:
        /// param + password in, the manager's landing out.
        ///
        /// Optional. Left null, the endpoint only reports the landing and assumes
        /// the password was already set through <c>api/profile/reset-password</c> —
        /// which is what a client written before this field did, and still works.
        ///
        /// The password goes through exactly the same service as reset-password, so
        /// invitation expiry, the single-use link and session invalidation all
        /// behave identically however it is called.
        /// </summary>
        public string? NewPassword { get; set; }
    }

    /// <summary>
    /// Where a user goes immediately after setting their password from an
    /// invitation link.
    ///
    /// A Team Manager always has a team from the moment their account is created,
    /// so they go straight to that team's Team Health page and never through a
    /// team setup step. Anyone else gets the default landing.
    /// </summary>
    public class PostPasswordLandingDto
    {
        public int UserId { get; set; }

        /// <summary>Every role the user holds (UserGroups ids).</summary>
        public List<int> RoleIds { get; set; } = new();

        /// <summary>True when Team Manager is among the user's roles.</summary>
        public bool IsTeamManager { get; set; }

        /// <summary>The team's id, or null when the user is not the manager of one.</summary>
        public long? UnitId { get; set; }

        public string? TeamName { get; set; }

        /// <summary>
        /// True when this team was created during the manager's own onboarding,
        /// false when they were put on a team that already existed. Read from the
        /// assignment, which records it at creation time — see
        /// <c>UserTeam.IsNewTeam</c>.
        ///
        /// Null when the user is not the manager of a team.
        /// </summary>
        public bool? IsNewTeam { get; set; }

        /// <summary>
        /// The id of the pre-existing team the manager was put on. Null when the
        /// team was created for them, in which case <see cref="UnitId"/> is that
        /// newly created team.
        /// </summary>
        public long? ExistingUnitId { get; set; }
    }
}
