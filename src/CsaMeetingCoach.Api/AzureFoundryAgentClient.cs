using System.ClientModel;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using CsaMeetingCoach.Core;
using OpenAI.Responses;

namespace CsaMeetingCoach.Api;

public sealed record FoundryOptions(
    Uri ProjectEndpoint,
    string ModelDeployment,
    string AgentName);

public sealed class AzureFoundryAgentClient : IFoundryAgentClient
{
    private readonly AIProjectClient projectClient;
    private readonly FoundryOptions options;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private bool agentReady;

    public AzureFoundryAgentClient(
        FoundryOptions options,
        TokenCredential credential)
    {
        this.options = options;
        projectClient = new AIProjectClient(options.ProjectEndpoint, credential);
    }

    public async Task<string> GetDecisionJsonAsync(
        string inputJson,
        CancellationToken cancellationToken)
    {
        await EnsureAgentAsync(cancellationToken);

        ProjectResponsesClient responsesClient = projectClient
            .ProjectOpenAIClient
            .GetProjectResponsesClientForAgent(options.AgentName);

#pragma warning disable OPENAI001
        var responseOptions = new CreateResponseOptions
        {
            InputItems =
            {
                ResponseItem.CreateUserMessageItem(inputJson)
            },
            MaxOutputTokenCount = 2_000,
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "coach_decision",
                    BinaryData.FromString(FoundryAgentContract.ResponseJsonSchema),
                    "Evidence-backed CSA meeting coaching decision.",
                    true)
            }
        };

        ResponseResult response = await responsesClient.CreateResponseAsync(
            responseOptions,
            cancellationToken);
        var outputText = response.GetOutputText();
#pragma warning restore OPENAI001
        return outputText;
    }

    private async Task EnsureAgentAsync(CancellationToken cancellationToken)
    {
        if (agentReady)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (agentReady)
            {
                return;
            }

            try
            {
                _ = await projectClient.AgentAdministrationClient.GetAgentAsync(
                    options.AgentName,
                    cancellationToken);
            }
            catch (ClientResultException exception) when (exception.Status == 404)
            {
                await CreateAgentAsync(cancellationToken);
            }

            agentReady = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    private async Task CreateAgentAsync(CancellationToken cancellationToken)
    {
        var definition = new DeclarativeAgentDefinition(options.ModelDeployment)
        {
            Instructions = FoundryAgentContract.Instructions
        };

        try
        {
            _ = await projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
                options.AgentName,
                new ProjectsAgentVersionCreationOptions(definition),
                cancellationToken: cancellationToken);
        }
        catch (ClientResultException exception) when (exception.Status == 409)
        {
            _ = await projectClient.AgentAdministrationClient.GetAgentAsync(
                options.AgentName,
                cancellationToken);
        }
    }
}
