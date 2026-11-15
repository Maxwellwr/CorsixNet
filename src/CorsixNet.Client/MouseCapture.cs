// Keeping the cursor in the window, as CorsixTH's App:setCaptureMouse (CorsixTH/Lua/app.lua) with
// THVideo:setCaptureMouse (CorsixTH/Src/th_lua_gfx.cpp, SDL_SetWindowGrab on the game's window).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Runtime.InteropServices;

namespace CorsixNet.Client;

/// <summary>
/// Grabs the mouse for MonoGame's SDL window (<c>SDL_SetWindowGrab</c>), or lets it go. Where SDL cannot be found the
/// cursor is simply not kept in.
/// </summary>
internal static unsafe class MouseCapture
{
    private static readonly string[] LibraryNames = ["SDL2", "SDL2.dll", "libSDL2-2.0.so.0", "libSDL2-2.0.0.dylib", "libSDL2.so"];

    private static delegate* unmanaged<nint, int, void> _setWindowGrab;
    private static bool _looked;
    private static bool _grabbed;

    /// <summary>Grabs the cursor for <paramref name="window"/> (an <c>SDL_Window</c>) or lets it go, if that changes.</summary>
    public static void Set(nint window, bool grab)
    {
        if (grab == _grabbed || window == 0 || !Find())
        {
            return;
        }

        _setWindowGrab(window, grab ? 1 : 0);
        _grabbed = grab;
    }

    private static bool Find()
    {
        if (!_looked)
        {
            _looked = true;
            foreach (var name in LibraryNames)
            {
                if (NativeLibrary.TryLoad(name, typeof(MouseCapture).Assembly, null, out var library)
                    && NativeLibrary.TryGetExport(library, "SDL_SetWindowGrab", out var export))
                {
                    _setWindowGrab = (delegate* unmanaged<nint, int, void>)export;
                    break;
                }
            }
        }

        return _setWindowGrab != null;
    }
}
