using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class ActionHubService : IActionHubService
    {
        private readonly IActionHubRepository _repository;
        public ActionHubService(IActionHubRepository repository)
        {
            _repository = repository;
        }
        public async Task<UpsertEventDto> UpsertAsync(UpsertEventDto dto)
        {
            // Validate Type
            if (string.IsNullOrWhiteSpace(dto.Type) ||
                !(dto.Type.Equals("manually", StringComparison.OrdinalIgnoreCase) || dto.Type.Equals("automatically", StringComparison.OrdinalIgnoreCase)))
            {
                dto.message = "Invalid Type. Please enter either 'manually' or 'automatically'.";
                return dto;
            }

            //  Validate ActionHubId
            var actionHub = await _repository.GetActionHubByIdAsync(dto.ActionHubId);
            if (actionHub == null)
            {
                dto.message = "Action Hub is not valid.";
                return dto;
            }
            actionHub.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateActionHubAsync(actionHub);

            if (dto.Id == 0)
            {
                // Create
                var entity = new ActionEvent
                {
                    DateAddedToAction = dto.DateAddedToAction,
                    ScheduledEventActivity = dto.ScheduledEventActivity,
                    DateImplemented = dto.DateImplemented,
                    Feedback = dto.Feedback,
                    Attachment = string.Empty,
                    Type = dto.Type.ToLower(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = dto.CreatedBy,
                    ActionHubId = dto.ActionHubId
                };

                await _repository.UpsertAsync(entity);
                dto.Id = entity.Id;
                dto.message = "Action Event created successfully.";
            }
            else
            {
                // Update
                var existing = await _repository.GetByIdAsync(dto.Id);
                if (existing == null)
                {
                    dto.message = "Action Event not found.";
                    return dto;
                }

                existing.DateAddedToAction = dto.DateAddedToAction;
                existing.ScheduledEventActivity = dto.ScheduledEventActivity;
                existing.DateImplemented = dto.DateImplemented;
                existing.Feedback = dto.Feedback;
                existing.Attachment = string.Empty;
                existing.ActionHubId = dto.ActionHubId;
                existing.Type = dto.Type.ToLower();

                await _repository.UpdateAsync(existing);
                dto.message = "Action Event updated successfully.";
            }

            return dto;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null || entity.IsDel)
                return false;
            var existing = await _repository.GetByIdActionHubAsync(entity.ActionHub.Id);
            if (existing == null)
            {
                return false;
            }
            existing.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateActionHubAsync(existing);

            entity.IsDel = true;
            await _repository.UpdateAsync(entity);

            return true;
        }

        public async Task<List<ActionHubWithEventsDto>> GetActionHubWithEventsAsync(int id, string type)
        {
            var normalizedType = type?.Trim().ToLower();

            List<ActionHub> hubWithEvents = new List<ActionHub>();

            // Filter by Type
            if (normalizedType == "manually" || normalizedType == "automatically")
            {
                var formattedType = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalizedType);
                hubWithEvents = await _repository.GetActionHubWithEventsAsync(id, formattedType);
            }
            else
            {
                hubWithEvents = await _repository.GetActionHubWithEventsAsync(id, null);
            }

            if (hubWithEvents == null || !hubWithEvents.Any())
                throw new ArgumentException($"No records found for Id {id}.");

            var result = new List<ActionHubWithEventsDto>();

            foreach (var hub in hubWithEvents)
            {
                string? worksheetFile = null;

                // CASE 1: ALWAYS TRY DIRECT ReportId FIRST
                if (hub.ReportId.HasValue && hub.ReportId.Value > 0)
                {
                    worksheetFile = await _repository.GetWorksheetFileByReportId(hub.ReportId.Value);
                }


                // CASE 2: RUN ONLY IF CASE 1 DID NOT FIND A WORKSHEET FILE
                if (worksheetFile == null)
                {
                    var actionTitle = hub.ActionTitle;
                    var unitId = hub.UnitId;

                    // Try to find matching TopAction
                    var topAction = await _repository.GetTopActionMatchAsync(unitId, actionTitle);

                    // If TopAction found and has a ReportId
                    if (topAction != null && topAction.ReportId > 0)
                    {
                        worksheetFile = await _repository.GetWorksheetFileByReportId(topAction.ReportId);
                    }
                }

                // Embed worksheet links in description
                var linkedDescription = EmbedWorksheetLinks(hub.Description);

                // Prepare DTO
                result.Add(new ActionHubWithEventsDto
                {
                    Id = hub.Id,
                    ActionTitle = hub.ActionTitle,
                    Description = linkedDescription,
                    ActionType = hub.Type,
                    CreatedDate = hub.CreatedAt,
                    UpdatedAt = hub.UpdatedAt,
                    WorksheetFile = worksheetFile,
                    ActionEvents = hub.ActionEvents?
                        .Where(e => !e.IsDel)
                        .Select(e => new ActionEventDto
                        {
                            Id = e.Id,
                            ScheduledEvent = e.ScheduledEventActivity,
                            DateImplemented = e.DateImplemented,
                            Feedback = e.Feedback
                        }).ToList()
                });
            }

            return result;
        }

        public async Task<ActionHubSummaryDto> GetActionHubSummaryAsync(int unitId, string? type)
        {
            var filterType = NormalizeActionType(type);

            var rows = await _repository.GetActionHubSummaryRowsAsync(unitId, filterType);

            var summary = new ActionHubSummaryDto
            {
                TotalActions = rows.Count,
                TotalActionsLabel = $"{rows.Count} Action Hub record{(rows.Count == 1 ? string.Empty : "s")}"
            };

            if (!rows.Any())
                return summary;

            // Card 2: most recently created action
            var lastCreated = rows
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .First();

            summary.LastAction = new ActionHubLastActionDto
            {
                Id = lastCreated.Id,
                Title = lastCreated.ActionTitle,
                ActionType = lastCreated.Type,
                CreatedDate = lastCreated.CreatedAt
            };

            // Card 3: most recently updated action (null when nothing was ever modified)
            var lastUpdated = rows
                .Where(x => x.UpdatedAt.HasValue)
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();

            if (lastUpdated != null)
            {
                summary.LastUpdatedAction = new ActionHubLastUpdatedActionDto
                {
                    Id = lastUpdated.Id,
                    Title = lastUpdated.ActionTitle,
                    ActionType = lastUpdated.Type,
                    UpdatedDate = lastUpdated.UpdatedAt.Value
                };
            }

            return summary;
        }

        // "all"/empty => no filter. Accepts both the UI labels and the stored values.
        private static string? NormalizeActionType(string? type)
        {
            var normalizedType = type?.Trim().ToLower();

            if (string.IsNullOrEmpty(normalizedType) || normalizedType == "all")
                return null;

            if (normalizedType == "manually" || normalizedType == "manual")
                return "manually";

            if (normalizedType == "automatically" || normalizedType == "automatic" || normalizedType == "auto")
                return "automatically";

            throw new ArgumentException("Invalid Type. Please enter either 'manually', 'automatically' or 'all'.");
        }

        private static string EmbedWorksheetLinks(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return description;

            // Match patterns like "Reflection 1", "Communication 2", "Learning 3", etc.
            var regex = new Regex(@"(Communication|Learning|Opportunit(?:y|ies)|Vulnerability|Enablement|Reflection)\s+(\d+)", RegexOptions.IgnoreCase);

            return regex.Replace(description, match =>
            {
                var category = match.Groups[1].Value;
                var number = match.Groups[2].Value;

                // Map category to its initial: Communication→C, Learning→L, Opportunity→O, etc.
                var initial = category[0].ToString().ToUpper();
                if (category.StartsWith("Opportunit", StringComparison.OrdinalIgnoreCase))
                    initial = "O";

                var fileName = $"Worksheet {initial}{number}.pdf";
                var apiUrl = $"/api/rag/get-worksheet-file?filename={Uri.EscapeDataString(fileName)}";

                return $"<a href=\"{apiUrl}\" target=\"_blank\">{match.Value}</a>";
            });
        }

        public async Task<UpsertActiontHubDto> UpsertActionHubAsync(UpsertActiontHubDto dto)
        {
            // Validate Type
            if (string.IsNullOrWhiteSpace(dto.Type) ||
                !(dto.Type.Equals("manually", StringComparison.OrdinalIgnoreCase) || dto.Type.Equals("automatically", StringComparison.OrdinalIgnoreCase)))
            {
                dto.message = "Invalid Type. Please enter either 'manually' or 'automatically'.";
                return dto;
            }

            // Validate Company
            var company = await _repository.CompanyExistsAsync(dto.ProfileId);
            if (company == null)
            {
                dto.message = "Invalid ProfileId";
                return dto;
            }

            // Validate Business Unit
            var businessunit = await _repository.UnitExistsAsync(dto.UnitId);
            if (businessunit == null)
            {
                dto.message = "Invalid BusinessUnitId";
                return dto;
            }

            if (dto.Id == 0)
            {
                // Create
                var entity = new ActionHub
                {
                    UnitId = dto.UnitId,
                    ProfileId = dto.ProfileId,
                    ActionTitle = dto.ActionTitle,
                    Description = dto.Description,
                    ReportId = dto.ReportId,
                    Type = dto.Type.ToLower(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = dto.CreatedBy
                };

                await _repository.UpsertActionHubAsync(entity);
                dto.Id = entity.Id;
                dto.message = "Action Hub created successfully.";
            }
            else
            {
                // Update
                var existing = await _repository.GetByIdActionHubAsync(dto.Id);
                if (existing == null)
                {
                    dto.message = "Action Hub not found.";
                    return dto;
                }

                existing.UnitId = dto.UnitId;
                existing.ProfileId = dto.ProfileId;
                existing.ActionTitle = dto.ActionTitle;
                existing.Description = dto.Description;
                existing.Type = dto.Type.ToLower();
                existing.UpdatedAt = DateTime.UtcNow;

                await _repository.UpdateActionHubAsync(existing);
                dto.message = "Action Hub updated successfully.";
            }

            return dto;
        }

        public async Task<bool> DeleteActionHubAsync(int id)
        {
            var entity = await _repository.GetByIdActionHubAsync(id);
            if (entity == null || entity.IsDel)
                return false;

            // Soft delete ActionHub
            entity.IsDel = true;
            entity.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateActionHubAsync(entity);

            // Fetch and soft delete related ActionEvents
            var relatedEvents = await _repository.GetActionEventsByHubIdAsync(id);
            if (relatedEvents != null && relatedEvents.Any())
            {
                foreach (var evt in relatedEvents)
                {
                    evt.IsDel = true;
                }
                await _repository.UpdateActionEventsAsync(relatedEvents);
            }

            return true;
        }
    }
}
