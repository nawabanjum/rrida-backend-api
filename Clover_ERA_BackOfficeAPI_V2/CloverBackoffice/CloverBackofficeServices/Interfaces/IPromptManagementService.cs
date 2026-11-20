using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IPromptManagementService
    {
        Task<PromptTemplate?> GetActivePromptAsync(string name, CancellationToken ct = default);
        Task<PromptTemplate?> GetPromptByVersionAsync(string name, string version, CancellationToken ct = default);
        Task<List<PromptTemplateResponseDto>> GetVersionsAsync(string name, CancellationToken ct = default);
        Task<List<PromptTemplateResponseDto>> GetAllActiveAsync(CancellationToken ct = default);
        Task<PromptTemplateResponseDto> UpsertPromptAsync(CreatePromptTemplateDto dto, CancellationToken ct = default);
        Task<bool> ActivateVersionAsync(string name, string version, CancellationToken ct = default);
        Task<bool> DeletePromptAsync(int id, CancellationToken ct = default);
        string RenderTemplate(string template, Dictionary<string, string> variables);

        // Test Cases
        Task<TestCaseResponseDto> UpsertTestCaseAsync(UpsertTestCaseRequest dto, CancellationToken ct = default);
        Task<List<TestCaseResponseDto>> GetTestCasesAsync(string? promptName, CancellationToken ct = default);
        Task<TestCaseDetailResponseDto?> GetTestCaseByIdAsync(int id, CancellationToken ct = default);
        Task<bool> DeleteTestCaseAsync(int id, CancellationToken ct = default);

        // Workbench Run
        Task<WorkbenchRunResultDto> RunWorkbenchAsync(WorkbenchRunRequestDto request, List<int> modelIds, IFormFile? testCaseFile, CancellationToken ct = default);

        // Workbench Run via Promptfoo
        Task<PromptfooEvalResultDto> RunWorkbenchWithPromptfooAsync(WorkbenchRunRequestDto request, List<int> modelIds, IFormFile? testCaseFile, CancellationToken ct = default);
    }
}
