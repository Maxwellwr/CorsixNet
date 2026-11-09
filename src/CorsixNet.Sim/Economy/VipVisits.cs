// VIP visits, ported from CorsixTH CorsixTH/Lua/world.lua (World:onEndDay's VIP check, spawnVIP, nextVip,
// _generateNextVipDate), app.lua (math.t_random), hospital.lua (vip_declined, num_vips, num_vips_ty, pleased_vips_ty),
// hospitals/player_hospital.lua (PlayerHospital:createVip) and dialogs/fullscreen/fax.lua (UIFax:choice for
// accept_vip and refuse_vip).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>
/// A hospital's VIPs: when the next one asks to visit, the fax asking (which accepts itself after 20 days), and how
/// many came and left pleased.
/// </summary>
/// <remarks>
/// CorsixTH keeps the next VIP's date in the world and invites VIPs to the local player's hospital only; here each
/// hospital has its own.
/// </remarks>
public sealed class VipVisits
{
    /// <summary>How many VIP names there are (<c>_S.vip_names</c>).</summary>
    public const int NameCount = 10;

    /// <summary>The fax asking accepts the VIP after this many days (<c>Date.hoursPerDay() * 20</c>).</summary>
    public const int InvitationDays = 20;

    /// <summary>How far, in days, a visit may come before or after the level's average (<c>_generateNextVipDate</c>'s bound).</summary>
    private const int Bound = 30;

    /// <summary>The day (counted from the start of the game) on which the next VIP asks to visit.</summary>
    public long NextVipDay { get; internal set; }

    /// <summary>The name (1 to <see cref="NameCount"/>) of the VIP asking to visit, or 0 when nobody asks.</summary>
    public int InvitedName { get; internal set; }

    /// <summary>The tick at which the fax asking accepts the VIP by itself.</summary>
    public long InvitationExpires { get; internal set; }

    /// <summary>VIPs refused in a row (<c>vip_declined</c>).</summary>
    public int Declined { get; internal set; }

    /// <summary>VIPs who came (<c>num_vips</c>).</summary>
    public int Count { get; internal set; }

    /// <summary>VIPs who made their report this year (<c>num_vips_ty</c>).</summary>
    public int ThisYear { get; internal set; }

    /// <summary>VIPs who left pleased this year (<c>pleased_vips_ty</c>).</summary>
    public int PleasedThisYear { get; internal set; }

    /// <summary>Whether a VIP is asking to visit.</summary>
    public bool IsInvitationPending => InvitedName != 0;

    /// <summary><c>World:nextVip</c>: the next VIP comes some days after <paramref name="today"/>.</summary>
    internal void ScheduleNext(SimRandom random, int mayorLaunch, long today) => NextVipDay = today + DaysUntilNext(random, mayorLaunch);

    /// <summary>
    /// <c>World:_generateNextVipDate</c>: days drawn from a triangular distribution around the level's
    /// <c>gbv.MayorLaunch</c>, 30 days either side (but at least a day), rounded.
    /// </summary>
    internal static long DaysUntilNext(SimRandom random, int mayorLaunch)
    {
        var mean = Math.Max(mayorLaunch, 1);
        var lower = Math.Max(mean - Bound, 1);
        var upper = mean + Bound;
        var days = (long)Math.Floor(TriangularRandom(random, lower, mean, upper) + 0.5m);
        return Math.Max(days, 1);
    }

    /// <summary>
    /// <c>math.t_random(a, c, b)</c>: a number from <paramref name="a"/> to <paramref name="b"/>, most likely near
    /// <paramref name="c"/>. CorsixTH asserts a &lt; c; with a = c (a level's <c>MayorLaunch</c> of 1) the peak is at a.
    /// </summary>
    internal static decimal TriangularRandom(SimRandom random, decimal a, decimal c, decimal b)
    {
        var range = b - a;
        var peak = (c - a) / range;
        var u = random.Next(1_000_000) / 1_000_000m;
        return u <= peak
            ? a + (Sqrt(peak * u) * range)
            : a + ((1 - Sqrt((1 - peak) * (1 - u))) * range);
    }

    /// <summary>
    /// The end of a day (<c>World:onEndDay</c>): on the day of the next visit a VIP asks to come, if the hospital has
    /// a room and a staffed reception desk; otherwise the visit is put off.
    /// </summary>
    internal static void OnEndDay(Simulation simulation, Hospital hospital)
    {
        var vips = hospital.Vips;
        var today = simulation.Tick / GameDate.HoursPerDay;
        if (today != vips.NextVipDay)
        {
            return;
        }

        if (HasRoom(simulation, hospital.Player) && ReceptionDesks.OfHospital(simulation, hospital.Player, staffedOnly: true).Any())
        {
            Invite(simulation, hospital);
        }
        else
        {
            vips.ScheduleNext(simulation.Random, simulation.Rules.MayorLaunch, today);
        }
    }

    /// <summary>
    /// <c>PlayerHospital:createVip</c>: a fax from one of the VIPs asks to visit. The next visit is not planned until
    /// this one is refused or over.
    /// </summary>
    internal static void Invite(Simulation simulation, Hospital hospital)
    {
        hospital.Vips.InvitedName = simulation.Random.NextInclusive(1, NameCount);
        hospital.Vips.InvitationExpires = simulation.Tick + (InvitationDays * GameDate.HoursPerDay);
    }

    /// <summary>The fax runs out and takes its default choice, inviting the VIP (<c>queueMessage</c>'s timeout).</summary>
    internal static void TickInvitation(Simulation simulation, Hospital hospital)
    {
        if (hospital.Vips.IsInvitationPending && simulation.Tick >= hospital.Vips.InvitationExpires)
        {
            Answer(simulation, hospital.Player, accept: true);
        }
    }

