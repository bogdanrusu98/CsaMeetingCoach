using System.Text.RegularExpressions;

namespace CsaMeetingCoach.KnowledgeIndexer;

public sealed partial record IndexerConfiguration(
    Uri ProjectEndpoint,
    string SourceDirectory,
    string StoreName)
{
    private const string DefaultStoreName = "CSA Meeting Coach Knowledge";

    public static IndexerConfiguration Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (index + 1 >= arguments.Count
                || !name.StartsWith("--", StringComparison.Ordinal)
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Argument '{name}' requires a value.");
            }

            if (name is not "--project-endpoint"
                and not "--source-directory"
                and not "--store-name")
            {
                throw new ArgumentException($"Unknown argument '{name}'.");
            }

            if (!values.TryAdd(name, arguments[index + 1].Trim()))
            {
                throw new ArgumentException(
                    $"Argument '{name}' may be specified only once.");
            }
        }

        var endpointValue = Require(values, "--project-endpoint");
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(endpoint.Host)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !ProjectPathRegex().IsMatch(endpoint.AbsolutePath))
        {
            throw new ArgumentException(
                "--project-endpoint must be an HTTPS Foundry project URL with path /api/projects/{project-name}.");
        }

        var sourceDirectory = Path.GetFullPath(
            Require(values, "--source-directory"));
        if (!Directory.Exists(sourceDirectory))
        {
            throw new ArgumentException(
                "--source-directory must identify an existing directory.");
        }

        var storeName = values.GetValueOrDefault(
            "--store-name",
            DefaultStoreName);
        if (string.IsNullOrWhiteSpace(storeName)
            || storeName.Length > 64
            || storeName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "--store-name must contain 1 to 64 non-control characters.");
        }

        return new IndexerConfiguration(endpoint, sourceDirectory, storeName);
    }

    private static string Require(
        IReadOnlyDictionary<string, string> values,
        string name) =>
        values.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException(
                    $"Required argument '{name}' was not supplied.");

    [GeneratedRegex(
        @"^/api/projects/[^/]+/?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPathRegex();
}

public static class KnowledgeSourceValidator
{
    public const int MaximumFileCount = 50;
    public const int MaximumFileMegabytes = 150;
    public const int MaximumTotalMegabytes = 300;
    public const long MaximumFileBytes = MaximumFileMegabytes * 1024L * 1024L;
    public const long MaximumTotalBytes = MaximumTotalMegabytes * 1024L * 1024L;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".docx",
            ".html",
            ".json",
            ".md",
            ".pdf",
            ".pptx",
            ".txt",
            ".xlsx"
        };

    public static IReadOnlyList<string> GetValidatedFiles(
        string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                "The knowledge source directory does not exist.");
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var files = Directory.EnumerateFiles(root, "*", options)
            .Where(path => AllowedExtensions.Contains(
                Path.GetExtension(path)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                "The source directory contains no supported knowledge files.");
        }

        if (files.Length > MaximumFileCount)
        {
            throw new InvalidOperationException(
                $"The source directory exceeds the {MaximumFileCount}-file limit.");
        }

        long totalBytes = 0;
        foreach (var path in files)
        {
            var length = new FileInfo(path).Length;
            if (length > MaximumFileBytes)
            {
                throw new InvalidOperationException(
                    $"Knowledge file '{Path.GetFileName(path)}' exceeds the {MaximumFileMegabytes} MB limit.");
            }

            totalBytes = checked(totalBytes + length);
            if (totalBytes > MaximumTotalBytes)
            {
                throw new InvalidOperationException(
                    $"Knowledge files exceed the {MaximumTotalMegabytes} MB aggregate limit.");
            }
        }

        return Array.AsReadOnly(files);
    }
}
