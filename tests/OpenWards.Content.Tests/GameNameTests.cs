using OpenWards.Content.Languages;

namespace OpenWards.Content.Tests;

public class GameNameTests
{
    [Theory]
    [InlineData("You are about to quit from CorsixTH. Are you sure?", "You are about to quit from OpenWards. Are you sure?")]
    [InlineData("--- CorsixTH Setup ---", "--- OpenWards Setup ---")]
    [InlineData("Das CorsixTH-Team sucht Verstärkung!", "Das OpenWards-Team sucht Verstärkung!")]
    [InlineData("より新しいバージョンのCorsixTHからセーブデータ", "より新しいバージョンのOpenWardsからセーブデータ")]
    [InlineData("Tervetuloa pelaamaan CorsixTH:ta.", "Tervetuloa pelaamaan OpenWardsia.")]
    [InlineData("vaatii CorsixTH:n käynnistämisen", "vaatii OpenWardsin käynnistämisen")]
    [InlineData("CorsixTH-tiimi etsii", "OpenWards-tiimi etsii")]
    [InlineData("Bienvenido a CorsixTH.", "Bienvenido a OpenWards.")]
    [InlineData("Links are on our website (CorsixTH.com).", "Links are on our website (CorsixTH.com).")]
    [InlineData("github.com/CorsixTH/CorsixTH/issues", "github.com/CorsixTH/CorsixTH/issues")]
    [InlineData("th-issues.corsix.org (#Corsix-TH, corsix-th)", "th-issues.corsix.org (#Corsix-TH, corsix-th)")]
    [InlineData("Nothing to change", "Nothing to change")]
    public void NamesOpenWardsWhereCorsixTHIsTheGame(string text, string expected) =>
        Assert.Equal(expected, GameName.Rebrand(text));

    [Theory]
    [InlineData("Üdvözöllek a CorsixTH-ban.", "Üdvözöllek az OpenWardsban.")]
    [InlineData("A CorsixTH csapatnak", "Az OpenWards csapatnak")]
    [InlineData("elindítod a CorsixTH-t", "elindítod az OpenWardsot")]
    [InlineData("CorsixTH-nak a futáshoz", "OpenWardsnak a futáshoz")]
    public void PutsHungarianEndingsAndArticleOnOpenWards(string text, string expected) =>
        Assert.Equal(expected, GameName.Rebrand(text, "Hungarian"));

    [Fact]
    public void BundledTranslationsNameOpenWards()
    {
        var languages = CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages"));
        foreach (var language in languages.Languages)
        {
            var strings = languages.Load(language.EnglishName, _ => null).Strings;
            var stillCorsix = strings.Where(entry => GameName.Rebrand(entry.Value, language.EnglishName) != entry.Value).Select(entry => entry.Key);
            Assert.True(!stillCorsix.Any(), $"{language.EnglishName}: {string.Join(", ", stillCorsix)}");
        }

        Assert.Equal(
            "You are about to quit from OpenWards. Are you sure this is what you want to do?",
            languages.Load("English", _ => null).Get("tooltip.main_menu.quit"));
    }
}
