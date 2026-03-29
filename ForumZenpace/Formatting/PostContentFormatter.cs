using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace ForumZenpace.Formatting
{
    public static class PostContentFormatter
    {
        private static readonly Regex HeadingRegex = new(@"^(#{1,4})\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex UnorderedListRegex = new(@"^[-*+]\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex OrderedListRegex = new(@"^\d+\.\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex ImageRegex = new(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new(@"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex HeadingMarkerRegex = new(@"^#{1,4}\s+", RegexOptions.Compiled);
        private static readonly Regex QuoteMarkerRegex = new(@"^>\s?", RegexOptions.Compiled);
        private static readonly Regex UnorderedMarkerRegex = new(@"^[-*+]\s+", RegexOptions.Compiled);
        private static readonly Regex OrderedMarkerRegex = new(@"^\d+\.\s+", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        // Matches a bare URL (http/https) as the entire trimmed content of a paragraph line
        private static readonly Regex BareUrlLineRegex = new(
            @"^(https?://[^\s""'<>\[\]]{4,})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Matches bare URLs embedded anywhere inside inline text
        private static readonly Regex InlineBareUrlRegex = new(
            @"(?<!\()(?<!\]\()https?://[^\s""'<>\[\]]{4,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);


        public static string ToHtml(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var html = new StringBuilder();
            var paragraphLines = new List<string>();
            var quoteLines = new List<string>();
            var codeLines = new List<string>();
            var listState = ListState.None;
            var inCodeBlock = false;

            foreach (var line in NormalizeLineEndings(markdown).Split('\n'))
            {
                var trimmedLine = line.TrimEnd();
                var markerLine = trimmedLine.TrimStart();

                if (inCodeBlock)
                {
                    if (markerLine.StartsWith("```", StringComparison.Ordinal))
                    {
                        FlushCodeBlock();
                        inCodeBlock = false;
                        continue;
                    }

                    codeLines.Add(trimmedLine);
                    continue;
                }

                if (markerLine.StartsWith("```", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    FlushQuote();
                    CloseList();
                    inCodeBlock = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    FlushParagraph();
                    FlushQuote();
                    CloseList();
                    continue;
                }

                if (TryReadHeading(markerLine, out var headingLevel, out var headingText))
                {
                    FlushParagraph();
                    FlushQuote();
                    CloseList();
                    html.Append("<h")
                        .Append(headingLevel)
                        .Append('>')
                        .Append(FormatInline(headingText))
                        .Append("</h")
                        .Append(headingLevel)
                        .Append('>');
                    continue;
                }

                if (TryReadQuote(markerLine, out var quoteText))
                {
                    FlushParagraph();
                    CloseList();
                    quoteLines.Add(quoteText);
                    continue;
                }

                FlushQuote();

                if (TryReadUnorderedItem(markerLine, out var unorderedItem))
                {
                    FlushParagraph();
                    EnsureList(ListState.Unordered);
                    html.Append("<li>")
                        .Append(FormatInline(unorderedItem))
                        .Append("</li>");
                    continue;
                }

                if (TryReadOrderedItem(markerLine, out var orderedItem))
                {
                    FlushParagraph();
                    EnsureList(ListState.Ordered);
                    html.Append("<li>")
                        .Append(FormatInline(orderedItem))
                        .Append("</li>");
                    continue;
                }

                CloseList();
                paragraphLines.Add(trimmedLine.Trim());
            }

            FlushParagraph();
            FlushQuote();
            CloseList();

            if (inCodeBlock)
            {
                FlushCodeBlock();
            }

            return html.ToString();

            void FlushParagraph()
            {
                if (paragraphLines.Count == 0)
                {
                    return;
                }

                // If the paragraph is a single bare URL, render as a link card
                if (paragraphLines.Count == 1)
                {
                    var singleLine = paragraphLines[0];
                    var bareUrlMatch = BareUrlLineRegex.Match(singleLine);
                    if (bareUrlMatch.Success && TryCreateSafeUrl(bareUrlMatch.Groups[1].Value, out var cardUrl))
                    {
                        var displayUrl = cardUrl.Length > 60 ? cardUrl[..57] + "..." : cardUrl;
                        html.Append("<a class=\"markdown-link-card\" href=\"")
                            .Append(HtmlEncoder.Default.Encode(cardUrl))
                            .Append("\" target=\"_blank\" rel=\"noopener noreferrer\">")
                            .Append("<span class=\"markdown-link-card__icon\"><i class=\"fa-solid fa-link\"></i></span>")
                            .Append("<span class=\"markdown-link-card__body\">")
                            .Append("<span class=\"markdown-link-card__url\">")
                            .Append(HtmlEncoder.Default.Encode(displayUrl))
                            .Append("</span>")
                            .Append("<span class=\"markdown-link-card__hint\">Nhấn để mở liên kết</span>")
                            .Append("</span>")
                            .Append("<span class=\"markdown-link-card__arrow\"><i class=\"fa-solid fa-arrow-up-right-from-square\"></i></span>")
                            .Append("</a>");
                        paragraphLines.Clear();
                        return;
                    }
                }

                html.Append("<p>");
                for (var index = 0; index < paragraphLines.Count; index++)
                {
                    if (index > 0)
                    {
                        html.Append("<br />");
                    }

                    html.Append(FormatInline(paragraphLines[index]));
                }

                html.Append("</p>");
                paragraphLines.Clear();
            }


            void FlushQuote()
            {
                if (quoteLines.Count == 0)
                {
                    return;
                }

                html.Append("<blockquote><p>");
                for (var index = 0; index < quoteLines.Count; index++)
                {
                    if (index > 0)
                    {
                        html.Append("<br />");
                    }

                    html.Append(FormatInline(quoteLines[index]));
                }

                html.Append("</p></blockquote>");
                quoteLines.Clear();
            }

            void FlushCodeBlock()
            {
                html.Append("<pre><code>")
                    .Append(HtmlEncoder.Default.Encode(string.Join('\n', codeLines)))
                    .Append("</code></pre>");
                codeLines.Clear();
            }

            void EnsureList(ListState targetState)
            {
                if (listState == targetState)
                {
                    return;
                }

                CloseList();
                html.Append(targetState == ListState.Unordered ? "<ul>" : "<ol>");
                listState = targetState;
            }

            void CloseList()
            {
                if (listState == ListState.None)
                {
                    return;
                }

                html.Append(listState == ListState.Unordered ? "</ul>" : "</ol>");
                listState = ListState.None;
            }
        }

        public static string ToExcerpt(string? markdown, int maxLength = 180)
        {
            var plainText = ToPlainText(markdown);
            if (plainText.Length <= maxLength)
            {
                return plainText;
            }

            return $"{plainText[..maxLength].TrimEnd()}...";
        }

        public static string ToPlainText(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var lines = new List<string>();
            var inCodeBlock = false;

            foreach (var line in NormalizeLineEndings(markdown).Split('\n'))
            {
                var trimmedLine = line.TrimEnd();
                var markerLine = trimmedLine.TrimStart();

                if (markerLine.StartsWith("```", StringComparison.Ordinal))
                {
                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                var cleaned = inCodeBlock ? trimmedLine : markerLine;
                if (!inCodeBlock)
                {
                    cleaned = HeadingMarkerRegex.Replace(cleaned, string.Empty);
                    cleaned = QuoteMarkerRegex.Replace(cleaned, string.Empty);
                    cleaned = UnorderedMarkerRegex.Replace(cleaned, string.Empty);
                    cleaned = OrderedMarkerRegex.Replace(cleaned, string.Empty);
                }

                cleaned = StripInlineMarkdown(cleaned);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    lines.Add(cleaned.Trim());
                }
            }

            return WhitespaceRegex.Replace(string.Join(' ', lines), " ").Trim();
        }

        private static string NormalizeLineEndings(string content)
        {
            return content.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static bool TryReadHeading(string line, out int level, out string content)
        {
            var match = HeadingRegex.Match(line);
            if (match.Success)
            {
                level = match.Groups[1].Value.Length;
                content = match.Groups[2].Value.Trim();
                return true;
            }

            level = 0;
            content = string.Empty;
            return false;
        }

        private static bool TryReadQuote(string line, out string content)
        {
            if (line.StartsWith(">", StringComparison.Ordinal))
            {
                content = line[1..].TrimStart();
                return true;
            }

            content = string.Empty;
            return false;
        }

        private static bool TryReadUnorderedItem(string line, out string content)
        {
            var match = UnorderedListRegex.Match(line);
            if (match.Success)
            {
                content = match.Groups[1].Value.Trim();
                return true;
            }

            content = string.Empty;
            return false;
        }

        private static bool TryReadOrderedItem(string line, out string content)
        {
            var match = OrderedListRegex.Match(line);
            if (match.Success)
            {
                content = match.Groups[1].Value.Trim();
                return true;
            }

            content = string.Empty;
            return false;
        }

        private static string StripInlineMarkdown(string content)
        {
            var cleaned = ImageRegex.Replace(content, "${alt}");
            cleaned = LinkRegex.Replace(cleaned, "${label}");
            cleaned = cleaned.Replace("**", string.Empty, StringComparison.Ordinal);
            cleaned = cleaned.Replace("*", string.Empty, StringComparison.Ordinal);
            cleaned = cleaned.Replace("`", string.Empty, StringComparison.Ordinal);
            cleaned = cleaned.Replace("\\", string.Empty, StringComparison.Ordinal);
            return cleaned;
        }

        private static string FormatInline(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            for (var index = 0; index < content.Length;)
            {
                if (content[index] == '\\' && index + 1 < content.Length)
                {
                    builder.Append(HtmlEncoder.Default.Encode(content[index + 1].ToString()));
                    index += 2;
                    continue;
                }

                if (TryRenderImage(content, index, out var imageHtml, out var consumedLength))
                {
                    builder.Append(imageHtml);
                    index += consumedLength;
                    continue;
                }

                if (TryRenderLink(content, index, out var linkHtml, out consumedLength))
                {
                    builder.Append(linkHtml);
                    index += consumedLength;
                    continue;
                }

                if (TryRenderWrapped(content, index, "**", "strong", out var strongHtml, out consumedLength))
                {
                    builder.Append(strongHtml);
                    index += consumedLength;
                    continue;
                }

                if (TryRenderWrapped(content, index, "*", "em", out var emphasisHtml, out consumedLength))
                {
                    builder.Append(emphasisHtml);
                    index += consumedLength;
                    continue;
                }

                if (TryRenderCode(content, index, out var codeHtml, out consumedLength))
                {
                    builder.Append(codeHtml);
                    index += consumedLength;
                    continue;
                }

                builder.Append(HtmlEncoder.Default.Encode(content[index].ToString()));
                index++;
            }

            return builder.ToString();
        }

        private static bool TryRenderImage(string content, int startIndex, out string html, out int consumedLength)
        {
            if (startIndex + 1 >= content.Length || content[startIndex] != '!' || content[startIndex + 1] != '[')
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var closeBracketIndex = FindUnescaped(content, "]", startIndex + 2);
            if (closeBracketIndex < 0 || closeBracketIndex + 1 >= content.Length || content[closeBracketIndex + 1] != '(')
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var closeParenIndex = FindUnescaped(content, ")", closeBracketIndex + 2);
            if (closeParenIndex < 0)
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var alt = content[(startIndex + 2)..closeBracketIndex];
            var url = content[(closeBracketIndex + 2)..closeParenIndex].Trim();
            if (!TryCreateSafeUrl(url, out var safeUrl))
            {
                html = HtmlEncoder.Default.Encode(content[startIndex..(closeParenIndex + 1)]);
                consumedLength = closeParenIndex - startIndex + 1;
                return true;
            }

            html = new StringBuilder()
                .Append("<img class=\"markdown-image\" src=\"")
                .Append(HtmlEncoder.Default.Encode(safeUrl))
                .Append("\" alt=\"")
                .Append(HtmlEncoder.Default.Encode(alt))
                .Append("\" loading=\"lazy\" />")
                .ToString();
            consumedLength = closeParenIndex - startIndex + 1;
            return true;
        }

        private static bool TryRenderLink(string content, int startIndex, out string html, out int consumedLength)
        {
            if (content[startIndex] != '[')
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var closeBracketIndex = FindUnescaped(content, "]", startIndex + 1);
            if (closeBracketIndex < 0 || closeBracketIndex + 1 >= content.Length || content[closeBracketIndex + 1] != '(')
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var closeParenIndex = FindUnescaped(content, ")", closeBracketIndex + 2);
            if (closeParenIndex < 0)
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var label = content[(startIndex + 1)..closeBracketIndex];
            var url = content[(closeBracketIndex + 2)..closeParenIndex].Trim();
            if (!TryCreateSafeUrl(url, out var safeUrl))
            {
                html = HtmlEncoder.Default.Encode(content[startIndex..(closeParenIndex + 1)]);
                consumedLength = closeParenIndex - startIndex + 1;
                return true;
            }

            html = new StringBuilder()
                .Append("<a href=\"")
                .Append(HtmlEncoder.Default.Encode(safeUrl))
                .Append("\" target=\"_blank\" rel=\"noopener noreferrer\">")
                .Append(FormatInline(label))
                .Append("</a>")
                .ToString();
            consumedLength = closeParenIndex - startIndex + 1;
            return true;
        }

        private static bool TryRenderWrapped(string content, int startIndex, string marker, string tagName, out string html, out int consumedLength)
        {
            if (startIndex + marker.Length > content.Length
                || !string.Equals(content.Substring(startIndex, marker.Length), marker, StringComparison.Ordinal))
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var closingIndex = FindUnescaped(content, marker, startIndex + marker.Length);
            if (closingIndex <= startIndex + marker.Length)
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var innerContent = content.Substring(startIndex + marker.Length, closingIndex - startIndex - marker.Length);
            html = $"<{tagName}>{FormatInline(innerContent)}</{tagName}>";
            consumedLength = closingIndex - startIndex + marker.Length;
            return true;
        }

        private static bool TryRenderCode(string content, int startIndex, out string html, out int consumedLength)
        {
            if (content[startIndex] != '`')
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            var closingIndex = FindUnescaped(content, "`", startIndex + 1);
            if (closingIndex <= startIndex + 1)
            {
                html = string.Empty;
                consumedLength = 0;
                return false;
            }

            html = new StringBuilder()
                .Append("<code>")
                .Append(HtmlEncoder.Default.Encode(content[(startIndex + 1)..closingIndex]))
                .Append("</code>")
                .ToString();
            consumedLength = closingIndex - startIndex + 1;
            return true;
        }

        private static int FindUnescaped(string content, string marker, int startIndex)
        {
            var index = startIndex;
            while (index < content.Length)
            {
                var foundIndex = content.IndexOf(marker, index, StringComparison.Ordinal);
                if (foundIndex < 0)
                {
                    return -1;
                }

                if (foundIndex == 0 || content[foundIndex - 1] != '\\')
                {
                    return foundIndex;
                }

                index = foundIndex + marker.Length;
            }

            return -1;
        }

        private static bool TryCreateSafeUrl(string url, out string safeUrl)
        {
            if (Uri.TryCreate(url, UriKind.Relative, out var relativeUri)
                && !relativeUri.IsAbsoluteUri
                && url.StartsWith("/", StringComparison.Ordinal)
                && !url.StartsWith("//", StringComparison.Ordinal))
            {
                safeUrl = url;
                return true;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp
                    || uri.Scheme == Uri.UriSchemeHttps
                    || uri.Scheme == Uri.UriSchemeMailto))
            {
                safeUrl = uri.ToString();
                return true;
            }

            safeUrl = string.Empty;
            return false;
        }

        private enum ListState
        {
            None,
            Unordered,
            Ordered
        }
    }
}
