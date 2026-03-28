using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using ICH.MauiApp.Services;

namespace ICH.MauiApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SignalRService _signalRService;

    [ObservableProperty]
    private string _sessionTitle = "No Active Session";

    [ObservableProperty]
    private string _sourceLanguage = "EN";

    [ObservableProperty]
    private string _targetLanguage = "ES";

    [ObservableProperty]
    private string _subtitleOriginal = "Waiting for speech...";

    [ObservableProperty]
    private string _subtitleTranslated = "Esperando el discurso...";

    [ObservableProperty]
    private string _currentSignWord = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isPipelineActive;

    [ObservableProperty]
    private double _latencyMs;

    [ObservableProperty]
    private int _transcriptCount;

    public ObservableCollection<TranscriptItem> Transcripts { get; } = new();

    public MainViewModel(SignalRService signalRService)
    {
        _signalRService = signalRService;

        _signalRService.ConnectionStatusChanged += (isConnected) => 
        {
            MainThread.BeginInvokeOnMainThread(() => IsConnected = isConnected);
        };

        _signalRService.PipelineStatusReceived += (sessionId, inputActive, outputActive, inputLatency, outputLatency, transcriptCount) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsPipelineActive = inputActive || outputActive;
                LatencyMs = Math.Max(inputLatency, outputLatency);
                TranscriptCount = transcriptCount;
                if (!string.IsNullOrEmpty(sessionId))
                    SessionTitle = $"Session: {sessionId}";
            });
        };

        _signalRService.SubtitleReceived += (original, translated, source, target, isFinal) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                SubtitleOriginal = original;
                SubtitleTranslated = translated;
            });
        };

        _signalRService.TranscriptReceived += (original, translated, source, target, speakerId, confidence, emotion) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Transcripts.Add(new TranscriptItem 
                { 
                    Time = DateTime.Now.ToString("HH:mm:ss"), 
                    Original = original, 
                    Translated = translated,
                    Speaker = speakerId,
                    Emotion = emotion
                });
            });
        };

        _signalRService.SignLanguageGestureReceived += (word, animationId, durationMs, index) =>
        {
            MainThread.BeginInvokeOnMainThread(() => CurrentSignWord = word);
        };

        // Try to connect (non-blocking, won't crash if backend is unavailable)
        Task.Run(async () =>
        {
            try
            {
                await _signalRService.ConnectAsync();
                System.Diagnostics.Debug.WriteLine("SignalR connected successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR connection failed (app will work offline): {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private void StartPipeline()
    {
        // Actually you'd probably trigger the backend here or join a session
        IsPipelineActive = true;
    }

    [RelayCommand]
    private void StopPipeline()
    {
        IsPipelineActive = false;
    }
}

public class TranscriptItem
{
    public string Time { get; set; } = "";
    public string Original { get; set; } = "";
    public string Translated { get; set; } = "";
    public string? Speaker { get; set; }
    public string? Emotion { get; set; }
}
