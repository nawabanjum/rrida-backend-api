using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IActionHubService
    {
        Task<UpsertEventDto> UpsertAsync(UpsertEventDto dto);
        Task<bool> DeleteAsync(int id);
        Task<UpsertActiontHubDto> UpsertActionHubAsync(UpsertActiontHubDto dto);
        Task<bool> DeleteActionHubAsync(int id);
        Task<List<ActionHubWithEventsDto>> GetActionHubWithEventsAsync(int id, string type);
        Task<ActionHubSummaryDto> GetActionHubSummaryAsync(int unitId, string? type);
    }
}
