using Azure.AI.Projects;
using Azure.Identity;
using OpenAI.Files;
using OpenAI.VectorStores;

namespace CsaMeetingCoach.KnowledgeIndexer;

#pragma warning disable OPENAI001
public static class Program
{
    private static readonly TimeSpan VectorStoreReadyTimeout =
        TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VectorStoreCleanupTimeout =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan UploadedFileCleanupTimeout =
        TimeSpan.FromSeconds(30);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var configuration = IndexerConfiguration.Parse(args);
            var files = KnowledgeSourceValidator.GetValidatedFiles(
                configuration.SourceDirectory);
            Console.Error.WriteLine(
                $"Uploading {files.Count} approved knowledge file(s).");

            var projectClient = new AIProjectClient(
                configuration.ProjectEndpoint,
                new DefaultAzureCredential());
            var fileClient = projectClient.ProjectOpenAIClient
                .GetOpenAIFileClient();
            var uploadedIds = new List<string>(files.Count);
            VectorStoreClient? vectorStoreClient = null;
            string? vectorStoreId = null;
            try
            {
                foreach (var path in files)
                {
                    OpenAIFile uploaded = await fileClient.UploadFileAsync(
                        path,
                        FileUploadPurpose.Assistants);
                    uploadedIds.Add(uploaded.Id);
                }

                Console.Error.WriteLine("Creating vector store.");
                vectorStoreClient = projectClient.ProjectOpenAIClient
                    .GetVectorStoreClient();
                var options = new VectorStoreCreationOptions
                {
                    Name = configuration.StoreName
                };
                foreach (var id in uploadedIds)
                {
                    options.FileIds.Add(id);
                }

                VectorStore store =
                    await vectorStoreClient.CreateVectorStoreAsync(options);
                vectorStoreId = store.Id;
                if (string.IsNullOrWhiteSpace(vectorStoreId)
                    || !vectorStoreId.StartsWith(
                        "vs_",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Foundry returned an invalid vector store identifier.");
                }

                Console.Error.WriteLine(
                    "Waiting for vector store ingestion to complete.");
                store = await WaitForReadyVectorStoreAsync(
                    vectorStoreClient,
                    vectorStoreId,
                    files.Count,
                    VectorStoreReadyTimeout);

                Console.WriteLine(store.Id);
                return 0;
            }
            catch
            {
                if (vectorStoreClient is not null
                    && vectorStoreId is not null)
                {
                    await DeleteVectorStoreBestEffortAsync(
                        vectorStoreClient,
                        vectorStoreId);
                }
                await DeleteUploadedFilesBestEffortAsync(
                    fileClient,
                    uploadedIds);
                throw;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Knowledge indexing failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<VectorStore> WaitForReadyVectorStoreAsync(
        VectorStoreClient client,
        string vectorStoreId,
        int expectedFileCount,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                timeoutSource.Token.ThrowIfCancellationRequested();
                var result = await client.GetVectorStoreAsync(
                    vectorStoreId,
                    timeoutSource.Token);
                var store = result.Value;
                if (store.Status == VectorStoreStatus.Completed)
                {
                    if (store.FileCounts.Total != expectedFileCount
                        || store.FileCounts.Completed != expectedFileCount
                        || store.FileCounts.Failed != 0
                        || store.FileCounts.InProgress != 0)
                    {
                        throw new InvalidOperationException(
                            $"Vector store ingestion completed with invalid file counts: {store.FileCounts.Completed} completed, {store.FileCounts.Failed} failed, {store.FileCounts.InProgress} in progress, {store.FileCounts.Total} total.");
                    }

                    return store;
                }

                if (store.Status != VectorStoreStatus.InProgress)
                {
                    throw new InvalidOperationException(
                        $"Vector store ingestion ended with status '{store.Status}'.");
                }

                await Task.Delay(
                    GetPollDelay(result.GetRawResponse()),
                    timeoutSource.Token);
            }
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Vector store ingestion did not complete within {timeout.TotalMinutes:0} minutes.",
                exception);
        }
    }

    private static TimeSpan GetPollDelay(
        System.ClientModel.Primitives.PipelineResponse response)
    {
        var delay = TimeSpan.FromSeconds(1);
        if (response.Headers.TryGetValue("Retry-After", out var retryAfter))
        {
            if (int.TryParse(retryAfter, out var delaySeconds))
            {
                delay = TimeSpan.FromSeconds(delaySeconds);
            }
            else if (DateTimeOffset.TryParse(
                retryAfter,
                out var retryAfterDate))
            {
                delay = retryAfterDate - DateTimeOffset.UtcNow;
            }
        }
        else if (response.Headers.TryGetValue(
                "openai-poll-after-ms",
                out var milliseconds)
            && int.TryParse(milliseconds, out var delayMilliseconds))
        {
            delay = TimeSpan.FromMilliseconds(delayMilliseconds);
        }

        return delay < TimeSpan.FromMilliseconds(250)
            ? TimeSpan.FromMilliseconds(250)
            : delay > TimeSpan.FromSeconds(10)
                ? TimeSpan.FromSeconds(10)
                : delay;
    }

    private static async Task DeleteVectorStoreBestEffortAsync(
        VectorStoreClient client,
        string vectorStoreId)
    {
        using var timeoutSource =
            new CancellationTokenSource(VectorStoreCleanupTimeout);
        try
        {
            await client.DeleteVectorStoreAsync(
                vectorStoreId,
                timeoutSource.Token);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Cleanup failed for vector store ID '{vectorStoreId}': {exception.Message}");
        }
    }

    private static async Task DeleteUploadedFilesBestEffortAsync(
        OpenAIFileClient fileClient,
        IEnumerable<string> uploadedIds)
    {
        using var timeoutSource =
            new CancellationTokenSource(UploadedFileCleanupTimeout);
        try
        {
            await Parallel.ForEachAsync(
                uploadedIds,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = timeoutSource.Token
                },
                async (id, cancellationToken) =>
                {
                    try
                    {
                        await fileClient.DeleteFileAsync(
                            id,
                            cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(
                            $"Cleanup failed for uploaded file ID '{id}': {exception.Message}");
                    }
                });
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Uploaded file cleanup stopped: {exception.Message}");
        }
    }
}
#pragma warning restore OPENAI001
