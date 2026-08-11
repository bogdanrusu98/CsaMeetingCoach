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
        Assert.Contains(
            "Get-ResourcePropertyValue",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Custom Speech project and endpoint state validated.",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "if ($endpointSelectedById)",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$normalizedExpectedEndpointId = $null",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "$expectedEndpointId = $null",
            script,
            StringComparison.Ordinal);
        Assert.Contains("byte order mark", script, StringComparison.Ordinal);
        Assert.DoesNotContain("contentUrl", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("az storage", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "[switch] $CleanupOnly",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Cleanup mode requires a canonical expected endpoint ID.",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "[Regex]::Escape(\"$ProjectDisplayName $ResourceKind \")",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"\\A\" +",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"[0-9]{8}-[0-9]{6}\\z\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "[AllowEmptyCollection()]",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Get-CustomEndpointModelId",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"/speechtotext/models/base/$modelId\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "The active custom model did not expose its dataset references.",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            ") -f\n        $cleanupResult.DeletedModels,",
            script.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(
            "Complete every read and protection check before issuing the first DELETE.",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$protectedModelIds",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$protectedDatasetIds",
            script,
            StringComparison.Ordinal);
        var deleteModelIndex = script.IndexOf(
            "-Operation \"delete obsolete managed model\"",
            StringComparison.Ordinal);
        var deleteDatasetIndex = script.IndexOf(
            "-Operation \"delete obsolete managed dataset\"",
            StringComparison.Ordinal);
        Assert.True(deleteModelIndex >= 0);
        Assert.True(deleteDatasetIndex > deleteModelIndex);
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
        Assert.Contains("default: train-and-deploy", workflow, StringComparison.Ordinal);
        Assert.Contains("- cleanup-obsolete", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "-CleanupOnly",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Cleanup requires BROWSER_SPEECH_ENDPOINT_ID or a valid deployment result.",
            workflow,
            StringComparison.Ordinal);
        var applicationDeployIndex = workflow.IndexOf(
            "gh workflow run deploy-demo-vm.yml",
            StringComparison.Ordinal);
        var cleanupIndex = workflow.IndexOf(
            "- name: Remove obsolete managed models and datasets",
            StringComparison.Ordinal);
        Assert.True(applicationDeployIndex >= 0);
        Assert.True(cleanupIndex > applicationDeployIndex);
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
