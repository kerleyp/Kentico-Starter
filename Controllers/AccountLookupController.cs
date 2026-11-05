using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AUA.Controllers
{
    /// <summary>
    /// Serves /api/account-lookup -- a thin server-side proxy in front of AUA's
    /// GetAccountInfo API (https://apiaspi.auanet.org/aua/GetAccountInfo). Exists
    /// purely so the Basic Auth credential never has to touch browser JS: the
    /// home page's button calls THIS endpoint via fetch(), and this controller is
    /// the only thing that ever talks to apiaspi.auanet.org directly, attaching
    /// the credential from server-side configuration.
    ///
    /// Credential setup (local dev): run, from the project root --
    ///   dotnet user-secrets set "AuaApi:Username" "the real username"
    ///   dotnet user-secrets set "AuaApi:Password" "the real password"
    /// User secrets are stored outside the project folder (not in source control,
    /// never pasted anywhere). In QA/UAT/production, set the same two keys as
    /// environment variables (AuaApi__Username / AuaApi__Password) or an
    /// equivalent secret store instead.
    /// </summary>
    public class AccountLookupController : Controller
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        private const string ApiBaseUrl = "https://apiaspi.auanet.org/aua/GetAccountInfo";

        [HttpGet]
        public async Task<IActionResult> Lookup(string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return BadRequest(new { error = "customerId is required." });
            }

            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var username = configuration["AuaApi:Username"];
            var password = configuration["AuaApi:Password"];

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return StatusCode(500, new
                {
                    error = "AUA API credentials are not configured on the server. " +
                             "Set AuaApi:Username / AuaApi:Password via dotnet user-secrets."
                });
            }

            var url = $"{ApiBaseUrl}?customerId={Uri.EscapeDataString(customerId)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var authBytes = Encoding.ASCII.GetBytes($"{username}:{password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            HttpResponseMessage response;
            try
            {
                response = await HttpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = $"Request to AUA API failed: {ex.Message}" });
            }

            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    error = $"AUA API returned {(int)response.StatusCode}",
                    body
                });
            }

            // Passes the API's JSON straight through -- the page just dumps it as-is.
            return Content(body, "application/json");
        }
    }
}
