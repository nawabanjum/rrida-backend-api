using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/permissions")]
    [ApiController]
    [Authorize]
    public class PermissionController : ControllerBase
    {
        private readonly IPermissionService _permissionService;

        public PermissionController(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        [HttpGet("matrix")]
        public async Task<IActionResult> GetPermissionMatrix()
        {
            var matrix = await _permissionService.GetPermissionMatrixAsync();
            return Ok(matrix);
        }

        [HttpPost("Setmatrix")]
        public async Task<IActionResult> SetPermissionMatrix(List<PermissionMatrixConfigurationDto> model)
        {
            if (model == null && model.Count > 0)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));
            }
            var responce = await _permissionService.SetPermissionMatrixAsync(model);
            if (responce == false)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.PermissionMatrixConfigFailed));
            }
            var matrix = await _permissionService.GetPermissionMatrixAsync();
            return Ok(matrix);

        }

        [HttpPost("setSingleMatrix")]
        public async Task<IActionResult> SetSinglePermissionMatrix(PermissionMatrixConfigurationDto model)
        {
            if (model == null)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));
            }
            var responce = await _permissionService.SetSinglePermissionMatrixAsync(model);
            if (responce == false)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.PermissionMatrixConfigFailed));
            }
            var matrix = await _permissionService.GetPermissionMatrixAsync();
            return Ok(matrix);
        }
    }
}
