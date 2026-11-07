using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Sim.Maps;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Maps;

/// <summary>A level to show: an original LEVELS file or a map file elsewhere on disk.</summary>
internal sealed record LevelSource(string Name, Func<LoadedLevelMap> Load);

/// <summary>
/// Shows a level map with a movable, zoomable camera.
/// </summary>
/// <remarks>
/// Keys: arrows/WASD or right/middle-drag pan, wheel zoom, Home reset camera,
/// PgUp/PgDn previous/next level, O cycle overlay, Esc quit.
/// </remarks>
internal sealed class MapViewScreen : IScreen
{
    private const float PanSpeed = 900f;
    private const float ZoomStep = 1.25f;
    private const int StatusHeight = 36;

    private static readonly Color Background = Color.Black;
    private static readonly Color HoverTint = new Color(255, 255, 255) * 0.35f;

    private readonly GraphicsDevice _device;
    private readonly IReadOnlyList<LevelSource> _levels;
    private readonly SpriteAtlas _blocks;
    private readonly MapRenderer _renderer;
    private readonly TextRenderer _text;
    private readonly Texture2D _diamond;
    private readonly Texture2D _pixel;
    private readonly Camera _camera = new();

    private int _levelIndex;
    private LoadedLevelMap? _level;
    private string? _error;
    private MapOverlay _overlay;
    private RenderTarget2D? _target;
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private Point? _hoveredTile;

    public MapViewScreen(
        ThemeHospitalInstall install,
        GraphicsDevice device,
        IReadOnlyList<LevelSource> levels,
        float zoom = 1f,
        MapOverlay overlay = MapOverlay.None)
    {
        _device = device;
        _levels = levels;

        var palette = install.LoadPalette("DATA", "MPALETTE.DAT");
        _blocks = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "VBLK-0", complex: false), palette);
        _renderer = new MapRenderer(_blocks);
        _text = new TextRenderer(device, new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT01V", complex: false)), palette);
        _diamond = CreateDiamond(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);

        _overlay = overlay;
        SelectLevel(0);
        _camera.ZoomAt(zoom, new Point(ViewSize.X / 2, ViewSize.Y / 2), ViewSize);
    }

    public bool ExitRequested { get; private set; }

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

    public void Update(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        var viewSize = ViewSize;
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

        if (Pressed(Keys.Escape))
        {
            ExitRequested = true;
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

        if (Pressed(Keys.O))
        {
            _overlay = (MapOverlay)(((int)_overlay + 1) % Enum.GetValues<MapOverlay>().Length);
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
        var tile = Camera.WorldToTile(_camera.ScreenToWorld(new Point(mouse.X, mouse.Y - StatusHeight), viewSize));
        _hoveredTile = inView && _level is not null && _level.Map.Contains(tile.X, tile.Y) ? tile : null;

        _previousKeys = keys;
        _previousMouse = mouse;
    }

    public void Draw(SpriteBatch batch)
    {
        var viewSize = ViewSize;
        var worldSize = _camera.WorldSize(viewSize);
        var topLeft = _camera.TopLeft(viewSize);
        EnsureTarget(worldSize);

        // Draw the map 1:1 into an off-screen target, then scale it to the window.
        // Scaling the finished image avoids seams between tiles at fractional zoom levels.
        _device.SetRenderTarget(_target);
        _device.Clear(Background);
        if (_level is not null)
        {
            var view = new Rectangle(topLeft, worldSize);
            var toTarget = Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, transformMatrix: toTarget);
            _renderer.Draw(batch, _level.Map, view);
            DrawOverlay(batch, _level.Map, view);
            batch.End();
        }

        _device.SetRenderTarget(null);
        _device.Clear(Background);

        var sampler = _camera.Zoom >= 1 ? SamplerState.PointClamp : SamplerState.LinearClamp;
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, sampler);
        var destination = new Rectangle(
            0,
            StatusHeight,
            (int)MathF.Round(worldSize.X * _camera.Zoom),
            (int)MathF.Round(worldSize.Y * _camera.Zoom));
        batch.Draw(_target, destination, Color.White);
        batch.End();

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        batch.Draw(_pixel, new Rectangle(0, 0, _device.Viewport.Width, StatusHeight), Color.Black);
        _text.Draw(batch, StatusLine(), new Vector2(8, 4));
        _text.Draw(batch, _error ?? TileLine(), new Vector2(8, 4 + _text.LineHeight + 2));
        batch.End();
    }

    public void Dispose()
    {
        _target?.Dispose();
        _blocks.Dispose();
        _text.Dispose();
        _diamond.Dispose();
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
        try
        {
            _level = _levels[index].Load();
            _error = null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            _level = null;
            _error = $"Cannot load {_levels[index].Name}: {e.Message}";
        }

        ResetCamera();
    }

    private void ResetCamera()
    {
        if (_level is not null)
        {
            var start = _level.Map.GetCameraTile(0);
            _camera.Center = Camera.TileCenter(start.X, start.Y);
        }
    }

    private void ClampCamera()
    {
        if (_level is null)
        {
            return;
        }

        var map = _level.Map;
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

    private void DrawOverlay(SpriteBatch batch, LevelMap map, Rectangle view)
    {
        var (firstScanline, lastScanline) = MapRenderer.VisibleScanlines(map, view);
        for (var s = firstScanline; s <= lastScanline; s++)
        {
            var (firstX, lastX) = MapRenderer.VisibleColumns(map, view, s);
            for (var x = firstX; x <= lastX; x++)
            {
                var y = s - x;
                var tint = _hoveredTile == new Point(x, y) ? HoverTint : OverlayTint(map[x, y]);
                if (tint != Color.Transparent)
                {
                    batch.Draw(_diamond, new Vector2((32 * (x - y)) - 32, 16 * (x + y)), tint);
                }
            }
        }
    }

    private Color OverlayTint(MapTile tile)
    {
        return _overlay switch
        {
            MapOverlay.Passable => tile.Has(TileFlags.Passable) ? new Color(0, 200, 0) * 0.4f : new Color(200, 0, 0) * 0.4f,
            MapOverlay.Hospital => tile.Has(TileFlags.Buildable) ? new Color(0, 120, 255) * 0.45f
                : tile.Has(TileFlags.Hospital) ? new Color(255, 200, 0) * 0.45f
                : Color.Transparent,
            MapOverlay.Parcels => tile.Parcel == 0 ? Color.Transparent : ParcelColor(tile.Parcel) * 0.45f,
            _ => Color.Transparent,
        };
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

    private string StatusLine()
    {
        var name = _levels.Count > 0 ? _levels[_levelIndex].Name : "-";
        var objects = _level?.Objects.Count ?? 0;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name}   ({_levelIndex + 1} of {_levels.Count})   {objects} objects   overlay {_overlay}   zoom x{_camera.Zoom:0.00}   PgUp PgDn: level   O: overlay   Home: reset   Esc: quit");
    }

    private string TileLine()
    {
        if (_hoveredTile is not { } position || _level is null)
        {
            return "Arrows or WASD or right drag: pan   Wheel: zoom";
        }

        var tile = _level.Map[position.X, position.Y];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Tile {position.X} {position.Y}   floor {tile.Floor}   north {tile.NorthWall}   west {tile.WestWall}   parcel {tile.Parcel}   {DescribeFlags(tile)}");
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
}
