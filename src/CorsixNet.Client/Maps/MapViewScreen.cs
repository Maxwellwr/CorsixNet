using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Client.Sessions;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Maps;

/// <summary>A level to show: an original LEVELS file or a map file elsewhere on disk.</summary>
internal sealed record LevelSource(string Name, Func<LoadedLevelMap> Load);

/// <summary>
/// Shows the map of a game session with a movable, zoomable camera.
/// </summary>
/// <remarks>
/// Keys: arrows/WASD or right/middle-drag pan, wheel zoom, Home reset camera,
/// PgUp/PgDn previous/next level (local games), O cycle overlay, U cycle sprite upscaling, Esc quit.
/// Simulation test controls: H spawn a patient at the cursor, G spawn ten in the hospital,
/// left click select a patient or send the selected one walking, R toggle random wandering,
/// Space or P pause, 1 to 5 game speed, Z held speeds up (local games), Ctrl+1 to Ctrl+4 hire staff. B builds a GP's office: blueprint, door, windows, then furniture
/// (right click turns an object, P picks one up, Enter confirms, Esc steps back). In a network
/// game, commands go through the server.
/// </remarks>
internal sealed class MapViewScreen : IScreen
{
    private const float PanSpeed = 900f;

    /// <summary>How far an earthquake at full strength moves the view, in map pixels (CorsixTH's <c>shake_screen_max_movement</c>).</summary>
    private const float ShakeMaxMovement = 50f;
    private const float ZoomStep = 1.25f;
    private const int StatusHeight = 36;

    private static readonly Color Background = Color.Black;
    private static readonly Color HoverTint = new Color(255, 255, 255) * 0.35f;
    private static readonly byte[] NoLayers = [];
    private static readonly Color PickUpTint = new(150, 200, 255);
    private static readonly Color CellOutlineColor = new(232, 0, 0);

    /// <summary>Colour of the status bar and the frame around the map while building, the blue of the blueprints.</summary>
    private static readonly Color BuildModeColor = new(0, 48, 144);

    /// <summary>Background of the date and speed box.</summary>
    private static readonly Color DatePanelColor = new(40, 40, 40);
    private static readonly Color MoneyPanelColor = new(0, 0, 0, 200);

    /// <summary>Width of the frame drawn around the map while building.</summary>
    private const int BuildFrameWidth = 3;

    /// <summary>How long a notice such as "not enough money" stays in the status line.</summary>
    private const int NoticeMilliseconds = 4000;

    /// <summary>Room door animations from CorsixTH's <c>objects/door.lua</c>.</summary>
    private const int RoomDoorNorth = 104;
    private const int RoomDoorWest = 106;

    private readonly GraphicsDevice _device;
    private readonly IReadOnlyList<LevelSource> _levels;
    private readonly SpriteAtlas _blocks;
    private readonly BlueprintDrawing _blueprints;
    private readonly MapRenderer _renderer;
    private readonly TextRenderer _text;
    private readonly Texture2D _diamond;
    private readonly Texture2D _cellOutline;
    private readonly Texture2D _pixel;
    private readonly Camera _camera = new();
    private readonly AnimationRenderer _animations;
    private readonly HumanoidDrawing _humanoids;
    private readonly EntranceDoorDrawing _doors;
    private readonly Dictionary<TilePosition, List<Entity>> _entitiesByTile = [];
    private readonly BuildRoomTool _buildTool = new();
    private readonly FurnishCorridorTool _furnishTool = new();
    private readonly HashSet<int> _hiddenObjects = [];
    private readonly ObjectDrawing _objects;

    /// <summary>Objects the demo still has to place in its GP's office, one at a time.</summary>
    private readonly Queue<string> _demoFurniture = new();
    private int _demoFurnitureExpected;
    private readonly List<TilePosition> _hospitalTiles = [];

    // Test controls choose targets at random; the choices reach the simulation as commands.
    private readonly Random _random = new();

    private IGameSession? _session;
    private int _levelIndex;
    private string? _error;
    private MapOverlay _overlay;
    private RenderTarget2D? _target;
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private Point? _hoveredTile;
    private (int X, int Y) _hoveredFraction;
    private Point _rightPressedAt;
    private bool _buildBack;

    /// <summary>The money panel shown over the map: none, the bank statement (F2) or the graphs' figures (F8).</summary>
    private MoneyPanel _moneyPanel;
    private string? _notice;
    private long _noticeUntil;

    /// <summary>Until when the status line reminds that other keys wait until building is over.</summary>
    private long _buildReminderUntil;
    private bool _wander;
    private bool _demoPending;

    /// <summary>The speed a local game starts at (<c>--speed</c>).</summary>
    private readonly GameSpeed _initialSpeed;
    private bool _demoDeskPending;
    private bool _demoDiagnosisPending;
    private int? _selected;
    private LevelMap? _preparedMap;
    private string? _capturePath;

