using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MyAnonaMouse;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Parser.Model;

namespace Chaptarr.Core.Test.Indexers
{
    [TestFixture]
    public class MyAnonaMouseReleaseIdentityFixture
    {
        private const string DownloadUrl = "https://www.myanonamouse.net/tor/download.php?tid=1263160";

        private static MyAnonaMouseSettings Settings(string baseUrl = "https://www.myanonamouse.net")
        {
            return new MyAnonaMouseSettings { BaseUrl = baseUrl };
        }

        private static string Derive(string downloadUrl, MyAnonaMouseSettings settings = null)
        {
            MyAnonaMouseReleaseIdentity.TryDeriveFromDownloadUrl(downloadUrl, settings ?? Settings(), out var guid);
            return guid;
        }

        [Test]
        public void should_derive_identity_from_download_url()
        {
            Assert.That(Derive(DownloadUrl), Is.EqualTo("MAM-1263160"));
        }

        [Test]
        public void should_ignore_extra_query_parameters()
        {
            Assert.That(Derive(DownloadUrl + "&fl&canUseToken=true"), Is.EqualTo("MAM-1263160"));
        }

        [Test]
        public void should_normalize_torrent_id()
        {
            Assert.That(Derive("https://www.myanonamouse.net/tor/download.php?tid=0001263160"), Is.EqualTo("MAM-1263160"));
        }

        [Test]
        public void should_accept_a_custom_base_url_including_a_path_prefix()
        {
            var derived = Derive("https://books.example.test:8443/mam/tor/download.php?tid=1263160",
                Settings("https://books.example.test:8443/mam/"));

            Assert.That(derived, Is.EqualTo("MAM-1263160"));
        }

        // Sharing the hostname is not enough: another service on the same host, a different port
        // or a different path must never be able to mint an identity for this account.
        [TestCase("http://www.myanonamouse.net/tor/download.php?tid=1263160", TestName = "scheme mismatch")]
        [TestCase("https://www.myanonamouse.net:8080/tor/download.php?tid=1263160", TestName = "port mismatch")]
        [TestCase("https://www.myanonamouse.net/anything?tid=1263160", TestName = "path mismatch")]
        [TestCase("https://www.myanonamouse.net/tor/download.php/extra?tid=1263160", TestName = "path suffix")]
        [TestCase("https://not-myanonamouse.test/tor/download.php?tid=1263160", TestName = "host mismatch")]
        public void should_not_derive_identity_outside_the_indexer_download_endpoint(string downloadUrl)
        {
            Assert.That(MyAnonaMouseReleaseIdentity.TryDeriveFromDownloadUrl(downloadUrl, Settings(), out var guid), Is.False);
            Assert.That(guid, Is.Null);
        }

        [TestCase("https://www.myanonamouse.net/tor/download.php")]
        [TestCase("https://www.myanonamouse.net/tor/download.php?tid=")]
        [TestCase("https://www.myanonamouse.net/tor/download.php?tid=abc")]
        [TestCase("https://www.myanonamouse.net/tor/download.php?tid=0")]
        [TestCase("https://www.myanonamouse.net/tor/download.php?tid=-5")]
        [TestCase("not a url")]
        [TestCase(null)]
        public void should_not_derive_identity_without_a_usable_torrent_id(string downloadUrl)
        {
            Assert.That(MyAnonaMouseReleaseIdentity.TryDeriveFromDownloadUrl(downloadUrl, Settings(), out _), Is.False);
        }

        [Test]
        public void should_not_derive_identity_without_settings()
        {
            Assert.That(MyAnonaMouseReleaseIdentity.TryDeriveFromDownloadUrl(DownloadUrl, null, out _), Is.False);
        }

        [Test]
        public void should_derive_identity_from_a_myanonamouse_indexer()
        {
            var definition = new IndexerDefinition { Id = 1, Settings = Settings() };
            var release = new ReleaseInfo { DownloadUrl = DownloadUrl, Guid = "PUSH-" + DownloadUrl };

            Assert.That(MyAnonaMouseReleaseIdentity.TryDeriveFromIndexer(definition, release, out var guid), Is.True);
            Assert.That(guid, Is.EqualTo("MAM-1263160"));
        }

        [Test]
        public void should_not_derive_identity_from_another_indexer_type()
        {
            var definition = new IndexerDefinition { Id = 1, Settings = new TorznabSettings { BaseUrl = "https://www.myanonamouse.net" } };
            var release = new ReleaseInfo { DownloadUrl = DownloadUrl };

            Assert.That(MyAnonaMouseReleaseIdentity.TryDeriveFromIndexer(definition, release, out _), Is.False);
        }

        [Test]
        public void should_not_derive_identity_without_an_indexer()
        {
            Assert.That(MyAnonaMouseReleaseIdentity.TryDeriveFromIndexer(null, new ReleaseInfo { DownloadUrl = DownloadUrl }, out _), Is.False);
        }

        [Test]
        public void should_parse_the_identity_it_builds()
        {
            var parsed = MyAnonaMouseReleaseIdentity.TryParse(MyAnonaMouseReleaseIdentity.Build("1263160"), out var torrentId);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.True);
                Assert.That(torrentId, Is.EqualTo("1263160"));
            });
        }

        [TestCase("PUSH-https://www.myanonamouse.net/tor/download.php?tid=1263160")]
        [TestCase("MAM-")]
        [TestCase("MAM-abc")]
        [TestCase("MAM-0")]
        [TestCase("")]
        [TestCase(null)]
        public void should_not_parse_a_foreign_guid(string guid)
        {
            Assert.That(MyAnonaMouseReleaseIdentity.TryParse(guid, out _), Is.False);
        }

        // The regression itself: the guard spends slots against the identity the indexer mints, so
        // a derived identity has to satisfy it while the pushed PUSH- guid does not.
        [Test]
        public void derived_identity_should_satisfy_the_unsatisfied_slot_guard()
        {
            var definition = new IndexerDefinition { Id = 1, Settings = Settings() };
            var release = new ReleaseInfo { DownloadUrl = DownloadUrl, Guid = "PUSH-" + DownloadUrl };

            Assert.That(MamUnsatisfiedSlotGuard.TryGetTorrentId(release, out _), Is.False, "pushed guid should not carry a MAM identity");

            MyAnonaMouseReleaseIdentity.TryDeriveFromIndexer(definition, release, out var guid);
            release.Guid = guid;

            Assert.That(MamUnsatisfiedSlotGuard.TryGetTorrentId(release, out var torrentId), Is.True);
            Assert.That(torrentId, Is.EqualTo("1263160"));
        }
    }
}
