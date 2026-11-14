// The town map as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/fullscreen/town_map.lua (UITownMap:
// its buttons, counts, heating, balance, the map with its entities, the plot under the cursor, buying and jumping) and
// CorsixTH/Src/th_lua_ui.cpp (l_town_map_draw).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Client.Rendering;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Research;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The fullscreen town map over Town01V: on the left the counts of patients, plants, fire extinguishers, other objects
/// and radiators with switches for showing each on the map, the radiators' heat with its buttons, the heating bill and
/// the balance (a click opens the bank manager, a right click the statement); on the right the town, three pixels a
/// tile, the hospital dark blue or by warmth, land for sale red, walls white, with the people and objects switched on;
/// below it the plot under the cursor. A click on land for sale buys it; a right click moves the view there.
/// </summary>
internal sealed class TownMapScreen
{
    private const string Palette = "TOWN01V";
    private const int MapX = 227;
    private const int MapY = 25;

    private static readonly Rectangle Close = new(594, 437, 26, 26);
    private static readonly Rectangle Balance = new(30, 420, 200, 50);
    private static readonly Rectangle Warmer = new(171, 315, 20, 20);
    private static readonly Rectangle Colder = new(70, 314, 20, 20);
    private static readonly int[] SwitchY = [37, 89, 141, 193, 246];

