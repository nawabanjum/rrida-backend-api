using CloverBackoffice.Infrastructure.DTO;
using Microsoft.AspNetCore.Http;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IDocumentEmbeddingService
    {
        Task<DocumentEmbeddingResponse> StoreAsync(StoreDocumentEmbeddingRequest request);
        Task<List<DocumentEmbeddingResponse>> SearchAsync(SearchDocumentEmbeddingRequest request);
        Task<DocumentEmbeddingResponse?> GetByIdAsync(int id);
        Task<DocumentEmbeddingByProcessResponse?> GetByProcessIdAsync(int processId);
        Task<List<DocumentEmbeddingListResponse>> GetAllAsync();
        Task<bool> DeleteAsync(int id);
        Task<bool> DeleteByProcessIdAsync(int processId);
        Task<UploadDocumentResponse> UploadFileAsync(IFormFile file, string? embeddingType);
        Task<UploadDocumentResponse> UpdateFileByProcessIdAsync(IFormFile file, int processId, string? embeddingType);
        Task<BulkUploadDocumentResponse> UploadZipFileAsync(IFormFile zipFile);
        Task<string> AnalyzeFeedbackPromptDataEmbedding(string startPoint, IFormFile file, string endpoint);
        Task<List<ImportPromptDataDto>> ImportPromptDataAsyncEmbedding(IFormFile file);

        // PromptConfiguration CRUD
        Task<PromptConfigurationResponse> UpsertPromptAsync(UpsertPromptConfigurationRequest request);
        Task<object> GetPromptsAsync(int? id);
        Task<bool> DeletePromptAsync(int id);

        // ModelConfiguration CRUD
        Task<ModelConfigurationResponse> UpsertModelAsync(UpsertModelConfigurationRequest request);
        Task<object> GetModelsAsync(int? id, int? modelConfigTypeId = null);
        Task<bool> DeleteModelAsync(int id);
    }
}
