using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class UserTeam
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public uint UnitId { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsUnAssigned { get; set; } = false;
        public bool? IsReadOnly { get; set; } = false;

        /// <summary>
        /// True when the team was created by the same request that made this
        /// assignment - i.e. the manager was onboarded with a NEW team rather than
        /// being put on an existing one.
        ///
        /// It has to be stored: create-manager knows it, but post-password-landing
        /// runs days later off nothing but the invitation link, and no timestamp
        /// comparison between the unit and the assignment is trustworthy enough to
        /// infer it. False for every row written before this column existed, and
        /// for every assignment made anywhere other than manager onboarding.
        /// </summary>
        public bool IsNewTeam { get; set; } = false;

        /// <summary>
        /// True while this row is a manager replacement WAITING for the invited
        /// manager to accept their invitation. Such a row is always saved with
        /// IsUnAssigned = true, so every reader that looks for the team's active
        /// manager keeps seeing the current (outgoing) manager until the
        /// replacement is finalized. Accepting the invitation flips it to an
        /// active row and unassigns the outgoing manager; any later assignment
        /// on the same team cancels it (flag cleared, row stays unassigned).
        /// </summary>
        public bool IsPendingActivation { get; set; } = false;

        /// <summary>
        /// When the manager acknowledged this team assignment (the "you have been
        /// assigned a new team" popup). Null = not seen yet. Every row that existed
        /// before this column was backfilled as seen; every new row starts null.
        /// A manager's first login marks all their rows seen, since onboarding
        /// covers those teams - so only teams added later ever show the popup.
        /// </summary>
        public DateTime? AssignmentSeenAt { get; set; }

    }
}
