using NAudio.Wave;
using Microsoft.Extensions.Logging;

namespace ICH.AudioEngine.VirtualDevices;

/// <summary>
/// Virtual microphone input that provides translated speech as microphone output.
/// Other applications (Teams, Zoom, etc.) can select this as their microphone input.
/// 
/// Uses a named pipe or audio loopback mechanism to expose translated audio
/// as a microphone source. Works in conjunction with a virtual audio cable driver.
/// </summary>
public sealed class VirtualMicrophoneOutput : IDisposable
{
    private readonly ILogger<VirtualMicrophoneOutput> _logger;
    private BufferedWaveProvider? _bufferProvider;
    private WaveOutEvent? _waveOut;
    private bool _isActive;
    private readonly WaveFormat _format;

    /// <summary>
    /// The wave provider that can be read by virtual audio cable software.
    /// </summary>
    public IWaveProvider? WaveProvider => _bufferProvider;
    public bool IsActive => _isActive;

    public VirtualMicrophoneOutput(ILogger<VirtualMicrophoneOutput> logger, WaveFormat? format = null)
    {
        _logger = logger;
        _format = format ?? new WaveFormat(16000, 16, 1);
    }

    /// <summary>
    /// Auto-discovers the VB-Audio Virtual Cable device index.
    /// NAudio uses integer indices for WaveOut devices, so we must enumerate them.
    /// </summary>
    private int FindVbCableDeviceNumber()
    {
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var capabilities = WaveOut.GetCapabilities(i);
            // "CABLE Input" is typically the name of the Virtual Cable playback endpoint.
            if (capabilities.ProductName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) || 
                capabilities.ProductName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Found VB-Audio Virtual Cable at device index {DeviceIndex}: {ProductName}", i, capabilities.ProductName);
                return i;
            }
        }
        
        _logger.LogWarning("VB-Audio Virtual Cable ('CABLE Input') not found among {DeviceCount} output devices.", WaveOut.DeviceCount);
        return -1; // -1 means default playback device in NAudio
    }

    /// <summary>
    /// Start the virtual microphone output.
    /// Routes audio to a specified output device (typically a virtual audio cable input).
    /// </summary>
    /// <param name="virtualCableDeviceNumber">
    /// Device number of the virtual audio cable input.
    /// Use AudioDeviceManager to find the correct device.
    /// </param>
    public void Start(int virtualCableDeviceNumber = -1)
    {
        if (_isActive) return;

        try
        {
            _bufferProvider = new BufferedWaveProvider(_format)
            {
                BufferDuration = TimeSpan.FromSeconds(10),
                DiscardOnBufferOverflow = true
            };

            // Auto-discover the VB-Cable if no specific device was requested
            int targetDevice = virtualCableDeviceNumber;
            if (targetDevice == -1)
            {
                targetDevice = FindVbCableDeviceNumber();
                if (targetDevice == -1)
                {
                    _logger.LogWarning("Falling back to default audio output because VB-Cable was not found.");
                }
            }

            // Route to the virtual cable's input
            _waveOut = new WaveOutEvent
            {
                DeviceNumber = targetDevice,
                DesiredLatency = 100
            };

            _waveOut.Init(_bufferProvider);
            _waveOut.Play();
            _isActive = true;

            _logger.LogInformation(
                "Virtual microphone output started. Target device: {Device}", targetDevice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start virtual microphone output");
            throw;
        }
    }

    /// <summary>
    /// Write translated speech audio to the virtual microphone.
    /// This audio will appear as microphone input to other applications.
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
            _logger.LogError(ex, "Error writing to virtual microphone");
        }
    }

    /// <summary>
    /// Write translated speech audio from a stream.
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
            _logger.LogError(ex, "Error streaming to virtual microphone");
        }
    }

    /// <summary>
    /// Stop the virtual microphone output.
    /// </summary>
    public void Stop()
    {
        if (!_isActive) return;

        try
        {
            _waveOut?.Stop();
            _isActive = false;
            _logger.LogInformation("Virtual microphone output stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping virtual microphone output");
        }
    }

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
