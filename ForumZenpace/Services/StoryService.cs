using ForumZenpace.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;

namespace ForumZenpace.Services
{
    public sealed class StoryService
    {
        private const string SpotifyExternalContentType = "external/spotify";
        private const string YouTubeExternalContentType = "external/youtube";
        private const string AudioPlayerKind = "audio";
        private const string SpotifyPlayerKind = "spotify";
        private const string YouTubePlayerKind = "youtube";
        private static readonly HashSet<string> AllowedStoryImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".webp"
        };

        private static readonly HashSet<string> SupportedBackgroundStyles = new(StringComparer.OrdinalIgnoreCase)
        {
            StoryBackgroundStyles.Aurora,
            StoryBackgroundStyles.Sunset,
            StoryBackgroundStyles.Lagoon,
            StoryBackgroundStyles.Midnight
        };

        private const long MaxStoryImageSizeBytes = 10 * 1024 * 1024;
        private readonly ForumDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly StoryMusicLibraryService _storyMusicLibraryService;

        public StoryService(
            ForumDbContext context,
            IWebHostEnvironment environment,
            StoryMusicLibraryService storyMusicLibraryService)
        {
            _context = context;
            _environment = environment;
            _storyMusicLibraryService = storyMusicLibraryService;
        }

        public async Task<CurrentUserStorySummaryViewModel?> GetCurrentUserStorySummaryAsync(int userId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var activeStories = await _context.Stories
                .AsNoTracking()
                .Where(story => story.UserId == userId && story.ExpiresAt > now)
                .OrderByDescending(story => story.CreatedAt)
                .Select(story => new { story.Id })
                .ToListAsync(cancellationToken);

            if (activeStories.Count == 0)
            {
                return new CurrentUserStorySummaryViewModel();
            }

            return new CurrentUserStorySummaryViewModel
            {
                HasActiveStory = true,
                ActiveStoryCount = activeStories.Count,
                LatestStoryId = activeStories[0].Id
            };
        }

        public async Task<IReadOnlyList<FriendSummaryViewModel>> PopulateActiveStoryStateAsync(
            int currentUserId,
            IReadOnlyList<FriendSummaryViewModel> friends,
            CancellationToken cancellationToken = default)
        {
            if (friends.Count == 0)
            {
                return friends;
            }

            var storyAwareFriends = friends
                .Select(friend => new FriendSummaryViewModel
                {
                    UserId = friend.UserId,
                    Username = friend.Username,
                    DisplayName = friend.DisplayName,
                    AvatarUrl = friend.AvatarUrl,
                    IsMessageBlockedByViewer = friend.IsMessageBlockedByViewer,
                    IsMessageBlockedByOtherUser = friend.IsMessageBlockedByOtherUser
                })
                .ToList();

            var friendIds = storyAwareFriends
                .Select(friend => friend.UserId)
                .Distinct()
                .ToList();

            var now = DateTime.UtcNow;
            var activeStories = await _context.Stories
                .AsNoTracking()
                .Where(story => friendIds.Contains(story.UserId) && story.ExpiresAt > now)
                .OrderByDescending(story => story.CreatedAt)
                .Select(story => new { story.Id, story.UserId, story.CreatedAt })
                .ToListAsync(cancellationToken);

            if (activeStories.Count == 0)
            {
                return storyAwareFriends;
            }

            var viewedStoryIds = await _context.StoryViews
                .AsNoTracking()
                .Where(storyView => storyView.ViewerUserId == currentUserId && activeStories.Select(story => story.Id).Contains(storyView.StoryId))
                .Select(storyView => storyView.StoryId)
                .ToListAsync(cancellationToken);

            var viewedStoryIdSet = viewedStoryIds.ToHashSet();
            var storyGroups = activeStories
                .GroupBy(story => story.UserId)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var friend in storyAwareFriends)
            {
                if (!storyGroups.TryGetValue(friend.UserId, out var userStories))
                {
                    continue;
                }

                friend.HasActiveStory = userStories.Count > 0;
                friend.ActiveStoryCount = userStories.Count;
                friend.LatestStoryId = userStories[0].Id;
                friend.HasUnviewedStory = userStories.Any(story => !viewedStoryIdSet.Contains(story.Id));
            }

