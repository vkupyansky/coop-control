using System;
using System.Text.RegularExpressions;

namespace OGCoop.Shared;

internal static class ProductInfo
{
    internal const string Name = "OG Coop";
    internal const string Version = "0.4.3";
    internal const string Tag = "v" + Version + "-wip";
    internal const string Author = "Vladislav Kupyansky (vkupyansky)";
    internal const string Telegram = "@skibidessa";
    internal const string Repository = "vkupyansky/og-coop";
    internal const string Releases = "https://github.com/" + Repository + "/releases";
    internal const string NewIssue = "https://github.com/" + Repository + "/issues/new/choose";
    internal const string ReleasesApi = "https://api.github.com/repos/" + Repository + "/releases?per_page=10";
    // OG_COOP_UPDATE_CHECK=0 turns the GitHub release check off.
    internal const string UpdateCheckVariable = "OG_COOP_UPDATE_CHECK";
}

// Reads GitHub's release list without a JSON library (the installer runs on
// .NET Framework): the newest published version, pre-releases included.
internal static class ReleaseCheck
{
    internal const int MaximumResponse = 512 * 1024;
    private static readonly Regex TagName = new Regex("\"tag_name\"\\s*:\\s*\"([^\"]{1,64})\"", RegexOptions.CultureInvariant);
    private static readonly Regex Draft = new Regex("\"draft\"\\s*:\\s*(true|false)", RegexOptions.CultureInvariant);
    private static readonly Regex VersionTag = new Regex("^v?(\\d{1,4})\\.(\\d{1,4})\\.(\\d{1,4})(?:-[A-Za-z0-9.]{1,32})?$", RegexOptions.CultureInvariant);

    internal static Version Current => Parse(ProductInfo.Tag) ?? throw new InvalidOperationException("ProductInfo.Tag is not a version.");

    internal static Version? Parse(string tag)
    {
        Match match = VersionTag.Match(tag);
        return match.Success ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)) : null;
    }

    internal static Version? Newest(string json)
    {
        if (json.Length > MaximumResponse) return null;
        MatchCollection tags = TagName.Matches(json);
        Version? newest = null;
        for (int index = 0; index < tags.Count; index++)
        {
            int start = tags[index].Index;
            int end = index + 1 < tags.Count ? tags[index + 1].Index : json.Length;
            // In GitHub's release object "draft" follows "tag_name"; a draft is not published.
            Match draft = Draft.Match(json, start, end - start);
            if (!draft.Success || draft.Groups[1].Value == "true") continue;
            Version? version = Parse(tags[index].Groups[1].Value);
            if (version != null && (newest == null || version > newest)) newest = version;
        }

        return newest;
    }

    internal static bool IsNewer(Version? latest) => latest != null && latest > Current;
}
