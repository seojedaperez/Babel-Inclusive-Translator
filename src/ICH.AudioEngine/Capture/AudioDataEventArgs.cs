using NAudio.Wave;

namespace ICH.AudioEngine.Capture;

/// <summary>
/// Audio data event arguments passed through the reactive stream.
/// </summary>
public class AudioDataEventArgs
{
    public byte[] Buffer { get; init; } = Array.Empty<byte>();
    public int BytesRecorded { get; init; }
    public WaveFormat WaveFormat { get; init; } = new(16000, 16, 1);
    public DateTimeOffset Timestamp { get; init; }
    public AudioSource Source { get; init; }
}

public enum AudioSource
{
    Microphone,
    SystemOutput,
    VirtualMicrophone,
    VirtualSpeaker,
    Keyboard
}
