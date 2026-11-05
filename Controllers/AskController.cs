using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AUA.Controllers
{
    /// <summary>
    /// Serves /ask -- a question-answering page that retrieves relevant chunks from
    /// the existing "aua_content" Algolia index (the same one /search uses) and asks
    /// Claude to summarize an answer grounded in those excerpts, with source links.
    ///
    /// Unlike /search, this cannot run client-side: answering requires the Anthropic
    /// API key, which is a secret and must never be exposed in browser JS (unlike
    /// Algolia's Search-Only key, which is safe client-side). So the whole
    /// retrieve-then-generate flow happens server-side in the Query() action below,
    /// and the view only ever talks to our own /ask/query endpoint.
    /// </summary>
    public class AskController : Controller
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        private const string AlgoliaAppId = "H9UYY6NMF3";
        private const string AlgoliaSearchKey = "f4171c56c847b9a83ebf9fab99ff898c";
        private const string AlgoliaIndexName = "aua_content";
        private const string AnthropicModel = "claude-sonnet-5";

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Query([FromBody] AskRequest request)
        {
            var question = request?.Question?.Trim();
            if (string.IsNullOrEmpty(question))
            {
                return BadRequest(new { error = "Question is required." });
            }

            var anthropicApiKey = HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()["Anthropic:ApiKey"];
            if (string.IsNullOrEmpty(anthropicApiKey))
            {
                return StatusCode(500, new { error = "Anthropic API key is not configured on the server." });
            }

            List<AlgoliaHit> hits;
            try
            {
                hits = await SearchAlgolia(question);
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = $"Search lookup failed: {ex.Message}" });
            }

            if (hits.Count == 0)
            {
                return Ok(new AskResponse
                {
                    Answer = "I couldn't find anything in AUA's published content that answers this. Try rephrasing, or use the regular search.",
                    Sources = new List<AskSource>()
                });
            }

            string answerText;
            try
            {
                answerText = await AskClaude(question, hits, anthropicApiKey);
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = $"Claude request failed: {ex.Message}" });
            }

            // De-duplicate sources by URL, preserving first-seen (highest-relevance) order.
            var seenUrls = new HashSet<string>();
            var sources = new List<AskSource>();
            foreach (var hit in hits)
            {
                if (seenUrls.Add(hit.Url))
                {
                    sources.Add(new AskSource { Title = hit.Title, Url = hit.Url });
                }
            }

            return Ok(new AskResponse { Answer = answerText, Sources = sources });
        }

        private async Task<List<AlgoliaHit>> SearchAlgolia(string question)
        {
            var url = $"https://{AlgoliaAppId}-dsn.algolia.net/1/indexes/{AlgoliaIndexName}/query";

            var requestBody = JsonSerializer.Serialize(new
            {
                query = question,
                hitsPerPage = 8,
                // Override the index-level distinct setting for retrieval: we want
                // multiple chunks from the same long document if they're all
                // relevant, unlike the /search UI which prefers one hit per page.
                distinct = false
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("X-Algolia-Application-Id", AlgoliaAppId);
            httpRequest.Headers.Add("X-Algolia-API-Key", AlgoliaSearchKey);

            var response = await HttpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<AlgoliaSearchResponse>(
                responseJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return parsed?.Hits ?? new List<AlgoliaHit>();
        }

        private async Task<string> AskClaude(string question, List<AlgoliaHit> hits, string apiKey)
        {
            var contextBlocks = string.Join("\n\n---\n\n", hits.Select((hit, i) =>
                $"[Source {i + 1}] {hit.Title} ({hit.Url})\n{hit.Text}"));

            const string systemPrompt =
                "You answer questions using ONLY the excerpts from the American Urological " +
                "Association's published website content provided below. Cite which numbered " +
                "source(s) support each claim, e.g. \"[Source 2]\". If the excerpts don't " +
                "contain enough information to answer, say so plainly rather than guessing. " +
                "This content includes clinical guidelines -- always note that your summary " +
                "is informational, drawn from AUA's published materials, and is not a " +
                "substitute for professional medical judgment or the full source document.";

            var userMessage = $"Question: {question}\n\nExcerpts:\n\n{contextBlocks}";

            var requestBody = JsonSerializer.Serialize(new
            {
                model = AnthropicModel,
                max_tokens = 1024,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userMessage }
                }
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");

            var response = await HttpClient.SendAsync(httpRequest);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Anthropic API returned {(int)response.StatusCode}: {responseJson}");
            }

            var parsed = JsonSerializer.Deserialize<AnthropicResponse>(
                responseJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return parsed?.Content?.FirstOrDefault()?.Text
                ?? "Claude returned an empty response.";
        }
    }

    public class AskRequest
    {
        public string Question { get; set; }
    }

    public class AskResponse
    {
        public string Answer { get; set; }
        public List<AskSource> Sources { get; set; }
    }

    public class AskSource
    {
        public string Title { get; set; }
        public string Url { get; set; }
    }

    // Shapes for deserializing Algolia's REST search response -- only the fields
    // this feature actually uses (matches the record schema from
    // build_algolia_records.py in the Algolia archive).
    public class AlgoliaSearchResponse
    {
        public List<AlgoliaHit> Hits { get; set; }
    }

    public class AlgoliaHit
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Text { get; set; }
    }

    // Shapes for deserializing Anthropic's Messages API response.
    public class AnthropicResponse
    {
        public List<AnthropicContentBlock> Content { get; set; }
    }

    public class AnthropicContentBlock
    {
        public string Type { get; set; }
        public string Text { get; set; }
    }
}
