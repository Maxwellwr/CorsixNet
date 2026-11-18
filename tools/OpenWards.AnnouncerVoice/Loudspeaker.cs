namespace OpenWards.AnnouncerVoice;

/// <summary>
/// Makes a clean voice sound like a hospital's public address system: the narrow band of a small horn speaker, a
/// little of its distortion, the echo of a large hall and the hiss of the line, at the original's rate.
/// </summary>
internal sealed record Loudspeaker
{
    /// <summary>The rate of the original's announcements.</summary>
    public int Rate { get; init; } = 22050;

    /// <summary>Below this the speaker gives nothing (Hz).</summary>
    public double LowCut { get; init; } = 380;

    /// <summary>Above this the speaker gives nothing (Hz).</summary>
    public double HighCut { get; init; } = 3400;

    /// <summary>Where the horn rings (Hz), and by how much (dB).</summary>
    public double Presence { get; init; } = 1900;

    public double PresenceGain { get; init; } = 5;

    /// <summary>
    /// The announcer's compressor, which keeps every word as loud as the original's: above this level (against a peak of
    /// 1) the voice is turned down by <see cref="Ratio"/>.
    /// </summary>
    public double Threshold { get; init; } = 0.08;

    public double Ratio { get; init; } = 4;

    /// <summary>How hard the amplifier is driven: 1 hardly distorts, 3 a lot.</summary>
    public double Drive { get; init; } = 1.8;

    /// <summary>The hall's echoes: when (seconds) and how loud.</summary>
    public IReadOnlyList<(double Delay, double Gain)> Echoes { get; init; } = [(0.055, 0.22), (0.120, 0.14), (0.205, 0.08), (0.310, 0.04)];

    /// <summary>The line's hiss, against a peak of 1.</summary>
    public double Hiss { get; init; } = 0.004;

    /// <summary>Silence before the voice and the echo after it (seconds).</summary>
    public double LeadIn { get; init; } = 0.08;

    public double Tail { get; init; } = 0.4;

    /// <summary>The loudest sample at the end.</summary>
    public double Peak { get; init; } = 0.9;

    /// <summary>A short description of the settings, saved with the files so that changing them speaks them again.</summary>
    public string Describe() =>
        FormattableString.Invariant($"rate {Rate}, band {LowCut}-{HighCut} Hz, presence {Presence} Hz +{PresenceGain} dB, compressor {Threshold}:{Ratio}, drive {Drive}, echoes {string.Join(" ", Echoes.Select(e => FormattableString.Invariant($"{e.Delay}s*{e.Gain}")))}, hiss {Hiss}, lead {LeadIn}s, tail {Tail}s, peak {Peak}");

