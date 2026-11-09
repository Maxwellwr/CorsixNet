// Emergencies, ported from CorsixTH CorsixTH/Lua/world.lua (World:onEndDay's emergency check, nextEmergency,
// computeNextEmergencyDates), hospital.lua (createEmergency, resolveEmergency, checkEmergencyOver,
// getHeliportPosition, getHeliportSpawnPosition, reputation_changes), objects/helicopter.lua, dialogs/watch.lua
// (UIWatch for "emergency"), hospitals/player_hospital.lua (makeEmergencyStartFax's timeout) and
// dialogs/fullscreen/fax.lua (accept_emergency, refuse_emergency).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Disasters;

/// <summary>
/// One of the level's planned emergencies (<c>emergency_control[n]</c>): it comes on a random day from
/// <see cref="StartMonth"/> to <see cref="EndMonth"/> (months of the game), with <see cref="Min"/> to <see cref="Max"/>
/// patients of the disease numbered <see cref="Illness"/>, and pays <see cref="Bonus"/> for each one cured if at least
/// <see cref="PercWin"/> percent are.
/// </summary>
public readonly record struct PlannedEmergency(int Index, int StartMonth, int EndMonth, int Min, int Max, int Illness, int PercWin, int Bonus)
{
    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Index);
        writer.WriteInt32(StartMonth);
        writer.WriteInt32(EndMonth);
        writer.WriteInt32(Min);
        writer.WriteInt32(Max);
        writer.WriteInt32(Illness);
        writer.WriteInt32(PercWin);
        writer.WriteInt32(Bonus);
    }

    internal static PlannedEmergency Read(SimReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}

/// <summary>An emergency offered by fax, or under way once accepted (CorsixTH's <c>hospital.emergency</c>).</summary>
public sealed class Emergency
{
    internal Emergency(string diseaseId, int victims, int bonus, int percentage)
    {
        DiseaseId = diseaseId;
        Victims = victims;
        Bonus = bonus;
        Percentage = percentage;
    }

    /// <summary>The patients' disease.</summary>
    public string DiseaseId { get; }

    /// <summary>How many patients come.</summary>
    public int Victims { get; }

    /// <summary>What each cured patient brings, if enough are cured.</summary>
    public int Bonus { get; }

    /// <summary>The percentage of patients that must be cured for the bonus.</summary>
    public int Percentage { get; }

    /// <summary>Patients cured.</summary>
    public int Cured { get; internal set; }

    /// <summary>Patients who died or went home uncured.</summary>
    public int Killed { get; internal set; }

    /// <summary>Whether the fax was accepted and the emergency is under way.</summary>
    public bool IsAccepted { get; internal set; }

    /// <summary>The tick at which the fax refuses the emergency by itself.</summary>
    public long OfferExpires { get; internal set; }

    /// <summary>The watch's segments left (CorsixTH's <c>open_timer</c>); at −1 time is up.</summary>
    public int WatchTimer { get; internal set; }

    /// <summary>Hours left of the watch's current segment (<c>tick_timer</c>).</summary>
    public int WatchTickTimer { get; internal set; }

    /// <summary>The patients who came, in order.</summary>
    public List<int> PatientIds { get; } = [];

    internal void Write(SimWriter writer)
    {
        writer.WriteString(DiseaseId);
        writer.WriteInt32(Victims);
        writer.WriteInt32(Bonus);
        writer.WriteInt32(Percentage);
        writer.WriteInt32(Cured);
        writer.WriteInt32(Killed);
        writer.WriteBool(IsAccepted);
        writer.WriteInt64(OfferExpires);
        writer.WriteInt32(WatchTimer);
        writer.WriteInt32(WatchTickTimer);
        writer.WriteInt32(PatientIds.Count);
        foreach (var id in PatientIds)
        {
            writer.WriteInt32(id);
        }
    }

