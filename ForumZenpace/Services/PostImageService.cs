using System.Text.RegularExpressions;
using ForumZenpace.Models;
using Microsoft.EntityFrameworkCore;

namespace ForumZenpace.Services
{
    public class PostImageService
    {
        private static readonly HashSet<string> AllowedPostImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".webp"
        };
        
        private static readonly Regex MarkdownImageRegex = new(@"!\[[^\]]*\]\((?<url>[^)]+)\)", RegexOptions.Compiled);
        private const long MaxPostImageSizeBytes = 10 * 1024 * 1024;

        private readonly ForumDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public PostImageService(ForumDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public string? ValidatePostImage(IFormFile? image)
        {
            if (image is null || image.Length == 0)
            {
                return "Anh tai len khong hop le.";
            }

            if (image.Length > MaxPostImageSizeBytes)
            {
                return "Anh trong bai viet chi duoc toi da 10MB.";
            }

            var extension = Path.GetExtension(image.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedPostImageExtensions.Contains(extension))
            {
                return "Chi chap nhan file JPG, PNG, GIF hoac WEBP.";
            }

            return null;
        }

        public async Task<(string FileName, string ImageUrl)> SavePostImageAsync(IFormFile image, int userId)
        {
            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var postImageDirectory = Path.Combine(webRootPath, "uploads", "posts");
            Directory.CreateDirectory(postImageDirectory);

            var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            var fileName = $"post-{userId}-{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(postImageDirectory, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await image.CopyToAsync(stream);

            return (fileName, $"/uploads/posts/{fileName}");
        }

        public async Task AttachDraftImagesToPostAsync(int postId, int userId, string draftToken, string content)
        {
            if (string.IsNullOrWhiteSpace(draftToken))
            {
                return;
            }

            var normalizedDraftToken = SanitizeDraftToken(draftToken);
            var referencedUrls = ExtractImageUrls(content);
            var draftImages = await _context.PostImages
                .Where(pi => pi.UserId == userId && pi.PostId == null && pi.DraftToken == normalizedDraftToken)
                .ToListAsync();

            var orphanedDraftImages = draftImages
                .Where(pi => !referencedUrls.Contains(pi.ImageUrl))
                .ToList();

            if (orphanedDraftImages.Count > 0)
            {
                _context.PostImages.RemoveRange(orphanedDraftImages);
                DeletePostImageFiles(orphanedDraftImages);
            }

            foreach (var image in draftImages.Except(orphanedDraftImages))
            {
                image.PostId = postId;
                image.DraftToken = null;
            }

            await _context.SaveChangesAsync();
        }

        public async Task SyncPostImagesAsync(int postId, int userId, string content)
        {
            var referencedUrls = ExtractImageUrls(content);
            var postImages = await _context.PostImages
                .Where(pi => pi.PostId == postId && pi.UserId == userId)
                .ToListAsync();

            var removedImages = postImages
                .Where(pi => !referencedUrls.Contains(pi.ImageUrl))
                .ToList();

            if (removedImages.Count == 0)
            {
                return;
            }

            _context.PostImages.RemoveRange(removedImages);
            DeletePostImageFiles(removedImages);
            await _context.SaveChangesAsync();
        }

        public void DeletePostImageFiles(IEnumerable<PostImage> postImages)
        {
            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var postImageDirectory = Path.Combine(webRootPath, "uploads", "posts");

            foreach (var postImage in postImages)
            {
                if (string.IsNullOrWhiteSpace(postImage.FileName))
                {
                    continue;
                }

                var filePath = Path.Combine(postImageDirectory, postImage.FileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
        }

        public string CreateDraftToken()
        {
            return Guid.NewGuid().ToString("N");
        }

        public string SanitizeDraftToken(string? draftToken)
        {
            if (string.IsNullOrWhiteSpace(draftToken))
            {
                return string.Empty;
            }

            var trimmed = draftToken.Trim();
            return trimmed.Length <= 64 ? trimmed : trimmed[..64];
        }

        public string CreateImageMarkdown(PostImage postImage)
        {
            var altText = Path.GetFileNameWithoutExtension(postImage.OriginalFileName)
                .Replace("-", " ", StringComparison.Ordinal)
                .Trim();

            if (string.IsNullOrWhiteSpace(altText))
            {
                altText = "Hinh anh bai viet";
            }

            return $"![{altText}]({postImage.ImageUrl})";
        }

        public HashSet<string> ExtractImageUrls(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return MarkdownImageRegex.Matches(content)
                .Select(match => match.Groups["url"].Value.Trim())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
