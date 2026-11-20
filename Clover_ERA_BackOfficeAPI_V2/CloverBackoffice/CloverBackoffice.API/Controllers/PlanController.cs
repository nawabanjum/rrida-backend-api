//using CloverBackoffice.Components.Helpers;
//using CloverBackoffice.Services.Interfaces;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;

//namespace CloverBackoffice.API.Controllers
//{
//    [Route("api/plan")]
//    [ApiController]
//    public class PlanController : ControllerBase
//    {
//        private readonly IPlanService _planService;

//        public PlanController(IPlanService planService)
//        {
//            _planService = planService;
//        }

//        [HttpGet]
//        [AllowAnonymous]
//        public async Task<IActionResult> GetAllPlans()
//        {
//            try
//            {
//                var plans = await _planService.GetAllActivePlansAsync();
//                return Ok(ApiResponseHandler.Success(plans, "Plans retrieved successfully."));
//            }
//            catch (Exception)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
//            }
//        }

//        [HttpGet("calculate-price")]
//        [AllowAnonymous]
//        public IActionResult CalculatePrice([FromQuery] int teamCount)
//        {
//            try
//            {
//                var result = _planService.CalculatePrice(teamCount);
//                return Ok(ApiResponseHandler.Success(result, "Price calculated successfully."));
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

//        [HttpGet("{slug}")]
//        [AllowAnonymous]
//        public async Task<IActionResult> GetPlanBySlug(string slug)
//        {
//            try
//            {
//                var plan = await _planService.GetPlanBySlugAsync(slug);
//                if (plan == null)
//                    return Ok(ApiResponseHandler.Fail<string>("Plan not found."));

//                return Ok(ApiResponseHandler.Success(plan, "Plan retrieved successfully."));
//            }
//            catch (Exception)
//            {
//                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
//            }
//        }
//    }
//}