    internal static Emergency Read(SimReader reader)
    {
        var emergency = new Emergency(reader.ReadString(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32())
        {
            Cured = reader.ReadInt32(),
            Killed = reader.ReadInt32(),
            IsAccepted = reader.ReadBool(),
            OfferExpires = reader.ReadInt64(),
            WatchTimer = reader.ReadInt32(),
            WatchTickTimer = reader.ReadInt32(),
        };
        if (DiseaseCatalog.Find(emergency.DiseaseId) is null)
        {
            throw new InvalidDataException($"Unknown emergency disease '{emergency.DiseaseId}'.");
        }

        var count = reader.ReadCount(1 << 10);
        for (var i = 0; i < count; i++)
        {
            emergency.PatientIds.Add(reader.ReadInt32());
        }

        return emergency;
    }
}

/// <summary>
/// A hospital's emergencies: when the next of the level's planned ones comes, the emergency offered or under way, and
/// the helicopter bringing its patients.
/// </summary>
/// <remarks>
/// CorsixTH keeps the schedule in the world and has emergencies at the local player's hospital only; here each
/// hospital has its own. Levels with random emergencies (<c>emergency_control[0].Mean</c> or <c>Random</c>) are not
/// supported: no original level has them.
/// </remarks>
public sealed class HospitalEmergencies
{
    /// <summary>Days the watch runs (<c>TICK_DAYS_EMERGENCY</c>).</summary>
    public const int WatchDays = 52;

    /// <summary>The watch's segments (<c>TIMER_SEGMENTS</c>).</summary>
    public const int WatchSegments = 13;

    /// <summary>The fax refuses the emergency by itself after this many days.</summary>
    public const int OfferDays = 16;

    /// <summary>Hours of one segment of the watch: 200.</summary>
    public const int WatchTickRate = WatchDays * GameDate.HoursPerDay / WatchSegments;

    /// <summary>The helicopter's phase when it is called: 120 hours before it shows.</summary>
    public const int HelicopterStart = -120;

    /// <summary>The number of the next planned emergency to look at (<c>next_emergency_no</c>).</summary>
    public int NextNumber { get; internal set; }

    /// <summary>The month of the game of the next emergency, or 0 for none (<c>next_emergency_month</c>).</summary>
    public int NextMonth { get; internal set; }

    /// <summary>The day of the month of the next emergency (<c>next_emergency_day</c>).</summary>
    public int NextDay { get; internal set; }

    /// <summary>The number of the planned emergency that comes next (<c>next_emergency</c>), or −1.</summary>
    public int NextIndex { get; internal set; } = -1;

    /// <summary>The emergency offered or under way, or null.</summary>
    public Emergency? Current { get; internal set; }

    /// <summary>
    /// The helicopter's phase: from −120 while on the way, 0 when it shows, landed from 60, gone after 147
    /// (CorsixTH's <c>Helicopter.phase</c>), or null when there is none.
    /// </summary>
    public int? HelicopterPhase { get; internal set; }

    /// <summary>Patients the helicopter has let out so far.</summary>
    public int HelicopterSpawned { get; internal set; }

    /// <summary>Patients the helicopter brings.</summary>
    public int HelicopterVictims { get; internal set; }

    /// <summary>The emergency's disease, for the helicopter's patients.</summary>
    public string HelicopterDisease { get; internal set; } = string.Empty;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(NextNumber);
        writer.WriteInt32(NextMonth);
        writer.WriteInt32(NextDay);
        writer.WriteInt32(NextIndex);
        writer.WriteBool(Current is not null);
        Current?.Write(writer);
        writer.WriteBool(HelicopterPhase is not null);
        writer.WriteInt32(HelicopterPhase ?? 0);
        writer.WriteInt32(HelicopterSpawned);
        writer.WriteInt32(HelicopterVictims);
        writer.WriteString(HelicopterDisease);
    }

    internal static HospitalEmergencies Read(SimReader reader)
    {
        var emergencies = new HospitalEmergencies
        {
            NextNumber = reader.ReadInt32(),
            NextMonth = reader.ReadInt32(),
            NextDay = reader.ReadInt32(),
            NextIndex = reader.ReadInt32(),
            Current = reader.ReadBool() ? Emergency.Read(reader) : null,
        };
        var hasHelicopter = reader.ReadBool();
        var phase = reader.ReadInt32();
        emergencies.HelicopterPhase = hasHelicopter ? phase : null;
        emergencies.HelicopterSpawned = reader.ReadInt32();
        emergencies.HelicopterVictims = reader.ReadInt32();
        emergencies.HelicopterDisease = reader.ReadString();
        return emergencies;
    }
}

