using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IDailyFeedbackAuditService
    {
        Task PerformAuditAsync();
    }
}
