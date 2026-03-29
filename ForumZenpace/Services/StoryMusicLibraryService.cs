using System.Text.Json;
using ForumZenpace.Models;
using Microsoft.AspNetCore.StaticFiles;

namespace ForumZenpace.Services
{
    public sealed class StoryMusicLibraryService
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",
            ".wav",
            ".ogg",
            ".m4a"
        };

        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<StoryMusicLibraryService> _logger;
        private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

        public StoryMusicLibraryService(IWebHostEnvironment environment, ILogger<StoryMusicLibraryService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public IReadOnlyList<StoryMusicTrackOptionViewModel> GetAvailableTracks()
        {
            var libraryDirectory = EnsureLibraryDirectory();
            var catalogPath = EnsureCatalogPath();

            var tracksFromCatalog = LoadCatalogTracks(catalogPath, libraryDirectory);
            if (tracksFromCatalog.Count > 0)
            {
                return tracksFromCatalog;
            }

            return ScanLibraryDirectory(libraryDirectory);
        }

        public StoryMusicTrackOptionViewModel? FindTrack(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return GetAvailableTracks()
                .FirstOrDefault(track => string.Equals(track.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private List<StoryMusicTrackOptionViewModel> LoadCatalogTracks(string catalogPath, string libraryDirectory)
        {
            if (!File.Exists(catalogPath))
            {
                return new List<StoryMusicTrackOptionViewModel>();
            }

            try
            {
                var json = File.ReadAllText(catalogPath);
                var entries = JsonSerializer.Deserialize<List<StoryMusicCatalogEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (entries is null || entries.Count == 0)
                {
                    return new List<StoryMusicTrackOptionViewModel>();
                }

                return entries
                    .Select((entry, index) => BuildTrack(entry.FileName, entry.Title, entry.Artist, entry.SortOrder ?? index, libraryDirectory))
                    .Where(track => track is not null)
                    .OrderBy(track => track!.SortOrder)
                    .ThenBy(track => track!.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                    .Select(track => track!.ToViewModel())
                    .ToList();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Khong the doc catalog nhac story tu {CatalogPath}.", catalogPath);
                return new List<StoryMusicTrackOptionViewModel>();
            }
        }

        private List<StoryMusicTrackOptionViewModel> ScanLibraryDirectory(string libraryDirectory)
        {
            if (!Directory.Exists(libraryDirectory))
            {
                return new List<StoryMusicTrackOptionViewModel>();
            }

            return Directory.EnumerateFiles(libraryDirectory)
                .Where(filePath => SupportedExtensions.Contains(Path.GetExtension(filePath)))
                .Select((filePath, index) => BuildTrack(Path.GetFileName(filePath), null, null, index, libraryDirectory))
                .Where(track => track is not null)
                .OrderBy(track => track!.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .Select(track => track!.ToViewModel())
                .ToList();
        }

        private StoryMusicTrackDescriptor? BuildTrack(
            string? fileName,
            string? title,
            string? artist,
            int sortOrder,
            string libraryDirectory)
        {
            var safeFileName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                return null;
            }

            var extension = Path.GetExtension(safeFileName);
            if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
            {
                return null;
            }

            var physicalPath = Path.Combine(libraryDirectory, safeFileName);
            if (!File.Exists(physicalPath))
            {
                return null;
            }

            if (!_contentTypeProvider.TryGetContentType(safeFileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            var normalizedTitle = string.IsNullOrWhiteSpace(title)
                ? BuildTitleFromFileName(safeFileName)
                : title.Trim();

            var normalizedArtist = string.IsNullOrWhiteSpace(artist)
                ? string.Empty
                : artist.Trim();

            return new StoryMusicTrackDescriptor
            {
                Key = safeFileName,
                Title = normalizedTitle,
                Artist = normalizedArtist,
                AudioUrl = $"/library/story-music/{Uri.EscapeDataString(safeFileName)}",
                ContentType = contentType,
                SortOrder = sortOrder
            };
        }

        private string EnsureLibraryDirectory()
        {
            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var libraryDirectory = Path.Combine(webRootPath, "library", "story-music");
            Directory.CreateDirectory(libraryDirectory);
            return libraryDirectory;
        }

        private string EnsureCatalogPath()
        {
            var catalogDirectory = Path.Combine(_environment.ContentRootPath, "Data", "StoryMusic");
            Directory.CreateDirectory(catalogDirectory);

            var catalogPath = Path.Combine(catalogDirectory, "catalog.json");
            if (!File.Exists(catalogPath))
            {
                File.WriteAllText(catalogPath, "[]");
            }

            return catalogPath;
        }

        private static string BuildTitleFromFileName(string fileName)
        {
            var rawName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "Story Track";
            }

            var sanitized = rawName
                .Replace("_", " ", StringComparison.Ordinal)
                .Replace("-", " ", StringComparison.Ordinal)
                .Trim();

            return string.Join(' ', sanitized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Length == 0
                    ? token
                    : char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant()));
        }

        private sealed class StoryMusicCatalogEntry
        {
            public string? FileName { get; set; }
            public string? Title { get; set; }
            public string? Artist { get; set; }
            public int? SortOrder { get; set; }
        }

        private sealed class StoryMusicTrackDescriptor
        {
            public string Key { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public string AudioUrl { get; set; } = string.Empty;
            public string ContentType { get; set; } = "application/octet-stream";
            public int SortOrder { get; set; }

            public string DisplayLabel => string.IsNullOrWhiteSpace(Artist)
                ? Title
                : $"{Title} - {Artist}";

            public StoryMusicTrackOptionViewModel ToViewModel()
            {
                return new StoryMusicTrackOptionViewModel
                {
                    Key = Key,
                    Title = Title,
                    Artist = Artist,
                    DisplayLabel = DisplayLabel,
                    AudioUrl = AudioUrl,
                    ContentType = ContentType
                };
            }
        }
    }
}
