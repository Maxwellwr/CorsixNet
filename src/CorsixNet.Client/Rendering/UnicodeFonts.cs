// Finding the Unicode font, ported from CorsixTH CorsixTH/Lua/graphics.lua (Graphics:loadFontFile).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Client.Rendering;

/// <summary>
/// The TrueType fonts languages the game's bitmap font cannot draw are drawn with (CorsixTH's <c>unicode_font</c>).
/// </summary>
/// <remarks>
/// CorsixTH looks at the configured file, the one named at compile time, then <c>CorsixTHUnicode.ttf</c> and
/// <c>GoNotoKurrent-Regular.ttf</c> in its own folder, and uses the first found for every language. This does the same
/// with the game's folder, then, as CorsixNet bundles no font, falls back on fonts the system has. Of those, the first
/// that has every character of the language is taken, so that Japanese or Korean find a font of their own. The found
/// file is not written back to the settings as CorsixTH does, so that a system font is looked for again each time.
/// </remarks>
internal sealed class UnicodeFonts : IDisposable
{
    /// <summary>The files CorsixTH looks for beside itself.</summary>
    public static readonly string[] BundledNames = ["CorsixTHUnicode.ttf", "GoNotoKurrent-Regular.ttf"];

    private readonly Dictionary<string, TrueTypeFace?> _faces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, bool> _exists;
    private readonly Func<string, byte[]> _read;

    public UnicodeFonts(string? configured, string folder, IEnumerable<string>? systemFonts = null, Func<string, bool>? exists = null, Func<string, byte[]>? read = null)
    {
        _exists = exists ?? File.Exists;
        _read = read ?? File.ReadAllBytes;
        Configured = configured;
        List<string> preferred = [];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            preferred.Add(configured);
        }

        preferred.AddRange(BundledNames.Select(name => Path.Combine(folder, name)));
        Preferred = preferred;
        SystemFonts = [.. systemFonts ?? DefaultSystemFonts()];
    }

    /// <summary>The font file the settings name (<c>unicode_font</c>), if any.</summary>
    public string? Configured { get; }

    /// <summary>The configured font and the ones beside the game, taken for any language when found.</summary>
    public IReadOnlyList<string> Preferred { get; }

    /// <summary>The system's fonts, taken for a language when one of them has all its characters.</summary>
    public IReadOnlyList<string> SystemFonts { get; }

    /// <summary>
    /// The face to draw the text's language with: the first of <see cref="Preferred"/> there is, else the first system
    /// font with all the text's characters, else the system font missing the fewest; null if there is no font at all.
    /// </summary>
    public (string Path, TrueTypeFace Face)? Find(string sample)
    {
        foreach (var path in Preferred)
        {
            if (Face(path) is { } face)
            {
                return (path, face);
            }
        }

        (string Path, TrueTypeFace Face, int Missing)? best = null;
        foreach (var path in SystemFonts)
        {
            if (Face(path) is not { } face)
            {
                continue;
            }

            var missing = face.MissingGlyphs(sample);
            if (missing == 0)
            {
                return (path, face);
            }

            if (best is null || missing < best.Value.Missing)
            {
                best = (path, face, missing);
            }
        }

        return best is { } found ? (found.Path, found.Face) : null;
    }

    /// <summary>Whether a font found for the text has all its characters.</summary>
    public bool CanDraw(string sample) => Find(sample) is { } found && found.Face.MissingGlyphs(sample) == 0;

    public void Dispose()
    {
        foreach (var face in _faces.Values)
        {
            face?.Dispose();
        }

        _faces.Clear();
    }

    /// <summary>Fonts with wide coverage that come with Windows, macOS and common Linux distributions.</summary>
    public static IEnumerable<string> DefaultSystemFonts()
    {
        if (OperatingSystem.IsWindows())
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (string.IsNullOrEmpty(fonts))
            {
                fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            }

            // Latin, Greek and Cyrillic first, Verdana and Tahoma before the rest as their wide letters, tall small
            // letters and hinting for the screen come nearest the game's own fonts; then Chinese (simplified,
            // traditional), Japanese and Korean.
            return new[] { "tahoma.ttf", "verdana.ttf", "segoeui.ttf", "arial.ttf", "msyh.ttc", "msjh.ttc", "YuGothR.ttc", "meiryo.ttc", "msgothic.ttc", "malgun.ttf", "simsun.ttc", "gulim.ttc" }
                .Select(name => Path.Combine(fonts, name));
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                "/System/Library/Fonts/Supplemental/Verdana.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf",
                "/System/Library/Fonts/Helvetica.ttc",
                "/System/Library/Fonts/PingFang.ttc",
                "/System/Library/Fonts/Hiragino Sans GB.ttc",
                "/System/Library/Fonts/AppleSDGothicNeo.ttc",
                "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
                "/Library/Fonts/Arial Unicode.ttf",
            ];
        }

        return
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
            "/usr/share/fonts/noto/NotoSans-Regular.ttf",
            "/usr/share/fonts/truetype/freefont/FreeSans.ttf",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc",
            "/usr/share/fonts/truetype/droid/DroidSansFallbackFull.ttf",
        ];
    }

    private TrueTypeFace? Face(string path)
    {
        if (_faces.TryGetValue(path, out var face))
        {
            return face;
        }

        try
        {
            face = _exists(path) ? TrueTypeFace.Load(_read(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            face = null;
        }

        _faces[path] = face;
        return face;
    }
}
