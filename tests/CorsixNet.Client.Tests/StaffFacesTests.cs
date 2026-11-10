using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>Staff portraits (CorsixTH's StaffProfile:drawFace).</summary>
public class StaffFacesTests
{
    [Fact]
    public void APortraitIsTheHairFaceAndChinOfTheProfileStackedUp()
    {
        var profile = StaffProfile.Create(StaffClass.Nurse, 500, StaffRules.Default, new SimRandom(3));

        var parts = StaffFaces.Parts(profile);

        Assert.Equal(new(0, profile.HairIndex * 29, 65, 29), parts[0].Source);
        Assert.Equal(new(0, 522 + (profile.FaceIndex * 24), 65, 24), parts[1].Source);
        Assert.Equal(new(0, 954 + (profile.ChinIndex * 22), 65, 22), parts[2].Source);
        Assert.Equal([0, 29, 53], parts.Select(p => p.Offset.Y));
        Assert.True(parts[2].Source.Bottom <= 1350);
    }

    [Fact]
    public void TheHireWindowShowsThePortraitOfTheCandidatePicked()
    {
        var host = new WindowTestHost();
        var window = new HireWindow();
        var candidates = host.Simulation.AvailableStaff(StaffClass.Doctor);
        Assert.True(candidates.Count > 1);

        var rows = window.Rows(host).ToList();
        var second = rows.Select(r => r.Cells.FirstOrDefault()).First(c => c?.OnClick is not null && c.Tooltip == "Show the portrait" && !c.Highlight);
        second!.OnClick!();
        rows = window.Rows(host).ToList();

        var picked = rows.Select(r => r.Cells.FirstOrDefault()).Where(c => c?.Tooltip == "Show the portrait").ToList();
        Assert.Single(picked, c => c!.Highlight);
        Assert.False(picked[0]!.Highlight);
    }
}
