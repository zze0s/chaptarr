using System;
using System.Globalization;
using System.Web;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.MyAnonaMouse
{
    // "MAM-<torrent id>" is the contract between the indexer, which mints the identity when it
    // parses a release, and MamUnsatisfiedSlotGuard, which reserves unsatisfied slots against it.
    // Both directions live here so the two ends cannot drift apart.
    public static class MyAnonaMouseReleaseIdentity
    {
        public const string Prefix = "MAM-";

        public const string DownloadPath = "/tor/download.php";

        public static string Build(string torrentId)
        {
            return Prefix + torrentId;
        }

        public static bool TryParse(string guid, out string torrentId)
        {
            torrentId = null;

            if (string.IsNullOrWhiteSpace(guid) || !guid.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var candidate = guid.Substring(Prefix.Length);

            if (!long.TryParse(candidate, out var numericId) || numericId <= 0)
            {
                return false;
            }

            torrentId = candidate;
            return true;
        }

        // Releases pushed in through the API carry no MAM identity, so recover one from the
        // download url. Only the indexer's own download endpoint is trusted, so a url that merely
        // shares the hostname cannot mint an identity that would reserve a slot on this account.
        public static bool TryDeriveFromDownloadUrl(string downloadUrl, MyAnonaMouseSettings settings, out string guid)
        {
            guid = null;

            if (!IsDownloadEndpoint(downloadUrl, settings, out var uri))
            {
                return false;
            }

            var torrentId = HttpUtility.ParseQueryString(uri.Query)["tid"];

            if (!long.TryParse(torrentId, out var numericId) || numericId <= 0)
            {
                return false;
            }

            guid = Build(numericId.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        public static bool TryDeriveFromIndexer(IndexerDefinition definition, ReleaseInfo release, out string guid)
        {
            guid = null;

            return definition?.Settings is MyAnonaMouseSettings settings &&
                   TryDeriveFromDownloadUrl(release?.DownloadUrl, settings, out guid);
        }

        // Scheme, host, port and path must all match the download endpoint the indexer builds for
        // itself, so neither another service on the same host nor a different path can be trusted.
        public static bool IsDownloadEndpoint(string downloadUrl, MyAnonaMouseSettings settings, out Uri uri)
        {
            uri = null;

            if (settings == null ||
                !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var candidate) ||
                !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                return false;
            }

            if (!string.Equals(candidate.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candidate.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                candidate.Port != baseUri.Port ||
                !string.Equals(candidate.AbsolutePath, baseUri.AbsolutePath.TrimEnd('/') + DownloadPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            uri = candidate;
            return true;
        }
    }
}
