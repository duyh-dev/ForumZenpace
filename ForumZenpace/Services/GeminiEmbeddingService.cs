using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ForumZenpace.Services
{
    public class GeminiSettings
    {
        public string ApiKey { get; set; } = string.Empty;
    }

    public class GeminiEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<GeminiEmbeddingService> _logger;
        private readonly IDistributedCache _cache;
        
        private const string EmbedApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/text-embedding-004:embedContent";
        private const string GenerateApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-latest:generateContent";
        private const string ApiKeyEnvironmentVariable = "GeminiSettings__ApiKey";

        public GeminiEmbeddingService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiEmbeddingService> logger, IDistributedCache cache)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cache;
            _apiKey = configuration["GeminiSettings:ApiKey"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogWarning(
                    "Gemini API key is not configured. Set the '{EnvironmentVariable}' environment variable.",
                    ApiKeyEnvironmentVariable);
            }
        }

        public async Task<float[]?> GetEmbeddingAsync(string textContent)
        {
            if (string.IsNullOrWhiteSpace(textContent) || string.IsNullOrWhiteSpace(_apiKey))
                return null;

            try
            {
                if (textContent.Length > 8000) textContent = textContent[..8000];

                var requestBody = new { model = "models/text-embedding-004", content = new { parts = new[] { new { text = textContent } } } };
                var response = await _httpClient.PostAsync($"{EmbedApiUrl}?key={_apiKey}", new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseString);
                return document.RootElement.GetProperty("embedding").GetProperty("values").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get embedding from Gemini");
                return null;
            }
        }

        public async Task<List<string>> GenerateCommentSuggestionsAsync(int userId, int postId, string title, string content, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey)) return new List<string>();

            var rateLimitKey = $"rate_comment_sugg_{userId}";
            if (await _cache.GetStringAsync(rateLimitKey, cancellationToken) != null)
            {
                _logger.LogWarning("Rate limit hit for user {UserId} generating comment suggestions.", userId);
                return new List<string>();
            }

            var cacheKey = $"ai_sugg_post_{postId}";
            var cachedData = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(cachedData))
            {
                try { return JsonSerializer.Deserialize<List<string>>(cachedData) ?? new List<string>(); }
                catch { /* Ignore deserialization errors and re-fetch */ }
            }

            // Set rate limit (30 seconds)
            await _cache.SetStringAsync(rateLimitKey, "1", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                var combinedContent = $"{title}\n\n{content}";
                if (combinedContent.Length > 4000) combinedContent = combinedContent[..4000]; // Giới hạn context text

                var prompt = $"Dựa vào bài viết sau, hãy cung cấp MẢNG JSON gồm 6 câu bình luận tiếng Việt ngắn gọn, súc tích (dưới 20 chữ mỗi câu, không quá 2 dòng) phản ứng tự nhiên với nội dung. CHỈ TRẢ VỀ ARRAY JSON, KHÔNG KÈM THEO MAKRDOWN FORMAT HOẶC KÝ TỰ KHÁC. Bài viết:\n\n{combinedContent}";

                var requestBody = new
                {
                    contents = new[] { new { parts = new[] { new { text = prompt } } } },
                    generationConfig = new { temperature = 0.7, topK = 40, maxOutputTokens = 256 }
                };

                _logger.LogInformation("Requesting Gemini generateContent for Post {PostId}", postId);

                var response = await _httpClient.PostAsync($"{GenerateApiUrl}?key={_apiKey}", new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"), cts.Token);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogInformation("Raw Gemini response: {ResponseLength} chars", responseString.Length);

                using var doc = JsonDocument.Parse(responseString);
                var textResponse = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString() ?? string.Empty;

                textResponse = textResponse.Trim();
                if (textResponse.StartsWith("```json")) textResponse = textResponse.Substring(7);
                if (textResponse.StartsWith("```")) textResponse = textResponse.Substring(3);
                if (textResponse.EndsWith("```")) textResponse = textResponse.Substring(0, textResponse.Length - 3);
                textResponse = textResponse.Trim();

                var suggestions = JsonSerializer.Deserialize<List<string>>(textResponse);
                if (suggestions != null && suggestions.Count > 0)
                {
                    // Clean format
                    suggestions = suggestions.Select(s => s.Replace("\n", " ").Trim()).Take(6).ToList();
                    
                    // Cache the successful result (10 minutes)
                    await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(suggestions), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, cancellationToken);
                    return suggestions;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Gemini API call timed out after 5s for Post {PostId}", postId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating comment suggestions for Post {PostId}", postId);
            }

            return new List<string>();
        }
    }
}
