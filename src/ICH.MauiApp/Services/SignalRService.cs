using Microsoft.AspNetCore.SignalR.Client;

namespace ICH.MauiApp.Services;

/// <summary>
/// SignalR client service for connecting to the backend hub.
/// </summary>
public class SignalRService : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private readonly string _hubUrl;

    public event Action<string, string, string, string, bool>? SubtitleReceived;
    public event Action<string, string, string, string, string?, double, string?>? TranscriptReceived;
    public event Action<string, string, int, int>? SignLanguageGestureReceived;
    public event Action<string, bool, bool, double, double, int>? PipelineStatusReceived;
    public event Action<bool>? ConnectionStatusChanged;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public SignalRService(string hubUrl = "https://localhost:49940/hub/audio")
    {
        _hubUrl = hubUrl;
    }

    public async Task ConnectAsync(string? accessToken = null, CancellationToken ct = default)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                if (!string.IsNullOrEmpty(accessToken))
                    options.AccessTokenProvider = () => Task.FromResult(accessToken)!;
                
                options.HttpMessageHandlerFactory = handler => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
            })
            .WithAutomaticReconnect()
            .Build();

        // Register event handlers
        _hubConnection.On<string, string, string, string, string, bool>(
            "ReceiveSubtitle",
            (sessionId, original, translated, source, target, isFinal) =>
            {
                SubtitleReceived?.Invoke(original, translated, source, target, isFinal);
            });

        _hubConnection.On<string, string, string, string, string, string?, double, string?>(
            "ReceiveTranscript",
            (sessionId, original, translated, source, target, speakerId, confidence, emotion) =>
            {
                TranscriptReceived?.Invoke(original, translated, source, target, speakerId, confidence, emotion);
            });

        _hubConnection.On<string, string, string, int, int>(
            "ReceiveSignLanguageGesture",
            (sessionId, word, animationId, durationMs, index) =>
            {
                SignLanguageGestureReceived?.Invoke(word, animationId, durationMs, index);
            });

        _hubConnection.On<string, bool, bool, double, double, int>(
            "ReceivePipelineStatus",
            (sessionId, inputActive, outputActive, inputLatency, outputLatency, totalEntries) =>
            {
                PipelineStatusReceived?.Invoke(sessionId, inputActive, outputActive,
                    inputLatency, outputLatency, totalEntries);
            });

        _hubConnection.Reconnecting += _ =>
        {
            ConnectionStatusChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += _ =>
        {
            ConnectionStatusChanged?.Invoke(true);
            return Task.CompletedTask;
        };

        await _hubConnection.StartAsync(ct);
        ConnectionStatusChanged?.Invoke(true);
    }

    public async Task JoinSessionAsync(string sessionId)
    {
        if (_hubConnection != null)
            await _hubConnection.SendAsync("JoinSession", sessionId);
    }

    public async Task LeaveSessionAsync(string sessionId)
    {
        if (_hubConnection != null)
            await _hubConnection.SendAsync("LeaveSession", sessionId);
    }

    public async Task SendKeyboardInputAsync(string sessionId, string text, string targetLanguage)
    {
        if (_hubConnection != null)
            await _hubConnection.SendAsync("SendKeyboardInput", sessionId, text, targetLanguage);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
            await _hubConnection.DisposeAsync();
    }
}
