// The build room window as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/build_room.lua (UIBuildRoom:
// its panels, categories, list, tooltips, draw, the preview animation, buildRoom).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Rooms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The build room window of Req09V's panels, 297 by 294, centred: the four kinds of room on the left, the rooms of the
/// chosen kind the hospital has discovered on the right (the one under the cursor in blue, with its cost and its preview
/// animation), and close. A room picked starts its blueprint, if the hospital can afford it.
/// </summary>
internal sealed class BuildRoomWindow(Action<RoomDefinition> build) : UiWindow
{
    public static readonly Point Size = new(297, 294);

    /// <summary>Each kind's rooms in CorsixTH's order (room.categories); the psychiatrist and ward are in two kinds.</summary>
    public static readonly string[][] Categories =
    [
        ["gp", "general_diag", "cardiogram", "scanner", "ultrascan", "blood_machine", "x_ray", "psych", "ward"],
        ["psych", "ward", "operating_theatre", "pharmacy"],
        ["inflation", "slack_tongue", "fracture_clinic", "hair_restoration", "electrolysis", "dna_fixer", "jelly_vat", "decontamination"],
        ["staff_room", "research", "toilets", "training"],
    ];

    /// <summary>The preview animation of each room (build_preview_animation).</summary>
    public static readonly IReadOnlyDictionary<string, int> Previews = new Dictionary<string, int>
    {
        ["gp"] = 900,
        ["general_diag"] = 916,
        ["cardiogram"] = 918,
        ["scanner"] = 920,
        ["ultrascan"] = 5068,
        ["blood_machine"] = 5094,
        ["x_ray"] = 5076,
        ["psych"] = 924,
        ["ward"] = 910,
        ["operating_theatre"] = 5080,
        ["pharmacy"] = 5088,
        ["inflation"] = 908,
        ["slack_tongue"] = 932,
        ["fracture_clinic"] = 5072,
        ["hair_restoration"] = 5074,
        ["electrolysis"] = 930,
        ["dna_fixer"] = 5070,
        ["jelly_vat"] = 928,
        ["decontamination"] = 5100,
        ["staff_room"] = 5066,
        ["research"] = 5102,
        ["toilets"] = 5098,
        ["training"] = 5086,
    };

    private static readonly int[] CategoryY = [21, 53, 84, 116];
    private static readonly Rectangle[] CategoryButtons = [new(9, 9, 129, 32), new(9, 41, 129, 31), new(9, 72, 129, 32), new(9, 104, 129, 32)];
    private static readonly Rectangle Close = new(154, 258, 134, 27);

    private int? _category;
    private int _hovered = -1;
    private string? _pressed;

    public override string Title => "Build a room";

    public override bool IsFullscreen(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ09V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null && graphics.Font("FONT02V", OriginalGraphics.MainPalette) is not null;

    public override Point FullscreenSize => Size;

    public override IEnumerable<WindowRow> Rows(IWindowHost host) => [];

    /// <summary>updateBuildableRooms: the rooms of a kind the hospital has discovered and the level has, in order.</summary>
    public static List<RoomDefinition> Rooms(Simulation simulation, int player, int category) =>
        Categories[category].Select(simulation.Content.FindRoom).OfType<RoomDefinition>()
            .Where(room => simulation.Rules.IsRoomAvailable(room) && RoomPurchases.CanBuild(simulation, room, player)).ToList();

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ09V", OriginalGraphics.MainPalette)!;
        var white = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var blue = graphics.Font("FONT02V", OriginalGraphics.MainPalette)!;
        var simulation = host.Simulation;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);

        int[] categoryPanels = [210, 212, 214, 216];
        int[] categoryPanelY = [0, 41, 72, 104];
        for (var i = 0; i < 4; i++)
        {
            Panel(held && _pressed == "category" + i && CategoryButtons[i].Contains(local) ? categoryPanels[i] + 1 : categoryPanels[i], 0, categoryPanelY[i]);
        }

        Panel(218, 0, 146);
        for (var y = 179; y <= 249; y += 10)
        {
            Panel(219, 0, y);
        }

