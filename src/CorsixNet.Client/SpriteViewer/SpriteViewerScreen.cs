using CorsixNet.Client.Rendering;
using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.SpriteViewer;

/// <summary>
/// Browses every sprite sheet and full-screen bitmap of the game data.
/// </summary>
/// <remarks>
/// Keys: Left/Right switch item, Up/Down/PageUp/PageDown/mouse wheel scroll,
/// +/- zoom, P/Shift+P cycle palette, C toggle chunk encoding, Esc quit.
/// </remarks>
internal sealed class SpriteViewerScreen : IDisposable
{
    private const int HeaderHeight = 54;
    private const int Margin = 8;
    private const int CellGap = 6;
    private const int MaxZoom = 4;

    private static readonly Color Background = new(36, 40, 48);
    private static readonly Color CellBackground = new(52, 58, 70);
    private static readonly Color HoverBackground = new(80, 92, 120);

    private readonly ThemeHospitalInstall _install;
    private readonly GraphicsDevice _device;
    private readonly IReadOnlyList<ViewerItem> _items;
    private readonly IReadOnlyList<(string Folder, string File)> _palettes;
    private readonly TextRenderer _text;
    private readonly Texture2D _pixel;
    private readonly List<(int Index, Rectangle Cell)> _layout = [];

    private int _itemIndex;
    private int _paletteIndex;
    private bool _complex;
    private int _zoom = 1;
    private int _scroll;
    private int _contentHeight;
    private int _layoutWidth;
    private SheetTextures? _sheet;
    private Texture2D? _bitmap;
    private string? _error;
    private KeyboardState _previousKeys;
    private int _previousWheel;
    private int? _hovered;

    public SpriteViewerScreen(ThemeHospitalInstall install, GraphicsDevice device, string? initialItem = null)
    {
        _install = install;
        _device = device;
        _items = ViewerCatalog.Build(install);
        _palettes = ViewerCatalog.FindPalettes(install);

        var mainPalette = install.LoadPalette("DATA", "MPALETTE.DAT");
        _text = new TextRenderer(device, new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT01V", complex: false)), mainPalette);

        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);

        var initial = initialItem is null
            ? -1
            : _items.ToList().FindIndex(item => string.Equals(item.Title, initialItem, StringComparison.OrdinalIgnoreCase));
        SelectItem(Math.Max(initial, 0));
    }

    public bool ExitRequested { get; private set; }

    private ViewerItem? Current => _items.Count > 0 ? _items[_itemIndex] : null;

    public void Update()
    {
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);
        var shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        var pageSize = Math.Max(_device.Viewport.Height - HeaderHeight - 40, 40);

        if (Pressed(Keys.Escape))
        {
            ExitRequested = true;
        }

        if (_items.Count > 0)
        {
            if (Pressed(Keys.Right))
            {
                SelectItem((_itemIndex + 1) % _items.Count);
            }

            if (Pressed(Keys.Left))
            {
                SelectItem((_itemIndex + _items.Count - 1) % _items.Count);
            }

            if (Pressed(Keys.P))
            {
                _paletteIndex = (_paletteIndex + (shift ? _palettes.Count - 1 : 1)) % _palettes.Count;
                Reload();
            }

            if (Pressed(Keys.C) && Current is { IsBitmap: false })
            {
                _complex = !_complex;
                Reload();
            }
        }

        if (Pressed(Keys.OemPlus) || Pressed(Keys.Add))
        {
            SetZoom(_zoom + 1);
        }

        if (Pressed(Keys.OemMinus) || Pressed(Keys.Subtract))
        {
            SetZoom(_zoom - 1);
        }

        var scroll = _scroll;
        scroll -= (mouse.ScrollWheelValue - _previousWheel) / 2;
        if (keys.IsKeyDown(Keys.Down))
        {
            scroll += 12;
        }

        if (keys.IsKeyDown(Keys.Up))
        {
            scroll -= 12;
        }

        if (Pressed(Keys.PageDown))
        {
            scroll += pageSize;
        }

        if (Pressed(Keys.PageUp))
        {
            scroll -= pageSize;
        }

        if (Pressed(Keys.Home))
        {
            scroll = 0;
        }

        EnsureLayout();
        _scroll = Math.Clamp(scroll, 0, Math.Max(0, _contentHeight - (_device.Viewport.Height - HeaderHeight)));

        var pointer = new Point(mouse.X, mouse.Y - HeaderHeight + _scroll);
        _hovered = mouse.Y >= HeaderHeight
            ? _layout.Where(entry => entry.Cell.Contains(pointer)).Select(entry => (int?)entry.Index).FirstOrDefault()
            : null;

