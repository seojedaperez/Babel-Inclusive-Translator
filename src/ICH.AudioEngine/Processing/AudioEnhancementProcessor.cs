using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace ICH.AudioEngine.Processing;

/// <summary>
/// Audio enhancement processor for improving speech clarity and quality.
/// Applies high-pass filter, dynamic compression, and voice frequency boost.
/// Inspired by UTell.ai's Sound Quality Enhancement pipeline.
/// </summary>
public sealed class AudioEnhancementProcessor
{
    private readonly ILogger<AudioEnhancementProcessor> _logger;
    private bool _enabled = true;

    // High-pass filter state (removes low rumble < 80Hz)
    private float _hpFilterState;

    // Compressor state
    private float _compressorEnvelope;
    private const float CompressorThreshold = 0.3f;
    private const float CompressorRatio = 4f;
    private const float CompressorAttack = 0.003f;
    private const float CompressorRelease = 0.05f;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _logger.LogInformation("Audio enhancement {Status}", value ? "enabled" : "disabled");
        }
    }

    public AudioEnhancementProcessor(ILogger<AudioEnhancementProcessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Process audio through the enhancement pipeline:
    /// 1. High-Pass Filter (removes rumble below 80Hz)
    /// 2. Voice Presence Boost (2-4kHz emphasis)
    /// 3. Dynamic Compression (normalizes volume)
    /// 4. Output Gain Normalization
    /// </summary>
    public byte[] ProcessAudio(byte[] audioData, WaveFormat format)
    {
        if (!_enabled || audioData.Length == 0)
            return audioData;

        try
        {
            var samples = ConvertToFloatSamples(audioData, format);
            if (samples.Length == 0) return audioData;

            // Step 1: High-pass filter — remove rumble below 80Hz
            ApplyHighPassFilter(samples, format.SampleRate, 80f);

            // Step 2: Voice presence boost — emphasize 2-4kHz range for clarity
            ApplyVoicePresenceBoost(samples, format.SampleRate);

            // Step 3: Dynamic compression — normalize volume levels
            ApplyDynamicCompression(samples);

            // Step 4: Output gain normalization — prevent clipping
            NormalizeGain(samples);

            return ConvertFromFloatSamples(samples, format);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio enhancement processing failed");
            return audioData;
        }
    }

    /// <summary>
    /// Simple first-order Butterworth high-pass filter.
    /// </summary>
    private void ApplyHighPassFilter(float[] samples, int sampleRate, float cutoffHz)
    {
        // RC high-pass filter coefficient
        float rc = 1.0f / (2.0f * MathF.PI * cutoffHz);
        float dt = 1.0f / sampleRate;
        float alpha = rc / (rc + dt);

        float prevInput = samples.Length > 0 ? samples[0] : 0;
        float prevOutput = _hpFilterState;

        for (int i = 0; i < samples.Length; i++)
        {
            float input = samples[i];
            float output = alpha * (prevOutput + input - prevInput);
            samples[i] = output;
            prevInput = input;
            prevOutput = output;
        }

        _hpFilterState = prevOutput;
    }

    /// <summary>
    /// Boost the 2-4kHz "presence" range where speech clarity lives.
    /// Uses a simple parametric EQ approximation.
    /// </summary>
    private static void ApplyVoicePresenceBoost(float[] samples, int sampleRate)
    {
        // Peaking EQ at 3kHz with Q=1.0 and +3dB gain
        float f0 = 3000f;
        float gain = 1.4f; // ~3dB
        float q = 1.0f;
        float w0 = 2.0f * MathF.PI * f0 / sampleRate;
        float sinW0 = MathF.Sin(w0);
        float cosW0 = MathF.Cos(w0);
        float alpha = sinW0 / (2.0f * q);
        float a = MathF.Sqrt(gain);

        // Peaking EQ coefficients (from Audio EQ Cookbook)
        float b0 = 1 + alpha * a;
        float b1 = -2 * cosW0;
        float b2 = 1 - alpha * a;
        float a0 = 1 + alpha / a;
        float a1 = -2 * cosW0;
        float a2 = 1 - alpha / a;

        // Normalize
        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 /= a0; a2 /= a0;

        float x1 = 0, x2 = 0, y1 = 0, y2 = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            float x0 = samples[i];
            float y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            samples[i] = y0;

            x2 = x1; x1 = x0;
            y2 = y1; y1 = y0;
        }
    }

    /// <summary>
    /// Apply dynamic range compression to normalize volume.
    /// </summary>
    private void ApplyDynamicCompression(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = MathF.Abs(samples[i]);

            // Envelope follower
            if (abs > _compressorEnvelope)
                _compressorEnvelope = CompressorAttack * abs + (1 - CompressorAttack) * _compressorEnvelope;
            else
                _compressorEnvelope = CompressorRelease * abs + (1 - CompressorRelease) * _compressorEnvelope;

            // Apply compression above threshold
            if (_compressorEnvelope > CompressorThreshold)
            {
                float excessDb = 20f * MathF.Log10(_compressorEnvelope / CompressorThreshold);
                float reducedDb = excessDb / CompressorRatio;
                float gain = MathF.Pow(10f, (reducedDb - excessDb) / 20f);
                samples[i] *= gain;
            }
        }
    }

    /// <summary>
    /// Normalize output to prevent clipping.
    /// </summary>
    private static void NormalizeGain(float[] samples)
    {
        float peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = MathF.Abs(samples[i]);
            if (abs > peak) peak = abs;
        }

        // If peak is above 0.95, scale down to prevent clipping
        if (peak > 0.95f)
        {
            float scale = 0.95f / peak;
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= scale;
        }
    }

    /// <summary>
    /// Reset processor state (e.g., after pause or stream change).
    /// </summary>
    public void Reset()
    {
        _hpFilterState = 0;
        _compressorEnvelope = 0;
        _logger.LogDebug("Audio enhancement processor state reset");
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
}
