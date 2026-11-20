using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;

namespace CloverBackoffice.Repository.Implementation
{
    public class DocumentEmbeddingAuditLogRepository : IDocumentEmbeddingAuditLogRepository
    {
        private readonly ApplicationDbContext _context;

        public DocumentEmbeddingAuditLogRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DocumentEmbeddingAuditLog> AddAsync(DocumentEmbeddingAuditLog entity)
        {
            await _context.DocumentEmbeddingAuditLogs.AddAsync(entity);
            await _context.SaveChangesAsync();
            return entity;
        }
    }
}
