using CorsixNet.Content.Audio;
using CorsixNet.Content.Languages;

namespace CorsixNet.Content.Tests;

public class LanguageVoiceTests
{
    [Fact]
    public void FindsTheFolderSoundsByTheOriginalsNamesIgnoringCase()
    {
        var folder = Directory.CreateTempSubdirectory("corsixnet-voice-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "EMERG006.WAV"), [1]);
            File.WriteAllBytes(Path.Combine(folder, "rand001.wav"), [2]);
            File.WriteAllText(Path.Combine(folder, "voice.yaml"), "voice: test");

            var voice = LanguageVoice.Load(folder);

            Assert.Equal(2, voice.Count);
            Assert.Equal(Path.Combine(folder, "EMERG006.WAV"), voice.Find("emerg006.wav"));
            Assert.Equal(Path.Combine(folder, "rand001.wav"), voice.Find("RAND001.WAV"));
            Assert.Null(voice.Find("EMERG007.WAV"));
            Assert.Null(voice.Find("voice.yaml"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AMissingFolderSpeaksNothingOfItsOwn()
    {
        Assert.Equal(0, LanguageVoice.Load(null).Count);
        Assert.Null(LanguageVoice.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Find("EMERG006.WAV"));
    }

    [Fact]
    public void RussianSpeaksEveryAnnouncementBesideTheGame()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Languages");
        var languages = CorsixLanguages.Load(directory);
        var voice = LanguageVoice.Load(languages.VoiceDirectory(directory, "Russian"));

        Assert.Equal(146, voice.Count);
        Assert.NotNull(voice.Find("EMERG006.WAV"));
        Assert.NotNull(voice.Find("RAND046.WAV"));
        Assert.Null(voice.Find("MACHWARN.WAV"));
        Assert.Equal(0, LanguageVoice.Load(languages.VoiceDirectory(directory, "French")).Count);
    }
}
