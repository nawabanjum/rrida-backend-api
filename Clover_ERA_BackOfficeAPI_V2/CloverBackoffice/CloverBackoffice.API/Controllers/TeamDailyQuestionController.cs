using CloverBackoffice.API.Filters;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Manages the Team Daily Questions bank: list, add, read, edit, import from the RRIDA
    /// Team Conditions workbook, re-apply a corrected workbook, delete, and report.
    ///
    /// The endpoints mirror the legacy daily-question endpoints on DashboardController one for
    /// one, so the back office screens transfer across with the paths and payload names changed
    /// and nothing else. Team questions live on their own route rather than being added to the
    /// dashboard, because they are a separate bank with a separate rotation.
    /// </summary>
    [Route("api/team-daily-question")]
    [ApiController]
    [Authorize]
    public class TeamDailyQuestionController : ControllerBase
    {
        private readonly ITeamQuestionService _teamQuestionService;
        private readonly ICustomQuestionService _customQuestionService;
        private readonly ITokenReaderService _tokenReader;
        private readonly ApplicationDbContext _context;

        public TeamDailyQuestionController(
            ITeamQuestionService teamQuestionService,
            ICustomQuestionService customQuestionService,
            ITokenReaderService tokenReader,
            ApplicationDbContext context)
        {
            _teamQuestionService = teamQuestionService;
            _customQuestionService = customQuestionService;
            _tokenReader = tokenReader;
            _context = context;
        }

        /// <summary>
        /// The whole team question bank, ordered by category, then condition, then rotation
        /// position. Pass <c>categoryId</c> to narrow it to one category — 0 is a real filter
        /// and returns the general questions; omitting it returns every category.
        /// Restricted to Clover ERA administrators, as the legacy question list is.
        /// </summary>
        [HttpPost("questions")]
        public async Task<IActionResult> GetTeamQuestionList([FromQuery] long? categoryId = null)
        {
            try
            {
                var denied = await CheckAdminAsync();
                if (denied != null)
                {
                    return denied;
                }

                var result = await _teamQuestionService.GetTeamQuestionListAsync(categoryId);

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.QuestionDataFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }


        // ── Categories ──────────────────────────────────────────────────────

        /// <summary>
        /// Every category, with the number of questions filed against each. Category 0
        /// ("General") is always present: it holds the questions served to everyone.
        /// </summary>
        [HttpGet("categories")]
        public async Task<IActionResult> GetTeamQuestionCategories([FromQuery] bool includeInactive = true)
        {
            try
            {
                var denied = await CheckAdminAsync();
                if (denied != null)
                {
                    return denied;
                }

                var result = await _teamQuestionService.GetTeamQuestionCategoriesAsync(includeInactive);

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.RecordFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>One category with its question count.</summary>
        [HttpGet("category-by-id")]
        public async Task<IActionResult> GetTeamQuestionCategoryById([FromQuery] long categoryId)
        {
            try
            {
                var denied = await CheckAdminAsync();
                if (denied != null)
                {
                    return denied;
                }

                var result = await _teamQuestionService.GetTeamQuestionCategoryByIdAsync(categoryId);

                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>("Category not found."));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.RecordFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Creates a category — an industry, a sector, a segment. Questions are then filed
        /// against the id this returns; questions meant for everyone keep CategoryId 0.
        /// </summary>
        [HttpPost("add-category")]
        public async Task<IActionResult> AddTeamQuestionCategory([FromBody] CreateTeamQuestionCategoryDto dto)
        {
            try
            {
                var denied = await CheckAdminAsync();
                if (denied != null)
                {
                    return denied;
                }

                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                var result = await _teamQuestionService.AddTeamQuestionCategoryAsync(dto, _tokenReader.GetUserIdFromToken());

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? "Failed to add the category."));
                }

                return Ok(ApiResponseHandler.Success(result.Category, "Category added successfully."));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Renames a category or changes its description / active flag. The seeded General
        /// category is refused: every uncategorised question points at it.
        /// </summary>
        [HttpPut("update-category")]
        public async Task<IActionResult> UpdateTeamQuestionCategory([FromBody] UpdateTeamQuestionCategoryDto dto)
        {
            try
            {
                var denied = await CheckAdminAsync();
                if (denied != null)
                {
                    return denied;
                }

                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                var result = await _teamQuestionService.UpdateTeamQuestionCategoryAsync(dto, _tokenReader.GetUserIdFromToken());

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? "Failed to update the category."));
                }

                return Ok(ApiResponseHandler.Success(result.Category, "Category updated successfully."));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Marks a category deleted. Refused while it still holds questions, and refused
        /// outright for the General category.
        /// </summary>
        [HttpDelete("delete-category")]
        public async Task<IActionResult> DeleteTeamQuestionCategory([FromQuery] long categoryId)
        {
            try
            {
                var denied = await CheckAdminAsync();
                if (denied != null)
                {
                    return denied;
                }

                var result = await _teamQuestionService.DeleteTeamQuestionCategoryAsync(categoryId, _tokenReader.GetUserIdFromToken());

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? "Failed to delete the category."));
                }

                return Ok(ApiResponseHandler.Success(result.Category, "Category deleted successfully."));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Adds one team question with its good / bad reasons and their micro-actions. The
        /// free-text ("Other") option is appended automatically when the payload omits it.
        /// </summary>
        [HttpPost("add-question")]
        public async Task<IActionResult> AddTeamQuestion([FromBody] AddTeamQuestionDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                if (string.IsNullOrWhiteSpace(dto.QuestionText))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.QuestionTextReq));
                }

                if (string.IsNullOrWhiteSpace(dto.TeamElement))
                {
                    return Ok(ApiResponseHandler.Fail<string>("TeamElement is required."));
                }

                if (string.IsNullOrWhiteSpace(dto.SubCategory))
                {
                    return Ok(ApiResponseHandler.Fail<string>("SubCategory is required."));
                }

                var hasReason =
                    (dto.PositiveReasons?.Any(r => !string.IsNullOrWhiteSpace(r?.Text)) ?? false) ||
                    (dto.NegativeReasons?.Any(r => !string.IsNullOrWhiteSpace(r?.Text)) ?? false);

                if (!hasReason)
                {
                    return Ok(ApiResponseHandler.Fail<string>("At least one reason (good or bad) must be provided."));
                }

                var result = await _teamQuestionService.AddTeamQuestionAsync(dto);

                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.QuestionAlreadyExist));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.QuestionSuccess));
            }
            catch (ArgumentException ex)
            {
                // Unrecognised Team Element / Sub-category / Variant — the caller's problem to fix.
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                // Occupied rotation slot, or no reasons survived normalisation.
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Adds one CUSTOM question — a question a company authors for one of its business
        /// units, outside the two fixed banks.
        ///
        /// This does not write to TeamDailyQuestions. Custom questions live in their own
        /// CustomQuestions table with their own scheduling stamp and status, and every write
        /// is snapshotted into CustomQuestionAudits. It sits on this controller because it is
        /// driven from the same back office screen as the team question bank.
        ///
        /// <c>profileId</c> and <c>unitId</c> say who the question belongs to; the unit must
        /// belong to that company. Omitting <c>status</c> derives it from the stamp: a future
        /// <c>scheduledAt</c> is Scheduled, anything else is Draft.
        /// </summary>
        [HttpPost("add-custom-question")]
        public async Task<IActionResult> AddCustomQuestion([FromBody] CreateCustomQuestionDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                if (string.IsNullOrWhiteSpace(dto.QuestionText))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.QuestionTextReq));
                }

                if (dto.ProfileId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("ProfileId is required."));
                }

                if (dto.UnitId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("UnitId is required."));
                }

                // Recorded as the author. A token without a user id still writes the question;
                // the column is nullable and the audit row carries the same null.
                var userId = _tokenReader.GetUserIdFromToken();

                var result = await _customQuestionService.AddCustomQuestionAsync(dto, userId);

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? "Failed to add the custom question."));
                }

                return Ok(ApiResponseHandler.Success(result.Question, ResponseMessages.Success.QuestionSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Records one free-text answer to a custom question, into CustomQuestionDailyFeedback.
        ///
        /// A custom question has no reason set and no sentiment scale, so the answer is the
        /// text, plus <c>unitId</c> saying which business unit it came from. That unit must be
        /// the one that owns the question — an answer filed under any other unit is refused
        /// rather than stored, so the two can never disagree. The read endpoint returns
        /// <c>unitId</c> on every question, so the caller already holds the value to send back.
        ///
        /// The write is snapshotted into CustomQuestionDailyFeedbackAudits, unit included.
        ///
        /// MOBILE AUTH, not the back office login — the same static token the api/v1/*
        /// endpoints take (see the attributes below). CreatedBy therefore lands as null
        /// for an app submission: the token identifies the app, not a person, and the
        /// column has always been nullable. The unit on the row is what says where the
        /// answer came from.
        /// </summary>
        // Overrides the controller's [Authorize]: the app has no user login, so it
        // carries CloveraPersonalApp:ApiToken in X-Api-Key / Authorization: Bearer /
        // x-functions-key / ?code=, exactly as the api/v1/* endpoints do. The path is
        // also listed in TokenAuthentication.skipPaths — that middleware runs before MVC
        // filters and would reject the request first otherwise.
        [AllowAnonymous]
        [PersonalAppTokenAuthorize]
        [SkipTenantGuard]
        [HttpPost("add-custom-question-feedback")]
        public async Task<IActionResult> AddCustomQuestionFeedback([FromBody] CreateCustomQuestionFeedbackDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                if (dto.CustomQuestionId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("CustomQuestionId is required."));
                }

                if (dto.UnitId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("UnitId is required."));
                }

                var userId = _tokenReader.GetUserIdFromToken();

                var result = await _customQuestionService.AddCustomQuestionFeedbackAsync(dto, userId);

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? "Failed to record the answer."));
                }

                return Ok(ApiResponseHandler.Success(result.Feedback, ResponseMessages.Success.CustomQuestionFeedbackSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// The CUSTOM questions a business unit should be asked today.
        ///
        /// The unit is named by <c>unitId</c> or by <c>unitShortCode</c> — at least one is
        /// required, and supplying both requires them to agree, so a stale pairing is
        /// reported instead of quietly serving another team's questions. Those are the only
        /// inputs: the day is decided here in UTC, so a client with a wrong clock or in
        /// another time zone cannot ask for a different one.
        ///
        /// A question answers this endpoint only when it is scheduled inside today, is still
        /// live (Scheduled or Active — never Draft, Cancelled or Expired), and its validity
        /// period has not run out. A custom question carries one ScheduledAt rather than a
        /// from/to pair, so that period is the UTC day the stamp falls in and ends at the
        /// midnight after it; <c>validUntil</c> on each row states it, and <c>date</c> on the
        /// response says which day was served.
        ///
        /// Selecting on today's window is what enforces the validity period, so the answer
        /// stays correct whether or not CustomQuestionExpiryBackgroundService has swept yet.
        /// A unit with nothing due is a success carrying an empty list.
        ///
        /// MOBILE AUTH, not the back office login — the same static token the api/v1/*
        /// endpoints take (see the attributes below). The app asks for its own unit by
        /// id or short code and gets back what that unit should answer today.
        /// </summary>
        // Same reasoning as add-custom-question-feedback below: [AllowAnonymous] drops
        // the controller's JWT requirement, [PersonalAppTokenAuthorize] puts the app
        // token in its place, and the path is listed in TokenAuthentication.skipPaths so
        // the middleware lets it through to these filters.
        [AllowAnonymous]
        [PersonalAppTokenAuthorize]
        [SkipTenantGuard]
        [HttpGet("custom-questions")]
        public async Task<IActionResult> GetCustomQuestions(
            [FromQuery] long? unitId = null,
            [FromQuery] string? unitShortCode = null)
        {
            try
            {
                if ((!unitId.HasValue || unitId.Value <= 0) && string.IsNullOrWhiteSpace(unitShortCode))
                {
                    return Ok(ApiResponseHandler.Fail<string>("Provide either UnitId or UnitShortCode."));
                }

                var result = await _customQuestionService.GetScheduledCustomQuestionsAsync(unitId, unitShortCode);

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? ResponseMessages.Errors.BusinessUnitNotFound));
                }

                if (result.Data == null || result.Data.TotalCount == 0)
                {
                    return Ok(ApiResponseHandler.Success(result.Data, ResponseMessages.Errors.NoRecords));
                }

                return Ok(ApiResponseHandler.Success(result.Data, ResponseMessages.Success.QuestionDataFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>One question with its specification, reasons and micro-actions.</summary>
        [HttpGet("get-question-info-by-id")]
        public async Task<IActionResult> GetTeamQuestionInfoById([FromQuery] long questionId)
        {
            try
            {
                if (questionId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid question ID."));
                }

                var result = await _teamQuestionService.GetTeamQuestionInfoByIdAsync(questionId);

                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>("Question not found."));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.QuestionDataFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Edits a question in place. Reasons and micro-actions carrying an Id are updated,
        /// those without one are created, and those flagged <c>isDel</c> are removed — unless
        /// feedback has already been recorded against them, which is refused.
        /// </summary>
        [HttpPut("update-question-info")]
        public async Task<IActionResult> UpdateTeamQuestionInfoById([FromBody] UpdateTeamQuestionInfoDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));
                }

                if (dto.QuestionId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid question ID."));
                }

                if (string.IsNullOrWhiteSpace(dto.QuestionText))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.QuestionTextReq));
                }

                if (dto.Specification == null || dto.Specification.SpecificationId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("Specification is required."));
                }

                var result = await _teamQuestionService.UpdateTeamQuestionInfoByIdAsync(dto);

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? "Update failed."));
                }

                return Ok(ApiResponseHandler.Success(result.QuestionInfo, ResponseMessages.Success.QuestionUpdated));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Imports the RRIDA Team Conditions workbook. Each row becomes one question with its
        /// good / bad reasons, Team States and micro-actions; rows are reported individually so
        /// a single bad row does not cost the rest of the file.
        /// </summary>
        [HttpPost("import-questions")]
        public async Task<IActionResult> ImportTeamQuestions(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileUploadError));
                }

                var extension = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();

                if (extension != ".csv" && extension != ".xlsx")
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid file format. Only CSV and XLSX files are supported."));
                }

                var result = await _teamQuestionService.ImportTeamQuestionsAsync(file);

                // A row-zero result is a whole-file problem (bad headers, unreadable file);
                // there is no per-row detail worth returning in that case.
                var fileLevelError = result.Results.FirstOrDefault(r => r.RowNumber == 0 && !r.IsSuccess);

                if (fileLevelError != null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(fileLevelError.Message));
                }

                if (result.TotalRows == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("No data rows found in the file."));
                }

                var summary = new ImportTeamQuestionSummaryDto
                {
                    TotalRows = result.TotalRows,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    Errors = result.Results
                        .Where(r => !r.IsSuccess)
                        .Select(r => new ImportTeamQuestionErrorDto
                        {
                            RowNumber = r.RowNumber,
                            Message = r.Message,
                            QuestionText = r.QuestionText
                        })
                        .ToList()
                };

                if (result.FailureCount == 0)
                {
                    return Ok(ApiResponseHandler.Success(summary, "All team questions imported successfully."));
                }

                if (result.SuccessCount > 0)
                {
                    return Ok(ApiResponseHandler.Success(
                        summary,
                        "Import completed with partial success. Some questions could not be imported."));
                }

                return Ok(ApiResponseHandler.Fail(summary, "Import failed. No questions were imported."));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Re-applies a corrected workbook over questions already in the bank: reason wording,
        /// micro-action wording and Team State only. Rows whose question is not in the bank are
        /// skipped and reported — nothing is created or deleted.
        /// </summary>
        [HttpPost("update-reasons-microactions-by-questions-from-excel")]
        public async Task<IActionResult> UpdateTeamQuestionsFromExcel(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileUploadError));
                }

                if (!string.Equals(System.IO.Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid file format. Only XLSX files are supported for this operation."));
                }

                var result = await _teamQuestionService.UpdateTeamQuestionsFromExcelAsync(file);

                if (result.TotalRowsInExcel == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("No data rows found in the file."));
                }

                if (result.MatchedQuestions == 0)
                {
                    return Ok(ApiResponseHandler.Fail(result, "No matching questions found in the database."));
                }

                var message = result.UpdatedQuestions > 0
                    ? $"Update complete. {result.UpdatedQuestions} question(s) updated: " +
                      $"{result.UpdatedPositiveReasons} positive reason(s), {result.UpdatedNegativeReasons} negative reason(s), " +
                      $"{result.UpdatedPositiveActions} positive action(s), {result.UpdatedNegativeActions} negative action(s), " +
                      $"{result.UpdatedTeamStates} team state(s) modified."
                    : "No changes detected. All records are already up to date.";

                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// The state of the question bank after an import: totals, the spread across the three
        /// groups and eight conditions, and the questions that are still missing something.
        /// </summary>
        [HttpGet("question-bank-report")]
        public async Task<IActionResult> GetTeamQuestionBankReport()
        {
            try
            {
                var denied = await CheckAdminAsync();
                if (denied != null)
                {
                    return denied;
                }

                var result = await _teamQuestionService.GetTeamQuestionBankReportAsync();

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.QuestionDataFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// The same Clover ERA administrator check the legacy question list applies. Returns
        /// the response to send when the caller is not allowed, or null when they are.
        /// </summary>
        private async Task<IActionResult?> CheckAdminAsync()
        {
            var userId = _tokenReader.GetUserIdFromToken();

            if (!userId.HasValue)
            {
                return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
            }

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
            }

            if (user.UserGroupId != (int)UserGroups.RridaAdmin)
            {
                return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.AuthorizeFailReports));
            }

            return null;
        }
    }
}
