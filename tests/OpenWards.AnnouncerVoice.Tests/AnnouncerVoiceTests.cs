using OpenWards.Content.Languages;

namespace OpenWards.AnnouncerVoice.Tests;

public class AnnouncerVoiceTests
{
    private const string EnglishFile = """
        language: [English, English, en]
        strings:
          subtitles:
            emerg006: 'Staff announcement: patients with Invisibility arriving.'
            machwarn: '*Machine alarm sound*.'
            rand001: White courtesy phone.
        """;

    private const string RussianFile = """
        language: [Русский, Russian, ru]
        steps:
          - inherit: English
        strings:
          subtitles:
            emerg006: 'Объявление для персонала: прибывают пациенты с Невидимостью.'
            machwarn: '*Сигнал тревоги аппарата*.'
            vip001: Внимание, у нас в здании VIP-персона.
        """;

    [Fact]
    public void SpeaksTheLanguagesOwnSubtitlesWithItsOverrides()
    {
        var languages = new CorsixLanguages([("english.yaml", EnglishFile), ("russian.yaml", RussianFile)]);

        var lines = AnnouncerLines.Of(languages, "Russian", new Dictionary<string, string> { ["vip001"] = "Внимание, у нас в здании вип-персона." });

        // rand001 is English's, inherited untranslated; machwarn only describes a sound.
        Assert.Equal(["emerg006", "vip001"], lines.Keys);
        Assert.Equal("Внимание, у нас в здании вип-персона.", lines["vip001"]);
        Assert.Equal("EMERG006.WAV", AnnouncerLines.SoundFile("emerg006"));
    }

    [Theory]
    [InlineData("*Machine alarm sound*.", true)]
    [InlineData(" *Сигнал тревоги аппарата* ", true)]
    [InlineData("Внимание!", false)]
    [InlineData("*", false)]
    public void TellsASoundsDescriptionFromSpeech(string text, bool soundOnly) =>
        Assert.Equal(soundOnly, AnnouncerLines.IsSoundOnly(text));

    [Fact]
    public void WritesTheOriginalsFormatAndReadsPipersBack()
    {
        var wav = Wav.Write8Bit([0f, 1f, -1f, 0.5f, 2f], 22050, () => 0f);

        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(22050, BitConverter.ToInt32(wav, 24));
        Assert.Equal(8, BitConverter.ToInt16(wav, 34));
        Assert.Equal(5, BitConverter.ToInt32(wav, 40));
        Assert.Equal(new byte[] { 128, 255, 1, 192, 255 }, wav[44..49]);

        var (samples, rate) = Wav.Read(Pcm16([0, 16384, -32768], 16000));
        Assert.Equal(16000, rate);
        Assert.Equal([0f, 0.5f, -1f], samples);
        Assert.Throws<InvalidDataException>(() => Wav.Read(wav));
    }

    [Fact]
    public void TheLoudspeakerKeepsTheVoicesBandAndDropsTheRest()
    {
        var speaker = new Loudspeaker { Hiss = 0, Echoes = [], LeadIn = 0, Tail = 0 };

        var speech = Level(speaker.Apply(Sine(1000, 22050, 1.0), 22050, () => 0), speaker.Rate);
        var rumble = Level(speaker.Apply(Mix(Sine(1000, 22050, 1.0), Sine(60, 22050, 1.0, 4)), 22050, () => 0), speaker.Rate);
        var hiss = Level(speaker.Apply(Mix(Sine(1000, 22050, 1.0), Sine(9000, 22050, 1.0, 4)), 22050, () => 0), speaker.Rate);

        // Loud rumble and hiss beside the voice hardly take its loudness away once the band drops them.
        Assert.InRange(rumble / speech, 0.8, 1.25);
        Assert.InRange(hiss / speech, 0.8, 1.25);
    }

    [Fact]
    public void TheLoudspeakerAddsTheHallAndLineAtTheOriginalsRate()
    {
        var speaker = new Loudspeaker();
        var random = new Random(1);

        var output = speaker.Apply(Sine(500, 16000, 0.5), 16000, () => (random.NextDouble() * 2) - 1);

        Assert.Equal((int)(0.08 * 22050) + (int)(0.5 * 22050) + (int)(0.4 * 22050), output.Length);
        Assert.All(output, sample => Assert.InRange(sample, -0.9f, 0.9f));

        // The echo and the hiss go on after the voice stops; the very end fades to nothing.
        var afterVoice = output.AsSpan((int)(0.6 * 22050), (int)(0.1 * 22050)).ToArray();
        Assert.True(afterVoice.Max(Math.Abs) > 0.01f);
        Assert.Equal(0f, output[^1]);
    }

    [Fact]
    public void TheRepositorysVoiceSpeaksTheCurrentSubtitles()
    {
        // voice.yaml must say what the subtitles and speech.yaml say now: when it does not, run the tool again.
        var directory = Path.Combine(AppContext.BaseDirectory, "Languages");
        var languages = CorsixLanguages.Load(directory);
        var folder = languages.VoiceDirectory(directory, "Russian")!;
        var lines = AnnouncerLines.Of(languages, "Russian", SpeechOverrides.Load(Path.Combine(folder, SpeechOverrides.FileName)));
        var manifest = VoiceManifest.Load(Path.Combine(folder, VoiceManifest.FileName))!;

        Assert.Equal(lines, manifest.Files);
        Assert.All(lines.Keys, name => Assert.True(File.Exists(Path.Combine(folder, AnnouncerLines.SoundFile(name))), name));
        Assert.Equal(new Loudspeaker().Describe(), manifest.Effect);
    }

    private static float[] Sine(double frequency, int rate, double seconds, double amplitude = 0.5) =>
        [.. Enumerable.Range(0, (int)(rate * seconds)).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate)))];

    private static float[] Mix(float[] a, float[] b) => [.. a.Zip(b, (x, y) => (x + y) / 5)];

    /// <summary>The level of a 1 kHz tone in the middle of a signal (its correlation with one).</summary>
    private static double Level(float[] signal, int rate)
    {
        double sin = 0, cos = 0;
        for (var i = signal.Length / 4; i < signal.Length * 3 / 4; i++)
        {
            sin += signal[i] * Math.Sin(2 * Math.PI * 1000 * i / rate);
            cos += signal[i] * Math.Cos(2 * Math.PI * 1000 * i / rate);
        }

        return Math.Sqrt((sin * sin) + (cos * cos));
    }

    private static byte[] Pcm16(short[] samples, int rate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples.Length * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
