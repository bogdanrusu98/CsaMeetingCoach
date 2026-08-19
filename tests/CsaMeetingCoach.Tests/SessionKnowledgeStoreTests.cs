using System.Net;
using System.IO.Compression;
using System.Text;
using CsaMeetingCoach.Api;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Tests;

public sealed class SessionKnowledgeStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CsaMeetingCoach.Knowledge",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StoreTextFile_ScansExtractsAndDeletesSessionContent()
    {
        var scanner = new RecordingScanner(KnowledgeMalwareScanResult.Clean);
        var store = new LocalSessionKnowledgeStore(_directory, scanner);
        var sessionId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        await using var content = new MemoryStream(
            Encoding.UTF8.GetBytes("Recovery Time Objective defines restoration time."));

        var stored = await store.StoreFileAsync(
            sessionId,
            sourceId,
            "recovery.txt",
            KnowledgeSourceVisibility.MemberEligible,
            "recovery.txt",
            "text/plain",
            content,
            CancellationToken.None);
        Assert.Empty(await store.ReadAsync(
            sessionId,
            [Guid.NewGuid()],
            CancellationToken.None));
        var snippet = Assert.Single(await store.ReadAsync(
            sessionId,
            [sourceId],
            CancellationToken.None));

        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.Equal(sourceId, snippet.SourceId);
        Assert.Contains("restoration time", snippet.Content, StringComparison.Ordinal);
        Assert.Single(scanner.ScannedPaths);

        await store.DeleteSessionArtifactsAsync(sessionId, CancellationToken.None);
        Assert.Empty(await store.ReadAsync(sessionId, [sourceId], CancellationToken.None));
    }

    [Fact]
    public async Task StoreHtml_RemovesExecutableContent()
    {
        var store = new LocalSessionKnowledgeStore(
            _directory,
            new RecordingScanner(KnowledgeMalwareScanResult.Clean));
        var sessionId = Guid.NewGuid();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(
            "<html><script>steal()</script><body><h1>Trusted heading</h1></body></html>"));

        var sourceId = Guid.NewGuid();
        await store.StoreFileAsync(
            sessionId,
            sourceId,
            "guide.html",
            KnowledgeSourceVisibility.HostPrivate,
            "guide.html",
            "text/html",
            content,
            CancellationToken.None);
        var snippet = Assert.Single(await store.ReadAsync(
            sessionId,
            [sourceId],
            CancellationToken.None));

        Assert.Equal("Trusted heading", snippet.Content);
        Assert.DoesNotContain("steal", snippet.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThreatDetected_RejectsAndLeavesNoReadableKnowledge()
    {
        var store = new LocalSessionKnowledgeStore(
            _directory,
            new RecordingScanner(KnowledgeMalwareScanResult.ThreatDetected));
        var sessionId = Guid.NewGuid();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("unsafe"));

        await Assert.ThrowsAsync<KnowledgeRejectedException>(() =>
            store.StoreFileAsync(
                sessionId,
                Guid.NewGuid(),
                "unsafe.txt",
                KnowledgeSourceVisibility.HostPrivate,
                "unsafe.txt",
                "text/plain",
                content,
                CancellationToken.None));

        Assert.Empty(await store.ReadAsync(
            sessionId,
            [Guid.NewGuid()],
            CancellationToken.None));
    }

    [Fact]
    public async Task OpenXmlExpansion_UsesActualDecompressedByteLimit()
    {
        var store = new LocalSessionKnowledgeStore(
            _directory,
            new RecordingScanner(KnowledgeMalwareScanResult.Clean));
        await using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(
                "word/document.xml",
                CompressionLevel.SmallestSize);
            await using var entryStream = entry.Open();
            var prefix = Encoding.UTF8.GetBytes("<document>");
            await entryStream.WriteAsync(prefix);
            var block = Enumerable.Repeat((byte)' ', 32_768).ToArray();
            for (var index = 0; index < 641; index++)
            {
                await entryStream.WriteAsync(block);
            }
            var suffix = Encoding.UTF8.GetBytes("</document>");
            await entryStream.WriteAsync(suffix);
        }
        content.Position = 0;

        await Assert.ThrowsAsync<KnowledgeRejectedException>(() =>
            store.StoreFileAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "oversized.docx",
                KnowledgeSourceVisibility.HostPrivate,
                "oversized.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                content,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.7")]
    [InlineData("169.254.169.254")]
    [InlineData("192.168.1.20")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    public void LinkFetcher_BlocksPrivateAndMetadataAddresses(string address)
    {
        Assert.False(SafeKnowledgeLinkFetcher.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void LinkFetcher_RequiresStandardPublicHttpsUri()
    {
        Assert.Throws<ArgumentException>(() =>
            SafeKnowledgeLinkFetcher.ValidateUri(new Uri("http://example.com")));
        Assert.Throws<ArgumentException>(() =>
            SafeKnowledgeLinkFetcher.ValidateUri(new Uri("https://user:pass@example.com")));
        Assert.Throws<ArgumentException>(() =>
            SafeKnowledgeLinkFetcher.ValidateUri(new Uri("https://example.com:8443")));
        Assert.Equal(
            "https://example.com/path",
            SafeKnowledgeLinkFetcher.ValidateUri(
                new Uri("https://example.com/path")).ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingScanner(KnowledgeMalwareScanResult result)
        : IKnowledgeMalwareScanner
    {
        public List<string> ScannedPaths { get; } = [];

        public Task<KnowledgeMalwareScanResult> ScanAsync(
            string path,
            CancellationToken cancellationToken)
        {
            ScannedPaths.Add(path);
            return Task.FromResult(result);
        }
    }
}
