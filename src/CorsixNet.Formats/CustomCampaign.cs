// What a campaign file holds, ported from CorsixTH CorsixTH/Lua/app.lua (App:readCampaignFile, App:loadCampaign),
// CorsixTH/Lua/strings.lua (Strings:getLocalisedText) and CorsixTH/Campaigns/example.campaign.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Formats;

/// <summary>
/// A CorsixTH campaign (<c>.campaign</c>): a name, a description, the level files to play in order, the text shown on
/// winning the last one and whether the level advance movies play.
/// </summary>
/// <remarks>
/// CorsixTH runs the file as Lua. Campaign files only assign strings (joined with <c>..</c>), numbers, booleans and
/// tables of them to names and table fields, so this reads that much of Lua and refuses anything else.
/// </remarks>
public sealed record CustomCampaign(string? Name, string? Description, IReadOnlyList<string> Levels, string? WinningText, bool Movie)
{
    /// <summary>
    /// Reads a campaign file, with the description and winning text in <paramref name="language"/> where the file gives
    /// them (<c>description_table</c>, <c>winning_text_table</c>), else in English, else the plain ones.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a campaign file of the kind CorsixTH ships.</exception>
    public static CustomCampaign Parse(string text, string language = "en")
    {
        var globals = LuaAssignments.Parse(text);
        string? Localised(string plain, string table)
        {
            var value = globals.GetValueOrDefault(plain) as string;
            if (globals.GetValueOrDefault(table) is not LuaTable localised)
            {
                return value;
            }

            return localised.Fields.GetValueOrDefault(language) as string ?? localised.Fields.GetValueOrDefault("en") as string ?? value;
        }

        var levels = globals.GetValueOrDefault("levels") is LuaTable table ? table.Items.OfType<string>().ToList() : [];
        return new CustomCampaign(
            globals.GetValueOrDefault("name") as string,
            Localised("description", "description_table"),
            levels,
            Localised("winning_text", "winning_text_table"),
            globals.GetValueOrDefault("movie") is true);
    }
}
