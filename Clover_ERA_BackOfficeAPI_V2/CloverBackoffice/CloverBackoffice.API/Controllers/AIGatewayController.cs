using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Authorize]
    [Route("api/ai-gateway")]
    [ApiController]
    public class AIGatewayController : ControllerBase
    {
        private readonly IAIGatewayService _gateway;
        private readonly IPromptManagementService _promptService;
        private readonly IResponseValidatorService _validator;
        private readonly IAIRequestLogRepository _logRepo;

        public AIGatewayController(
            IAIGatewayService gateway,
            IPromptManagementService promptService,
            IResponseValidatorService validator,
            IAIRequestLogRepository logRepo)
        {
            _gateway = gateway;
            _promptService = promptService;
            _validator = validator;
            _logRepo = logRepo;
        }

        /// <summary>Test the AI gateway with a prompt + user content</summary>
        [HttpPost("test")]
        public async Task<IActionResult> TestGateway([FromBody] AIGatewayTestRequestDto request)
        {
            var prompt = await _promptService.GetActivePromptAsync(request.PromptName ?? "clover-era-insights");
            var system = prompt?.SystemPrompt ?? "You are a JSON API.";

            var response = await _gateway.ChatCompletionWithJsonAsync(
                system, request.UserContent,
                new AIGatewayRequestDto { Model = request.Model });

            ValidationResultDto? validation = null;
            if (request.ValidateResponse && response.Success)
                validation = _validator.Validate(response.Content, system);

            return Ok(ApiResponseHandler.Success(
                new { response, validation },
                "Gateway test completed."));
        }

        /// <summary>Get recent AI request logs</summary>
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs([FromQuery] int limit = 50)
        {
            var logs = await _logRepo.GetRecentAsync(limit);
            return Ok(ApiResponseHandler.Success(logs, "Logs retrieved."));
        }

        /// <summary>Validate a raw JSON response against the 7 assertions</summary>
        [HttpPost("validate")]
        public IActionResult ValidateResponse([FromBody] string rawJson)
        {
            var result = _validator.Validate(rawJson);
            return Ok(ApiResponseHandler.Success(result, "Validation completed."));
        }
    }
}
