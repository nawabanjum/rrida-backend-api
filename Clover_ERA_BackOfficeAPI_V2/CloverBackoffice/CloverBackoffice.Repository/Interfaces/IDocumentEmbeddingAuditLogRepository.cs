using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IDocumentEmbeddingAuditLogRepository
    {
        Task<DocumentEmbeddingAuditLog> AddAsync(DocumentEmbeddingAuditLog entity);
    }
}
