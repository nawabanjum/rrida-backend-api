using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Components.Enums
{
    /// <summary>
    /// The product's four roles. The values are the primary keys of the
    /// <c>UserGroups</c> table, which holds exactly these four rows.
    ///
    /// Hierarchy, highest first:
    ///   RridaAdmin        platform super admin - spans every profile
    ///   ProfileAdmin      administers one profile
    ///   TeamManager       runs the teams assigned to them, inside one profile
    ///   PortfolioManager  additional role a Profile Admin may hand out
    ///
    /// The ids are NOT in hierarchy order, deliberately. ProfileAdmin 1 and
    /// TeamManager 2 were already assigned and are held by live accounts, so the
    /// two roles added afterwards took the next free numbers rather than forcing a
    /// remap of every user. Read the hierarchy from this comment, never from the
    /// numbers.
    ///
    /// <see cref="RridaAdmin"/> is the only role that is not profile-scoped, and it
    /// replaces the Clover ERA Admin of the original six-role model. It cannot be
    /// created through the API - <c>api/profile/upsert-user</c> rejects it and it is
    /// hidden from the role picker. DatabaseSeeder is the only thing that mints one.
    /// </summary>
    public enum UserGroups
    {
        /// <summary>Administers one profile, and the managers inside it.</summary>
        ProfileAdmin = 1,

        /// <summary>Runs the teams assigned to them, inside one profile.</summary>
        TeamManager = 2,

        /// <summary>Platform super admin. Sees and manages every profile.</summary>
        RridaAdmin = 3,

        /// <summary>
        /// Additional role a Profile Admin may assign. Carries NO data access yet -
        /// every scoped query returns empty for it - pending its permission spec.
        /// </summary>
        PortfolioManager = 4
    }
}
