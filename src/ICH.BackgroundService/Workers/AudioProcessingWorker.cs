using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR.Client;
using ICH.AudioEngine.Capture;
using ICH.AudioEngine.Devices;
using ICH.AudioEngine.VirtualDevices;
using ICH.AudioEngine.Processing;
using ICH.AIPipeline.Pipeline;
using ICH.AIPipeline.Speech;
using ICH.AIPipeline.Translation;
using ICH.Shared.Configuration;
using ICH.Shared.DTOs;

namespace ICH.BackgroundService.Workers;

/// <summary>
/// Main background service that orchestrates audio capture, AI processing,
/// and virtual device output. Runs as a Windows Service.
/// </summary>
public class AudioProcessingWorker : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly ILogger<AudioProcessingWorker> _logger;
    private readonly AudioSettings _audioSettings;
    private readonly SignalRSettings _signalRSettings;

    private SystemAudioCapture? _systemCapture;
    private MicrophoneCapture? _micCapture;
    private readonly AudioDeviceManager _deviceManager;
    private VirtualAudioOutput? _virtualSpeaker;
    private VirtualMicrophoneOutput? _virtualMic;
    private readonly AudioPipelineOrchestrator _pipeline;
    private AudioFormatConverter? _formatConverter;

    private HubConnection? _hubConnection;
    private string _currentSessionId = string.Empty;

    public AudioProcessingWorker(
        ILogger<AudioProcessingWorker> logger,
        IOptions<AudioSettings> audioSettings,
        IOptions<SignalRSettings> signalRSettings,
        AudioDeviceManager deviceManager,
        AudioPipelineOrchestrator pipeline)
    {
        _logger = logger;
        _audioSettings = audioSettings.Value;
        _signalRSettings = signalRSettings.Value;
        _deviceManager = deviceManager;
        _pipeline = pipeline;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("═══════════════════════════════════════════════════════════");
        _logger.LogInformation("  Inclusive Communication Hub - Background Service");
        _logger.LogInformation("  Starting audio processing system...");
        _logger.LogInformation("═══════════════════════════════════════════════════════════");

        try
        {
            // 1. Initialize audio devices
            InitializeDevices();

            // 2. Connect to SignalR hub
            await ConnectToHubAsync(stoppingToken);

            // 3. Main processing loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check hub connection health
                    if (_hubConnection?.State != HubConnectionState.Connected)
                    {
                        _logger.LogWarning("SignalR disconnected, attempting reconnection...");
                        await ConnectToHubAsync(stoppingToken);
                    }

                    // Send periodic status updates
                    if (!string.IsNullOrEmpty(_currentSessionId))
                    {
                        var status = _pipeline.GetStatus();
                        await BroadcastStatusAsync(status);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in main processing loop");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Audio Processing Worker");
        }
        finally
        {
            await ShutdownAsync();
        }
    }

    private void InitializeDevices()
    {
        _logger.LogInformation("Initializing audio devices...");

        // List available devices
        var inputDevices = _deviceManager.GetInputDevices();
        var outputDevices = _deviceManager.GetOutputDevices();

        _logger.LogInformation("Available input devices:");
        foreach (var d in inputDevices)
            _logger.LogInformation("  [{Id}] {Name} {Default}{Virtual}",
                d.Id[..Math.Min(20, d.Id.Length)], d.Name,
                d.IsDefault ? " (DEFAULT)" : "",
                d.IsVirtual ? " (VIRTUAL)" : "");

        _logger.LogInformation("Available output devices:");
        foreach (var d in outputDevices)
            _logger.LogInformation("  [{Id}] {Name} {Default}{Virtual}",
                d.Id[..Math.Min(20, d.Id.Length)], d.Name,
                d.IsDefault ? " (DEFAULT)" : "",
                d.IsVirtual ? " (VIRTUAL)" : "");

        // Initialize captures
        var logFactory = LoggerFactory.Create(b => b.AddConsole());

        _systemCapture = new SystemAudioCapture(logFactory.CreateLogger<SystemAudioCapture>());
        _micCapture = new MicrophoneCapture(logFactory.CreateLogger<MicrophoneCapture>());
        _virtualSpeaker = new VirtualAudioOutput(logFactory.CreateLogger<VirtualAudioOutput>());
        _virtualMic = new VirtualMicrophoneOutput(logFactory.CreateLogger<VirtualMicrophoneOutput>());
        _formatConverter = new AudioFormatConverter(logFactory.CreateLogger<AudioFormatConverter>());

        _logger.LogInformation("Audio devices initialized successfully");
    }

    private async Task ConnectToHubAsync(CancellationToken ct)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_signalRSettings.HubUrl, options => 
            {
                options.HttpMessageHandlerFactory = handler => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
            })
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            })
            .Build();

        // Handle commands from the hub
        _hubConnection.On<string, string, string>("StartPipeline", async (sessionId, source, target) =>
        {
            _logger.LogInformation("Received StartPipeline command: {Session} {Source}→{Target}",
                sessionId, source, target);
            await StartProcessingAsync(sessionId, source, target, ct);
        });

        _hubConnection.On<string>("StopPipeline", async (sessionId) =>
        {
            _logger.LogInformation("Received StopPipeline command: {Session}", sessionId);
            await StopProcessingAsync();
        });

        _hubConnection.On<string, string, string>("SendKeyboardInput", async (sessionId, text, targetLang) =>
        {
            if (_pipeline != null)
            {
                await _pipeline.ProcessKeyboardInputAsync(text, _virtualMic, ct);
            }
        });

        _hubConnection.On<string, string, string>("UpdateLanguage", (sessionId, source, target) =>
        {
            _pipeline?.UpdateLanguages(source, target);
        });

        _hubConnection.Reconnecting += (ex) =>
        {
            _logger.LogWarning(ex, "SignalR reconnecting...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += (connectionId) =>
        {
            _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync(ct);
            _logger.LogInformation("Connected to SignalR hub: {Url}", _signalRSettings.HubUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub. Will retry...");
        }
    }

    private async Task StartProcessingAsync(string sessionId, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        _currentSessionId = sessionId;

        try
        {
            _logger.LogInformation("Starting audio processing for session {SessionId}", sessionId);

            // Start capture devices
            _micCapture?.StartCapture();
            _systemCapture?.StartCapture();
            _virtualSpeaker?.Start();
            _virtualMic?.Start();

            // Configure and start pipeline
            // Note: In production, these services would be properly DI-injected
            // This is simplified for the background worker context
            _pipeline?.Configure(sessionId, sourceLanguage, targetLanguage);

            if (_pipeline != null && _micCapture != null)
            {
                await _pipeline.StartInputPipelineAsync(_micCapture, _virtualMic, ct);
            }

            if (_pipeline != null && _systemCapture != null)
            {
                await _pipeline.StartOutputPipelineAsync(_systemCapture, _virtualSpeaker, ct);
            }

            // Subscribe to pipeline events → forward to hub
            _pipeline?.Subtitles.Subscribe(async subtitle =>
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("BroadcastSubtitle",
                        sessionId, subtitle.Text, subtitle.TranslatedText,
                        subtitle.SourceLanguage, subtitle.TargetLanguage,
                        subtitle.IsFinal, ct);
                }
            });

            _pipeline?.ProcessingEvents.Subscribe(async evt =>
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("BroadcastTranscript",
                        sessionId, evt.OriginalText, evt.TranslatedText,
                        evt.SourceLanguage, evt.TargetLanguage,
                        evt.SpeakerId, evt.Confidence, evt.Emotion, ct);
                }
            });

            _pipeline?.SignLanguageGestures.Subscribe(async gesture =>
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("BroadcastSignLanguageGesture",
                        sessionId, gesture.Word, gesture.AnimationId,
                        gesture.DurationMs, gesture.SequenceIndex, ct);
                }
            });

            _logger.LogInformation("Audio processing started for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start processing for session {SessionId}", sessionId);
        }
    }

    private async Task StopProcessingAsync()
    {
        try
        {
            if (_pipeline != null)
                await _pipeline.StopAsync();

            _micCapture?.StopCapture();
            _systemCapture?.StopCapture();
            _virtualSpeaker?.Stop();
            _virtualMic?.Stop();

            _logger.LogInformation("Audio processing stopped for session {SessionId}", _currentSessionId);
            _currentSessionId = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping audio processing");
        }
    }

    private async Task BroadcastStatusAsync(PipelineStatus status)
    {
        if (_hubConnection?.State != HubConnectionState.Connected) return;

        try
        {
            await _hubConnection.SendAsync("ReceivePipelineStatus",
                status.SessionId,
                status.IsInputPipelineActive,
                status.IsOutputPipelineActive,
                status.InputLatencyMs,
                status.OutputLatencyMs,
                status.TotalTranscriptEntries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting status");
        }
    }

    private async Task ShutdownAsync()
    {
        _logger.LogInformation("Shutting down Audio Processing Worker...");

        await StopProcessingAsync();

        _systemCapture?.Dispose();
        _micCapture?.Dispose();
        _virtualSpeaker?.Dispose();
        _virtualMic?.Dispose();
        _deviceManager?.Dispose();

        if (_pipeline != null)
            await _pipeline.DisposeAsync();

        if (_hubConnection != null)
            await _hubConnection.DisposeAsync();

        _logger.LogInformation("Audio Processing Worker shut down successfully");
    }
}
