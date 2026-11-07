namespace CorsixNet.Formats.Tests;

/// <summary>
/// Access to a real copy of Theme Hospital for tests. Tests using it are skipped
/// when no copy is found; set <see cref="ThemeHospitalInstall.PathEnvironmentVariable"/>
/// to point at one explicitly.
/// </summary>
internal static class RealGameData
{
    private static readonly Lazy<ThemeHospitalInstall?> Install = new(ThemeHospitalInstall.Locate);

    public static ThemeHospitalInstall Require()
    {
        var install = Install.Value;
        if (install is null)
        {
            Assert.Skip($"Theme Hospital data not found; set {ThemeHospitalInstall.PathEnvironmentVariable} to run this test.");
        }

        return install;
    }
}
