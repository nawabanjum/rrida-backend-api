using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using Microsoft.AspNetCore.Http;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ICloverEraEngineService
    {
        Task<CloverEraCompleteReportDto> RunEngineAsync(
            WorkbenchRunRequestDto request,
            IFormFile csvFile,
            CancellationToken ct = default);
    }
}
