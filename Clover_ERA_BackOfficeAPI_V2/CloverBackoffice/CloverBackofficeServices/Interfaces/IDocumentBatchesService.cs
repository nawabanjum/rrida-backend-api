using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IDocumentBatchesService
    {
        Task<string> AnalyzeFeedbackBatchesEmbedding(long unitId, int days);
        Task<BusinessUnitBatchJobsDto> GetBatchJobsWithInsightsAsyncEmbedding(int businessUnitId);
        Task<BatchJobAzureDto> AnalyzeFeedbackAzureResultsEmbedding([FromQuery] long unitId, [FromQuery] DateTimeOffset fromDate, [FromQuery] DateTimeOffset toDate);

    }
}
