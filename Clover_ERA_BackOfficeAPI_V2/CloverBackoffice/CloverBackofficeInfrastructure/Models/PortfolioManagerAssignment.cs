using System;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// A Team Manager placed under a Portfolio Manager.
    ///
    /// The Portfolio Manager counterpart of <see cref="UserTeam"/>, and shaped the
    /// same way on purpose: a manager has ONE Portfolio Manager at a time, a
    /// Portfolio Manager may hold many managers, and a replaced row is flipped to
    /// <see cref="IsUnAssigned"/> and kept rather than deleted, so the record of who
    /// reported to whom survives. The database enforces the one-at-a-time rule with
    /// a partial unique index on <see cref="ManagerUserId"/> WHERE NOT IsUnAssigned.
    ///
    /// Both users belong to <see cref="ProfileId"/>. It is stored rather than joined
    /// for, so a company's portfolio can be listed with one indexed filter and a row
    /// can never silently straddle two tenants.
    /// </summary>
    public class PortfolioManagerAssignment
    {
        [Key]
        public int Id { get; set; }

        /// <summary>The user holding the Portfolio Manager role.</summary>
        public int PortfolioManagerUserId { get; set; }

        /// <summary>The Team Manager placed under them.</summary>
        public int ManagerUserId { get; set; }

        /// <summary>bigint, matching Profiles.Id.</summary>
        public uint ProfileId { get; set; }

        public bool IsUnAssigned { get; set; } = false;

        public int CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; }

        /// <summary>When the row was flipped to unassigned. Null while it is live.</summary>
        public DateTime? UnAssignedDate { get; set; }
    }
}
