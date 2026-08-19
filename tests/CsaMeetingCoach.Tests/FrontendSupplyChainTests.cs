using System.Text.Json;

namespace CsaMeetingCoach.Tests;

public sealed class FrontendSupplyChainTests
{
    [Fact]
    public void ToastifyDependencies_ArePinnedToMicrosoftCfs()
    {
        var npmConfiguration = File.ReadAllText(
            RepositoryPath("src", "CsaMeetingCoach.Web", ".npmrc"));
        var packageLock = File.ReadAllText(
            RepositoryPath("src", "CsaMeetingCoach.Web", "package-lock.json"));
        using var packageDocument = JsonDocument.Parse(File.ReadAllText(
            RepositoryPath("src", "CsaMeetingCoach.Web", "package.json")));

        Assert.Contains(
            "registry=https://packagefeedproxy.microsoft.io/npm/",
            npmConfiguration,
            StringComparison.Ordinal);
        Assert.Contains(
            "strict-allow-scripts=true",
            npmConfiguration,
            StringComparison.Ordinal);
        Assert.DoesNotContain("registry.npmjs.org", packageLock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registry.yarnpkg.com", packageLock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registry.npmmirror.com", packageLock, StringComparison.OrdinalIgnoreCase);
        using var lockDocument = JsonDocument.Parse(packageLock);
        foreach (var package in lockDocument.RootElement
                     .GetProperty("packages")
                     .EnumerateObject())
        {
            if (!package.Value.TryGetProperty("resolved", out var resolvedElement))
            {
                continue;
            }

            var resolved = new Uri(resolvedElement.GetString()!);
            Assert.Equal(Uri.UriSchemeHttps, resolved.Scheme);
            Assert.True(
                string.Equals(
                    resolved.Host,
                    "packagefeedproxy.microsoft.io",
                    StringComparison.OrdinalIgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(
                    resolved.Host,
                    @"^ms-feed-[0-9]+\.pkgs\.visualstudio\.com$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.CultureInvariant),
                $"Unapproved npm package host: {resolved.Host}");
        }
        var dependencies = packageDocument.RootElement.GetProperty("dependencies");
        Assert.Equal("19.2.8", dependencies.GetProperty("react").GetString());
        Assert.Equal("19.2.8", dependencies.GetProperty("react-dom").GetString());
        Assert.Equal("11.1.0", dependencies.GetProperty("react-toastify").GetString());
        Assert.True(packageDocument.RootElement
            .GetProperty("allowScripts")
            .GetProperty("esbuild@0.28.2")
            .GetBoolean());
    }

    [Fact]
    public void DeploymentWorkflow_BuildsToastifyOnlyThroughCfs()
    {
        var workflow = File.ReadAllText(
            RepositoryPath(".github", "workflows", "deploy-demo-vm.yml"));

        Assert.Contains(
            "actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "NPM_CONFIG_REGISTRY: https://packagefeedproxy.microsoft.io/npm/",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "npm ci --ignore-scripts --no-audit --no-fund",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "npm run build --ignore-scripts",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "npm run validate:supply-chain --ignore-scripts",
            workflow,
            StringComparison.Ordinal);

        var validator = File.ReadAllText(
            RepositoryPath(
                "src",
                "CsaMeetingCoach.Web",
                "scripts",
                "validate-package-lock.mjs"));
        Assert.Contains(
            "package-lock.json contains an unapproved resolved URL.",
            validator,
            StringComparison.Ordinal);
        Assert.Contains(
            "^ms-feed-[0-9]+\\.pkgs\\.visualstudio\\.com$",
            validator,
            StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "CsaMeetingCoach.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
