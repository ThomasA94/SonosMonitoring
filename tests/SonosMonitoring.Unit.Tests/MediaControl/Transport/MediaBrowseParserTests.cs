using SonosMonitoring.Infrastructure.MediaControl.Transport;

namespace SonosMonitoring.Unit.Tests.MediaControl.Transport;

/// <summary>Checks bounded DIDL parsing before entries enter the private reference cache.</summary>
public sealed class MediaBrowseParserTests
{
    /// <summary>Preserves a playable URI and metadata while extracting bounded display data.</summary>
    [Fact]
    public void ParsesFavoriteWithoutDiscardingMetadata()
    {
        const string DIDL = "<DIDL-Lite xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><item id=\"FV:2/1\"><dc:title>Radio</dc:title><res>https://example.test/stream?token=private</res></item></DIDL-Lite>";
        MediaBrowseEntry entry = Assert.Single(MediaBrowseParser.Parse(DIDL, 1, 10));
        Assert.Equal("FV:2/1", entry.ObjectId);
        Assert.Equal("Radio", entry.Title);
        Assert.Contains("token=private", entry.Uri, StringComparison.Ordinal);
        Assert.Contains("Radio", entry.Metadata, StringComparison.Ordinal);
    }

    /// <summary>Rejects mismatched counts and XML entities instead of silently losing queue entries.</summary>
    [Fact]
    public void RejectsCountMismatchAndDtd()
    {
        const string EMPTY = "<DIDL-Lite />";
        Assert.Throws<FormatException>(() => MediaBrowseParser.Parse(EMPTY, 1, 10));
        Assert.Throws<FormatException>(() => MediaBrowseParser.Parse("<!DOCTYPE x><DIDL-Lite />", 0, 10));
    }

    /// <summary>Uses the favorite's resource metadata instead of its outer bookmark wrapper.</summary>
    [Fact]
    public void FavoritePreservesResourceMetadata()
    {
        const string DIDL = "<DIDL-Lite xmlns:r=\"urn:schemas-rinconnetworks-com:metadata-1-0/\"><item id=\"FV:2/1\"><title>Station</title><res>x-sonosapi-stream:test</res><r:resMD>&lt;DIDL-Lite&gt;&lt;item id=&quot;stream&quot;&gt;&lt;title&gt;Actual stream&lt;/title&gt;&lt;/item&gt;&lt;/DIDL-Lite&gt;</r:resMD></item></DIDL-Lite>";
        MediaBrowseEntry entry = Assert.Single(MediaBrowseParser.Parse(DIDL, 1, 10));
        Assert.Contains("Actual stream", entry.Metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("FV:2/1", entry.Metadata, StringComparison.Ordinal);
    }
}
