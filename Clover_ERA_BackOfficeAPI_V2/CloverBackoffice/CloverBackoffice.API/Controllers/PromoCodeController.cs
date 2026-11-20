//using CloverBackoffice.Components.Helpers;
//using CloverBackoffice.Infrastructure.DTO;
//using CloverBackoffice.Services.Interfaces;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;

//namespace CloverBackoffice.API.Controllers
//{
//    [Route("api/promo-code")]
//    [ApiController]
//    [Authorize]
//    public class PromoCodeController : ControllerBase
//    {
//        private readonly IPromoCodeService _promoCodeService;
//        private readonly ITokenReaderService _tokenReader;

//        public PromoCodeController(IPromoCodeService promoCodeService, ITokenReaderService tokenReader)
//        {
//            _promoCodeService = promoCodeService;
//            _tokenReader = tokenReader;
//        }

//        [HttpPost]
//        public async Task<IActionResult> Create([FromBody] CreatePromoCodeDto dto)
//        {
//            try
//            {
//                var isAdmin = await _tokenReader.IsAdminAsync();
//                if (!isAdmin)
//                    return Ok(ApiResponseHandler.Fail<string>("Only Super Admin can create promo codes."));

//                var user = await _tokenReader.GetCurrentUserAsync();
//                if (user == null)
//                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

//                var result = await _promoCodeService.CreateAsync(dto, user.UserId);
//                return Ok(ApiResponseHandler.Success(result, "Promo code created successfully."));
//            }
//            catch (InvalidOperationException ex)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
//            }
//            catch (Exception)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
//            }
//        }

//        [HttpGet]
//        public async Task<IActionResult> GetAll()
//        {
//            try
//            {
//                var isAdmin = await _tokenReader.IsAdminAsync();
//                if (!isAdmin)
//                    return Ok(ApiResponseHandler.Fail<string>("Only Super Admin can view promo codes."));

//                var result = await _promoCodeService.GetAllAsync();
//                return Ok(ApiResponseHandler.Success(result, "Promo codes retrieved successfully."));
//            }
//            catch (Exception)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
//            }
//        }

//        [HttpGet("{id}")]
//        public async Task<IActionResult> GetById(int id)
//        {
//            try
//            {
//                var isAdmin = await _tokenReader.IsAdminAsync();
//                if (!isAdmin)
//                    return Ok(ApiResponseHandler.Fail<string>("Only Super Admin can view promo codes."));

//                var result = await _promoCodeService.GetByIdAsync(id);
//                if (result == null)
//                    return Ok(ApiResponseHandler.Fail<string>("Promo code not found."));

//                return Ok(ApiResponseHandler.Success(result, "Promo code retrieved successfully."));
//            }
//            catch (Exception)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
//            }
//        }

//        [HttpPut("{id}")]
//        public async Task<IActionResult> Update(int id, [FromBody] UpdatePromoCodeDto dto)
//        {
//            try
//            {
//                var isAdmin = await _tokenReader.IsAdminAsync();
//                if (!isAdmin)
//                    return Ok(ApiResponseHandler.Fail<string>("Only Super Admin can update promo codes."));

//                var result = await _promoCodeService.UpdateAsync(id, dto);
//                return Ok(ApiResponseHandler.Success(result, "Promo code updated successfully."));
//            }
//            catch (InvalidOperationException ex)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
//            }
//            catch (Exception)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
//            }
//        }

//        [HttpDelete("{id}")]
//        public async Task<IActionResult> Delete(int id)
//        {
//            try
//            {
//                var isAdmin = await _tokenReader.IsAdminAsync();
//                if (!isAdmin)
//                    return Ok(ApiResponseHandler.Fail<string>("Only Super Admin can delete promo codes."));

//                await _promoCodeService.DeleteAsync(id);
//                return Ok(ApiResponseHandler.Success<string?>(null, "Promo code deleted successfully."));
//            }
//            catch (InvalidOperationException ex)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
//            }
//            catch (Exception)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
//            }
//        }

//        [HttpPost("validate")]
//        public async Task<IActionResult> Validate([FromBody] ValidatePromoCodeRequestDto dto)
//        {
//            try
//            {
//                var user = await _tokenReader.GetCurrentUserAsync();
//                if (user == null)
//                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

//                if (user.ProfileId == null)
//                    return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

//                var result = await _promoCodeService.ValidateAsync(dto.Code, (uint)user.ProfileId.Value, dto.BillingCycle);
//                return Ok(ApiResponseHandler.Success(result, result.IsValid ? "Promo code is valid." : "Promo code validation failed."));
//            }
//            catch (Exception)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
//            }
//        }
//    }
//}
