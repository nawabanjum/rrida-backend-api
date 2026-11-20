using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CloverBackoffice.Components.Helpers
{
    public static class ResponseMessages
    {

        public static class Errors
        {
            public static string ExceptionMsg = "An error occurred processing your request.";
            public static string Failedemail = "Failed to send email.";

            public static string InvalidLink = "The reset link is invalid or has been altered.";
            public static string PasswordUpdateFailed = "Failed to update password.";

            public static string UserNotFound = "User not found.";
            public const string UserIdRequired = "userId is required and must be a positive number.";
            public const string UserTeamsForbidden = "You are not allowed to view this user's teams.";
            public static string IncorrectOldPassword = "Old password is incorrect.";
            public static string PasswordReuse = "Your new password must be different from your previous password.";
            public static string CompanyExists = "This company has already been registered.";
            public static string InvalidCompany = "Company detail invalid.";

            public static string InvalidData = "Please provide valid data.";
            public static string GroupConfigFailed = "User group configuration failed.";

            public static string UserNotFoundFailed = "User not found, operation failed.";
            public const string CompanyCreationFailedMessage = "An error occurred while creating company and related entities.";

            public const string UnitCreationFailedMessage = "An error occurred while creating unit and related entities.";
            public const string EmailPasswordRequired = "Email and password are required.";
            public const string RecipientEmailRequired = "At least one recipient email is required.";
            public const string CalendarInviteTeamRequired = "unitId or profileId is required: daily questions are set per team.";
            public const string TeamCalendarInviteForbidden = "Only a Team Manager of this team can request its calendar invitation.";
            public const string TeamCalendarInviteEmailInvalid = "Your account does not have a valid email address, so the calendar invitation could not be sent.";
            public const string TeamCalendarInviteNotGenerated = "The calendar invitation could not be generated for this team.";
            public const string TeamCalendarInviteNotDelivered = "The calendar invitation could not be delivered. Please try again.";
            public const string CalendarReminderMinutesInvalid = "reminderMinutes must be between 0 and 10080 (7 days).";
            public const string UserNotRegistered = "User has not been registered.";
            public const string InvalidCredentials = "Invalid email address or password.";
            public const string AccountLocked = "Your account has been locked due to multiple failed login attempts. Please wait before trying again.";
            public const string InvalidEmail = "Email is not valid.";
            public const string ComapnyNameUnique = "Company name must be unique.";
            public const string BussinessNameUnique = "Bussiness name must be unique.";
            public const string ContactEmailUnique = "Contact email must be unique.";
            public const string TeamNameUnique = "Team name must be unique.";
            public const string TeamSizeRequired = "Team size is required.";
            public const string TeamSizeInvalid = "Team size must be greater than zero.";
            // ── New manager, mandatory team ──────────────────────────────────
            // A Team Manager is never created without a team. The COMPANY is not
            // chosen by the admin — it is read off the manager's email domain — but
            // the TEAM is, and choosing one is required: an existing team of that
            // company, or a new one created in the same request.
            public const string ManagerEmailDomainUnresolved = "That email address does not identify an organisation. Use the manager's work email address.";
            // Manager onboarding no longer creates a profile for an unrecognised
            // email domain; it only ever attaches a manager and a team to one that
            // already exists. An unknown domain is therefore a refusal, and the
            // profile has to be registered through profile registration first.
            public const string ManagerCompanyNotFound = "No profile is registered for this email domain. Register the profile first, then add its managers.";
            // isNewTeam false and no existingUnitId. There is no "ambiguous" case:
            // isNewTeam decides on its own, so sending both is a new team, not an error.
            public const string ManagerTeamRequired = "A team is required. Select an existing team for this manager, or create a new one.";
            public const string ManagerTeamNotFound = "The selected team does not exist or does not belong to this company.";
            public const string ManagerTeamCreateFailed = "The manager's team could not be created. No user was created.";
            public const string ManagerCreateFailed = "The manager could not be created with a team. Nothing has been saved.";

            // Only admin roles may bring a manager — and, on an unseen domain, a
            // whole company — into existence. A Team Manager or Customer holding a
            // valid token must not be able to.
            public const string ManagerCreateNotPermitted = "You do not have permission to create managers.";

            // api/profile/upsert-user. Adding a user is a Profile Admin action;
            // a Team Manager or Portfolio Manager holding a valid token is not
            // enough. Separate from ManagerCreateNotPermitted so the two endpoints
            // can be told apart in a log or a support ticket.
            public const string UserCreateNotPermitted = "You do not have permission to add users to this profile.";

            // Required-name validation for the registration endpoints. Checked
            // before any uniqueness / domain lookup so a blank name fails fast
            // with a field-specific message instead of a generic one.
            public const string CompanyNameRequired = "Company name is required.";
            public const string ProfileNameRequired = "Profile name is required.";
            public const string UnitNameRequired = "Unit name is required.";
            public const string UserNameRequired = "User name is required.";

            public const string CompanyNotFound = "Company not found.";
            public const string EmailAlreadyExist = "Email already exists and is linked to a company.";
            public const string UserNotFoundOrInvalid = "User not found or username invalid.";
            public const string InvalidRequest = "Invalid request.";
            public const string InvalidAction = "Invalid Action Performed.";
            public const string NoRecords = "No records found.";

            public const string PermissionMatrixConfigFailed = "Permission Matrix configuration failed";
            public const string NotCreatedUser = "User not created.";
            public const string InvalidUser = "Invalid user data.";
            public const string InvalidUserGroup = "Invalid user type. Allowed values are 1 (Profile Admin), 2 (Team Manager) and 4 (Portfolio Manager).";
            public const string UnitsNotFound = "No units found for the provided company ID.";
            public const string InvalidDays = "Days parameter must be greater than 0.";
            public const string InvalidUnit = "Invalid unit ID.";
            public const string DashboardSummaryNotFound = "Dashboard summary not found.";
            public const string BusinessUnitNotFound = "Business unit not found.";
            public const string UnitNotLicensedForReport = "The selected unit do not have an active license. A report cannot be generated for an unlicensed unit.";
            public const string TopInsightsNotFound = "No top insights found for the provided unit.";
            public const string UserUpsertFail = "User upsert failed.";
            public const string UserDeleteFail = "User deletion failed or user not found.";
            public const string InValidStatus = "Invalid status value. Use Active / Inactive, or 1 (Active), 2 (Pending), 3 (Suspend).";
            public const string AccountInactive = "Your account is inactive. Please contact your Profile Admin.";
            public const string StatusChangeNotAllowed = "Only a Profile Admin can change a user's status.";
            public const string InvalidReactivationMethod = "Invalid reactivation method. Use ManualCredentials or Invite, and only when making a user Active.";

            // api/profile/resend-invitation and api/profile/invitation-link.
            public const string InvitationNotAllowed = "Only a Profile Admin can resend an invitation or copy an invite link.";
            public const string InvitationUserNotPending = "This user has already accepted their invitation. Invitations can only be resent to pending users.";
            public const string InvitationUserInactive = "This user is inactive. Make them active to send them an invitation.";
            public const string InvitationEmailFailed = "The invitation email could not be sent. Copy the invite link and share it with the user, or try again.";

            // api/profile/unlock-user.
            public const string UnlockNotAllowed = "Only a Profile Admin or RRIDA Admin can unlock a user account.";
            public const string UnlockOtherProfileNotAllowed = "You can only unlock users in your own profile.";
            public const string UserNotLocked = "This user's account is not locked.";

            // api/profile/activate-user.
            public const string ActivateNotAllowed = "Only a Profile Admin or RRIDA Admin can activate a user account.";
            public const string ActivateOtherProfileNotAllowed = "You can only activate users in your own profile.";
            public const string UserAlreadyActive = "This user is already active. Only invited (pending) or inactive users can be activated.";

            // PasswordPolicy (same rules as the RRIDA password screens).
            public const string PasswordRequired = "Temporary password is required.";
            public const string PasswordTooShort = "Password must be at least 10 characters.";
            public const string PasswordTooLong = "Password cannot be longer than 72 characters.";
            public const string PasswordHasSpaces = "Password cannot contain spaces.";
            public const string PasswordNeedsUpperAndLower = "Password must contain an upper and a lower case letter.";
            public const string PasswordNeedsNumberOrSymbol = "Password must contain a number or a symbol.";
            public const string Invalidtoken = "Invalid or missing userId in token.";
            public const string TeamNotFound = "Businessteam not found.";
            public const string UserGroupNotFound = "No user groups found.";
            public const string InvalidUserTeam = "Invalid UserId or TeamId.";
            public const string UserAssignteamFail = "Failed to assign user to team. User or team might not exist.";
            public const string InvalidProfileId = "Invalid ProfileId. Profile does not exist.";
            public const string RridaAdminProfileIdRequired = "profileId is required: select the profile the user is being added to.";
            public const string InvalidUnitId = "Invalid UnitId. Unit does not exist for the provided Company.";
            public const string InvalidCompanyUnit = "Company or Unit not found.";
            public const string ActionEventNotFound = "Action Event not found.";
            public const string ActionHubNotFound = "Action Hub not found.";
            public const string NameRequired = "Plz Enter Name.";
            public const string NameFail = "Name is valid.";
            public const string EmailFail = "Provided email is Valid";
            public const string EmailReq = "Email address is required.";
            public const string TokenExpireAlready = "User token not found or already expired.";
            public const string OperationFail = "Operation not performed";
            public const string NoReportsFound = "No reports found for this UnitId";
            public const string InvalidReportData = "Invalid unit ID or Id";
            public const string ResultNotFound = "Results not found";
            public const string NoInsightsFound = "No insights found for this business unit";
            public const string BatchJobFailure = "Batch job cancellation - implementation pending";
            public const string AuthorizeFailReports = "Not authorized to perform this action.";
            public const string ProfileAdminOnly = "This action is only for the RRIDA Admin";
            public const string FileUploadError = "No file uploaded";
            public const string BatchJobFail = "No batch jobs found.";
            public const string BatchFail = "BatchJob not created.";
            public const string MissingFileName = "Filename is required.";
            public const string FileNotFound = "Requested worksheet file not found.";
            public const string UnexpectedError = "An unexpected error occurred while processing your request";
            public const string InvalidToken = "Invalid token - UserId missing.";
            public const string InvalidOpp = "Invalid opportunity data.";
            public const string Invalidcomp = "Invalid or non-existent company.";
            public const string Invalidpart = "Invalid or non-existent partner.";
            public const string oppfail = "Opportunity not found or update failed";
            public const string OperationFailed = "Invalid or non-existent opportunity";
            public const string Opefailed = "No opportunities found for this company";
            public const string LinkAlreadyUsed = "Already Used Linked";
            public const string InvitationLinkSuperseded = "This invitation link is no longer valid because a newer invitation has been sent. Please use the link from the most recent invitation email.";
            public const string DomainAlreadyUsed = "This organization is either registered by another partner or a current customer of Clover ERA. Please contact Clover ERA if you would like to discuss further.";
            public const string ProfileAdminAccessDenied = "Access denied. Only a RRIDA Admin can access this dashboard.";
            public const string NoReportFoundId = "Report not found.";
            public const string InvalidRunId = "Invalid or non-existent run ID.";
            public const string TopThemesRequired = "Top Themes are required.";
            public const string TopRecommendationsRequired = "Top Recommendations are required.";
            public const string TopActionsRequired = "Top Actions are required.";
            public const string QuestionTextReq = "QuestionText is required.";
            public const string CloverCategoryReq = "CloverCategory is required.";
            public const string QuestionAlreadyExist = "Question text already exists.";
            public const string ContentCultureId = "ContentCultureId must be greater than 0.";
            public const string OrdinalId = "Ordinal must be greater than 0.";
            public const string RequestBodyEmpty = "Request body cannot be empty.";
            public const string TeamAlreadyUnAssigned = "This team has already been unassigned.";
            public const string CompanyNotVerified = "Company already exists but is not verified.";
            public const string EmailExistsNotVerified = "Email exists but is not verified yet.";
            public const string UserEmailAlreadyExists = "A user with this email address already exists.";

            // api/profile/upsert-user — Add User with roles and relationships.
            public const string UserEmailInAnotherProfile = "A user with this email address already belongs to another profile.";
            public const string RoleNotAssignableToRridaAdmin = "Roles cannot be assigned to a RRIDA Admin account.";
            public const string UserAlreadyAssigned = "This user already holds this role and relationship. Nothing was changed.";
            public const string PortfolioManagerManagerRequired = "A manager is required. Select the Team Manager this Portfolio Manager will oversee.";
            public const string PortfolioManagerManagerNotFound = "The selected manager does not exist or does not belong to this profile.";
            public const string PortfolioManagerManagerNotEligible = "The selected user is not a Team Manager and cannot be placed under a Portfolio Manager.";
            public const string PortfolioManagerSelf = "A user cannot be their own Portfolio Manager.";
            public const string PortfolioManagerCycle = "The selected manager is already this user's Portfolio Manager.";
            public const string TeamManagerReplaceConfirm = "Team '{0}' already has a manager ({1}). Confirm to replace them.";
            public const string PortfolioManagerReplaceConfirm = "{0} already reports to Portfolio Manager {1}. Confirm to move them.";
            public const string TeamNameTaken = "A team with this name already exists in this profile.";
            public const string NoAvailableLicenseForTeam = "No available license. A new team needs a free license — purchase or free up a license before creating the team.";
            public const string ManagerTeamLimitReached = "Team limit reached: this manager already has {0} teams. A manager licence covers at most {1} teams.";
            public const string NoAvailableLicenseForTeamManager = "No available license. Each Team Manager uses one license and all licenses are in use — purchase another license before adding a Team Manager.";
            public const string TeamCapacityReached = "Team limit reached: {0} license(s) allow up to {1} teams ({2} per Team Manager). Purchase another license to add more teams.";
            public const string TeamLimitExtensionNotAllowed = "Only a RRIDA Admin can extend team limits.";
            public const string NoAvailableLicenseForUser ="No available license. The user was not added — purchase or free up a license first.";
            public const string LicenseLimitReached = "Licence limit reached. All licences on this profile are in use — buy additional licences to add more users.";

            // A profile may never be left without a Profile Admin. Two separate
            // rules: an admin never removes THEMSELF (they would lock themselves
            // out of the profile they administer), and nobody removes the LAST one
            // (the profile would have no administrator at all).
            public const string ProfileAdminCannotRemoveSelf = "You cannot remove your own Profile Admin account. Another Profile Admin can remove you.";
            public const string ProfileAdminRoleNotPermitted = "You do not have permission to change the Profile Admin role on this profile.";
            public const string ProfileAdminAlreadyAssigned = "This user is already a Profile Admin. Nothing was changed.";
            public const string ProfileAdminNotAssigned = "This user is not a Profile Admin. Nothing was changed.";
            public const string TeamManagerRemovalNotPermitted = "You do not have permission to remove the Team Manager role on this profile.";
            public const string TeamManagerNotAssigned = "This user is not a Team Manager. Nothing was changed.";
            public const string LastProfileAdminRequired ="This is the only Profile Admin left. A profile must always have at least one active Profile Admin — add another one first.";

            // A team may never be left without a manager. Unassigning the only
            // manager is refused; the admin replaces them instead (Add User /
            // assign-user-to-team), which hands the team over in one step.
            public const string TeamMustKeepManager = "This team would be left without a manager. Assign a replacement manager first, then this one is released automatically.";

            // Team Structure page (drag a team onto a Team Manager).
            public const string TeamStructureAccessDenied = "Only a Profile Admin can view or change the team structure.";
            public const string TeamStructureInvalidRequest = "ProfileId, UnitId and UserId are required.";
            public const string TeamStructureManagerNotFound = "The selected Team Manager does not exist or does not belong to this company.";
            public const string TeamStructureNotTeamManager = "{0} is not a Team Manager, so teams cannot be assigned to them. Give them the Team Manager role in Users first.";
            public const string TeamStructureManagerInactive = "{0} is inactive, so teams cannot be assigned to them. Make them active in Users first.";
            public const string TeamManagerLicenceAvailableConfirm = "A Team Manager license is available. Would you like to assign it to this user?";
            public const string TeamManagerLicenceNotAvailable = "No Team Manager licenses are currently available. Please purchase or free up a license.";
            public const string TeamManagerLicenceAssignFailed = "Unable to assign the Team Manager role. Please try again.";
            public const string TeamManagerLicenceUserInactive = "{0} is inactive, so a Team Manager license cannot be assigned to them. Make them active in Users first.";
            public const string TrialManagerNoPaidLicenceAvailable = "This Team Manager is on a trial license. No purchased license is available to assign. Please purchase a license first.";
            public const string PaidLicenceCannotBeReplacedByTrial = "This Team Manager already holds a purchased license. A trial license cannot replace it.";
            public const string TeamStructureNoLicence = "{0} does not have a valid team manager licence, so teams cannot be assigned to them. Go to the Licences tab to buy one, then move the team.";
            public const string TeamStructureUnlicensedManagerAlert = "{0} {1} assigned to {2} who does not have an active licence. {3} not visible in Team Health.";
            public const string TeamStructureInactiveManagerAlert = "{0} {1} assigned to {2} who is inactive. {3} not visible in Team Health.";
            public const string MoveManagerInvalidRequest = "ProfileId and UserId are required.";

            // Team Health milestones (Add milestone on the Team Health timeline).
            public const string MilestoneUnitRequired = "unitId is required.";
            public const string MilestoneTeamNotFound = "Team not found.";
            public const string MilestoneNotFound = "Milestone not found for this team.";
            public const string TimelineTeamIdInvalid = "teamId is required and must be a positive whole number.";
            public const string TimelineAccessDenied = "You do not have permission to view Team Health for this team.";

            // api/portfolio (Portfolio View).
            public const string PortfolioNotAllowed = "The Portfolio View is for a Portfolio Manager or a RRIDA Admin.";
            public const string PortfolioManagerIdInvalid = "managerId must be a positive whole number, or left out for All Managers.";
            public const string PortfolioManagerNotInPortfolio = "This manager is not in your portfolio.";
            public const string PortfolioManagerNotInProfile = "This manager is not in the selected profile.";
            public const string PortfolioProfileRequired = "profileId is required for a RRIDA Admin.";
            public const string PortfolioProfileNotFound = "The selected profile does not exist.";

            // Team Health custom questions ("Schedule a custom question").
            public const string WeeklyFlagWeekInvalid = "weekStartDate must be a valid date in YYYY-MM-DD or DD-MM-YYYY format.";

            public const string CustomQuestionAccessDenied = "You do not have permission to manage custom questions for this team.";
            public const string CustomQuestionTextRequired = "Question is required.";
            public const string CustomQuestionTextTooLong = "Question must be 140 characters or fewer.";
            public const string CustomQuestionDateRequired = "Ask-on date is required.";
            public const string CustomQuestionWeekend = "That is a weekend. Pick a working day \u2014 nobody is asked at the weekend.";
            public const string CustomQuestionDatePassed = "That date has already passed. Pick today or a later working day.";
            public const string CustomQuestionTodayTooLate = "Today's check-in has already gone out at 16:00. Pick a later working day.";
            public const string CustomQuestionSlotsFull = "Three questions are already scheduled, which is the maximum. Remove one to add another.";
            public const string CustomQuestionDayTaken = "There is already a question scheduled for that day. One custom question per day.";
            public const string CustomQuestionDuplicate = "This question is already scheduled for this team.";
            public const string CustomQuestionNotFound = "Custom question not found.";
            public const string CustomQuestionNotRemovable = "This question has already been asked, so it can no longer be removed.";
            public const string CustomQuestionAlreadyAnswered = "People have already answered this question, so it can no longer be removed.";
            public const string MilestoneAccessDenied = "You do not have permission to manage milestones for this team.";
            public const string MilestoneTitleRequired = "Title is required.";
            public const string MilestoneTitleTooLong = "Title must be 40 characters or fewer.";
            public const string MilestoneDescriptionRequired = "Description is required.";
            public const string MilestoneDescriptionTooLong = "Description must be 200 characters or fewer.";
            public const string MilestoneDateRequired = "Date is required.";
            public const string MilestoneDateInvalid = "Date must be a valid date in YYYY-MM-DD or DD-MM-YYYY format.";
            public const string MilestoneDateInFuture = "That is in the future. A milestone records something that has already happened — use a team goal for something planned.";
            public const string MilestoneDateRangeInvalid = "from must be on or before to.";

            // Team Health actions (action details panel behind an action dot).
            public const string ActionIdInvalid = "actionId is required and must be a positive whole number.";
            public const string ActionNotFound = "Action not found.";
            public const string ActionNotInFlight = "Action not in flight. It becomes an action in flight when a manager taps \"I'll do it\".";
            public const string ActionAccessDenied = "You do not have permission to manage actions for this team.";

            // api/actions (manager-created actions).
            public const string ManagerActionBodyRequired = "Request body is required.";
            public const string ManagerActionTitleRequired = "Title is required.";
            public const string ManagerActionTitleTooLong = "Title must be 200 characters or fewer.";
            public const string ManagerActionDescriptionTooLong = "Description must be 2000 characters or fewer.";
            public const string ManagerActionTeamRequired = "teamId is required.";
            public const string ManagerActionTeamNotFound = "Team not found.";
            public const string ManagerActionDueDateInvalid = "dueDate must be a valid date (YYYY-MM-DD or DD-MM-YYYY).";
            public const string ManagerActionDueDateInPast = "dueDate cannot be in the past.";
            public const string ManagerActionStartDateRequired = "startDate is required.";
            public const string ManagerActionStartDateInvalid = "startDate must be a valid date (YYYY-MM-DD or DD-MM-YYYY).";
            public const string ManagerActionDueBeforeStart = "dueDate cannot be before startDate.";
            public const string ManagerActionStatusInvalid = "status must be OPEN, IN_PROGRESS, DONE or CANCELLED.";
            public const string ManagerActionSourceInvalid = "source must be MANUAL, ENGINE, REPORT_GENERATED or SYSTEM.";
            public const string ManagerActionIdInvalid = "actionId must be a positive number, or engine-<number> for an engine action.";
            public const string ManagerActionEngineFieldNotEditable = "description cannot be changed on an engine action; send title, dueDate or status.";
            public const string ManagerActionNotFound = "Action not found.";
            public const string ManagerActionAccessDenied = "You do not have permission to access this action.";
            public const string ManagerActionCreateDenied = "You do not have permission to create an action here.";
            public const string ManagerActionProfileRequired = "profileId or teamId is required.";
            public const string ManagerActionNoChange = "Nothing to update.";
            public const string ManagerActionSuggestionChoiceInvalid = "suggestionChoice must be SUGGESTION, NONE or NONE_OF_THESE.";
            public const string ManagerActionSuggestionIdRequired = "engineActionId is required when suggestionChoice is SUGGESTION.";
            public const string ManagerActionSuggestionConflict = "engineActionId can only be sent with suggestionChoice SUGGESTION, or on its own.";
            public const string ManagerActionSuggestionNotFound = "engineActionId must be one of this team's ERAN suggestions (GET api/actions/eran-suggestions).";
            public const string ManagerActionSuggestionEngineAction = "engineActionId and suggestionChoice apply to manual actions only.";
            public const string ActionNothingToUpdate = "Send text or dueDate to update the action.";
            public const string ActionTextRequired = "Text cannot be empty.";
            public const string ActionTextTooLong = "Text must be 1000 characters or fewer.";
            public const string ActionDueDateInvalid = "dueDate must be a valid date in YYYY-MM-DD or DD-MM-YYYY format, or empty to clear it.";
            public const string ActionDueDateBeforeAction = "dueDate cannot be before the day the action was delivered.";
            public const string ActionStatusInvalid = "status must be CLOSED or IN_FLIGHT.";
            public const string ActionAlreadyClosed = "This action is already closed.";
            public const string ActionAlreadyInFlight = "This action is already in flight, so there is nothing to reopen.";
            public const string ActionStale = "This action was changed by someone else. Refresh it and try again.";
            public const string ActionCommentRequired = "Comment cannot be empty.";
            public const string ActionCommentTooLong = "Comment must be 500 characters or fewer.";
            public const string MoveManagerNotFound = "The selected manager does not exist or does not belong to this company.";
            public const string MoveManagerTargetNotFound = "The manager you dropped on does not exist or does not belong to this company.";
            public const string MoveManagerNotAManager = "{0} is not a Team Manager or Portfolio Manager, so they cannot be moved in the team structure.";
            public const string MoveManagerTargetNotAManager = "{0} is not a Team Manager or Portfolio Manager, so managers cannot report to them.";
            public const string MoveManagerTargetInactive = "{0} is inactive, so managers cannot report to them. Make them active in Users first.";
            public const string MoveManagerOntoSelf = "A manager cannot report to themselves.";
            public const string MoveManagerCircular = "{0} cannot report to {1}, because {1} already reports to {0}.";
            public const string TeamStructureLicenceExpiringAlert = "{0}'s licence expires {1}. It ends on {2} and will not renew automatically. {3} being asked questions the next day.";
            public const string TeamStructureUnassignedAlert = "{0} {1} no Team Manager. Drag {2} onto a Team Manager to assign {2}.";
            public const string ReferredCompanyNotFound = "The referred company does not exist.";

            // Partner-Customer hierarchy
            public const string PartnerCustomerAccessDenied = "Access denied. You do not have permission to perform this action.";
            public const string PartnerNotFound = "Partner not found or is not a valid partner.";
            public const string CustomerAlreadyAssigned = "Customer already assigned to {0}. Please reassign first.";
            public const string CannotReassignPartner = "Cannot reassign a partner company. Only customers can be reassigned.";
            public const string CompanyNotPartner = "The specified company is not a partner.";
            public const string PartnerHasCustomers = "Cannot delete partner. The following customers are still assigned: {0}";
            public const string CompanyAlreadyHasPartner = "This company is already assigned to partner \"{0}\". Please reassign the existing partner first.";

            // License module authorization. Used to stop a Partner admin (or any
            // non-Clovera role) from reaching another company's — e.g. a child
            // company's — License/subscription data via a crafted profileId.
            public const string LicenseModuleAccessDenied = "You are not authorized to access the License module for this company.";

            // Multi-tenant isolation. Returned (403) when an authenticated caller
            // sends a company id belonging to a tenant they are not part of.
            public const string CrossCompanyAccessDenied = "You are not authorized to access data for this company.";

            // Special units (FeedbackAutoImport:BusinessUnitIds) are exempt from
            // licensing — they are permanently covered without holding a license,
            // so pinning one to them would consume a paid seat for nothing.
            public const string UnitExemptFromLicensing = "This team is exempt from licensing and cannot be assigned a license.";

            // POST /api/license-assignments/cancel.
            public const string NoActiveLicenseForUser = "No active license found for this user.";
            public const string LicenseHasNoRecurringBilling = "This license has no recurring billing to cancel. Trial and one-time licenses do not renew.";
            public const string LicenseInBundledPlan = "This license is part of a bundled plan and cannot be cancelled on its own. Cancel the subscription instead.";

            // POST /api/subscription/cancel, /api/subscription/reactivate,
            // /api/license-assignments/cancel.
            public const string LicenseBillingNotAllowed = "Only a Profile Admin of this profile or a RRIDA Admin can manage this license.";
            public const string LicenseUserNotInProfile = "This user does not belong to the selected profile.";

            // POST /api/subscription/reactivate.
            public const string LicenseHasNoRecurringBillingToReactivate = "This license has no recurring billing to reactivate. Trial and one-time licenses do not renew.";
            public const string LicenseAlreadyEnded = "This license has already ended and cannot be reactivated. Buy a new license to continue.";

            // POST /api/license/change.
            public const string InvalidLicenseBillingCycle = "BillingCycle must be 'monthly' or 'annual'.";
            public const string LicenseAlreadyOnBillingCycle = "This license is already on the {0} billing cycle.";
            public const string LicenseCancelledNoPlanChange = "This license is cancelled and will end at the end of the current period. Reactivate it before changing its plan.";
            public const string LicensePlanChangeBundled = "This license is part of a bundled plan and its billing cycle cannot be changed on its own.";
            public const string LicensePlanChangeInProgress = "A plan change for this license is already being completed. Try again in a moment.";
            public const string NoStripeCustomer = "This profile has no Stripe customer record. Complete a paid checkout first.";

            // Plan management (api/admin/plans, api/admin/plan-features). RRIDA Admin only.
            public const string PlanAdminOnly = "Only a RRIDA Admin can manage plans.";
            public const string PlanBodyRequired = "Request body is required.";
            public const string PlanNotFound = "Plan not found.";
            public const string PlanCodeRequired = "Code is required.";
            public const string PlanCodeInvalid = "Code must be 2-30 characters: lowercase letters, digits and '-' (e.g. 'pro', 'light-plus').";
            public const string PlanCodeTaken = "A plan with this code already exists.";
            public const string PlanNameRequired = "Name is required.";
            public const string PlanNameTooLong = "Name must be at most 100 characters.";
            public const string PlanNameTaken = "A plan with this name already exists.";
            public const string PlanDescriptionTooLong = "Description must be at most 1000 characters.";
            public const string PlanInternalNotesTooLong = "Internal notes must be at most 4000 characters.";
            public const string PlanPricingUnitInvalid = "Pricing unit must be 1-50 characters (e.g. 'manager').";
            public const string PlanCurrencyInvalid = "Currency must be a 3-letter ISO code (e.g. 'USD').";
            public const string PlanSortOrderInvalid = "Sort order must be 0 or more.";
            public const string PlanVersionRequired = "Version is required. Send the version from the last read of the plan.";
            public const string PlanVersionConflict = "This plan was changed by someone else. Reload it and try again.";
            public const string PlanStatusRequired = "isActive is required.";
            public const string PlanStatusUnchanged = "The plan is already {0}.";
            public const string PlanActivateNeedsTiers = "A plan needs at least one price tier before it can be active.";
            public const string PlanLastActive = "This is the only active plan. Activate another plan before deactivating it.";
            public const string PlanReasonTooLong = "Reason must be at most 500 characters.";
            public const string PlanTiersRequired = "At least one price tier is required.";
            public const string PlanTierFieldsRequired = "Tier {0}: minManagers, monthlyPrice and annualPrice are required.";
            public const string PlanTierRangeInvalid = "Tier {0}: minManagers must be 1 or more and maxManagers, when sent, must not be below minManagers.";
            public const string PlanTierPriceInvalid = "Tier {0}: prices must be between 0 and 99,999,999.99 with at most 2 decimals.";
            public const string PlanTierMustStartAtOne = "The first price tier must start at 1 manager.";
            public const string PlanTierGapOrOverlap = "Price tiers must follow on without gaps or overlaps: tier {0} must start at {1}.";
            public const string PlanTierOpenEndedNotLast = "Only the last price tier can have no maxManagers.";
            public const string PlanTiersTooMany = "A plan can have at most 20 price tiers.";
            public const string PlanFeaturesRequired = "At least one feature value is required.";
            public const string PlanFeatureRefRequired = "Feature {0}: featureId or featureKey is required.";
            public const string PlanFeatureRefNotFound = "Feature {0} was not found.";
            public const string PlanFeatureDuplicate = "Feature {0} is listed more than once.";
            public const string PlanFeatureInactive = "Feature {0} is inactive. Activate it before including it in a plan.";
            public const string PlanFeatureIncludedRequired = "Feature {0}: isIncluded is required.";
            public const string PlanFeatureLimitNotAllowed = "Feature {0}: limitValue is only allowed on LIMIT features.";
            public const string PlanFeatureLimitInvalid = "Feature {0}: limitValue must be 0 or more (leave it empty for Unlimited).";
            public const string PlanFeatureTextRequired = "Feature {0}: displayText is required when a TEXT feature is included.";
            public const string PlanFeatureDisplayTextTooLong = "Feature {0}: displayText must be at most 150 characters.";
            public const string PlanFeatureSubTextTooLong = "Feature {0}: subText must be at most 200 characters.";
            public const string PlanReorderIdsRequired = "ids must list every {0} exactly once.";
            public const string PlanFeatureNotFound = "Feature not found.";
            public const string PlanFeatureKeyRequired = "Key is required.";
            public const string PlanFeatureKeyInvalid = "Key must be 2-60 characters: uppercase letters, digits and '_' (e.g. 'PORTFOLIO_VIEW').";
            public const string PlanFeatureKeyTaken = "A feature with this key already exists.";
            public const string PlanFeatureTitleRequired = "Title is required.";
            public const string PlanFeatureTitleTooLong = "Title must be at most 150 characters.";
            public const string PlanFeatureDescriptionTooLong = "Description must be at most 500 characters.";
            public const string PlanFeatureValueTypeInvalid = "valueType must be INCLUDED, LIMIT or TEXT.";
            public const string PlanFeatureLimitUnitTooLong = "limitUnit must be at most 50 characters.";
            public const string PlanFeatureLimitPeriodInvalid = "limitPeriod must be NONE or MONTH, and is only allowed on LIMIT features.";
            public const string PlanFeatureFootnoteTooLong = "Footnote must be at most 500 characters.";
            public const string PlanFeatureSystemTypeLocked = "This feature is used by the product, so its value type cannot change.";
            public const string PlanFeatureTypeChangeInUse = "Value type cannot change while plans have values for this feature.";
            public const string PlanFeatureSystemCannotDeactivate = "This feature is used by the product and cannot be deactivated.";
            public const string PlanFeatureStatusUnchanged = "The feature is already {0}.";

            // Plan catalogue reads and entitlements (api/plans, api/entitlements).
            public const string PlanQuotePlanRequired = "planId or planCode is required.";
            public const string PlanQuoteQuantityInvalid = "quantity must be between 1 and 10000.";
            public const string PlanQuoteCycleInvalid = "billingCycle must be 'monthly' or 'annual'.";
            public const string PlanNotAvailable = "Plan not found or not available.";
            public const string PlanNoTierForQuantity = "This plan has no price for {0} managers.";
            public const string PlanHasNoVersion = "This plan has no saved version yet. Save its price tiers or features first.";
            public const string PlanVersionNotFound = "That version of the plan was not found.";
            public const string ReplacedLicenceCancelFailed = "The plan was switched, but licence {0} could not be cancelled automatically ({1}). It keeps renewing until cancelled with POST license-assignments/cancel.";
            public const string PlanChoiceRequired = "Both Light and Pro licences are free. Send planCode (\"light\" or \"pro\") to choose which one to give.";
            public const string PlanTeamsOverLimit = "Runs {0} teams; the {1} plan allows {2}. Existing teams are kept, but no new team can be added until this manager is within the limit.";
            public const string PlanFeatureNotInPlan = "{0} is not included in your {1} plan. Upgrade to Pro to use it.";
            public const string PlanFeatureLimitReached = "You have used all {0} {1} included in your {2} plan this month. More are available from {3}.";
            public const string TeamLimitExtensionPlanNotAllowed = "This licence's plan does not allow a team limit extension. Only plans such as Pro can be extended.";
            public const string PlanLicenceNotAvailable = "No available {0} licence. Purchase a {0} licence first.";
            public const string PlanLicenceWrongPlan = "License {0} is not a {1} licence.";
            public const string PlanSwitchTooManyTeams = "{0} runs {1} teams but the {2} plan allows {3}. Move teams to another manager first.";
            public const string EntitlementUserNotFound = "User not found.";
            public const string EntitlementDenied = "You can only see your own plan entitlements, or those of users in the profile you administer.";
        }

        public static class Success
        {
            public static string Linkemail = "Reset password link sent to your email.";
            public static string PasswordReset = "Password has been reset successfully.";
            public static string PasswordUpdated = "Password updated successfully.";
            public static string RegistrationSuccess = "Company and user created. Please verify your account via the email sent.";
            public static string ProfileRegistrationSuccess = "Profile and user created. Please verify your account via the email sent.";

            public static string GroupConfigSuccess = "User group configuration completed successfully.";

            public static string UserFound = "User data found successfully.";
            public static string GroupUpdateSuccess = "User group updated successfully.";
            public const string UnitCreationSuccess = "Unit and user created. Please verify your account via the email sent.";

            public const string UnitLinkedToCompany = "A new unit '{0}' has been created and linked to your company.";
            public const string BusinessUnitCreated = "A new business unit '{0}' has been created under your company '{1}'.";

            public const string LoginSuccess = "User logged in successfully.";
            public const string CompanyRecordFound = "Company Record found.";
            public const string EmailAvailable = "Email is valid and available.";
            public const string DomainAvailable = "Domain is valid.";
            public const string CompanyDataFound = "Company data found successfully.";
            public const string EmailVerified = "User email verified successfully.";
            public const string NotificationsFetched = "Notifications fetched successfully.";
            public const string UserCreated = "User created successfully.";
            public const string UnitsFound = "Units retrieved successfully.";
            public const string FeedbackSummaryGenerated = "Feedback dashboard summary generated successfully.";
            public const string TopInsightsGenerated = "Top insights generated successfully.";
            public const string RecordFound = "Record Found Succesfully";
            public const string DailyQuestionInviteSent = "Daily question calendar invite sent successfully.";
            public const string TeamCalendarInviteSent = "Calendar invitation sent successfully to your registered email address.";
            public const string TeamCalendarInviteAlreadySent = "This calendar invitation was just sent to your registered email address.";
            public const string Userdump = "User dump completed successfully.";
            public const string Userupsert = "User upserted successfully.";

            // api/profile/upsert-user, whose two halves must not read alike: the
            // create half tells the admin an invitation is on its way, and says so
            // only when the send actually succeeded. Sending them to watch an inbox
            // nothing was posted to is the failure this pair exists to prevent.
            public const string UserInvited = "User added. An invitation to set their password has been sent to their email address.";
            public const string UserCreatedInviteFailed = "User added, but the invitation email could not be sent. Re-send it from the user list.";
            public const string UserUpdated = "User updated successfully.";
            public const string UserRoleAssigned = "The existing user has been assigned the role. No new account or invitation was created.";
            public const string ProfileAdminAssigned = "The user is now a Profile Admin. Their details and other roles are unchanged.";
            public const string ProfileAdminRemoved = "The Profile Admin role has been removed. The user's details and other roles are unchanged.";
            public const string TeamManagerRemoved = "Team Manager role removed successfully.";

            public const string ManagerCreatedWithTeam = "Manager created and assigned to team '{0}'. An invitation has been sent to them.";
            public const string ManagerCreatedInviteFailed = "Manager created and assigned to team '{0}', but the invitation email could not be sent. Re-send it from the user list.";
            public const string Userdelete = "User deleted successfully";
            public const string StatusUpdate = "User status updated successfully.";
            public const string UserMadeActive = "User made active successfully.";
            public const string UserMadeActiveWithCredentials = "User made active. Share the set-password link with the user.";
            public const string UserMadeActiveInviteSent = "User made active and an invitation email has been sent.";
            public const string UserMadeActiveInviteFailed = "User made active, but the invitation email could not be sent. Try again or generate credentials manually.";
            public const string UserMadeInactive = "User made inactive successfully.";
            public const string UserUnlocked = "User account unlocked successfully.";
            public const string UserActivatedManually = "User activated successfully. They can now log in with the temporary password.";
            public const string InvitationResent = "The invitation has been sent to the user's email address again.";
            public const string InvitationLinkReady = "Invite link ready to copy. It can be used once and expires on the date shown.";
            public const string TeamDelSuccess = "Businessteam deleted successfully.";
            public const string UserGroupSuccess = "User Groups fetch successfully.";
            public const string UserAssignteamSuccess = "User assigned to team successfully.";
            public const string UsersList = "User Records Found Successfully";
            public const string TeamCreateSuccess = "Team created successfully.";
            public const string TeamUpdateSuccess = "Team updated successfully.";
            public const string TeamListSuccess = "Team list fetched successfully.";
            public const string TeamStructureSuccess = "Team structure fetched successfully.";
            public const string TeamMovedToManager = "{0} now sits under {1}.";
            public const string TeamManagerLicenceAndRoleAssigned = "Team Manager license and role successfully assigned.";
            public const string TrialConvertedToPaidLicence = "Trial license ended. The purchased license is now active for this Team Manager.";
            public const string TeamAlreadyWithManager = "{0} is already assigned to {1}. Nothing was changed.";
            public const string ManagerMoved = "{0} now reports to {1}.";
            public const string ManagerAlreadyReports = "{0} already reports to {1}. Nothing was changed.";

            // Team Health milestones.
            public const string MilestoneCreated = "Milestone added.";
            public const string MilestoneUpdated = "Milestone updated.";
            public const string TimelineSuccess = "Team Health timeline fetched successfully.";
            public const string PortfolioManagersSuccess = "Portfolio managers fetched successfully.";
            public const string PortfolioTeamsSuccess = "Portfolio teams fetched successfully.";
            public const string PortfolioAlertsSuccess = "Portfolio alerts fetched successfully.";
            public const string PortfolioEranContextSuccess = "Portfolio Eran context fetched successfully.";
            public const string WeeklyFlagsFetched = "Weekly flags fetched successfully.";
            public const string WeeklyFlagsNone = "Nothing flagged for this team yet.";
            public const string TeamStateFetched = "Team state fetched successfully.";
            public const string CustomQuestionsFetched = "Custom questions fetched successfully.";
            public const string UserTeamsFetched = "User teams fetched successfully.";
            public const string CustomQuestionScheduled = "Question scheduled.";
            public const string CustomQuestionRemoved = "Question removed.";
            public const string MilestoneListSuccess = "Milestones fetched successfully.";
            public const string ActionFetched = "Action fetched successfully.";
            public const string ActionsFetched = "Actions fetched successfully.";
            public const string ActionUpdated = "Action updated.";
            public const string ActionClosed = "Action marked as closed.";
            public const string ActionReopened = "Action reopened.";
            public const string ActionCommentAdded = "Comment added.";
            public const string ManagerActionCreated = "Action created.";
            public const string ManagerActionUpdated = "Action updated.";
            public const string ManagerActionCommentAdded = "Comment added.";
            public const string ManagerActionsFetched = "Actions retrieved successfully.";
            public const string ManagerActionFetched = "Action retrieved successfully.";
            public const string EranSuggestionsFetched = "ERAN suggestions retrieved successfully.";

            // Plan management (api/admin/plans, api/admin/plan-features).
            public const string PlansFetched = "Plans retrieved successfully.";
            public const string PlanFetched = "Plan retrieved successfully.";
            public const string PlanCreated = "Plan created.";
            public const string PlanUpdated = "Plan updated.";
            public const string PlanActivated = "Plan activated.";
            public const string PlanDeactivated = "Plan deactivated.";
            public const string PlanPriceTiersUpdated = "Price tiers updated.";
            public const string PlanFeaturesUpdated = "Plan features updated.";
            public const string PlanDuplicated = "Plan duplicated as an inactive draft.";
            public const string PlansReordered = "Plans reordered.";
            public const string PlanHistoryFetched = "Plan history retrieved successfully.";
            public const string PlanComparisonFetched = "Plan comparison retrieved successfully.";
            public const string PlanFeaturesFetched = "Features retrieved successfully.";
            public const string PlanFeatureFetched = "Feature retrieved successfully.";
            public const string PlanFeatureCreated = "Feature created.";
            public const string PlanFeatureUpdated = "Feature updated.";
            public const string PlanFeatureActivated = "Feature activated.";
            public const string PlanFeatureDeactivated = "Feature deactivated.";
            public const string PlanFeaturesReordered = "Features reordered.";
            public const string PlanPriceQuoted = "Price calculated successfully.";
            public const string PlanVersionsFetched = "Plan versions retrieved successfully.";
            public const string PlanLicencesMigrated = "Licences moved to the plan's newest version.";
            public const string EntitlementsFetched = "Plan entitlements retrieved successfully.";

            // Team setup — the team plus its type, context and goals, saved as one thing.
            public const string TeamSetupCreated = "Team set up successfully.";
            public const string TeamSetupUpdated = "Team setup updated successfully.";
            public const string TeamSetupFound = "Team setup fetched successfully.";
            public const string TeamAttributesUpdated = "Team attributes updated successfully.";
            public const string TeamGoalsUpdated = "Team goals updated successfully.";
            public const string ActionHubSuccess = "ActionHub created successfully.";
            public const string ActionEventDelSuccess = "ActionEvent deleted successfully.";
            public const string ActionHubDelSuccess = "ActionHub deleted successfully.";
            public const string ActionHubDataFound = "Action Hub details retrieved successfully.";
            public const string ActionHubSummaryFound = "Action Hub summary retrieved successfully.";
            public const string NameReq = "Name is required.";
            public const string NameSuccess = "Name already exist.Plz use unique name.";
            public const string EmailSuccess = "Provided email already exists.";
            public const string UserLogout = "User Logout Successfully.";
            public const string RefreshToken = "Token refreshed successfully.";
            public const string OperationSuccess = "Operation performed Successfully.";
            public const string LicenseCancelScheduled = "Subscription will cancel at the end of the current period.";
            public const string LicenseReactivated = "License reactivated. It will renew as normal.";
            public const string TeamLimitExtended = "Team limit extended successfully.";
            public const string LicensePlanChangeCheckout = "Continue in checkout to confirm plan change.";
            public const string InsightsGenerate = "Insights generation initiated.";
            public const string BatchJobSuccess = "Batch job listing endpoint - implementation pending";
            public const string ReportsSuccess = "Reports generated successfully.";
            public const string FeedbackDataImport = "Feedback data imported successfully";
            public const string QuestionDataFound = "Question data found successfully.";
            public const string AzureAISuccess = "AzureAI generated successfully.";
            public const string ImportPromptData = "Import prompt data successfully";
            public const string BatchJobStatus = "BatchJob status fetched successfully";
            public const string AzureInsightsSaved = "Insights saved successfully";
            public const string BatchJobSucc = "BatchJob created successfully.";
            public const string WorksheetFound = "Worksheet file found successfully.";
            public const string PartnerStatusSuccess = "Company partner status updated successfully.";
            public const string tasksucces = "Task is successed successfully.";
            public const string oppupdate = "Opportunity updated successfully.";
            public const string ResetPasswordLinkGenerated = "Reset password link generated successfully.";
            public const string CompanyEmailAvailable = "Domain is free and ready to proceed.";
            public const string ReportsDelSuccess = "Report deleted successfully.";
            public const string RunInsightsSaved = "Run insights saved successfully.";
            public const string RunInsightsUpdated = "Run insights updated successfully.";
            public const string QuestionSuccess = "Question added successfully.";
            public const string CustomQuestionFeedbackSuccess = "Answer recorded successfully.";
            public const string FileFound = "File found successfully.";
            public const string QuestionUpdated = "Question Updated successfully.";
            public const string UserUnAssignTeamSuccess = "User has been successfully unassigned from the team.";
            public const string ReferralAssignedSuccess = "Referral assigned successfully.";

            // Partner-Customer hierarchy
            public const string PartnerCustomerCreated = "Customer created successfully.";
            public const string PartnerCustomerReassigned = "Customer reassigned successfully.";
            public const string PartnerDeleted = "Partner deleted successfully.";

        }

        public static class Warnings
        {
            public static string InvalidLink = "The reset link is invalid or has been altered.";

            public static string EmailNotVerified = "Your email is not verified.";

            public static string InvalidLinkReset = "Invalid or expired reset request.";

            public static string UnitExists = "This Unit has already been registered.";
            public const string EmailAlreadyTaken = "Email already taken.";
            public const string DatesNotProvided = "Please provide both FromDate and ToDate.";
            public const string DaysNotProvided = "Please provide valid Days when DateRange is disabled.";


        }

        public static class General
        {
            public static string ResetFailed = "Unable to process reset request.";

            public static string ResetExpired = "Reset link has expired.";

        }





    }
}
