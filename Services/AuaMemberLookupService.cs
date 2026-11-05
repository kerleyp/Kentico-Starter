using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace AUA.Services
{
    /// <summary>
    /// Looked-up membership details for a verified email address, once a
    /// visitor has signed in via ExternalAuthController. Field names below
    /// are guesses at a plausible shape -- update DeserializeResponse below
    /// once you have the real by-email endpoint's actual response.
    /// </summary>
    public class AuaMemberInfo
    {
        [JsonPropertyName("isMember")]
        public bool IsMember { get; set; }

        [JsonPropertyName("customerId")]
        public string CustomerId { get; set; }

        [JsonPropertyName("fullName")]
        public string FullName { get; set; }
    }

    public interface IAuaMemberLookupService
    {
        Task<AuaMemberInfo> LookupByEmailAsync(string email);
    }

    /// <summary>
    /// TODO before this is real: this project's only confirmed-working AUA
    /// API so far is GetAccountInfo (see AccountLookupController), which is
    /// keyed by customerId, NOT email -- there is no confirmed by-email
    /// endpoint yet. ApiBaseUrl below is a placeholder guess
    /// (GetAccountInfoByEmail) that almost certainly needs to be swapped
    /// for whatever AUA's actual email-lookup endpoint turns out to be
    /// (could be a different URL entirely, a different auth scheme, or the
    /// existing GetAccountInfo endpoint with an "email" query param instead
    /// of "customerId" -- worth checking with whoever owns apiaspi.auanet.org
    /// before assuming this shape is right).
    ///
    /// Reuses the same Basic Auth credential as AccountLookupController
    /// (AuaApi:Username / AuaApi:Password via dotnet user-secrets -- see
    /// AccountLookupController's header comment for the exact commands).
    /// Returns null on any failure (network error, non-2xx response,
    /// missing credentials) rather than throwing -- callers should treat
    /// null as "couldn't confirm membership," not "confirmed non-member."
    /// </summary>
    public class AuaMemberLookupService : IAuaMemberLookupService
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        // TODO: confirm the real endpoint -- this is a placeholder.
        private const string ApiBaseUrl = "https://apiaspi.auanet.org/aua/GetAccountInfoByEmail";

        private readonly IConfiguration _configuration;

        public AuaMemberLookupService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<AuaMemberInfo> LookupByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            var username = _configuration["AuaApi:Username"];
            var password = _configuration["AuaApi:Password"];
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return null;
            }

            var url = $"{ApiBaseUrl}?email={Uri.EscapeDataString(email)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var authBytes = Encoding.ASCII.GetBytes($"{username}:{password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            try
            {
                var response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<AuaMemberInfo>(json, options);
            }
            catch
            {
                // Network/parse failure -- treat as "unknown," see class comment.
                return null;
            }
        }
    }
}
