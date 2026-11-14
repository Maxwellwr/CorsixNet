using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CorsixNet.AnnouncerVoice;

/// <summary>
/// voice.yaml beside the sounds: the voice and settings they were spoken with, and what each says, so that only what
/// changed is spoken again.
/// </summary>
internal sealed class VoiceManifest
{
    public const string FileName = "voice.yaml";

    public string Voice { get; set; } = string.Empty;

    public string Settings { get; set; } = string.Empty;

    public string Effect { get; set; } = string.Empty;

    public SortedDictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);

    public static VoiceManifest? Load(string path) =>
        File.Exists(path)
            ? new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build().Deserialize<VoiceManifest>(File.ReadAllText(path))
            : null;

    public void Save(string path, string language)
    {
        var yaml = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build().Serialize(this);
        var header = $"# The announcer's speech in {language}, written by tools/CorsixNet.AnnouncerVoice from the translation's subtitles:\n"
            + "# the voice and settings used, and what each sound says. Speak them again with that tool, not by hand.\n";
        File.WriteAllText(path, header + yaml.ReplaceLineEndings("\n"));
    }
}
