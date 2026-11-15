using YamlDotNet.Serialization;

namespace CorsixNet.AnnouncerVoice;

/// <summary>
/// speech.yaml beside the sounds: by subtitle name, what to say instead of the subtitle, where the voice reads it badly
/// (a slip of grammar, a stress in the wrong place, an abbreviation).
/// </summary>
internal static class SpeechOverrides
{
    public const string FileName = "speech.yaml";

    public static IReadOnlyDictionary<string, string> Load(string path) =>
        File.Exists(path)
            ? new DeserializerBuilder().Build().Deserialize<Dictionary<string, string>?>(File.ReadAllText(path)) ?? []
            : new Dictionary<string, string>();
}
