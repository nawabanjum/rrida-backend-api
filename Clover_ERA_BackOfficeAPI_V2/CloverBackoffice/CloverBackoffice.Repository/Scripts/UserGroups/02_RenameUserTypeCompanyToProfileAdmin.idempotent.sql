-- Renames the stored value of Users.Type from 'Company' to 'ProfileAdmin',
-- following the same rename in the UserType enum.
--
-- Mirrors migration 20260908120000_RenameUserTypeCompanyToProfileAdmin.
-- Re-runnable: after the first run no row holds 'Company' any more.
--
-- Users.Type is a separate axis from UserGroupId — the group says what a user may
-- DO, Type says what the account IS. 'Company' has always meant "this profile's
-- own admin account", so it is now spelled ProfileAdmin to match.
--
-- This is what keeps the rename behaviour-neutral: the value is read back by
-- AccountRegistrationService.checkCompany, which finds a profile's admin by it to
-- decide whether that admin has verified their email. Renaming the enum without
-- moving the rows would leave every existing profile's admin unfindable.
--
-- 'Unit' and 'PatnerUser' are untouched.

UPDATE public."Users" SET "Type" = 'ProfileAdmin' WHERE "Type" = 'Company';
