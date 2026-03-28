using NAudio.CoreAudioApi;
using Microsoft.Extensions.Logging;
using ICH.Shared.DTOs;

namespace ICH.AudioEngine.Devices;

/// <summary>
/// Manages audio device enumeration and selection.
/// </summary>
public sealed class AudioDeviceManager : IDisposable
{
    private readonly ILogger<AudioDeviceManager> _logger;
    private readonly MMDeviceEnumerator _enumerator;

    public AudioDeviceManager(ILogger<AudioDeviceManager> logger)
    {
        _logger = logger;
        _enumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// Get all available input (microphone) devices.
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    DeviceType = AudioDeviceType.Microphone,
                    IsDefault = device.ID == defaultDevice.ID,
                    IsVirtual = device.FriendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                                device.FriendlyName.Contains("ICH", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate input devices");
        }
        return devices;
    }

    /// <summary>
    /// Get all available output (speaker) devices.
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    DeviceType = AudioDeviceType.Speaker,
                    IsDefault = device.ID == defaultDevice.ID,
                    IsVirtual = device.FriendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                                device.FriendlyName.Contains("ICH", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate output devices");
        }
        return devices;
    }

    /// <summary>
    /// Get a specific device by its ID.
    /// </summary>
    public MMDevice? GetDevice(string deviceId)
    {
        try
        {
            return _enumerator.GetDevice(deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get device {DeviceId}", deviceId);
            return null;
        }
    }

    /// <summary>
    /// Get the default microphone.
    /// </summary>
    public MMDevice GetDefaultMicrophone() =>
        _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

    /// <summary>
    /// Get the default speaker/output device.
    /// </summary>
    public MMDevice GetDefaultSpeaker() =>
        _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    public void Dispose()
    {
        _enumerator.Dispose();
    }
}
