using CorsixNet.Client.Input;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Tests;

/// <summary>The scripted mouse and keyboard of <c>--script</c>.</summary>
public sealed class InputScriptTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("corsixnet-script").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void PlaysClicksKeysAndShotsInTime()
    {
        var script = Load("""
            # a comment
            wait 1000
            click 10 20
            key LeftShift+F3
            shot one.png
            quit
            """);

        script.Advance(999);
        Assert.Equal(-1, script.MouseState.X);

        script.Advance(1);
        Assert.Equal((10, 20), (script.MouseState.X, script.MouseState.Y));
        Assert.Equal(ButtonState.Released, script.MouseState.LeftButton);

        script.Advance(100);
        Assert.Equal(ButtonState.Pressed, script.MouseState.LeftButton);

        script.Advance(200);
        Assert.Equal(ButtonState.Released, script.MouseState.LeftButton);
        Assert.True(script.KeyboardState.IsKeyDown(Keys.LeftShift));
        Assert.True(script.KeyboardState.IsKeyDown(Keys.F3));

        script.Advance(160);
        Assert.Empty(script.KeyboardState.GetPressedKeys());
        Assert.Equal(Path.Combine(_folder, "one.png"), script.PendingShot);
        Assert.False(script.QuitRequested);

        script.ShotTaken();
        script.Advance(0);
        Assert.True(script.QuitRequested);
    }

    [Fact]
    public void AnUnknownCommandNamesItsLine()
    {
        var error = Assert.Throws<InvalidDataException>(() => Load("wait 10\njump 3"));

        Assert.Contains("line 2", error.Message, StringComparison.Ordinal);
    }

    private InputScript Load(string text)
    {
        var path = Path.Combine(_folder, "script.txt");
        File.WriteAllText(path, text);
        return InputScript.Load(path);
    }
}
