using System.Globalization;
using CorsixNet.Client.Audio;
using CorsixNet.Client.Input;
using CorsixNet.Client.Rendering;
using CorsixNet.Client.Sessions;
using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content;
using CorsixNet.Content.Interface;
using CorsixNet.Content.Languages;
using CorsixNet.Content.Levels;
using CorsixNet.Content.Saves;
using CorsixNet.Content.Settings;
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
/// Keys: arrows/WASD, screen edges or right/middle-drag pan, wheel or =/− zoom, 0 original size, Alt+digit remember
/// the view and Ctrl+digit go back to it, Home reset camera,
/// F3 list of levels to pick from (local games), O cycle overlay, U cycle sprite upscaling, Esc close the front window or open the menu (save, load, options, quit).
/// Clicking a member of staff, a patient, a room or a desk opens their window; C faxes, Y hire staff, F4 town map.
/// F1 bank manager, F2 bank statement, Shift+F3 staff management, F5 casebook, F7 progress report, F11 cheats, F6 research, F8 graphs, F9 policy, F10 machines (windows, see <see cref="WindowLayer"/>).
/// Simulation test controls: H spawn a patient at the cursor, G spawn ten in the hospital,
/// left click select a patient or send the selected one walking, R toggle random wandering,
/// Space or P pause, 1 to 5 game speed, Z held speeds up (local games), Ctrl+1 to Ctrl+4 hire staff. B builds a GP's office: blueprint, door, windows, then furniture
/// (right click turns an object, P picks one up, Enter confirms, Esc steps back). Alt+Shift+S saves a local game to
/// the quick save and Alt+Shift+L loads it; the game also saves itself each month. In a network game, commands go
/// through the server.
/// </remarks>
internal sealed class MapViewScreen : IScreen, IWindowHost
{
    /// <summary>The most interface ticks of scrolling (18 ms each) one frame catches up on.</summary>
    private const int MaxScrollTicksPerFrame = 10;

    /// <summary>UIPlaceStaff takes the tile under a point this many screen pixels below the cursor.</summary>
    private const int StaffCursorDrop = 14;

    /// <summary>A member of staff who cannot be put down where they are shown is grey (the ghost palette).</summary>
    private static readonly Color StaffBlockedTint = new Color(130, 130, 130);

    /// <summary>How far an earthquake at full strength moves the view, in map pixels (CorsixTH's <c>shake_screen_max_movement</c>).</summary>
    private const float ShakeMaxMovement = 50f;

    private static readonly Color Background = Color.Black;
    private static readonly Color HoverTint = new Color(255, 255, 255) * 0.35f;
    private static readonly byte[] NoLayers = [];
    private static readonly Color PickUpTint = new(150, 200, 255);
    private static readonly Color CellOutlineColor = new(232, 0, 0);

    /// <summary>The surgeon screen with two green outfits (2772), one green and one white (2776), or two white (2774).</summary>
    private const int SurgeonScreenGreens = 2772;
    private const int SurgeonScreenBoth = 2776;
    private const int SurgeonScreenWhites = 2774;

    /// <summary>The gear icon over a machine waiting for its repair (machine.lua, the only icon for machinery).</summary>
    private const int RepairGearIcon = 4564;

