using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [AllowAnonymous]
    [Route("api/embedding")]
    [ApiController]
    public class EmbeddingController : ControllerBase
    {
        private readonly IEmbeddingService _embeddingService;

        public EmbeddingController(IEmbeddingService embeddingService)
        {
            _embeddingService = embeddingService;
        }

        [HttpPost("open-ai")]
        public async Task<IActionResult> Generate([FromBody] List<string> inputs)
        {
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(inputs);
            return Ok(embeddings);
        }
    }
}