    /// <summary>Which of people, plants, extinguishers, objects and radiators are shown, kept between openings.</summary>
    private static readonly bool[] Shown = [true, true, true, true, true];

    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("TOWN01V") is not null && graphics.Sheet("TOWN02V", Palette) is not null
        && graphics.Font("FONT34V", Palette) is not null && graphics.Font("FONT31V", Palette) is not null
        && graphics.Font("FONT05V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The tile at a point of the dialog (<c>onMouseMove</c>), or null off the map.</summary>
    public static TilePosition? TileAt(LevelMap map, Point local)
    {
        int x = (int)Math.Floor((local.X - MapX) / 3.0), y = (int)Math.Floor((local.Y - MapY) / 3.0);
        return map.Contains(x, y) ? new TilePosition(x, y) : null;
    }

    /// <summary>The heating bill shown: the radiators' heat in tenths, times the radiators, times 7.5, rounded down.</summary>
    public static long HeatingBill(int heatTenths, int radiators, bool freeBuild) => freeBuild ? 0 : (long)Math.Floor(heatTenths * radiators * 7.5);

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var map = simulation.Map;
        batch.Draw(graphics.Picture("TOWN01V")!, origin.ToVector2(), Color.White);
        var sheet = graphics.Sheet("TOWN02V", Palette)!;
        var info = graphics.Font("FONT34V", Palette)!;
        var city = graphics.Font("FONT31V", Palette)!;
        var money = graphics.Font("FONT05V", OriginalGraphics.MainPalette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void At(TextRenderer font, string text, int x, int y) => font.Draw(batch, text, new Vector2(origin.X + x, origin.Y + y));
        void Pressed(string key, Rectangle area, int sprite)
        {
            if (held && _pressed == key && area.Contains(local))
            {
                sheet.Draw(batch, sprite, origin.X + area.X, origin.Y + area.Y, Color.White);
            }
        }

        Pressed("close", Close, 8);
        Pressed("warmer", Warmer, 6);
        Pressed("colder", Colder, 7);
        for (var i = 0; i < SwitchY.Length; i++)
        {
            // A switch that is off shows its panel; one that is on shows the picture under it.
            if (!Shown[i])
            {
                sheet.Draw(batch, i + 1, origin.X + 140, origin.Y + SwitchY[i], Color.White);
            }
        }

        var objects = simulation.Entities.OfType<RoomObject>().Where(o => !o.IsDestroyed && Ours(map, host.Player, o.Tile)).ToList();
        var patients = simulation.Entities.OfType<Humanoid>().Count(h => h.Behaviour is PatientBehaviour && h.HospitalPlayer == host.Player && !h.IsDespawning && InHospital(map, host.Player, h.Tile));
        var radiators = objects.Count(o => o.ObjectType == "radiator");
        At(info, patients.ToString(CultureInfo.InvariantCulture), 95, 57);
        At(info, objects.Count(o => o.ObjectType == "plant").ToString(CultureInfo.InvariantCulture), 95, 110);
        At(info, objects.Count(o => o.ObjectType == "extinguisher").ToString(CultureInfo.InvariantCulture), 95, 157);
        At(info, objects.Count(o => o.ObjectType is not ("plant" or "extinguisher" or "radiator")).ToString(CultureInfo.InvariantCulture), 95, 211);
        At(info, radiators.ToString(CultureInfo.InvariantCulture), 95, 265);
        var bill = HeatingBill(hospital.RadiatorHeat, radiators, simulation.Rules.FreeBuild).ToString(CultureInfo.InvariantCulture);
        At(info, bill, 100 + (8 * 9) - info.Measure(bill).Width, 355);
        foreach (var (digit, dx) in OriginalBottomPanel.BalanceDigits(hospital.Balance))
        {
            money.Draw(batch, digit.ToString(), new Vector2(origin.X + dx + 5, origin.Y + 431));
        }

        // The radiators' heat: a pixel-wide sprite every pixel over 60 for the whole.
        for (var dx = 0; dx <= 6 * hospital.RadiatorHeat; dx++)
        {
            sheet.Draw(batch, 9, origin.X + 101 + dx, origin.Y + 319, Color.White);
        }

        DrawTown(batch, host, graphics.Pixel, origin + new Point(MapX, MapY), Shown[4]);
        DrawEntities(batch, simulation, host.Player, graphics.Pixel, origin + new Point(MapX, MapY));

        // The plot under the cursor.
        string number = "-", owner = "-", area = "-", price = "-";
        if (TileAt(map, local) is { } tile)
        {
            var parcel = map[tile.X, tile.Y].Parcel;
            if (parcel == 0)
            {
                price = host.Texts?.String(13, 4) ?? "Not for sale";
            }
            else
            {
                number = parcel.ToString(CultureInfo.InvariantCulture);
                area = map.ParcelTileCount(parcel).ToString(CultureInfo.InvariantCulture);
                var ownerNumber = map.GetParcelOwner(parcel);
                if (ownerNumber == 0)
                {
                    owner = host.Texts?.String(13, 3) ?? "For sale";
                    price = string.Create(CultureInfo.InvariantCulture, $"${simulation.ParcelPrice(parcel)}");
                }
                else
                {
                    owner = ownerNumber == host.Player + 1 ? host.PlayerName : string.Create(CultureInfo.InvariantCulture, $"Player {ownerNumber}");
                }
            }
        }

        foreach (var (label, value, x, y) in new[]
        {
            (host.Texts?.String(13, 5) ?? "Number", number, 227, 435),
            (host.Texts?.String(13, 6) ?? "Owner", owner, 227, 450),
            (host.Texts?.String(13, 7) ?? "Area", area, 432, 435),
            (host.Texts?.String(13, 8) ?? "Price", price, 432, 450),
        })
        {
            At(city, label, x, y);
            At(city, ":", x + (x == 227 ? 73 : 63), y);
            At(city, value, x + (x == 227 ? 88 : 83), y);
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        if (mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released)
        {
            if (Balance.Contains(local))
            {
                host.Open(new BankStatementWindow());
            }
            else if (TileAt(simulation.Map, local) is { } jump && simulation.Map[jump.X, jump.Y].Parcel != 0)
            {
                // onMouseUp's right button: the view goes there and the map closes.
                host.CenterOn(jump);
                host.Close(window);
            }

            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = Close.Contains(local) ? "close" : Warmer.Contains(local) ? "warmer" : Colder.Contains(local) ? "colder"
                : Balance.Contains(local) ? "balance" : SwitchAt(local) is { } on ? "switch" + on : TileAt(simulation.Map, local) is not null ? "map" : null;
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        switch (pressed)
        {
            case "close" when Close.Contains(local):
                host.PlaySound("no4.wav");
                host.Close(window);
                break;
            case "warmer" when Warmer.Contains(local):
            case "colder" when Colder.Contains(local):
                // increaseHeat and decreaseHeat, from 1 to 10 tenths, not while the boiler is broken.
                host.PlaySound(WindowLayer.DefaultButtonSound);
                if (!hospital.IsHeatingBroken)
                {
                    host.Submit(new SetRadiatorHeatCommand(Math.Clamp(hospital.RadiatorHeat + (pressed == "warmer" ? 1 : -1), 1, 10)));
                }

                break;
            case "balance" when Balance.Contains(local):
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Open(new BankManagerWindow());
                break;
            case "map" when TileAt(simulation.Map, local) is { } tile:
                // onMouseUp's left button: buy the plot, cashreg.wav, or Wrong2.wav when it cannot be bought.
                var parcel = simulation.Map[tile.X, tile.Y].Parcel;
                if (parcel != 0)
                {
                    var buyable = simulation.IsParcelPurchasable(parcel, host.Player) && simulation.ParcelPrice(parcel) <= hospital.Balance;
                    host.PlaySound(buyable ? "cashreg.wav" : "wrong2.wav");
                    if (buyable)
                    {
                        host.Submit(new PurchaseParcelCommand(parcel));
                    }
                }

                break;
            case { } name when name.StartsWith("switch", StringComparison.Ordinal) && SwitchAt(local) is { } index && name == "switch" + index:
                host.PlaySound(WindowLayer.DefaultButtonSound);
                Shown[index] = !Shown[index];
                break;
        }
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return Close.Contains(local) ? Tip(201, "Close the town map")
            : Balance.Contains(local) ? Tip(200, "Your balance")
            : Warmer.Contains(local) ? Tip(197, "Turn the heating up")
            : Colder.Contains(local) ? Tip(198, "Turn the heating down")
            : SwitchAt(local) is { } index ? Tip(191 + index, "Show or hide them on the map")
            : new Rectangle(94, 318, 73, 13).Contains(local) ? Tip(196, "The radiators' heat")
            : new Rectangle(72, 351, 95, 23).Contains(local) ? Tip(199, "The heating bill")
            : null;
    }

    /// <summary>
    /// l_town_map_draw: the hospital's land dark blue (or by its warmth while the radiators are shown), land for sale red,
    /// the walls white, three pixels a tile.
    /// </summary>
    private static void DrawTown(SpriteBatch batch, IWindowHost host, Texture2D pixel, Point at, bool heat)
    {
        var simulation = host.Simulation;
        var map = simulation.Map;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var tile = map[x, y];
                var original = map.HasOriginalTiles ? map.OriginalTile(x, y) : tile;
                int px = at.X + (x * 3), py = at.Y + 3 + (y * 3);
                if (original.Has(TileFlags.Hospital))
                {
                    Color? colour = null;
                    if (!tile.Has(TileFlags.Hospital))
                    {
                        if (simulation.IsParcelPurchasable(tile.Parcel, host.Player))
                        {
                            colour = new Color(255, 0, 0);
                        }
                    }
                    else
                    {
                        colour = heat ? MapViewScreen.TemperatureColor(simulation.Temperatures[x, y]) : new Color(0, 0, 70);
                    }

                    if (colour is { } fill)
                    {
                        batch.Draw(pixel, new Rectangle(px, py, 3, 3), fill);
                    }
                }

                var shown = tile.Has(TileFlags.Hospital) || original.Has(TileFlags.Hospital);
                if (shown && tile.NorthWall != 0)
                {
                    batch.Draw(pixel, new Rectangle(px, py, 3, 1), Color.White);
                }

                if (shown && tile.WestWall != 0)
                {
                    batch.Draw(pixel, new Rectangle(px, py, 1, 3), Color.White);
                }
            }
        }
    }

    /// <summary>The people and objects switched on, as UITownMap:draw's coloured squares.</summary>
    private static void DrawEntities(SpriteBatch batch, Simulation simulation, int player, Texture2D pixel, Point at)
    {
        void Square(TilePosition tile, Color colour, int size) =>
            batch.Draw(pixel, new Rectangle(at.X + (tile.X * 3) - 2, at.Y + (tile.Y * 3) + 1, size, size), colour);

        var map = simulation.Map;
        if (Shown[0])
        {
            foreach (var humanoid in simulation.Entities.OfType<Humanoid>().Where(h => InHospital(map, player, h.Tile)))
            {
                if (humanoid.Profile is not null && humanoid.Employer == player && !humanoid.IsFired)
                {
                    Square(humanoid.Tile, new Color(97, 109, 235), 2);
                }
                else if (humanoid.Behaviour is PatientBehaviour && humanoid.HospitalPlayer == player)
                {
                    Square(humanoid.Tile, Color.White, 2);
                }
            }
        }

        foreach (var placed in simulation.Entities.OfType<RoomObject>().Where(o => !o.IsDestroyed))
        {
            switch (placed.ObjectType)
            {
                case "radiator" when Shown[4]:
                    Square(placed.Tile, new Color(255, 0, 70), 1);
                    break;
                case "extinguisher" when Shown[2]:
                    Square(placed.Tile, new Color(216, 0, 0), 2);
                    break;
                case "plant" when Shown[1]:
                    Square(placed.Tile, new Color(127, 180, 73), 2);
                    break;
                default:
                    if (Shown[3] && ResearchObjects.Find(placed.ObjectType) is { IsMachine: true })
                    {
                        Square(placed.Tile, new Color(142, 182, 182), 3);
                    }

                    break;
            }
        }
    }

    private static int? SwitchAt(Point local)
    {
        for (var i = 0; i < SwitchY.Length; i++)
        {
            if (new Rectangle(140, SwitchY[i], 46, 46).Contains(local))
            {
                return i;
            }
        }

        return null;
    }

    private static bool Ours(LevelMap map, int player, TilePosition tile) =>
        map.Contains(tile.X, tile.Y) && map.GetParcelOwner(map[tile.X, tile.Y].Parcel) == player + 1;

    private static bool InHospital(LevelMap map, int player, TilePosition tile) =>
        Ours(map, player, tile) && map[tile.X, tile.Y].Has(TileFlags.Hospital);
}
