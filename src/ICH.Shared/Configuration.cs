namespace ICH.Shared.Configuration;

public class AzureSpeechSettings
{
    public string SubscriptionKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string EndpointId { get; set; } = string.Empty;
}

public class AzureTranslatorSettings
{
    public string SubscriptionKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.cognitive.microsofttranslator.com";
    public string Region { get; set; } = string.Empty;
}

public class AzureOpenAISettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = "gpt-4o";
    public string EmbeddingDeploymentName { get; set; } = "text-embedding-ada-002";
}

public class AzureStorageSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string AudioContainerName { get; set; } = "audio-recordings";
    public string TranscriptContainerName { get; set; } = "transcripts";
}

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ICH.API";
    public string Audience { get; set; } = "ICH.Clients";
    public int ExpirationMinutes { get; set; } = 60;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}

public class AudioSettings
{
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
    public int BitsPerSample { get; set; } = 16;
    public int BufferSizeMs { get; set; } = 100;
    public string DefaultInputDeviceId { get; set; } = string.Empty;
    public string DefaultOutputDeviceId { get; set; } = string.Empty;
    public bool EnableNoiseSuppression { get; set; } = true;
    public bool EnableAutomaticGainControl { get; set; } = true;
}

public class SignalRSettings
{
    public string HubUrl { get; set; } = "https://localhost:5001/hub/audio";
    public int ReconnectIntervalSeconds { get; set; } = 5;
    public int MaxReconnectAttempts { get; set; } = 10;
}

public class ResponsibleAISettings
{
    public bool RequireConsent { get; set; } = true;
    public bool AllowRecording { get; set; } = true;
    public bool AllowDeletion { get; set; } = true;
    public bool ShowAINotification { get; set; } = true;
    public int MaxRetentionDays { get; set; } = 90;
}