    /// <summary>The X-ray viewer's layers while lit: layer 11 shows its second variant (<c>setLayer(11, 2)</c>).</summary>
    private static readonly byte[] XRayViewerLit = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2];

    /// <summary>Colour of the status bar and the frame around the map while building, the blue of the blueprints.</summary>
    private static readonly Color BuildModeColor = new(0, 48, 144);

    /// <summary>Background of the date and speed box.</summary>
    private static readonly Color DatePanelColor = new(40, 40, 40);
    private static readonly Color MoneyPanelColor = new(0, 0, 0, 200);

    private static readonly Color RoomListHover = new(60, 90, 160, 220);

    private static readonly Color RoomListHoverBlocked = new(90, 60, 60, 220);

    private static readonly Color RoomListBlocked = new(150, 150, 150);

    private static readonly Color LevelListCurrent = new(255, 220, 120);

    /// <summary>Width of the frame drawn around the map while building.</summary>
    private const int BuildFrameWidth = 3;

    /// <summary>How long a notice such as "not enough money" stays in the status line.</summary>
    private const int NoticeMilliseconds = 4000;

    /// <summary>The emergency helicopter (CorsixTH's objects/helicopter.lua).</summary>
    private const int HelicopterAnimation = 2456;

    /// <summary>Room door animations from CorsixTH's <c>objects/door.lua</c>.</summary>
    private const int RoomDoorNorth = 104;

    /// <summary>The rat hole in a north wall (objects/rathole.lua).</summary>
    private const int RatholeAnimation = 1904;

    /// <summary>The rat's animations (entities/rat.lua).</summary>
    private const int RatWalkNorth = 1908;
    private const int RatWalkEast = 1912;
    private const int RatWalkSouth = 1916;
    private const int RatWalkWest = 1920;
    private const int RatEnterNorthHole = 1924;
    private const int RatEnterWestHole = 1926;
    private const int RatLeaveHole = 1928;
    private const int RoomDoorWest = 106;

    /// <summary>Swing doors standing: the left one in a north (1996) or west (1998) wall, the right one in a north wall (2006).</summary>
    private const int SwingDoorLeftNorth = 1996;
    private const int SwingDoorLeftWest = 1998;
    private const int SwingDoorRightNorth = 2006;

    private readonly GraphicsDevice _device;
    private IReadOnlyList<LevelSource> _levels;
    private MainMenuWindow? _mainMenu;
    private readonly SpriteAtlas _blocks;
    private readonly BlueprintDrawing _blueprints;
    private readonly MapRenderer _renderer;
    private readonly TextRenderer _text;
    private UnicodeFonts _unicodeFonts;
    private readonly Texture2D _diamond;
    private readonly Texture2D _cellOutline;
    private readonly Texture2D _pixel;
    private readonly Camera _camera = new();
    private readonly AnimationRenderer _animations;
    private readonly HumanoidDrawing _humanoids;
    private readonly EntranceDoorDrawing _doors;
    private readonly Dictionary<TilePosition, List<Entity>> _entitiesByTile = [];

    /// <summary>Entities drawn with their tile's north wall (CorsixTH's early list), by tile.</summary>
    private readonly Dictionary<TilePosition, List<Entity>> _earlyByTile = [];

    /// <summary>The tiles of placed objects of the early list, whose users are drawn early too.</summary>
    private readonly HashSet<TilePosition> _earlyObjectTiles = [];

    /// <summary>Objects whose second part (CorsixTH's slave object) is drawn on the tile.</summary>
    private readonly Dictionary<TilePosition, List<RoomObject>> _slavePartsByTile = [];

    /// <summary>Parts of objects drawn split over several tiles, and of their users' animations, by the tile drawing them.</summary>
    private readonly Dictionary<TilePosition, List<(Entity Entity, int Left, int Right)>> _splitPartsByTile = [];
    private readonly BuildRoomTool _buildTool = new();
    private readonly FurnishCorridorTool _furnishTool = new();

    /// <summary>The map editor's cursor while the editor is open (<c>UIMapEditor</c>), or null.</summary>
    private MapEditorTool? _editorTool;

    /// <summary>The map file the editor last loaded or saved, or null for a new map.</summary>
    private string? _editorPath;
    private readonly HashSet<int> _hiddenObjects = [];
    private readonly ObjectDrawing _objects;
    private readonly FloatingDollarDrawing _floatingDollars;

    /// <summary>The original's cursors (DATA/MPointer), or null if the sheet could not be loaded.</summary>
    private readonly CursorDrawing? _cursors;

    /// <summary>Whether the mouse was over a window at the last update.</summary>
    private bool _cursorOverWindow;

    /// <summary>Objects the demo still has to place in its GP's office, one at a time.</summary>
    private readonly Queue<string> _demoFurniture = new();
    private int _demoFurnitureExpected;
    private readonly List<TilePosition> _hospitalTiles = [];

    // Test controls choose targets at random; the choices reach the simulation as commands.
    private readonly Random _random = new();

    private IGameSession? _session;

    /// <summary>The saves folder: the quick save and the monthly autosaves.</summary>
    private readonly SaveGameStore _saves = new(SaveGameStore.DefaultDirectory());
    private int _levelIndex;

    private string? _error;
    private MapOverlay _overlay;
    private RenderTarget2D? _target;
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private Point? _hoveredTile;

    /// <summary>The tile a member of staff being hired or carried goes on: 14 screen pixels below the cursor's, as
    /// <c>UIPlaceStaff:onCursorWorldPositionChange</c> takes it.</summary>
    private Point? _staffTile;
    private (int X, int Y) _hoveredFraction;
    private Point _rightPressedAt;
    private bool _buildBack;

    /// <summary>The windows open over the map (bank, casebook, research, policy, ...).</summary>
    private readonly WindowLayer _windows;

    /// <summary>A button of the room edit panel pressed, for the build tool's next step.</summary>
    private BuildButton? _buildButton;

    /// <summary>The candidate for hire on the cursor, to be placed with a click.</summary>
    private (StaffClass Class, StaffProfile Profile)? _hiring;

    /// <summary>The year when last looked, so that a new one opens the annual report.</summary>
    private int? _lastYearSeen;

    /// <summary>The level's outcome when last looked, so that winning or losing opens the level's end.</summary>
    private EndOutcome _lastOutcome;

    /// <summary>Whether a countdown ran when last looked, so that a new one opens the watch.</summary>
    private bool _watchRan;

    /// <summary>The faxes waiting when last looked, so that a new one opens the faxes window.</summary>
    private HashSet<string> _seenFaxes = new(StringComparer.Ordinal);

    /// <summary>Announces machines that start needing a repair urgently (CorsixTH's <c>announceRepair</c>).</summary>
    private readonly RepairAnnouncer _repairs = new();

    private readonly HospitalAnnouncements _announcements = new(new Random());

    private readonly WorldSounds _worldSounds = new(new Random());

    private readonly Announcer _announcer = new(new Random());

    private readonly GatesToHellSounds _hellSounds = new();

    private readonly FrameSounds _frameSounds = new();

    private object? _repairSession;

    /// <summary>Whether the list of levels is open (F3), a stand-in for CorsixTH's new game and custom level windows.</summary>
    private bool _levelList;

    /// <summary>The level list's rows on screen, from the last time it was drawn, for clicks.</summary>
    private readonly List<(Rectangle Row, int Index)> _levelListRows = [];

    /// <summary>Whether the list of rooms to build is open (B), a stand-in for CorsixTH's build room window.</summary>
    private bool _roomList;

    /// <summary>The list's room rows on screen, from the last time it was drawn, for clicks.</summary>
    private readonly List<(Rectangle Row, RoomDefinition Room)> _roomListRows = [];
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
    private bool _demoMachinePending;
    private bool _demoToiletsPending;
    private int? _selected;

    /// <summary>The member of staff picked up with M, to be put down with M again.</summary>
    private int? _pickedUpStaff;
    private LevelMap? _preparedMap;
    private string? _capturePath;

    /// <summary>The player's settings.</summary>
    private GameConfig _config;

    /// <summary>Where the settings are saved, or null to leave the file alone.</summary>
    private readonly string? _configPath;

    /// <summary>The keys for the dialogs and other actions, from <c>hotkeys.txt</c> next to the settings.</summary>
    private Hotkeys _hotkeys;

    /// <summary>The game install, for reloading the sounds when the options change.</summary>
    private readonly ThemeHospitalInstall _install;

    /// <summary>The game's texts in the installed language, or null if no string file loads.</summary>
    private OriginalTexts? _texts;
    private readonly Adviser _adviser = new();
    private readonly AdviserFigure _adviserFigure = new();

    /// <summary>The original's bottom panel, or null where its graphics could not be read (the text panel then).</summary>
    private readonly OriginalBottomPanel? _bottomPanel;

    /// <summary>The fullscreen dialogs' graphics, loaded as they are first shown.</summary>
    private readonly OriginalGraphics _graphics;

    /// <summary>The bottom panel's sprites (<c>Data/Panel02V</c>), for the adviser's balloon.</summary>
    private readonly SpriteAtlas? _panelSprites;

    /// <summary>The black font of the adviser's balloon (<c>QData/Font50V</c>).</summary>
    private readonly TextRenderer? _blackText;

    /// <summary>The tutorial of the first level, when the new game dialog asked for it.</summary>
    private Tutorial? _tutorial;

    /// <summary>The game the tutorial was started with.</summary>
    private IGameSession? _tutorialSession;
    private readonly Random _adviserRandom = new();
    private AdviserChecks _adviserChecks;
    private object? _adviserSession;
    private readonly HashSet<string> _roomsDescribed = new(StringComparer.Ordinal);
    private bool _tipShown;
    private bool _pickingRoomToEdit;

    /// <summary>The original's sound effects, silent when they are turned off or cannot be played.</summary>
    private SoundEffects _sounds;

    /// <summary>Scrolling and zooming the view as CorsixTH does, with the player's speeds.</summary>
    private MapScrolling _scrolling;

    /// <summary>Views remembered with Alt+digit, by digit (CorsixTH's <c>recallpositions</c>).</summary>
    private readonly (Vector2 Center, float Zoom)?[] _recallPositions = new (Vector2 Center, float Zoom)?[10];

    /// <summary>Time not yet scrolled for, less than an interface tick.</summary>
    private double _scrollMilliseconds;

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
        GameSpeed speed = GameSpeed.Normal,
        string? loadPath = null,
        GameConfig? config = null,
        string? configPath = null,
        bool mainMenu = false,
        bool mapEditor = false)
    {
        _config = config ?? new GameConfig();
        _adviser.IsEnabled = !_config.AdviserDisabled;
        _adviserChecks = new AdviserChecks(_adviser, _adviserRandom);
        _configPath = configPath;
        _hotkeys = LoadHotkeys(configPath);
        _install = install;
        _device = device;
        _initialSpeed = speed;
        _levels = networkSession is null ? levels : [];

        var palette = install.LoadPalette("DATA", "MPALETTE.DAT");
        // Floor tiles are the 64x32 diamonds of the block sheet.
        _blocks = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "VBLK-0", complex: false), palette, upscale, sprite => sprite.Width == 64 && sprite.Height == 32);
        _renderer = new MapRenderer(_blocks);
        _text = new TextRenderer(device, new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT01V", complex: false)), palette);
        _windows = new WindowLayer(text => _text.Measure(text).Width, () => _text.LineHeight) { Origin = new Point(8, StatusHeight + 8) };
        try
        {
            _bottomPanel = new OriginalBottomPanel(device, install, palette);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            // Without the panel's graphics, the text panel stands in.
            _bottomPanel = null;
            _windows.Open(new BottomPanel(BottomPanelAction));
        }

        InputScript.ButtonFinder = (text, nth) => (nth <= 1 && _bottomPanel is not null ? OriginalBottomPanel.FindButton(_device.Viewport, text) : null) ?? _windows.FindButton(text, nth);
        var sprites = install.LoadSpriteSheet("DATA", "VSPR-0", complex: false);
        _animations = new AnimationRenderer(device, AnimationSet.Load(install), sprites, palette, upscale);
        _blueprints = new BlueprintDrawing(device, install, _blocks, _animations, sprites, palette);
        _humanoids = new HumanoidDrawing(_animations) { FrameDrawn = (key, frame, x, y) => _frameSounds.Drawn(_animations.Animations, key, frame, x, y) };
        _doors = new EntranceDoorDrawing(_animations);
        _objects = new ObjectDrawing(_animations) { FrameDrawn = (key, frame, x, y) => _frameSounds.Drawn(_animations.Animations, key, frame, x, y) };
        _floatingDollars = new FloatingDollarDrawing(device, install, palette, upscale);
        try
        {
            _cursors = new CursorDrawing(device, install, palette);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            _cursors = null;
        }
        try
        {
            _panelSprites = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "PANEL02V", complex: true), palette);
            _blackText = new TextRenderer(device, new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT50V", complex: false)), palette);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            _panelSprites = null;
            _blackText = null;
        }

        _graphics = new OriginalGraphics(device, install);
        Ui.StaffFaces.Current = Ui.StaffFaces.Load(device, install, palette);
        _unicodeFonts = new UnicodeFonts(_config.UnicodeFont, AppContext.BaseDirectory);
        _texts = LoadTexts(install, _config);
        ApplyLanguageFont();
        _sounds = SoundEffects.Load(install, _config);
        _scrolling = new MapScrolling(_config.ScrollSettings());
        _buildTool.PlaySound = name => _sounds.Play(name);
        _buildTool.AbandonRequested = () => _windows.Open(new ConfirmWindow(Confirmation(5, "Are you sure you want to delete this room?"), () =>
        {
            _session?.Submit(new AbandonRebuildCommand());
            _buildTool.Stop();
        }));
        _furnishTool.PlaySound = name => _sounds.Play(name);
        _diamond = CreateDiamond(device);
        _cellOutline = CreateCellOutline(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);

        _overlay = overlay;
        _demoPending = demo;
        _demoDeskPending = demo;
        _demoDiagnosisPending = demo;
        _demoMachinePending = demo;
        _demoToiletsPending = demo;
        if (demo)
        {
            foreach (var item in (string[])["desk", "cabinet", "chair", .. DemoRadiators])
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
            if (loadPath is not null)
            {
                LoadGame(loadPath);
            }
        }

        PrepareMapIfReady();
        _camera.ZoomAt(zoom, new Point(ViewSize.X / 2, ViewSize.Y / 2), ViewSize);
        if (mapEditor)
        {
            StartMapEditor(MapEditor.Blank(), null);
        }
        else if (mainMenu && _session is LocalSession)
        {
            OpenMainMenu();
        }
    }

    public bool ExitRequested { get; private set; }

    /// <summary>
    /// Saves the next rendered map view (without the status bar) to a PNG file. The off-screen
    /// buffer is used, so this works even when the window cannot be seen, e.g. on a locked screen.
    /// </summary>
    public void RequestCapture(string path) => _capturePath = path;

    private Simulation? Simulation => _session?.Simulation;

    private LevelMap? Map => _session?.Simulation?.Map;

    /// <summary>Lists the original levels in the game's LEVELS folder, in campaign order, at a difficulty.</summary>
    public static IReadOnlyList<LevelSource> OriginalLevels(ThemeHospitalInstall install, Difficulty difficulty = Difficulty.Medium)
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
            .Select(level => new LevelSource(level.Name, () => LevelMapLoader.LoadOriginal(install, level.Name, difficulty)))
            .ToList();
    }

    /// <summary>
    /// The difficulties whose level files are installed (<c>UINewGame</c>: medium always, easy and hard when their
    /// first level's settings file is there).
    /// </summary>
    public static IReadOnlyList<Difficulty> AvailableDifficulties(ThemeHospitalInstall install)
    {
        var folder = install.FindDirectory("LEVELS");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard }
            .Where(difficulty => difficulty == Difficulty.Medium || files.Contains($"{LevelRulesLoader.FilePrefix(difficulty)}01.SAM"))];
    }

    public void Update(GameTime gameTime, bool hasFocus)
    {
        if (!hasFocus)
        {
            // No input, but the game goes on.
            AdvanceSession(gameTime);
            return;
        }

        var keys = InputScript.Keyboard();
        var mouse = InputScript.Mouse();
        var viewSize = ViewSize;
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

        // A window waiting for a key (the hotkey assignment dialog) takes the keyboard: the game sees no keys then.
        var realKeys = keys;
        if (_windows.Windows.OfType<IKeyCaptureWindow>().FirstOrDefault(window => window.IsCapturing) is { } capture)
        {
            if (HotkeyInput.Captured(keys, _previousKeys) is { } captured)
            {
                capture.Capture(captured);
            }

            keys = default;
        }

        if (Pressed(Keys.Escape))
        {
            if (_hiring is not null)
            {
                _hiring = null;
            }
            else if (_pickingRoomToEdit)
            {
                _pickingRoomToEdit = false;
            }
            else if (_buildTool.IsFurnishing && _windows.Windows.LastOrDefault(window => !window.IsPinned) is RoomEditWindow)
            {
                // The edit panel is the furnishing itself (CorsixTH's UIEditRoom): closing it would only reopen it, so Esc
                // goes to the build tool, which drops an extra object from the cursor or takes the room down.
                _buildBack = true;
            }
            else if (_windows.Any)
            {
                _windows.CloseTop();
            }
            else if (_mainMenu is not null)
            {
                // esc_closes = false.
            }
            else if (_levelList)
            {
                _levelList = false;
            }
            else if (_roomList)
            {
                _roomList = false;
            }
            else if (_buildTool.IsActive || _furnishTool.IsActive)
            {
                _buildBack = true;
            }
            else if (_editorTool is not null)
            {
                OpenMapEditorWindow();
            }
            else if (IsWindowHostReady)
            {
                // CorsixTH's Esc shows the menu bar; quitting is on it.
                _windows.Open(new MenuWindow(MenuAction));
            }
            else
            {
                ExitRequested = true;
            }
        }

        // The level list (F3): a click on a level starts it; a click elsewhere, F3 or Esc closes the list.
        if (_levels.Count > 0 && Pressed(Keys.F3) && !keys.IsKeyDown(Keys.LeftShift) && !keys.IsKeyDown(Keys.RightShift))
        {
            _levelList = !_levelList;
            _roomList = false;
        }

        if (_levelList)
        {
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            {
                var chosen = _levelListRows.FirstOrDefault(row => row.Row.Contains(mouse.X, mouse.Y));
                _levelList = false;
                if (chosen.Row != Rectangle.Empty)
                {
                    SelectLevel(chosen.Index);
                }
            }

            _previousKeys = realKeys;
            _previousMouse = mouse;
            return;
        }

        UpdateAdviser();
        if (_tutorial is { IsActive: true } tutorial && TutorialView() is { } tutorialView)
        {
            tutorial.Update(tutorialView);
        }

        // Windows over the map take the mouse while it is over them: the map sees no clicks and no wheel then.
        var realMouse = mouse;
        if (ClickAdviser(mouse))
        {
            mouse = new MouseState(mouse.X, mouse.Y, mouse.ScrollWheelValue, _previousMouse.LeftButton, mouse.MiddleButton, _previousMouse.RightButton, mouse.XButton1, mouse.XButton2);
        }

        var overPanel = IsWindowHostReady && _bottomPanel is not null
            && _bottomPanel.Update(_device.Viewport, mouse, _previousMouse, PanelFaxes().Count, BottomPanelButton);
        var panelHeight = _bottomPanel is null ? 0 : OriginalBottomPanel.Height;
        var overWindow = IsWindowHostReady
            && (_windows.Update(this, mouse, _previousMouse, new Rectangle(0, StatusHeight, _device.Viewport.Width, Math.Max(1, _device.Viewport.Height - StatusHeight - panelHeight))) | overPanel);
        _cursorOverWindow = overWindow;
        if (overWindow)
        {
            mouse = new MouseState(mouse.X, mouse.Y, _previousMouse.ScrollWheelValue, _previousMouse.LeftButton, _previousMouse.MiddleButton, _previousMouse.RightButton, _previousMouse.XButton1, _previousMouse.XButton2);
        }

        if (Pressed(Keys.Home))
        {
            ResetCamera();
        }

        UpdateCameraKeys(keys, mouse, viewSize, Pressed);

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
            _floatingDollars.SetScale(scale);
        }

        // CorsixTH's GameUI scrolling: the arrow keys (and WASD) and the screen's edges scroll, faster with Shift and the
        // longer they do; the right or middle button drags; the wheel zooms with momentum, towards the cursor.
        var keyX = (ScrollKeyHeld("ingame_scroll_right", keys) || keys.IsKeyDown(Keys.D) ? 1 : 0) - (ScrollKeyHeld("ingame_scroll_left", keys) || keys.IsKeyDown(Keys.A) ? 1 : 0);
        var keyY = (ScrollKeyHeld("ingame_scroll_down", keys) || keys.IsKeyDown(Keys.S) ? 1 : 0) - (ScrollKeyHeld("ingame_scroll_up", keys) || keys.IsKeyDown(Keys.W) ? 1 : 0);
        var shiftHeld = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        // In the map editor the right button copies an area (UIMapEditor); the middle one still drags the view.
        var rightPans = _editorTool is null;
        var dragging = (rightPans && mouse.RightButton == ButtonState.Pressed) || mouse.MiddleButton == ButtonState.Pressed;
        var wasDragging = (rightPans && _previousMouse.RightButton == ButtonState.Pressed) || _previousMouse.MiddleButton == ButtonState.Pressed;
        if (dragging && wasDragging)
        {
            var (dragX, dragY) = _scrolling.Drag(mouse.X - _previousMouse.X, mouse.Y - _previousMouse.Y, _camera.Zoom);
            _camera.Center += new Vector2(dragX, dragY);
        }

        var wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (wheel != 0)
        {
            _scrolling.Wheel(wheel / 120.0);
        }

        var (edgeX, edgeY) = _scrolling.EdgePull(mouse.X, mouse.Y, _device.Viewport.Width, _device.Viewport.Height, _config.Fullscreen);
        var zoomAnchor = new Point(mouse.X, mouse.Y - StatusHeight);
        _scrollMilliseconds += gameTime.ElapsedGameTime.TotalMilliseconds;
        for (var tick = 0; _scrollMilliseconds >= MapScrolling.TickMilliseconds; tick++)
        {
            if (tick == MaxScrollTicksPerFrame)
            {
                // After a long pause (a level loading, the window dragged), don't scroll for all of it at once.
                _scrollMilliseconds = 0;
                break;
            }

            _scrollMilliseconds -= MapScrolling.TickMilliseconds;
            var step = _scrolling.Tick(new ScrollInput(keyX, keyY, shiftHeld, edgeX, edgeY, dragging));
            _camera.Center += new Vector2((float)step.X, (float)step.Y);
            if (step.Zoom != 0 && _scrolling.Zoomed(_camera.Zoom, step.Zoom, _device.Viewport.Width) is { } zoomed)
            {
                _camera.ZoomAt((float)zoomed, zoomAnchor, viewSize);
            }
        }

        ClampCamera();

        var inView = !overWindow && mouse.Y >= StatusHeight && mouse.X >= 0 && mouse.X < viewSize.X && mouse.Y < StatusHeight + viewSize.Y;
        var world = _camera.ScreenToWorld(new Point(mouse.X, mouse.Y - StatusHeight), viewSize);
        var tile = Camera.WorldToTile(world);
        _hoveredTile = inView && Map is { } map && map.Contains(tile.X, tile.Y) ? tile : null;
        _hoveredFraction = Camera.WorldToTileFraction(world);
        var staffTile = Camera.WorldToTile(_camera.ScreenToWorld(new Point(mouse.X, mouse.Y - StatusHeight + StaffCursorDrop), viewSize));
        _staffTile = inView && Map is { } staffMap && staffMap.Contains(staffTile.X, staffTile.Y) ? staffTile : null;
        _hoveredWorld = inView ? new Point((int)Math.Floor(world.X), (int)Math.Floor(world.Y)) : null;
        UpdateHover();

        UpdateSimulation(gameTime, mouse, keys, Pressed);

        _previousKeys = realKeys;
        _previousMouse = realMouse;
    }

    /// <summary>
    /// Where an earthquake moves the view this frame (CorsixTH's <c>GameUI:draw</c> with <c>shake_screen_intensity</c>);
    /// the view keeps still while the game is paused, as CorsixTH's "pause" stage stops the shaking.
    /// </summary>
    private Point ShakeOffset()
    {
        var intensity = _config.EnableScreenShake && _session is { IsPaused: false, Simulation: { } simulation } ? simulation.Earthquakes.Shake switch
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
            _humanoids.BeginFrame();
            _objects.BeginFrame();
            _frameSounds.BeginFrame();
            // The object being placed is drawn in the same order as placed objects, so walls in front hide it.
            var ghost = _buildTool.Ghost ?? _furnishTool.Ghost;
            var ghostTile = ghost is { } g && Simulation is { } ghostSimulation ? ObjectDrawing.DrawTile(ghostSimulation.Content, g.ObjectType, g.Tile, g.Orientation) : (TilePosition?)null;
            var blueprint = _buildTool.Blueprint;
            // Walls in front of what is being put down let it be seen.
            _renderer.SeeThroughAt = ghostTile ?? (_hiring is not null || _pickedUpStaff is not null ? _staffTile is { } staffAt ? new TilePosition(staffAt.X, staffAt.Y) : null : null);
            _renderer.Draw(batch, map, view, (tile, anchorX, anchorY) =>
            {
                if (blueprint is not null)
                {
                    _blueprints.DrawTile(batch, blueprint, tile, anchorX, anchorY);
                }

                DrawEntities(batch, tile, anchorX, anchorY);
                DrawEditorObjects(batch, tile, anchorX, anchorY);
                if (tile == ghostTile && ghost is { } shown)
                {
                    _objects.Draw(batch, shown.ObjectType, shown.Tile, shown.Orientation, Simulation!.Tick, shown.Tint);
                }
            },
            tile => _entitiesByTile.TryGetValue(tile, out var here) && here.Any(e => e is Humanoid or Rat),
            (tile, anchorX, anchorY) => DrawEarlyEntities(batch, tile, anchorX, anchorY),
            tile => _earlyByTile.TryGetValue(tile, out var early) && early.Count > 0);
            DrawOverlay(batch, map, view);
            DrawPlacingStaff(batch);
            if (_editorTool is { } editor && (_hoveredTile is not null || editor.IsDragging))
            {
                // UIMapEditor:fillCursorArea: a red outline on each tile the cursor covers.
                foreach (var (corner, sizeX, sizeY) in editor.Outlines())
                {
                    for (var dx = 0; dx < sizeX; dx++)
                    {
                        for (var dy = 0; dy < sizeY; dy++)
                        {
                            var (cx, cy) = (corner.X + dx, corner.Y + dy);
                            batch.Draw(_cellOutline, new Vector2((32 * (cx - cy)) - 32, 16 * (cx + cy)), CellOutlineColor);
                        }
                    }
                }
            }

            _buildTool.DrawPreview(batch, _diamond);
            if (_buildTool.IsActive && _hoveredTile is { } cell)
            {
                // While building a room, the tile under the cursor has a red outline, as in CorsixTH's UIEditRoom:draw.
                // Furnishing the corridor is CorsixTH's plain UIPlaceObjects, which draws none.
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

            RenderCloseUps(batch, map);
        }

        // animation::draw: a frame's sound plays where it was drawn.
        foreach (var (sound, x, y) in _frameSounds.TakeDue())
        {
            _sounds.PlayNumberAt(sound, (new Vector2(x, y) - _camera.TopLeft(viewSize).ToVector2()) * _camera.Zoom, viewSize);
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
        DrawRoomList(batch);
        if (IsWindowHostReady)
        {
            var pointer = InputScript.Mouse();
            DrawBottomPanel(batch, viewport, new Point(pointer.X, pointer.Y));
            DrawAdviser(batch, viewport);
            _windows.Draw(batch, _text, _pixel, new Point(pointer.X, pointer.Y));
            if (_bottomPanel is not null && OriginalBottomPanel.TooltipAt(viewport, new Point(pointer.X, pointer.Y), ((IWindowHost)this).HotkeyText) is { } tooltip)
            {
                OriginalBottomPanel.DrawTooltip(batch, _text, _pixel, viewport, new Point(pointer.X, pointer.Y), tooltip);
            }
        }

        DrawLevelList(batch);
        if (_cursors is not null)
        {
            var at = InputScript.Mouse();
            _cursors.Draw(batch, CurrentCursor(at), new Point(at.X, at.Y));
        }

        batch.End();
    }

    /// <inheritdoc/>
    public bool DrawsCursor => _cursors is not null;

    /// <summary>
    /// GameUI:onCursorWorldPositionChange's cursor: the epidemic's over an infected patient during a cover-up, else the
    /// hover cursor of what is under it (a patient, staff, a door or reception desk's queue, a machine to repair, a rat),
    /// else the pressed one while a button is down; editing a room or carrying staff have theirs.
    /// </summary>
    private GameCursor CurrentCursor(MouseState mouse)
    {
        if (_pickingRoomToEdit)
        {
            return GameCursor.EditRoom;
        }

        if (_pickedUpStaff is not null)
        {
            return GameCursor.Grab;
        }

        if (Simulation is { } waiting && _buildTool.IsWaitingForRoom(waiting))
        {
            return GameCursor.Sleep;
        }

        var down = mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed;
        if (!_cursorOverWindow && _buildTool.IsActive
            && _buildTool.Cursor(_hoveredTile is { } buildTile ? new TilePosition(buildTile.X, buildTile.Y) : null) is { } buildCursor)
        {
            return buildCursor;
        }
        if (_cursorOverWindow || Simulation is not { } simulation || _session is null || _hoveredTile is not { } hovered
            || _buildTool.IsActive || _furnishTool.IsActive)
        {
            return down ? GameCursor.Clicked : GameCursor.Default;
        }

        var tile = new TilePosition(hovered.X, hovered.Y);
        if (simulation.FindEntity(_hoveredHumanoid) is Humanoid humanoid)
        {
            if (humanoid.Behaviour is PatientBehaviour { IsInfected: true } && humanoid.HospitalPlayer < simulation.Hospitals.Count
                && simulation.Hospitals[humanoid.HospitalPlayer].Epidemics.Current is { IsCoverUp: true, IsWatchRunning: true })
            {
                return GameCursor.Epidemic;
            }

            if (humanoid.Behaviour is PatientBehaviour)
            {
                return GameCursor.Patient;
            }

            if (humanoid.Profile is not null && !humanoid.IsFired)
            {
                return GameCursor.Staff;
            }

            return GameCursor.Default;
        }

        if (simulation.Entities.OfType<Rat>().Any(rat => !rat.IsGone && RatDrawing(rat).Tile == tile))
        {
            return GameCursor.KillRatHover;
        }

        if (RatNear(simulation, _session.Player, hovered) is not null)
        {
            return GameCursor.KillRat;
        }

        if (MachineUnderCursor(simulation, tile) is { } machine && simulation.FindEntity(machine.RoomId) is Room { IsActive: true })
        {
            return GameCursor.Repair;
        }

        if (DeskUnderCursor(simulation, tile) is not null
            || simulation.Entities.OfType<RoomDoor>().Any(door => door.Tile == tile))
        {
            return GameCursor.Queue;
        }

        return down ? GameCursor.Clicked : GameCursor.Default;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _target?.Dispose();
        _blueprints.Dispose();
        _floatingDollars.Dispose();
        _cursors?.Dispose();
        _panelSprites?.Dispose();
        _bottomPanel?.Dispose();
        _graphics.Dispose();
        foreach (var closeUp in _closeUps.Values)
        {
            closeUp.Dispose();
        }
        _blackText?.Dispose();
        _animations.Dispose();
        _blocks.Dispose();
        _text.Dispose();
        _unicodeFonts.Dispose();
        _diamond.Dispose();
        _cellOutline.Dispose();
        _pixel.Dispose();
        _sounds.Dispose();
    }

    /// <summary>Where on the map view the middle of a tile is, for sounds heard there.</summary>
    private Vector2 ScreenOf(TilePosition tile)
    {
        var topLeft = _camera.TopLeft(ViewSize);
        return (Camera.TileCenter(tile.X, tile.Y) - topLeft.ToVector2()) * _camera.Zoom;
    }

    /// <summary>The status bar's height: two lines of text, taller with a TrueType font.</summary>
    private int StatusHeight => Math.Max(36, (2 * _text.LineHeight) + 10);

    private Point ViewSize => new(_device.Viewport.Width, Math.Max(1, _device.Viewport.Height - StatusHeight));

    private void SelectLevel(int index)
    {
        if (_levels.Count == 0)
        {
            _error = "No levels found.";
            return;
        }

        CloseMainMenu();
        _levelIndex = index;
        _lastYearSeen = null;
        _lastOutcome = EndOutcome.None;
        _session?.Dispose();
        _session = null;
        try
        {
            var level = _levels[index].Load();
            var local = new LocalSession(_levels[index].Name, level.StartSimulation(seed: 1), _saves, _config.AutosaveFrequency);
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
    /// <c>App:quickSave</c> and <c>App:quickLoad</c> (Alt+Shift+S and Alt+Shift+L): only for local games, and not while
    /// building. Loading without a quick save makes one, as CorsixTH does.
    /// </summary>
    /// <returns>Whether another game was loaded.</returns>
    private bool UpdateQuickSave(KeyboardState keys, Func<Keys, bool> pressed)
    {
        if (_session is not LocalSession local)
        {
            return false;
        }

        var save = HotkeyInput.Pressed(_hotkeys["ingame_quickSave"], keys, pressed);
        var load = HotkeyInput.Pressed(_hotkeys["ingame_quickLoad"], keys, pressed);
        if (!save && !load)
        {
            return false;
        }

        return QuickSave(load);
    }

    /// <summary>Saves to the quick save, or loads it (making one if there is none).</summary>
    /// <returns>Whether another game was loaded.</returns>
    private bool QuickSave(bool load)
    {
        if (_session is not LocalSession local)
        {
            return false;
        }

        if (_buildTool.IsActive || _furnishTool.IsActive)
        {
            ShowNotice("Finish building before saving or loading");
            return false;
        }

        if (load && File.Exists(_saves.QuickSavePath))
        {
            return LoadGame(_saves.QuickSavePath);
        }

        try
        {
            SaveGameStore.Save(_saves.QuickSavePath, local.Name, local.Simulation);
            ShowNotice(load ? "There was no quick save to load, so one has been made" : "Quick saved");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice($"Cannot save: {e.Message}");
        }

        return false;
    }

    /// <summary>Replaces the local game with a saved one, keeping the game speed.</summary>
    /// <returns>Whether the game was loaded.</returns>
    private bool LoadGame(string path)
    {
        SavedGame saved;
        try
        {
            saved = SaveGameStore.Load(path, BaseGameContent.Create());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            ShowNotice($"Cannot load {Path.GetFileName(path)}: {e.Message}");
            return false;
        }

        var speed = _mainMenu is null && _session is { CanChangeSpeed: true } previous ? previous.Speed : _initialSpeed;
        CloseMainMenu();
        _session?.Dispose();
        var local = new LocalSession(saved.LevelName, saved.Simulation, _saves, _config.AutosaveFrequency);
        local.SetSpeed(speed == GameSpeed.SpeedUp ? GameSpeed.Normal : speed);
        _session = local;
        _error = null;
        var index = _levels.ToList().FindIndex(level => string.Equals(level.Name, saved.LevelName, StringComparison.Ordinal));
        if (index >= 0)
        {
            _levelIndex = index;
        }

        // The demo's steps belong to the game it was started on.
        _demoPending = _demoDeskPending = _demoDiagnosisPending = _demoMachinePending = _demoToiletsPending = false;
        _demoFurniture.Clear();
        PrepareMapIfReady();
        ShowNotice($"Loaded {Path.GetFileName(path)}");
        return true;
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
            foreach (var orientation in DemoOrientations(objectId))
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
    /// Two radiators for each demo room: without them its staff grow cold and unhappy in the first winter, ask for a raise
    /// and are fired when nobody answers (Humanoid:tickDay, Staff:checkIfWaitedTooLongForRaise); two keep a room of four
    /// by four tiles comfortable at the default heat.
    /// </summary>
    private static readonly string[] DemoRadiators = ["radiator", "radiator"];

    private void EnqueueDemoRadiators()
    {
        foreach (var radiator in DemoRadiators)
        {
            _demoFurniture.Enqueue(radiator);
        }
    }

    /// <summary>The orientations the demo tries for an object; radiators only have sprites facing north and east.</summary>
    private static Direction[] DemoOrientations(string objectId) => string.Equals(objectId, "radiator", StringComparison.Ordinal)
        ? [Direction.North, Direction.East]
        : [Direction.North, Direction.East, Direction.South, Direction.West];

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
            EnqueueDemoRadiators();
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
    /// Once the demo's general diagnosis works, builds toilets near the camera with two toilets and a sink, for the
    /// patients who need them.
    /// </summary>
    private void BuildDemoToilets()
    {
        if (!_demoToiletsPending || _session is null || Simulation is not { } simulation
            || !simulation.Entities.OfType<Room>().Any(room => room is { IsActive: true, RoomType: "general_diag" }))
        {
            return;
        }

        _demoToiletsPending = false;
        var start = simulation.Map.GetCameraTile(Math.Min(_session.Player, simulation.Map.PlayerCount - 1));
        if (simulation.Content.FindRoom("toilets") is not { } room || FindRoomSpot(simulation.Map, room, start) is not { } plan)
        {
            return;
        }

        _session.Submit(new BuildRoomCommand(plan));
        foreach (var item in new[] { "loo", "loo", "sink" })
        {
            _demoFurniture.Enqueue(item);
        }

        EnqueueDemoRadiators();
    }

    /// <summary>
    /// Once the demo's reception desk stands, buys a drinks machine a few tiles from it, for the patients who grow thirsty
    /// while they wait.
    /// </summary>
    private void PlaceDemoDrinksMachine()
    {
        if (!_demoMachinePending || _session is null || Simulation is not { } simulation
            || simulation.Entities.OfType<RoomObject>().FirstOrDefault(o => o.Desk is not null) is not { } desk)
        {
            return;
        }

        _demoMachinePending = false;
        foreach (var tile in _hospitalTiles.Where(t => Math.Abs(t.X - desk.Tile.X) + Math.Abs(t.Y - desk.Tile.Y) >= 3)
            .OrderBy(t => Math.Abs(t.X - desk.Tile.X) + Math.Abs(t.Y - desk.Tile.Y)).ThenBy(t => t.Y).ThenBy(t => t.X))
        {
            foreach (var orientation in new[] { Direction.South, Direction.East, Direction.North, Direction.West })
            {
                if (ObjectPlacer.Check(simulation, "drinks_machine", tile, orientation, _session.Player) == ObjectPlacementProblem.None)
                {
                    _session.Submit(new PlaceObjectCommand("drinks_machine", tile, orientation));
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
        EnqueueDemoRadiators();
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

    /// <summary>
    /// CorsixTH's camera hotkeys: = and − zoom in and out towards the cursor (five times as far with Shift,
    /// <c>World:adjustZoom</c>), 0 goes back to the original size (<c>World:resetZoom</c>), Alt+digit remembers the view
    /// and Ctrl+digit goes back to it (<c>GameUI:setMapRecallPosition</c>, <c>recallMapPosition</c>). Until the hire window
    /// exists, Ctrl+1 to Ctrl+4 hire staff instead, so those keys do not recall. All of them are hotkeys
    /// (<c>ingame_zoom_in</c>, <c>ingame_zoom_in_more</c>, <c>ingame_zoom_out</c>, <c>ingame_zoom_out_more</c>,
    /// <c>ingame_reset_zoom</c>, <c>ingame_storePosition_N</c>, <c>ingame_recallPosition_N</c>).
    /// </summary>
    private void UpdateCameraKeys(KeyboardState keys, MouseState mouse, Point viewSize, Func<Keys, bool> pressed)
    {
        bool hotkey(string action) => HotkeyInput.Pressed(_hotkeys[action], keys, pressed);
        var zoomDelta = (hotkey("ingame_zoom_in") ? 1 : 0) + (hotkey("ingame_zoom_in_more") ? 5 : 0)
            - (hotkey("ingame_zoom_out") ? 1 : 0) - (hotkey("ingame_zoom_out_more") ? 5 : 0);
        if (zoomDelta != 0 && _scrolling.Zoomed(_camera.Zoom, zoomDelta, _device.Viewport.Width) is { } zoomed)
        {
            _camera.ZoomAt((float)zoomed, new Point(mouse.X, mouse.Y - StatusHeight), viewSize);
        }

        if (hotkey("ingame_reset_zoom"))
        {
            _camera.ZoomAt(1f, new Point(viewSize.X / 2, viewSize.Y / 2), viewSize);
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            var suffix = digit.ToString(CultureInfo.InvariantCulture);
            if (hotkey("ingame_storePosition_" + suffix))
            {
                _recallPositions[digit] = (_camera.Center, _camera.Zoom);
            }
            else if (hotkey("ingame_recallPosition_" + suffix) && !IsDebugHireKey(_hotkeys["ingame_recallPosition_" + suffix])
                && _recallPositions[digit] is var (center, zoom))
            {
                _camera.ZoomAt(zoom, new Point(viewSize.X / 2, viewSize.Y / 2), viewSize);
                _camera.Center = center;
            }
        }
    }

    /// <summary>Ctrl+1 to Ctrl+4, which hire staff for now.</summary>
    private static bool IsDebugHireKey(Hotkey? key) =>
        key is { Ctrl: true, Shift: false, Alt: false, Key: "1" or "2" or "3" or "4" };

    /// <summary>An <c>ingame_scroll_*</c> key held; Shift (<c>ingame_scroll_shift</c>) only makes it faster, so modifiers do not matter.</summary>
    private bool ScrollKeyHeld(string action, KeyboardState keys) =>
        _hotkeys[action] is { } key && HotkeyInput.ToKeys(key.Key) is { } down && keys.IsKeyDown(down);

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
        PlaceDemoDrinksMachine();
        BuildDemoToilets();
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
        // The level behind the main menu stays paused.
        if (_session is not { CanChangeSpeed: true } session || _mainMenu is not null)
        {
            return;
        }

        var control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        var alt = keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
        (string Action, GameSpeed Speed)[] speedKeys =
        [
            ("ingame_gamespeed_slowest", GameSpeed.Slowest), ("ingame_gamespeed_slower", GameSpeed.Slower),
            ("ingame_gamespeed_normal", GameSpeed.Normal), ("ingame_gamespeed_max", GameSpeed.MaxSpeed),
            ("ingame_gamespeed_thensome", GameSpeed.AndThenSomeMore),
        ];
        foreach (var (action, speed) in speedKeys)
        {
            if (HotkeyInput.Pressed(_hotkeys[action], keys, pressed))
            {
                session.SetSpeed(speed);
            }
        }

        // The insane speed has no hotkey in CorsixTH (only its menu): 6 here.
        if (!control && !alt && pressed(Keys.D6))
        {
            session.SetSpeed(GameSpeed.Insane);
        }

        // While building, P is the pick-up key.
        if (pressed(Keys.Space) || (HotkeyInput.Pressed(_hotkeys["ingame_pause"], keys, pressed) && !_buildTool.IsActive))
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

        if (HotkeyInput.Held(_hotkeys["ingame_gamespeed_speedup"], keys))
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

        if (_editorTool is { } editorTool)
        {
            UpdateMapEditor(editorTool, mouse);
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

        // Until the build room window exists, B opens a list of the rooms to build.
        var wasBuilding = _buildTool.IsActive;
        var shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        var ctrl = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);

        var alt = keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
        if (UpdateQuickSave(keys, pressed))
        {
            // Another game now: this frame's input was meant for the old one.
            return;
        }

        // Until the bank manager exists: L borrows 5,000, Shift+L pays 5,000 back; with Ctrl as much as allowed.
        if (pressed(Keys.L) && !alt)
        {
            _session.Submit(shift ? new RepayCommand(ctrl) : new BorrowCommand(ctrl));
        }

        // Until faxes exist, E accepts the emergency offered and Shift+E refuses it.
        if (pressed(Keys.E) && simulation.Hospitals[_session.Player].Emergencies.Current is { IsAccepted: false })
        {
            _session.Submit(new AnswerEmergencyCommand(!shift));
        }

        // Until faxes exist, I covers up the epidemic the hospital was told of and Shift+I declares it.
        if (pressed(Keys.I) && simulation.Hospitals[_session.Player].Epidemics.IsFaxPending)
        {
            _session.Submit(new AnswerEpidemicCommand(!shift));
        }

        // Until faxes exist, K answers the first waiting patient's fax: wait; Shift+K sends them home, Ctrl+K guesses the cure.
        if (pressed(Keys.K) && FirstQuestion(simulation, _session.Player) is { } asking)
        {
            _session.Submit(new AnswerPatientCommand(asking.Id, ctrl ? PatientAnswer.GuessCure : shift ? PatientAnswer.SendHome : PatientAnswer.Wait));
        }

        // Until patients can be clicked, J picks every infected patient for vaccination during a cover-up.
        if (pressed(Keys.J) && simulation.Hospitals[_session.Player].Epidemics.Current is { IsCoverUp: true, IsWatchRunning: true } coverUp)
        {
            foreach (var id in coverUp.InfectedIds)
            {
                if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour { IsVaccinated: false, IsMarkedForVaccination: false } })
                {
                    _session.Submit(new VaccinateCommand(id));
                }
            }
        }

        // GameUI's ingame_setTransparent (X, while held) and ingame_toggleTransparent (Shift+X): walls half transparent.
        if (HotkeyInput.Pressed(_hotkeys["ingame_toggleTransparent"], keys, pressed))
        {
            _wallsTransparentToggled = !_wallsTransparentToggled;
        }

        _renderer.TransparentWalls = _wallsTransparentToggled || HotkeyInput.Held(_hotkeys["ingame_setTransparent"], keys);

        // Until staff can be grabbed with the hand cursor, M picks up the player's member of staff nearest the cursor
        // (within a tile) and M again puts them down at the cursor; on the same tile, nothing happens.
        if (pressed(Keys.M) && (_pickedUpStaff is null ? _hoveredTile : _staffTile) is { } handPoint)
        {
            var hand = new TilePosition(handPoint.X, handPoint.Y);
            if (_pickedUpStaff is { } carried)
            {
                PutDownCarried(simulation, carried, hand, StaffPlacing.Click(candidateGone: false, left: true, right: false, _session.IsPaused));
            }
            else
            {
                _pickedUpStaff = simulation.Entities.OfType<Humanoid>()
                    .Where(h => h.Profile is not null && h.Employer == _session.Player && !h.IsDespawning && !h.IsFired)
                    .Where(h => Math.Abs(h.Tile.X - hand.X) <= 1 && Math.Abs(h.Tile.Y - hand.Y) <= 1)
                    .OrderBy(h => Math.Abs(h.Tile.X - hand.X) + Math.Abs(h.Tile.Y - hand.Y))
                    .Select(h => (int?)h.Id)
                    .FirstOrDefault();
                if (_pickedUpStaff is not null)
                {
                    _sounds.Play("pickup.wav"); // the pickup action
                    ShowNotice("Picked up: click to put them down, right click lets them go");
                }
            }
        }

        // Until patients can be clicked, Q sends the player's patient nearest the cursor home (within a tile), and
        // Shift+Q guesses their cure, as the patient window's buttons do.
        if (pressed(Keys.Q) && _hoveredTile is { } spot)
        {
            var chosen = simulation.Entities.OfType<Humanoid>()
                .Where(h => h.Behaviour is PatientBehaviour && h.HospitalPlayer == _session.Player && !h.IsDespawning)
                .Where(h => Math.Abs(h.Tile.X - spot.X) <= 1 && Math.Abs(h.Tile.Y - spot.Y) <= 1)
                .OrderBy(h => Math.Abs(h.Tile.X - spot.X) + Math.Abs(h.Tile.Y - spot.Y))
                .FirstOrDefault();
            if (chosen is not null)
            {
                _session.Submit(shift ? new GuessPatientCureCommand(chosen.Id) : new SendPatientHomeCommand(chosen.Id));
                if (!shift)
                {
                    _sounds.PlayAt("sack.wav", ScreenOf(chosen.Tile), ViewSize); // UIPatient:goHome
                }
            }
        }

        // Until faxes exist, V invites the VIP who asks to visit and Shift+V refuses them.
        if (pressed(Keys.V) && simulation.Hospitals[_session.Player].Vips.IsInvitationPending)
        {
            _session.Submit(new AnswerVipCommand(!shift));
        }

        UpdateWindowKeys(keys, pressed);

        // Until the town map exists, T and Shift+T turn the heating up and down.

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
            // UITownMap's purchase: the till rings, or the wrong-choice buzz.
            if (!simulation.IsParcelPurchasable(parcel, _session.Player))
            {
                ShowNotice($"Parcel {parcel} is not for sale");
                _sounds.Play("Wrong2.wav");
            }
            else if (price > simulation.Hospitals[_session.Player].Balance)
            {
                ShowNotice($"Not enough money for parcel {parcel}: it costs {MoneyText.Format(price)}");
                _sounds.Play("Wrong2.wav");
            }
            else
            {
                _session.Submit(new PurchaseParcelCommand(parcel));
                _sounds.Play("cashreg.wav");
            }
        }

        // The room list (B): a click on a room starts it; a click elsewhere, B or Esc closes the list.
        if (_roomList)
        {
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            {
                var chosen = _roomListRows.FirstOrDefault(row => row.Row.Contains(mouse.X, mouse.Y));
                _roomList = false;
                if (chosen.Room is { } picked)
                {
                    TryStartRoom(simulation, picked);
                }

                return;
            }

            if (HotkeyInput.Pressed(_hotkeys["ingame_panel_buildRoom"], keys, pressed))
            {
                _roomList = false;
                return;
            }
        }
        else if (HotkeyInput.Pressed(_hotkeys["ingame_panel_buildRoom"], keys, pressed) && !_buildTool.IsActive && !_furnishTool.IsActive)
        {
            _roomList = true;
            return;
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

        if (UpdateHiring(simulation, mouse) || UpdateCarrying(simulation, mouse))
        {
            return;
        }

        if (_buildTool.IsFurnishing && !_windows.Windows.Any(window => window is RoomEditWindow))
        {
            _windows.Open(new RoomEditWindow(_buildTool, button => _buildButton = button));
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
                pressed(Keys.Enter) || TakeButton(BuildButton.Confirm),
                back || TakeButton(BuildButton.Back),
                pressed(Keys.P) || TakeButton(BuildButton.PickUp));
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

        if (pressed(Keys.H) && !keys.IsKeyDown(Keys.LeftControl) && !keys.IsKeyDown(Keys.RightControl) && _hoveredTile is { } spawnAt)
        {
            _session.Submit(new SpawnHumanoidCommand(new TilePosition(spawnAt.X, spawnAt.Y)));
        }

        // Debug hiring until the hire window exists: Ctrl+1 to Ctrl+4 hire the first doctor, nurse,
        // handyman or receptionist on the list and place them on the tile under the cursor. The plain
        // number keys set the game speed, as in CorsixTH.
        var control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);

        // Ctrl+C over a finished room of the hospital puts a copy of it on the cursor (our addition, BuildRoomTool.StartCopy).
        if (control && pressed(Keys.C) && !_buildTool.IsActive && !_furnishTool.IsActive && _hoveredTile is { } copyAt
            && simulation.FindEntity(simulation.Map[copyAt.X, copyAt.Y].RoomId) is Room { IsActive: true } copied
            && RoomBuilder.IsOwnedBy(simulation.Map, copied, _session.Player))
        {
            _windows.CloseAll();
            _buildTool.StartCopy(simulation, copied);
        }

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
                    _sounds.Play("plac_st2.wav"); // UIPlaceStaff:close
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
        if (_pickingRoomToEdit)
        {
            PickRoomToEdit(simulation, clicked, mouse);
            return;
        }

        if (clicked && _hoveredTile is { } aim && RatNear(simulation, _session.Player, aim) is { } shot)
        {
            // Rat:onClick: a click on one of the player's rats (within a tile of the cursor) shoots it.
            _session.Submit(new ShootRatCommand(shot.Id));
            _sounds.Play("shotgun.wav");
            _sounds.Play("deadrat2.wav");
        }
        else if (clicked && _hoveredTile is { } target)
        {
            var tile = new TilePosition(target.X, target.Y);
            var here = HumanoidUnderCursor(simulation, tile);
            var desk = DeskUnderCursor(simulation, tile);
            var roomId = simulation.Map[tile.X, tile.Y].RoomId;
            if (here is not null)
            {
                _selected = here.Id;
                if (here.Profile is not null && here.Employer == _session.Player && !here.IsFired)
                {
                    // A click on a member of staff opens their window (CorsixTH's UIStaff), on a patient theirs (UIPatient).
                    _windows.Open(new StaffWindow(here.Id));
                }
                else if (here.Behaviour is PatientBehaviour && here.HospitalPlayer == _session.Player)
                {
                    _windows.Open(new PatientWindow(here.Id));
                }
            }
            else if (MachineUnderCursor(simulation, tile) is { } machine && simulation.FindEntity(machine.RoomId) is Room machineRoom && RoomBuilder.IsOwnedBy(simulation.Map, machineRoom, _session.Player))
            {
                // A click on a machine opens its window (CorsixTH's UIMachine).
                _selected = null;
                _windows.Open(new MachineWindow(machine.Id));
            }
            else if (desk is not null || (roomId > 0 && simulation.FindEntity(roomId) is Room { IsActive: true }))
            {
                // A click on a reception desk or a room opens its queue (CorsixTH's UIQueue).
                _selected = null;
                _windows.Open(new QueueWindow(desk?.Id ?? roomId));
            }
            else if (_selected is { } id)
            {
                _session.Submit(new WalkToCommand(id, tile));
            }
        }
    }

    /// <summary>Whether Shift+X has made the walls transparent (GameUI's toggled_transparency).</summary>
    private bool _wallsTransparentToggled;

    /// <summary>The player's rat nearest the tile under the cursor, if it is within a tile.</summary>
    private Rat? RatNear(Simulation simulation, int player, Point aim) =>
        simulation.Entities.OfType<Rat>()
            .Where(rat => !rat.IsGone && rat.Player == player)
            .Select(rat => (rat, RatDrawing(rat).Tile))
            .Where(entry => Math.Abs(entry.Tile.X - aim.X) <= 1 && Math.Abs(entry.Tile.Y - aim.Y) <= 1)
            .OrderBy(entry => Math.Abs(entry.Tile.X - aim.X) + Math.Abs(entry.Tile.Y - aim.Y))
            .Select(entry => entry.rat)
            .FirstOrDefault();

    /// <summary>The humanoid under the cursor (picked as a click picks one), for its hoverable mood.</summary>
    private int _hoveredHumanoid;

    /// <summary>The world pixel under the cursor, for picking by the sprites drawn there.</summary>
    private Point? _hoveredWorld;

    /// <summary>The placed object whose sprite is under the cursor (GameUI's hitTestObjects), or null.</summary>
    private RoomObject? ObjectUnderCursor(Simulation simulation) =>
        _hoveredWorld is { } point && _objects.ObjectAt(point) is { } id ? simulation.FindEntity(id) as RoomObject : null;

    /// <summary>The machine under the cursor: by its sprite, else on the tile under it.</summary>
    private RoomObject? MachineUnderCursor(Simulation simulation, TilePosition tile) =>
        ObjectUnderCursor(simulation) is { Machine: not null } machine ? machine : MachineWindow.At(simulation, tile);

    /// <summary>The reception desk under the cursor: by its sprite, else on the tile under it.</summary>
    private RoomObject? DeskUnderCursor(Simulation simulation, TilePosition tile) =>
        ObjectUnderCursor(simulation) is { Desk: not null } desk ? desk
        : simulation.Entities.OfType<RoomObject>().FirstOrDefault(o => o.Desk is not null && o.Tile == tile);

    /// <summary>
    /// The humanoid under the cursor: by the pixels of the sprites last drawn (GameUI's hitTestObjects), else one
    /// standing on the tile under it.
    /// </summary>
    private Humanoid? HumanoidUnderCursor(Simulation simulation, TilePosition tile) =>
        (_hoveredWorld is { } point && _humanoids.HumanoidAt(simulation, point) is { } id ? simulation.FindEntity(id) as Humanoid : null)
        ?? simulation.Entities.OfType<Humanoid>().FirstOrDefault(h => h.Tile == tile || h.StepTarget == tile);

    /// <summary>The room under the cursor or of the door under it, whose queueing patients show the queue mood.</summary>
    private int _hoveredRoom;

    /// <summary>
    /// GameUI:onCursorWorldPositionChange's queueing icons and hoverable moods: HLIGHTP2.wav when the cursor comes onto a
    /// room with a queue, or onto someone too cold or too hot.
    /// </summary>
    private void UpdateHover()
    {
        if (Simulation is not { } simulation || _hoveredTile is not { } hovered)
        {
            _hoveredHumanoid = 0;
            _hoveredRoom = 0;
            return;
        }

        var tile = new TilePosition(hovered.X, hovered.Y);
        var roomId = simulation.Map[tile.X, tile.Y].RoomId;
        if (roomId == 0)
        {
            // Over a door from the corridor: its room.
            roomId = simulation.Entities.OfType<RoomDoor>()
                .FirstOrDefault(door => door.Tile == tile || (door.Edge == EdgeOrientation.North ? door.Tile with { Y = door.Tile.Y - 1 } : door.Tile with { X = door.Tile.X - 1 }) == tile)?.RoomId ?? 0;
        }

        if (roomId != _hoveredRoom)
        {
            _hoveredRoom = roomId;
            if (simulation.FindEntity(roomId) is Room { DoorQueue.Count: > 0 })
            {
                _sounds.Play("HLIGHTP2.wav");
            }
        }

        var humanoid = HumanoidUnderCursor(simulation, tile);
        var id = humanoid?.Id ?? 0;
        if (id != _hoveredHumanoid)
        {
            _hoveredHumanoid = id;
            if (humanoid is not null && HumanoidMoods.HoverIcon(humanoid) is not null)
            {
                _sounds.Play("HLIGHTP2.wav");
            }
        }
    }

    private void CollectEntitiesByTile()
    {
        foreach (var list in _entitiesByTile.Values)
        {
            list.Clear();
        }

        foreach (var list in _earlyByTile.Values)
        {
            list.Clear();
        }

        _earlyObjectTiles.Clear();

        foreach (var list in _slavePartsByTile.Values)
        {
            list.Clear();
        }

        foreach (var list in _splitPartsByTile.Values)
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

            if (entity is RoomObject early && IsEarlyObject(simulation, early))
            {
                _earlyObjectTiles.Add(early.Tile);
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
            // The pickup action: one being carried is drawn on the cursor instead.
            if (entity is Humanoid { IsHidden: true } || (entity is Humanoid && entity.Id == _pickedUpStaff))
            {
                continue;
            }

            TilePosition? tile = entity switch
            {
                Humanoid humanoid => HumanoidDrawing.DrawTile(humanoid),
                EntranceDoor door => door.Tile,
                RoomDoor door => door.Tile,
                RoomObject placed => ObjectDrawing.DrawTile(simulation.Content, placed.ObjectType, placed.Tile, placed.Orientation),
                Litter { IsRemoved: false } litter => litter.Tile,
                Rat { IsGone: false } rat => RatDrawing(rat).Tile,
                _ => null,
            };
            if (tile is not { } position)
            {
                continue;
            }

            if (SplitParts(simulation, entity) is { } parts)
            {
                foreach (var (partTile, left, right) in parts)
                {
                    if (!_splitPartsByTile.TryGetValue(partTile, out var partList))
                    {
                        partList = [];
                        _splitPartsByTile[partTile] = partList;
                    }

                    partList.Add((entity, left, right));
                }

                continue;
            }

            // DrawFlags.EarlyList: an object of the early list, and whoever uses it (use_object.lua), with the north wall;
            // so is a door in a north wall (Door:setAnimation), both halves of a swing door included.
            if ((entity is RoomObject earlyObject && IsEarlyObject(simulation, earlyObject))
                || (entity is Humanoid { ActionAnimation: not null } user && _earlyObjectTiles.Contains(user.ActionAnimationTile))
                || entity is RoomDoor { Edge: EdgeOrientation.North })
            {
                AddEarly(position, entity);
                if (entity is RoomDoor { IsSwing: true } earlySwing)
                {
                    AddEarly(earlySwing.SlaveTile, entity);
                }

                continue;
            }

            if (!_entitiesByTile.TryGetValue(position, out var list))
            {
                list = [];
                _entitiesByTile[position] = list;
            }

            if (entity is RoomDoor { IsSwing: true } swingDoor)
            {
                // The left swing door is drawn in its own tile's pass.
                if (!_entitiesByTile.TryGetValue(swingDoor.SlaveTile, out var slaveList))
                {
                    slaveList = [];
                    _entitiesByTile[swingDoor.SlaveTile] = slaveList;
                }

                slaveList.Add(swingDoor);
            }

            if (entity is RoomObject master && ObjectDrawing.SlaveDrawTile(master.ObjectType, master.Tile, master.Orientation) is { } slaveTile)
            {
                if (!_slavePartsByTile.TryGetValue(slaveTile, out var slaves))
                {
                    slaves = [];
                    _slavePartsByTile[slaveTile] = slaves;
                }

                slaves.Add(master);
            }

            // The gates to hell the grim reaper opened are drawn in their own tile's pass, under the patient falling in.
            if (entity is Humanoid { Behaviour: GrimReaperBehaviour grim })
            {
                if (!_entitiesByTile.TryGetValue(grim.HoleTile, out var holeList))
                {
                    holeList = [];
                    _entitiesByTile[grim.HoleTile] = holeList;
                }

                holeList.Insert(0, entity);
            }

            // Litter lies on the floor, under whatever else is on its tile (CorsixTH's DrawingLayers.Litter).
            if (entity is Litter)
            {
                list.Insert(0, entity);
            }
            else
            {
                list.Add(entity);
            }
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

    /// <summary>
    /// The parts of an object drawn split over several tiles (<see cref="ObjectDrawing.Split"/>), or of the animation of
    /// someone using it from its tile, which shows the object; null for anything drawn whole.
    /// </summary>
    private static IEnumerable<(TilePosition Tile, int Left, int Right)>? SplitParts(Simulation simulation, Entity entity) => entity switch
    {
        RoomObject placed when simulation.FindEntity(placed.RoomId) is not Room { IsCrashed: true } =>
            ObjectDrawing.Split(placed.ObjectType, placed.Tile, placed.Orientation),
        Humanoid { ActionAnimation: not null, HiddenObjectId: not 0 } humanoid
            when simulation.FindEntity(humanoid.HiddenObjectId) is RoomObject used && humanoid.ActionAnimationTile == used.Tile =>
            ObjectDrawing.Split(used.ObjectType, used.Tile, used.Orientation),
        _ => null,
    };

    /// <summary>The first patient of the player's hospital waiting for an answer to their fax, in entity order.</summary>
    private static Humanoid? FirstQuestion(Simulation simulation, int player) =>
        simulation.Entities.OfType<Humanoid>().FirstOrDefault(humanoid => humanoid is { Behaviour: PatientBehaviour { Question: not PatientQuestion.None }, IsDespawning: false } && humanoid.HospitalPlayer == player);

    /// <summary>
    /// The emergency helicopter (CorsixTH's objects/helicopter.lua, animation 2456) on the tile south of the heliport:
    /// hidden on its way, then 600 pixels up coming down 10 a tick, landed, and going up again.
    /// </summary>
    /// <summary>
    /// Where and how a rat is drawn (CorsixTH's <c>Rat:_walkToNextTile</c>): running east or south it is drawn from the
    /// tile it runs to, west or north from the one it runs from, slid 32 by 16 pixels a tile; coming out of or going into
    /// a hole it plays that animation at the hole.
    /// </summary>
    private static (TilePosition Tile, int Animation, int OffsetX, int OffsetY) RatDrawing(Rat rat)
    {
        switch (rat.Phase)
        {
            case RatPhase.Emerging:
                return (rat.Origin.Tile, RatLeaveHole, 0, 0);
            case RatPhase.Entering:
                return (rat.Target.Tile, rat.Target.Wall == Direction.West ? RatEnterWestHole : RatEnterNorthHole, 0, 0);
        }

        var from = rat.From;
        var to = rat.To;
        var done = Rat.TicksPerTile - Math.Clamp(rat.Timer, 0, Rat.TicksPerTile);
        var slide32 = 32 * done / Rat.TicksPerTile;
        var slide16 = 16 * done / Rat.TicksPerTile;
        if (to.X > from.X)
        {
            return (to, RatWalkEast, -32 + slide32, -16 + slide16);
        }

        if (to.X < from.X)
        {
            return (from, RatWalkWest, -slide32, -slide16);
        }

        if (to.Y > from.Y)
        {
            return (to, RatWalkSouth, 32 - slide32, -16 + slide16);
        }

        return (from, RatWalkNorth, slide32, -slide16);
    }

    private void DrawHelicopter(SpriteBatch batch, Simulation simulation, TilePosition tile, int anchorX, int anchorY)
    {
        if (_session is not { } session || simulation.Hospitals[session.Player].Emergencies.HelicopterPhase is not { } phase || phase < 0)
        {
            return;
        }

        var heliport = simulation.Map.GetHeliportTile(session.Player);
        if (tile != heliport with { Y = heliport.Y + 1 })
        {
            return;
        }

        var height = phase switch
        {
            < 60 => 600 - (10 * phase),
            < 87 => 0,
            _ => 10 * (phase - 87),
        };
        var set = _animations.Animations;
        var first = set.FirstFrame(HelicopterAnimation);
        var frame = set.Advance(first, (int)(simulation.Tick % set.Length(first)));
        _animations.DrawFrame(batch, frame, anchorX, anchorY - height, NoLayers, flipHorizontally: false);
    }

    private void DrawEntities(SpriteBatch batch, TilePosition tile, int anchorX, int anchorY)
    {
        if (Simulation is { } helicopterSimulation)
        {
            DrawHelicopter(batch, helicopterSimulation, tile, anchorX, anchorY);
        }

        if (Simulation is { } holeSimulation)
        {
            // Rat holes, in north and west walls only; a west one is the north one mirrored.
            foreach (var hospital in holeSimulation.Hospitals)
            {
                foreach (var hole in hospital.Ratholes)
                {
                    if (hole.IsVisible && hole.Tile == tile)
                    {
                        _animations.DrawFrame(batch, _animations.Animations.FirstFrame(RatholeAnimation), anchorX, anchorY, NoLayers, flipHorizontally: hole.Wall == Direction.West);
                    }
                }
            }
        }

        if (Simulation is not { } simulation)
        {
            return;
        }

        if (!_entitiesByTile.TryGetValue(tile, out var list))
        {
            DrawSplitParts(batch, simulation, tile, anchorX, anchorY);
            DrawSlaveParts(batch, tile);
            DrawFloatingDollars(batch, simulation, tile, anchorX, anchorY);
            return;
        }

        // Litter lies under everything, split objects included.
        var splitDrawn = false;
        foreach (var entity in list)
        {
            if (!splitDrawn && entity is not Litter)
            {
                DrawSplitParts(batch, simulation, tile, anchorX, anchorY);
                splitDrawn = true;
            }

            DrawEntity(batch, simulation, entity, tile, anchorX, anchorY);
        }

        if (!splitDrawn)
        {
            DrawSplitParts(batch, simulation, tile, anchorX, anchorY);
        }

        DrawSlaveParts(batch, tile);
        DrawFloatingDollars(batch, simulation, tile, anchorX, anchorY);
    }

    private void AddEarly(TilePosition tile, Entity entity)
    {
        if (!_earlyByTile.TryGetValue(tile, out var list))
        {
            list = [];
            _earlyByTile[tile] = list;
        }

        list.Add(entity);
    }

    /// <summary>Whether a placed object facing its way is of the early list (<c>early_list</c>).</summary>
    private static bool IsEarlyObject(Simulation simulation, RoomObject placed) =>
        simulation.Content.FindObject(placed.ObjectType)?.Orientations.GetValueOrDefault(placed.Orientation) is { EarlyList: true };

    /// <summary>The early entities of a tile, drawn after its north wall (level_map::draw's oEarlyEntities).</summary>
    private void DrawEarlyEntities(SpriteBatch batch, TilePosition tile, int anchorX, int anchorY)
    {
        if (Simulation is not { } simulation || !_earlyByTile.TryGetValue(tile, out var list))
        {
            return;
        }

        foreach (var entity in list)
        {
            DrawEntity(batch, simulation, entity, tile, anchorX, anchorY);
        }
    }

    /// <summary>The parts of split objects, and of their users' animations, that <paramref name="tile"/> draws, each cropped to its column.</summary>
    private void DrawSplitParts(SpriteBatch batch, Simulation simulation, TilePosition tile, int anchorX, int anchorY)
    {
        if (!_splitPartsByTile.TryGetValue(tile, out var parts))
        {
            return;
        }

        foreach (var (entity, left, right) in parts)
        {
            _animations.SetColumnClip(left, right);
            DrawEntity(batch, simulation, entity, tile, anchorX, anchorY);
            _animations.ClearColumnClip();
        }
    }

    private void DrawEntity(SpriteBatch batch, Simulation simulation, Entity entity, TilePosition tile, int anchorX, int anchorY)
    {
        switch (entity)
        {
            case EntranceDoor door:
                _doors.Draw(batch, door, anchorX, anchorY, simulation.Tick);
                break;
            case Humanoid { Behaviour: GrimReaperBehaviour grim } when tile == grim.HoleTile:
                // gates_to_hell.lua: open (1602) until the grim reaper has gone, then closing (2552).
                var hole = grim.Phase == GrimReaperPhase.Closing ? GrimReaperBehaviour.HoleClosing : GrimReaperBehaviour.HoleIdle;
                var holeFirst = _animations.Animations.FirstFrame(hole.Id);
                var holeTicks = simulation.Tick - (grim.Phase == GrimReaperPhase.Closing ? grim.PhaseStart : 0);
                _animations.DrawFrame(batch, _animations.Animations.Advance(holeFirst, (int)(holeTicks % hole.Length)), anchorX, anchorY, NoLayers, flipHorizontally: false);
                break;
            case Humanoid { Behaviour: GrimReaperBehaviour { Phase: GrimReaperPhase.Closing } }:
                break;
            case Humanoid humanoid:
                _humanoids.Draw(batch, humanoid, anchorX, anchorY, simulation.Tick, HumanoidMoods.Icon(simulation, humanoid, humanoid.Id == _hoveredHumanoid, _hoveredRoom));
                break;
            case RoomObject placed:
                var tint = placed.Id == _buildTool.HighlightedObject ? PickUpTint : Color.White;
                var machine = placed.Machine is null ? null : simulation.Content.FindObject(placed.ObjectType)?.Machine;
                if (machine is not null && simulation.FindEntity(placed.RoomId) is Room { IsCrashed: true })
                {
                    _objects.DrawCrashed(batch, machine.Crashed.Id, placed.Tile, tint);
                    break;
                }

                if (placed.ObjectType == "surgeon_screen")
                {
                    // use_screen.lua's surgical_state: the outfits hanging on the screen, green scrubs and white coats.
                    var whites = simulation.Entities.Count(e => e is Humanoid { Type: HumanoidType.Surgeon } surgeon
                        && simulation.Map[surgeon.Tile.X, surgeon.Tile.Y].RoomId == placed.RoomId);
                    var screenAnimation = whites < 2 ? (whites > 0 ? SurgeonScreenBoth : SurgeonScreenGreens) : SurgeonScreenWhites;
                    _animations.DrawFrame(batch, _animations.Animations.FirstFrame(screenAnimation), 32 * (placed.Tile.X - placed.Tile.Y), 16 * (placed.Tile.X + placed.Tile.Y), NoLayers, flipHorizontally: false, tint);
                    break;
                }

                // OperatingTheatreRoom:setXRayOn: the viewer lights up (layer 11 set to 2) while a patient is in the theatre.
                var lit = placed.ObjectType == "x_ray_viewer" && simulation.FindEntity(placed.RoomId) is Room { PatientId: not 0 };
                _objects.Draw(batch, placed.ObjectType, placed.Tile, placed.Orientation, simulation.Tick, tint, placed.HangingClothes, placed.Plant?.State, lit ? XRayViewerLit : null, placed.Id);
                if (machine is not null)
                {
                    _objects.DrawSmoke(batch, machine.Smoke.Id, placed.Tile, placed.Orientation, placed.Machine!.RemainingUses, simulation.Tick);
                    if (placed.Machine.IsRepairCalled)
                    {
                        // Machine:setRepairingStatus: the gear icon (4564) turns above the machine (setMoodInfo, 96 pixels up)
                        // until the repair is done.
                        var gearFirst = _animations.Animations.FirstFrame(RepairGearIcon);
                        var gearFrame = _animations.Animations.Advance(gearFirst, (int)(simulation.Tick % _animations.Animations.Length(gearFirst)));
                        _animations.DrawFrame(batch, gearFrame, (32 * (placed.Tile.X - placed.Tile.Y)) - 1, (16 * (placed.Tile.X + placed.Tile.Y)) - 96, NoLayers, flipHorizontally: false);
                    }
                }

                break;
            case Rat rat:
                var (_, ratAnimation, offsetX, offsetY) = RatDrawing(rat);
                var ratFirst = _animations.Animations.FirstFrame(ratAnimation);
                var ratFrame = _animations.Animations.Advance(ratFirst, (int)(simulation.Tick % _animations.Animations.Length(ratFirst)));
                _animations.DrawFrame(batch, ratFrame, anchorX + offsetX, anchorY + offsetY, NoLayers, flipHorizontally: false);
                break;
            case Litter litter:
                _animations.DrawFrame(batch, _animations.Animations.FirstFrame(litter.Animation), anchorX, anchorY, NoLayers, flipHorizontally: litter.IsMirrored);
                break;
            case RoomDoor { IsSwing: true } swingDoor:
                DrawSwingDoor(batch, simulation, swingDoor, tile, anchorX, anchorY);
                break;
            case RoomDoor roomDoor:
                var animation = roomDoor.Edge == EdgeOrientation.North ? RoomDoorNorth : RoomDoorWest;
                _animations.DrawFrame(batch, _animations.Animations.FirstFrame(animation), anchorX, anchorY, NoLayers, flipHorizontally: false);
                break;
        }
    }

    /// <summary>
    /// One of a pair of swing doors (objects/doors/swing_door_left.lua, swing_door_right.lua): standing, or swinging in or
    /// out, the left one from the start of the swing and the right one once the humanoid is through; mirrored in a west
    /// wall, where only the left door has its own standing animation.
    /// </summary>
    private void DrawSwingDoor(SpriteBatch batch, Simulation simulation, RoomDoor door, TilePosition tile, int anchorX, int anchorY)
    {
        var west = door.Edge == EdgeOrientation.West;
        var slave = tile == door.SlaveTile;
        var animation = slave ? (west ? SwingDoorLeftWest : SwingDoorLeftNorth) : SwingDoorRightNorth;
        var flip = west && !slave;
        if (simulation.Content.SwingDoor is { } swing && door.SwingStart >= 0)
        {
            var swingAnimation = slave
                ? (door.SwingIn ? swing.SlaveIn : swing.SlaveOut)
                : (door.SwingIn ? swing.MasterIn : swing.MasterOut);
            var elapsed = simulation.Tick - door.SwingStart - (slave ? 0 : door.SwingCrossTicks);
            if (elapsed >= 0 && elapsed < swingAnimation.Length)
            {
                var first = _animations.Animations.FirstFrame(swingAnimation.Id);
                _animations.DrawFrame(batch, _animations.Animations.Advance(first, (int)elapsed), anchorX, anchorY, NoLayers, flipHorizontally: west);
                return;
            }
        }

        _animations.DrawFrame(batch, _animations.Animations.FirstFrame(animation), anchorX, anchorY, NoLayers, flipHorizontally: flip);
    }

    /// <summary>The second parts of objects drawn on <paramref name="tile"/>, such as the radiation shield's front.</summary>
    private void DrawSlaveParts(SpriteBatch batch, TilePosition tile)
    {
        if (!_slavePartsByTile.TryGetValue(tile, out var slaves))
        {
            return;
        }

        foreach (var master in slaves)
        {
            var tint = master.Id == _buildTool.HighlightedObject ? PickUpTint : Color.White;
            _objects.DrawSlave(batch, master.ObjectType, master.Tile, master.Orientation, tint);
        }
    }

    /// <summary>
    /// The local player's floating dollar signs on <paramref name="tile"/>, over everything else on it (CorsixTH's
    /// <c>DrawingLayers.FloatingDollars</c>).
    /// </summary>
    private void DrawFloatingDollars(SpriteBatch batch, Simulation simulation, TilePosition tile, int anchorX, int anchorY)
    {
        if (_session is not { } session)
        {
            return;
        }

        foreach (var sign in simulation.FloatingDollarSigns)
        {
            if (sign.Tile == tile && sign.Player == session.Player)
            {
                _floatingDollars.Draw(batch, sign, anchorX, anchorY, simulation.Tick);
            }
        }
    }

    /// <summary>
    /// Where the adviser is: CorsixTH's bottom panel is 640 by 48 pixels, centred at the bottom of the screen, and the
    /// adviser's window sits at (378, -16) in it, his animation 200 pixels further right.
    /// </summary>
    private static Point AdviserOrigin(Viewport viewport) => new(((viewport.Width - 640) / 2) + 378, viewport.Height - 48 - 16);

    /// <summary>
    /// UIAdviser:draw: the adviser's animation, and while he talks the balloon (Panel02V's sprites 37 to 39, the middle
    /// one repeated to the balloon's width) with his speech in the black font.
    /// </summary>
    private void DrawAdviser(SpriteBatch batch, Viewport viewport)
    {
        if (_adviserFigure.Animation is not { } animation)
        {
            return;
        }

        var origin = AdviserOrigin(viewport);
        var set = _animations.Animations;
        _animations.DrawFrame(batch, set.Advance(set.FirstFrame(animation), _adviserFigure.Frame), origin.X + 200, origin.Y, NoAdviserLayers, flipHorizontally: false);
        if (_adviserFigure.Speech is not { } speech || _panelSprites is null || _blackText is null)
        {
            return;
        }

        var width = AdviserFigure.BalloonWidth(speech);
        var left = BalloonLeft(origin, width);
        for (var dx = 0; dx <= width; dx += 16)
        {
            _panelSprites.Draw(batch, 38, origin.X + 139 - dx, origin.Y - 25, Color.White);
        }

        _panelSprites.Draw(batch, 37, left - 16, origin.Y - 25, Color.White);
        _panelSprites.Draw(batch, 39, origin.X + 155, origin.Y - 40, Color.White);
        var y = origin.Y - 20;
        var tint = _blackText.IsTrueType ? Color.Black : (Color?)null;
        foreach (var line in WrapToWidth(speech.TrimEnd('/'), width + 60, text => _blackText.Measure(text).Width))
        {
            _blackText.Draw(batch, line, new Vector2(left - 8, y), tint: tint);
            y += _blackText.LineHeight;
        }
    }

    private static readonly byte[] NoAdviserLayers = new byte[Humanoid.LayerCount];

    /// <summary>The left edge of the balloon's repeated middle part, the last one drawn.</summary>
    private static int BalloonLeft(Point origin, int width) => origin.X + 139 - (width / 16 * 16);

    /// <summary>
    /// UIAdviser:onMouseDown: a left click on the adviser or his balloon moves on to the next message, or he gets down if
    /// there is none; a right click sends him down and the waiting messages to the history unsaid.
    /// </summary>
    /// <returns>Whether the click was on him, so nothing else gets it.</returns>
    private bool ClickAdviser(MouseState mouse)
    {
        var left = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        var right = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;
        if ((!left && !right) || !_adviserFigure.IsUp || _adviserFigure.Animation is not { } animation)
        {
            return false;
        }

        var origin = AdviserOrigin(_device.Viewport);
        var set = _animations.Animations;
        var onHim = _animations.HitTest(set.Advance(set.FirstFrame(animation), _adviserFigure.Frame), origin.X + 200, origin.Y, NoAdviserLayers, false, mouse.X, mouse.Y);
        if (!onHim && _adviserFigure.Speech is { } speech && _panelSprites is not null)
        {
            var balloonLeft = BalloonLeft(origin, AdviserFigure.BalloonWidth(speech)) - 16;
            var balloon = new Rectangle(balloonLeft, origin.Y - 40, origin.X + 155 + _panelSprites.WidthOf(39) - balloonLeft, 15 + _panelSprites.HeightOf(38));
            onHim = balloon.Contains(mouse.X, mouse.Y);
        }

        if (!onHim)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (right)
        {
            _adviser.DismissAll();
            _adviserFigure.Hide(now);
        }
        else if (_adviser.Queued > 0)
        {
            _adviser.Next(now);
        }
        else
        {
            _adviser.Next(now);
            _adviserFigure.Hide(now);
        }

        return true;
    }

    /// <summary>Breaks text into lines no wider than <paramref name="width"/> pixels at spaces (longer words stay whole).</summary>
    internal static IReadOnlyList<string> WrapToWidth(string text, int width, Func<string, int> measure)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var longer = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(longer) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = longer;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// UIPlaceStaff:draw: the member of staff being hired or carried stands, in the first frame of their idle animation
    /// facing east, on the tile they would be put on, grey where they cannot be.
    /// </summary>
    private void DrawPlacingStaff(SpriteBatch batch)
    {
        if (Simulation is not { } simulation || _session is null || _staffTile is not { } point)
        {
            return;
        }

        var tile = new TilePosition(point.X, point.Y);
        Humanoid shown;
        bool valid;
        if (_hiring is { } hiring)
        {
            var index = simulation.AvailableStaff(hiring.Class).ToList().IndexOf(hiring.Profile);
            if (index < 0)
            {
                return;
            }

            shown = Humanoid.CreateStaff(0, tile, hiring.Profile, _session.Player);
            valid = StaffHiring.Check(simulation, hiring.Class, index, tile, _session.Player) == StaffHireProblem.None;
        }
        else if (_pickedUpStaff is { } carried && simulation.FindEntity(carried) is Humanoid held)
        {
            shown = held;
            valid = held.Tile == tile || StaffMoving.Check(simulation, carried, tile, _session.Player) == StaffHireProblem.None;
        }
        else
        {
            return;
        }

        _humanoids.DrawPlacing(batch, shown, 32 * (tile.X - tile.Y), 16 * (tile.X + tile.Y), valid ? Color.White : StaffBlockedTint);
    }

    private void DrawOverlay(SpriteBatch batch, LevelMap map, Rectangle view)
    {
        // While building, the blueprint and the object being placed show where the cursor is, as in the original.
        // The member of staff being placed shows where they go instead.
        var hovered = _buildTool.IsActive || _furnishTool.IsActive || _hiring is not null || _pickedUpStaff is not null ? null : _hoveredTile;
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
    internal static Color TemperatureColor(ushort temperature)
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

    /// <summary>
    /// CorsixTH's dialogs' hotkeys, read from <c>hotkeys.txt</c> (see <see cref="Hotkeys"/>): by default F1 bank manager,
    /// F2 bank statement, F4 town map, F5 casebook, F6 research, F7 progress report, F8 graphs, F9 policy, F10 machines, Ctrl+H the
    /// adviser's history, Y hire, C faxes, F11 cheats, each opening its window or closing it again. F3 (staff
    /// management) stays the level list for now; Shift+F3 opens staff management.
    /// </summary>
    private void UpdateWindowKeys(KeyboardState keys, Func<Keys, bool> pressed)
    {
        // A new fax opens the faxes (C opens and closes them), as the original's message icon asks for a look.
        var faxes = FaxWindow.Pending(Simulation!, _session!.Player).ToHashSet(StringComparer.Ordinal);
        if (faxes.Any(fax => !_seenFaxes.Contains(fax)))
        {
            _windows.Open(new FaxWindow());
            _sounds.Play("NewFax.wav"); // UIBottomPanel:queueMessage
        }

        _seenFaxes = faxes;
        if (!ReferenceEquals(_session, _repairSession))
        {
            _repairSession = _session;
            _repairs.Reset();
            _announcements.Reset();
            _worldSounds.Reset();
        }

        // The announcer's: announceRepair's (critical) and the hospital's; the rest are plain sounds.
        var staffedDesk = Simulation!.Entities.Any(e => e is Humanoid { Behaviour: ReceptionistBehaviour { Phase: ReceptionistPhase.Staffing } } receptionist && receptionist.Employer == _session.Player);
        foreach (var sound in _repairs.Update(Simulation!, _session.Player))
        {
            _announcer.Request(sound, AnnouncementPriority.Critical, Simulation!.Hours, staffedDesk);
        }

        foreach (var sound in _announcements.Update(Simulation!, _session.Player))
        {
            _sounds.Play(sound);
        }

        foreach (var (name, priority) in _announcements.TakeAnnouncements())
        {
            _announcer.Request(name, priority, Simulation!.Hours, staffedDesk);
        }

        _worldSounds.Update(Simulation!, Environment.TickCount64, _session.IsPaused, (name, tile) => _sounds.PlayAt(name, ScreenOf(tile), ViewSize), _sounds.Play, name => _announcer.Request(name, AnnouncementPriority.Critical, Simulation!.Hours, staffedDesk));
        _announcer.Update(Environment.TickCount64, Simulation!.Hours, _session.IsPaused, staffedDesk, _config.PlayAnnouncements, _sounds.PlayAnnouncement);

        _hellSounds.Update(Simulation!, _session.Speed, Environment.TickCount64, hole => _sounds.PlayAt("LAVA00*.WAV", ScreenOf(hole), ViewSize));
        if (HotkeyInput.Pressed(_hotkeys["ingame_openFirstMessage"], keys, pressed))
        {
            _windows.Toggle(new FaxWindow());
        }

        var watch = WatchWindow.IsRunning(Simulation!, _session.Player);
        if (watch && !_watchRan)
        {
            _windows.Open(new WatchWindow());
        }

        _watchRan = watch;

        // The annual report at the year end, the winning letter or losing fax at the level's end.
        var hospital = Simulation!.Hospitals[_session.Player];
        if (_lastYearSeen is { } seen && Simulation.Date.Year > seen)
        {
            _windows.Open(new AnnualReportWindow());
        }

        _lastYearSeen = Simulation.Date.Year;
        if (hospital.Outcome != EndOutcome.None && hospital.Outcome != _lastOutcome)
        {
            _windows.Open(new LevelEndWindow());
        }

        _lastOutcome = hospital.Outcome;
        if (HotkeyInput.Pressed(_hotkeys["ingame_toggleAdvisor"], keys, pressed))
        {
            ToggleAdviser();
        }

        (string Action, Func<UiWindow> Window)[] dialogs =
        [
            ("ingame_panel_hireStaff", () => new HireWindow()),
            ("ingame_panel_staffManage", () => new StaffManagementWindow()),
            ("ingame_panel_bankManager", () => new BankManagerWindow()),
            ("ingame_panel_bankStats", () => new BankStatementWindow()),
            ("ingame_panel_townMap", () => new TownMapWindow()),
            ("ingame_panel_casebook", () => new CasebookWindow()),
            ("ingame_panel_research", () => new ResearchWindow()),
            ("ingame_panel_status", () => new ProgressReportWindow()),
            ("ingame_panel_charts", () => new GraphWindow()),
            ("ingame_panel_policy", () => new PolicyWindow()),
            ("ingame_panel_machineMenu", () => new MachineMenuWindow()),
            ("ingame_panel_adviserHistory", () => new AdviserHistoryWindow()),
            ("ingame_showCheatWindow", () => new CheatsWindow()),
        ];
        foreach (var (action, window) in dialogs)
        {
            if (HotkeyInput.Pressed(_hotkeys[action], keys, pressed))
            {
                _windows.Toggle(window());
                _sounds.Play("selectx.wav");
            }
        }
    }

    /// <summary>
    /// Lets the adviser watch the game and move on through his messages; a new game or a loaded one starts him
    /// afresh.
    /// </summary>
    private void UpdateAdviser()
    {
        if (!ReferenceEquals(_session, _adviserSession))
        {
            _adviserSession = _session;
            _adviser.Clear();
            _adviserFigure.Reset();
            _adviserChecks = new AdviserChecks(_adviser, _adviserRandom);

            // The tutorial belongs to the game it was started with: another game ends it.
            if (_tutorial is not null && !ReferenceEquals(_session, _tutorialSession))
            {
                _tutorial = null;
                Tutorial.Current = null;
            }

            _tutorial?.SayAgain();

            // A game saved during the tutorial comes back without it: its opening watch starts afresh.
            if (_tutorial is null && Simulation is { } loaded && _session!.Player < loaded.Hospitals.Count && loaded.Hospitals[_session.Player].IsOpeningWatchHeld)
            {
                _session.Submit(new HoldOpeningWatchCommand(false));
            }

            _roomsDescribed.Clear();
            if (IsWindowHostReady)
            {
                // Rooms already built in a loaded game have had their description.
                _roomsDescribed.UnionWith(Simulation!.Entities.OfType<Room>().Where(room => room.IsActive).Select(room => room.RoomType));

                // CorsixTH shows a tip with the main menu; here, with the first game.
                if (!_tipShown && _editorTool is null)
                {
                    _tipShown = true;
                    _windows.Open(new TipOfTheDayWindow(_adviserRandom.Next(TipOfTheDayWindow.Tips.Count)));
                }
            }
        }

        if (IsWindowHostReady)
        {
            var now = Environment.TickCount64;
            _adviserChecks.StartedWithTutorial = _tutorialSession is not null && ReferenceEquals(_session, _tutorialSession);
            _adviserChecks.Observe(Simulation!, _session!.Player, _texts, now);
            _adviser.Update(now);
            _adviserFigure.Update(now, _adviser.Current);
            DescribeNewRooms(Simulation!, _session.Player);
        }
    }

    /// <summary>
    /// <c>Room:roomFinished</c>: the first time a kind of room is built, its description from the game's texts.
    /// </summary>
    private void DescribeNewRooms(Simulation simulation, int player)
    {
        foreach (var room in simulation.Entities.OfType<Room>())
        {
            if (room.IsActive && RoomBuilder.IsOwnedBy(simulation.Map, room, player) && _roomsDescribed.Add(room.RoomType)
                && _config.RoomInformationDialogs && _texts?.RoomDescription(room.RoomType) is { Count: > 0 } description)
            {
                _windows.Open(new InformationWindow(description, simulation.Content.FindRoom(room.RoomType)?.Name ?? "Information"));
            }
        }
    }

    Adviser IWindowHost.Adviser => _adviser;

    /// <summary>Whether there is a game with a hospital of the local player for windows to show.</summary>
    private bool IsWindowHostReady => _session is { Simulation: { } simulation, Player: var player } && player < simulation.Hospitals.Count;

    Simulation IWindowHost.Simulation => Simulation!;

    int IWindowHost.Player => _session!.Player;

    OriginalTexts? IWindowHost.Texts => _texts;

    void IWindowHost.Submit(Command command) => _session?.Submit(command);

    void IWindowHost.Open(UiWindow window) => _windows.Open(window);

    void IWindowHost.Close(UiWindow window) => _windows.Close(window);

    void IWindowHost.Notice(string text) => ShowNotice(text);

    void IWindowHost.PlaySound(string name) => _sounds.Play(name);

    void IWindowHost.PickUpStaff(int staffId)
    {
        _pickedUpStaff = staffId;
        _sounds.Play("pickup.wav");
        ShowNotice("Picked up: click to put them down, right click lets them go");
    }

    void IWindowHost.CenterOn(TilePosition tile) => _camera.Center = Camera.TileCenter(tile.X, tile.Y);

    bool IWindowHost.HasNextLevel => _session is LocalSession && _levelIndex + 1 < _levels.Count;

    OriginalGraphics? IWindowHost.Graphics => _graphics;

    string IWindowHost.PlayerName => _config.PlayerName is { Length: > 0 } name ? name : "Player";

    string? IWindowHost.HotkeyText(string action) => _hotkeys[action]?.ToString();

    void IWindowHost.StartLevel(bool next)
    {
        if (_session is not LocalSession)
        {
            return;
        }

        _windows.CloseAll();
        SelectLevel(next ? Math.Min(_levelIndex + 1, _levels.Count - 1) : _levelIndex);
    }

    /// <summary>The menu's items that are not windows.</summary>
    private void MenuAction(string action)
    {
        switch (action)
        {
            case "save" or "load" when _session is not LocalSession:
                ShowNotice("Only local games can be saved and loaded");
                break;
            case "save" or "load" when _buildTool.IsActive || _furnishTool.IsActive:
                ShowNotice("Finish building before saving or loading");
                break;
            case "save" or "load":
                _windows.Open(new SaveLoadWindow(_saves, action == "save", SaveGameTo, LoadGame));
                break;
            case "quicksave" or "quickload":
                QuickSave(load: action == "quickload");
                break;
            case "restart":
                _windows.Open(new ConfirmWindow(Confirmation(7, "Are you sure you want to restart the level?"), () => ((IWindowHost)this).StartLevel(next: false)));
                break;
            case "quit":
                _windows.Open(new ConfirmWindow(Confirmation(1, "Are you sure you want to quit?"), () => ExitRequested = true));
                break;
            case "options":
                _windows.Open(NewOptionsWindow());
                break;
            case "savereplay":
                SaveReplay();
                break;
            case "replays":
                _windows.Open(new ReplayListWindow(Path.Combine(_saves.Directory, Replay.Folder), WatchReplay));
                break;
            case "newgame":
                _windows.Open(new NewGameWindow(AvailableDifficulties(_install), _config.PlayerName, (difficulty, tutorial) => StartCampaign(difficulty, tutorial)));
                break;
            case "levels" when _levels.Count > 0:
                _levelList = true;
                break;
        }
    }

    /// <summary>Saves the replay of the local game so far to the saves folder's replays.</summary>
    private void SaveReplay()
    {
        if (_session is not LocalSession local)
        {
            ShowNotice("Only local games are recorded");
            return;
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(local.Name)} {DateTime.Now:yyyy-MM-dd HH-mm-ss}{Replay.Extension}");
        var path = Path.Combine(_saves.Directory, Replay.Folder, name);
        try
        {
            local.Recording.Finish(local.Simulation);
            local.Recording.Save(path);
            ShowNotice($"Saved the replay {Path.GetFileNameWithoutExtension(path)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice($"Cannot save the replay: {e.Message}");
        }
    }

    /// <summary>Plays a replay back in place of the game.</summary>
    private void WatchReplay(string path)
    {
        ReplaySession replay;
        try
        {
            replay = new ReplaySession(Path.GetFileNameWithoutExtension(path), Replay.Load(path), BaseGameContent.Create());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            ShowNotice($"Cannot watch {Path.GetFileName(path)}: {e.Message}");
            return;
        }

        _session?.Dispose();
        replay.SetSpeed(GameSpeed.Normal);
        _session = replay;
        _error = null;
        _demoPending = _demoDeskPending = _demoDiagnosisPending = _demoMachinePending = _demoToiletsPending = false;
        _demoFurniture.Clear();
        PrepareMapIfReady();
        ShowNotice("Watching a replay: your commands are not taken; pick a level or load a game to play again");
    }

    /// <summary>CorsixTH's <c>confirmation</c> texts, S[35][index], with an English stand-in.</summary>
    private string Confirmation(int index, string fallback) => _texts?.String(35, index) ?? fallback;

    /// <summary>Saves the local game to a file, saying so or why not.</summary>
    private bool SaveGameTo(string path)
    {
        if (_session is not LocalSession local)
        {
            return false;
        }

        try
        {
            SaveGameStore.Save(path, local.Name, local.Simulation);
            ShowNotice($"Saved {Path.GetFileNameWithoutExtension(path)}");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice($"Cannot save: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// <c>GameUI:toggleAdviser</c> (Shift+A by default): the adviser falls silent or speaks again, and the setting is
    /// saved.
    /// </summary>
    private void ToggleAdviser()
    {
        _config = _config with { AdviserDisabled = !_config.AdviserDisabled };
        _adviser.IsEnabled = !_config.AdviserDisabled;
        if (!_adviser.IsEnabled)
        {
            _adviser.DismissAll();
        }

        if (_configPath is not null)
        {
            try
            {
                _config.Save(_configPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ShowNotice($"Cannot save the settings: {e.Message}");
                return;
            }
        }

        ShowNotice(_adviser.IsEnabled ? "The adviser is back" : "The adviser is quiet");
    }

    /// <summary>The options, with the hotkey assignment dialog (<c>UIOptions:buttonHotkey</c>), which goes back to them.</summary>
    private OptionsWindow NewOptionsWindow() =>
        new(_config, ApplyConfig, InstalledLanguageNames(_install), () => _windows.Open(new HotkeyAssignWindow(_hotkeys, SaveHotkeys, () => _windows.Open(NewOptionsWindow()))));

    /// <summary><c>UIHotkeyAssign:buttonAccept</c>: the keys are used at once and saved to <c>hotkeys.txt</c>.</summary>
    private void SaveHotkeys(Hotkeys hotkeys)
    {
        _hotkeys = hotkeys;
        if (_configPath is null || Path.GetDirectoryName(Path.GetFullPath(_configPath)) is not { } folder)
        {
            return;
        }

        try
        {
            hotkeys.Save(Path.Combine(folder, Hotkeys.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice($"Cannot save the hotkeys: {e.Message}");
        }
    }

    /// <summary>
    /// The hotkeys from <c>hotkeys.txt</c> beside the settings file (written with the defaults if missing); the defaults
    /// without a settings file or when the file can't be read.
    /// </summary>
    private static Hotkeys LoadHotkeys(string? configPath)
    {
        if (configPath is null || Path.GetDirectoryName(Path.GetFullPath(configPath)) is not { } folder)
        {
            return new Hotkeys();
        }

        try
        {
            return Hotkeys.Load(Path.Combine(folder, Hotkeys.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Cannot read {Hotkeys.FileName}: {e.Message}; using the default keys.");
            return new Hotkeys();
        }
    }

    /// <summary>
    /// The new game dialog's Start (<c>UINewGame:startGame</c>): the original campaign at the difficulty, from its first
    /// level; Page Up/Down, F3 and the next level go through the campaign's levels from then on.
    /// </summary>
    private void StartCampaign(Difficulty difficulty, bool tutorial = false)
    {
        if (_session is not null and not LocalSession)
        {
            ShowNotice("A new game can only be started outside a network game");
            return;
        }

        var levels = OriginalLevels(_install, difficulty);
        if (levels.Count == 0)
        {
            ShowNotice("The original levels are not installed");
            return;
        }

        _levels = levels;
        _levelList = false;
        SelectLevel(0);
        if (tutorial)
        {
            StartTutorial();
        }
    }

    /// <summary><c>UINewGame:startGame</c> with the tutorial ticked: <c>GameUI:startTutorial</c>.</summary>
    private void StartTutorial()
    {
        _tutorial = new Tutorial(_adviser, () => _texts, FinishTutorial);
        _tutorialSession = _session;
        Tutorial.Current = _tutorial;

        // World:onTick starts no opening watch while the tutorial runs.
        _session?.Submit(new HoldOpeningWatchCommand(true));
        if (TutorialView() is { } view)
        {
            _tutorial.Start(view);
        }
    }

    /// <summary>
    /// The tutorial's last chapter (its begin_callback): the information on what to do next (introduction texts 15 to 17,
    /// and the level's own off the first level).
    /// </summary>
    private void FinishTutorial()
    {
        var paragraphs = new List<string>();
        foreach (var level in new[] { 15, 16, 17 })
        {
            if (_texts?.IntroductionText(level) is { } text)
            {
                paragraphs.AddRange(text.Split("//", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        if (_levelIndex != 0 && _texts?.IntroductionText(1) is { } first)
        {
            paragraphs.AddRange(first.Split("//", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (paragraphs.Count == 0)
        {
            paragraphs.Add("That's the basic mechanics of putting a hospital together. Build diagnosis and treatment rooms, hire staff for them, and keep an eye on your reputation.");
        }

        _windows.Open(new InformationWindow(paragraphs));

        // ...and the opening watch, from the start.
        _session?.Submit(new HoldOpeningWatchCommand(false));
    }

    /// <summary>What the tutorial follows this frame, or null without a game of the local player's.</summary>
    private TutorialView? TutorialView()
    {
        if (_session is not { Simulation: { } simulation } session || session.Player >= simulation.Hospitals.Count)
        {
            return null;
        }

        var player = session.Player;
        var owner = player + 1;
        var map = simulation.Map;
        bool Ours(TilePosition tile) => map.GetParcelOwner(map[tile.X, tile.Y].Parcel) == owner;
        var staff = simulation.Entities.OfType<Humanoid>().Where(h => h.Employer == player && h.Profile is not null && !h.IsFired).ToList();
        var build = _buildTool.TutorialState(simulation, player);
        bool? hiringValid = null;
        if (_hiring is { } hiring && _staffTile is { } hovered)
        {
            var index = simulation.AvailableStaff(hiring.Class).ToList().IndexOf(hiring.Profile);
            hiringValid = index >= 0 && StaffHiring.Check(simulation, hiring.Class, index, new TilePosition(hovered.X, hovered.Y), player) == StaffHireProblem.None;
        }

        return new TutorialView
        {
            CorridorWindowOpen = _windows.Windows.OfType<FurnishCorridorWindow>().Any(),
            ReceptionDeskValid = _furnishTool.Ghost is { ObjectType: ReceptionDesks.ObjectId } ghost ? ghost.Tint == Color.White : _furnishTool.IsActive && _furnishTool.PlacingId == ReceptionDesks.ObjectId ? false : null,
            ReceptionDesks = simulation.Entities.OfType<RoomObject>().Count(o => o.ObjectType == ReceptionDesks.ObjectId && Ours(o.Tile)),
            HireWindowOpen = _windows.Windows.OfType<HireWindow>().Any(),
            HireCategory = _windows.Windows.OfType<HireWindow>().FirstOrDefault()?.Category,
            HiringClass = _hiring?.Class,
            HiringValid = hiringValid == true,
            Receptionists = staff.Count(h => h.Profile!.Class == StaffClass.Receptionist),
            Doctors = staff.Count(h => h.Profile!.Class == StaffClass.Doctor),
            RoomListOpen = _roomList,
            BuildStage = build.Stage,
            BuildRoomType = build.RoomType,
            Blueprint = build.Blueprint,
            PlacementValid = build.Valid,
            MissingObjects = build.Missing,
            ActiveGpOffices = simulation.Entities.OfType<Room>().Count(r => r is { RoomType: "gp", IsActive: true } && Ours(new TilePosition(r.Area.X, r.Area.Y))),
            InformationWindowOpen = _windows.Windows.OfType<InformationWindow>().Any(),
        };
    }

    /// <summary>
    /// <c>UIMainMenu</c>, when the game starts with nothing to play given: the first level waits paused behind it until
    /// a game is started or loaded.
    /// </summary>
    private void OpenMainMenu()
    {
        _session?.SetSpeed(GameSpeed.Pause);
        _mainMenu = new MainMenuWindow(MainMenuAction, typeof(MapViewScreen).Assembly.GetName().Version?.ToString(3) ?? "?");
        _windows.Open(_mainMenu);
    }

    private void CloseMainMenu()
    {
        if (_mainMenu is not null)
        {
            _windows.Close(_mainMenu);
            _mainMenu = null;
        }
    }

    /// <summary>The main menu's items (<c>UIMainMenu:button*</c>).</summary>
    private void MainMenuAction(string action)
    {
        var saves = _saves.Directory;
        switch (action)
        {
            case "newgame":
                _windows.Open(new NewGameWindow(AvailableDifficulties(_install), _config.PlayerName, (difficulty, tutorial) => StartCampaign(difficulty, tutorial)));
                break;
            case "campaign":
                _windows.Open(new CustomCampaignWindow(CustomGames.FindCampaigns(GameConfig.UserFolder(_config.Campaigns, saves, "Campaigns")), StartCustomCampaign));
                break;
            case "scenario":
                _windows.Open(new CustomLevelWindow(CustomGames.FindLevels(GameConfig.UserFolder(_config.Levels, saves, "Levels")), _config.FreeBuildMode, StartCustomLevel));
                break;
            case "continue" when _saves.MostRecentSave() is { } latest:
                LoadGame(latest);
                break;
            case "continue":
                _windows.Open(new InformationWindow(["Error while loading game: There are no saved games."]));
                break;
            case "load":
                _windows.Open(new SaveLoadWindow(_saves, false, SaveGameTo, LoadGame));
                break;
            case "options":
                _windows.Open(NewOptionsWindow());
                break;
            case "mapeditor":
                StartMapEditor(MapEditor.Blank(), null);
                break;
            case "exit":
                _windows.Open(new ConfirmWindow("You are about to quit from CorsixNet. Are you sure this is what you want to do?", () => ExitRequested = true));
                break;
        }
    }

    /// <summary><c>UICustomGame:buttonLoadLevel</c>: the level on its own, with free build if ticked.</summary>
    private void StartCustomLevel(CustomLevelEntry level, bool freeBuild)
    {
        _levels = [new LevelSource(Path.GetFileName(level.Path), () => freeBuild
            ? LevelMapLoader.LoadFile(level.Path, _install).WithFreeBuild()
            : LevelMapLoader.LoadFile(level.Path, _install))];
        _levelList = false;
        SelectLevel(0);
    }

    /// <summary><c>UICustomCampaign:startCampaign</c>: the campaign's levels from its first.</summary>
    private void StartCustomCampaign(CustomCampaignEntry campaign)
    {
        IReadOnlyList<string> paths;
        try
        {
            paths = LevelMapLoader.LoadCampaign(campaign.Path).LevelPaths;
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            _windows.Open(new InformationWindow([$"Cannot start the campaign: {e.Message}"]));
            return;
        }

        _levels = [.. paths.Select(path => new LevelSource(Path.GetFileName(path), () => LevelMapLoader.LoadFile(path, _install)))];
        _levelList = false;
        SelectLevel(0);
    }

    /// <summary>
    /// <c>UI:showMapEditor</c>: the map is shown paused, with nothing on it but its entrance doors, and the editor's
    /// window and cursor take the mouse.
    /// </summary>
    private void StartMapEditor(MapEditor editor, string? path)
    {
        CloseMainMenu();
        _windows.CloseAll();
        foreach (var panel in _windows.Windows.OfType<BottomPanel>().ToList())
        {
            _windows.Close(panel);
        }

        _session?.Dispose();
        var simulation = new Simulation(editor.Map, seed: 1, BaseGameContent.Create(), LevelRules.Free);
        var local = new LocalSession(path is null ? "Map Editor" : Path.GetFileName(path), simulation, _saves, autosaveFrequency: 0);
        local.SetSpeed(GameSpeed.Pause);
        _session = local;
        _error = null;
        _editorTool = new MapEditorTool(editor) { Changed = () => { } };
        _editorPath = path;
        PrepareMapIfReady();
        OpenMapEditorWindow();
    }

    private void OpenMapEditorWindow()
    {
        if (_editorTool is { } tool && !_windows.Windows.OfType<MapEditorWindow>().Any())
        {
            _windows.Open(new MapEditorWindow(tool, _blocks, MapEditorAction));
        }
    }

    /// <summary>The editor's mouse on the map; the parcels' overlay shows while parcels are being set.</summary>
    private void UpdateMapEditor(MapEditorTool tool, MouseState mouse)
    {
        var tile = _hoveredTile is { } point ? new TilePosition(point.X, point.Y) : (TilePosition?)null;
        tool.Update(
            tile,
            mouse.LeftButton == ButtonState.Pressed,
            mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released,
            mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed,
            mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released,
            mouse.RightButton == ButtonState.Released && _previousMouse.RightButton == ButtonState.Pressed);
        if (tool.Mode == EditorMode.Parcel)
        {
            _overlay = MapOverlay.Parcels;
        }
        else if (_overlay == MapOverlay.Parcels)
        {
            _overlay = MapOverlay.None;
        }
    }

    /// <summary>The entrance doors the edited map places on <paramref name="tile"/>, closed.</summary>
    private void DrawEditorObjects(SpriteBatch batch, TilePosition tile, int anchorX, int anchorY)
    {
        if (_editorTool is not { } tool)
        {
            return;
        }

        foreach (var placement in tool.Editor.Objects)
        {
            if (placement.Tile != tile || placement.ObjectType is not (MapEditor.EntranceLeftDoor or MapEditor.EntranceRightDoor))
            {
                continue;
            }

            var west = (placement.Direction & 1) != 0;
            var animation = (placement.ObjectType == MapEditor.EntranceRightDoor, west) switch
            {
                (true, false) => 308,
                (true, true) => 312,
                (false, false) => 316,
                _ => 318,
            };
            _animations.DrawFrame(batch, _animations.Animations.FirstFrame(animation), anchorX, anchorY, NoLayers, flipHorizontally: false);
        }
    }

    /// <summary>The editor window's own items: save and load maps in the levels folder, a new blank map, and quitting.</summary>
    private void MapEditorAction(string action)
    {
        if (_editorTool is not { } tool)
        {
            return;
        }

        var folder = GameConfig.UserFolder(_config.Levels, _saves.Directory, "Levels");
        switch (action)
        {
            case "save":
                var path = _editorPath ?? NewMapPath(folder);
                try
                {
                    tool.Editor.Save(path);
                    _editorPath = path;
                    ShowNotice($"Saved the map as {path}");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _windows.Open(new InformationWindow([$"Cannot save the map: {e.Message}"]));
                }

                break;
            case "load":
                var maps = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.map").Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
                _windows.Open(new MapListWindow(maps, LoadEditorMap));
                break;
            case "restart":
                _windows.Open(new ConfirmWindow("Start again with a blank map? Changes not saved are lost.", () => StartMapEditor(MapEditor.Blank(), null)));
                break;
            case "quit":
                _windows.Open(new ConfirmWindow("Leave the map editor? Changes not saved are lost.", QuitMapEditor));
                break;
        }
    }

    /// <summary>A file name in the levels folder that no map has yet.</summary>
    private static string NewMapPath(string folder)
    {
        for (var i = 1; ; i++)
        {
            var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"Map {i}.map"));
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    private void LoadEditorMap(string path)
    {
        try
        {
            StartMapEditor(MapEditor.Open(MapFile.Parse(File.ReadAllBytes(path))), path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _windows.Open(new InformationWindow([$"Cannot load the map: {e.Message}"]));
        }
    }

    /// <summary>Back to the main menu, over the first level as when the game starts.</summary>
    private void QuitMapEditor()
    {
        _editorTool = null;
        _editorPath = null;
        _overlay = MapOverlay.None;
        _windows.CloseAll();
        _windows.Open(new BottomPanel(BottomPanelAction));
        SelectLevel(0);
        OpenMainMenu();
    }

    /// <summary>The options window's Apply: the new settings are saved to <c>config.txt</c> and used at once.</summary>
    private void ApplyConfig(GameConfig config)
    {
        _config = config;
        _adviser.IsEnabled = !config.AdviserDisabled;
        _scrolling = new MapScrolling(config.ScrollSettings());
        _sounds.Dispose();
        _sounds = SoundEffects.Load(_install, config);
        if (!string.Equals(config.UnicodeFont, _unicodeFonts.Configured, StringComparison.Ordinal))
        {
            _text.UseTrueType(null);
            _blackText?.UseTrueType(null);
            _bottomPanel?.UseTrueType(null);
            _graphics.UseTrueType(null);
            _unicodeFonts.Dispose();
            _unicodeFonts = new UnicodeFonts(config.UnicodeFont, AppContext.BaseDirectory);
        }

        _texts = LoadTexts(_install, config);
        ApplyLanguageFont();
        if (_configPath is not null)
        {
            try
            {
                config.Save(_configPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ShowNotice($"Cannot save the settings: {e.Message}");
                return;
            }
        }

        ShowNotice("Settings saved; the screen size changes on the next start");
    }

    /// <summary>Whether the room edit panel's <paramref name="button"/> was pressed; it counts once.</summary>
    private bool TakeButton(BuildButton button)
    {
        if (_buildButton != button)
        {
            return false;
        }

        _buildButton = null;
        return true;
    }

    /// <summary>The bottom panel's buttons that are not windows: the room list and the furnish corridor window.</summary>
    private void BottomPanelAction(string action)
    {
        if (_buildTool.IsActive || _furnishTool.IsActive)
        {
            _buildReminderUntil = Environment.TickCount64 + NoticeMilliseconds;
            return;
        }

        switch (action)
        {
            case "rooms":
                _roomList = !_roomList;
                break;
            case "corridor":
                _windows.Open(new FurnishCorridorWindow(definition => _furnishTool.Start(definition)));
                break;
            case "edit":
                // CorsixTH's edit room button: the next click on a room of the hospital edits it.
                _pickingRoomToEdit = true;
                _windows.CloseAll();
                ShowNotice("Click a room to edit it; right click or Esc cancels");
                break;
        }
    }

    /// <summary>
    /// Picking the room to edit: a click on an active room of the player's sends it to be edited (<c>Room:tryToEdit</c>)
    /// and opens it for furnishing; a right click gives up.
    /// </summary>
    private void PickRoomToEdit(Simulation simulation, bool clicked, MouseState mouse)
    {
        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            _pickingRoomToEdit = false;
            return;
        }

        if (!clicked || _hoveredTile is not { } target || !simulation.Map.Contains(target.X, target.Y))
        {
            return;
        }

        var player = _session!.Player;
        if (simulation.FindEntity(simulation.Map[target.X, target.Y].RoomId) is Room { IsCrashed: true } wreck)
        {
            // GameUI:onMouseUp: a wrecked room can only be cleared away, and only with remove_destroyed_rooms, for a fee.
            if (_config.RemoveDestroyedRooms && RemoveDestroyedRoomCommand.RemovalCost(simulation, wreck, player) is { } cost)
            {
                _pickingRoomToEdit = false;
                var question = string.Create(CultureInfo.InvariantCulture, $"Would you like to remove the room for ${cost}?");
                _windows.Open(new ConfirmWindow(question, () => _session.Submit(new RemoveDestroyedRoomCommand(wreck.Id))));
            }

            return;
        }

        if (simulation.FindEntity(simulation.Map[target.X, target.Y].RoomId) is Room { IsActive: true } room
            && RoomBuilder.IsOwnedBy(simulation.Map, room, player)
            && simulation.Content.FindRoom(room.RoomType) is { } definition)
        {
            _pickingRoomToEdit = false;
            _session.Submit(new EditRoomCommand(room.Id));
            _buildTool.StartEditing(definition, room.Id, simulation.Tick);
        }
    }

    void IWindowHost.StartHiring(StaffClass staffClass, int index)
    {
        _hiring = (staffClass, Simulation!.AvailableStaff(staffClass)[index]);
        _windows.CloseAll();
        ShowNotice("Click in the hospital to place the new member of staff; right click or Esc cancels");
    }

    /// <summary>
    /// <c>UIPlaceStaff</c>: the candidate on the cursor is hired onto the tile clicked if they may stand there; a right
    /// click (or Esc) puts them back on the list. A candidate gone from the list in the meantime is dropped.
    /// </summary>
    /// <returns>Whether a candidate is being placed, so the click was for that.</returns>
    private bool UpdateHiring(Simulation simulation, MouseState mouse)
    {
        if (_hiring is not { } hiring)
        {
            return false;
        }

        var index = simulation.AvailableStaff(hiring.Class).ToList().IndexOf(hiring.Profile);
        var click = StaffPlacing.Click(
            candidateGone: index < 0,
            left: mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released,
            right: mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released,
            paused: _session!.IsPaused);
        if (click == StaffPlacingClick.Cancel)
        {
            _hiring = null;
            return true;
        }

        if (click == StaffPlacingClick.Paused)
        {
            ShowNotice("The game is paused: unpause it to place the new member of staff");
            _sounds.Play("wrong2.wav");
            return true;
        }

        if (click == StaffPlacingClick.Place && _staffTile is { } point)
        {
            var tile = new TilePosition(point.X, point.Y);
            var problem = StaffHiring.Check(simulation, hiring.Class, index, tile, _session!.Player);
            if (problem == StaffHireProblem.None)
            {
                AdviseOnPlacement(simulation, hiring.Profile, tile, 0);
                _session.Submit(new HireStaffCommand(hiring.Class, index, tile));
                _sounds.Play("plac_st2.wav"); // UIPlaceStaff:close
                _hiring = null;
            }
            else
            {
                ShowNotice($"Cannot place them there: {problem}");
                _sounds.Play("wrong2.wav");
            }
        }

        return true;
    }

    /// <summary>
    /// <c>UIPlaceStaff</c> for a member of staff picked up (the staff window's Pick up, or M): a left click puts them
    /// down on the tile under the cursor, a right click lets them go where they are.
    /// </summary>
    /// <returns>Whether someone is being carried, so the click was for that.</returns>
    private bool UpdateCarrying(Simulation simulation, MouseState mouse)
    {
        if (_pickedUpStaff is not { } carried)
        {
            return false;
        }

        var click = StaffPlacing.Click(
            candidateGone: simulation.FindEntity(carried) is not Humanoid { IsDespawning: false, IsFired: false },
            left: mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released,
            right: mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released,
            paused: _session!.IsPaused);
        if (click == StaffPlacingClick.Place && _staffTile is null)
        {
            return true;
        }

        PutDownCarried(simulation, carried, _staffTile is { } at ? new TilePosition(at.X, at.Y) : default, click);
        return true;
    }

    /// <summary>
    /// <c>UIPlaceStaff:onMouseUp</c> and <c>close</c> for the member of staff carried: put down on <paramref name="hand"/>
    /// if they may stand there (on their own tile, simply let go), else they stay on the cursor and the adviser says so;
    /// a cancel lets them go where they are. Nothing happens while paused.
    /// </summary>
    private void PutDownCarried(Simulation simulation, int carried, TilePosition hand, StaffPlacingClick click)
    {
        switch (click)
        {
            case StaffPlacingClick.Paused:
                ShowNotice("The game is paused: unpause it to put the member of staff down");
                _sounds.Play("wrong2.wav");
                return;
            case StaffPlacingClick.Cancel:
                _pickedUpStaff = null;
                _sounds.Play("plac_st2.wav");
                return;
            case StaffPlacingClick.Place:
                break;
            default:
                return;
        }

        if (simulation.FindEntity(carried) is Humanoid held && held.Tile != hand)
        {
            var problem = StaffMoving.Check(simulation, carried, hand, _session!.Player);
            if (problem != StaffHireProblem.None)
            {
                // _A.placement_info.staff_cannot_place: they stay on the cursor.
                ShowNotice($"Cannot put the member of staff down there: {problem}");
                _sounds.Play("wrong2.wav");
                return;
            }

            AdviseOnPlacement(simulation, held.Profile, hand, carried);
            _session.Submit(new MoveStaffCommand(carried, hand));
        }

        _pickedUpStaff = null;
        _sounds.Play("plac_st2.wav");
    }

    private void ShowNotice(string text)
    {
        _notice = text;
        _noticeUntil = Environment.TickCount64 + NoticeMilliseconds;
    }

    private string StatusLine()
    {
        if (_editorTool is { } editor)
        {
            var name = _editorPath is null ? "new map" : Path.GetFileName(_editorPath);
            var at = editor.Cursor;
            var tile = editor.Editor.Map[at.X, at.Y];
            return string.Create(CultureInfo.InvariantCulture, $"MAP EDITOR   {name}   tile ({at.X}, {at.Y})   floor {tile.Floor}   north {tile.NorthWall}   west {tile.WestWall}   parcel {tile.Parcel}   Esc: editor window");
        }

        if (_session is null)
        {
            return "No game.";
        }

        var simulation = Simulation;
        var money = simulation is not null && _session.Player < simulation.Hospitals.Count
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"   {MoneyText.Format(simulation.Hospitals[_session.Player].Balance)}   cured {simulation.Hospitals[_session.Player].Cured}   died {simulation.Hospitals[_session.Player].Deaths}   reputation {simulation.Hospitals[_session.Player].Reputation}   value {MoneyText.Format(simulation.Hospitals[_session.Player].Value)}{LoanText(simulation.Hospitals[_session.Player])}{VipText(simulation.Hospitals[_session.Player])}{EmergencyText(simulation.Hospitals[_session.Player])}{EpidemicText(simulation, simulation.Hospitals[_session.Player])}{QuestionText(simulation, _session.Player)}{OutcomeText(simulation.Hospitals[_session.Player])}")
            : string.Empty;

        string EmergencyText(Hospital hospital) => hospital.Emergencies.Current switch
        {
            { IsAccepted: false } offer => $"   EMERGENCY: {offer.Victims} with {DiseaseName(offer.DiseaseId)}, {offer.Percentage} % to cure (E accept, Shift+E refuse)",
            { } emergency => $"   EMERGENCY: cured {emergency.Cured}, lost {emergency.Killed} of {emergency.Victims}, {emergency.WatchTimer + 1}/13 left",
            null => string.Empty,
        };

        string EpidemicText(Simulation simulation, Hospital hospital) => hospital.Epidemics.Current switch
        {
            { FaxExpires: > 0 } fax => $"   EPIDEMIC of {DiseaseName(fax.DiseaseId)}: declare for {MoneyText.Format(fax.DeclareFine)} (Shift+I) or cover up (I)",
            { IsWatchRunning: true } epidemic => $"   EPIDEMIC: {Epidemics.CountInfectedPatients(simulation, epidemic)} infected, {epidemic.WatchTimer + 1}/13 left (J vaccinate)",
            { } => "   EPIDEMIC: the health inspector is coming",
            null => string.Empty,
        };

        string QuestionText(Simulation simulation, int player)
        {
            if (FirstQuestion(simulation, player) is not { Behaviour: PatientBehaviour patient })
            {
                return string.Empty;
            }

            var count = simulation.Entities.Count(entity => entity is Humanoid { Behaviour: PatientBehaviour { Question: not PatientQuestion.None } } other && other.HospitalPlayer == player);
            return patient.Question == PatientQuestion.NoDiagnosisRoom
                ? $"   {count} ASKING: no diagnosis room for {DiseaseName(patient.DiseaseId)} ({patient.DiagnosisProgress / 10} %) (K wait, Shift+K send home, Ctrl+K guess cure)"
                : $"   {count} ASKING: no room to treat {DiseaseName(patient.DiseaseId)} (K wait, Shift+K send home)";
        }

        string DiseaseName(string diseaseId) => _texts?.DiseaseName(diseaseId) ?? diseaseId;

        string VipText(Hospital hospital) => hospital.Vips.IsInvitationPending ? $"   VIP {_texts?.VipName(hospital.Vips.InvitedName) ?? hospital.Vips.InvitedName.ToString(CultureInfo.InvariantCulture)} asks to visit (V invite, Shift+V refuse)" : string.Empty;

        static string LoanText(Hospital hospital) => hospital.Loan > 0 ? $"   loan {MoneyText.Format(hospital.Loan)}" : string.Empty;

        static string OutcomeText(Hospital hospital) => hospital.Outcome switch
        {
            EndOutcome.Won => "   LEVEL WON",
            EndOutcome.Lost => $"   LEVEL LOST ({hospital.LostTo?.Criterion})",
            _ => string.Empty,
        };

        var levels = _levels.Count > 0 ? $"   ({_levelIndex + 1} of {_levels.Count})" : string.Empty;
        var people = Simulation?.Entities.OfType<Humanoid>().Count() ?? 0;
        var levelKeys = _levels.Count > 0 ? "   F3: levels" : string.Empty;
        var autosave = _session is LocalSession { AutosaveError: { } failed } ? $"   AUTOSAVE FAILED: {failed}" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_session.Name}{levels}{money}{autosave}   {_session.Status}   {people} people{(_wander ? " wandering" : string.Empty)}   overlay {_overlay}   zoom x{_camera.Zoom:0.00}   upscale x{_blocks.Scale}{levelKeys}   O: overlay   U: upscale   Esc: quit");
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
        if (_session.Player < simulation.Hospitals.Count && simulation.Hospitals[_session.Player] is { IsOpen: false, IsOpeningWatchHeld: false } hospital)
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

    /// <summary>
    /// The list of levels (F3): every level the client can start, the current one marked; the one under the cursor is
    /// highlighted. Campaign levels get their number.
    /// </summary>
    private void DrawLevelList(SpriteBatch batch)
    {
        _levelListRows.Clear();
        if (!_levelList || _levels.Count == 0)
        {
            return;
        }

        const string title = "Choose a level (click one; F3 or Esc closes)";
        var labels = _levels.Select(LevelLabel).ToList();
        var lineHeight = _text.LineHeight + 4;
        var width = labels.Append(title).Max(label => _text.Measure(label).Width) + 40;
        var box = new Rectangle(8, StatusHeight + 8, width, ((labels.Count + 1) * lineHeight) + 12);
        batch.Draw(_pixel, box, MoneyPanelColor);
        _text.Draw(batch, title, new Vector2(box.X + 12, box.Y + 6));
        var mouse = InputScript.Mouse();
        for (var i = 0; i < labels.Count; i++)
        {
            var row = new Rectangle(box.X + 4, box.Y + 6 + ((i + 1) * lineHeight) - 2, box.Width - 8, lineHeight);
            _levelListRows.Add((row, i));
            if (row.Contains(mouse.X, mouse.Y))
            {
                batch.Draw(_pixel, row, RoomListHover);
            }

            var current = i == _levelIndex;
            _text.Draw(batch, (current ? "> " : "  ") + labels[i], new Vector2(box.X + 12, row.Y + 2), tint: current ? LevelListCurrent : Color.White);
        }
    }

    /// <summary>What the level list shows for a level: the original's campaign levels with their number, others by file.</summary>
    private static string LevelLabel(LevelSource level)
    {
        var name = level.Name;
        if (name.StartsWith("LEVEL.L", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return number <= 12 ? $"Campaign level {number}   ({name})" : $"Level {number}   ({name})";
        }

        return name;
    }

    /// <summary>
    /// Starts building a room: as in the original, only once discovered and with its whole cost in the bank; otherwise
    /// says why not.
    /// </summary>
    private void TryStartRoom(Simulation simulation, RoomDefinition roomType)
    {
        if (_session is null)
        {
            return;
        }

        if (RoomListProblem(simulation, roomType) is { } problem)
        {
            ShowNotice($"{roomType.Name}: {problem}");
            _sounds.Play("Wrong2.wav");
            return;
        }

        _buildTool.Start(roomType);
    }

    /// <summary>What the adviser says when a member of staff is put down: no lecture chair, a desk of their own, or the wrong room.</summary>
    private void AdviseOnPlacement(Simulation simulation, StaffProfile? profile, TilePosition tile, int movingId)
    {
        if (profile is null)
        {
            return;
        }

        if (AdviserChecks.LacksLectureChair(simulation, profile, tile, movingId))
        {
            _adviser.Say(AdviserChecks.NotEnoughLectureChairs.Text(_texts), Environment.TickCount64);
        }
        else if (AdviserChecks.StaffPlacement(simulation, profile, tile, movingId, Random.Shared.Next) is var (advice, argument))
        {
            _adviser.Say(argument is null ? advice.Text(_texts) : advice.Text(_texts, argument), Environment.TickCount64);
        }
    }

    /// <summary>Why the player cannot start a room now, or null when they can.</summary>
    private string? RoomListProblem(Simulation simulation, RoomDefinition room)
    {
        var player = _session!.Player;
        if (!simulation.Rules.IsRoomAvailable(room))
        {
            return "not on this level";
        }

        if (!RoomPurchases.CanBuild(simulation, room, player))
        {
            return "not researched yet";
        }

        return RoomPurchases.CanAfford(simulation, room, player) ? null : "not enough money";
    }

    /// <summary>
    /// The list of rooms (B), a stand-in for CorsixTH's build room window (<c>dialogs/build_room.lua</c>): every room type
    /// with what it costs in all, or why it cannot be built; the one under the cursor is highlighted.
    /// </summary>
    private void DrawRoomList(SpriteBatch batch)
    {
        _roomListRows.Clear();
        if (!_roomList || _session is not { Simulation: { } simulation } session || session.Player >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[session.Player];
        var rooms = simulation.Content.Rooms.OrderBy(room => room.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var rows = rooms.Select(room => (Room: room, Cost: MoneyText.Format(RoomPurchases.BuildCost(simulation, hospital, room)), Problem: RoomListProblem(simulation, room))).ToList();
        const string title = "Build a room (click one; B or Esc closes)";
        var lineHeight = _text.LineHeight + 4;
        var nameWidth = rows.Max(row => _text.Measure(row.Room.Name).Width);
        var costWidth = rows.Max(row => _text.Measure(row.Cost).Width);
        var problemWidth = rows.Max(row => row.Problem is null ? 0 : _text.Measure(row.Problem).Width);
        var width = Math.Max(_text.Measure(title).Width, nameWidth + costWidth + problemWidth + 48) + 24;
        var box = new Rectangle(8, StatusHeight + 8, width, ((rows.Count + 1) * lineHeight) + 12);
        batch.Draw(_pixel, box, MoneyPanelColor);
        _text.Draw(batch, title, new Vector2(box.X + 12, box.Y + 6));
        var mouse = InputScript.Mouse();
        for (var i = 0; i < rows.Count; i++)
        {
            var (room, cost, problem) = rows[i];
            var row = new Rectangle(box.X + 4, box.Y + 6 + ((i + 1) * lineHeight) - 2, box.Width - 8, lineHeight);
            _roomListRows.Add((row, room));
            if (row.Contains(mouse.X, mouse.Y))
            {
                batch.Draw(_pixel, row, problem is null ? RoomListHover : RoomListHoverBlocked);
            }

            if (room.Id == "gp" && Tutorial.Lit("room:gp"))
            {
                batch.Draw(_pixel, row, RoomListHover);
            }

            var tint = problem is null ? Color.White : RoomListBlocked;
            var y = row.Y + 2;
            _text.Draw(batch, room.Name, new Vector2(box.X + 12, y), tint: tint);
            var costRight = box.X + 12 + nameWidth + 24 + costWidth;
            _text.Draw(batch, cost, new Vector2(costRight - _text.Measure(cost).Width, y), tint: tint);
            if (problem is not null)
            {
                _text.Draw(batch, problem, new Vector2(costRight + 24, y), tint: tint);
            }
        }
    }

    /// <summary>The date as the original's bottom panel shows it (day and month), with the year.</summary>
    private static string DateText(GameDate date)
    {
        string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        return string.Create(CultureInfo.InvariantCulture, $"{date.Day} {months[date.Month - 1]} year {date.Year}");
    }

    /// <summary>The close-up views windows asked for (their view circles), by the entity they follow.</summary>
    private readonly Dictionary<int, RenderTarget2D> _closeUps = [];
    private readonly HashSet<int> _closeUpsWanted = [];

    /// <summary>The size of a close-up view in world pixels (UIStaff's 75 by 75 of the map).</summary>
    public const int CloseUpSize = 75;

    Texture2D? IWindowHost.CloseUp(int entityId)
    {
        _closeUpsWanted.Add(entityId);
        return _closeUps.GetValueOrDefault(entityId);
    }

    /// <summary>
    /// UIStaff:draw's <c>map:draw</c> into the view circle: for each entity a window asked about last frame, the map
    /// round it, 75 by 75 world pixels with the entity in the middle and a little low, as the frame shows it.
    /// </summary>
    private void RenderCloseUps(SpriteBatch batch, LevelMap map)
    {
        foreach (var stale in _closeUps.Keys.Where(id => !_closeUpsWanted.Contains(id)).ToList())
        {
            _closeUps[stale].Dispose();
            _closeUps.Remove(stale);
        }

        if (_closeUpsWanted.Count == 0 || Simulation is not { } simulation)
        {
            return;
        }

        var scale = _blocks.Scale;
        var humanoidFrames = _humanoids.FrameDrawn;
        var objectFrames = _objects.FrameDrawn;
        _humanoids.FrameDrawn = null;
        _objects.FrameDrawn = null;
        foreach (var id in _closeUpsWanted)
        {
            if (simulation.FindEntity(id) is not Humanoid humanoid)
            {
                continue;
            }

            var anchor = _humanoids.DrawnAt(id) ?? new Point(32 * (humanoid.Tile.X - humanoid.Tile.Y), 16 * (humanoid.Tile.X + humanoid.Tile.Y));
            var topLeft = new Point(anchor.X - (CloseUpSize / 2), anchor.Y - 61);
            if (!_closeUps.TryGetValue(id, out var target) || target.Width != CloseUpSize * scale)
            {
                target?.Dispose();
                target = new RenderTarget2D(_device, CloseUpSize * scale, CloseUpSize * scale);
                _closeUps[id] = target;
            }

            _device.SetRenderTarget(target);
            _device.Clear(Background);
            var toTarget = Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0) * Matrix.CreateScale(scale, scale, 1);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, transformMatrix: toTarget);
            _renderer.Draw(batch, map, new Rectangle(topLeft, new Point(CloseUpSize, CloseUpSize)), (tile, anchorX, anchorY) => DrawEntities(batch, tile, anchorX, anchorY),
                tile => _entitiesByTile.TryGetValue(tile, out var here) && here.Any(e => e is Humanoid or Rat),
                (tile, anchorX, anchorY) => DrawEarlyEntities(batch, tile, anchorX, anchorY),
                tile => _earlyByTile.TryGetValue(tile, out var early) && early.Count > 0);
            batch.End();
        }

        _humanoids.FrameDrawn = humanoidFrames;
        _objects.FrameDrawn = objectFrames;
        _closeUpsWanted.Clear();
    }
    /// <summary>GameUI: the bottom panel's dynamic info of what is under the cursor, or null.</summary>
    private string? HoverInfo()
    {
        if (_cursorOverWindow || Simulation is not { } simulation || _hoveredTile is not { } point)
        {
            return null;
        }

        var tile = new TilePosition(point.X, point.Y);
        if (simulation.FindEntity(_hoveredHumanoid) is Humanoid hovered && DynamicInfo.Of(simulation, hovered, _texts) is { Length: > 0 } info)
        {
            return info;
        }

        return MachineUnderCursor(simulation, tile) is { } machine ? DynamicInfo.OfMachine(simulation, machine, _texts) : null;
    }

    /// <summary>
    /// The kinds of the faxes waiting (UIMessage's types), in the order the fax window lists them: the VIP's invitation
    /// is a personality, results are reports, a patient's diagnosis is information.
    /// </summary>
    private List<string> PanelFaxes()
    {
        if (Simulation is not { } simulation || _session is null || _session.Player >= simulation.Hospitals.Count)
        {
            return [];
        }

        return FaxWindow.Pending(simulation, _session.Player).Select(key => key[..key.IndexOf(':', StringComparison.Ordinal)] switch
        {
            "vip" => "personality",
            "vipreport" or "emergencyreport" => "report",
            "emergency" => "emergency",
            "epidemic" => "epidemy",
            _ => "information",
        }).ToList();
    }

    /// <summary>A button of the original bottom panel: the dialog it opens or closes, or the tool it starts.</summary>
    private void BottomPanelButton(string key)
    {
        // research and edit room have no click sound (setSound()).
        if (key is not ("edit" or "research"))
        {
            _sounds.Play(WindowLayer.DefaultButtonSound);
        }

        UiWindow? dialog = key switch
        {
            "bank" => new BankManagerWindow(),
            "bankstats" => new BankStatementWindow(),
            "hire" => new HireWindow(),
            "staff" => new StaffManagementWindow(),
            "town" => new TownMapWindow(),
            "casebook" => new CasebookWindow(),
            "research" => new ResearchWindow(),
            "status" => new ProgressReportWindow(),
            "charts" => new GraphWindow(),
            "policy" => new PolicyWindow(),
            "fax" => new FaxWindow(),
            _ => null,
        };
        if (dialog is null)
        {
            BottomPanelAction(key);
        }
        else
        {
            _windows.Toggle(dialog);
        }
    }

    /// <summary>The kinds of dialog the original bottom panel's toggle buttons open, so they show pressed while open.</summary>
    private bool PanelDialogOpen(string key) => key switch
    {
        "bank" => _windows.Windows.Any(w => w is BankManagerWindow or BankStatementWindow),
        "hire" => _windows.Windows.Any(w => w is HireWindow),
        "staff" => _windows.Windows.Any(w => w is StaffManagementWindow),
        "town" => _windows.Windows.Any(w => w is TownMapWindow),
        "casebook" => _windows.Windows.Any(w => w is CasebookWindow),
        "research" => _windows.Windows.Any(w => w is ResearchWindow),
        "status" => _windows.Windows.Any(w => w is ProgressReportWindow),
        "charts" => _windows.Windows.Any(w => w is GraphWindow),
        "policy" => _windows.Windows.Any(w => w is PolicyWindow),
        "fax" => _windows.Windows.Any(w => w is FaxWindow),
        "rooms" => _roomList || _buildTool.IsActive,
        "corridor" => _furnishTool.IsActive || _windows.Windows.Any(w => w is FurnishCorridorWindow),
        "edit" => _pickingRoomToEdit,
        _ => false,
    };

    private void DrawBottomPanel(SpriteBatch batch, Viewport viewport, Point mouse)
    {
        if (_bottomPanel is null || Simulation is not { } simulation || _session is null || _session.Player >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[_session.Player];
        var month = _texts?.String(6, simulation.Date.Month) ?? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(simulation.Date.Month);
        var state = new BottomPanelState(
            hospital.Balance,
            hospital.Reputation,
            string.Create(CultureInfo.InvariantCulture, $"{simulation.Date.Day} {month}"),
            HoverInfo(),
            _session.IsPaused ? _texts?.String(3, 22) ?? "Pause" : null,
            PanelFaxes(),
            PanelDialogOpen,
            Tutorial.Lit);
        _bottomPanel.Draw(batch, viewport, state, mouse, InputScript.Mouse());
    }

    private string TileLine()
    {
        if (_editorTool is not null)
        {
            return "Pan: arrows WASD middle drag   Wheel: zoom   Left drag: paint, clear or paste   Right drag: copy an area";
        }

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

        if (_bottomPanel is null && HoverInfo() is { } info)
        {
            return info;
        }


        if (_hoveredTile is not { } position || Map is not { } map)
        {
            var pause = _session?.CanChangeSpeed == true ? "   Space P: pause   1-6: speed   Z: hold to speed up" : string.Empty;
            return $"Pan: arrows WASD right drag   Wheel: zoom   B: build a room   F: buy reception desk   H: spawn   G: spawn 10   Ctrl+1-4: hire   Click: select or walk   R: wander{pause}";
        }

        var tile = map[position.X, position.Y];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Tile {position.X} {position.Y}   floor {tile.Floor}   north {tile.NorthWall}   west {tile.WestWall}   parcel {tile.Parcel}{(tile.RoomId != 0 ? $"   room {tile.RoomId}" : string.Empty)}   {DescribeFlags(tile)}{(_selected is { } id ? $"   selected {id}" : string.Empty)}{StaffText(position)}");
    }

    /// <summary>
    /// The name, profession and description of a member of staff on the tile, standing in for the staff window and
    /// the bottom panel's information until they exist.
    /// </summary>
    private string StaffText(Point position)
    {
        if (_texts is null || Simulation is not { } simulation)
        {
            return string.Empty;
        }

        var staff = simulation.Entities.OfType<Humanoid>().FirstOrDefault(humanoid => humanoid.Profile is not null && humanoid.Tile.X == position.X && humanoid.Tile.Y == position.Y);
        return staff?.Profile is { } profile
            ? $"   {_texts.Staff.FullName(profile)}, {_texts.Staff.Profession(profile.Class)}: {_texts.Staff.Description(profile)}"
            : string.Empty;
    }

    /// <summary>CorsixTH's language files beside the game (Languages/*.lua), read once.</summary>
    private static readonly Lazy<CorsixLanguages> TranslationFiles = new(() => CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages")));

    /// <summary>
    /// The languages to choose from, as the settings take them: CorsixTH's translations, by English name, that the
    /// game's font can draw or a Unicode font can (<c>Graphics:hasLanguageFont</c>; the Russian original's own font,
    /// "mik", is not read, so Russian takes the Unicode font too), then any installed original language none of them
    /// names.
    /// </summary>
    private List<string> InstalledLanguageNames(ThemeHospitalInstall install)
    {
        var translations = TranslationFiles.Value;
        List<string> names = [.. translations.Languages.Where(CanDraw).Select(language => language.EnglishName)];
        foreach (var original in install.InstalledLanguages().Order().Select(GameConfig.OriginalLanguageName).OfType<string>())
        {
            if (translations.Find(original) is not { } translation || !CanDraw(translation))
            {
                names.Add(original);
            }
        }

        return names;
    }

    /// <summary>Whether the language can be shown: with the game's font, or with a Unicode font that has its name's characters.</summary>
    private bool CanDraw(CorsixLanguage language) => language.UsesGameFont || _unicodeFonts.CanDraw(language.NativeName);

    /// <summary>
    /// Draws text in the settings' language with the game's font, or with the Unicode font found for the language when
    /// its translation asks for one (<c>App:initLanguage</c>, <c>Graphics:onChangeLanguage</c>); the status bar and the
    /// windows below it follow the font's height.
    /// </summary>
    private void ApplyLanguageFont()
    {
        TrueTypeFace? face = null;
        if (TranslationFiles.Value.Find(_config.Language) is { UsesGameFont: false } translation)
        {
            var sample = translation.NativeName + string.Concat(_texts?.Language?.Strings.Values ?? []);
            face = _unicodeFonts.Find(sample)?.Face;
        }

        _text.UseTrueType(face);
        _blackText?.UseTrueType(face);
        _bottomPanel?.UseTrueType(face);
        _graphics.UseTrueType(face);
        _windows.Origin = new Point(8, StatusHeight + 8);
    }

    /// <summary>
    /// The texts in the settings' language: the original's strings in that language if installed (or in the one its
    /// CorsixTH translation inherits), else in English, else in the first installed language, with the translation laid
    /// over them when the game's font or a Unicode font can draw it.
    /// </summary>
    private OriginalTexts? LoadTexts(ThemeHospitalInstall install, GameConfig config)
    {
        var languages = install.InstalledLanguages();
        if (languages.Count == 0)
        {
            return null;
        }

        var translations = TranslationFiles.Value;
        var translation = translations.Find(config.Language) is { } found && CanDraw(found) ? found : null;
        var wanted = config.OriginalLanguageNumber() ?? (translation is null ? null : translations.OriginalStringsNumber(translation.EnglishName));
        var language = wanted is { } number && languages.Contains(number) ? number
            : languages.Contains(StringTable.English) ? StringTable.English
            : languages[0];
        try
        {
            var strings = install.LoadStrings(language);

            // CorsixTH's own translation over the original's strings: Inherit("original_strings", n) takes LANG-n.DAT
            // where it is installed (the one already loaded is reused).
            var overlay = translation is null
                ? null
                : translations.Load(translation.EnglishName, n => n == language ? strings : languages.Contains(n) ? install.LoadStrings(n) : null);
            return new OriginalTexts(strings, overlay);
        }
        catch (InvalidDataException)
        {
            return null;
        }
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
