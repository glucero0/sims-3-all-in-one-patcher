using System;
using System.Text.Json;

namespace Sims3ModernPatcher
{
    public sealed class GitHubReleaseAsset
    {
        public GitHubReleaseAsset(
            string name,
            string? browserDownloadUrl,
            string? apiDownloadUrl,
            string? sha256)
        {
            Name = name;
            BrowserDownloadUrl = browserDownloadUrl;
            ApiDownloadUrl = apiDownloadUrl;
            Sha256 = sha256;
        }

        public string Name { get; }
        public string? BrowserDownloadUrl { get; }
        public string? ApiDownloadUrl { get; }
        public string? Sha256 { get; }
    }

    /// <summary>
    /// Reads GitHub release JSON so downloads can follow a named asset
    /// (for example Win32-latest/wininet-Win32.zip) instead of a disposable asset id.
    /// </summary>
    public static class GitHubReleaseAssets
    {
        public static GitHubReleaseAsset? FindByName(string releaseJson, string assetName)
        {
            if (string.IsNullOrWhiteSpace(releaseJson) || string.IsNullOrWhiteSpace(assetName))
                return null;

            using JsonDocument document = JsonDocument.Parse(releaseJson);
            if (!document.RootElement.TryGetProperty("assets", out JsonElement assets)
                || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string? name = GetString(asset, "name");
                if (name is null || !name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return new GitHubReleaseAsset(
                    name,
                    GetString(asset, "browser_download_url"),
                    GetString(asset, "url"),
                    ParseSha256Digest(GetString(asset, "digest")));
            }

            return null;
        }

        public static string? ParseSha256Digest(string? digest)
        {
            if (string.IsNullOrWhiteSpace(digest))
                return null;

            const string prefix = "sha256:";
            if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            string hash = digest.Substring(prefix.Length).Trim();
            return hash.Length == 64 ? hash.ToLowerInvariant() : null;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
