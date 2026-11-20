using CloverBackoffice.Infrastructure.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Components.Helpers
{
    public static class ApiResponseHandler
    {
        public static ResponseDto<T> Success<T>(T data, string message = "") =>
        new ResponseDto<T> { IsSuccess = true, Message = message, Content = data };

        public static ResponseDto<T> Fail<T>(string message) =>
        new ResponseDto<T> { IsSuccess = false, Message = message, Content = default };

        public static ResponseDto<T> Fail<T>(T data, string message) =>
        new ResponseDto<T> { IsSuccess = false, Message = message, Content = data };
    }
}
