using System.Text.RegularExpressions;

namespace OpenWards.Content.Languages;

/// <summary>
/// The game's name in CorsixTH's translations: they name CorsixTH where they mean the game the player runs ("You are
/// about to quit from CorsixTH"), and tools/OpenWards.LuaConverter writes them out naming OpenWards instead. Web and
/// IRC addresses (CorsixTH.com, th-issues.corsix.org, github.com/CorsixTH, #corsix-th) stay as they are.
/// </summary>
public static partial class GameName
{
    /// <summary>The game's name as the player sees it.</summary>
    public const string Name = "OpenWards";

    /// <summary>A translated string with CorsixTH as the game's name changed to <see cref="Name"/>.</summary>
    /// <param name="text">The string.</param>
    /// <param name="language">The language's English name: Hungarian's article a becomes az before OpenWards.</param>
    public static string Rebrand(string text, string? language = null)
    {
        if (!text.Contains("Corsix", StringComparison.Ordinal))
        {
            return text;
        }

        var hungarian = string.Equals(language, "Hungarian", StringComparison.OrdinalIgnoreCase);
        return CorsixTH().Replace(text, match =>
        {
            var article = match.Groups["article"];
            var before = !article.Success ? "" : article.Value + (hungarian ? "z " : " ");
            return before + Name + Suffix(match.Groups["suffix"].Value);
        });
    }

    // Finnish and Hungarian put case endings on CorsixTH after a colon or a hyphen (CorsixTH:ta, CorsixTH-ban); on
    // OpenWards they go straight on, with a linking vowel where the language needs one.
    private static string Suffix(string suffix) => suffix switch
    {
        ":ta" => "ia",
        ":n" => "in",
        "-t" => "ot",
        "-nak" => "nak",
        "-ban" => "ban",
        _ => "",
    };

    // CorsixTH or Corsix-TH as a word, not inside an address, after the word a or A if it stands there. Letters are
    // matched as ASCII, so a name written straight after Japanese or Chinese text (バージョンのCorsixTHから) still
    // counts as a word.
    [GeneratedRegex(@"(?:(?<![\p{L}\p{N}])(?<article>[aA]) )?(?<![A-Za-z0-9./#-])Corsix-?TH(?:(?<suffix>:ta|:n|-t|-nak|-ban)(?![A-Za-z0-9\p{L}])|(?![A-Za-z0-9/]|\.com))")]
    private static partial Regex CorsixTH();
}
