using System.Text;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Implementation
{
    public partial class PortfolioViewService
    {
        // The banner: teams in Headwinds or Stall, in table order. The count is the list.
        private static PortfolioAlertsDto BuildAlerts(Scope scope, List<PortfolioTeamDto> teams)
        {
            var attention = teams
                .Where(t => !t.IsCalibrating && (t.HealthState == TeamStates.Headwinds || t.HealthState == TeamStates.Stall))
                .Select(t => new PortfolioAlertTeamDto
                {
                    TeamId = t.TeamId,
                    TeamName = t.TeamName,
                    HealthState = t.HealthState!,
                    WeeksCount = t.WeeksCount,
                    ManagerId = t.TeamManagerId,
                    ManagerName = t.TeamManagerName,
                    Reason = ReasonOf(t)
                })
                .ToList();

            return new PortfolioAlertsDto
            {
                ManagerId = scope.Selected?.UserId,
                ManagerName = scope.Selected?.Name,
                AttentionCount = attention.Count,
                Title = attention.Count == 0
                    ? null
                    : $"{attention.Count} {(attention.Count == 1 ? "team needs" : "teams need")} you, not the manager.",
                Message = attention.Count == 0 ? null : string.Join(" ", attention.Select(a => a.Reason)),
                ReviewTeamIds = attention.Select(a => a.TeamId).ToList(),
                Teams = attention
            };
        }

        private static string ReasonOf(PortfolioTeamDto team) => team.HealthState == TeamStates.Headwinds
            ? $"{team.TeamName} is carrying external load: its own conditions are holding, but the pressure is coming from outside the team."
            : $"{team.TeamName} has been in Stall for {Weeks(team.WeeksCount)}.";

        // The Eran column's brief, from the same teams and alerts the page shows.
        private static PortfolioEranContextDto BuildEranContext(Scope scope, List<PortfolioTeamDto> teams)
        {
            var alerts = BuildAlerts(scope, teams);
            var counts = new PortfolioStateCountsDto
            {
                Headwinds = teams.Count(t => t.HealthState == TeamStates.Headwinds),
                Stall = teams.Count(t => t.HealthState == TeamStates.Stall),
                Drift = teams.Count(t => t.HealthState == TeamStates.Drift),
                Cruise = teams.Count(t => t.HealthState == TeamStates.Cruise),
                Calibrating = teams.Count(t => t.IsCalibrating)
            };

            var total = teams.Count;
            var whose = scope.Selected != null ? $"{scope.Selected.Name}'s" : scope.IsRridaAdmin ? "this profile's" : "your";
            string lead;
            if (total == 0)
                lead = scope.Selected != null
                    ? $"{scope.Selected.Name} has no teams yet."
                    : $"There are no teams in {(scope.IsRridaAdmin ? "this profile" : "your portfolio")} yet.";
            else if (alerts.AttentionCount == 0)
                lead = total == 1 ? $"{Cap(whose)} one team is steady." : $"All {Words(total)} of {whose} teams are steady.";
            else
                lead = $"{Cap(Words(alerts.AttentionCount))} of {whose} {Words(total)} {(total == 1 ? "team wants" : "teams want")} a look this week.";

            var body = new List<string>();
            var steady = total - alerts.AttentionCount - counts.Calibrating;
            if (steady > 0 && alerts.AttentionCount > 0)
                body.Add($"{Cap(Words(steady))} {(steady == 1 ? "is steady and does" : "are steady and do")} not need you.");
            foreach (var team in teams.Where(t => alerts.ReviewTeamIds.Contains(t.TeamId)))
                body.Add(team.HealthState == TeamStates.Headwinds
                    ? $"{team.TeamName} is in Headwinds: its own conditions are holding but the pressure is coming from outside, so its manager cannot fix it alone."
                    : $"{team.TeamName} has been in Stall for {Weeks(team.WeeksCount)}.");
            if (counts.Calibrating > 0)
                body.Add($"{Cap(Words(counts.Calibrating))} {(counts.Calibrating == 1 ? "is" : "are")} still calibrating, so there is no state to read yet.");

            var prompts = new List<string>();
            if (total > 0)
            {
                prompts.Add("Which team should take the next initiative?");
                var stalled = teams.FirstOrDefault(t => t.HealthState == TeamStates.Stall);
                if (stalled != null) prompts.Add($"Why is {stalled.TeamName} still in Stall?");
                prompts.Add("Who is at risk of leaving?");
            }

            return new PortfolioEranContextDto
            {
                Scope = scope.Selected != null ? "manager" : "portfolio",
                ManagerId = scope.Selected?.UserId,
                ManagerName = scope.Selected?.Name,
                TeamCount = total,
                ManagerCount = teams.Select(t => t.TeamManagerId).Distinct().Count(),
                StateCounts = counts,
                Lead = lead,
                Body = string.Join(" ", body),
                Prompts = prompts,
                Alerts = alerts,
                Teams = teams
            };
        }

        // The system block for an Eran chat turn on the Portfolio View. Only this scope's teams.
        private static string BuildEranContextText(Scope scope, PortfolioEranContextDto c)
        {
            var person = scope.IsRridaAdmin ? "a RRIDA administrator" : "a Portfolio Manager";
            var theirs = scope.IsRridaAdmin ? "this profile" : "their portfolio";
            var above = scope.IsRridaAdmin ? "attention above the manager" : "the Portfolio Manager";

            var sb = new StringBuilder();
            sb.AppendLine("## THIS PORTFOLIO (live from the RRIDA Portfolio View)");
            sb.AppendLine();
            sb.AppendLine(c.ManagerName != null
                ? $"The person is {person} looking at the teams of one manager in {theirs}: {c.ManagerName}."
                : $"The person is {person} looking at every team across the managers in {theirs} (All Managers).");
            sb.AppendLine("These are the only teams in this conversation. Do not discuss any team, manager or figure that is not "
                          + "listed here, and do not guess at one. States route attention: they never rank one team or manager "
                          + "against another and never feed a manager's evaluation. Headwinds means the causes read as outside "
                          + $"the team, so its manager cannot fix it alone; that and Stall are the ones that need {above}. "
                          + "A calibrating team has no state to read yet.");
            sb.AppendLine();
            sb.AppendLine($"Teams: {c.TeamCount} across {c.ManagerCount} manager(s). Headwinds {c.StateCounts.Headwinds}, "
                          + $"Stall {c.StateCounts.Stall}, Drift {c.StateCounts.Drift}, Cruise {c.StateCounts.Cruise}, "
                          + $"Calibrating {c.StateCounts.Calibrating}.");
            if (c.Alerts.AttentionCount > 0)
                sb.AppendLine($"Needs {above}: {c.Alerts.Message}");
            sb.AppendLine();
            sb.AppendLine("### Teams");
            foreach (var t in c.Teams)
            {
                var line = $"- {t.TeamName} — manager {t.TeamManagerName} — {t.MemberCount} members — ";
                line += t.IsCalibrating
                    ? "calibrating, no state to read yet"
                    : $"{Label(t.HealthState)} for {Weeks(t.WeeksCount)} (as of {t.AsOfDate}; vertical {Label(t.Vertical)}"
                      + (t.HeadwindsFlag ? ", headwinds flag raised" : "") + ")"
                      + (t.Trend.Count > 0
                          ? $" — weekly share of days in Cruise over the last quarter, oldest first: {string.Join(", ", t.Trend.Select(p => p.Score))}"
                          : "");
                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        private static string Label(string? state) =>
            string.IsNullOrEmpty(state) ? "" : char.ToUpperInvariant(state[0]) + state[1..].ToLowerInvariant();

        private static string Weeks(int weeks) => weeks == 1 ? "1 week" : $"{weeks} weeks";

        private static readonly string[] NumberWords =
            { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

        private static string Words(int n) => n >= 0 && n < NumberWords.Length ? NumberWords[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