/// <summary>
/// Emergencies as CorsixTH has them: on the planned day the hospital is offered one by fax; accepted, a helicopter
/// lands at the heliport and lets out the patients, already diagnosed and on their way to treatment, and a watch
/// runs for 52 days. When time is up, or every patient is cured, dead or gone, the patients not yet cured are set to
/// die, and the hospital gains reputation and a bonus for each cured patient if enough were cured, or loses
/// reputation.
/// </summary>
public static class Emergencies
{
    /// <summary>Reputation for an emergency handled well (<c>reputation_changes.emergency_success</c>).</summary>
    public const int SuccessReputation = 15;

    /// <summary>Reputation for an emergency handled badly (<c>reputation_changes.emergency_failed</c>).</summary>
    public const int FailureReputation = -20;

    /// <summary>The planned emergencies' bonus when the level does not say (a random emergency's).</summary>
    private const int RandomBonus = 1000;

    /// <summary>A random emergency's percentage to cure.</summary>
    private const int RandomPercentage = 75;

    /// <summary>Plans the hospital's first emergency (<c>World:World</c>).</summary>
    internal static void Start(Simulation simulation, Hospital hospital) => NextEmergency(simulation, hospital);

    /// <summary>
    /// <c>World:nextEmergency</c>: the next of the level's planned emergencies that can still come, stepping over one
    /// missing number; none when they run out.
    /// </summary>
    internal static void NextEmergency(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Emergencies;
        var planned = simulation.Rules.Emergencies;
        while (true)
        {
            var number = state.NextNumber;
            if (Find(planned, number) is null && Find(planned, number + 1) is not null)
            {
                number++;
                state.NextNumber = number;
            }

            if (Find(planned, number) is not { } emergency)
            {
                state.NextMonth = 0;
                state.NextIndex = -1;
                return;
            }

            state.NextIndex = number;
            state.NextNumber++;
            if (ComputeNextDates(simulation, state, emergency))
            {
                return;
            }
        }
    }

    /// <summary>
    /// The end of a day (<c>World:onEndDay</c>): on the planned day an emergency is offered, or put off to a random day
    /// of the next month while a watch is running (the opening's or another emergency's).
    /// </summary>
    internal static void OnEndDay(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Emergencies;
        var date = simulation.Date;
        if (date.MonthOfGame != state.NextMonth || date.Day != state.NextDay)
        {
            return;
        }

        if (!hospital.IsOpen || state.Current is { IsAccepted: true })
        {
            state.NextMonth++;
            var monthOfYear = 1 + ((state.NextMonth - 1) % 12);
            state.NextDay = simulation.Random.NextInclusive(1, GameDate.DaysIn(monthOfYear));
            return;
        }

        var available = simulation.Rules.Diseases.AvailableDiseases().ToList();
        Emergency? planned = null;
        if (Find(simulation.Rules.Emergencies, state.NextIndex) is { } next
            && available.FirstOrDefault(disease => disease.ExpertiseId == next.Illness) is { } disease)
        {
            planned = new Emergency(disease.Id, simulation.Random.NextInclusive(next.Min, next.Max), next.Bonus, next.PercWin);
        }

        // CorsixTH gives up the emergency without planning the next, which ends the level's emergencies; here the
        // next one is planned.
        if (!Create(simulation, hospital, planned, available))
        {
            NextEmergency(simulation, hospital);
        }
    }

    /// <summary>
    /// <c>Hospital:createEmergency</c>: a random disease and number are drawn either way; with a heliport and a staffed
    /// desk the emergency is offered, unless its disease is not known yet.
    /// </summary>
    private static bool Create(Simulation simulation, Hospital hospital, Emergency? emergency, List<DiseaseDefinition> available)
    {
        if (available.Count == 0)
        {
            return false;
        }

        var random = available[simulation.Random.Next(available.Count)];
        var number = simulation.Random.NextInclusive(2, Math.Max(random.EmergencyNumber, 2));
        if (HeliportSpawnTile(simulation, hospital.Player) is null || !ReceptionDesks.OfHospital(simulation, hospital.Player, staffedOnly: true).Any())
        {
            return false;
        }

        // UIBottomPanel:canQueueFax: not while an epidemic's fax waits; the emergency is dropped (cancelFax).
        if (hospital.Epidemics.IsFaxPending)
        {
            return false;
        }

        emergency ??= new Emergency(random.Id, number, RandomBonus, RandomPercentage);
        if (!hospital.CaseOf(DiseaseCatalog.Find(emergency.DiseaseId)!, simulation.Rules.Diseases).Discovered)
        {
            return false;
        }

        emergency.OfferExpires = simulation.Tick + (HospitalEmergencies.OfferDays * GameDate.HoursPerDay);
        hospital.Emergencies.Current = emergency;
        return true;
    }

