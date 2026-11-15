using CorsixNet.Client.Ui.Windows;

namespace CorsixNet.Client.Tests;

public sealed class UpdateCheckTests
{
    private static string Release(string tag, string url = "https://github.com/Maxwellwr/CorsixNet/releases/tag/v1.3.0", bool prerelease = false) =>
        "{\"tag_name\":\"" + tag + "\",\"html_url\":\"" + url + "\",\"prerelease\":" + (prerelease ? "true" : "false") + ",\"body\":\"Faster.\\r\\nFixes.\"}";

    [Theory]
    [InlineData("1.3.0", true)]
    [InlineData("1.3.1", true)]
    [InlineData("2.0", true)]
    [InlineData("1.2.9", false)]
    [InlineData("1.3.0-beta", false)]
    [InlineData("1.3.0 RC1", false)]
    [InlineData("1.3.1-beta", true)]
    public void VersionsAreComparedAsCorsixThDoes(string current, bool newest) =>
        Assert.Equal(newest, UpdateCheck.IsCurrentNewest(current, (1, 3, 0)));

    [Fact]
    public void ANewerReleaseIsOfferedWithItsPageAndNotes()
    {
        var update = UpdateCheck.FromRelease("1.2.0", Release("v1.3"));

        Assert.NotNull(update);
        Assert.Equal(("1.2.0", "1.3.0"), (update.CurrentVersion, update.NewVersion));
        Assert.Equal("https://github.com/Maxwellwr/CorsixNet/releases/tag/v1.3.0", update.DownloadUrl);
        Assert.Equal("Faster.\r\nFixes.", update.Description);
    }

    [Fact]
    public void NothingIsOfferedForTheSameVersionAPreReleaseOrAnUntrustedPage()
    {
        Assert.Null(UpdateCheck.FromRelease("1.3.0", Release("v1.3.0")));
        Assert.Null(UpdateCheck.FromRelease("1.2.0", Release("v1.3.0", prerelease: true)));
        Assert.Null(UpdateCheck.FromRelease("1.2.0", Release("v1.3.0", url: "https://example.com/corsixnet.zip")));
        Assert.Null(UpdateCheck.FromRelease("1.2.0", Release("nightly")));
    }

    [Fact]
    public async Task DevelopmentVersionsAreNotChecked()
    {
        Assert.True(UpdateCheck.IsDevelopment("0.1.0-dev"));
        Assert.Null(await UpdateCheck.FindAsync("0.1.0-dev", TestContext.Current.CancellationToken));
        Assert.Null(await UpdateCheck.FindAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheUpdateWindowOpensTheDownloadPageOrIsIgnored()
    {
        var host = new WindowTestHost();
        string? opened = null;
        var update = new UpdateInfo("1.2.0", "1.3.0", "Faster.", "https://github.com/Maxwellwr/CorsixNet/releases/tag/v1.3.0");
        var window = new UpdateWindow(update, url => opened = url);
        host.Open(window);

        host.Click(window, "Skip and go to main menu");
        Assert.Null(opened);
        Assert.DoesNotContain(window, host.Opened);

        host.Open(window);
        host.Click(window, "Go to download page");
        Assert.Equal(update.DownloadUrl, opened);
        Assert.DoesNotContain(window, host.Opened);
    }
}