    /// <summary>
    /// <c>UIFax:choice</c>: an invited VIP comes; a refused one is put off, unless they were refused more than twice
    /// before and decide, one time in two, to come anyway.
    /// </summary>
    internal static void Answer(Simulation simulation, int player, bool accept)
    {
        if (player < 0 || player >= simulation.Hospitals.Count || !simulation.Hospitals[player].Vips.IsInvitationPending)
        {
            return;
        }

        var hospital = simulation.Hospitals[player];
        var vips = hospital.Vips;
        var name = vips.InvitedName;
        vips.InvitedName = 0;
        var ignoresRefusal = simulation.Random.NextInclusive(1, 2) == 2;
        if (!accept && vips.Declined > 2 && ignoresRefusal)
        {
            vips.Count++;
            Spawn(simulation, hospital, name);
            vips.Declined = 0;
        }
        else if (!accept)
        {
            vips.ScheduleNext(simulation.Random, simulation.Rules.MayorLaunch, simulation.Tick / GameDate.HoursPerDay);
            vips.Declined++;
        }
        else
        {
            vips.Count++;
            Spawn(simulation, hospital, name);
        }
    }

    /// <summary>
    /// <c>World:spawnVIP</c>: the VIP comes in at a random spawn point and heads for reception, noting how the hospital
    /// stands. In a hospital of more than 79 rooms they look into fewer of them.
    /// </summary>
    internal static Humanoid? Spawn(Simulation simulation, Hospital hospital, int name)
    {
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        if (points.Count == 0)
        {
            // CorsixTH needs a spawn point; without one the visit is off.
            hospital.Vips.ScheduleNext(simulation.Random, simulation.Rules.MayorLaunch, simulation.Tick / GameDate.HoursPerDay);
            return null;
        }

        // Vip:Vip draws the starting rating before the spawn point is chosen.
        var rating = 12 - simulation.Random.NextInclusive(0, 5);
        var rooms = RoomsOf(simulation, hospital.Player).Count();
        var spawn = points[simulation.Random.Next(points.Count)];
        var vip = simulation.AddEntity(id => Humanoid.CreateVip(id, spawn, hospital.Player));
        vip.Behaviour = new VipBehaviour(name, rating)
        {
            EnterDeaths = hospital.Deaths,
            EnterVisitors = hospital.Visitors,
            EnterCures = hospital.Cured,
            EnterPatients = PatientsOf(simulation, hospital.Player),
            RoomVisitChance = rooms > 79 ? rooms / 40 : 1,
        };
        return vip;
    }

    /// <summary>
    /// <c>Vip:onDestroy</c>: off the hospital grounds, the VIP's report brings a pleased hospital money and reputation,
    /// and costs a displeased one reputation; then the next visit is planned.
    /// </summary>
    internal static void OnVipLeft(Simulation simulation, Hospital hospital, VipBehaviour vip)
    {
        if (vip.Rating <= VipBehaviour.PleasedRating)
        {
            hospital.Receive(simulation.Tick, vip.CashReward, TransactionKind.VipAward, string.Empty);
            hospital.UnconditionalChangeReputation(vip.ReputationReward);
            hospital.Vips.PleasedThisYear++;
        }
        else
        {
            hospital.UnconditionalChangeReputation(vip.ReputationReward);
        }

        hospital.Vips.ScheduleNext(simulation.Random, simulation.Rules.MayorLaunch, simulation.Tick / GameDate.HoursPerDay);
    }

    /// <summary>The rooms of a hospital, built or not, in the order they were made (CorsixTH's <c>world.rooms</c>).</summary>
    internal static IEnumerable<Room> RoomsOf(Simulation simulation, int player)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Room room && RoomVisits.Owner(simulation, room) == player)
            {
                yield return room;
            }
        }
    }

    /// <summary>The patients of a hospital on the map (<c>#hospital.patients</c>).</summary>
    internal static int PatientsOf(Simulation simulation, int player)
    {
        var count = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour, IsDespawning: false } patient && patient.HospitalPlayer == player)
            {
                count++;
            }
        }

        return count;
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt64(NextVipDay);
        writer.WriteInt32(InvitedName);
        writer.WriteInt64(InvitationExpires);
        writer.WriteInt32(Declined);
        writer.WriteInt32(Count);
        writer.WriteInt32(ThisYear);
        writer.WriteInt32(PleasedThisYear);
    }

    internal static VipVisits Read(SimReader reader)
    {
        var visits = new VipVisits
        {
            NextVipDay = reader.ReadInt64(),
            InvitedName = reader.ReadInt32(),
            InvitationExpires = reader.ReadInt64(),
            Declined = reader.ReadInt32(),
            Count = reader.ReadInt32(),
            ThisYear = reader.ReadInt32(),
            PleasedThisYear = reader.ReadInt32(),
        };
        return visits.InvitedName is >= 0 and <= NameCount ? visits : throw new InvalidDataException($"Unknown VIP name {visits.InvitedName}.");
    }

    private static bool HasRoom(Simulation simulation, int player) => RoomsOf(simulation, player).Any();

    /// <summary>A square root by Newton's method, in decimals to keep the simulation deterministic.</summary>
    private static decimal Sqrt(decimal value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var guess = value >= 1 ? value : 1;
        for (var i = 0; i < 100; i++)
        {
            var next = (guess + (value / guess)) / 2;
            if (next == guess)
            {
                break;
            }

            guess = next;
        }

        return guess;
    }
}
