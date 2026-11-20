using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Renames the stored value of Users.Type from 'Company' to 'ProfileAdmin',
    /// following the same rename in the UserType enum.
    ///
    /// Users.Type is a separate axis from UserGroupId: the group says what a user
    /// may DO, Type says what the account IS. 'Company' has always meant "this
    /// profile's own admin account" — POST api/profile/Create, POST
    /// api/profile/create-profile, the Entra CreateUser path and the super-admin
    /// seeder all stamp it — so it is now spelled ProfileAdmin to match.
    ///
    /// This migration is what keeps the change behaviour-neutral. The value is
    /// read back by exactly one caller — AccountRegistrationService.checkCompany,
    /// which finds a profile's admin with
    /// <c>u.Type == UserType.ProfileAdmin.ToString()</c> to decide whether that
    /// admin has verified their email. Renaming the enum without moving the rows
    /// would leave every existing profile's admin unfindable and silently change
    /// what checkCompany returns.
    ///
    /// 'Unit' and 'PatnerUser' are untouched — they are different account kinds,
    /// not the profile admin, and nothing about them changed.
    ///
    /// No schema changes — data only, and not scaffolded: the EF model does not
    /// change, so `migrations add` would have produced an empty Up() plus
    /// unrelated drift. Mirrors
    /// Scripts/UserGroups/02_RenameUserTypeCompanyToProfileAdmin.idempotent.sql.
    /// </summary>
    public partial class RenameUserTypeCompanyToProfileAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public."Users" SET "Type" = 'ProfileAdmin' WHERE "Type" = 'Company';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public."Users" SET "Type" = 'Company' WHERE "Type" = 'ProfileAdmin';
                """);
        }
    }
}
