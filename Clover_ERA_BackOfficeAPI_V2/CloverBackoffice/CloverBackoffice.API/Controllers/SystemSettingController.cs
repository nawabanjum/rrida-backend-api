using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SystemSettingController : ControllerBase
    {
        private readonly ISystemSettingService _service;
        public SystemSettingController(ISystemSettingService service)
        {
            _service = service;
        }

        [HttpGet("GetPromptTypes")]
        public async Task<IActionResult> GetPromptTypes()
        {
            var types = await _service.GetPromptTypes();
            return Ok(types);
        }

        [HttpGet("GetUserGroups")]
        public async Task<IActionResult> GetUserGroups()
        {
            return Ok(await _service.GetUserGroups());
        }

        [HttpGet("GetSystemModule")]
        public async Task<IActionResult> GetSystemModule()
        {
            var modules = await _service.GetSystemModules();
            return Ok(modules);
        }

        [HttpPost("GroupConfiguration")]
        public async Task<IActionResult> GroupConfiguration(GroupConfigurationDto mdl)
        {
            try
            {
                if (mdl.UserGroupId <= 0)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.InvalidData));
                }
                var modules = await _service.SaveAssignUserRight(mdl);
                if (modules == false)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.GroupConfigFailed));
                }

                return Ok(ApiResponseHandler.Success(modules, ResponseMessages.Success.GroupConfigSuccess));

            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