    /// <summary>
    /// <c>Cheats:cheatEmergency</c>: a random emergency is offered now, if the hospital has a heliport and a staffed desk
    /// and knows the disease. Not while one is under way, which CorsixTH would overwrite.
    /// </summary>
    internal static bool CreateRandom(Simulation simulation, Hospital hospital) =>
        hospital.Emergencies.Current is not { IsAccepted: true }
        && Create(simulation, hospital, null, simulation.Rules.Diseases.AvailableDiseases().ToList());

    /// <summary>
    /// <c>UIFax:choice</c>: accepted, the helicopter comes and the watch starts; refused, the next emergency is planned.
    /// </summary>
    internal static void Answer(Simulation simulation, int player, bool accept)
    {
        if (player < 0 || player >= simulation.Hospitals.Count || simulation.Hospitals[player].Emergencies.Current is not { IsAccepted: false } emergency)
        {
            return;
        }

        var hospital = simulation.Hospitals[player];
        var state = hospital.Emergencies;
        if (!accept)
        {
            state.Current = null;
            NextEmergency(simulation, hospital);
            return;
        }

        emergency.IsAccepted = true;
        emergency.WatchTimer = HospitalEmergencies.WatchSegments - 1;
        emergency.WatchTickTimer = HospitalEmergencies.WatchTickRate;
        state.HelicopterPhase = HospitalEmergencies.HelicopterStart;
        state.HelicopterSpawned = 0;
        state.HelicopterVictims = emergency.Victims;
        state.HelicopterDisease = emergency.DiseaseId;
    }

    /// <summary>
    /// One hour after the entities: the helicopter's (<c>Helicopter:tick</c>), then the fax's timeout (refusing) or the
    /// watch's (<c>UIWatch:onWorldTick</c>).
    /// </summary>
    internal static void Tick(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Emergencies;
        TickHelicopter(simulation, hospital, state);
        if (state.Current is not { } emergency)
        {
            return;
        }

        if (!emergency.IsAccepted)
        {
            if (simulation.Tick >= emergency.OfferExpires)
            {
                Answer(simulation, hospital.Player, accept: false);
            }

            return;
        }

        if (emergency.WatchTickTimer == 0 && emergency.WatchTimer >= 0)
        {
            emergency.WatchTickTimer = HospitalEmergencies.WatchTickRate;
            emergency.WatchTimer--;
        }
        else if (emergency.WatchTimer == -1)
        {
            Resolve(simulation, hospital);
        }
        else
        {
            emergency.WatchTickTimer--;
        }
    }

    /// <summary>
    /// An emergency patient was cured, or died or went home uncured; once all are accounted for the watch ends early
    /// (<c>updateCuredCounts</c>, <c>humanoidDeath</c>, <c>updateNotCuredCounts</c>, <c>checkEmergencyOver</c>).
    /// </summary>
    internal static void PatientDone(Simulation simulation, Hospital hospital, bool cured)
    {
        if (hospital.Emergencies.Current is not { IsAccepted: true } emergency)
        {
            return;
        }

        if (cured)
        {
            emergency.Cured++;
        }
        else
        {
            emergency.Killed++;
        }

        if (emergency.Killed + emergency.Cured >= emergency.Victims)
        {
            Resolve(simulation, hospital);
        }
    }

