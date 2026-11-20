namespace CloverBackoffice.Infrastructure.DTO
{
    // Onboarding nudges sent to a NEW team's manager in its first week.
    // Bound to "TeamOnboardingNotifications" in appsettings.json.
    //
    //   Day 2 — "you've had X responses yesterday"
    //   Day 5 — "your average is X responses per day; log in to see your scores"
    //
    // Both are time-delayed relative to team creation, so they are driven by a
    // daily background service rather than by the upsert-team endpoint. Day
    // numbers are counted from BusinessUnit.RegistrationDate: day 2 means two
    // whole days after the team was created.
    public class TeamOnboardingNotificationOptions
    {
        // false = the hosted service exits at startup and no nudges are sent.
        public bool Enabled { get; set; } = true;

        // "A new team has been created and assigned to you" — sent immediately
        // when a manager is assigned to a team. A team is created with no
        // manager, so this is the first moment there is anyone to email.
        //
        // NOT licence-gated: it tells a manager they now own a team, which is
        // true whether or not the team is paid for. The day 2 / day 5 nudges
        // below are paid-only, because they point at scores.
        public bool NotifyOnManagerAssignment { get; set; } = true;

        // "You have been unassigned from X" — sent when a manager is removed
        // from a team, and to the OUTGOING manager when a team is handed to
        // someone else. Losing a team is as much a change of responsibility as
        // gaining one, and the outgoing manager stops receiving that team's
        // reports and alerts without this.
        public bool NotifyOnManagerUnassignment { get; set; } = true;

        // Normally a manager is told once per team: the in-request hook and the
        // background catch-up can both reach the same assignment, and nobody
        // should be mailed twice.
        //
        // Set true to bypass that check on the ASSIGN path, so every assign
        // action sends the email again. Use when a manager reports never having
        // received it and you need to force a resend without touching the
        // database. The background catch-up keeps de-duplicating either way, so
        // this cannot turn into a mail loop.
        public bool ResendAssignmentNotifications { get; set; } = false;

        // How recently a team must have been created for an assignment to read
        // as "a NEW team has been created and assigned to you" rather than
        // "the team X has been assigned to you". Covers the normal gap between
        // creating a team and getting round to assigning its manager.
        public int NewTeamGraceDays { get; set; } = 14;

        // Day numbers, counted from the day the team FIRST got a manager — not
        // from team creation. A team created on Monday whose manager is assigned
        // on Friday starts its day-2 count from Friday, because before that
        // there was nobody to notify.
        public int Day2AfterDays { get; set; } = 2;
        public int Day5AfterDays { get; set; } = 5;

        // How many extra days a nudge stays eligible after its due day.
        //
        // Two things need this. A team created before its manager was assigned
        // has nobody to notify on day 2 — with a 2-day window a manager
        // assigned on day 3 or 4 still gets it, and after that it is dropped
        // rather than arriving stale. It also makes the job resilient: a missed
        // run (deploy, downtime) is caught up on the next one instead of
        // silently skipping that team forever.
        public int CatchUpDays { get; set; } = 2;

        // Path appended to SendGridSettings.FrontendBaseUrl for the "log in and
        // see your scores" link. Shared by every manager email this service
        // sends — team assigned, day 2, day 5, and the no-response alert.
        public string ScoresUrlPath { get; set; } = "/teams";

        // Where the Action Hub inactivity email points. That alert is about
        // actions, not scores, so it gets its own route rather than sharing
        // ScoresUrlPath.
        public string ActionHubUrlPath { get; set; } = "/actionhub";

        // ── Daily response monitoring ─────────────────────────────────────────
        // Ongoing, unlike the onboarding nudges above: every day the job checks
        // yesterday and alerts the manager if the team was silent.

        // false = no "your team didn't respond yesterday" emails.
        public bool NoResponseAlertEnabled { get; set; } = true;

        // Minimum days between consecutive alerts for the SAME team. 1 = alert
        // every day it stays silent (the literal rule). Raise it if a team that
        // never responds should not generate a daily email forever.
        public int NoResponseAlertCooldownDays { get; set; } = 1;

        // Teams still inside their onboarding sequence are skipped: the day 2
        // nudge already tells a brand-new manager there were no responses, and
        // both firing on the same morning reads as a system fault.
        public bool SkipNoResponseAlertDuringOnboarding { get; set; } = true;

        // ── Action Hub inactivity ─────────────────────────────────────────────
        // "Nothing has moved on your actions for a week."

        public bool ActionHubIdleAlertEnabled { get; set; } = true;

        // Days of no Action Hub activity before the manager is told. Activity =
        // an action created, edited or deleted, or an event added to one.
        public int ActionHubIdleDays { get; set; } = 7;

        // Minimum days between consecutive Action Hub alerts for the same team.
        // Defaults to the same 7 days as the threshold: a team that stays idle
        // is reminded weekly, not every morning.
        public int ActionHubIdleCooldownDays { get; set; } = 7;

        // true  = only teams that have at least one action in the hub are
        //         alerted. A team that has never created an action is not
        //         "inactive on its actions" — it has none — and nagging teams
        //         that don't use the feature is noise.
        // false = every paid team with a manager is alerted, including those
        //         that have never created an action.
        public bool ActionHubAlertOnlyIfActionsExist { get; set; } = true;

        // Audit identity used both for the trail and as the "already sent"
        // marker (LLMLogs.Endpoint = "Hangfire/{JobName}").
        public const string JobName = "TeamOnboarding";
        public const string ActionHubIdleAlertStatus = "ActionHubIdleAlert";
        public const string Day2SentStatus = "Day2Sent";
        public const string Day5SentStatus = "Day5Sent";
        public const string NoResponseAlertStatus = "NoResponseAlert";
    }
}
