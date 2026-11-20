using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    // The manager side of the licence team limit: a manager licence covers at
    // most LicenseTeamLimits.MaxTeamsPerLicense teams, more when a RRIDA Admin
    // extended the profile or the licence (TeamLimitExtensions). Shared by every path that
    // gives a manager a team (assign-user-to-team, Add User, team setup) and by
    // the usage read, so they all count the same way.
    //
    // Checked only when a NEW team is added to a manager. A manager who already
    // holds more teams keeps all of them.
    public static class ManagerTeamLimitQueries
    {
        // Team Managers of a profile — the users that consume a licence. Same rule
        // as Add User: UserRoles, the legacy UserGroupId only for a user with no
        // UserRoles rows. Invited (not yet accepted) managers count: the licence is
        // taken when the manager is added.
        public static IQueryable<User> TeamManagersOf(this ApplicationDbContext context, int profileId)
        {
            var teamManager = (int)UserGroups.TeamManager;
            return context.Users.Where(u => u.ProfileId == profileId && !u.IsDel
                && (u.Roles.Any(r => r.UserGroupId == teamManager)
                    || (!u.Roles.Any() && u.UserGroupId == teamManager)));
        }

        // Teams the manager holds now: active assignments plus a replacement still
        // waiting for them to accept their invitation. Deleted teams don't count.
        public static IQueryable<uint> ManagerTeamIds(this ApplicationDbContext context, int userId)
            => context.UserTeams
                .Where(ut => ut.UserId == userId && (!ut.IsUnAssigned || ut.IsPendingActivation))
                .Join(context.BusinessUnits.Where(b => b.IsDel != true),
                      ut => ut.UnitId, b => b.Id, (ut, b) => ut.UnitId)
                .Distinct();

        // Teams the manager holds, not counting targetUnitId (re-saving a manager
        // onto a team they already have never needs a free slot).
        public static Task<int> CountManagerTeamsAsync(
            this ApplicationDbContext context, int userId, uint targetUnitId = 0, CancellationToken ct = default)
            => context.ManagerTeamIds(userId).Where(id => id != targetUnitId).CountAsync(ct);

        // Profile team capacity: sum of every live licence's team limit (licences x
        // TeamsPerLicense + licence extensions). Null when one more team fits,
        // otherwise the message to return. Creating a team never consumes a
        // licence; it only has to fit the capacity the licences give.
        public static string? TeamCapacityError(this LicenseUsage usage)
            => usage.TotalTeams >= usage.MaxTeams
                ? string.Format(Components.Helpers.ResponseMessages.Errors.TeamCapacityReached,
                                usage.TotalLicenses, usage.MaxTeams, usage.TeamsPerLicense)
                : null;

        // Null when the manager can take one more team, otherwise the limit-reached
        // message to return.
        public static async Task<string?> ManagerTeamLimitErrorAsync(
            this ApplicationDbContext context, int userId, uint targetUnitId = 0, CancellationToken ct = default)
        {
            var teams = await context.CountManagerTeamsAsync(userId, targetUnitId, ct);
            var limit = await context.ManagerTeamLimitAsync(userId, limits: null, ct);
            return teams >= limit
                ? string.Format(Components.Helpers.ResponseMessages.Errors.ManagerTeamLimitReached,
                                LicenseTeamLimits.FormatUsage(teams, limit), limit)
                : null;
        }

        // ---------- RRIDA Admin team limit extensions (TeamLimitExtensions) ----------

        // The profile's team limits: teams per licence (3 + profile extensions), each
        // licence's own extra teams, and - with a plan catalogue - each licence's full limit
        // from its plan (PlanLicenseLimitsAsync).
        public static async Task<ProfileTeamLimits> GetProfileTeamLimitsAsync(
            this ApplicationDbContext context, uint profileId, CancellationToken ct = default)
        {
            var rows = await context.TeamLimitExtensions.AsNoTracking()
                .Where(e => e.ProfileId == profileId)
                .Select(e => new { e.CompanyLicenseId, e.AdditionalTeams })
                .ToListAsync(ct);

            var profileExtra = rows.Where(r => r.CompanyLicenseId == null).Sum(r => r.AdditionalTeams);
            var licenseExtra = rows.Where(r => r.CompanyLicenseId != null)
                .GroupBy(r => r.CompanyLicenseId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.AdditionalTeams));

            var (planLimits, nonExtendable) = await context.PlanLicenseLimitsAsync(profileId, profileExtra, licenseExtra, ct);

            return new ProfileTeamLimits
            {
                TeamsPerLicense = LicenseTeamLimits.MaxTeamsPerLicense + profileExtra,
                LicenseExtraTeams = licenseExtra,
                PlanLicenseLimits = planLimits,
                NonExtendableLicenseIds = nonExtendable
            };
        }

        // Each (not deleted) licence's team limit from the terms it was bought on (its plan
        // version, PlanTermsQueries): the MANAGERS_AND_TEAMS value (not included -> 0, no number
        // -> Unlimited, no value or the row made inactive -> 3) plus the profile and licence
        // extensions when those terms allow extension. A licence with no plan reads as the legacy
        // plan (Pro) version 1. Empty without a plan catalogue, so the old 3 + extensions rule
        // applies unchanged.
        private static async Task<(Dictionary<int, int> Limits, HashSet<int> NonExtendable)> PlanLicenseLimitsAsync(
            this ApplicationDbContext context, uint profileId, int profileExtra,
            IReadOnlyDictionary<int, int> licenseExtra, CancellationToken ct)
        {
            var limits = new Dictionary<int, int>();
            var nonExtendable = new HashSet<int>();

            var licenceIds = await context.CompanyLicenses.AsNoTracking()
                .Where(l => l.ProfileId == profileId && !l.IsDel)
                .Select(l => l.Id)
                .ToListAsync(ct);
            var terms = await context.GetLicenceTermsAsync(licenceIds, ct);
            if (terms.Count == 0) return (limits, nonExtendable);

            var teamsRowActive = await context.PlanFeatures.AsNoTracking()
                .AnyAsync(f => f.Key == LicenseTeamLimits.TeamsFeatureKey && f.IsActive, ct);

            foreach (var (licenceId, t) in terms)
            {
                var cell = teamsRowActive ? t.Terms.Feature(LicenseTeamLimits.TeamsFeatureKey) : null;
                int limit;
                if (cell == null) limit = LicenseTeamLimits.MaxTeamsPerLicense;
                else if (!cell.IsIncluded) limit = 0;
                else limit = cell.LimitValue ?? LicenseTeamLimits.Unlimited;

                if (!t.Terms.AllowsTeamLimitExtension)
                    nonExtendable.Add(licenceId);
                else if (limit < LicenseTeamLimits.Unlimited)
                    limit += profileExtra + (licenseExtra.TryGetValue(licenceId, out var extra) ? extra : 0);

                limits[licenceId] = limit;
            }

            return (limits, nonExtendable);
        }

        // Teams one licence allows (its plan's limit, or 3 + extensions without a catalogue).
        public static async Task<int> LicenseTeamLimitAsync(
            this ApplicationDbContext context, int companyLicenseId, CancellationToken ct = default)
        {
            var profileId = await context.CompanyLicenses.AsNoTracking()
                .Where(l => l.Id == companyLicenseId)
                .Select(l => (uint?)l.ProfileId)
                .FirstOrDefaultAsync(ct);
            if (profileId == null) return LicenseTeamLimits.MaxTeamsPerLicense;

            return (await context.GetProfileTeamLimitsAsync(profileId.Value, ct)).LimitOf(companyLicenseId);
        }

        // Teams a manager may hold: the limit of the live licence they hold or their teams
        // ride on (the largest, if several); a manager with no live licence keeps the
        // profile's teams per licence. limits: pass the profile's limits when already
        // loaded (bulk reads), else they are read here.
        public static async Task<int> ManagerTeamLimitAsync(
            this ApplicationDbContext context, int userId, ProfileTeamLimits? limits, CancellationToken ct = default)
        {
            if (limits == null)
            {
                var profileId = await context.Users.AsNoTracking()
                    .Where(u => u.UserId == userId)
                    .Select(u => u.ProfileId)
                    .FirstOrDefaultAsync(ct);
                if (profileId is null or <= 0) return LicenseTeamLimits.MaxTeamsPerLicense;
                limits = await context.GetProfileTeamLimitsAsync((uint)profileId.Value, ct);
            }

            // No plan limits and no licence-level extension: every licence allows the same.
            if (limits.LicenseExtraTeams.Count == 0 && limits.PlanLicenseLimits.Count == 0)
                return limits.TeamsPerLicense;

            var now = DateTime.UtcNow;
            var teamIds = context.ManagerTeamIds(userId);
            var licenseIds = await context.CompanyLicenses.AsNoTracking()
                .Where(l => !l.IsDel
                            && (l.ExpiryDate == null || l.ExpiryDate > now)
                            && (l.AssignedUserId == userId
                                || context.AssignedCompanyLicenses.Any(a =>
                                       a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel && teamIds.Contains(a.BusinessUnitId))
                                || context.ComplimentaryLicenseGrants.Any(g =>
                                       g.CompanyLicenseId == l.Id && g.IsActive && !g.IsDel && teamIds.Contains(g.ComplimentaryBusinessUnitId))))
                .Select(l => l.Id)
                .ToListAsync(ct);

            return licenseIds.Count == 0
                ? limits.TeamsPerLicense
                : licenseIds.Max(id => limits.LimitOf(id));
        }
    }
}
