// Names, descriptions and professions of staff from the original's strings, ported from CorsixTH
// CorsixTH/Lua/staff_profile.lua (StaffProfile:randomiseOrganical, getFullName), CorsixTH/Lua/world.lua
// (World:updateInitialsCache, World:localiseInitial) and the string mapping of
// CorsixTH/Lua/languages/original_strings.lua (staff_class, humanoid_name_starts and _ends, staff_descriptions).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Text;
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

    private readonly StringTable _strings;
    private readonly string[] _initials;

    public StaffTexts(StringTable strings)
    {
        _strings = strings;
        _initials = Initials(strings);
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
        return Pick(_strings, NameStartsSection, int.MaxValue, random) + Pick(_strings, NameEndsSection, int.MaxValue, random);
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
        Pick(_strings, NameStartsSection, int.MaxValue, random);
        Pick(_strings, NameEndsSection, int.MaxValue, random);

        var first = Pick(_strings, profile.Skill < GoodSkill ? BadDescriptionsSection : GoodDescriptionsSection, GoodOrBadDescriptionCount, random);
        var second = Pick(_strings, MiscDescriptionsSection, MiscDescriptionCount, random);
        if (Encoding.UTF8.GetByteCount(first) + Encoding.UTF8.GetByteCount(second) > MaxDescriptionLength)
        {
            return first;
        }

        return random.Next(2) == 0 ? second + first : first + second;
    }

    /// <summary>The name of a staff class (<c>staff_class</c>, the profession CorsixTH shows for staff).</summary>
    public string Profession(StaffClass staffClass) => _strings.Get(StaffClassSection, (int)staffClass) ?? staffClass.ToString();

    /// <summary>
    /// <c>World:updateInitialsCache</c>: the characters of all name starts and ends joined, less the first 32 bytes.
    /// </summary>
    private static string[] Initials(StringTable strings)
    {
        var joined = new StringBuilder();
        foreach (var section in (ReadOnlySpan<int>)[NameStartsSection, NameEndsSection])
        {
            for (var i = 0; i < strings.SectionSize(section); i++)
            {
                joined.Append(strings[section, i]);
            }
        }

        var bytes = Encoding.UTF8.GetBytes(joined.ToString());
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

    private static string Pick(StringTable strings, int section, int limit, SimRandom random)
    {
        var count = Math.Min(strings.SectionSize(section), limit);
        return count > 0 ? strings[section, random.Next(count)] : string.Empty;
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
