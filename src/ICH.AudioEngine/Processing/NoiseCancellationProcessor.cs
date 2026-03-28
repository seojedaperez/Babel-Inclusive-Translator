using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace ICH.AudioEngine.Processing;

/// <summary>
/// AI-powered noise cancellation processor.
/// Uses spectral gating to remove background noise from audio streams.
/// Integrates with the audio pipeline before STT processing.
/// </summary>
public sealed class NoiseCancellationProcessor
{
    private readonly ILogger<NoiseCancellationProcessor> _logger;
    private NoiseSuppressionLevel _level = NoiseSuppressionLevel.Medium;
    private bool _enabled = true;

    // Noise profile learned from silent frames
    private float[]? _noiseProfile;
    private int _noiseProfileFrames;
    private const int NoiseProfileLearningFrames = 10;
    private const int FftSize = 512;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _logger.LogInformation("Noise cancellation {Status}", value ? "enabled" : "disabled");
        }
    }

    public NoiseCancellationProcessor(ILogger<NoiseCancellationProcessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Configure the noise suppression level.
    /// </summary>
    public void SetLevel(NoiseSuppressionLevel level)
    {
        _level = level;
        _noiseProfile = null; // Reset noise profile when level changes
        _noiseProfileFrames = 0;
        _logger.LogInformation("Noise suppression level set to {Level}", level);
    }

    /// <summary>
    /// Process audio data through the noise cancellation pipeline.
    /// Uses spectral subtraction for noise reduction.
    /// </summary>
    public byte[] ProcessAudio(byte[] audioData, WaveFormat format)
    {
        if (!_enabled || audioData.Length == 0)
            return audioData;

        try
        {
            // Convert to float samples for processing
            var samples = ConvertToFloatSamples(audioData, format);
            if (samples.Length == 0) return audioData;

            // Apply noise gate (simple but effective for real-time)
            var threshold = _level switch
            {
                NoiseSuppressionLevel.Low => 0.005f,
                NoiseSuppressionLevel.Medium => 0.015f,
                NoiseSuppressionLevel.High => 0.03f,
                _ => 0.015f
            };

            // Compute RMS energy
            float rms = 0f;
            for (int i = 0; i < samples.Length; i++)
                rms += samples[i] * samples[i];
            rms = MathF.Sqrt(rms / samples.Length);

            // Learn noise floor from quiet frames
            if (rms < threshold * 0.5f && _noiseProfileFrames < NoiseProfileLearningFrames)
            {
                LearnNoiseProfile(samples);
            }

            // Apply spectral subtraction if we have a noise profile
            if (_noiseProfile != null)
            {
                ApplySpectralSubtraction(samples, threshold);
            }
            else
            {
                // Fallback: simple noise gate
                ApplyNoiseGate(samples, threshold);
            }

            return ConvertFromFloatSamples(samples, format);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Noise cancellation processing failed");
            return audioData;
        }
    }

    private void LearnNoiseProfile(float[] samples)
    {
        if (_noiseProfile == null)
        {
            _noiseProfile = new float[samples.Length];
        }

        // Average noise energy per sample position
        for (int i = 0; i < Math.Min(samples.Length, _noiseProfile.Length); i++)
        {
            _noiseProfile[i] = (_noiseProfile[i] * _noiseProfileFrames + MathF.Abs(samples[i])) / (_noiseProfileFrames + 1);
        }
        _noiseProfileFrames++;
        
        if (_noiseProfileFrames == NoiseProfileLearningFrames)
        {
            _logger.LogDebug("Noise profile learned from {Frames} frames", _noiseProfileFrames);
        }
    }

    private void ApplySpectralSubtraction(float[] samples, float threshold)
    {
        // Spectral subtraction: subtract estimated noise from signal
        var overSubtraction = _level switch
        {
            NoiseSuppressionLevel.Low => 1.0f,
            NoiseSuppressionLevel.Medium => 2.0f,
            NoiseSuppressionLevel.High => 4.0f,
            _ => 2.0f
        };

        for (int i = 0; i < samples.Length; i++)
        {
            float noiseEstimate = (i < _noiseProfile!.Length) ? _noiseProfile[i] * overSubtraction : threshold;
            float signalMagnitude = MathF.Abs(samples[i]);

            if (signalMagnitude < noiseEstimate)
            {
                // Below noise floor — attenuate heavily
                samples[i] *= 0.05f;
            }
            else
            {
                // Above noise floor — reduce by noise estimate
                float scale = (signalMagnitude - noiseEstimate * 0.5f) / signalMagnitude;
                samples[i] *= Math.Max(0.05f, scale);
            }
        }
    }

    private static void ApplyNoiseGate(float[] samples, float threshold)
    {
        // Simple noise gate with smooth attack/release
        const float attackCoeff = 0.1f;
        const float releaseCoeff = 0.01f;
        float envelope = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float abs = MathF.Abs(samples[i]);
            
            if (abs > envelope)
                envelope = attackCoeff * abs + (1 - attackCoeff) * envelope;
            else
                envelope = releaseCoeff * abs + (1 - releaseCoeff) * envelope;

            if (envelope < threshold)
            {
                // Soft knee gate — gradually reduce instead of hard cut
                float gain = envelope / threshold;
                samples[i] *= gain * gain; // Quadratic curve for smoother gating
            }
        }
    }

    private static float[] ConvertToFloatSamples(byte[] audioData, WaveFormat format)
    {
        if (format.BitsPerSample == 16)
        {
            var samples = new float[audioData.Length / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short sample = BitConverter.ToInt16(audioData, i * 2);
                samples[i] = sample / 32768f;
            }
            return samples;
        }
        else if (format.BitsPerSample == 32 && format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            var samples = new float[audioData.Length / 4];
            Buffer.BlockCopy(audioData, 0, samples, 0, audioData.Length);
            return samples;
        }
        return Array.Empty<float>();
    }

    private static byte[] ConvertFromFloatSamples(float[] samples, WaveFormat format)
    {
        if (format.BitsPerSample == 16)
        {
            var output = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Math.Clamp(samples[i], -1f, 1f);
                short pcm = (short)(clamped * 32767f);
                BitConverter.GetBytes(pcm).CopyTo(output, i * 2);
            }
            return output;
        }
        else if (format.BitsPerSample == 32)
        {
            var output = new byte[samples.Length * 4];
            Buffer.BlockCopy(samples, 0, output, 0, output.Length);
            return output;
        }
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Reset the learned noise profile (e.g., when environment changes).
    /// </summary>
    public void ResetNoiseProfile()
    {
        _noiseProfile = null;
        _noiseProfileFrames = 0;
        _logger.LogInformation("Noise profile reset");
    }
}

public enum NoiseSuppressionLevel
{
    Low,
    Medium,
    High,
    Auto
}
