using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LiteLLMDebugController : ControllerBase
    {
        private readonly ILiteLLMService _litellmService;

        public LiteLLMDebugController(ILiteLLMService litellmService)
        {
            _litellmService = litellmService;
        }

        [HttpGet("debug")]
        public async Task<IActionResult> Debug()
        {
            var debugInfo = await _litellmService.DebugRequestAsync();
            return Ok(new { debug = debugInfo });
        }

        [HttpPost("test")]
        public async Task<IActionResult> Test()
        {
            try
            {
                var response = await _litellmService.GetChatCompletionAsync(
                    "You are a helpful assistant.",
                    "Hello, this is a test from C# API");

                return Ok(new
                {
                    success = true,
                    response = response,
                    message = "Test completed successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }
    }
}
