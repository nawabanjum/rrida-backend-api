using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class TokenReaderService : ITokenReaderService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApplicationDbContext _context;

        public TokenReaderService(IHttpContextAccessor httpContextAccessor, ApplicationDbContext context)
        {
            _httpContextAccessor = httpContextAccessor;
            _context = context;
        }

        public string GetToken()
        {
            var authorizationHeader = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authorizationHeader))
                return null;

            // If using "Bearer <token>", remove the scheme prefix:
            if (authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return authorizationHeader.Substring("Bearer ".Length).Trim();

            return authorizationHeader;
        }
        public int? GetUserIdFromToken()
        {
            var token = GetToken();

            if (string.IsNullOrWhiteSpace(token))
                return null;

            var authToken = _context.UserTokens
             .FirstOrDefault(a => a.Token.ToLower() == token.ToLower() && a.ExpiryDate >= DateTime.UtcNow);

            if (authToken == null)
                return null;
            return authToken.UserId;

        }


        public int? GetTokenInfo()
        {
            var token = GetToken();

            if (string.IsNullOrWhiteSpace(token))
                return null;

            var authToken = _context.UserTokens
             .FirstOrDefault(a => a.Token.ToLower() == token.ToLower());

            if (authToken == null)
                return null;
            return authToken.UserId;

        }

        public async Task<User?> GetCurrentUserAsync()
        {
            var userId = GetUserIdFromToken();
            if (!userId.HasValue)
                return null;

            return await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId.Value && !u.IsDel);
        }

        public async Task<bool> IsAdminAsync()
        {
            var user = await GetCurrentUserAsync();
            return user?.UserGroupId == (int)UserGroups.RridaAdmin;
        }
    }
}
