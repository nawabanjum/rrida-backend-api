namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// One role in the response of GET api/profile/role-hierarchy, which returns
    /// the caller's own role plus every role beneath it, highest first.
    /// </summary>
    public class RoleHierarchyDto
    {
        /// <summary>
        /// The UserGroups primary key, and the value api/profile/upsert-user takes
        /// as <c>userGroupId</c>.
        ///
        /// It says NOTHING about rank — 1 Profile Admin outranks 2 Team Manager,
        /// and 3 RRIDA Admin outranks both. Order by <see cref="Level"/>, never by
        /// this. (The ids predate the current role model; two of them were already
        /// held by live accounts when the other two were added.)
        /// </summary>
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>What the role may do, as stored on the UserGroups row.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// Absolute rank in the whole hierarchy: 1 RRIDA Admin, 2 Profile Admin,
        /// 3 Team Manager, 4 Portfolio Manager.
        ///
        /// ABSOLUTE, not relative to the caller — a Team Manager's own role reads
        /// 3, not 1. That is what lets a client show the same badge for a role
        /// whoever is looking at it. The list is already returned in this order, so
        /// re-sorting by it is optional.
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Whether this role may be given to a user through
        /// api/profile/upsert-user.
        ///
        /// False for RRIDA Admin only: it is platform staff, minted solely by
        /// DatabaseSeeder, and upsert-user rejects it outright. A RRIDA Admin
        /// therefore SEES their own role here — the hierarchy is what this endpoint
        /// answers — but a role picker built on this list must not offer it, or the
        /// create call behind it fails with "Invalid user type".
        /// </summary>
        public bool IsAssignable { get; set; }

        /// <summary>True for the caller's own role — the first entry in the list.</summary>
        public bool IsOwnRole { get; set; }
    }
}
