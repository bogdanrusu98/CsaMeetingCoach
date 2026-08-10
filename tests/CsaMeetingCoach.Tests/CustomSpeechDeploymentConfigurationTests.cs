namespace CsaMeetingCoach.Tests;

public sealed class CustomSpeechDeploymentConfigurationTests
{
    [Fact]
    public void DeploymentScriptUsesCurrentPrivateDataBlockApiAndDisablesLogging()
    {
        var script = File.ReadAllText(RepositoryPath("deploy", "Deploy-CustomSpeech.ps1"));

        Assert.Contains("$apiVersion = \"2025-10-15\"", script, StringComparison.Ordinal);
        Assert.Contains("/blocks?blockid=", script, StringComparison.Ordinal);
        Assert.Contains("/blocks`:commit", script, StringComparison.Ordinal);
        Assert.Contains("application/octet-stream", script, StringComparison.Ordinal);
        Assert.Contains("loggingEnabled = $false", script, StringComparison.Ordinal);
        Assert.Contains("contentLoggingEnabled = $false", script, StringComparison.Ordinal);
        Assert.Contains(
            "ConvertTo-Json -InputObject $Value",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "$Value | ConvertTo-Json",
            script,
            StringComparison.Ordinal);
        Assert.Contains("ExpectedEndpointId", script, StringComparison.Ordinal);
        Assert.Contains("belongs to a different project", script, StringComparison.Ordinal);
        Assert.Contains("byte order mark", script, StringComparison.Ordinal);
        Assert.DoesNotContain("contentUrl", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("az storage", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomSpeechWorkflowIsManualAndWiresEndpointIntoDemoDeployment()
    {
        var workflow = File.ReadAllText(
            RepositoryPath(".github", "workflows", "deploy-custom-speech.yml"));
        var demoWorkflow = File.ReadAllText(
            RepositoryPath(".github", "workflows", "deploy-demo-vm.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  push:", workflow, StringComparison.Ordinal);
        Assert.Contains("MEDIA_BOT_SPEECH_KEY", workflow, StringComparison.Ordinal);
        Assert.Contains("BROWSER_SPEECH_ENDPOINT_ID", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "-ExpectedEndpointId $env:CONFIGURED_ENDPOINT_ID",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh variable set", workflow, StringComparison.Ordinal);
        Assert.Contains("gh workflow run deploy-demo-vm.yml", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "BrowserSpeechEndpointId = $env:BROWSER_SPEECH_ENDPOINT_ID",
            demoWorkflow,
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
