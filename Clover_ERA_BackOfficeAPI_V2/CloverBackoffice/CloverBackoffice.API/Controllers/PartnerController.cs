using CloverBackoffice.BusinessModel;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PartnerController : ControllerBase
    {
        private readonly IOpportunityService _opportunityService;
        private readonly ITokenReaderService _tokenReader;

        public PartnerController(IOpportunityService opportunityService, ITokenReaderService tokenReader)
        {
            _opportunityService = opportunityService;
            _tokenReader = tokenReader;
        }

        [HttpPost("upsert-opportunity")]
        public async Task<IActionResult> Upsert([FromBody] Opportunity model)
        {
            try
            {
                var companyInfo = await _opportunityService.CheckDomainExistsAsync(model.PrimaryContactEmail);

                if (!companyInfo.IsAvailable)
                {
                    return Ok(ApiResponseHandler.Fail<string>(companyInfo.Message));
                }

                int? userId = _tokenReader.GetTokenInfo();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));
                }
                if (model == null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidOpp));

                if (!ModelState.IsValid)
                {
                    var errors = new List<string>();
                    foreach (var state in ModelState)
                    {
                        foreach (var error in state.Value.Errors)
                        {
                            errors.Add(error.ErrorMessage);
                        }
                    }
                    return Ok(ApiResponseHandler.Fail<List<string>>(ResponseMessages.Errors.InvalidOpp));
                }
                if (model.ProfileId <= 0 || !await _opportunityService.CheckCompanyExistsAsync(model.ProfileId))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidpart));
                }
                model.CreatedBy = userId.Value;
                var result = await _opportunityService.UpsertAsync(model);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
            
        }

        [HttpGet("opportunity-lookups")]
        public async Task<IActionResult> GetOpportunityLookups()
        {
            try
            {
                var lookups = await _opportunityService.GetAllLookupsAsync();
                return Ok(ApiResponseHandler.Success(lookups ,ResponseMessages.Success.tasksucces));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpPost("opportunities/list")]
        public async Task<IActionResult> GetOpportunities([FromBody] OpportunityFilterRequest filter)
        {
           
         
            try
            {
                var result = await _opportunityService.GetOpportunities(filter);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
        [HttpPut("apply-opportunity-changes")]
        public async Task<IActionResult> UpdateOpportunity([FromBody] UpdateOpportunityDTO dto)
        {
            try
            {
                int? userId = _tokenReader.GetTokenInfo();
                if (!userId.HasValue)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidToken));
                }

                if (dto.OpportunityId <= 0 || !await _opportunityService.CheckOpportunityExistsAsync(dto.OpportunityId))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidpart));
                }

                dto.UpdatedBy = userId.Value;
                var result = await _opportunityService.UpdateOpportunityAsync(dto);


                if (!result)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.oppfail));

                return Ok(ApiResponseHandler.Success<string>(null, ResponseMessages.Success.oppupdate));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("get-opportunity-by-id/{id}")]
        public async Task<IActionResult> GetOpportunityById(int id)
        {
            try
            {
                var result = await _opportunityService.GetOpportunityDetailAsync(id);

                if (result == null)
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.OperationFailed));

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        [HttpGet("opportunities-stats/{companyId}")]
        public async Task<IActionResult> GetOpportunityDashboard(int profileId)
        {
            try
            {
                var result = await _opportunityService.GetOpportunityDashboardAsync(profileId);
                 

                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Opefailed));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg)
                );
            }
        }

        [HttpGet("partner-domain-status/{contactEmail}")]
        public async Task<IActionResult> CheckCompanyDomain(string contactEmail)
        {
            try
            {
                var result = await _opportunityService.CheckDomainExistsAsync(contactEmail);

                if (!result.IsAvailable)
                {
                    return Ok(ApiResponseHandler.Fail<string>(result.Message));
                }

                return Ok(ApiResponseHandler.Success<string>(null, result.Message));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(
                    ResponseMessages.Errors.ExceptionMsg
                ));
            }
        }

        [HttpPost("opportunities/list-with-stats")]
        public async Task<IActionResult> GetOpportunitiesWithStats([FromQuery] int profileId, [FromBody] OpportunityFilterRequest filter)
        {
            try
            {
                int? userId = _tokenReader.GetUserIdFromToken();
                if (userId == null)
                {
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));
                }

                var result = await _opportunityService.GetOpportunitiesWithStatsAsync(userId.Value, profileId, filter);

                return Ok(ApiResponseHandler.Success(result,
                    ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception)
            {
                return StatusCode(500,
                    ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
