using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IPlanService
    {
        Task<List<PlanDto>> GetAllActivePlansAsync();
        Task<PlanDto?> GetPlanBySlugAsync(string slug);
        Task<PlanDto?> GetPlanByIdAsync(int id);
        PriceCalculationDto CalculatePrice(int teamCount);
    }
}
