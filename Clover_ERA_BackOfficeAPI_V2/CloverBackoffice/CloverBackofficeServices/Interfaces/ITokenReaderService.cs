using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ITokenReaderService
    {
        string GetToken();
        int? GetUserIdFromToken();
        int? GetTokenInfo();
        Task<User?> GetCurrentUserAsync();
        Task<bool> IsAdminAsync();
    }
}
