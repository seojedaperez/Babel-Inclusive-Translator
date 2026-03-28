using NAudio.Wave;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ICH.AudioEngine.VirtualDevices;

/// <summary>
/// Virtual audio output device that receives translated speech audio.
/// This acts as a virtual speaker - other applications can select this as their audio source.
/// 
/// NOTE: True virtual audio device drivers require kernel-mode drivers (WDM).
/// This implementation uses a local audio pipe that can be routed via
/// software like VB-Audio Virtual Cable or Windows Audio Session API.
/// 
/// For production deployment, a signed kernel driver would be needed.
/// This implementation provides the audio routing layer that feeds into
/// whatever virtual device solution is available.
/// </summary>
public sealed class VirtualAudioOutput : IDisposable
{
    private readonly ILogger<VirtualAudioOutput> _logger;
    private readonly ConcurrentQueue<byte[]> _audioQueue = new();
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _bufferProvider;
    private bool _isActive;
    private readonly WaveFormat _outputFormat;

    public bool IsActive => _isActive;
    public WaveFormat OutputFormat => _outputFormat;

    public VirtualAudioOutput(ILogger<VirtualAudioOutput> logger, WaveFormat? outputFormat = null)
    {
        _logger = logger;
        _outputFormat = outputFormat ?? new WaveFormat(16000, 16, 1);
    }

    /// <summary>
    /// Initialize the virtual output device and start playing.
    /// </summary>
    /// <param name="deviceNumber">The output device number. -1 for default.</param>
    public void Start(int deviceNumber = -1)
    {
        if (_isActive) return;

        try
        {
            _bufferProvider = new BufferedWaveProvider(_outputFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(10),
                DiscardOnBufferOverflow = true
            };

            _waveOut = new WaveOutEvent
            {
                DeviceNumber = deviceNumber,
                DesiredLatency = 100
            };

            _waveOut.Init(_bufferProvider);
            _waveOut.Play();
            _isActive = true;

            _logger.LogInformation(
                "Virtual audio output started on device {Device}. Format: {Format}",
                deviceNumber, _outputFormat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start virtual audio output");
            throw;
        }
    }

    /// <summary>
    /// Write audio data to the virtual output.
    /// This is the translated speech audio that will be played to the user.
    /// </summary>
    public void WriteAudio(byte[] audioData)
    {
        if (!_isActive || _bufferProvider == null) return;

        try
        {
            _bufferProvider.AddSamples(audioData, 0, audioData.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to virtual audio output");
        }
    }

    /// <summary>
    /// Write audio data from a stream.
    /// </summary>
    public async Task WriteAudioAsync(Stream audioStream, CancellationToken ct = default)
    {
        if (!_isActive || _bufferProvider == null) return;

        try
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await audioStream.ReadAsync(buffer, ct)) > 0)
            {
                _bufferProvider.AddSamples(buffer, 0, bytesRead);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming to virtual audio output");
        }
    }

    /// <summary>
    /// Stop the virtual output device.
    /// </summary>
    public void Stop()
    {
        if (!_isActive) return;

        try
        {
            _waveOut?.Stop();
            _isActive = false;
            _logger.LogInformation("Virtual audio output stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping virtual audio output");
        }
    }

    /// <summary>
    /// Clear the audio buffer.
    /// </summary>
    public void ClearBuffer()
    {
        _bufferProvider?.ClearBuffer();
    }

    public void Dispose()
    {
        Stop();
        _waveOut?.Dispose();
    }
}
