using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Components.Helpers
{
    public class JwtTokenGenerator
    {
        private readonly IConfiguration _configuration;

        public JwtTokenGenerator(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateToken(User user, string email)
        {
            var jwtSettingsKeySecretName = _configuration["AzureKeyVault:SecretNames:JwtSettingsKey"];
            var jwtSettingsKey = _configuration[jwtSettingsKeySecretName];
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettingsKey));

            var jwtSettings = _configuration.GetSection("JwtSettings");

            //var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("userId", user.UserId.ToString()),
                new Claim("userGroupId", user.UserGroupId.ToString()),
            };

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(jwtSettings["ExpiryInMinutes"])),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

    }
}
