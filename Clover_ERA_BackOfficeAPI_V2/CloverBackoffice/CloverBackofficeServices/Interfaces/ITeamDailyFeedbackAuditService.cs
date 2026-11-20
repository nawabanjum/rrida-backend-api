using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ITeamDailyFeedbackAuditService
    {
        Task PerformAuditAsync();
    }
}
