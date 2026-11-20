using System;
using System.Collections.Generic;
using System.Linq;

namespace CloverBackoffice.Components.Enums
{
    /// <summary>
    /// Rank of the four roles, and the rule that turns a user's SET of roles into
    /// the single role still stored on <c>Users.UserGroupId</c>.
    ///
    /// The ids cannot supply rank (1 Profile Admin outranks 2 Team Manager, and 3
    /// RRIDA Admin outranks both) and the UserGroups table stores none, so this array
    /// is the definition. A role added later belongs in it at its rank, not appended.
    /// </summary>
    public static class RoleHierarchy
    {
        /// <summary>
        /// <c>Users.UserGroupId</c> of a user who holds no role at all — e.g. a Profile
        /// Admin whose only role was taken off. The column is NOT NULL with a foreign
        /// key, so it points at the "No Role" row of UserGroups (Id 0, script 09).
        /// Not a role: it is in neither array, ranks -1, and every "is this user a
        /// Profile Admin / Team Manager" check therefore answers no.
        /// </summary>
        public const int NoRoleGroupId = 0;

        public static readonly UserGroups[] HighestFirst =
        {
            UserGroups.RridaAdmin,
            UserGroups.ProfileAdmin,
            UserGroups.TeamManager,
            UserGroups.PortfolioManager
        };

        /// <summary>The three roles a Profile Admin may hand out through Add User.</summary>
        public static readonly UserGroups[] Assignable =
        {
            UserGroups.ProfileAdmin,
            UserGroups.TeamManager,
            UserGroups.PortfolioManager
        };

        /// <summary>0 for the highest role; -1 for an id that is not one of the four.</summary>
        public static int RankOf(int userGroupId) =>
            Array.IndexOf(HighestFirst, (UserGroups)userGroupId);

        public static bool IsAssignable(int userGroupId) =>
            Assignable.Contains((UserGroups)userGroupId);

        /// <summary>
        /// A non Team Manager who may be made one by taking a Team Manager licence
        /// (licence assign by managerId, Team Structure drop): a Portfolio Manager, a
        /// Profile Admin (e.g. one whose Team Manager role was removed), or a user
        /// with no role at all ("No Role", roles = []). The role is added next to the
        /// ones they hold. A RRIDA Admin is not.
        /// </summary>
        public static bool CanBecomeTeamManager(IEnumerable<int> userGroupIds)
        {
            var held = userGroupIds.Where(id => RankOf(id) >= 0).ToList();
            return held.Count == 0
                || held.Any(id => id == (int)UserGroups.PortfolioManager || id == (int)UserGroups.ProfileAdmin);
        }

        /// <summary>
        /// The highest-ranked of a user's roles — the value <c>Users.UserGroupId</c>
        /// carries while that column still exists. Every existing check on the
        /// column is either "is RRIDA Admin", "is at least Profile Admin" or a
        /// per-role switch, and "highest wins" keeps all of them granting what the
        /// user's strongest role grants. Null when none of the ids is a known role.
        /// </summary>
        public static int? PrimaryOf(IEnumerable<int> userGroupIds)
        {
            var best = userGroupIds
                .Select(id => (Id: id, Rank: RankOf(id)))
                .Where(r => r.Rank >= 0)
                .OrderBy(r => r.Rank)
                .Select(r => (int?)r.Id)
                .FirstOrDefault();

            return best;
        }
    }
}
