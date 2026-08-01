using CsaMeetingCoach.KnowledgeIndexer;

namespace CsaMeetingCoach.Tests;

public sealed class KnowledgeIndexerTests
{
    [Fact]
    public void Parse_ValidArguments_ReturnsNormalizedConfiguration()
    {
        var directory = CreateTestDirectory();
        try
        {
            var configuration = IndexerConfiguration.Parse(
            [
                "--project-endpoint",
                "https://example.services.ai.azure.com/api/projects/example",
                "--source-directory",
                directory,
                "--store-name",
                "Reviewed knowledge"
            ]);

            Assert.Equal("Reviewed knowledge", configuration.StoreName);
            Assert.Equal(
                Path.GetFullPath(directory),
                configuration.SourceDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("http://example.com/api/projects/test")]
    [InlineData("https://example.com/not-a-project")]
    [InlineData("https://user@example.com/api/projects/test")]
    [InlineData("https://example.com/api/projects/test?secret=value")]
    public void Parse_InvalidProjectEndpoint_RejectsArguments(string endpoint)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            IndexerConfiguration.Parse(
            [
                "--project-endpoint",
                endpoint,
                "--source-directory",
                "."
            ]));

        Assert.Contains("--project-endpoint", exception.Message);
    }

    [Fact]
    public void GetValidatedFiles_SelectsOnlyAllowedTypesRecursively()
    {
        var directory = CreateTestDirectory();
        try
        {
            var child = Directory.CreateDirectory(
                Path.Combine(directory, "child")).FullName;
            File.WriteAllText(Path.Combine(directory, "source.md"), "approved");
            File.WriteAllText(Path.Combine(child, "notes.txt"), "approved");
            File.WriteAllText(Path.Combine(child, "catalog.xlsx"), "approved");
            File.WriteAllText(Path.Combine(child, "script.exe"), "ignored");

            var files = KnowledgeSourceValidator.GetValidatedFiles(directory);

            Assert.Equal(3, files.Count);
            Assert.Contains(
                files,
                path => path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                files,
                path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetValidatedFiles_NoSupportedFiles_RejectsDirectory()
    {
        var directory = CreateTestDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "source.exe"), "ignored");

            Assert.Throws<InvalidOperationException>(() =>
                KnowledgeSourceValidator.GetValidatedFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetValidatedFiles_TooManyFiles_RejectsDirectory()
    {
        var directory = CreateTestDirectory();
        try
        {
            for (var index = 0;
                 index <= KnowledgeSourceValidator.MaximumFileCount;
                 index++)
            {
                File.WriteAllText(
                    Path.Combine(directory, $"source-{index}.md"),
                    "approved");
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                KnowledgeSourceValidator.GetValidatedFiles(directory));
            Assert.Contains("file limit", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetValidatedFiles_OversizedFile_RejectsDirectory()
    {
        var directory = CreateTestDirectory();
        try
        {
            using (var stream = File.Create(
                Path.Combine(directory, "oversized.pdf")))
            {
                stream.SetLength(
                    KnowledgeSourceValidator.MaximumFileBytes + 1);
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                KnowledgeSourceValidator.GetValidatedFiles(directory));
            Assert.Contains("50 MB", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            $"knowledge-indexer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
