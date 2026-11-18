using OpenWards.Sim.Disasters;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Maps;

namespace OpenWards.Sim.Tests;

/// <summary>The network game's emergencies and VIP visits: offered to all, the first to accept receives them.</summary>
public class FirstComeOfferTests
{
    [Fact]
    public void TheFirstToAcceptAnEmergencyReceivesItAndTheOthersOffersGo()
    {
        var simulation = NewSimulation();
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.Emergencies.Current = new Emergency("uncommon_cold", 4, 100, 75) { OfferExpires = 500 };
        }

        Emergencies.Answer(simulation, 1, accept: true);

        Assert.True(simulation.Hospitals[1].Emergencies.Current!.IsAccepted);
        Assert.Null(simulation.Hospitals[0].Emergencies.Current);
    }

    [Fact]
    public void ARefusalTakesOnlyThatHospitalsOffer()
    {
        var simulation = NewSimulation();
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.Emergencies.Current = new Emergency("uncommon_cold", 4, 100, 75) { OfferExpires = 500 };
        }

        Emergencies.Answer(simulation, 0, accept: false);

        Assert.Null(simulation.Hospitals[0].Emergencies.Current);
        Assert.False(simulation.Hospitals[1].Emergencies.Current!.IsAccepted);
    }

    [Fact]
    public void TheFirstToInviteTheVipReceivesTheVisit()
    {
        var simulation = NewSimulation();
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.Vips.InvitedName = 3;
            hospital.Vips.InvitationExpires = 500;
        }

        VipVisits.Answer(simulation, 1, accept: true);

        Assert.False(simulation.Hospitals[0].Vips.IsInvitationPending);
        Assert.False(simulation.Hospitals[1].Vips.IsInvitationPending);
        Assert.Equal((0, 1), (simulation.Hospitals[0].Vips.Count, simulation.Hospitals[1].Vips.Count));
    }

    private static Simulation NewSimulation()
    {
        var map = new LevelMap(10, 10, playerCount: 2);
        map.ParcelCount = 3;
        map.SetParcelOwner(1, 1);
        map.SetParcelOwner(2, 2);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                tile.Parcel = (ushort)(x < 5 ? 1 : 2);
            }
        }

        return new Simulation(map, seed: 1, rules: new LevelRules(10_000, [], []));
    }
}
