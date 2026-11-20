using Microsoft.Identity.Client;
using System;
using System.Net.Http;
using System.Net.Http.Headers;

HttpClient client = new HttpClient();

var clientId = "d6f51a26-33a3-4290-ae74-1da2c1a1940e";
var clientSecret = Environment.GetEnvironmentVariable("CLOVER_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set the CLOVER_CLIENT_SECRET environment variable.");
var scopes1 = new[] { "api://d6f51a26-33a3-4290-ae74-1da2c1a1940e/.default" };
var tenantId = "ca6c3888-d29c-43c2-809b-a4f892f923c1";
var authority = $"https://login.microsoftonline.com/{tenantId}";
try
{
    HttpClient client1 = new HttpClient();

    var app = ConfidentialClientApplicationBuilder
        .Create(clientId)
        .WithAuthority(authority)
        .WithClientSecret(clientSecret)
        .Build();

    var result = await app.AcquireTokenForClient(scopes1).ExecuteAsync();

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
    var response = await client.GetAsync("https://localhost:7195/api/User/GetUserInfo");
    var content = await response.Content.ReadAsStringAsync();

}
catch (Exception ex)
{

    //throw ex;
}
