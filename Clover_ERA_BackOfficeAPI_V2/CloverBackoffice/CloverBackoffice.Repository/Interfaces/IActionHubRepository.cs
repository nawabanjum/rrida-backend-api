using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IActionHubRepository
    {
        Task<Profile> CompanyExistsAsync(uint profileId);
        Task<BusinessUnit> UnitExistsAsync(uint unitId);
        Task<ActionEvent?> GetByIdAsync(int id);
        Task<ActionEvent> UpsertAsync(ActionEvent actionEvent);
        Task<ActionEvent> UpdateAsync(ActionEvent actionEvent);
        Task<List<ActionHub>> GetActionEventListAsync();
        Task<List<ActionHub>> GetActionHubWithEventsAsync(int id, string type);
        Task<List<ActionHubSummaryRowDto>> GetActionHubSummaryRowsAsync(int unitId, string? type);
        Task<ActionHub?> GetByIdActionHubAsync(int id);
        Task<ActionHub> UpsertActionHubAsync(ActionHub actionHub);
        Task<ActionHub> UpdateActionHubAsync(ActionHub actionHub);
        Task<List<ActionEvent>> GetActionEventsByHubIdAsync(int actionHubId);
        Task UpdateActionEventsAsync(IEnumerable<ActionEvent> events);
        Task<ActionHub?> GetActionHubByIdAsync(int id);
        Task<string?> GetWorksheetFileByReportId(long reportId);
        Task<TopAction?> GetTopActionMatchAsync(long unitId, string actionTitle);

    }
}