        _previousKeys = keys;
        _previousWheel = mouse.ScrollWheelValue;
    }

    public void Draw(SpriteBatch batch)
    {
        EnsureLayout();
        _device.Clear(Background);

        var viewport = _device.Viewport;
        var content = Matrix.CreateTranslation(0, HeaderHeight - _scroll, 0);
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, transformMatrix: content);
        if (_bitmap is not null)
        {
            batch.Draw(_bitmap, new Rectangle(Margin, Margin, _bitmap.Width * _zoom, _bitmap.Height * _zoom), Color.White);
        }
        else if (_sheet is not null)
        {
            var visibleTop = _scroll;
            var visibleBottom = _scroll + viewport.Height - HeaderHeight;
            foreach (var (index, cell) in _layout)
            {
                if (cell.Bottom < visibleTop || cell.Top > visibleBottom)
                {
                    continue;
                }

                batch.Draw(_pixel, cell, index == _hovered ? HoverBackground : CellBackground);
                _text.Draw(batch, index.ToString(System.Globalization.CultureInfo.InvariantCulture), new Vector2(cell.X + 2, cell.Y + 2));
                if (_sheet[index] is { } texture)
                {
                    var spritePosition = new Vector2(cell.X + 2, cell.Y + 4 + _text.LineHeight);
                    batch.Draw(texture, spritePosition, null, Color.White, 0, Vector2.Zero, _zoom, SpriteEffects.None, 0);
                }
            }
        }

        batch.End();

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        batch.Draw(_pixel, new Rectangle(0, 0, viewport.Width, HeaderHeight), Color.Black);
        var line = _text.LineHeight + 2;
        _text.Draw(batch, StatusLine(), new Vector2(Margin, 4));
        _text.Draw(batch, _error ?? HoverLine(), new Vector2(Margin, 4 + line));
        _text.Draw(batch, "Left Right: item   Wheel Up Down PgUp PgDn: scroll   + -: zoom   P: palette   C: encoding   Esc: quit", new Vector2(Margin, 4 + (2 * line)));
        batch.End();
    }

    public void Dispose()
    {
        UnloadTextures();
        _text.Dispose();
        _pixel.Dispose();
    }

    private string StatusLine()
    {
        if (Current is not { } item)
        {
            return "No sprite sheets found.";
        }

        var (paletteFolder, paletteFile) = _palettes[_paletteIndex];
        var kind = item.IsBitmap
            ? "bitmap 640x480"
            : $"{_sheet?.Count ?? 0} sprites, {(_complex ? "complex" : "simple")} encoding";
        // The Theme Hospital fonts have no slash glyph.
        return $"{item.Folder} {item.Name}   ({_itemIndex + 1} of {_items.Count})   {kind}   palette {paletteFolder} {paletteFile}   zoom x{_zoom}";
    }

    private string HoverLine()
    {
        if (_hovered is { } index && _sheet is not null)
        {
            var sprite = _sheet.Sheet[index];
            return sprite.IsEmpty ? $"#{index}: empty" : $"#{index}: {sprite.Width}x{sprite.Height}";
        }

        return string.Empty;
    }

    private void SelectItem(int index)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _itemIndex = index;
        var item = _items[index];
        _complex = item.Complex;
        _paletteIndex = Math.Max(0, IndexOfPalette(item.PaletteFolder, item.PaletteFile));
        _scroll = 0;
        Reload();
    }

    private int IndexOfPalette(string folder, string file)
    {
        for (var i = 0; i < _palettes.Count; i++)
        {
            if (string.Equals(_palettes[i].Folder, folder, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_palettes[i].File, file, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void Reload()
    {
        UnloadTextures();
        _error = null;
        if (Current is not { } item)
        {
            return;
        }

        try
        {
            var (paletteFolder, paletteFile) = _palettes[_paletteIndex];
            var palette = _install.LoadPalette(paletteFolder, paletteFile);
            if (item.IsBitmap)
            {
                _bitmap = TextureFactory.Create(_device, _install.LoadRawBitmap(item.Folder, item.Name), palette);
            }
            else
            {
                _sheet = new SheetTextures(_device, _install.LoadSpriteSheet(item.Folder, item.Name, _complex), palette);
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            _error = $"Cannot load {item.Title}: {e.Message}";
        }

        _layoutWidth = -1;
    }

    private void UnloadTextures()
    {
        _sheet?.Dispose();
        _sheet = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void SetZoom(int zoom)
    {
        _zoom = Math.Clamp(zoom, 1, MaxZoom);
        _layoutWidth = -1;
    }

    /// <summary>Flows sprite cells left to right, wrapping at the window width.</summary>
    private void EnsureLayout()
    {
        var width = _device.Viewport.Width;
        if (width == _layoutWidth)
        {
            return;
        }

        _layoutWidth = width;
        _layout.Clear();
        if (_bitmap is not null)
        {
            _contentHeight = (_bitmap.Height * _zoom) + (2 * Margin);
            return;
        }

        if (_sheet is null)
        {
            _contentHeight = 0;
            return;
        }

        int x = Margin, y = Margin, rowHeight = 0;
        for (var i = 0; i < _sheet.Count; i++)
        {
            var sprite = _sheet.Sheet[i];
            var label = _text.Measure(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var cellWidth = Math.Max(sprite.Width * _zoom, label.Width) + 4;
            var cellHeight = (sprite.Height * _zoom) + _text.LineHeight + 6;
            if (x + cellWidth > width - Margin && x > Margin)
            {
                x = Margin;
                y += rowHeight + CellGap;
                rowHeight = 0;
            }

            _layout.Add((i, new Rectangle(x, y, cellWidth, cellHeight)));
            x += cellWidth + CellGap;
            rowHeight = Math.Max(rowHeight, cellHeight);
        }

        _contentHeight = y + rowHeight + Margin;
    }
}
