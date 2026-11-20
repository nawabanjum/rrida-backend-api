using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentAuditLogRepository _auditLog;

        public PaymentRepository(ApplicationDbContext context, IPaymentAuditLogRepository auditLog)
        {
            _context = context;
            _auditLog = auditLog;
        }

        public async Task<Payment> CreateAsync(Payment payment)
        {
            payment.CreatedAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;
            await _context.Payments.AddAsync(payment);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(payment, "INSERT");
            return payment;
        }

        public async Task<Payment> UpdateAsync(Payment payment)
        {
            payment.UpdatedAt = DateTime.UtcNow;
            _context.Payments.Update(payment);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(payment, "UPDATE");
            return payment;
        }

        public async Task<Payment?> GetByIdAsync(int id)
        {
            return await _context.Payments.FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<List<Payment>> GetByProfileIdAsync(uint profileId, int page, int pageSize)
        {
            return await _context.Payments
                .Where(p => p.ProfileId == profileId)
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetTotalCountByProfileIdAsync(uint profileId)
        {
            return await _context.Payments
                .CountAsync(p => p.ProfileId == profileId);
        }

        public async Task<List<Payment>> GetByCompanySubscriptionIdAsync(int companySubscriptionId, int page, int pageSize)
        {
            return await _context.Payments
                .Where(p => p.CompanySubscriptionId == companySubscriptionId)
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetTotalCountByCompanySubscriptionIdAsync(int companySubscriptionId)
        {
            return await _context.Payments
                .CountAsync(p => p.CompanySubscriptionId == companySubscriptionId);
        }

        public async Task<bool> HasSucceededPaymentInPeriodAsync(int companySubscriptionId, DateTime periodStart, DateTime periodEnd)
        {
            return await _context.Payments
                .AnyAsync(p => p.CompanySubscriptionId == companySubscriptionId
                    && p.Status == PaymentStatus.Succeeded
                    && p.CreatedAt >= periodStart
                    && p.CreatedAt <= periodEnd);
        }

        public async Task<bool> HasPendingInvoiceForSubscriptionAsync(int companySubscriptionId)
        {
            return await _context.Payments
                .AnyAsync(p => p.CompanySubscriptionId == companySubscriptionId
                    && p.PaymentMethod == PaymentMethodType.Invoice
                    && p.Status == PaymentStatus.AwaitingVerification);
        }

        public async Task<List<Payment>> GetPendingInvoicesForSubscriptionAsync(int companySubscriptionId)
        {
            return await _context.Payments
                .Where(p => p.CompanySubscriptionId == companySubscriptionId
                    && p.PaymentMethod == PaymentMethodType.Invoice
                    && p.Status == PaymentStatus.AwaitingVerification)
                .ToListAsync();
        }
    }
}
