using NAudio.Wave;
using Microsoft.Extensions.Logging;

namespace ICH.AudioEngine.Processing;

/// <summary>
/// Converts audio between formats for pipeline processing.
/// Azure Speech Service requires 16kHz, 16-bit, mono PCM.
/// </summary>
public sealed class AudioFormatConverter
{
    private readonly ILogger<AudioFormatConverter> _logger;

    // Target format for Azure Speech Services
    public static readonly WaveFormat SpeechServiceFormat = new(16000, 16, 1);

    public AudioFormatConverter(ILogger<AudioFormatConverter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Convert audio data to the format required by Azure Speech Service.
    /// </summary>
    public byte[] ConvertToSpeechFormat(byte[] audioData, WaveFormat sourceFormat)
    {
        if (sourceFormat.SampleRate == SpeechServiceFormat.SampleRate &&
            sourceFormat.BitsPerSample == SpeechServiceFormat.BitsPerSample &&
            sourceFormat.Channels == SpeechServiceFormat.Channels)
        {
            return audioData;
        }

        try
        {
            using var sourceStream = new RawSourceWaveStream(audioData, 0, audioData.Length, sourceFormat);
            using var conversionStream = new WaveFormatConversionStream(SpeechServiceFormat, sourceStream);

            using var ms = new MemoryStream();
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = conversionStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, bytesRead);
            }
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio format conversion failed from {Source} to {Target}",
                FormatToString(sourceFormat), FormatToString(SpeechServiceFormat));
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Convert IEEE float audio to 16-bit PCM.
    /// WASAPI Loopback typically returns 32-bit IEEE float.
    /// </summary>
    public byte[] FloatToPcm16(byte[] floatData, int channels)
    {
        int sampleCount = floatData.Length / 4; // 4 bytes per float sample
        var pcmData = new byte[sampleCount * 2]; // 2 bytes per 16-bit sample

        for (int i = 0; i < sampleCount; i++)
        {
            float sample = BitConverter.ToSingle(floatData, i * 4);

            // Clamp
            sample = Math.Max(-1.0f, Math.Min(1.0f, sample));

            short pcmSample = (short)(sample * short.MaxValue);
            BitConverter.GetBytes(pcmSample).CopyTo(pcmData, i * 2);
        }

        return pcmData;
    }

    /// <summary>
    /// Downmix stereo to mono by averaging channels.
    /// </summary>
    public byte[] StereoToMono(byte[] stereoData, int bitsPerSample)
    {
        int bytesPerSample = bitsPerSample / 8;
        int samplePairs = stereoData.Length / (bytesPerSample * 2);
        var monoData = new byte[samplePairs * bytesPerSample];

        for (int i = 0; i < samplePairs; i++)
        {
            if (bitsPerSample == 16)
            {
                short left = BitConverter.ToInt16(stereoData, i * 4);
                short right = BitConverter.ToInt16(stereoData, i * 4 + 2);
                short mono = (short)((left + right) / 2);
                BitConverter.GetBytes(mono).CopyTo(monoData, i * 2);
            }
            else if (bitsPerSample == 32)
            {
                float left = BitConverter.ToSingle(stereoData, i * 8);
                float right = BitConverter.ToSingle(stereoData, i * 8 + 4);
                float mono = (left + right) / 2.0f;
                BitConverter.GetBytes(mono).CopyTo(monoData, i * 4);
            }
        }

        return monoData;
    }

    /// <summary>
    /// Resample audio to a target sample rate using simple linear interpolation.
    /// </summary>
    public byte[] Resample(byte[] audioData, WaveFormat sourceFormat, int targetSampleRate)
    {
        if (sourceFormat.SampleRate == targetSampleRate)
            return audioData;

        try
        {
            using var sourceStream = new RawSourceWaveStream(audioData, 0, audioData.Length, sourceFormat);
            var targetFormat = new WaveFormat(targetSampleRate, sourceFormat.BitsPerSample, sourceFormat.Channels);

            using var resampler = new MediaFoundationResampler(sourceStream, targetFormat);
            resampler.ResamplerQuality = 60;

            using var ms = new MemoryStream();
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, bytesRead);
            }
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resampling failed from {Source}Hz to {Target}Hz",
                sourceFormat.SampleRate, targetSampleRate);
            return audioData;
        }
    }

    private static string FormatToString(WaveFormat format) =>
        $"{format.SampleRate}Hz/{format.BitsPerSample}bit/{format.Channels}ch";
}
