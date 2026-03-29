using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ForumZenpace.Models;

namespace ForumZenpace.Services
{
    public class RecommendationService
    {
        private readonly ForumDbContext _context;
        private readonly GeminiEmbeddingService _embeddingService;
        private readonly ILogger<RecommendationService> _logger;
        private readonly IDistributedCache _cache;

        public RecommendationService(ForumDbContext context, GeminiEmbeddingService embeddingService, ILogger<RecommendationService> logger, IDistributedCache cache)
        {
            _context = context;
            _embeddingService = embeddingService;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Generate and store embedding vector for a post.
        /// Called after post creation/edit.
        /// </summary>
        public async Task GeneratePostEmbeddingAsync(Post post)
        {
            try
            {
                var textForEmbedding = $"{post.Title}\n\n{post.Content}";
                var embedding = await _embeddingService.GetEmbeddingAsync(textForEmbedding);

                if (embedding != null)
                {
                    post.VectorData = JsonSerializer.Serialize(embedding);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Generated embedding for post {PostId}", post.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for post {PostId}", post.Id);
            }
        }

        /// <summary>
        /// Recalculate user's preference vector by averaging embeddings of all liked posts.
        /// </summary>
        public async Task UpdateUserPreferenceVectorAsync(int userId)
        {
            try
            {
                var likedPostVectors = await _context.Likes
                    .Where(l => l.UserId == userId)
                    .Join(_context.Posts, l => l.PostId, p => p.Id, (l, p) => p.VectorData)
                    .Where(v => v != null)
                    .ToListAsync();

                if (likedPostVectors.Count == 0)
                {
                    var user = await _context.Users.FindAsync(userId);
                    if (user != null)
                    {
                        user.PreferenceVectorData = null;
                        await _context.SaveChangesAsync();
                    }
                    return;
                }

                // Parse all vectors
                var vectors = likedPostVectors
                    .Select(v => JsonSerializer.Deserialize<float[]>(v!))
                    .Where(v => v != null && v.Length > 0)
                    .ToList();

                if (vectors.Count == 0) return;

                // Average them
                var dimension = vectors[0]!.Length;
                var averageVector = new float[dimension];

                foreach (var vector in vectors)
                {
                    for (int i = 0; i < dimension; i++)
                    {
                        averageVector[i] += vector![i];
                    }
                }

                for (int i = 0; i < dimension; i++)
                {
                    averageVector[i] /= vectors.Count;
                }

                var userEntity = await _context.Users.FindAsync(userId);
                if (userEntity != null)
                {
                    userEntity.PreferenceVectorData = JsonSerializer.Serialize(averageVector);
                    await _context.SaveChangesAsync();
                    await _cache.RemoveAsync($"feed:user:{userId}");
                    _logger.LogInformation("Updated preference vector for user {UserId} from {Count} liked posts", userId, vectors.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update preference vector for user {UserId}", userId);
            }
        }

        /// <summary>
        /// Get recommended posts for a user based on their preference vector.
        /// Falls back to recent popular posts if user has no preferences.
        /// </summary>
        public async Task<List<Post>> GetRecommendedPostsAsync(int userId)
        {
            var cacheKey = $"feed:user:{userId}";
            var cachedFeedData = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedFeedData))
            {
                try
                {
                    var cachedIds = JsonSerializer.Deserialize<List<int>>(cachedFeedData);
                    if (cachedIds != null && cachedIds.Count > 0)
                    {
                        var cachedPosts = await _context.Posts
                            .Include(p => p.User)
                            .Include(p => p.Category)
                            .Include(p => p.Likes)
                            .Include(p => p.Comments)
                            .AsSplitQuery()
                            .Where(p => cachedIds.Contains(p.Id))
                            .ToListAsync();

                        // Preserve cached ordering
                        return cachedIds
                            .Select(id => cachedPosts.FirstOrDefault(p => p.Id == id))
                            .Where(p => p != null)
                            .Cast<Post>()
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize cached feed for user {UserId}", userId);
                }
            }

            var user = await _context.Users.FindAsync(userId);

            // If user has no preference, fallback to popular recent posts
            if (user == null || string.IsNullOrWhiteSpace(user.PreferenceVectorData))
            {
                return await GetFallbackPostsAsync();
            }

            float[]? userPreference;
            try
            {
                userPreference = JsonSerializer.Deserialize<float[]>(user.PreferenceVectorData);
            }
            catch
            {
                return await GetFallbackPostsAsync();
            }

            if (userPreference == null || userPreference.Length == 0)
            {
                return await GetFallbackPostsAsync();
            }

            // Load recent posts that have vectors (projected to avoid fetching giant tracking entities)
            var recentPostsData = await _context.Posts
                .Where(p => p.Status == "Active" && p.VectorData != null)
                .OrderByDescending(p => p.CreatedAt)
                .Take(200)
                .Select(p => new {
                    p.Id,
                    p.VectorData,
                    p.CreatedAt,
                    LikesCount = p.Likes.Count
                })
                .ToListAsync();

            if (recentPostsData.Count == 0)
            {
                return await GetFallbackPostsAsync();
            }

            // Score each post
            var scoredPosts = new List<(int PostId, double Score)>();

            foreach (var post in recentPostsData)
            {
                try
                {
                    var postVector = JsonSerializer.Deserialize<float[]>(post.VectorData!);
                    if (postVector == null || postVector.Length == 0) continue;

                    // Cosine similarity (-1 to 1, higher = more similar)
                    double similarityScore = CalculateCosineSimilarity(userPreference, postVector);

                    // Time decay: exponential decay, lambda = 0.05
                    double hoursOld = (DateTime.UtcNow - post.CreatedAt).TotalHours;
                    double timeDecay = Math.Exp(-hoursOld * 0.05);

                    // Popularity score
                    double popularity = post.LikesCount * 0.01;

                    // Final heuristic score
                    double finalScore = (similarityScore * 0.6) + (timeDecay * 0.3) + (popularity * 0.1);

                    scoredPosts.Add((post.Id, finalScore));
                }
                catch
                {
                    // Skip posts with invalid vector data
                }
            }

            // Also include posts without vectors (sorted by recency, lower priority)
            var postsWithoutVectorsData = await _context.Posts
                .Where(p => p.Status == "Active" && p.VectorData == null)
                .OrderByDescending(p => p.CreatedAt)
                .Take(50)
                .Select(p => new {
                    p.Id,
                    p.CreatedAt,
                    LikesCount = p.Likes.Count
                })
                .ToListAsync();

            foreach (var post in postsWithoutVectorsData)
            {
                double hoursOld = (DateTime.UtcNow - post.CreatedAt).TotalHours;
                double timeDecay = Math.Exp(-hoursOld * 0.05);
                double finalScore = (timeDecay * 0.5) + (post.LikesCount * 0.01 * 0.5);
                scoredPosts.Add((post.Id, finalScore));
            }

            // Get top 50 IDs
            var topPostIds = scoredPosts
                .OrderByDescending(x => x.Score)
                .Select(x => x.PostId)
                .Take(50)
                .ToList();

            if (topPostIds.Count == 0) return await GetFallbackPostsAsync();

            // Fetch fully populated posts for the top IDs using AsSplitQuery
            var finalPostsUnordered = await _context.Posts
                .Include(p => p.User)
                .Include(p => p.Category)
                .Include(p => p.Likes)
                .Include(p => p.Comments)
                .AsSplitQuery()
                .Where(p => topPostIds.Contains(p.Id))
                .ToListAsync();

            // Restore score ordering
            var finalResults = topPostIds
                .Select(id => finalPostsUnordered.FirstOrDefault(p => p.Id == id))
                .Where(p => p != null)
                .Cast<Post>()
                .ToList();

            if (finalResults.Count > 0)
            {
                var idList = finalResults.Select(p => p.Id).ToList();
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(idList), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
                });
            }

            return finalResults;
        }

