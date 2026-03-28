using NAudio.Wave;
using NAudio.CoreAudioApi;
using Microsoft.Extensions.Logging;
using System.Reactive.Subjects;
using System.Reactive.Linq;

namespace ICH.AudioEngine.Capture;

/// <summary>
/// Captures microphone input using WASAPI.
/// This captures what the user speaks.
/// </summary>
public sealed class MicrophoneCapture : IDisposable
{
    private readonly ILogger<MicrophoneCapture> _logger;
    private WasapiCapture? _capture;
    private readonly Subject<AudioDataEventArgs> _audioSubject = new();
    private bool _isCapturing;

    public IObservable<AudioDataEventArgs> AudioStream => _audioSubject.AsObservable();
    public bool IsCapturing => _isCapturing;
    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    public MicrophoneCapture(ILogger<MicrophoneCapture> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start capturing microphone audio via WASAPI.
    /// </summary>
    public void StartCapture(MMDevice? device = null)
    {
        if (_isCapturing)
        {
            _logger.LogWarning("Microphone capture is already active");
            return;
        }

        try
        {
            if (device != null)
            {
                _capture = new WasapiCapture(device);
            }
            else
            {
                // Use default microphone
                var enumerator = new MMDeviceEnumerator();
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _capture = new WasapiCapture(defaultDevice);
            }

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isCapturing = true;

            _logger.LogInformation(
                "Microphone capture started. Format: {SampleRate}Hz, {Channels}ch, {BitsPerSample}bit",
                _capture.WaveFormat.SampleRate,
                _capture.WaveFormat.Channels,
                _capture.WaveFormat.BitsPerSample);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start microphone capture");
            throw;
        }
    }

    /// <summary>
    /// Stop capturing microphone audio.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing) return;

        try
        {
            _capture?.StopRecording();
            _isCapturing = false;
            _logger.LogInformation("Microphone capture stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping microphone capture");
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
                Source = AudioSource.Microphone
            });
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger.LogError(e.Exception, "Microphone capture stopped with error");
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