    /// <summary>The voice <paramref name="input"/> at <paramref name="inputRate"/> through the speaker; <paramref name="noise"/> gives white noise between -1 and 1.</summary>
    public float[] Apply(ReadOnlySpan<float> input, int inputRate, Func<double> noise)
    {
        var voice = Resample(input, inputRate, Rate);
        Normalize(voice, 0.8);

        var lead = (int)(LeadIn * Rate);
        var length = lead + voice.Length + (int)(Tail * Rate);
        var signal = new double[length];
        for (var i = 0; i < voice.Length; i++)
        {
            signal[lead + i] = voice[i];
        }

        // The horn's band, its ring, then the amplifier driven a little too hard.
        Filter(signal, Biquad.HighPass(LowCut, Rate), Biquad.HighPass(LowCut, Rate), Biquad.Peak(Presence, PresenceGain, 1.2, Rate));
        Compress(signal);
        var norm = Math.Tanh(Drive);
        for (var i = 0; i < length; i++)
        {
            signal[i] = Math.Tanh(signal[i] * Drive) / norm;
        }

        // The hall, heard through the same speaker, with the line's hiss.
        var hall = (double[])signal.Clone();
        foreach (var (delay, gain) in Echoes)
        {
            var offset = (int)(delay * Rate);
            for (var i = offset; i < length; i++)
            {
                hall[i] += signal[i - offset] * gain;
            }
        }

        for (var i = 0; i < length; i++)
        {
            hall[i] += noise() * Hiss * 4;
        }

        Filter(hall, Biquad.LowPass(HighCut, Rate), Biquad.LowPass(HighCut, Rate), Biquad.HighPass(LowCut * 0.6, Rate));

        // The ends fade in and out, so that nothing clicks.
        var fade = Math.Min(length / 2, (int)(0.02 * Rate));
        for (var i = 0; i < fade; i++)
        {
            var gain = (double)i / fade;
            hall[i] *= gain;
            hall[length - 1 - i] *= gain;
        }

        // A limiter: the loudest few samples (one in a hundred) set the level, and those above it are rounded off under
        // the peak, so that short spikes do not leave the voice quiet.
        var sorted = hall.Select(Math.Abs).Order().ToArray();
        var level = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.99)] : 0;
        var makeup = level > 0 ? Peak / level : 1;
        return [.. hall.Select(sample => (float)(Peak * Math.Tanh(sample * makeup / Peak)))];
    }

    /// <summary>
    /// A compressor: an envelope that follows the level (5 ms up, 120 ms down) and, above the threshold, the gain that
    /// brings it down by the ratio; then the peak back to 1.
    /// </summary>
    private void Compress(double[] signal)
    {
        var attack = Math.Exp(-1.0 / (0.005 * Rate));
        var release = Math.Exp(-1.0 / (0.120 * Rate));
        var envelope = 0.0;
        var peak = 0.0;
        for (var i = 0; i < signal.Length; i++)
        {
            var level = Math.Abs(signal[i]);
            envelope = level > envelope ? (attack * envelope) + ((1 - attack) * level) : (release * envelope) + ((1 - release) * level);
            if (envelope > Threshold)
            {
                signal[i] *= Math.Pow(envelope / Threshold, (1 / Ratio) - 1);
            }

            peak = Math.Max(peak, Math.Abs(signal[i]));
        }

        if (peak > 0)
        {
            for (var i = 0; i < signal.Length; i++)
            {
                signal[i] /= peak;
            }
        }
    }

    /// <summary>Linear interpolation from one rate to another; the same rate copies.</summary>
    public static float[] Resample(ReadOnlySpan<float> input, int from, int to)
    {
        if (from == to || input.Length == 0)
        {
            return input.ToArray();
        }

        var output = new float[(int)((long)input.Length * to / from)];
        for (var i = 0; i < output.Length; i++)
        {
            var position = (double)i * from / to;
            var index = (int)position;
            var next = Math.Min(index + 1, input.Length - 1);
            var fraction = (float)(position - index);
            output[i] = (input[index] * (1 - fraction)) + (input[next] * fraction);
        }

        return output;
    }

    private static void Normalize(Span<float> samples, double peak)
    {
        var max = 0f;
        foreach (var sample in samples)
        {
            max = Math.Max(max, Math.Abs(sample));
        }

        if (max > 0)
        {
            var gain = (float)(peak / max);
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }
    }

    private static void Filter(double[] signal, params Biquad[] filters)
    {
        foreach (var filter in filters)
        {
            filter.Run(signal);
        }
    }

    /// <summary>A second-order filter, after Robert Bristow-Johnson's Audio EQ Cookbook.</summary>
    private sealed record Biquad(double B0, double B1, double B2, double A1, double A2)
    {
        public static Biquad LowPass(double frequency, int rate) => Pass(frequency, rate, high: false);

        public static Biquad HighPass(double frequency, int rate) => Pass(frequency, rate, high: true);

        public static Biquad Peak(double frequency, double gainDb, double q, int rate)
        {
            var a = Math.Pow(10, gainDb / 40);
            var w = 2 * Math.PI * frequency / rate;
            var alpha = Math.Sin(w) / (2 * q);
            var a0 = 1 + (alpha / a);
            return new Biquad((1 + (alpha * a)) / a0, -2 * Math.Cos(w) / a0, (1 - (alpha * a)) / a0, -2 * Math.Cos(w) / a0, (1 - (alpha / a)) / a0);
        }

        private static Biquad Pass(double frequency, int rate, bool high)
        {
            var w = 2 * Math.PI * frequency / rate;
            var alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
            var cos = Math.Cos(w);
            var a0 = 1 + alpha;
            var b1 = high ? -(1 + cos) : 1 - cos;
            var b0 = high ? (1 + cos) / 2 : (1 - cos) / 2;
            return new Biquad(b0 / a0, b1 / a0, b0 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }

        public void Run(double[] signal)
        {
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (var i = 0; i < signal.Length; i++)
            {
                var x = signal[i];
                var y = (B0 * x) + (B1 * x1) + (B2 * x2) - (A1 * y1) - (A2 * y2);
                x2 = x1;
                x1 = x;
                y2 = y1;
                y1 = y;
                signal[i] = y;
            }
        }
    }
}
