using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Components.Helpers
{
    public static class StatusMapper
    {
        public static string GetStatusName(string statusCode)
        {
            return statusCode switch
            {
                "1" => "Active",
                "2" => "Pending",
                "3" => "Suspended"
            };
        }

        public static UserStatusDto ToDto(User user)
        {
            return new UserStatusDto
            {
                UserId = user.UserId,
                TenantId = user.TenantId,
                Username = user.Username,
                Name = user.Name,
                CreatedDate = user.CreatedDate,
                ModifiedDate = user.ModifiedDate,
                CreatedBy = user.CreatedBy,
                ModifiedBy = user.ModifiedBy,
                IsDel = user.IsDel,
                IsActive = user.IsActive,
                UserGroupId = user.UserGroupId,
                LastLoginDate = user.LastLoginDate,
                Password = user.Password,
                IsVerified = user.IsVerified,
                Status = user.Status,
                Type = user.Type,
                IsEntraId = user.IsEntraId,
                IsFirstTimePasswordChange = user.IsFirstTimePasswordChange,
                ProfileId = user.ProfileId,
                StatusName = GetStatusName(user.Status)
            };
        }
    }
}
