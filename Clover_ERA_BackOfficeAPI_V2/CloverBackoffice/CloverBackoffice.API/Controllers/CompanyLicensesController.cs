//using CloverBackoffice.Components.Helpers;
//using CloverBackoffice.Infrastructure.DTO;
//using CloverBackoffice.Services.Interfaces;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;

//namespace CloverBackoffice.API.Controllers
//{
//    [Route("api/company-licenses")]
//    [ApiController]
//    [Authorize]
//    public class CompanyLicensesController : ControllerBase
//    {
//        private readonly ICompanyLicenseService _service;
//        private readonly ITokenReaderService _tokenReader;

//        public CompanyLicensesController(ICompanyLicenseService service, ITokenReaderService tokenReader)
//        {
//            _service = service;
//            _tokenReader = tokenReader;
//        }

//        // GET /api/company-licenses?profileId=125
//        [HttpGet]
//        public async Task<IActionResult> Get([FromQuery] uint profileId)
//        {
//            try
//            {
//                if (profileId == 0)
//                    return Ok(ApiResponseHandler.Fail<string>("profileId is required."));

//                var data = await _service.GetByCompanyAsync(profileId);
//                return Ok(ApiResponseHandler.Success(data, ResponseMessages.Success.OperationSuccess));
//            }
//            catch (Exception ex)
//            {
//                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
//            }
//        }

//        // GET /api/company-licenses/{id}/history
//        [HttpGet("{id:int}/history")]
//        public async Task<IActionResult> History(int id)
//        {
//            try
//            {
//                var data = await _service.GetHistoryAsync(id);
//                return Ok(ApiResponseHandler.Success(data, ResponseMessages.Success.OperationSuccess));
//            }
//            catch (Exception ex)
//            {
//                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
//            }
//        }

//        // POST /api/company-licenses/assign
//        [HttpPost("assign")]
//        public async Task<IActionResult> Assign([FromBody] AssignCompanyLicenseDto dto)
//        {
//            try
//            {
//                if (dto == null || !ModelState.IsValid)
//                    return Ok(ApiResponseHandler.Fail<string>("Invalid payload."));

//                var actor = _tokenReader.GetUserIdFromToken();
//                var result = await _service.AssignAsync(dto, actor);
//                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
//            }
//            catch (InvalidOperationException ex)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
//            }
//            catch (Exception ex)
//            {
//                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
//            }
//        }

//        // POST /api/company-licenses/unassign
//        [HttpPost("unassign")]
//        public async Task<IActionResult> Unassign([FromBody] UnassignCompanyLicenseDto dto)
//        {
//            try
//            {
//                if (dto == null || !ModelState.IsValid)
//                    return Ok(ApiResponseHandler.Fail<string>("Invalid payload."));

//                var actor = _tokenReader.GetUserIdFromToken();
//                var ok = await _service.UnassignAsync(dto.CompanyLicenseId, actor);
//                return Ok(ApiResponseHandler.Success(new { unassigned = ok }, ResponseMessages.Success.OperationSuccess));
//            }
//            catch (Exception ex)
//            {
//                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
//            }
//        }
//    }
//}
