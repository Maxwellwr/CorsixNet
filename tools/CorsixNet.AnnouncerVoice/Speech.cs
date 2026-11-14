using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CorsixNet.AnnouncerVoice;

/// <summary>
/// The text-to-speech engine, run through Python: Piper (<c>pip install piper-tts</c>) for a voice given as its
/// <c>.onnx</c> file, Vosk TTS (<c>pip install vosk-tts</c>) for a voice given as its model folder and a speaker.
/// </summary>
internal sealed class Speech(string python, string voice, int speaker, double lengthScale)
{
    private const string VoskScript = """
        import json, sys
        from vosk_tts import Model, Synth
        model_path, speaker, length_scale, jobs = sys.argv[1], int(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
        synth = Synth(Model(model_path=model_path))
        for job in json.load(open(jobs, encoding="utf-8")):
            synth.synth(job["text"], job["file"], speaker_id=speaker, speech_rate=1.0 / length_scale)
        """;

    /// <summary>Whether the voice is a Vosk TTS model folder rather than a Piper voice file.</summary>
    public bool IsVosk => Directory.Exists(voice);

    /// <summary>The voice's name, as voice.yaml keeps it: the file or folder name, and the speaker for Vosk.</summary>
    public string Name => IsVosk
        ? FormattableString.Invariant($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(voice))} speaker {speaker}")
        : Path.GetFileNameWithoutExtension(voice);

    /// <summary>The engine's settings, as voice.yaml keeps them.</summary>
    public string Settings => FormattableString.Invariant($"{(IsVosk ? "vosk-tts" : "piper")}, length scale {lengthScale}");

    /// <summary>Speaks each text, as 16-bit PCM WAV files by the same keys.</summary>
    public Dictionary<string, byte[]> SpeakAll(IReadOnlyDictionary<string, string> texts)
    {
        var folder = Directory.CreateTempSubdirectory("corsixnet-voice-").FullName;
        try
        {
            var files = texts.ToDictionary(pair => pair.Key, pair => Path.Combine(folder, pair.Key + ".wav"));
            if (texts.Count == 0)
            {
                return [];
            }

            if (IsVosk)
            {
                // One process for them all: the model takes long to load.
                var jobs = Path.Combine(folder, "jobs.json");
                File.WriteAllText(jobs, JsonSerializer.Serialize(texts.Select(pair => new { text = pair.Value, file = files[pair.Key] })), new UTF8Encoding(false));
                var script = Path.Combine(folder, "speak.py");
                File.WriteAllText(script, VoskScript);
                Run([script, voice, speaker.ToString(CultureInfo.InvariantCulture), lengthScale.ToString(CultureInfo.InvariantCulture), jobs], null);
            }
            else
            {
                foreach (var (key, text) in texts)
                {
                    Run(["-m", "piper", "-m", voice, "-f", files[key], "--length-scale", lengthScale.ToString(CultureInfo.InvariantCulture)], text);
                }
            }

            return files.ToDictionary(pair => pair.Key, pair => File.Exists(pair.Value)
                ? File.ReadAllBytes(pair.Value)
                : throw new InvalidOperationException($"No speech was written for {pair.Key}."));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private void Run(IEnumerable<string> arguments, string? input)
    {
        var start = new ProcessStartInfo(python)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Python reads its input in the console's code page unless told to use UTF-8.
        start.Environment["PYTHONUTF8"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{python} could not be started.");
        var errors = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            process.StandardInput.WriteLine(input);
        }

        process.StandardInput.Close();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Speech failed ({process.ExitCode}): {errors.Result}");
        }
    }
}
