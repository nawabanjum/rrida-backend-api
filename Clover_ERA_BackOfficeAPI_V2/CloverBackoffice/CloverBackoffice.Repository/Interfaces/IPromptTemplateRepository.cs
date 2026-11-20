using CloverBackoffice.Infrastructure.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IPromptTemplateRepository
    {
        // Prompt Templates
        Task<PromptTemplate?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<PromptTemplate?> GetActiveByNameAsync(string name, CancellationToken ct = default);
        Task<PromptTemplate?> GetByNameAndVersionAsync(string name, string version, CancellationToken ct = default);
        Task<List<PromptTemplate>> GetVersionsByNameAsync(string name, CancellationToken ct = default);
        Task<List<PromptTemplate>> GetAllActiveAsync(CancellationToken ct = default);
        Task AddAsync(PromptTemplate entity);
        Task DeactivateAllByNameAsync(string name, CancellationToken ct = default);
        Task SoftDeletePromptAsync(int id, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);

        // Test Cases
        Task<List<TestCase>> GetTestCasesByIdsAsync(List<int> ids, CancellationToken ct = default);
        Task<TestCase?> GetTestCaseByIdAsync(int id, CancellationToken ct = default);
        Task<List<TestCase>> GetTestCasesByPromptNameAsync(string promptName, CancellationToken ct = default);
        Task<List<TestCase>> GetAllTestCasesAsync(CancellationToken ct = default);
        Task AddTestCaseAsync(TestCase entity);
        Task SoftDeleteTestCaseAsync(int id, CancellationToken ct = default);

        // Test Case Assertions
        Task AddAssertionsAsync(List<TestCaseAssertion> entities);
        Task DeleteAssertionsByTestCaseIdAsync(int testCaseId, CancellationToken ct = default);
        Task<List<TestCaseAssertion>> GetAssertionsByTestCaseIdAsync(int testCaseId, CancellationToken ct = default);
        Task<int> GetAssertionCountByTestCaseIdAsync(int testCaseId, CancellationToken ct = default);
    }
}
