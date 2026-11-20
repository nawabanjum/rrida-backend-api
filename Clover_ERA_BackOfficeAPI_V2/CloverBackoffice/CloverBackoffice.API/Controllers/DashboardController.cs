using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/dashboard")]
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IFeedbackAnalysisService _service;
        private readonly IQuestionService _questionService;
        private readonly ITokenReaderService _tokenReader;
        private readonly ApplicationDbContext _context;
        private readonly ILicensedPeriodService _licensedPeriod;
        public DashboardController(IFeedbackAnalysisService service, ITokenReaderService tokenReader, ApplicationDbContext context, IQuestionService questionService, ILicensedPeriodService licensedPeriod)
        {
            _service = service;
            _tokenReader = tokenReader;
            _context = context;
            _questionService = questionService;
            _licensedPeriod = licensedPeriod;
        }

        [HttpGet("stats-analysis")]
        public async Task<IActionResult> AnalyzeFeedbackDaily([FromQuery] long unitId, [FromQuery] int days, bool customRange, [FromQuery] DateTimeOffset? fromDate, [FromQuery] DateTimeOffset? toDate)
        {
            try
            {
                // 1. UnitId required
                if (unitId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUnit));
                }

                // 2. Validation depending on customRange
                if (customRange)
                {
                    if (!fromDate.HasValue || !toDate.HasValue)
                    {
                        return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Warnings.DatesNotProvided));
                    }
                }
                else
                {
                    if (days <= 0)
                    {
                        return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Warnings.DaysNotProvided));
                    }
                }

                // 3. Check if Business Unit exists
                var unitExists = await _service.CheckBusinessUnitAsync(unitId);
                if (!unitExists)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BusinessUnitNotFound));
                }


                DashboardSummaryDto? result;

                if (customRange)
                {
                    // Run with date range
                    result = await _service.AnalyzeFeedbackRunTimeAsync(
                        unitId,
                        fromDate!.Value.UtcDateTime,
                        toDate!.Value.UtcDateTime
                    );
                }
                else
                {
                    DateTime toDate1 = DateTime.UtcNow;
                    DateTime fromDate1 = toDate1.AddDays(-days);
                    result = await _service.AnalyzeFeedbackRunTimeAsync(unitId, fromDate1, toDate1);
                }

                if (result == null)
                {
                    return NotFound(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.DashboardSummaryNotFound));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.FeedbackSummaryGenerated));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("top-insights")]
        public async Task<IActionResult> GetTopData([FromQuery] long unitId)
        {
            try
            {
                if (unitId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUnit));
                }

                var unitExists = await _service.CheckBusinessUnitAsync(unitId);
                if (!unitExists)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BusinessUnitNotFound));
                }

                var result = await _service.GetTopDataAsync(unitId);

                if (result == null || (!result.Recommendations.Any() && !result.Themes.Any()) && !result.Actions.Any())
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.TopInsightsNotFound));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.TopInsightsGenerated));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("dump-user")]
        public async Task<IActionResult> DumpUsers()
        {
            try
            {
                var result = await _service.DumpUsersAsync();

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.Userdump));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("create-report-manually")]
        public async Task<IActionResult> CreateReport([FromBody] LLMReportRequestDto dto)
        {
            try
            {
                // Extract user id from token
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid or missing token."));
                }

                // Get user from DB
                var user = await _context.Users.AsNoTracking()
                                               .FirstOrDefaultAsync(u => u.UserId == userId);
                if (user == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
                }

                // Authorize only RRIDA Admins
                if (user.UserGroupId != (int)UserGroups.RridaAdmin)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.AuthorizeFailReports));
                }

                // Validate request body
                if (dto == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidRequest));
                }

                if (dto.UnitId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidUnit));
                }

                var unitExists = await _service.CheckBusinessUnitAsync(dto.UnitId);
                if (!unitExists)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.BusinessUnitNotFound));
                }

                // Only units that currently hold an active license (purchased
                // assignment or complimentary grant) may generate KPI reports.
                // Block generation up front for unlicensed company/unit selections.
                var licensedUnitIds = await _licensedPeriod.GetActivelyLicensedUnitIdsAsync(new[] { dto.UnitId });
                if (!licensedUnitIds.Contains(dto.UnitId))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UnitNotLicensedForReport));
                }

                // Check worksheet file only if user provided a value
                if (!string.IsNullOrWhiteSpace(dto.WorksheetFile))
                {
                    var fileExists = await _service.CheckWorksheetFileExistsAsync(dto.WorksheetFile.Trim());
                    if (!fileExists)
                    {
                        return Ok(ApiResponseHandler.Fail<string>("Worksheet file not found."));
                    }
                }

                // Continue and create report
                // Create report
                var report = await _service.CreateReportAsync(dto);
                return Ok(ApiResponseHandler.Success(report, ResponseMessages.Success.ReportsSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("questions")]
        public async Task<IActionResult> GetQuestionList()
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }
                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
                if (user == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.UserNotFound));
                }

                // Authorize only RRIDA Admins
                if (user.UserGroupId != (int)UserGroups.RridaAdmin)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.AuthorizeFailReports));
                }

                var resp = await _service.GetQuestionListAsync();

                return Ok(ApiResponseHandler.Success(resp, ResponseMessages.Success.QuestionDataFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        //[HttpPost("add-daily-question")]
        //public async Task<IActionResult> AddDailyQuestion([FromBody] CreateDailyQuestionDto dto)
        //{
        //    try
        //    {
        //        if (dto == null)
        //        {
        //            return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidRequest));
        //        }

        //        // Additional validation
        //        if (string.IsNullOrWhiteSpace(dto.Question))
        //        {
        //            return Ok(ApiResponseHandler.Fail<string>("Question text is required."));
        //        }

        //        if (string.IsNullOrWhiteSpace(dto.Category))
        //        {
        //            return Ok(ApiResponseHandler.Fail<string>("Category is required."));
        //        }

        //        // At least one reason (positive or negative) must be provided
        //        if (string.IsNullOrWhiteSpace(dto.PositiveReasons) && string.IsNullOrWhiteSpace(dto.NegativeReasons))
        //        {
        //            return Ok(ApiResponseHandler.Fail<string>("At least one reason (positive or negative) must be provided."));
        //        }

        //        var result = await _questionService.AddDailyQuestionAsync(dto);

        //        if (result)
        //        {
        //            return Ok(ApiResponseHandler.Success(result, "Question added successfully"));
        //        }

        //        return Ok(ApiResponseHandler.Fail<string>("Failed to add question"));
        //    }
        //    catch (ArgumentException ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>(ex.Message));
        //    }
        //    catch (InvalidOperationException ex)
        //    {
        //        // Include inner exception message if available for better debugging
        //        var errorMessage = ex.InnerException != null
        //            ? $"{ex.Message} - {ex.InnerException.Message}"
        //            : ex.Message;
        //        return Ok(ApiResponseHandler.Fail<string>(errorMessage));
        //    }
        //    catch (Exception ex)
        //    {
        //        // Log the full exception for debugging (you might want to use ILogger here)
        //        // In production, return generic message; in development, include details
        //        var errorMessage = ResponseMessages.Errors.ExceptionMsg;
        //        return StatusCode(500, ApiResponseHandler.Fail<string>(errorMessage));
        //    }
        //}

        [HttpPost("custom-add-question")]
        public async Task<IActionResult> CustomAddQuestion([FromBody] CustomAddQuestionDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));

                if (dto == null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RequestBodyEmpty));

                // Validations...
                if (string.IsNullOrWhiteSpace(dto.QuestionText))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.QuestionTextReq));

                if (string.IsNullOrWhiteSpace(dto.CloverCategory))
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.CloverCategoryReq));

                // Call service
                var result = await _questionService.AddCustomQuestionAsync(dto);

                if (result == null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.QuestionAlreadyExist));

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.QuestionSuccess));
            }
            catch
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("get-question-info-by-id")]
        public async Task<IActionResult> GetQuestionInfoById([FromQuery] long questionId)
        {
            try
            {
                if (questionId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid question ID."));
                }

                var result = await _questionService.GetQuestionInfoByIdAsync(questionId);

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

        [HttpPut("update-question-info")]
        public async Task<IActionResult> UpdateQuestionInfoById([FromBody] UpdateQuestionInfoDto dto)
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

                if (dto.Specification == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>("Specification is required."));
                }

                if (string.IsNullOrWhiteSpace(dto.Specification.CloverCategory))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.CloverCategoryReq));
                }

                var result = await _questionService.UpdateQuestionInfoByIdAsync(dto);

                if (!result.IsSuccess)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.ErrorMessage ?? "Update failed."));
                }

                return Ok(ApiResponseHandler.Success(result.QuestionInfo, ResponseMessages.Success.QuestionUpdated));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>($"An unexpected error occurred: {ex.Message}"));
            }
        }

        [HttpPost("update-reasons-microactions-by-questions-from-excel")]
        public async Task<IActionResult> UpdateQuestionsFromExcel(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileUploadError));
                }

                var fileExtension = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
                if (fileExtension != ".xlsx")
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid file format. Only XLSX files are supported for this operation."));
                }

                var result = await _questionService.UpdateQuestionsFromExcelAsync(file);

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
                      $"{result.UpdatedPositiveActions} positive action(s), {result.UpdatedNegativeActions} negative action(s) modified."
                    : "No changes detected. All records are already up to date.";

                return Ok(ApiResponseHandler.Success(result, message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("import-questions")]
        public async Task<IActionResult> ImportQuestions(IFormFile file)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileUploadError));
                }

                var fileExtension = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
                if (fileExtension != ".csv" && fileExtension != ".xlsx")
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid file format. Only CSV and XLSX files are supported."));
                }

                // Call service
                var result = await _questionService.ImportQuestionsAsync(file);

                // Check if validation failed (file format issue)
                if (result.Results.Any() && result.Results[0].RowNumber == 0 && 
                    (result.Results[0].Message.Contains("Invalid file format") || 
                     result.Results[0].Message.Contains("required columns")))
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.Results[0].Message));
                }

                if (result.TotalRows == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("No data rows found in the file."));
                }

                // Create summary response with error details for failed rows
                var summary = new ImportQuestionSummaryDto
                {
                    TotalRows = result.TotalRows,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    Errors = result.Results
                        .Where(r => !r.IsSuccess)
                        .Select(r => new ImportErrorDetailDto
                        {
                            RowNumber = r.RowNumber,
                            Message = r.Message,
                            QuestionText = r.QuestionText
                        })
                        .ToList()
                };

                // Determine message and success status based on results
                string message;
                bool isSuccess;

                if (result.FailureCount == 0)
                {
                    // Case 1: Full success
                    message = "All questions imported successfully.";
                    isSuccess = true;
                }
                else if (result.SuccessCount > 0)
                {
                    // Case 2: Partial success
                    message = "Import completed with partial success. Some questions could not be imported.";
                    isSuccess = true;
                }
                else
                {
                    // Case 3: Complete failure
                    message = "Import failed. No questions were imported.";
                    isSuccess = false;
                }

                if (isSuccess)
                {
                    return Ok(ApiResponseHandler.Success(summary, message));
                }
                else
                {
                    return Ok(ApiResponseHandler.Fail(summary, message));
                }
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

    }
}
