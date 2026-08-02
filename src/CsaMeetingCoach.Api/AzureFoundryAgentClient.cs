using System.ClientModel;
using System.ClientModel.Primitives;
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
    string AgentName,
    IReadOnlyList<string> VectorStoreIds);

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

    public Task WarmUpAsync(CancellationToken cancellationToken)
        => EnsureAgentAsync(cancellationToken);

    public async Task<string> GetDecisionJsonAsync(
        string inputJson,
        CancellationToken cancellationToken)
    {
        await EnsureAgentAsync(cancellationToken);

        ProjectResponsesClient responsesClient = projectClient
            .ProjectOpenAIClient
            .GetProjectResponsesClientForAgent(options.AgentName);

#pragma warning disable OPENAI001
        ResponseResult response = await responsesClient.CreateResponseAsync(
            BuildResponseOptions(inputJson),
            cancellationToken);
        var outputText = response.GetOutputText();
#pragma warning restore OPENAI001
        return outputText;
    }

#pragma warning disable OPENAI001
    internal static CreateResponseOptions BuildResponseOptions(string inputJson)
    {
        return new CreateResponseOptions
        {
            InputItems =
            {
                ResponseItem.CreateUserMessageItem(inputJson)
            },
            MaxOutputTokenCount = 2_000
        };
    }
#pragma warning restore OPENAI001

    internal static DeclarativeAgentDefinition BuildAgentDefinition(
        string modelDeployment,
        IReadOnlyList<string>? vectorStoreIds = null)
    {
#pragma warning disable OPENAI001
        var definition = new DeclarativeAgentDefinition(modelDeployment)
        {
            Instructions = FoundryAgentContract.Instructions,
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "coach_decision",
                    BinaryData.FromString(FoundryAgentContract.ResponseJsonSchema),
                    "Evidence-backed CSA meeting coaching decision.",
                    true)
            }
        };
        if (vectorStoreIds is { Count: > 0 })
        {
            definition.Tools.Add(ResponseTool.CreateFileSearchTool(
                vectorStoreIds: vectorStoreIds));
        }

        return definition;
#pragma warning restore OPENAI001
    }

    internal static BinaryData BuildAgentUpdateBody(
        string modelDeployment,
        IReadOnlyList<string> vectorStoreIds)
    {
        var options = new ProjectsAgentVersionCreationOptions(
            BuildAgentDefinition(modelDeployment, vectorStoreIds));
        return ModelReaderWriter.Write(
            options,
            new ModelReaderWriterOptions("W"));
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

            await UpdateOrCreateAgentAsync(cancellationToken);

            agentReady = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    private async Task UpdateOrCreateAgentAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await UpdateExistingAgentAsync(cancellationToken);
        }
        catch (ClientResultException exception) when (exception.Status == 404)
        {
            await CreateAgentAsync(cancellationToken);
        }
    }

    private async Task UpdateExistingAgentAsync(
        CancellationToken cancellationToken)
    {
        var updateBody = BuildAgentUpdateBody(
            options.ModelDeployment,
            options.VectorStoreIds);
        using var content = BinaryContent.Create(updateBody);
        var requestOptions = new RequestOptions
        {
            CancellationToken = cancellationToken
        };

        _ = await projectClient.AgentAdministrationClient.UpdateAgentAsync(
            options.AgentName,
            content,
            options: requestOptions);
    }

    private async Task CreateAgentAsync(CancellationToken cancellationToken)
    {
        var definition = BuildAgentDefinition(
            options.ModelDeployment,
            options.VectorStoreIds);

        try
        {
            _ = await projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
                options.AgentName,
                new ProjectsAgentVersionCreationOptions(definition),
                cancellationToken: cancellationToken);
        }
        catch (ClientResultException exception) when (exception.Status == 409)
        {
            await UpdateExistingAgentAsync(cancellationToken);
        }
    }
}
