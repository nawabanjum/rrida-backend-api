using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Components.Enums
{
    /// <summary>
    /// What a user IS, persisted as literal text in <c>Users.Type</c> — a separate
    /// axis from <see cref="UserGroups"/>, which is what a user may DO.
    ///
    /// <see cref="ProfileAdmin"/> marks the profile's own admin account and is the
    /// only member anything reads back: checkCompany finds a profile's admin by it.
    /// It was called "Company" until the two-role change; the stored text was
    /// migrated with it, so the lookup still matches every existing row.
    /// </summary>
    public enum UserType
    {
        ProfileAdmin,
        Unit,
        PatnerUser
    }
}
