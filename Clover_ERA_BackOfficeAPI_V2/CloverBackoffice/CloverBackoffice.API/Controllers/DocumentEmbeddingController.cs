using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [AllowAnonymous]
    [Route("api/document-embeddings")]
    [ApiController]
    public class DocumentEmbeddingController : ControllerBase
    {
        private readonly IDocumentEmbeddingService _service;
        private readonly ITokenReaderService _tokenReader;

        public DocumentEmbeddingController(IDocumentEmbeddingService service, ITokenReaderService tokenReader)
        {
            _service = service;
            _tokenReader = tokenReader;
        }

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".xlsx"
        };

        private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

        [HttpPost("upload")]
        [RequestSizeLimit(524_288_000)] // Increased limit for Zip uploads
        [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
        public async Task<IActionResult> Upload(IFormFile file, [FromForm] string? embeddingType)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("No file was uploaded or file is empty."));
                }

                if (file.Length > 524_288_000) // check generic max limit first
                {
                    return Ok(ApiResponseHandler.Fail<string>("File size exceeds the maximum allowed size."));
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

                // Handle Zip File (Bulk)
                if (extension == ".zip")
                {
                    var result = await _service.UploadZipFileAsync(file);
                    return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
                }

                if (!AllowedExtensions.Contains(extension))
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid file type. Supported formats: PDF, DOCX, XLSX, ZIP."));
                }

                if (file.Length > 10 * 1024 * 1024)
                {
                    return Ok(ApiResponseHandler.Fail<string>("File size exceeds the maximum allowed size of 10 MB for single documents."));
                }

                var singleResult = await _service.UploadFileAsync(file, embeddingType);
                return Ok(ApiResponseHandler.Success(singleResult, ResponseMessages.Success.OperationSuccess));
            }
            catch (NotSupportedException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("store")]
        public async Task<IActionResult> Store([FromBody] StoreDocumentEmbeddingRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));
                }

                var result = await _service.StoreAsync(request);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] SearchDocumentEmbeddingRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Query))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));
                }

                var results = await _service.SearchAsync(request);
                return Ok(ApiResponseHandler.Success(results, ResponseMessages.Success.RecordFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("process/{processId}")]
        public async Task<IActionResult> GetByProcessId(int processId)
        {
            try
            {
                var result = await _service.GetByProcessIdAsync(processId);
                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.RecordFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var results = await _service.GetAllAsync();
                return Ok(ApiResponseHandler.Success(results, ResponseMessages.Success.RecordFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPut("update")]
        [RequestSizeLimit(524_288_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
        public async Task<IActionResult> Update([FromForm] int processId, [FromForm] string? embeddingType, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>("No file was uploaded or file is empty."));
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (!AllowedExtensions.Contains(extension))
                {
                    return Ok(ApiResponseHandler.Fail<string>("Invalid file type. Supported formats: PDF, DOCX, XLSX."));
                }

                if (file.Length > MaxFileSize)
                {
                    return Ok(ApiResponseHandler.Fail<string>("File size exceeds the maximum allowed size of 10 MB."));
                }

                var result = await _service.UpdateFileByProcessIdAsync(file, processId, embeddingType);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (NotSupportedException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var result = await _service.DeleteAsync(id);
                if (!result)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpDelete("process/{processId}")]
        public async Task<IActionResult> DeleteByProcessId(int processId)
        {
            try
            {
                var result = await _service.DeleteByProcessIdAsync(processId);
                if (!result)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("upload-prompt-data-file-embedding")]
        public async Task<IActionResult> UploadPromptEmbedding([FromForm] string startPoint, IFormFile file, [FromForm] string endpoint)
        {
            if (file == null || file.Length == 0)
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.FileUploadError));

            var data = await _service.AnalyzeFeedbackPromptDataEmbedding(startPoint, file, endpoint);

            return Ok(ApiResponseHandler.Success(data, ResponseMessages.Success.ImportPromptData));
        }


        // Create or Update Prompt - if Id is provided, update; otherwise create
        [HttpPost("upsert-prompts")]
        public async Task<IActionResult> UpsertPrompt([FromBody] UpsertPromptConfigurationRequest request)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }

                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    return Ok(ApiResponseHandler.Fail<string>("Prompt text is required."));
                }

                // Ensure defaults and audit fields are handled by backend
                if (!request.Id.HasValue || request.Id.Value <= 0)
                {
                    // On create: if user didn't send IsDefault, default it to false
                    if (!request.IsDefault.HasValue)
                    {
                        request.IsDefault = false;
                    }
                    request.CreatedBy = userId.Value.ToString();
                    request.ModifiedBy = userId.Value.ToString();
                }
                else
                {
                    // On update: track modifier, ignore any incoming CreatedBy from client
                    request.ModifiedBy = userId.Value.ToString();
                }

                var result = await _service.UpsertPromptAsync(request);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (KeyNotFoundException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Get All Prompts or Get Single Prompt by Id
        [HttpGet("get-prompts")]
        public async Task<IActionResult> GetPrompts([FromQuery] int? id)
        {
            try
            {
                var result = await _service.GetPromptsAsync(id);
                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.RecordFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Soft Delete Prompt
        [HttpDelete("prompts/{id}")]
        public async Task<IActionResult> DeletePrompt(int id)
        {
            try
            {
                var result = await _service.DeletePromptAsync(id);
                if (!result)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // ModelConfiguration CRUD Endpoints

        // Create or Update Model - if Id is provided, update; otherwise create
        [HttpPost("upsert-models")]
        public async Task<IActionResult> UpsertModel([FromBody] UpsertModelConfigurationRequest request)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid token - UserId missing."));
                }

                if (string.IsNullOrWhiteSpace(request.ModelName))
                {
                    return Ok(ApiResponseHandler.Fail<string>("Model name is required."));
                }

                // Ensure defaults and audit fields are handled by backend
                if (!request.Id.HasValue || request.Id.Value <= 0)
                {
                    // On create: if user didn't send IsDefault, default it to false
                    if (!request.IsDefault.HasValue)
                    {
                        request.IsDefault = false;
                    }
                    request.CreatedBy = userId.Value.ToString();
                    request.ModifiedBy = userId.Value.ToString();
                }
                else
                {
                    // On update: track modifier, ignore any incoming CreatedBy from client
                    request.ModifiedBy = userId.Value.ToString();
                }

                var result = await _service.UpsertModelAsync(request);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (KeyNotFoundException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Get All Models or Get Single Model by Id
        [HttpGet("get-models")]
        public async Task<IActionResult> GetModels([FromQuery] int? id)
        {
            try
            {
                var result = await _service.GetModelsAsync(id, modelConfigTypeId: 1);
                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.RecordFound));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        // Soft Delete Model
        [HttpDelete("models/{id}")]
        public async Task<IActionResult> DeleteModel(int id)
        {
            try
            {
                var result = await _service.DeleteModelAsync(id);
                if (!result)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                return Ok(ApiResponseHandler.Success(true, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
