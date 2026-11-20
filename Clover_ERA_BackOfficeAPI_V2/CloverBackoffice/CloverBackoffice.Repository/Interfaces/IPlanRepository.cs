using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IPlanRepository
    {
        Task<List<Plan>> GetAllActiveAsync();
        Task<Plan?> GetBySlugAsync(string slug);
        Task<Plan?> GetByIdAsync(int id);
    }
}