            return storyAwareFriends;
        }

        public async Task<bool> CanViewerAccessStoriesAsync(int profileUserId, int viewerUserId, CancellationToken cancellationToken = default)
        {
            if (profileUserId == viewerUserId)
            {
                return true;
            }

            return await AreFriendsAsync(profileUserId, viewerUserId, cancellationToken);
        }

        public async Task<IReadOnlyList<ProfileStorySummaryViewModel>> GetProfileStoriesAsync(
            int profileUserId,
            int? viewerUserId,
            bool canViewStories,
            CancellationToken cancellationToken = default)
        {
            if (!canViewStories)
            {
                return Array.Empty<ProfileStorySummaryViewModel>();
            }

            var stories = await _context.Stories
                .AsNoTracking()
                .Include(story => story.User)
                .Include(story => story.Views)
                .Where(story => story.UserId == profileUserId)
                .OrderByDescending(story => story.CreatedAt)
                .ToListAsync(cancellationToken);

            if (stories.Count == 0)
            {
                return Array.Empty<ProfileStorySummaryViewModel>();
            }

            return stories
                .Select(story => MapStorySummary(story, viewerUserId))
                .ToList();
        }

        public async Task<StoryViewerPageViewModel?> GetViewerPageAsync(int storyId, int viewerUserId, CancellationToken cancellationToken = default)
        {
            var story = await _context.Stories
                .Include(item => item.User)
                .Include(item => item.Views)
                .FirstOrDefaultAsync(item => item.Id == storyId, cancellationToken);

            if (story is null || !story.User.IsActive)
            {
                return null;
            }

            var isOwner = story.UserId == viewerUserId;
            if (!isOwner && !await AreFriendsAsync(story.UserId, viewerUserId, cancellationToken))
            {
                return null;
            }

            if (!isOwner && !story.Views.Any(view => view.ViewerUserId == viewerUserId))
            {
                var storyView = new StoryView
                {
                    StoryId = story.Id,
                    ViewerUserId = viewerUserId,
                    ViewedAt = DateTime.UtcNow
                };

                _context.StoryViews.Add(storyView);
                story.Views.Add(storyView);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var isStoryExpired = story.ExpiresAt <= DateTime.UtcNow;
            var authorStoriesQuery = _context.Stories
                .AsNoTracking()
                .Include(item => item.User)
                .Include(item => item.Views)
                .Where(item => item.UserId == story.UserId);

            if (isStoryExpired)
            {
                // If viewing an expired story from archive, just show it as a standalone story
                authorStoriesQuery = authorStoriesQuery.Where(item => item.Id == storyId);
            }
            else
            {
                // If viewing an active story, queue all active stories of the user
                authorStoriesQuery = authorStoriesQuery.Where(item => item.ExpiresAt > DateTime.UtcNow);
            }

            var authorStories = await authorStoriesQuery
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            var selectedIndex = authorStories.FindIndex(item => item.Id == storyId);
            if (selectedIndex < 0)
            {
                return null;
            }

            return new StoryViewerPageViewModel
            {
                CurrentUserId = viewerUserId,
                IsOwner = isOwner,
                CanManage = isOwner,
                ReturnProfileUsername = story.User.Username,
                Story = MapStorySummary(authorStories[selectedIndex], viewerUserId),
                Sequence = authorStories
                    .Select(item => new StorySequenceItemViewModel
                    {
                        Id = item.Id,
                        IsCurrent = item.Id == storyId,
                        IsExpired = item.ExpiresAt <= DateTime.UtcNow,
                        HasBeenViewedByViewer = item.UserId == viewerUserId || item.Views.Any(view => view.ViewerUserId == viewerUserId),
                        ImageUrl = item.ImageUrl,
                        MusicUrl = item.MusicUrl
                    })
                    .ToList(),
                PreviousStoryId = selectedIndex < authorStories.Count - 1 ? authorStories[selectedIndex + 1].Id : null,
                NextStoryId = selectedIndex > 0 ? authorStories[selectedIndex - 1].Id : null
            };
        }

        public async Task<CreateStoryResult> CreateStoryAsync(int userId, CreateStoryViewModel model, CancellationToken cancellationToken = default)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(account => account.Id == userId && account.IsActive, cancellationToken);

            if (user is null)
            {
                return CreateFailure("Khong tim thay tai khoan dang tai khoanh khac.");
            }

            var textContent = (model.TextContent ?? string.Empty).Trim();
            var hasImage = model.Image is not null && model.Image.Length > 0;

            if (!hasImage && string.IsNullOrWhiteSpace(textContent))
            {
                return CreateFailure("Khoanh khac can co anh hoac noi dung van ban.");
            }

            if (hasImage)
            {
                var validationError = ValidateStoryImage(model.Image);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    return CreateFailure(validationError);
                }
            }

            var backgroundStyle = NormalizeBackgroundStyle(model.BackgroundStyle);
            string? imageFileName = null;
            string? imageOriginalFileName = null;
            string? imageContentType = null;
            string? imageUrl = null;
            string? musicFileName = null;
            string? musicOriginalFileName = null;
            string? musicContentType = null;
            string? musicUrl = null;

            if (hasImage && model.Image is not null)
            {
                var saveResult = await SaveStoryImageAsync(model.Image, userId);
                imageFileName = saveResult.FileName;
                imageUrl = saveResult.ImageUrl;
                imageOriginalFileName = Path.GetFileName(model.Image.FileName);
                imageContentType = model.Image.ContentType ?? "application/octet-stream";
            }

            var externalMusicResult = TryResolveExternalMusic(
                model.MusicExternalUrl,
                model.MusicExternalTitle,
                model.MusicExternalArtist);

            if (!externalMusicResult.Success)
            {
                if (!string.IsNullOrWhiteSpace(imageFileName))
                {
                    DeleteStoryImageFile(imageFileName);
                }

                return CreateFailure(externalMusicResult.ErrorMessage);
            }

            if (externalMusicResult.Selection is not null)
            {
                musicFileName = externalMusicResult.Selection.FileName;
                musicOriginalFileName = externalMusicResult.Selection.DisplayName;
                musicContentType = externalMusicResult.Selection.ContentType;
                musicUrl = externalMusicResult.Selection.Url;
            }

            var story = new Story
            {
                UserId = userId,
                TextContent = string.IsNullOrWhiteSpace(textContent) ? null : textContent,
                BackgroundStyle = backgroundStyle,
                ImageFileName = imageFileName,
                ImageOriginalFileName = imageOriginalFileName,
                ImageContentType = imageContentType,
                ImageUrl = imageUrl,
                MusicFileName = musicFileName,
                MusicOriginalFileName = musicOriginalFileName,
                MusicContentType = musicContentType,
                MusicUrl = musicUrl,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            _context.Stories.Add(story);
            await _context.SaveChangesAsync(cancellationToken);

            var friendIds = await GetFriendIdsAsync(userId, cancellationToken);
            var deliveries = new List<StoryNotificationDeliveryResult>();
            if (friendIds.Count > 0)
            {
                var notificationContent = BuildStoryNotificationContent(user);
                var existingNotifications = await _context.Notifications
                    .Where(notification =>
                        friendIds.Contains(notification.UserId)
                        && notification.ActorUserId == userId
                        && notification.Type == NotificationTypes.StoryPublished
                        && !notification.IsRead)
                    .ToListAsync(cancellationToken);

                var notificationsByUserId = existingNotifications.ToDictionary(notification => notification.UserId);
                var createdAt = DateTime.UtcNow;

                foreach (var friendId in friendIds)
                {
                    if (!notificationsByUserId.TryGetValue(friendId, out var notification))
                    {
                        notification = new Notification
                        {
                            UserId = friendId,
                            ActorUserId = userId,
                            StoryId = story.Id,
                            Type = NotificationTypes.StoryPublished,
                            Content = notificationContent,
                            CreatedAt = createdAt
                        };

                        _context.Notifications.Add(notification);
                        notificationsByUserId[friendId] = notification;
                        continue;
                    }

                    notification.StoryId = story.Id;
                    notification.Content = notificationContent;
                    notification.CreatedAt = createdAt;
                }

                await _context.SaveChangesAsync(cancellationToken);

                var unreadCounts = await _context.Notifications
                    .AsNoTracking()
                    .Where(notification => friendIds.Contains(notification.UserId) && !notification.IsRead)
                    .GroupBy(notification => notification.UserId)
                    .Select(group => new
                    {
                        UserId = group.Key,
                        Count = group.Count()
                    })
                    .ToDictionaryAsync(item => item.UserId, item => item.Count, cancellationToken);

                deliveries.AddRange(friendIds.Select(friendId => new StoryNotificationDeliveryResult
                {
                    UserId = friendId,
                    Notification = MapNotificationItem(notificationsByUserId[friendId], user),
                    UnreadNotificationCount = unreadCounts.TryGetValue(friendId, out var unreadCount) ? unreadCount : 0
                }));
            }

            story.User = user;
            return new CreateStoryResult
            {
                Success = true,
                Story = MapStorySummary(story, userId),
                NotificationDeliveries = deliveries
            };
        }

        public async Task<DeleteStoryResult> DeleteStoryAsync(int userId, int storyId, CancellationToken cancellationToken = default)
        {
            var story = await _context.Stories
                .Include(item => item.Views)
                .FirstOrDefaultAsync(item => item.Id == storyId && item.UserId == userId, cancellationToken);

            if (story is null)
            {
                return new DeleteStoryResult
                {
                    Success = false,
                    ErrorMessage = "Khong tim thay khoanh khac de xoa."
                };
            }

            var notifications = await _context.Notifications
                .Where(notification => notification.StoryId == storyId)
                .ToListAsync(cancellationToken);

            _context.Notifications.RemoveRange(notifications);
            _context.StoryViews.RemoveRange(story.Views);
            _context.Stories.Remove(story);
            DeleteStoryImageFile(story.ImageFileName);
            DeleteStoryMusicFile(story.MusicFileName, story.MusicUrl);
            await _context.SaveChangesAsync(cancellationToken);

            return new DeleteStoryResult
            {
                Success = true,
                StoryId = storyId
            };
        }

        public NotificationItemViewModel MapNotificationItem(Notification notification, User actorUser)
        {
            return new NotificationItemViewModel
            {
                Id = notification.Id,
                Type = notification.Type,
                Content = notification.Content,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt,
                ActorUserId = actorUser.Id,
                ActorUsername = actorUser.Username,
                ActorDisplayName = GetDisplayName(actorUser.FullName, actorUser.Username),
                ActorAvatarUrl = actorUser.Avatar,
                StoryId = notification.StoryId,
                TargetUrl = notification.StoryId.HasValue ? GetStoryViewerUrl(notification.StoryId.Value) : string.Empty,
                ActionLabel = notification.StoryId.HasValue ? "Mo story" : string.Empty
            };
        }

        public static string GetStoryViewerUrl(int storyId)
        {
            return $"/Story/Viewer/{storyId}";
        }

        private async Task<List<int>> GetFriendIdsAsync(int userId, CancellationToken cancellationToken)
        {
            return await _context.Friendships
                .AsNoTracking()
                .Where(friendship => friendship.UserAId == userId || friendship.UserBId == userId)
                .Select(friendship => friendship.UserAId == userId ? friendship.UserBId : friendship.UserAId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        private async Task<bool> AreFriendsAsync(int firstUserId, int secondUserId, CancellationToken cancellationToken)
        {
            var (userAId, userBId) = OrderUsers(firstUserId, secondUserId);
            return await _context.Friendships
                .AsNoTracking()
                .AnyAsync(friendship => friendship.UserAId == userAId && friendship.UserBId == userBId, cancellationToken);
        }

        private async Task<(string FileName, string ImageUrl)> SaveStoryImageAsync(IFormFile image, int userId)
        {
            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var storyImageDirectory = Path.Combine(webRootPath, "uploads", "stories");
            Directory.CreateDirectory(storyImageDirectory);

            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            var fileName = $"story-{userId}-{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(storyImageDirectory, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await image.CopyToAsync(stream);

            return (fileName, $"/uploads/stories/{fileName}");
        }

        private async Task<(bool Success, string FileName, string MusicUrl, string ErrorMessage)> SaveAndTrimStoryMusicAsync(IFormFile musicFile, int userId, CancellationToken cancellationToken, int startSeconds = 0, int durationSeconds = 30)
        {
            if (musicFile.Length > 20 * 1024 * 1024)
            {
                return (false, string.Empty, string.Empty, "File nhac khong duoc vuot qua 20MB.");
            }

            var extension = Path.GetExtension(musicFile.FileName).ToLowerInvariant();
            if (extension != ".mp3" && extension != ".m4a" && extension != ".wav")
            {
                return (false, string.Empty, string.Empty, "Chi ho tro file MP3, M4A va WAV.");
            }

            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var storyMusicDirectory = Path.Combine(webRootPath, "uploads", "story-music");
            Directory.CreateDirectory(storyMusicDirectory);

            var uniqueId = Guid.NewGuid().ToString("N");
            var tempFileName = $"temp-{userId}-{uniqueId}{extension}";
            var tempFilePath = Path.Combine(storyMusicDirectory, tempFileName);

            // Save uploaded file to temp path first (will be used as fallback if FFmpeg fails)
            await using (var stream = new FileStream(tempFilePath, FileMode.Create))
            {
                await musicFile.CopyToAsync(stream, cancellationToken);
            }

            // Clamp values to safe range
            startSeconds = Math.Max(0, startSeconds);
            durationSeconds = Math.Clamp(durationSeconds, 5, 60);

            var finalFileName = $"story-music-{userId}-{uniqueId}.mp3";
            var finalFilePath = Path.Combine(storyMusicDirectory, finalFileName);

            var trimmedByFfmpeg = false;
            try
            {
                var ssArg = startSeconds > 0 ? $"-ss {startSeconds} " : string.Empty;
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{tempFilePath}\" {ssArg}-t {durationSeconds} -b:a 128k -ar 44100 -map a \"{finalFilePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    if (process.ExitCode == 0 && File.Exists(finalFilePath) && new FileInfo(finalFilePath).Length > 0)
                    {
                        trimmedByFfmpeg = true;
                    }
                }
            }
            catch (Exception)
            {
                // FFmpeg not available; will fall back to raw file
            }

            if (trimmedByFfmpeg)
            {
                // Clean up temp file - FFmpeg output is the keeper
                try { File.Delete(tempFilePath); } catch { /* ignore */ }
                return (true, finalFileName, $"/uploads/story-music/{finalFileName}", string.Empty);
            }

            // FFmpeg failed or unavailable — use the temp file as-is (rename it to final name)
            // Clean up any partial FFmpeg output first
            try { if (File.Exists(finalFilePath)) File.Delete(finalFilePath); } catch { /* ignore */ }

            if (File.Exists(tempFilePath))
            {
                var fallbackFileName = $"story-music-{userId}-{uniqueId}{extension}";
                var fallbackFilePath = Path.Combine(storyMusicDirectory, fallbackFileName);
                File.Move(tempFilePath, fallbackFilePath, overwrite: true);
                return (true, fallbackFileName, $"/uploads/story-music/{fallbackFileName}", string.Empty);
            }

            return (false, string.Empty, string.Empty, "Loi khi luu file nhac.");
        }

        private void DeleteStoryImageFile(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var storyImageDirectory = Path.Combine(webRootPath, "uploads", "stories");
            var filePath = Path.Combine(storyImageDirectory, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private void DeleteStoryMusicFile(string? fileName, string? musicUrl)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || string.IsNullOrWhiteSpace(musicUrl)
                || !musicUrl.StartsWith("/uploads/story-music/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var storyMusicDirectory = Path.Combine(webRootPath, "uploads", "story-music");
            var filePath = Path.Combine(storyMusicDirectory, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private static string? ValidateStoryImage(IFormFile? image)
        {
            if (image is null || image.Length == 0)
            {
                return "Anh khoanh khac khong hop le.";
            }

            if (image.Length > MaxStoryImageSizeBytes)
            {
                return "Anh story chi duoc toi da 10MB.";
            }

            var extension = Path.GetExtension(image.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedStoryImageExtensions.Contains(extension))
            {
                return "Story chi chap nhan file JPG, PNG, GIF hoac WEBP.";
            }

            return null;
        }

        private static ProfileStorySummaryViewModel MapStorySummary(Story story, int? viewerUserId)
        {
            var isOwner = viewerUserId.HasValue && viewerUserId.Value == story.UserId;
            var hasBeenViewedByViewer = isOwner
                || (viewerUserId.HasValue && story.Views.Any(view => view.ViewerUserId == viewerUserId.Value));
            var music = DescribeStoryMusic(story);

            return new ProfileStorySummaryViewModel
            {
                Id = story.Id,
                AuthorUserId = story.UserId,
                AuthorUsername = story.User.Username,
                AuthorDisplayName = GetDisplayName(story.User.FullName, story.User.Username),
                AuthorAvatarUrl = story.User.Avatar,
                TextContent = story.TextContent,
                PreviewText = BuildPreviewText(story),
                BackgroundStyle = NormalizeBackgroundStyle(story.BackgroundStyle),
                ImageUrl = story.ImageUrl,
                HasImage = !string.IsNullOrWhiteSpace(story.ImageUrl),
                MusicUrl = story.MusicUrl,
                MusicDisplayName = music.DisplayName,
                HasMusic = music.HasMusic,
                ShowInlineMusicPlayer = music.ShowInlinePlayer,
                CanEmbedMusic = music.CanEmbed,
                MusicEmbedUrl = music.EmbedUrl,
                MusicPlayerKind = music.PlayerKind,
                MusicPlayerKey = music.PlayerKey,
                MusicPlayerUri = music.PlayerUri,
                CanSeekMusic = music.CanSeek,
                MusicSourceLabel = music.SourceLabel,
                MusicActionUrl = music.ActionUrl,
                MusicActionLabel = music.ActionLabel,
                CreatedAt = story.CreatedAt,
                ExpiresAt = story.ExpiresAt,
                IsExpired = story.ExpiresAt <= DateTime.UtcNow,
                HasBeenViewedByViewer = hasBeenViewedByViewer,
                ViewCount = story.Views.Count
            };
        }

        private static string BuildPreviewText(Story story)
        {
            if (!string.IsNullOrWhiteSpace(story.TextContent))
            {
                var trimmed = story.TextContent.Trim();
                return trimmed.Length <= 96 ? trimmed : $"{trimmed[..93]}...";
            }

            return !string.IsNullOrWhiteSpace(story.ImageUrl)
                ? "Khoanh khac anh"
                : "Khoanh khac moi";
        }

        private static string BuildMusicDisplayName(Story story)
        {
            if (!string.IsNullOrWhiteSpace(story.MusicOriginalFileName))
            {
                return story.MusicOriginalFileName.Trim();
            }

            if (string.IsNullOrWhiteSpace(story.MusicFileName))
            {
                return string.Empty;
            }

            return Path.GetFileNameWithoutExtension(story.MusicFileName)
                .Replace("-", " ", StringComparison.Ordinal)
                .Replace("_", " ", StringComparison.Ordinal)
                .Trim();
        }

        private static StoryMusicSelectionResult TryResolveExternalMusic(
            string? rawUrl,
            string? rawTitle,
            string? rawArtist)
        {
            var trimmedUrl = (rawUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedUrl))
            {
                return StoryMusicSelectionResult.Empty;
            }

            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri)
                || !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return StoryMusicSelectionResult.Failure("Link nhac chi chap nhan URL http hoac https hop le.");
            }

            var spotifyTrackId = ExtractSpotifyTrackId(uri);
            if (!string.IsNullOrWhiteSpace(spotifyTrackId))
            {
                return StoryMusicSelectionResult.FromSelection(new StoryMusicSelection
                {
                    FileName = $"spotify:{spotifyTrackId}",
                    DisplayName = BuildExternalMusicDisplayName(rawTitle, rawArtist, "Spotify track"),
                    ContentType = SpotifyExternalContentType,
                    Url = $"https://open.spotify.com/track/{spotifyTrackId}"
                });
            }

            var youTubeVideoId = ExtractYouTubeVideoId(uri);
            if (!string.IsNullOrWhiteSpace(youTubeVideoId))
            {
                return StoryMusicSelectionResult.FromSelection(new StoryMusicSelection
                {
                    FileName = $"youtube:{youTubeVideoId}",
                    DisplayName = BuildExternalMusicDisplayName(rawTitle, rawArtist, "YouTube track"),
                    ContentType = YouTubeExternalContentType,
                    Url = $"https://www.youtube.com/watch?v={youTubeVideoId}"
                });
            }

            return StoryMusicSelectionResult.Failure("Story hien chi ho tro link bai hat tu Spotify hoac video nhac YouTube.");
        }

        private static StoryMusicDescriptor DescribeStoryMusic(Story story)
        {
            if (string.IsNullOrWhiteSpace(story.MusicUrl))
            {
                return StoryMusicDescriptor.Empty;
            }

            var displayName = BuildMusicDisplayName(story);
            var musicUrl = story.MusicUrl.Trim();

            if (string.Equals(story.MusicContentType, SpotifyExternalContentType, StringComparison.OrdinalIgnoreCase))
            {
                var spotifyTrackId = ExtractStoredExternalId(story.MusicFileName, "spotify");
                return new StoryMusicDescriptor
                {
                    HasMusic = true,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Spotify track" : displayName,
                    CanEmbed = !string.IsNullOrWhiteSpace(spotifyTrackId),
                    EmbedUrl = string.IsNullOrWhiteSpace(spotifyTrackId)
                        ? string.Empty
                        : $"https://open.spotify.com/embed/track/{spotifyTrackId}",
                    PlayerKind = string.IsNullOrWhiteSpace(spotifyTrackId) ? string.Empty : SpotifyPlayerKind,
                    PlayerKey = spotifyTrackId ?? string.Empty,
                    PlayerUri = string.IsNullOrWhiteSpace(spotifyTrackId) ? string.Empty : $"spotify:track:{spotifyTrackId}",
                    CanSeek = !string.IsNullOrWhiteSpace(spotifyTrackId),
                    SourceLabel = "Spotify",
                    ActionUrl = musicUrl,
                    ActionLabel = "Mo tren Spotify"
                };
            }

            if (string.Equals(story.MusicContentType, YouTubeExternalContentType, StringComparison.OrdinalIgnoreCase))
            {
                var videoId = ExtractStoredExternalId(story.MusicFileName, "youtube");
                return new StoryMusicDescriptor
                {
                    HasMusic = true,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? "YouTube track" : displayName,
                    CanEmbed = !string.IsNullOrWhiteSpace(videoId),
                    EmbedUrl = string.IsNullOrWhiteSpace(videoId)
                        ? string.Empty
                        : $"https://www.youtube.com/embed/{videoId}?rel=0&playsinline=1",
                    PlayerKind = string.IsNullOrWhiteSpace(videoId) ? string.Empty : YouTubePlayerKind,
                    PlayerKey = videoId ?? string.Empty,
                    CanSeek = !string.IsNullOrWhiteSpace(videoId),
                    SourceLabel = "YouTube",
                    ActionUrl = musicUrl,
                    ActionLabel = "Mo tren YouTube"
                };
            }

            return new StoryMusicDescriptor
            {
                HasMusic = true,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Nhac story" : displayName,
                ShowInlinePlayer = true,
                PlayerKind = AudioPlayerKind,
                CanSeek = true,
                SourceLabel = musicUrl.StartsWith("/library/story-music/", StringComparison.OrdinalIgnoreCase)
                    ? "Thu vien"
                    : musicUrl.StartsWith("/uploads/story-music/", StringComparison.OrdinalIgnoreCase)
                        ? "Tep cu"
                        : "Audio"
            };
        }

        private static string BuildExternalMusicDisplayName(string? rawTitle, string? rawArtist, string fallback)
        {
            var title = Truncate(rawTitle, 120);
            var artist = Truncate(rawArtist, 120);

            if (string.IsNullOrWhiteSpace(title))
            {
                return fallback;
            }

            return string.IsNullOrWhiteSpace(artist)
                ? title
                : $"{title} - {artist}";
        }

        private static string? ExtractSpotifyTrackId(Uri uri)
        {
            var host = uri.Host.ToLowerInvariant();
            if (!host.Contains("spotify.com", StringComparison.Ordinal))
            {
                return null;
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var trackIndex = Array.FindIndex(segments, segment => string.Equals(segment, "track", StringComparison.OrdinalIgnoreCase));
            if (trackIndex < 0 || trackIndex >= segments.Length - 1)
            {
                return null;
            }

            var trackId = segments[trackIndex + 1];
            return IsValidSpotifyId(trackId) ? trackId : null;
        }

        private static string? ExtractYouTubeVideoId(Uri uri)
        {
            var host = uri.Host.ToLowerInvariant();
            if (string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                var shortId = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return IsValidYouTubeId(shortId) ? shortId : null;
            }

            if (!host.Contains("youtube.com", StringComparison.Ordinal))
            {
                return null;
            }

            var path = uri.AbsolutePath.Trim('/');
            if (string.Equals(path, "watch", StringComparison.OrdinalIgnoreCase))
            {
                var query = QueryHelpers.ParseQuery(uri.Query);
                if (query.TryGetValue("v", out var value))
                {
                    var videoId = value.FirstOrDefault();
                    return IsValidYouTubeId(videoId) ? videoId : null;
                }

                return null;
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 2
                && (string.Equals(segments[0], "shorts", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segments[0], "embed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segments[0], "live", StringComparison.OrdinalIgnoreCase)))
            {
                return IsValidYouTubeId(segments[1]) ? segments[1] : null;
            }

            return null;
        }

        private static string? ExtractStoredExternalId(string? fileName, string expectedPrefix)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var prefix = $"{expectedPrefix}:";
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var id = fileName[prefix.Length..];
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        private static bool IsValidSpotifyId(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length is >= 16 and <= 32
                && value.All(char.IsLetterOrDigit);
        }

        private static bool IsValidYouTubeId(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 11
                && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string BuildStoryNotificationContent(User actorUser)
        {
            return $"{GetDisplayName(actorUser.FullName, actorUser.Username)} vua dang khoanh khac moi.";
        }

        private static string NormalizeBackgroundStyle(string? backgroundStyle)
        {
            if (string.IsNullOrWhiteSpace(backgroundStyle))
            {
                return StoryBackgroundStyles.Aurora;
            }

            return SupportedBackgroundStyles.Contains(backgroundStyle.Trim())
                ? backgroundStyle.Trim().ToLowerInvariant()
                : StoryBackgroundStyles.Aurora;
        }

        private static string GetDisplayName(string? fullName, string username)
        {
            return string.IsNullOrWhiteSpace(fullName) ? username : fullName.Trim();
        }

        private static (int UserAId, int UserBId) OrderUsers(int firstUserId, int secondUserId)
        {
            return firstUserId < secondUserId
                ? (firstUserId, secondUserId)
                : (secondUserId, firstUserId);
        }

        private static CreateStoryResult CreateFailure(string message)
        {
            return new CreateStoryResult
            {
                Success = false,
                ErrorMessage = message
            };
        }
    }

    public sealed class CreateStoryResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public ProfileStorySummaryViewModel? Story { get; set; }
        public IReadOnlyList<StoryNotificationDeliveryResult> NotificationDeliveries { get; set; } = Array.Empty<StoryNotificationDeliveryResult>();
    }

    public sealed class StoryNotificationDeliveryResult
    {
        public int UserId { get; set; }
        public int UnreadNotificationCount { get; set; }
        public NotificationItemViewModel Notification { get; set; } = null!;
    }

    public sealed class DeleteStoryResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int StoryId { get; set; }
    }

    internal sealed class StoryMusicSelectionResult
    {
        public static StoryMusicSelectionResult Empty { get; } = new() { Success = true };

        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public StoryMusicSelection? Selection { get; set; }

        public static StoryMusicSelectionResult Failure(string message)
        {
            return new StoryMusicSelectionResult
            {
                Success = false,
                ErrorMessage = message
            };
        }

        public static StoryMusicSelectionResult FromSelection(StoryMusicSelection selection)
        {
            return new StoryMusicSelectionResult
            {
                Success = true,
                Selection = selection
            };
        }
    }

    internal sealed class StoryMusicSelection
    {
        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    internal sealed class StoryMusicDescriptor
    {
        public static StoryMusicDescriptor Empty { get; } = new();

        public bool HasMusic { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool ShowInlinePlayer { get; set; }
        public bool CanEmbed { get; set; }
        public string EmbedUrl { get; set; } = string.Empty;
        public string PlayerKind { get; set; } = string.Empty;
        public string PlayerKey { get; set; } = string.Empty;
        public string PlayerUri { get; set; } = string.Empty;
        public bool CanSeek { get; set; }
        public string SourceLabel { get; set; } = string.Empty;
        public string ActionUrl { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
    }
}
