// Names, descriptions and professions of staff from the original's strings, ported from CorsixTH
// CorsixTH/Lua/staff_profile.lua (StaffProfile:randomiseOrganical, getFullName), CorsixTH/Lua/world.lua
// (World:updateInitialsCache, World:localiseInitial) and the string mapping of
// CorsixTH/Lua/languages/original_strings.lua (staff_class, humanoid_name_starts and _ends, staff_descriptions).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Text;
using CorsixNet.Content.Languages;
using CorsixNet.Formats;
using CorsixNet.Sim;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Content;

/// <summary>
/// The texts of a member of staff in a language: initial, name, description and profession.
/// </summary>
/// <remarks>
/// CorsixTH picks the name and description at random when it makes a profile and keeps the text. Here the
/// simulation keeps no text, so the picks are made from the profile's fixed random values (<see
/// cref="StaffProfile.NameSeed"/>, attention to detail and looks): the same member of staff always gets the same
/// name and description, on every player's screen, in whichever language is loaded.
/// </remarks>
public sealed class StaffTexts
{
    // Sections of the original strings (CorsixTH's S[n] is section n - 1).
    private const int StaffClassSection = 0;
    private const int NameStartsSection = 8;
    private const int NameEndsSection = 9;
    private const int MiscDescriptionsSection = 45;
    private const int GoodDescriptionsSection = 46;
    private const int BadDescriptionsSection = 47;

    // CorsixTH removes the last string of each description list, a ".": misc[38], good[16] and bad[16].
    private const int MiscDescriptionCount = 37;
    private const int GoodOrBadDescriptionCount = 15;

    /// <summary>A description of two parts is shortened to one when longer than this, in UTF-8 bytes.</summary>
    private const int MaxDescriptionLength = 96;

    /// <summary>Below this skill (CorsixTH's 0.55) the description is a bad one.</summary>
    private const int GoodSkill = 550;

    private readonly IReadOnlyList<string> _nameStarts;
    private readonly IReadOnlyList<string> _nameEnds;
    private readonly IReadOnlyList<string> _misc;
    private readonly IReadOnlyList<string> _good;
    private readonly IReadOnlyList<string> _bad;
    private readonly IReadOnlyList<string> _classes;
    private readonly string[] _initials;

    /// <summary>
    /// The texts from the original's strings, or from CorsixTH's translation where it has its own lists
    /// (<c>humanoid_name_starts</c>, <c>humanoid_name_ends</c>, <c>staff_descriptions</c>, <c>staff_class</c>).
    /// </summary>
    public StaffTexts(StringTable strings, LanguageStrings? language = null)
    {
        _nameStarts = Choose(language, "humanoid_name_starts", strings, NameStartsSection, int.MaxValue);
        _nameEnds = Choose(language, "humanoid_name_ends", strings, NameEndsSection, int.MaxValue);
        _misc = Choose(language, "staff_descriptions.misc", strings, MiscDescriptionsSection, MiscDescriptionCount);
        _good = Choose(language, "staff_descriptions.good", strings, GoodDescriptionsSection, GoodOrBadDescriptionCount);
        _bad = Choose(language, "staff_descriptions.bad", strings, BadDescriptionsSection, GoodOrBadDescriptionCount);
        _classes =
        [
            .. new[] { "nurse", "doctor", "handyman", "receptionist" }.Select((key, index) =>
                language?.Get("staff_class." + key) is { Length: > 0 } named ? named : strings.Get(StaffClassSection, index) ?? string.Empty),
        ];
        _initials = Initials([.. _nameStarts, .. _nameEnds]);
    }

    /// <summary>The initial (CorsixTH's <c>initial</c>, from <c>name_seed</c>).</summary>
    public string Initial(StaffProfile profile)
    {
        if (_initials.Length == 0)
        {
            return "?";
        }

        // CorsixTH: initials[name_seed % #initials], where initials[0] is the last one.
        var number = profile.NameSeed % _initials.Length;
        return number == 0 ? _initials[^1] : _initials[number - 1];
    }

    /// <summary>The surname: a random start and a random end.</summary>
    public string Name(StaffProfile profile)
    {
        var random = RandomFor(profile);
        return Pick(_nameStarts, random) + Pick(_nameEnds, random);
    }

    /// <summary>The initial and the surname (<c>getFullName</c>).</summary>
    public string FullName(StaffProfile profile) => $"{Initial(profile)}. {Name(profile)}";

    /// <summary>
    /// A good (skill at least 0.55) or bad description and a miscellaneous one, in random order; only the first when
    /// the two together are longer than 96 bytes.
    /// </summary>
    public string Description(StaffProfile profile)
    {
        var random = RandomFor(profile);

        // The name's two picks come first, as in randomiseOrganical.
        Pick(_nameStarts, random);
        Pick(_nameEnds, random);

        var first = Pick(profile.Skill < GoodSkill ? _bad : _good, random);
        var second = Pick(_misc, random);
        if (Encoding.UTF8.GetByteCount(first) + Encoding.UTF8.GetByteCount(second) > MaxDescriptionLength)
        {
            return first;
        }

        return random.Next(2) == 0 ? second + first : first + second;
    }

    /// <summary>The name of a staff class (<c>staff_class</c>, the profession CorsixTH shows for staff).</summary>
    public string Profession(StaffClass staffClass) =>
        (int)staffClass < _classes.Count && _classes[(int)staffClass] is { Length: > 0 } name ? name : staffClass.ToString();

    /// <summary>
    /// <c>World:updateInitialsCache</c>: the characters of all name starts and ends joined, less the first 32 bytes.
    /// </summary>
    private static string[] Initials(IEnumerable<string> names)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(names));
        var initials = new List<string>();
        var position = Math.Min(32, bytes.Length);
        while (position < bytes.Length)
        {
            // A character starts with an ASCII byte or a lead byte; stray continuation bytes are skipped.
            var start = position++;
            if (bytes[start] is >= 0x80 and < 0xC2 or > 0xF4)
            {
                continue;
            }

            while (position < bytes.Length && bytes[position] is >= 0x80 and <= 0xBF)
            {
                position++;
            }

            initials.Add(Encoding.UTF8.GetString(bytes, start, position - start));
        }

        return [.. initials];
    }

    private static string Pick(IReadOnlyList<string> strings, SimRandom random) =>
        strings.Count > 0 ? strings[random.Next(strings.Count)] : string.Empty;

    /// <summary>The translation's list by name when it has one, else the first <paramref name="limit"/> strings of a section.</summary>
    private static IReadOnlyList<string> Choose(LanguageStrings? language, string name, StringTable strings, int section, int limit)
    {
        if (language?.List(name) is { Count: > 0 } translated)
        {
            return translated.Count > limit ? [.. translated.Take(limit)] : translated;
        }

        return [.. Enumerable.Range(0, Math.Min(strings.SectionSize(section), limit)).Select(index => strings[section, index])];
    }

    private static SimRandom RandomFor(StaffProfile profile)
    {
        var seed = ((ulong)(uint)profile.NameSeed << 32)
            | ((ulong)(uint)profile.AttentionToDetail << 12)
            | ((ulong)(uint)profile.HairIndex << 7)
            | ((ulong)(uint)profile.FaceIndex << 2)
            | (uint)profile.Class;
        return new SimRandom(seed, (ulong)(uint)profile.ChinIndex);
    }
}