    /// <summary>Creates the view for a local game on <paramref name="levels"/>, or for <paramref name="networkSession"/> if given.</summary>
    public MapViewScreen(
        ThemeHospitalInstall install,
        GraphicsDevice device,
        IReadOnlyList<LevelSource> levels,
        float zoom = 1f,
        MapOverlay overlay = MapOverlay.None,
        bool demo = false,
        IGameSession? networkSession = null,
        int upscale = 1,
        GameSpeed speed = GameSpeed.Normal)
    {
        _device = device;
        _initialSpeed = speed;
        _levels = networkSession is null ? levels : [];

        var palette = install.LoadPalette("DATA", "MPALETTE.DAT");
        // Floor tiles are the 64x32 diamonds of the block sheet.
        _blocks = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "VBLK-0", complex: false), palette, upscale, sprite => sprite.Width == 64 && sprite.Height == 32);
        _renderer = new MapRenderer(_blocks);
        _text = new TextRenderer(device, new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT01V", complex: false)), palette);
        var sprites = install.LoadSpriteSheet("DATA", "VSPR-0", complex: false);
        _animations = new AnimationRenderer(device, AnimationSet.Load(install), sprites, palette, upscale);
        _blueprints = new BlueprintDrawing(device, install, _blocks, _animations, sprites, palette);
        _humanoids = new HumanoidDrawing(_animations);
        _doors = new EntranceDoorDrawing(_animations);
        _objects = new ObjectDrawing(_animations);
        _diamond = CreateDiamond(device);
        _cellOutline = CreateCellOutline(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);

        _overlay = overlay;
        _demoPending = demo;
        _demoDeskPending = demo;
        _demoDiagnosisPending = demo;
        if (demo)
        {
            foreach (var item in new[] { "desk", "cabinet", "chair" })
            {
                _demoFurniture.Enqueue(item);
            }
        }
        if (networkSession is not null)
        {
            _session = networkSession;
        }
        else
        {
            SelectLevel(0);
        }

        PrepareMapIfReady();
        _camera.ZoomAt(zoom, new Point(ViewSize.X / 2, ViewSize.Y / 2), ViewSize);
    }

    public bool ExitRequested { get; private set; }

    /// <summary>
    /// Saves the next rendered map view (without the status bar) to a PNG file. The off-screen
    /// buffer is used, so this works even when the window cannot be seen, e.g. on a locked screen.
    /// </summary>
    public void RequestCapture(string path) => _capturePath = path;

    private Simulation? Simulation => _session?.Simulation;

    private LevelMap? Map => _session?.Simulation?.Map;

    /// <summary>Lists the original levels in the game's LEVELS folder, in campaign order.</summary>
    public static IReadOnlyList<LevelSource> OriginalLevels(ThemeHospitalInstall install)
    {
        var folder = install.FindDirectory("LEVELS");
        if (folder is null)
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "LEVEL.L*")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(name => (Name: name.ToUpperInvariant(), Number: int.TryParse(name.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue))
            .OrderBy(level => level.Number)
            .ThenBy(level => level.Name, StringComparer.Ordinal)
            .Select(level => new LevelSource(level.Name, () => LevelMapLoader.LoadOriginal(install, level.Name)))
            .ToList();
    }

    public void Update(GameTime gameTime, bool hasFocus)
    {
        if (!hasFocus)
        {
            // No input, but the game goes on.
            AdvanceSession(gameTime);
            return;
        }

        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        var viewSize = ViewSize;
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

        if (Pressed(Keys.Escape))
        {
            if (_buildTool.IsActive || _furnishTool.IsActive)
            {
                _buildBack = true;
            }
            else
            {
                ExitRequested = true;
            }
        }

        if (_levels.Count > 0 && Pressed(Keys.PageDown))
        {
            SelectLevel((_levelIndex + 1) % _levels.Count);
        }

        if (_levels.Count > 0 && Pressed(Keys.PageUp))
        {
            SelectLevel((_levelIndex + _levels.Count - 1) % _levels.Count);
        }

        if (Pressed(Keys.Home))
        {
            ResetCamera();
        }

        // Shift+O opens the hospital early instead.
        if (Pressed(Keys.O) && !keys.IsKeyDown(Keys.LeftShift) && !keys.IsKeyDown(Keys.RightShift))
        {
            _overlay = (MapOverlay)(((int)_overlay + 1) % Enum.GetValues<MapOverlay>().Length);
        }

        if (Pressed(Keys.U))
        {
            // Original pixels, then MMPX-smoothed sprites at 2x and 4x.
            var scale = _blocks.Scale switch { 1 => 2, 2 => 4, _ => 1 };
            _blocks.SetScale(scale);
            _animations.SetScale(scale);
            _blueprints.SetScale(scale);
        }

        var pan = Vector2.Zero;
        if (keys.IsKeyDown(Keys.Left) || keys.IsKeyDown(Keys.A))
        {
            pan.X -= 1;
        }

        if (keys.IsKeyDown(Keys.Right) || keys.IsKeyDown(Keys.D))
        {
            pan.X += 1;
        }

        if (keys.IsKeyDown(Keys.Up) || keys.IsKeyDown(Keys.W))
        {
            pan.Y -= 1;
        }

        if (keys.IsKeyDown(Keys.Down) || keys.IsKeyDown(Keys.S))
        {
            pan.Y += 1;
        }

        var seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _camera.Center += pan * PanSpeed * seconds / _camera.Zoom;

        var dragging = mouse.RightButton == ButtonState.Pressed || mouse.MiddleButton == ButtonState.Pressed;
        var wasDragging = _previousMouse.RightButton == ButtonState.Pressed || _previousMouse.MiddleButton == ButtonState.Pressed;
        if (dragging && wasDragging)
        {
            _camera.Center -= new Vector2(mouse.X - _previousMouse.X, mouse.Y - _previousMouse.Y) / _camera.Zoom;
        }

        var wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (wheel != 0)
        {
            var factor = wheel > 0 ? ZoomStep : 1 / ZoomStep;
            _camera.ZoomAt(_camera.Zoom * factor, new Point(mouse.X, mouse.Y - StatusHeight), viewSize);
        }

        ClampCamera();

        var inView = mouse.Y >= StatusHeight && mouse.X >= 0 && mouse.X < viewSize.X && mouse.Y < StatusHeight + viewSize.Y;
        var world = _camera.ScreenToWorld(new Point(mouse.X, mouse.Y - StatusHeight), viewSize);
        var tile = Camera.WorldToTile(world);
        _hoveredTile = inView && Map is { } map && map.Contains(tile.X, tile.Y) ? tile : null;
        _hoveredFraction = Camera.WorldToTileFraction(world);

        UpdateSimulation(gameTime, mouse, keys, Pressed);

        _previousKeys = keys;
        _previousMouse = mouse;
    }

    /// <summary>
    /// Where an earthquake moves the view this frame (CorsixTH's <c>GameUI:draw</c> with <c>shake_screen_intensity</c>);
    /// the view keeps still while the game is paused, as CorsixTH's "pause" stage stops the shaking.
    /// </summary>
    private Point ShakeOffset()
    {
        var intensity = _session is { IsPaused: false, Simulation: { } simulation } ? simulation.Earthquakes.Shake switch
        {
            EarthquakeShake.Warning => 0.2f,
            EarthquakeShake.Small => 0.5f,
            EarthquakeShake.Large => 1f,
            _ => 0f,
        } : 0f;
        if (intensity == 0f)
        {
            return Point.Zero;
        }

        int Offset() => (int)MathF.Floor((0.5f - _random.NextSingle()) * intensity * ShakeMaxMovement * 2);
        return new Point(Offset(), Offset());
    }

    public void Draw(SpriteBatch batch)
    {
        var viewSize = ViewSize;
        var worldSize = _camera.WorldSize(viewSize);
        var topLeft = _camera.TopLeft(viewSize) + ShakeOffset();
        var scale = _blocks.Scale;
        EnsureTarget(new Point(worldSize.X * scale, worldSize.Y * scale));

        // Draw the map into an off-screen target at the sprites' scale (1:1 for original sprites),
        // then scale it to the window. Scaling the finished image avoids seams between tiles at
        // fractional zoom levels.
        _device.SetRenderTarget(_target);
        _device.Clear(Background);
        if (Map is { } map)
        {
            var view = new Rectangle(topLeft, worldSize);
            var toTarget = Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0) * Matrix.CreateScale(scale, scale, 1);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, transformMatrix: toTarget);
            CollectEntitiesByTile();
            // The object being placed is drawn in the same order as placed objects, so walls in front hide it.
            var ghost = _buildTool.Ghost ?? _furnishTool.Ghost;
            var ghostTile = ghost is { } g && Simulation is { } ghostSimulation ? ObjectDrawing.DrawTile(ghostSimulation.Content, g.ObjectType, g.Tile, g.Orientation) : (TilePosition?)null;
            var blueprint = _buildTool.Blueprint;
            _renderer.Draw(batch, map, view, (tile, anchorX, anchorY) =>
            {
                if (blueprint is not null)
                {
                    _blueprints.DrawTile(batch, blueprint, tile, anchorX, anchorY);
                }

                DrawEntities(batch, tile, anchorX, anchorY);
                if (tile == ghostTile && ghost is { } shown)
                {
                    _objects.Draw(batch, shown.ObjectType, shown.Tile, shown.Orientation, Simulation!.Tick, shown.Tint);
                }
            });
            DrawOverlay(batch, map, view);
            _buildTool.DrawPreview(batch, _diamond);
            if ((_buildTool.IsActive || _furnishTool.IsActive) && _hoveredTile is { } cell)
            {
                // While building, the tile under the cursor has a red outline, as in CorsixTH's UIEditRoom:draw.
                batch.Draw(_cellOutline, new Vector2((32 * (cell.X - cell.Y)) - 32, 16 * (cell.X + cell.Y)), CellOutlineColor);
            }
            batch.End();
            if (_capturePath is { } capturePath && _target is not null)
            {
                using (var file = File.Create(capturePath))
                {
                    _target.SaveAsPng(file, _target.Width, _target.Height);
                }

                Console.WriteLine($"Saved {_target.Width}x{_target.Height} capture to {capturePath}");
                _capturePath = null;
                ExitRequested = true;
            }
        }

        _device.SetRenderTarget(null);
        _device.Clear(Background);

        var sampler = _camera.Zoom >= scale ? SamplerState.PointClamp : SamplerState.LinearClamp;
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, sampler);
        var destination = new Rectangle(
            0,
            StatusHeight,
            (int)MathF.Round(worldSize.X * _camera.Zoom),
            (int)MathF.Round(worldSize.Y * _camera.Zoom));
        batch.Draw(_target, destination, Color.White);
        batch.End();

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        var viewport = _device.Viewport;
        var building = _buildTool.IsActive || _furnishTool.IsActive;
        batch.Draw(_pixel, new Rectangle(0, 0, viewport.Width, StatusHeight), building ? BuildModeColor : Color.Black);
        if (building)
        {
            // A frame round the map, so that building can be told apart at a glance.
            var mapHeight = viewport.Height - StatusHeight;
            batch.Draw(_pixel, new Rectangle(0, StatusHeight, BuildFrameWidth, mapHeight), BuildModeColor);
            batch.Draw(_pixel, new Rectangle(viewport.Width - BuildFrameWidth, StatusHeight, BuildFrameWidth, mapHeight), BuildModeColor);
            batch.Draw(_pixel, new Rectangle(0, viewport.Height - BuildFrameWidth, viewport.Width, BuildFrameWidth), BuildModeColor);
        }

        _text.Draw(batch, StatusLine(), new Vector2(8, 4));
        _text.Draw(batch, _error ?? TileLine(), new Vector2(8, 4 + _text.LineHeight + 2));
        DrawDatePanel(batch, viewport);
        DrawMoneyPanel(batch);
        batch.End();
    }

    public void Dispose()
    {
        _session?.Dispose();
        _target?.Dispose();
        _blueprints.Dispose();
        _animations.Dispose();
        _blocks.Dispose();
        _text.Dispose();
        _diamond.Dispose();
        _cellOutline.Dispose();
        _pixel.Dispose();
    }

    private Point ViewSize => new(_device.Viewport.Width, Math.Max(1, _device.Viewport.Height - StatusHeight));

    private void SelectLevel(int index)
    {
        if (_levels.Count == 0)
        {
            _error = "No levels found.";
            return;
        }

        _levelIndex = index;
        _session?.Dispose();
        _session = null;
        try
        {
            var level = _levels[index].Load();
            var local = new LocalSession(_levels[index].Name, level.StartSimulation(seed: 1));
            local.SetSpeed(_initialSpeed);
            _session = local;
            _error = null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            _error = $"Cannot load {_levels[index].Name}: {e.Message}";
        }

        PrepareMapIfReady();
    }

    /// <summary>
    /// Sets up everything that depends on the map once it is available: immediately for local
    /// games, after joining for network games.
    /// </summary>
    private void PrepareMapIfReady()
    {
        if (Map is not { } map || ReferenceEquals(map, _preparedMap))
        {
            return;
        }

        _preparedMap = map;
        _selected = null;
        _humanoids.Reset();
        _doors.Reset();
        _hospitalTiles.Clear();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (map[x, y].Has(TileFlags.Passable | TileFlags.Hospital))
                {
                    _hospitalTiles.Add(new TilePosition(x, y));
                }
            }
        }

        ResetCamera();
        if (_demoPending)
        {
            _demoPending = false;
            StartDemo(map);
        }
    }

    /// <summary>
    /// Spawns twenty patients, half inside and half outside near the camera, hires one of each kind of
    /// staff, and lets them wander.
    /// </summary>
    private void StartDemo(LevelMap map)
    {
        if (_session is null || _hospitalTiles.Count == 0)
        {
            return;
        }

        // The demo does not wait a hundred days for the hospital to open.
        _session.Submit(new OpenHospitalCommand());

        var start = map.GetCameraTile(Math.Min(_session.Player, map.PlayerCount - 1));
        var outside = new List<TilePosition>();
        for (var y = Math.Max(0, start.Y - 12); y <= Math.Min(map.Height - 1, start.Y + 12); y++)
        {
            for (var x = Math.Max(0, start.X - 12); x <= Math.Min(map.Width - 1, start.X + 12); x++)
            {
                var tile = map[x, y];
                if (tile.Has(TileFlags.Passable) && !tile.Has(TileFlags.Hospital))
                {
                    outside.Add(new TilePosition(x, y));
                }
            }
        }

        // Build a GP's office first, so that spawned patients cannot appear inside its walls.
        var roomPlan = Simulation?.Content.FindRoom("gp") is { } gp ? FindRoomSpot(map, gp, start) : null;
        if (roomPlan is { } plan)
        {
            _session.Submit(new BuildRoomCommand(plan));
        }

        for (var i = 0; i < 20; i++)
        {
            var pool = i % 2 == 0 || outside.Count == 0 ? _hospitalTiles : outside;
            _session.Submit(new SpawnHumanoidCommand(pool[_random.Next(pool.Count)]));
        }

        // Hire one of each kind of staff and place them near the camera, so that they can be seen.
        // Not inside the room, which is built first: receptionists may not go into rooms.
        var nearby = _hospitalTiles
            .Where(tile => roomPlan is not { } built || !new TileRect(built.Area.X - 1, built.Area.Y - 1, built.Area.Width + 2, built.Area.Height + 2).Contains(tile))
            .OrderBy(tile => Math.Abs(tile.X - start.X) + Math.Abs(tile.Y - start.Y))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Take(40)
            .ToList();
        // The most skilled candidate of each kind, so that the GP diagnoses patients well enough to send them on to
        // the pharmacy while there are no diagnosis rooms.
        foreach (var staffClass in StaffClasses.HireOrder)
        {
            var candidates = Simulation?.AvailableStaff(staffClass) ?? [];
            var best = candidates.Count == 0 ? 0 : candidates.Select((profile, index) => (profile.Skill, index)).Max().index;
            _session.Submit(new HireStaffCommand(staffClass, best, nearby[_random.Next(nearby.Count)]));
        }

        _wander = true;
    }

    /// <summary>
    /// Places the demo's furniture in its unfinished room (the GP's office, then the pharmacy), one object at a
    /// time: each placement is checked against the state that includes the previous object. The room is confirmed
    /// with its last object.
    /// </summary>
    private void FurnishDemoRoom()
    {
        if (_session is null || Simulation is not { } simulation || _demoFurniture.Count == 0)
        {
            return;
        }

        var objectId = _demoFurniture.Peek();
        var room = simulation.Entities.OfType<Room>()
            .FirstOrDefault(r => !r.IsActive && simulation.Content.FindRoom(r.RoomType)?.Allows(objectId) == true);
        if (room is null || simulation.Entities.OfType<RoomObject>().Count() < _demoFurnitureExpected)
        {
            return;
        }

        _demoFurniture.Dequeue();
        var confirm = !_demoFurniture.TryPeek(out var next) || simulation.Content.FindRoom(room.RoomType)?.Allows(next) != true;
        foreach (var tile in DemoFurnitureTiles(room.Area, objectId))
        {
            foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
            {
                if (ObjectPlacer.Check(simulation, objectId, tile, orientation, _session.Player) == ObjectPlacementProblem.None)
                {
                    _session.Submit(new PlaceObjectCommand(objectId, tile, orientation));
                    _demoFurnitureExpected++;
                    if (confirm)
                    {
                        // Commands of one tick run in order, so the room is complete when this runs.
                        _session.Submit(new ConfirmRoomCommand(room.Id));
                    }

                    return;
                }
            }
        }
    }

    /// <summary>
    /// The tiles of the demo room in the order they are tried for an object: row by row from the back. The
    /// patient's chair keeps off the south and east walls, which are drawn in front of it and would hide the
    /// patient sitting on it.
    /// </summary>
    private static IEnumerable<TilePosition> DemoFurnitureTiles(TileRect area, string objectId)
    {
        var tiles = new List<TilePosition>();
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                tiles.Add(new TilePosition(x, y));
            }
        }

        if (!string.Equals(objectId, "chair", StringComparison.Ordinal))
        {
            return tiles;
        }

        return tiles.OrderBy(tile => (tile.Y == area.Bottom - 1 ? 1 : 0) + (tile.X == area.Right - 1 ? 1 : 0));
    }

    /// <summary>
    /// Once the demo's GP's office works, buys a reception desk near the camera, so that patients start to
    /// arrive and the hired receptionist goes to it.
    /// </summary>
    private void PlaceDemoReceptionDesk()
    {
        if (!_demoDeskPending || _session is null || Simulation is not { } simulation
            || !simulation.Entities.OfType<Room>().Any(room => room.IsActive))
        {
            return;
        }

        _demoDeskPending = false;
        var start = simulation.Map.GetCameraTile(Math.Min(_session.Player, simulation.Map.PlayerCount - 1));

        // A pharmacy next, for the patients the GP diagnoses with a disease cured by a drink. The desk keeps clear of it.
        var keepClear = new TileRect(0, 0, 0, 0);
        if (simulation.Content.FindRoom("pharmacy") is { } pharmacy && FindRoomSpot(simulation.Map, pharmacy, start) is { } pharmacyPlan)
        {
            _session.Submit(new BuildRoomCommand(pharmacyPlan));
            _demoFurniture.Enqueue("pharmacy_cabinet");
            var area = pharmacyPlan.Area;
            keepClear = new TileRect(area.X - 2, area.Y - 2, area.Width + 4, area.Height + 4);
        }

        foreach (var tile in _hospitalTiles.Where(t => !keepClear.Contains(t)).OrderBy(t => Math.Abs(t.X - start.X) + Math.Abs(t.Y - start.Y)).ThenBy(t => t.Y).ThenBy(t => t.X))
        {
            foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
            {
                if (ObjectPlacer.Check(simulation, ReceptionDesks.ObjectId, tile, orientation, _session.Player) == ObjectPlacementProblem.None)
                {
                    _session.Submit(new PlaceObjectCommand(ReceptionDesks.ObjectId, tile, orientation));
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Once the demo's pharmacy works, builds general diagnosis near the camera and hires a second doctor for it, so
    /// that patients the GP cannot diagnose at once get examined and come back.
    /// </summary>
    private void BuildDemoGeneralDiagnosis()
    {
        if (!_demoDiagnosisPending || _session is null || Simulation is not { } simulation
            || !simulation.Entities.OfType<Room>().Any(room => room is { IsActive: true, RoomType: "pharmacy" }))
        {
            return;
        }

        _demoDiagnosisPending = false;
        var start = simulation.Map.GetCameraTile(Math.Min(_session.Player, simulation.Map.PlayerCount - 1));
        if (simulation.Content.FindRoom("general_diag") is not { } room || FindRoomSpot(simulation.Map, room, start) is not { } plan)
        {
            return;
        }

        _session.Submit(new BuildRoomCommand(plan));
        _demoFurniture.Enqueue("screen");
        _demoFurniture.Enqueue("crash_trolley");
        var area = new TileRect(plan.Area.X - 1, plan.Area.Y - 1, plan.Area.Width + 2, plan.Area.Height + 2);
        var tile = _hospitalTiles.Where(t => !area.Contains(t) && simulation.Map[t.X, t.Y].Has(TileFlags.Passable) && simulation.Map[t.X, t.Y].RoomId == 0)
            .OrderBy(t => Math.Abs(t.X - start.X) + Math.Abs(t.Y - start.Y)).ThenBy(t => t.Y).ThenBy(t => t.X).FirstOrDefault();
        var doctors = simulation.AvailableStaff(StaffClass.Doctor);
        var best = doctors.Count == 0 ? 0 : doctors.Select((profile, index) => (profile.Skill, index)).Max().index;
        _session.Submit(new HireStaffCommand(StaffClass.Doctor, best, tile));
    }

    /// <summary>Finds the valid room plan closest to <paramref name="near"/>, trying a few sizes and every door position.</summary>
    private RoomPlan? FindRoomSpot(LevelMap map, RoomDefinition room, TilePosition near)
    {
        var size = room.MinimumSize + 1;
        for (var radius = 0; radius < 20; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                    {
                        continue;
                    }

                    var area = new TileRect(near.X + dx, near.Y + dy, size, size);
                    if (RoomBuilder.CheckArea(map, room, area, _session!.Player) != RoomPlanProblem.None)
                    {
                        continue;
                    }

                    for (var i = 0; i < size; i++)
                    {
                        DoorPlacement[] doors =
                        [
                            new(new TilePosition(area.X + i, area.Bottom), EdgeOrientation.North),
                            new(new TilePosition(area.Right, area.Y + i), EdgeOrientation.West),
                            new(new TilePosition(area.X + i, area.Y), EdgeOrientation.North),
                            new(new TilePosition(area.X, area.Y + i), EdgeOrientation.West),
                        ];
                        foreach (var door in doors)
                        {
                            var plan = new RoomPlan(room.Id, area, door);
                            if (RoomBuilder.Check(map, Simulation!.Content, plan, _session.Player) == RoomPlanProblem.None)
                            {
                                return plan;
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private void ResetCamera()
    {
        if (Map is { } map && _session is not null)
        {
            // Each player starts looking at their own hospital.
            var start = map.GetCameraTile(Math.Min(_session.Player, map.PlayerCount - 1));
            _camera.Center = Camera.TileCenter(start.X, start.Y);
        }
    }

    private void ClampCamera()
    {
        if (Map is not { } map)
        {
            return;
        }

        _camera.Center = new Vector2(
            Math.Clamp(_camera.Center.X, -32f * map.Height, 32f * map.Width),
            Math.Clamp(_camera.Center.Y, 0f, 16f * (map.Width + map.Height)));
    }

    private void EnsureTarget(Point size)
    {
        if (_target is not null && _target.Width == size.X && _target.Height == size.Y)
        {
            return;
        }

        _target?.Dispose();
        _target = new RenderTarget2D(_device, size.X, size.Y);
    }

    /// <summary>Runs the session and the input-free test controls (wandering, demo furnishing).</summary>
    private void AdvanceSession(GameTime gameTime)
    {
        if (_session is null)
        {
            return;
        }

        _session.Update(gameTime.ElapsedGameTime);
        PrepareMapIfReady();
        FurnishDemoRoom();
        PlaceDemoReceptionDesk();
        BuildDemoGeneralDiagnosis();
        if (Simulation is not { } simulation || !_wander || _hospitalTiles.Count == 0 || _session.IsPaused)
        {
            return;
        }

        foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
        {
            // People with something to do (patients on a visit, staff at work) are left to it.
            if (humanoid.Behaviour is null && humanoid.Destination is null && humanoid.Id != _selected && _random.Next(60) == 0)
            {
                _session.Submit(new MeanderCommand(humanoid.Id));
            }
        }
    }

    /// <summary>
    /// CorsixTH's speed keys: 1 to 5 pick a speed, P or Space pause and unpause, Z held down speeds the
    /// game up and going back to the previous speed when let go. The speed also changes while building,
    /// as game time runs on in the original.
    /// </summary>
    private void UpdateSpeed(KeyboardState keys, Func<Keys, bool> pressed)
    {
        if (_session is not { CanChangeSpeed: true } session)
        {
            return;
        }

        var control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        (Keys Key, GameSpeed Speed)[] speedKeys =
        [
            (Keys.D1, GameSpeed.Slowest), (Keys.D2, GameSpeed.Slower), (Keys.D3, GameSpeed.Normal),
            (Keys.D4, GameSpeed.MaxSpeed), (Keys.D5, GameSpeed.AndThenSomeMore),
        ];
        foreach (var (key, speed) in speedKeys)
        {
            if (!control && pressed(key))
            {
                session.SetSpeed(speed);
            }
        }

        // While building, P is the pick-up key.
        if (pressed(Keys.Space) || (pressed(Keys.P) && !_buildTool.IsActive))
        {
            if (session.IsPaused)
            {
                session.ResumeSpeed();
            }
            else
            {
                session.SetSpeed(GameSpeed.Pause);
            }
        }

        if (keys.IsKeyDown(Keys.Z))
        {
            session.SetSpeed(GameSpeed.SpeedUp);
        }
        else if (session.Speed == GameSpeed.SpeedUp)
        {
            session.ResumeSpeed();
        }
    }

    private void UpdateSimulation(GameTime gameTime, MouseState mouse, KeyboardState keys, Func<Keys, bool> pressed)
    {
        AdvanceSession(gameTime);
        if (_session is null || Simulation is not { } simulation)
        {
            return;
        }

        UpdateSpeed(keys, pressed);

        if (pressed(Keys.R))
        {
            _wander = !_wander;
        }

        // Until the watch window exists, Shift+O opens the hospital early, as clicking the watch's button does.
        if (pressed(Keys.O) && (keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift)) && _session.Player < simulation.Hospitals.Count && !simulation.Hospitals[_session.Player].IsOpen)
        {
            _session.Submit(new OpenHospitalCommand());
        }

        // Until the build window exists: B builds a GP's office, Shift+B a pharmacy, Ctrl+B general diagnosis.
        var wasBuilding = _buildTool.IsActive;
        var shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        var ctrl = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);

        // Until the bank manager exists: L borrows 5,000, Shift+L pays 5,000 back; with Ctrl as much as allowed.
        if (pressed(Keys.L))
        {
            _session.Submit(shift ? new RepayCommand(ctrl) : new BorrowCommand(ctrl));
        }

        // Until the town map exists, T and Shift+T turn the heating up and down.
        // F2 and F8 open the bank statement and the graphs in CorsixTH; here they toggle their figures as text.
        if (pressed(Keys.F2))
        {
            _moneyPanel = _moneyPanel == MoneyPanel.Statement ? MoneyPanel.None : MoneyPanel.Statement;
        }

        if (pressed(Keys.F8))
        {
            _moneyPanel = _moneyPanel == MoneyPanel.Graphs ? MoneyPanel.None : MoneyPanel.Graphs;
        }

        if (pressed(Keys.T))
        {
            var hospital = simulation.Hospitals[_session.Player];
            if (hospital.IsHeatingBroken)
            {
                ShowNotice("The boiler is broken: the heating cannot be set until it is repaired");
            }
            else
            {
                var heat = Math.Clamp(hospital.RadiatorHeat + (shift ? -1 : 1), Hospital.MinRadiatorHeat, Hospital.MaxRadiatorHeat);
                _session.Submit(new SetRadiatorHeatCommand(heat));
                ShowNotice($"Heating: {heat * 10} %");
            }
        }

        // Until the town map exists, N buys the parcel under the cursor.
        if (pressed(Keys.N) && _hoveredTile is { } landAt)
        {
            var parcel = simulation.Map[landAt.X, landAt.Y].Parcel;
            var price = simulation.ParcelPrice(parcel);
            if (!simulation.IsParcelPurchasable(parcel, _session.Player))
            {
                ShowNotice($"Parcel {parcel} is not for sale");
            }
            else if (price > simulation.Hospitals[_session.Player].Balance)
            {
                ShowNotice($"Not enough money for parcel {parcel}: it costs {MoneyText.Format(price)}");
            }
            else
            {
                _session.Submit(new PurchaseParcelCommand(parcel));
            }
        }

        var roomToBuild = shift ? "pharmacy" : ctrl ? "general_diag" : "gp";
        if (pressed(Keys.B) && !_buildTool.IsActive && simulation.Content.FindRoom(roomToBuild) is { } roomType)
        {
            // As in the original, a room can only be started once discovered and with its whole cost in the bank.
            if (!RoomPurchases.CanBuild(simulation, roomType, _session.Player))
            {
                ShowNotice(simulation.Rules.IsRoomAvailable(roomType)
                    ? $"The {roomType.Name} has not been researched yet"
                    : $"There is no {roomType.Name} on this level");
            }
            else if (RoomPurchases.CanAfford(simulation, roomType, _session.Player))
            {
                _buildTool.Start(roomType);
            }
            else
            {
                ShowNotice($"Not enough money for a {roomType.Name}: it costs {MoneyText.Format(simulation.Rules.BuildCost(roomType))}");
            }
        }

        var back = _buildBack;
        _buildBack = false;
        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            _rightPressedAt = new Point(mouse.X, mouse.Y);
        }

        if (pressed(Keys.F) && !_buildTool.IsActive && !_furnishTool.IsActive
            && simulation.Content.FindObject(ReceptionDesks.ObjectId) is { } receptionDesk)
        {
            _furnishTool.Start(receptionDesk);
        }
        else if (pressed(Keys.Tab) && _furnishTool.IsActive)
        {
            // Until the furnish corridor window exists, Tab (Shift+Tab) goes through the corridor objects.
            _furnishTool.Cycle(simulation.Content, backwards: shift);
        }

        if (_buildTool.IsActive || _furnishTool.IsActive)
        {
            // While building, the mouse belongs to the build tool; a right click (not a drag, which pans) turns objects.
            var leftDown = mouse.LeftButton == ButtonState.Pressed;
            var leftWasDown = _previousMouse.LeftButton == ButtonState.Pressed;
            var rightReleased = mouse.RightButton == ButtonState.Released && _previousMouse.RightButton == ButtonState.Pressed;
            var rightClicked = rightReleased && Math.Abs(mouse.X - _rightPressedAt.X) + Math.Abs(mouse.Y - _rightPressedAt.Y) < 6;
            var input = new BuildInput(
                _hoveredTile is { } point ? new TilePosition(point.X, point.Y) : null,
                _hoveredFraction.X,
                _hoveredFraction.Y,
                leftDown,
                leftDown && !leftWasDown,
                !leftDown && leftWasDown,
                rightClicked,
                pressed(Keys.Enter),
                back,
                pressed(Keys.P));
            var tool = _buildTool.IsActive ? _buildTool.Update(simulation, _session.Player, input) : _furnishTool.Update(simulation, _session.Player, input);
            if (tool is { } command)
            {
                _session.Submit(command);
            }

            if (wasBuilding && (pressed(Keys.B) || pressed(Keys.H) || pressed(Keys.G)))
            {
                _buildReminderUntil = Environment.TickCount64 + NoticeMilliseconds;
            }

            return;
        }

        if (pressed(Keys.H) && _hoveredTile is { } spawnAt)
        {
            _session.Submit(new SpawnHumanoidCommand(new TilePosition(spawnAt.X, spawnAt.Y)));
        }

        // Debug hiring until the hire window exists: Ctrl+1 to Ctrl+4 hire the first doctor, nurse,
        // handyman or receptionist on the list and place them on the tile under the cursor. The plain
        // number keys set the game speed, as in CorsixTH.
        var control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        (Keys Key, StaffClass Class)[] hireKeys =
        [
            (Keys.D1, StaffClass.Doctor), (Keys.D2, StaffClass.Nurse), (Keys.D3, StaffClass.Handyman), (Keys.D4, StaffClass.Receptionist),
        ];
        foreach (var (key, staffClass) in hireKeys)
        {
            if (control && pressed(key) && _hoveredTile is { } hireAt)
            {
                var tile = new TilePosition(hireAt.X, hireAt.Y);
                var problem = StaffHiring.Check(simulation, staffClass, 0, tile, _session.Player);
                if (problem == StaffHireProblem.None)
                {
                    _session.Submit(new HireStaffCommand(staffClass, 0, tile));
                }
                else
                {
                    ShowNotice($"Cannot hire a {staffClass}: {problem}");
                }
            }
        }

        if (pressed(Keys.G) && _hospitalTiles.Count > 0)
        {
            for (var i = 0; i < 10; i++)
            {
                _session.Submit(new SpawnHumanoidCommand(_hospitalTiles[_random.Next(_hospitalTiles.Count)]));
            }
        }

        var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        if (clicked && _hoveredTile is { } target)
        {
            var tile = new TilePosition(target.X, target.Y);
            var here = simulation.Entities.OfType<Humanoid>().FirstOrDefault(h => h.Tile == tile || h.StepTarget == tile);
            if (here is not null)
            {
                _selected = here.Id;
            }
            else if (_selected is { } id)
            {
                _session.Submit(new WalkToCommand(id, tile));
            }
        }
    }

    private void CollectEntitiesByTile()
    {
        foreach (var list in _entitiesByTile.Values)
        {
            list.Clear();
        }

        if (Simulation is not { } simulation)
        {
            return;
        }

        // An object in use is drawn as part of its user's animation (CorsixTH's makeInvisible).
        _hiddenObjects.Clear();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { HiddenObjectId: not 0 and var hidden })
            {
                _hiddenObjects.Add(hidden);
            }
        }

        foreach (var entity in simulation.Entities)
        {
            // A room door is hidden too while someone goes through: their door animation shows it.
            if (entity is RoomObject or RoomDoor && _hiddenObjects.Contains(entity.Id))
            {
                continue;
            }

            // Someone shown by another's animation, such as a patient at the pharmacy cabinet.
            if (entity is Humanoid { IsHidden: true })
            {
                continue;
            }

            TilePosition? tile = entity switch
            {
                Humanoid humanoid => HumanoidDrawing.DrawTile(humanoid),
                EntranceDoor door => door.Tile,
                RoomDoor door => door.Tile,
                RoomObject placed => ObjectDrawing.DrawTile(simulation.Content, placed.ObjectType, placed.Tile, placed.Orientation),
                _ => null,
            };
            if (tile is not { } position)
            {
                continue;
            }

            if (!_entitiesByTile.TryGetValue(position, out var list))
            {
                list = [];
                _entitiesByTile[position] = list;
            }

            list.Add(entity);
        }

        // A receptionist stands on the desk's own tile. Facing north or west (away from the viewer) they are
        // drawn after the desk, otherwise behind it (CorsixTH's action_staff_reception_idle_phase).
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: ReceptionistBehaviour { Phase: ReceptionistPhase.Staffing } staffing } receptionist
                && simulation.FindEntity(staffing.DeskId) is RoomObject desk
                && _entitiesByTile.TryGetValue(receptionist.Tile, out var list) && list.Remove(desk))
            {
                var index = list.IndexOf(receptionist);
                list.Insert(receptionist.Facing is Direction.North or Direction.West ? index : index + 1, desk);
            }
        }
    }

    private void DrawEntities(SpriteBatch batch, TilePosition tile, int anchorX, int anchorY)
    {
        if (Simulation is not { } simulation || !_entitiesByTile.TryGetValue(tile, out var list))
        {
            return;
        }

        foreach (var entity in list)
        {
            switch (entity)
            {
                case EntranceDoor door:
                    _doors.Draw(batch, door, anchorX, anchorY, simulation.Tick);
                    break;
                case Humanoid humanoid:
                    _humanoids.Draw(batch, humanoid, anchorX, anchorY, simulation.Tick);
                    break;
                case RoomObject placed:
                    var tint = placed.Id == _buildTool.HighlightedObject ? PickUpTint : Color.White;
                    _objects.Draw(batch, placed.ObjectType, placed.Tile, placed.Orientation, simulation.Tick, tint, placed.HangingClothes);
                    break;
                case RoomDoor roomDoor:
                    var animation = roomDoor.Edge == EdgeOrientation.North ? RoomDoorNorth : RoomDoorWest;
                    _animations.DrawFrame(batch, _animations.Animations.FirstFrame(animation), anchorX, anchorY, NoLayers, flipHorizontally: false);
                    break;
            }
        }
    }

    private void DrawOverlay(SpriteBatch batch, LevelMap map, Rectangle view)
    {
        // While building, the blueprint and the object being placed show where the cursor is, as in the original.
        var hovered = _buildTool.IsActive || _furnishTool.IsActive ? null : _hoveredTile;
        var (firstScanline, lastScanline) = MapRenderer.VisibleScanlines(map, view);
        for (var s = firstScanline; s <= lastScanline; s++)
        {
            var (firstX, lastX) = MapRenderer.VisibleColumns(map, view, s);
            for (var x = firstX; x <= lastX; x++)
            {
                var y = s - x;
                var tint = hovered == new Point(x, y) ? HoverTint : OverlayTint(map[x, y], x, y);
                if (tint != Color.Transparent)
                {
                    batch.Draw(_diamond, new Vector2((32 * (x - y)) - 32, 16 * (x + y)), tint);
                }
            }
        }
    }

    private Color OverlayTint(MapTile tile, int x, int y)
    {
        return _overlay switch
        {
            MapOverlay.Temperature => tile.Has(TileFlags.Hospital) && Simulation is { } simulation
                ? TemperatureColor(simulation.Temperatures[x, y]) * 0.6f
                : Color.Transparent,
            MapOverlay.Passable => tile.Has(TileFlags.Passable) ? new Color(0, 200, 0) * 0.4f : new Color(200, 0, 0) * 0.4f,
            MapOverlay.Hospital => tile.Has(TileFlags.Buildable) ? new Color(0, 120, 255) * 0.45f
                : tile.Has(TileFlags.Hospital) ? new Color(255, 200, 0) * 0.45f
                : Color.Transparent,
            MapOverlay.Parcels => tile.Parcel == 0 ? Color.Transparent : ParcelColor(tile.Parcel) * 0.45f,
            _ => Color.Transparent,
        };
    }

    /// <summary>
    /// The town map's blue, green and red warmth colours (CorsixTH's th_lua_ui.cpp, <c>temperature_theme::multi_colour</c>):
    /// blue below 11 °C, green up to 18 °C, red above.
    /// </summary>
    private static Color TemperatureColor(ushort temperature)
    {
        // 4 °C and below is 0, 25 °C and above 255.
        var scaled = temperature < 5200 ? 0 : temperature > 32767 ? 255 : (temperature - 5200) / 108;
        const int MinOk = 140;
        const int MaxOk = 180;
        return scaled < MinOk ? new Color(0, 0, Channel(0, MinOk - 1, scaled, 200, 60))
            : scaled < MaxOk ? new Color(0, Channel(MinOk, MaxOk - 1, scaled, 140, 224), 70)
            : new Color(Channel(MaxOk, 255, scaled, 224, 255), 0, 70);

        // map_color_channel in th_lua_ui.h.
        static int Channel(int low, int high, int value, int start, int end) =>
            value <= low ? start : value >= high ? end : start + ((end - start) * (value - low) / (high - low));
    }

    private static Color ParcelColor(int parcel)
    {
        // Spread hues so that neighbouring parcel numbers differ visibly.
        var hue = (parcel * 0.618034f) % 1f;
        return HueToColor(hue);
    }

    private static Color HueToColor(float hue)
    {
        var h = hue * 6f;
        var x = 1f - MathF.Abs((h % 2f) - 1f);
        var (r, g, b) = (int)h switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
        return new Color(r, g, b);
    }

    private void ShowNotice(string text)
    {
        _notice = text;
        _noticeUntil = Environment.TickCount64 + NoticeMilliseconds;
    }

    private string StatusLine()
    {
        if (_session is null)
        {
            return "No game.";
        }

        var simulation = Simulation;
        var money = simulation is not null && _session.Player < simulation.Hospitals.Count
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"   {MoneyText.Format(simulation.Hospitals[_session.Player].Balance)}   cured {simulation.Hospitals[_session.Player].Cured}   died {simulation.Hospitals[_session.Player].Deaths}   reputation {simulation.Hospitals[_session.Player].Reputation}   value {MoneyText.Format(simulation.Hospitals[_session.Player].Value)}{LoanText(simulation.Hospitals[_session.Player])}{OutcomeText(simulation.Hospitals[_session.Player])}")
            : string.Empty;

        static string LoanText(Hospital hospital) => hospital.Loan > 0 ? $"   loan {MoneyText.Format(hospital.Loan)}" : string.Empty;

        static string OutcomeText(Hospital hospital) => hospital.Outcome switch
        {
            EndOutcome.Won => "   LEVEL WON",
            EndOutcome.Lost => $"   LEVEL LOST ({hospital.LostTo?.Criterion})",
            _ => string.Empty,
        };

        var levels = _levels.Count > 0 ? $"   ({_levelIndex + 1} of {_levels.Count})" : string.Empty;
        var people = Simulation?.Entities.OfType<Humanoid>().Count() ?? 0;
        var levelKeys = _levels.Count > 0 ? "   PgUp PgDn: level" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_session.Name}{levels}{money}   {_session.Status}   {people} people{(_wander ? " wandering" : string.Empty)}   overlay {_overlay}   zoom x{_camera.Zoom:0.00}   upscale x{_blocks.Scale}{levelKeys}   O: overlay   U: upscale   Esc: quit");
    }

    /// <summary>
    /// The date and the game speed in a box in the top right corner, standing in for the date on the
    /// original's bottom panel until that panel exists.
    /// </summary>
    private void DrawDatePanel(SpriteBatch batch, Viewport viewport)
    {
        if (_session is null || Simulation is not { } simulation)
        {
            return;
        }

        var date = DateText(simulation.Date);
        if (_session.Player < simulation.Hospitals.Count && simulation.Hospitals[_session.Player] is { IsOpen: false } hospital)
        {
            // Stands in for CorsixTH's opening watch; whole days rounded up.
            var days = (hospital.HoursUntilOpen + GameDate.HoursPerDay - 1) / GameDate.HoursPerDay;
            date += string.Create(CultureInfo.InvariantCulture, $", opens in {days} days (Shift+O: now)");
        }

        var speed = GameSpeeds.Name(_session.Speed);
        var width = Math.Max(_text.Measure(date).Width, _text.Measure(speed).Width) + 16;
        var box = new Rectangle(viewport.Width - width, 0, width, StatusHeight);
        batch.Draw(_pixel, box, DatePanelColor);
        _text.Draw(batch, date, new Vector2(box.X + 8, 4));
        _text.Draw(batch, speed, new Vector2(box.X + 8, 4 + _text.LineHeight + 2));
    }

    private void DrawMoneyPanel(SpriteBatch batch)
    {
        if (_moneyPanel == MoneyPanel.None || _session is not { Simulation: { } simulation } session || session.Player >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[session.Player];
        var lines = (_moneyPanel == MoneyPanel.Statement ? MoneyPanels.Statement(hospital) : MoneyPanels.Graphs(hospital)).ToList();
        var lineHeight = _text.LineHeight + 2;
        var width = lines.Max(line => _text.Measure(line).Width) + 16;
        var box = new Rectangle(8, StatusHeight + 8, width, (lines.Count * lineHeight) + 12);
        batch.Draw(_pixel, box, MoneyPanelColor);
        for (var i = 0; i < lines.Count; i++)
        {
            _text.Draw(batch, lines[i], new Vector2(box.X + 8, box.Y + 6 + (i * lineHeight)));
        }
    }

    /// <summary>The date as the original's bottom panel shows it (day and month), with the year.</summary>
    private static string DateText(GameDate date)
    {
        string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        return string.Create(CultureInfo.InvariantCulture, $"{date.Day} {months[date.Month - 1]} year {date.Year}");
    }

    private string TileLine()
    {
        if (_buildTool.IsActive)
        {
            var reminder = Environment.TickCount64 < _buildReminderUntil ? " (finish or cancel the room to use other keys)" : string.Empty;
            return $"BUILD MODE{reminder}   {_buildTool.Status(Simulation)}";
        }

        if (_furnishTool.IsActive)
        {
            return $"BUILD MODE   {_furnishTool.Status(Simulation)}";
        }

        if (_notice is not null && Environment.TickCount64 < _noticeUntil)
        {
            return _notice;
        }

        if (_hoveredTile is not { } position || Map is not { } map)
        {
            var pause = _session?.CanChangeSpeed == true ? "   Space P: pause   1-5: speed   Z: hold to speed up" : string.Empty;
            return $"Pan: arrows WASD right drag   Wheel: zoom   B: build GP office (Shift: pharmacy, Ctrl: general diagnosis)   F: buy reception desk   H: spawn   G: spawn 10   Ctrl+1-4: hire   Click: select or walk   R: wander{pause}";
        }

        var tile = map[position.X, position.Y];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Tile {position.X} {position.Y}   floor {tile.Floor}   north {tile.NorthWall}   west {tile.WestWall}   parcel {tile.Parcel}{(tile.RoomId != 0 ? $"   room {tile.RoomId}" : string.Empty)}   {DescribeFlags(tile)}{(_selected is { } id ? $"   selected {id}" : string.Empty)}");
    }

    private static string DescribeFlags(MapTile tile)
    {
        var parts = new List<string>();
        if (tile.Has(TileFlags.Passable))
        {
            parts.Add("passable");
        }

        if (tile.Has(TileFlags.Hospital))
        {
            parts.Add("hospital");
        }

        if (tile.Has(TileFlags.Buildable))
        {
            parts.Add("buildable");
        }

        return string.Join(' ', parts);
    }

    /// <summary>The outline of a tile's floor diamond, two pixels wide across and one down.</summary>
    private static Texture2D CreateCellOutline(GraphicsDevice device)
    {
        const int width = 64;
        const int height = 32;
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var distance = (MathF.Abs(x + 0.5f - 32f) / 32f) + (MathF.Abs(y + 0.5f - 16f) / 16f);
                pixels[(y * width) + x] = distance <= 1f && distance > 1f - (2f / 32f) ? Color.White : Color.Transparent;
            }
        }

        var texture = new Texture2D(device, width, height);
        texture.SetData(pixels);
        return texture;
    }

    private static Texture2D CreateDiamond(GraphicsDevice device)
    {
        const int width = 64;
        const int height = 32;
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = (MathF.Abs(x + 0.5f - 32f) / 32f) + (MathF.Abs(y + 0.5f - 16f) / 16f) <= 1f;
                pixels[(y * width) + x] = inside ? Color.White : Color.Transparent;
            }
        }

        var texture = new Texture2D(device, width, height);
        texture.SetData(pixels);
        return texture;
    }
}

/// <summary>Tile information drawn over the map.</summary>
internal enum MapOverlay
{
    None,
    Passable,
    Hospital,
    Parcels,
    Temperature,
}

/// <summary>Which money panel <see cref="MapViewScreen"/> shows.</summary>
internal enum MoneyPanel
{
    None,
    Statement,
    Graphs,
}
