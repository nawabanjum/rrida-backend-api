using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class PromptTemplateRepository : IPromptTemplateRepository
    {
        private readonly ApplicationDbContext _context;

        public PromptTemplateRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PromptTemplate?> GetByIdAsync(int id, CancellationToken ct)
        {
            return await _context.PromptTemplates
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDel, ct);
        }

        public async Task<PromptTemplate?> GetActiveByNameAsync(string name, CancellationToken ct)
        {
            return await _context.PromptTemplates
                .FirstOrDefaultAsync(x => x.Name == name && x.IsActive && !x.IsDel, ct);
        }

        public async Task<PromptTemplate?> GetByNameAndVersionAsync(string name, string version, CancellationToken ct)
        {
            return await _context.PromptTemplates
                .FirstOrDefaultAsync(x => x.Name == name && x.Version == version && !x.IsDel, ct);
        }

        public async Task<List<PromptTemplate>> GetVersionsByNameAsync(string name, CancellationToken ct)
        {
            return await _context.PromptTemplates
                .Where(x => x.Name == name && !x.IsDel)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<List<PromptTemplate>> GetAllActiveAsync(CancellationToken ct)
        {
            return await _context.PromptTemplates
                .Where(x => x.IsActive && !x.IsDel)
                .ToListAsync(ct);
        }

        public async Task AddAsync(PromptTemplate entity)
        {
            await _context.PromptTemplates.AddAsync(entity);
        }

        public async Task DeactivateAllByNameAsync(string name, CancellationToken ct)
        {
            var actives = await _context.PromptTemplates
                .Where(x => x.Name == name && x.IsActive && !x.IsDel)
                .ToListAsync(ct);

            foreach (var p in actives)
                p.IsActive = false;
        }

        public async Task SoftDeletePromptAsync(int id, CancellationToken ct)
        {
            var entity = await _context.PromptTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity != null)
                entity.IsDel = true;
        }

        public async Task SaveChangesAsync(CancellationToken ct)
        {
            await _context.SaveChangesAsync(ct);
        }

        // ── Test Cases ──

        public async Task<List<TestCase>> GetTestCasesByIdsAsync(List<int> ids, CancellationToken ct)
        {
            return await _context.TestCases
                .Where(t => ids.Contains(t.Id) && !t.IsDel)
                .ToListAsync(ct);
        }

        public async Task<TestCase?> GetTestCaseByIdAsync(int id, CancellationToken ct)
        {
            return await _context.TestCases
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDel, ct);
        }

        public async Task<List<TestCase>> GetTestCasesByPromptNameAsync(string promptName, CancellationToken ct)
        {
            return await _context.TestCases
                .Where(x => x.PromptTemplateName == promptName && !x.IsDel)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<List<TestCase>> GetAllTestCasesAsync(CancellationToken ct)
        {
            return await _context.TestCases
                .Where(x => !x.IsDel)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task AddTestCaseAsync(TestCase entity)
        {
            await _context.TestCases.AddAsync(entity);
        }

        public async Task SoftDeleteTestCaseAsync(int id, CancellationToken ct)
        {
            var entity = await _context.TestCases.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity != null)
                entity.IsDel = true;
        }

        // ── Test Case Assertions ──

        public async Task AddAssertionsAsync(List<TestCaseAssertion> entities)
        {
            await _context.TestCaseAssertions.AddRangeAsync(entities);
        }

        public async Task DeleteAssertionsByTestCaseIdAsync(int testCaseId, CancellationToken ct)
        {
            var assertions = await _context.TestCaseAssertions
                .Where(x => x.TestCaseId == testCaseId && !x.IsDel)
                .ToListAsync(ct);

            foreach (var a in assertions)
                a.IsDel = true;
        }

        public async Task<List<TestCaseAssertion>> GetAssertionsByTestCaseIdAsync(int testCaseId, CancellationToken ct)
        {
            return await _context.TestCaseAssertions
                .Where(x => x.TestCaseId == testCaseId && !x.IsDel)
                .ToListAsync(ct);
        }

        public async Task<int> GetAssertionCountByTestCaseIdAsync(int testCaseId, CancellationToken ct)
        {
            return await _context.TestCaseAssertions
                .CountAsync(x => x.TestCaseId == testCaseId && !x.IsDel, ct);
        }
    }
}
