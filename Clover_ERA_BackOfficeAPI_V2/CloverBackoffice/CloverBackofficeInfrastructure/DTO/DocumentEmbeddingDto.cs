namespace CloverBackoffice.Infrastructure.DTO
{
    public class StoreDocumentEmbeddingRequest
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public string SourceType { get; set; }
    }

    public class SearchDocumentEmbeddingRequest
    {
        public string Query { get; set; }
        public int TopK { get; set; } = 5;
    }

    public class DocumentEmbeddingResponse
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string SourceType { get; set; }
        public string? EmbeddingType { get; set; }
        public int ProcessId { get; set; }
        public double? Score { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UploadDocumentResponse
    {
        public string FileName { get; set; }
        public string SourceType { get; set; }
        public string? EmbeddingType { get; set; }
        public int ProcessId { get; set; }
        public int TotalChunks { get; set; }
        public List<int> ChunkIds { get; set; }
    }

    public class UpdateDocumentEmbeddingRequest
    {
        public int ProcessId { get; set; }
        public string? EmbeddingType { get; set; }
    }

    public class DocumentEmbeddingListResponse
    {
        public int ProcessId { get; set; }
        public string? FileName { get; set; }
        public string SourceType { get; set; }
        public string? EmbeddingType { get; set; }
        public int TotalChunks { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class DocumentEmbeddingByProcessResponse
    {
        public int ProcessId { get; set; }
        public string? FileName { get; set; }
        public string SourceType { get; set; }
        public string? EmbeddingType { get; set; }
        public int TotalChunks { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<DocumentEmbeddingChunkResponse> Chunks { get; set; }
    }

    public class DocumentEmbeddingChunkResponse
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public int? ChunkIndex { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class BulkUploadDocumentResponse
    {
        public int TotalFilesProcessed { get; set; }
        public int TotalChunksGenerated { get; set; }
        public List<UploadDocumentResponse> FileResults { get; set; }
        public List<string> SkippedFiles { get; set; }
    }
}
