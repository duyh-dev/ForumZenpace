using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ForumZenpace.Models;
using ForumZenpace.Services;

namespace ForumZenpace.Controllers
{
    public class HomeController : Controller
    {
        private readonly ForumDbContext _context;
        private readonly SocialService _socialService;
        private readonly StoryService _storyService;
        private readonly RecommendationService _recommendationService;

        public HomeController(ForumDbContext context, SocialService socialService, StoryService storyService, RecommendationService recommendationService)
        {
            _context = context;
            _socialService = socialService;
            _storyService = storyService;
            _recommendationService = recommendationService;
        }

        public async Task<IActionResult> Index(string searchString, int? categoryId, string sortOrder)
        {
            var currentUserId = GetCurrentUserId();

            // Default to 'recommended' for logged-in users on first visit.
            if (string.IsNullOrEmpty(sortOrder) && currentUserId.HasValue)
            {
                sortOrder = "recommended";
            }

            List<Post> postList;
            bool isRecommendedSort = sortOrder == "recommended" && currentUserId.HasValue;

            if (isRecommendedSort && string.IsNullOrEmpty(searchString) && !categoryId.HasValue)
            {
                // Use recommendation engine
                postList = await _recommendationService.GetRecommendedPostsAsync(currentUserId!.Value);
            }
            else
            {
                var posts = _context.Posts
                    .Include(p => p.User)
                    .Include(p => p.Category)
                    .Include(p => p.Likes)
                    .Include(p => p.Comments)
                    .AsSplitQuery()
                    .Where(p => p.Status == "Active")
                    .AsQueryable();

                if (!string.IsNullOrEmpty(searchString))
                {
                    posts = posts.Where(p => p.Title.Contains(searchString) || p.Content.Contains(searchString));
                }

                if (categoryId.HasValue)
                {
                    posts = posts.Where(p => p.CategoryId == categoryId.Value);
                }

                switch (sortOrder)
                {
                    case "views":
                        posts = posts.OrderByDescending(p => p.ViewCount);
                        break;
                    case "likes":
                        posts = posts.OrderByDescending(p => p.Likes.Count);
                        break;
                    default:
                        posts = posts.OrderByDescending(p => p.CreatedAt);
                        break;
                }

                postList = await posts.ToListAsync();
            }

            var friends = currentUserId.HasValue
                ? await _socialService.GetFriendsAsync(currentUserId.Value)
                : Array.Empty<FriendSummaryViewModel>();

            if (currentUserId.HasValue && friends.Count > 0)
            {
                friends = await _storyService.PopulateActiveStoryStateAsync(currentUserId.Value, friends, HttpContext.RequestAborted);
            }

            var suggestedFriends = currentUserId.HasValue
                ? (await _socialService.SearchFriendCandidatesAsync(currentUserId.Value, null, 10, HttpContext.RequestAborted))
                    .Where(c => c.RelationshipState == "none")
                    .Take(10)
                    .ToList()
                : (IReadOnlyList<FriendCandidateViewModel>)Array.Empty<FriendCandidateViewModel>();

            return View(new HomeIndexViewModel
            {
                Posts = postList,
                Categories = await _context.Categories.ToListAsync(),
                CurrentSort = sortOrder ?? string.Empty,
                CurrentCategoryId = categoryId,
                SearchString = searchString,
                CurrentUserId = currentUserId,
                IsRecommendedSort = isRecommendedSort,
                CurrentUserStory = currentUserId.HasValue
                    ? await _storyService.GetCurrentUserStorySummaryAsync(currentUserId.Value, HttpContext.RequestAborted)
                    : null,
                Friends = friends,
                SuggestedFriends = suggestedFriends,
                SuggestionInsertAfterPost = 3,
                UnreadNotificationCount = currentUserId.HasValue
                    ? await _socialService.GetUnreadNotificationCountAsync(currentUserId.Value)
                    : 0
            });
        }

        public async Task<IActionResult> Categories()
        {
            var categories = await _context.Categories
                .Include(c => c.Posts)
                .ToListAsync();
            return View(categories);
        }

        private int? GetCurrentUserId()
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                ? userId
                : null;
        }
    }
}
