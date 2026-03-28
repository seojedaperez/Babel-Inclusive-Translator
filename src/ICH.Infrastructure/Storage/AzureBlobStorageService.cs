using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ICH.Domain.Interfaces;
using ICH.Shared.Configuration;

namespace ICH.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage service for audio recordings.
/// </summary>
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;

    public AzureBlobStorageService(
        ILogger<AzureBlobStorageService> logger,
        IOptions<AzureStorageSettings> settings)
    {
        _logger = logger;
        _blobServiceClient = new BlobServiceClient(settings.Value.ConnectionString);
        _containerName = settings.Value.AudioContainerName;
    }

    public async Task<string> UploadAudioAsync(
        Guid sessionId, Stream audioStream, string contentType, CancellationToken ct = default)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(_containerName);
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

            var blobName = $"sessions/{sessionId}/{sessionId}.wav";
            var blobClient = container.GetBlobClient(blobName);

            var headers = new BlobHttpHeaders { ContentType = contentType };
            await blobClient.UploadAsync(audioStream, new BlobUploadOptions
            {
                HttpHeaders = headers,
                Metadata = new Dictionary<string, string>
                {
                    ["sessionId"] = sessionId.ToString(),
                    ["uploadedAt"] = DateTimeOffset.UtcNow.ToString("o")
                }
            }, ct);

            _logger.LogInformation("Audio uploaded for session {SessionId}: {BlobUri}", sessionId, blobClient.Uri);
            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload audio for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<Stream?> DownloadAudioAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobName = $"sessions/{sessionId}/{sessionId}.wav";
            var blobClient = container.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync(ct))
            {
                _logger.LogWarning("Audio blob not found for session {SessionId}", sessionId);
                return null;
            }

            var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download audio for session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task DeleteAudioAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobName = $"sessions/{sessionId}/{sessionId}.wav";
            var blobClient = container.GetBlobClient(blobName);

            await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
            _logger.LogInformation("Audio deleted for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete audio for session {SessionId}", sessionId);
        }
    }

    public async Task<string> GetAudioUrlAsync(Guid sessionId, TimeSpan expiry, CancellationToken ct = default)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobName = $"sessions/{sessionId}/{sessionId}.wav";
            var blobClient = container.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync(ct))
                return string.Empty;

            var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(expiry));
            return sasUri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate SAS URL for session {SessionId}", sessionId);
            return string.Empty;
        }
    }
}
