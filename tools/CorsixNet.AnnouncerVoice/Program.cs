// Speaks a translation's announcer subtitles (subtitles.* of Languages/<language>.yaml) with a Piper voice, through a
// hospital loudspeaker, into Languages/Voice/<language>, where the game hears them instead of the original's.
//
//   dotnet run --project tools/CorsixNet.AnnouncerVoice -- --voice <voice.onnx> [--language Russian] [--python python]
//       [--languages <folder>] [--length-scale 1.0] [--only emerg006,rand001] [--force]
//
// Piper (pip install piper-tts) and the voice (python -m piper.download_voices ru_RU-denis-medium) are needed. Only the
// sounds whose text, voice or settings changed since voice.yaml was written are spoken again (--force: all of them;
// --only: just those named). A speech.yaml beside them may give, by subtitle name, what to say instead of the subtitle
// (to help pronunciation).

using System.Diagnostics;
using System.Globalization;
using System.Text;
using CorsixNet.AnnouncerVoice;
using CorsixNet.Content.Languages;

const string Usage = """
    usage: CorsixNet.AnnouncerVoice --voice <voice.onnx> [--language <name>] [--python <python>] [--languages <folder>]
                                    [--length-scale <n>] [--only <name,...>] [--force]
    """;

var options = new Dictionary<string, string>(StringComparer.Ordinal);
var force = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--force")
    {
        force = true;
    }
    else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        options[args[i][2..]] = args[++i];
    }
    else
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

if (!options.TryGetValue("voice", out var voice) || !File.Exists(voice))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var language = options.GetValueOrDefault("language", "Russian");
var python = options.GetValueOrDefault("python", "python");
var lengthScale = double.Parse(options.GetValueOrDefault("length-scale", "1.0"), CultureInfo.InvariantCulture);
var only = options.TryGetValue("only", out var list)
    ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
    : null;
var languagesFolder = options.GetValueOrDefault("languages") ?? Path.Combine(RepositoryRoot(), "src", "CorsixNet.Content", "Languages");

var languages = CorsixLanguages.Load(languagesFolder);
if (languages.Find(language) is not { } found || languages.VoiceDirectory(languagesFolder, language) is not { } output)
{
    Console.Error.WriteLine($"No language file declares {language}.");
    return 1;
}

Directory.CreateDirectory(output);
var lines = AnnouncerLines.Of(languages, found.EnglishName, SpeechOverrides.Load(Path.Combine(output, SpeechOverrides.FileName)));
var loudspeaker = new Loudspeaker();
var manifest = new VoiceManifest
{
    Voice = Path.GetFileNameWithoutExtension(voice),
    Piper = FormattableString.Invariant($"length scale {lengthScale}"),
    Effect = loudspeaker.Describe(),
};
var manifestPath = Path.Combine(output, VoiceManifest.FileName);
var previous = VoiceManifest.Load(manifestPath);
var sameSettings = previous is not null && previous.Voice == manifest.Voice && previous.Piper == manifest.Piper && previous.Effect == manifest.Effect;

// Sounds whose subtitle went: their files go too.
foreach (var gone in previous?.Files.Keys.Where(name => !lines.ContainsKey(name)) ?? [])
{
    File.Delete(Path.Combine(output, AnnouncerLines.SoundFile(gone)));
}

var spoken = 0;
foreach (var (name, text) in lines)
{
    var file = Path.Combine(output, AnnouncerLines.SoundFile(name));
    var unchanged = sameSettings && File.Exists(file) && previous!.Files.GetValueOrDefault(name) == text;
    if (only is not null ? !only.Contains(name) : unchanged && !force)
    {
        // Left as it was (with --only, one not spoken yet stays unspoken).
        if (File.Exists(file) && previous?.Files.GetValueOrDefault(name) is { } said)
        {
            manifest.Files[name] = said;
        }

        continue;
    }

    var (samples, rate) = Wav.Read(Speak(python, voice, lengthScale, text));
    var random = new Random(Seed(name));
    var processed = loudspeaker.Apply(samples, rate, () => (random.NextDouble() * 2) - 1);
    File.WriteAllBytes(file, Wav.Write8Bit(processed, loudspeaker.Rate, () => (float)(random.NextDouble() - random.NextDouble())));
    manifest.Files[name] = text;
    spoken++;
    Console.WriteLine($"{AnnouncerLines.SoundFile(name)}: {text}");
}

manifest.Save(manifestPath, found.EnglishName);
Console.WriteLine($"{spoken} of {lines.Count} announcements spoken into {output}.");
return 0;

static byte[] Speak(string python, string voice, double lengthScale, string text)
{
    var temporary = Path.Combine(Path.GetTempPath(), $"corsixnet-voice-{Guid.NewGuid():N}.wav");
    try
    {
        var start = new ProcessStartInfo(python)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-m", "piper", "-m", voice, "-f", temporary, "--length-scale", lengthScale.ToString(CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }

        // Piper reads its text in the console's code page unless Python is told to use UTF-8.
        start.Environment["PYTHONUTF8"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{python} could not be started.");
        var errors = process.StandardError.ReadToEndAsync();
        process.StandardInput.WriteLine(text);
        process.StandardInput.Close();
        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(temporary))
        {
            throw new InvalidOperationException($"Piper failed ({process.ExitCode}): {errors.Result}");
        }

        return File.ReadAllBytes(temporary);
    }
    finally
    {
        File.Delete(temporary);
    }
}

// The same noise for a sound each time it is spoken (string.GetHashCode differs between runs).
static int Seed(string name) => name.Aggregate(17, (hash, c) => unchecked((hash * 31) + c));

static string RepositoryRoot()
{
    for (var folder = new DirectoryInfo(Directory.GetCurrentDirectory()); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "CorsixNet.slnx")))
        {
            return folder.FullName;
        }
    }

    throw new InvalidOperationException("Run this inside the repository, or give --languages.");
}
