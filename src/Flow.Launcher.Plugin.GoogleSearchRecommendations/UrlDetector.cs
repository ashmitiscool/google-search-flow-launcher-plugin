using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.GoogleSearchRecommendations;

public static partial class UrlDetector
{
    private static readonly Regex DomainRegex = new(
        @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,24}(?::\d{1,5})?(?:/.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LocalhostRegex = new(
        @"^localhost(?::\d{1,5})(?:/.*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryGetUrl(string? input, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string trimmed = input.Trim();

        // Any whitespace means it's a multi-word search query, not a single URL
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? explicitUri) &&
                !string.IsNullOrEmpty(explicitUri.Host) &&
                (explicitUri.Host.Contains('.') || explicitUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
            {
                normalizedUrl = explicitUri.AbsoluteUri;
                return true;
            }

            return false;
        }

        if (LocalhostRegex.IsMatch(trimmed))
        {
            if (Uri.TryCreate("http://" + trimmed, UriKind.Absolute, out Uri? localUri))
            {
                normalizedUrl = localUri.AbsoluteUri;
                return true;
            }
        }

        if (DomainRegex.IsMatch(trimmed))
        {
            if (Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out Uri? domainUri))
            {
                normalizedUrl = domainUri.AbsoluteUri;
                return true;
            }
        }

        return false;
    }
}