        Panel(220, 0, 259);
        Panel(221, 146, 0);
        var rooms = _category is { } chosen ? Rooms(simulation, host.Player, chosen) : [];
        for (var i = 0; i < 10; i++)
        {
            var row = new Rectangle(158, 34 + (i * 19), 126, 19);
            Panel(held && i < rooms.Count && _pressed == "room" + i && row.Contains(local) ? 223 : 222, 146, 34 + (i * 19));
        }

        Panel(224, 146, 224);

        void Text(TextRenderer font, string text, int x, int y) => font.Draw(batch, text, new Vector2(origin.X + x, origin.Y + y));
        Text(white, host.Texts?.String(16, _category is null ? 2 : 3) ?? (_category is null ? "Pick department" : "Pick room type"), 163, 18);
        for (var i = 0; i < 4; i++)
        {
            Text(i == _category ? blue : white, host.Texts?.String(19, 4 + i) ?? new[] { "Diagnosis", "Treatment", "Clinics", "Facilities" }[i], 19, CategoryY[i]);
        }

        for (var i = 0; i < Math.Min(10, rooms.Count); i++)
        {
            var lit = rooms[i].Id == "gp" && Tutorial.Lit("room:gp");
            Text(i == _hovered || lit ? blue : white, rooms[i].Name, 163, 21 + ((i + 1) * 19));
        }

        var cost = _hovered >= 0 && _hovered < rooms.Count ? RoomPurchases.BuildCost(simulation, simulation.Hospitals[host.Player], rooms[_hovered]) : 0;
        Text(white, (host.Texts?.String(16, 5) ?? "Cost: ") + cost.ToString(CultureInfo.InvariantCulture), 163, 232);
        if (_hovered >= 0 && _hovered < rooms.Count && Previews.TryGetValue(rooms[_hovered].Id, out var preview))
        {
            host.DrawAnimation(batch, preview, origin + new Point(70, 200));
        }
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        var simulation = host.Simulation;
        var rooms = _category is { } chosen ? Rooms(simulation, host.Player, chosen) : [];
        var hovered = RoomAt(local, rooms.Count);
        if (hovered != _hovered)
        {
            // onMouseMove: HLight5.wav as the cursor goes onto another room.
            host.PlaySound("HLight5.wav");
            _hovered = hovered;
        }

        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = Close.Contains(local) ? "close" : hovered >= 0 ? "room" + hovered : CategoryAt(local) is { } category ? "category" + category : null;
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed == "close" && Close.Contains(local))
        {
            host.PlaySound("no4.wav");
            host.Close(this);
        }
        else if (pressed is { } key && key.StartsWith("category", StringComparison.Ordinal) && CategoryAt(local) is { } category && key == "category" + category)
        {
            host.PlaySound(WindowLayer.DefaultButtonSound);
            _category = category;
            _hovered = -1;
        }
        else if (pressed is { } roomKey && roomKey.StartsWith("room", StringComparison.Ordinal) && hovered >= 0 && roomKey == "room" + hovered)
        {
            // buildRoom: the blueprint, or the adviser and Wrong2.wav without the money.
            var room = rooms[hovered];
            if (RoomPurchases.CanAfford(simulation, room, host.Player))
            {
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Close(this);
                build(room);
            }
            else
            {
                host.Adviser.Say(host.Texts?.String(11, 73) ?? "You need more money: take a loan from the bank.", Environment.TickCount64);
                host.PlaySound("wrong2.wav");
            }
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return CategoryAt(local) is { } category ? Tip(2 + category, "Rooms of this kind")
            : Close.Contains(local) ? Tip(7, "Close this window")
            : new Rectangle(160, 228, 122, 14).Contains(local) ? Tip(6, "The room's cost")
            : null;
    }

    private static int? CategoryAt(Point local)
    {
        for (var i = 0; i < CategoryButtons.Length; i++)
        {
            if (CategoryButtons[i].Contains(local))
            {
                return i;
            }
        }

        return null;
    }

    private static int RoomAt(Point local, int count)
    {
        for (var i = 0; i < Math.Min(10, count); i++)
        {
            if (new Rectangle(158, 34 + (i * 19), 126, 19).Contains(local))
            {
                return i;
            }
        }

        return -1;
    }
}