    /// <summary>
    /// <c>Hospital:resolveEmergency</c>: the patients still waiting are set to die; enough cured brings a bonus for each
    /// and reputation, too few cost reputation. Then the next emergency is planned.
    /// </summary>
    private static void Resolve(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Emergencies;
        if (state.Current is not { } emergency)
        {
            return;
        }

        state.Current = null;
        foreach (var id in emergency.PatientIds)
        {
            if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false })
            {
                patient.SetToDieUnlessCuredOrLeaving();
            }
        }

        var success = emergency.Cured * 100 >= emergency.Percentage * emergency.Victims;
        var entry = hospital.CaseOf(DiseaseCatalog.Find(emergency.DiseaseId)!, simulation.Rules.Diseases);
        if (success)
        {
            hospital.ChangeReputation(simulation.Random, SuccessReputation, entry);
            var earned = emergency.Bonus * emergency.Cured;
            if (earned != 0)
            {
                hospital.Receive(simulation.Tick, earned, TransactionKind.EmergencyBonus, string.Empty);
            }
        }
        else
        {
            hospital.ChangeReputation(simulation.Random, FailureReputation, entry);
        }

        NextEmergency(simulation, hospital);
    }

    /// <summary>
    /// <c>Helicopter:tick</c>: 120 hours on the way, 60 coming down, then a patient every 26 hours until all are out,
    /// and 60 hours going up again.
    /// </summary>
    private static void TickHelicopter(Simulation simulation, Hospital hospital, HospitalEmergencies state)
    {
        if (state.HelicopterPhase is not { } phase)
        {
            return;
        }

        if (phase == 60)
        {
            state.HelicopterSpawned = 0;
        }
        else if (phase == 85 && state.HelicopterSpawned < state.HelicopterVictims)
        {
            SpawnPatient(simulation, hospital, state);
            phase = 60;
        }
        else if (phase == 147)
        {
            state.HelicopterPhase = null;
            return;
        }

        state.HelicopterPhase = phase + 1;
    }

    /// <summary>
    /// <c>Helicopter:spawnPatient</c>: a patient with the emergency's disease, diagnosed, comes out by the heliport and
    /// heads for the last room of the treatment, unless they find its price too high.
    /// </summary>
    private static void SpawnPatient(Simulation simulation, Hospital hospital, HospitalEmergencies state)
    {
        state.HelicopterSpawned++;
        if (DiseaseCatalog.Find(state.HelicopterDisease) is not { } disease || HeliportSpawnTile(simulation, hospital.Player) is not { } tile)
        {
            return;
        }

        var health = simulation.Random.NextInclusive(80, 100) / 100m;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, disease.Look, hospital.Player));
        var patient = new PatientBehaviour { Health = health };
        patient.SetDisease(disease, PatientCare.ChooseInsuranceCompany(simulation.Random), simulation.Random);
        humanoid.Behaviour = patient;
        hospital.Visitors++;
        hospital.VisitorsThisYear++;
        hospital.Emergencies.Current?.PatientIds.Add(humanoid.Id);
        patient.StartEmergency(simulation, humanoid, state.HelicopterSpawned);
    }

    /// <summary>
    /// <c>Hospital:getHeliportSpawnPosition</c>: the tile north of the level's heliport tile, if the heliport tile is
    /// passable (level 2 names a heliport tile but has no heliport).
    /// </summary>
    public static TilePosition? HeliportSpawnTile(Simulation simulation, int player)
    {
        var map = simulation.Map;
        if (player < 0 || player >= map.PlayerCount)
        {
            return null;
        }

        var heliport = map.GetHeliportTile(player);
        if (!map.Contains(heliport.X, heliport.Y) || heliport.Y < 1 || !map[heliport.X, heliport.Y].Has(TileFlags.Passable))
        {
            return null;
        }

        return heliport with { Y = heliport.Y - 1 };
    }

    /// <summary>
    /// <c>World:computeNextEmergencyDates</c>: a random month from the start (no earlier than now) to the end, and a
    /// random day of it, no earlier than today if it is this month. False if that is already past.
    /// </summary>
    private static bool ComputeNextDates(Simulation simulation, HospitalEmergencies state, PlannedEmergency emergency)
    {
        var date = simulation.Date;
        var start = Math.Max(emergency.StartMonth, date.MonthOfGame);
        if (emergency.EndMonth < start)
        {
            return false;
        }

        var month = simulation.Random.NextInclusive(start, emergency.EndMonth);
        state.NextMonth = month;
        var dayStart = start == emergency.EndMonth ? date.Day : 1;
        state.NextDay = simulation.Random.NextInclusive(dayStart, GameDate.DaysIn(1 + ((month - 1) % 12)));

        // CorsixTH compares dates with their hours: today counts only at the first hour.
        return month > date.MonthOfGame || (month == date.MonthOfGame && (state.NextDay > date.Day || (state.NextDay == date.Day && date.Hour == 0)));
    }

    private static PlannedEmergency? Find(IReadOnlyList<PlannedEmergency> planned, int index)
    {
        foreach (var emergency in planned)
        {
            if (emergency.Index == index)
            {
                return emergency;
            }
        }

        return null;
    }
}
