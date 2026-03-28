using NAudio.CoreAudioApi;
using NAudio.Wave;
using Microsoft.Extensions.Logging;
using System.Reactive.Subjects;
using System.Reactive.Linq;

namespace ICH.AudioEngine.Capture;

/// <summary>
/// Captures system audio output using WASAPI Loopback.
/// This captures what the user hears (e.g., other people in a call).
/// </summary>
public sealed class SystemAudioCapture : IDisposable
{
    private readonly ILogger<SystemAudioCapture> _logger;
    private WasapiLoopbackCapture? _capture;
    private readonly Subject<AudioDataEventArgs> _audioSubject = new();
    private bool _isCapturing;

    public IObservable<AudioDataEventArgs> AudioStream => _audioSubject.AsObservable();
    public bool IsCapturing => _isCapturing;
    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    public SystemAudioCapture(ILogger<SystemAudioCapture> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start capturing system audio via WASAPI Loopback.
    /// </summary>
    public void StartCapture(MMDevice? device = null)
    {
        if (_isCapturing)
        {
            _logger.LogWarning("System audio capture is already active");
            return;
        }

        try
        {
            _capture = device != null
                ? new WasapiLoopbackCapture(device)
                : new WasapiLoopbackCapture();

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isCapturing = true;

            _logger.LogInformation(
                "System audio capture started. Format: {SampleRate}Hz, {Channels}ch, {BitsPerSample}bit",
                _capture.WaveFormat.SampleRate,
                _capture.WaveFormat.Channels,
                _capture.WaveFormat.BitsPerSample);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start system audio capture");
            throw;
        }
    }

    /// <summary>
    /// Stop capturing system audio.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing) return;

        try
        {
            _capture?.StopRecording();
            _isCapturing = false;
            _logger.LogInformation("System audio capture stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping system audio capture");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded > 0)
        {
            var buffer = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);

            _audioSubject.OnNext(new AudioDataEventArgs
            {
                Buffer = buffer,
                BytesRecorded = e.BytesRecorded,
                WaveFormat = _capture!.WaveFormat,
                Timestamp = DateTimeOffset.UtcNow,
                Source = AudioSource.SystemOutput
            });
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger.LogError(e.Exception, "System audio capture stopped with error");
        }
        _isCapturing = false;
    }

    public void Dispose()
    {
        StopCapture();
        _capture?.Dispose();
        _audioSubject.Dispose();
    }
}