        /// <summary>Fallback: recent posts sorted by likes + recency.</summary>
        private async Task<List<Post>> GetFallbackPostsAsync()
        {
            var cacheKey = "feed:fallback";
            var cachedFallbackData = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedFallbackData))
            {
                try
                {
                    var cachedIds = JsonSerializer.Deserialize<List<int>>(cachedFallbackData);
                    if (cachedIds != null && cachedIds.Count > 0)
                    {
                        var cachedPosts = await _context.Posts
                            .Include(p => p.User)
                            .Include(p => p.Category)
                            .Include(p => p.Likes)
                            .Include(p => p.Comments)
                            .AsSplitQuery()
                            .Where(p => cachedIds.Contains(p.Id))
                            .ToListAsync();

                        return cachedIds
                            .Select(id => cachedPosts.FirstOrDefault(p => p.Id == id))
                            .Where(p => p != null)
                            .Cast<Post>()
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize cached fallback feed");
                }
            }

            var fallbackResults = await _context.Posts
                .Include(p => p.User)
                .Include(p => p.Category)
                .Include(p => p.Likes)
                .Include(p => p.Comments)
                .AsSplitQuery()
                .Where(p => p.Status == "Active")
                .OrderByDescending(p => p.Likes.Count + p.ViewCount)
                .ThenByDescending(p => p.CreatedAt)
                .Take(50)
                .ToListAsync();

            if (fallbackResults.Count > 0)
            {
                var idList = fallbackResults.Select(p => p.Id).ToList();
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(idList), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
                });
            }

            return fallbackResults;
        }

        /// <summary>
        /// Calculate Cosine Similarity between two vectors.
        /// Returns value between -1 and 1.
        /// </summary>
        public static double CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
        {
            if (vectorA.Length != vectorB.Length || vectorA.Length == 0) return 0;

            double dotProduct = 0, magnitudeA = 0, magnitudeB = 0;

            for (int i = 0; i < vectorA.Length; i++)
            {
                dotProduct += vectorA[i] * (double)vectorB[i];
                magnitudeA += vectorA[i] * (double)vectorA[i];
                magnitudeB += vectorB[i] * (double)vectorB[i];
            }

            if (magnitudeA == 0 || magnitudeB == 0) return 0;

            return dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
        }
    }
}
