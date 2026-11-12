// Epidemics, ported from CorsixTH CorsixTH/Lua/epidemic.lua, hospital.lua (determineIfContagious, addToEpidemic,
// manageEpidemics, spawnContagiousPatient, cancelEpidemics, countEpidemics, isInHospital), entity_map.lua
// (getPatientsInAdjacentSquares), entities/humanoids/inspector.lua, objects/reception_desk.lua (handleInspector),
// dialogs/watch.lua (UIWatch for "epidemic"), dialogs/bottom_panel.lua (canQueueFax, cancelFax), cheats.lua
// (cheatEpidemic, cheatToggleEpidemic), dialogs/fullscreen/fax.lua (declare_epidemic, cover_up_epidemic) and
// base_config.lua (the gbv defaults).
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
/// The level's epidemic settings: how likely each disease's patients are to be contagious (<c>expertise[n].ContRate</c>,
/// one in that many, 0 for never) and the <c>gbv</c> values, with base_config.lua's defaults.
/// </summary>
public sealed class EpidemicRules
{
    private readonly int[] _contagiousRates;

    /// <param name="contagiousRates">Each <c>expertise</c> entry's <c>ContRate</c>, indexed from 1 (index 0 is unused).</param>
    /// <param name="spreadFactor"><c>gbv.ContagiousSpreadFactor</c>: how often, in percent (scaled down by 200), the disease passes on.</param>
    /// <param name="reduceContagiousMonths"><c>gbv.ReduceContMonths</c>: no contagious patients until after this month of the game.</param>
    /// <param name="reduceContagiousPeopleCount"><c>gbv.ReduceContPeepCount</c>: nor until more patients than this came.</param>
    /// <param name="vaccinationCost"><c>gbv.VacCost</c>: what a vaccination costs.</param>
    /// <param name="fine"><c>gbv.EpidemicFine</c>: the fine for each patient still infected (at least 2,000 in all).</param>
    /// <param name="compensationLow"><c>gbv.EpidemicCompLo</c>: the least compensation for a cover-up that worked.</param>
    /// <param name="compensationHigh"><c>gbv.EpidemicCompHi</c>: the most.</param>
    /// <param name="reputationLossMinimum"><c>gbv.EpidemicRepLossMinimum</c>: from this many still infected the fax speaks of reputation too.</param>
    /// <param name="evacuationMinimum"><c>gbv.EpidemicEvacMinimum</c>: from this many still infected the hospital is evacuated.</param>
    /// <param name="concurrentLimit"><c>gbv.EpidemicConcurrentLimit</c>: how many epidemics there may be at once, revealed or not.</param>
    public EpidemicRules(
        IReadOnlyList<int> contagiousRates,
        int spreadFactor = 25,
        int reduceContagiousMonths = 14,
        int reduceContagiousPeopleCount = 20,
        int vaccinationCost = 50,
        int fine = 2000,
        int compensationLow = 1000,
        int compensationHigh = 15000,
        int reputationLossMinimum = 5,
        int evacuationMinimum = 10,
        int concurrentLimit = 1)
    {
        if (contagiousRates.Count != DiseaseRules.ExpertiseCount + 1)
        {
            throw new ArgumentException($"Expected {DiseaseRules.ExpertiseCount + 1} entries.", nameof(contagiousRates));
        }

        _contagiousRates = [.. contagiousRates];
        SpreadFactor = spreadFactor;
        ReduceContagiousMonths = reduceContagiousMonths;
        ReduceContagiousPeopleCount = reduceContagiousPeopleCount;
        VaccinationCost = vaccinationCost;
        Fine = fine;
        CompensationLow = compensationLow;
        CompensationHigh = compensationHigh;
        ReputationLossMinimum = reputationLossMinimum;
        EvacuationMinimum = evacuationMinimum;
        ConcurrentLimit = concurrentLimit;
    }

    /// <summary>base_config.lua: no disease contagious, and the <c>gbv</c> defaults.</summary>
    public static EpidemicRules Default { get; } = new(new int[DiseaseRules.ExpertiseCount + 1]);

    public int SpreadFactor { get; }

    public int ReduceContagiousMonths { get; }

    public int ReduceContagiousPeopleCount { get; }

    public int VaccinationCost { get; }

    public int Fine { get; }

    public int CompensationLow { get; }

    public int CompensationHigh { get; }

    public int ReputationLossMinimum { get; }

    public int EvacuationMinimum { get; }

    public int ConcurrentLimit { get; }

    /// <summary>The <c>ContRate</c> of an <c>expertise</c> entry: one patient in that many is contagious, 0 for none.</summary>
    public int ContagiousRate(int expertiseId) => expertiseId >= 1 && expertiseId < _contagiousRates.Length ? _contagiousRates[expertiseId] : 0;

    internal void Write(SimWriter writer)
    {
        for (var i = 1; i < _contagiousRates.Length; i++)
        {
            writer.WriteInt32(_contagiousRates[i]);
        }

        writer.WriteInt32(SpreadFactor);
        writer.WriteInt32(ReduceContagiousMonths);
        writer.WriteInt32(ReduceContagiousPeopleCount);
        writer.WriteInt32(VaccinationCost);
        writer.WriteInt32(Fine);
        writer.WriteInt32(CompensationLow);
        writer.WriteInt32(CompensationHigh);
        writer.WriteInt32(ReputationLossMinimum);
        writer.WriteInt32(EvacuationMinimum);
        writer.WriteInt32(ConcurrentLimit);
    }

    internal static EpidemicRules Read(SimReader reader)
    {
        var rates = new int[DiseaseRules.ExpertiseCount + 1];
        for (var i = 1; i < rates.Length; i++)
        {
            rates[i] = reader.ReadInt32();
        }

        return new EpidemicRules(
            rates, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
    }
}

/// <summary>How an epidemic's cover-up ended (the result fax).</summary>
public enum EpidemicOutcome : byte
{
    /// <summary>Not over yet.</summary>
    None,

    /// <summary>Declared: the fine, and reputation lost.</summary>
    Declared,

    /// <summary>Covered up with nobody left infected: compensation.</summary>
    CoveredUp,

    /// <summary>Covered up with some still infected: a fine and reputation lost.</summary>
    Fined,

    /// <summary>Covered up with so many still infected that the hospital is evacuated: a fine and a third of the reputation.</summary>
    Evacuated,
}

/// <summary>
/// An epidemic (CorsixTH's <c>Epidemic</c>): contagious patients of one disease, passing it on to patients next to them.
/// It shows once one of them is diagnosed; the player then declares it (a fine) or covers it up, and has the watch's time
/// to cure everyone infected before the health inspector comes.
/// </summary>
public sealed class Epidemic
{
    public Epidemic(string diseaseId)
    {
        DiseaseId = diseaseId;
    }

    /// <summary>The contagious disease.</summary>
    public string DiseaseId { get; }

    /// <summary>The infected patients, in the order they caught it.</summary>
    public List<int> InfectedIds { get; } = [];

    /// <summary>Whether one of the infected patients has been diagnosed, so that the epidemic may show (<c>ready_to_reveal</c>).</summary>
    public bool IsReadyToReveal { get; internal set; }

    /// <summary>Whether the player has been told: the fax asking whether to declare or cover up (<c>revealed</c>).</summary>
    public bool IsRevealed { get; internal set; }

    /// <summary>The tick at which the fax runs out and the epidemic is covered up, or 0 when no fax is waiting.</summary>
    public long FaxExpires { get; internal set; }

    /// <summary>The fine for declaring the epidemic, set when it shows (<c>declare_fine</c>).</summary>
    public int DeclareFine { get; internal set; }

    /// <summary>Whether the player chose to cover up (<c>coverup_selected</c>).</summary>
    public bool IsCoverUp { get; internal set; }

    /// <summary>The cover-up watch's segments left (CorsixTH's <c>open_timer</c>), or −2 once it closed.</summary>
    public int WatchTimer { get; internal set; } = -2;

    /// <summary>Hours left of the watch's segment.</summary>
    public int WatchTickTimer { get; internal set; }

    /// <summary>The health inspector, once the cover-up is over, or 0.</summary>
    public int InspectorId { get; internal set; }

    /// <summary>Times an infected patient passed it on (<c>total_infections</c>).</summary>
    public int TotalInfections { get; internal set; }

    /// <summary>Times an infected patient tried to (<c>attempted_infections</c>).</summary>
    public int AttemptedInfections { get; internal set; }

    /// <summary>The fine if the cover-up fails (<c>coverup_fine</c>).</summary>
    public int CoverUpFine { get; internal set; }

    /// <summary>The compensation if it works (<c>compensation</c>).</summary>
    public int Compensation { get; internal set; }

    /// <summary>The reputation lost (<c>reputation_hit</c>).</summary>
    public int ReputationHit { get; internal set; }

    /// <summary>How it ended.</summary>
    public EpidemicOutcome Outcome { get; internal set; }

    /// <summary>Whether the cover-up watch is running.</summary>
    public bool IsWatchRunning => WatchTimer >= -1;

    /// <summary>Whether the cover-up is under way: chosen, and the inspector not called yet (<c>_isCoverUpActive</c>).</summary>
    public bool IsCoverUpActive => IsCoverUp && InspectorId == 0;

    internal void Write(SimWriter writer)
    {
        writer.WriteString(DiseaseId);
        writer.WriteInt32(InfectedIds.Count);
        foreach (var id in InfectedIds)
        {
            writer.WriteInt32(id);
        }

        writer.WriteBool(IsReadyToReveal);
        writer.WriteBool(IsRevealed);
        writer.WriteInt64(FaxExpires);
        writer.WriteInt32(DeclareFine);
        writer.WriteBool(IsCoverUp);
        writer.WriteInt32(WatchTimer);
        writer.WriteInt32(WatchTickTimer);
        writer.WriteInt32(InspectorId);
        writer.WriteInt32(TotalInfections);
        writer.WriteInt32(AttemptedInfections);
        writer.WriteInt32(CoverUpFine);
        writer.WriteInt32(Compensation);
        writer.WriteInt32(ReputationHit);
        writer.WriteByte((byte)Outcome);
    }

    internal static Epidemic Read(SimReader reader)
    {
        var epidemic = new Epidemic(reader.ReadString());
        if (DiseaseCatalog.Find(epidemic.DiseaseId) is null)
        {
            throw new InvalidDataException($"Unknown disease '{epidemic.DiseaseId}'.");
        }

        var count = reader.ReadCount(1 << 16);
        for (var i = 0; i < count; i++)
        {
            epidemic.InfectedIds.Add(reader.ReadInt32());
        }

        epidemic.IsReadyToReveal = reader.ReadBool();
        epidemic.IsRevealed = reader.ReadBool();
        epidemic.FaxExpires = reader.ReadInt64();
        epidemic.DeclareFine = reader.ReadInt32();
        epidemic.IsCoverUp = reader.ReadBool();
        epidemic.WatchTimer = reader.ReadInt32();
        epidemic.WatchTickTimer = reader.ReadInt32();
        epidemic.InspectorId = reader.ReadInt32();
        epidemic.TotalInfections = reader.ReadInt32();
        epidemic.AttemptedInfections = reader.ReadInt32();
        epidemic.CoverUpFine = reader.ReadInt32();
        epidemic.Compensation = reader.ReadInt32();
        epidemic.ReputationHit = reader.ReadInt32();
        var outcome = reader.ReadByte();
        epidemic.Outcome = outcome <= (byte)EpidemicOutcome.Evacuated ? (EpidemicOutcome)outcome : throw new InvalidDataException($"Unknown epidemic outcome {outcome}.");
        return epidemic;
    }
}

/// <summary>A hospital's epidemics: the one the player knows of, those still hidden, and the last one's outcome.</summary>
public sealed class HospitalEpidemics
{
    /// <summary>The epidemic the player was told of (CorsixTH's <c>epidemic</c>), or null.</summary>
    public Epidemic? Current { get; internal set; }

    /// <summary>Epidemics not shown yet (<c>future_epidemics_pool</c>).</summary>
    public List<Epidemic> Future { get; } = [];

    /// <summary>Whether the cheat turned epidemics off (<c>epidemics_disabled</c>).</summary>
    public bool IsDisabled { get; internal set; }

    /// <summary>How the last epidemic ended, for the result fax.</summary>
    public EpidemicOutcome LastOutcome { get; internal set; }

    /// <summary>The last epidemic's fine or compensation.</summary>
    public int LastAmount { get; internal set; }

    /// <summary>Whether the fax asking whether to declare or cover up is waiting for an answer.</summary>
    public bool IsFaxPending => Current is { FaxExpires: > 0 };

    /// <summary><c>countEpidemics</c>: the current epidemic and the hidden ones.</summary>
    public int Count => (Current is null ? 0 : 1) + Future.Count;

    internal void Write(SimWriter writer)
    {
        writer.WriteBool(Current is not null);
        Current?.Write(writer);
        writer.WriteInt32(Future.Count);
        foreach (var epidemic in Future)
        {
            epidemic.Write(writer);
        }

        writer.WriteBool(IsDisabled);
        writer.WriteByte((byte)LastOutcome);
        writer.WriteInt32(LastAmount);
    }

    internal static HospitalEpidemics Read(SimReader reader)
    {
        var epidemics = new HospitalEpidemics();
        if (reader.ReadBool())
        {
            epidemics.Current = Epidemic.Read(reader);
        }

        var count = reader.ReadCount(DiseaseCatalog.All.Count);
        for (var i = 0; i < count; i++)
        {
            epidemics.Future.Add(Epidemic.Read(reader));
        }

        epidemics.IsDisabled = reader.ReadBool();
        var outcome = reader.ReadByte();
        epidemics.LastOutcome = outcome <= (byte)EpidemicOutcome.Evacuated ? (EpidemicOutcome)outcome : throw new InvalidDataException($"Unknown epidemic outcome {outcome}.");
        epidemics.LastAmount = reader.ReadInt32();
        return epidemics;
    }
}

/// <summary>The epidemics' logic, per hospital.</summary>
public static class Epidemics
{
    /// <summary>The fax asking whether to declare or cover up runs out after this many hours, and the epidemic is covered up (<c>24*20</c>).</summary>
    public const int FaxHours = 24 * 20;

    /// <summary>The cover-up watch: 100 days in 13 segments, as the opening's (<c>TICK_DAYS</c>, <c>TIMER_SEGMENTS</c>).</summary>
    public const int WatchSegments = 13;

    /// <summary>Hours of one segment of the cover-up watch: 384.</summary>
    public const int WatchTickRate = 100 * GameDate.HoursPerDay / WatchSegments;

    /// <summary>
    /// The spread factor is divided by this, for balance (<c>spread_scale_factor</c>): a patient passes the disease on when
    /// that keeps the epidemic's share of successful infections below <c>spread_factor / 200</c>.
    /// </summary>
    private const int SpreadScaleFactor = 200;

    /// <summary>The least fine (<c>calculateInfectedFine</c>'s 2,000).</summary>
    private const int MinimumFine = 2000;

    /// <summary>
    /// <c>Hospital:determineIfContagious</c>, when a patient comes: one in the disease's <c>ContRate</c> is contagious,
    /// once the game is past <c>ReduceContMonths</c> and more than <c>ReduceContPeepCount</c> patients came; not
    /// emergency patients, and not while epidemics are off.
    /// </summary>
    internal static void DetermineIfContagious(Simulation simulation, Hospital hospital, Humanoid humanoid, PatientBehaviour patient)
    {
        if (hospital.Epidemics.IsDisabled || patient.EmergencyIndex > 0 || patient.Disease is not { IsContagious: true } disease)
        {
            return;
        }

        var rules = simulation.Rules.Epidemics;
        var rate = rules.ContagiousRate(disease.ExpertiseId);
        var potentiallyContagious = rate > 0 && simulation.Random.NextInclusive(1, rate) == rate;
        if (potentiallyContagious && simulation.Date.MonthOfGame > rules.ReduceContagiousMonths && hospital.Visitors > rules.ReduceContagiousPeopleCount)
        {
            AddToEpidemic(simulation, hospital, humanoid, patient);
        }
    }

    /// <summary>
    /// <c>Hospital:addToEpidemic</c>: a contagious patient joins the shown epidemic of their disease (unless it is being
    /// covered up), or a hidden one, or starts a new one if there are fewer than the level allows.
    /// </summary>
    internal static void AddToEpidemic(Simulation simulation, Hospital hospital, Humanoid humanoid, PatientBehaviour patient)
    {
        var state = hospital.Epidemics;
        var current = state.Current;
        if (current is { IsCoverUp: false } && current.DiseaseId == patient.DiseaseId)
        {
            AddContagiousPatient(current, humanoid, patient);
            return;
        }

        if (current is { IsCoverUp: true })
        {
            return;
        }

        foreach (var future in state.Future)
        {
            if (future.DiseaseId == patient.DiseaseId)
            {
                AddContagiousPatient(future, humanoid, patient);
                return;
            }
        }

        if (state.Count < simulation.Rules.Epidemics.ConcurrentLimit)
        {
            var epidemic = new Epidemic(patient.DiseaseId);
            AddContagiousPatient(epidemic, humanoid, patient);
            MarkPatientsAsPassedReception(simulation, hospital.Player);
            state.Future.Add(epidemic);
        }
    }

    /// <summary>
    /// <c>Hospital:manageEpidemics</c>, every hour: the shown epidemic goes on; a hidden one with nobody infected is
    /// dropped, one ready is shown if no watch is running and no epidemic is shown, and the others go on.
    /// </summary>
    internal static void Tick(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Epidemics;
        if (state.Current is { } current)
        {
            TickEpidemic(simulation, hospital, current);
        }

        // CorsixTH removes from the pool while walking it with ipairs, which skips the next epidemic; that is kept.
        for (var i = 0; i < state.Future.Count; i++)
        {
            var future = state.Future[i];
            if (future.InfectedIds.Count == 0)
            {
                state.Future.RemoveAt(i);
            }
            else if (CanBeRevealed(hospital, future))
            {
                state.Current = future;
                Reveal(simulation, hospital, future);
                state.Future.RemoveAt(i);
            }
            else
            {
                TickEpidemic(simulation, hospital, future);
            }
        }
    }

    /// <summary>
    /// <c>UIFax:choice</c>: declared, the fine is paid and the epidemic is over; covered up, the watch starts and every
    /// infected patient shows.
    /// </summary>
    internal static void Answer(Simulation simulation, int player, bool coverUp)
    {
        if (player < 0 || player >= simulation.Hospitals.Count || simulation.Hospitals[player].Epidemics.Current is not { FaxExpires: > 0 } epidemic)
        {
            return;
        }

        var hospital = simulation.Hospitals[player];
        epidemic.FaxExpires = 0;
        if (!coverUp)
        {
            ResolveDeclaration(simulation, hospital, epidemic);
            return;
        }

        // Epidemic:startCoverUp.
        epidemic.WatchTimer = WatchSegments - 1;
        epidemic.WatchTickTimer = WatchTickRate;
        CheckPatientsForRemoval(simulation, epidemic);
        epidemic.IsCoverUp = true;
    }

    /// <summary>
    /// <c>Inspector:checkForReceptionDesk</c> and <c>handleInspector</c>: the inspector has reported at reception, or
    /// found none; those still infected decide the outcome, and everyone infected counts as vaccinated.
    /// </summary>
    internal static void InspectorArrived(Simulation simulation, Hospital hospital)
    {
        if (hospital.Epidemics.Current is not { } epidemic)
        {
            return;
        }

        var stillInfected = CountInfectedPatients(simulation, epidemic);
        DetermineFines(simulation, epidemic, stillInfected);
        ClearAllInfectedPatients(simulation, epidemic);
        ApplyOutcome(simulation, hospital, epidemic);
    }

    /// <summary>
    /// <c>Hospital:spawnContagiousPatient</c> for the cheat: a patient with a random contagious disease of the level, by
    /// the heliport or at a spawn point, starts or joins an epidemic. Needs a staffed desk.
    /// </summary>
    internal static bool SpawnContagiousPatient(Simulation simulation, Hospital hospital)
    {
        if (hospital.Epidemics.IsDisabled || !ReceptionDesks.OfHospital(simulation, hospital.Player, staffedOnly: true).Any())
        {
            return false;
        }

        var contagious = simulation.Rules.Diseases.AvailableDiseases().Where(disease => disease.IsContagious).ToList();
        if (contagious.Count == 0)
        {
            return false;
        }

        var disease = contagious[simulation.Random.Next(contagious.Count)];
        TilePosition tile;
        if (Emergencies.HeliportSpawnTile(simulation, hospital.Player) is { } heliport)
        {
            tile = heliport;
        }
        else
        {
            var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
            if (points.Count == 0)
            {
                return false;
            }

            tile = points[simulation.Random.Next(points.Count)];
        }

        var health = simulation.Random.NextInclusive(80, 100) / 100m;
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, disease.Look, hospital.Player));
        var patient = new PatientBehaviour { Health = health };
        patient.SetDisease(disease, PatientCare.ChooseInsuranceCompany(simulation.Random), simulation.Random);
        humanoid.Behaviour = patient;
        hospital.Visitors++;
        hospital.VisitorsThisYear++;
        AddToEpidemic(simulation, hospital, humanoid, patient);
        return true;
    }

    /// <summary>
    /// <c>Cheats:cheatToggleEpidemic</c>: epidemics off, cancelling the shown and hidden ones (<c>cancelEpidemics</c>),
    /// or back on.
    /// </summary>
    internal static void ToggleDisabled(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Epidemics;
        if (!state.IsDisabled)
        {
            if (state.Current is { } current)
            {
                Cancel(simulation, current);
                state.Current = null;
            }

            foreach (var future in state.Future)
            {
                Cancel(simulation, future);
            }

            state.Future.Clear();
        }

        state.IsDisabled = !state.IsDisabled;
    }

    /// <summary>
    /// <c>UIBottomPanel:canQueueFax</c> and <c>cancelFax</c>: the epidemic's fax cannot come while an emergency's is
    /// waiting, and the epidemic is then dropped.
    /// </summary>
    private static bool CanQueueFax(Hospital hospital) => hospital.Emergencies.Current is not { IsAccepted: false };

    private static bool CanBeRevealed(Hospital hospital, Epidemic epidemic) =>
        hospital.IsOpen && hospital.Emergencies.Current is not { IsAccepted: true } && hospital.Epidemics.Current is null && epidemic.IsReadyToReveal;

    /// <summary><c>Epidemic:tick</c>.</summary>
    private static void TickEpidemic(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        InfectOtherPatients(simulation, hospital, epidemic);
        CheckIfReadyToReveal(simulation, epidemic);
        TickFaxAndWatch(simulation, hospital, epidemic);
        if (hospital.Epidemics.Current != epidemic && !hospital.Epidemics.Future.Contains(epidemic))
        {
            return;
        }

        MarkedPatientsCallForVaccination(simulation, hospital, epidemic);

        CheckInfectedLeftHospital(simulation, hospital, epidemic);
        CheckNoInfectedPatients(simulation, hospital, epidemic);
        CheckPatientsForRemoval(simulation, epidemic);
    }

    /// <summary>
    /// The fax's timeout (the cover-up, its default choice) and the cover-up watch (<c>UIWatch:onWorldTick</c>), which CorsixTH
    /// ticks as windows after the world; here they tick with the epidemic.
    /// </summary>
    private static void TickFaxAndWatch(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        if (epidemic.FaxExpires > 0)
        {
            if (simulation.Tick >= epidemic.FaxExpires)
            {
                Answer(simulation, hospital.Player, coverUp: true);
            }

            return;
        }

        if (!epidemic.IsWatchRunning)
        {
            return;
        }

        if (epidemic.WatchTickTimer == 0 && epidemic.WatchTimer >= 0)
        {
            epidemic.WatchTickTimer = WatchTickRate;
            epidemic.WatchTimer--;
        }
        else if (epidemic.WatchTimer == -1)
        {
            FinishCoverUp(simulation, hospital, epidemic);
        }
        else
        {
            epidemic.WatchTickTimer--;
        }
    }

    /// <summary>
    /// <c>Patient</c>'s click during a cover-up and <c>Epidemic:markForVaccination</c>: an infected patient not vaccinated
    /// yet is picked for vaccination, while the watch runs.
    /// </summary>
    internal static void MarkForVaccination(Simulation simulation, int player, int patientId)
    {
        if (player < 0 || player >= simulation.Hospitals.Count
            || simulation.Hospitals[player].Epidemics.Current is not { IsCoverUp: true, IsWatchRunning: true }
            || simulation.FindEntity(patientId) is not Humanoid { Behaviour: PatientBehaviour { IsInfected: true, IsVaccinated: false, IsMarkedForVaccination: false } patient } humanoid
            || humanoid.HospitalPlayer != player)
        {
            return;
        }

        patient.IsMarkedForVaccination = true;
    }

    /// <summary>
    /// <c>perform_vaccination</c>: the patient is vaccinated (<c>setVaccinatedStatus</c>) and the hospital pays the fee
    /// (<c>gbv.VacCost</c>).
    /// </summary>
    internal static void Vaccinated(Simulation simulation, Hospital hospital, PatientBehaviour patient)
    {
        patient.IsMarkedForVaccination = false;
        patient.IsVaccinated = true;
        hospital.Spend(simulation.Hours, simulation.Rules.Epidemics.VaccinationCost, TransactionKind.Vaccination, patient.DiseaseId);
    }

    /// <summary><c>is_in_adjacent_square</c>: on tiles next to each other, neither in a room.</summary>
    internal static bool AreNextToEachOtherOutside(Simulation simulation, Humanoid nurse, Humanoid patient)
    {
        var map = simulation.Map;
        return Math.Abs(nurse.Tile.X - patient.Tile.X) + Math.Abs(nurse.Tile.Y - patient.Tile.Y) == 1
            && map[nurse.Tile.X, nurse.Tile.Y].RoomId == 0 && map[patient.Tile.X, patient.Tile.Y].RoomId == 0;
    }

    /// <summary>
    /// <c>Epidemic:markedPatientsCallForVaccination</c> and the calls dispatcher's vaccination call: each marked patient
    /// standing still and not waiting for a nurse calls for one. A free nurse out of rooms within 5 tiles, the nearest by
    /// path (one who cannot reach them counts 10,000 more), answers (<c>verifyStaffForVaccination</c>,
    /// <c>getPriorityForVaccination</c>) and walks to the free tile next to the patient she reaches soonest
    /// (<c>getBestVaccinationTile</c>); with none, the patient calls again later.
    /// </summary>
    private static void MarkedPatientsCallForVaccination(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        foreach (var id in epidemic.InfectedIds)
        {
            if (simulation.FindEntity(id) is not Humanoid { Behaviour: PatientBehaviour { IsMarkedForVaccination: true } patient, IsDespawning: false } humanoid)
            {
                continue;
            }

            if (patient.VaccinatingNurseId != 0
                && simulation.FindEntity(patient.VaccinatingNurseId) is Humanoid { Behaviour: NurseBehaviour reserved } && reserved.VaccinatePatientId == id)
            {
                continue;
            }

            patient.VaccinatingNurseId = 0;
            if (!patient.IsStatic(humanoid) || simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId != 0)
            {
                continue;
            }

            if (FindNurseForVaccination(simulation, hospital, humanoid) is not var (nurse, nurseBehaviour))
            {
                continue;
            }

            if (BestVaccinationTile(simulation, nurse, humanoid) is { } tile)
            {
                patient.VaccinatingNurseId = nurse.Id;
                nurseBehaviour.StartVaccination(simulation, nurse, humanoid, tile);
            }
        }
    }

    private static (Humanoid Nurse, NurseBehaviour Behaviour)? FindNurseForVaccination(Simulation simulation, Hospital hospital, Humanoid patient)
    {
        const int Radius = 5;
        const int UnreachablePenalty = 10_000;
        (Humanoid, NurseBehaviour)? best = null;
        var bestScore = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Humanoid { Behaviour: NurseBehaviour { IsFree: true } behaviour, IsDespawning: false } nurse || nurse.Employer != hospital.Player
                || nurse.IsMoving || simulation.Map[nurse.Tile.X, nurse.Tile.Y].RoomId != 0
                || Math.Abs(nurse.Tile.X - patient.Tile.X) > Radius || Math.Abs(nurse.Tile.Y - patient.Tile.Y) > Radius)
            {
                continue;
            }

            var score = simulation.Pathfinder.FindDistance(simulation.Map, nurse.Tile, patient.Tile) ?? UnreachablePenalty;
            if (best is null || score < bestScore)
            {
                best = (nurse, behaviour);
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary><c>getBestVaccinationTile</c>: of the tiles next to the patient with nobody and nothing on them, the nearest the nurse can reach.</summary>
    private static TilePosition? BestVaccinationTile(Simulation simulation, Humanoid nurse, Humanoid patient)
    {
        // A patient on a bench is vaccinated from the tile in front of them.
        if (patient.Behaviour is PatientBehaviour seated && seated.SeatedBench(simulation) is { } bench)
        {
            return People.RoomVisits.UseTile(simulation, bench);
        }

        var map = simulation.Map;
        var x = patient.Tile.X;
        var y = patient.Tile.Y;
        ReadOnlySpan<TilePosition> squares = [new(x - 1, y), new(x + 1, y), new(x, y - 1), new(x, y + 1)];
        TilePosition? best = null;
        var shortest = 0;
        foreach (var square in squares)
        {
            if (!map.Contains(square.X, square.Y) || map[square.X, square.Y].ObjectId != 0
                || simulation.Entities.Any(entity => entity is Humanoid { IsDespawning: false } other && other.Tile == square))
            {
                continue;
            }

            if (simulation.Pathfinder.FindDistance(map, nurse.Tile, square) is { } distance && (best is null || distance < shortest))
            {
                best = square;
                shortest = distance;
            }
        }

        return best;
    }

    /// <summary><c>Epidemic:addContagiousPatient</c>.</summary>
    private static void AddContagiousPatient(Epidemic epidemic, Humanoid humanoid, PatientBehaviour patient)
    {
        patient.IsInfected = true;
        epidemic.InfectedIds.Add(humanoid.Id);
    }

    /// <summary>
    /// <c>Epidemic:infectOtherPatients</c>: each infected patient may pass the disease on to patients on the four tiles
    /// next to them, in the same room or both in corridors, inside the hospital. Each patient is tried only once.
    /// </summary>
    private static void InfectOtherPatients(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        var rules = simulation.Rules.Epidemics;
        var map = simulation.Map;

        // Patients infected on the way are walked too, as CorsixTH's ipairs over the growing list.
        for (var i = 0; i < epidemic.InfectedIds.Count; i++)
        {
            if (simulation.FindEntity(epidemic.InfectedIds[i]) is not Humanoid { Behaviour: PatientBehaviour infectorPatient, IsDespawning: false } infector)
            {
                continue;
            }

            var x = infector.Tile.X;
            var y = infector.Tile.Y;
            ReadOnlySpan<TilePosition> squares = [new(x - 1, y), new(x + 1, y), new(x, y - 1), new(x, y + 1)];
            foreach (var square in squares)
            {
                if (!map.Contains(square.X, square.Y))
                {
                    continue;
                }

                foreach (var entity in simulation.Entities)
                {
                    if (entity is not Humanoid { Behaviour: PatientBehaviour victimPatient, IsDespawning: false } victim || victim.Tile != square
                        || !CanInfect(simulation, hospital, infector, infectorPatient, victim, victimPatient))
                    {
                        continue;
                    }

                    victimPatient.IsUnderInfectionAttempt = true;
                    epidemic.AttemptedInfections++;
                    if (epidemic.TotalInfections * SpreadScaleFactor < rules.SpreadFactor * epidemic.AttemptedInfections)
                    {
                        if (victimPatient.DiseaseId != infectorPatient.DiseaseId)
                        {
                            victimPatient.ChangeDisease(infectorPatient.Disease!);
                        }

                        AddContagiousPatient(epidemic, victim, victimPatient);
                        epidemic.TotalInfections++;
                    }
                }
            }
        }
    }

    /// <summary><c>canInfectOther</c>.</summary>
    private static bool CanInfect(Simulation simulation, Hospital hospital, Humanoid infector, PatientBehaviour infectorPatient, Humanoid victim, PatientBehaviour victimPatient)
    {
        if (infectorPatient.IsCured || infectorPatient.IsVaccinated)
        {
            return false;
        }

        if (!IsInHospital(simulation, hospital.Player, infector.Tile) || !IsInHospital(simulation, hospital.Player, victim.Tile))
        {
            return false;
        }

        if (victimPatient.IsInfected || victimPatient.IsCured || victimPatient.IsVaccinated || victimPatient.IsUnderInfectionAttempt || victimPatient.EmergencyIndex > 0)
        {
            return false;
        }

        if (infectorPatient.DiseaseId != victimPatient.DiseaseId && (victimPatient.Disease is not { IsContagious: true } || victimPatient.IsDiagnosed))
        {
            return false;
        }

        var map = simulation.Map;
        return map[infector.Tile.X, infector.Tile.Y].RoomId == map[victim.Tile.X, victim.Tile.Y].RoomId;
    }

    /// <summary><c>Epidemic:checkIfReadyToReveal</c>: once an infected patient is diagnosed.</summary>
    private static void CheckIfReadyToReveal(Simulation simulation, Epidemic epidemic)
    {
        if (epidemic.IsReadyToReveal)
        {
            return;
        }

        foreach (var id in epidemic.InfectedIds)
        {
            if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour { IsDiagnosed: true } })
            {
                epidemic.IsReadyToReveal = true;
                return;
            }
        }
    }

    /// <summary>
    /// <c>Epidemic:revealEpidemic</c> and <c>sendInitialFax</c>: the fax asks whether to declare, for a fine of 2,000 or
    /// <c>EpidemicFine</c> for each infected patient, or to cover up. It cannot come while an emergency's fax waits; the
    /// epidemic is then dropped.
    /// </summary>
    private static void Reveal(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        epidemic.IsRevealed = true;
        epidemic.DeclareFine = CalculateFine(simulation, CountInfectedPatients(simulation, epidemic));
        if (!CanQueueFax(hospital))
        {
            ClearAllInfectedPatients(simulation, epidemic);
            hospital.Epidemics.Current = null;
            return;
        }

        epidemic.FaxExpires = simulation.Tick + FaxHours;
    }

    /// <summary><c>Epidemic:resolveDeclaration</c>: everyone infected counts as vaccinated; the fine and reputation (a hundredth of it) go.</summary>
    private static void ResolveDeclaration(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        ClearAllInfectedPatients(simulation, epidemic);
        hospital.Spend(simulation.Hours, epidemic.DeclareFine, TransactionKind.EpidemicFine, epidemic.DiseaseId);
        epidemic.ReputationHit = ReputationFromFine(epidemic.DeclareFine);

        // CorsixTH lowers the reputation directly, without holding it to 0–1000.
        hospital.UnconditionalChangeReputation(-epidemic.ReputationHit);
        epidemic.Outcome = EpidemicOutcome.Declared;
        hospital.Epidemics.LastOutcome = EpidemicOutcome.Declared;
        hospital.Epidemics.LastAmount = epidemic.DeclareFine;
        hospital.Epidemics.Current = null;
    }

    /// <summary>
    /// <c>Epidemic:finishCoverUp</c>: the watch closes and the health inspector comes, from a random spawn point, heading
    /// for reception.
    /// </summary>
    private static void FinishCoverUp(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        epidemic.WatchTimer = -2;
        if (epidemic.InspectorId != 0)
        {
            return;
        }

        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        if (points.Count == 0)
        {
            // CorsixTH needs a spawn point; without one the inspector's verdict comes at once.
            epidemic.InspectorId = -1;
            InspectorArrived(simulation, hospital);
            return;
        }

        var spawn = points[simulation.Random.Next(points.Count)];
        var inspector = simulation.AddEntity(id => Humanoid.CreateInspector(id, spawn, hospital.Player));
        inspector.Behaviour = new InspectorBehaviour();
        epidemic.InspectorId = inspector.Id;
    }

    /// <summary>
    /// <c>Epidemic:checkInfectedLeftHospital</c>: during the cover-up, an infected patient not cured who leaves the grounds
    /// gives it away, and the inspector comes at once.
    /// </summary>
    private static void CheckInfectedLeftHospital(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        if (!epidemic.IsCoverUpActive)
        {
            return;
        }

        foreach (var id in epidemic.InfectedIds)
        {
            if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour patient } humanoid
                && (patient.Phase == PatientPhase.GoingHome || patient.IsSetToDie) && !patient.IsCured
                && !IsInHospital(simulation, hospital.Player, humanoid.Tile))
            {
                FinishCoverUp(simulation, hospital, epidemic);
                return;
            }
        }
    }

    /// <summary><c>Epidemic:checkNoInfectedPatients</c>: during the cover-up, with nobody left infected the inspector comes at once.</summary>
    private static void CheckNoInfectedPatients(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        if (epidemic.IsCoverUpActive && CountInfectedPatients(simulation, epidemic) == 0)
        {
            FinishCoverUp(simulation, hospital, epidemic);
        }
    }

    /// <summary>
    /// <c>Epidemic:checkPatientsForRemoval</c>: patients who died or are gone leave the epidemic, and so do those on their
    /// way out before a cover-up.
    /// </summary>
    private static void CheckPatientsForRemoval(Simulation simulation, Epidemic epidemic)
    {
        for (var i = epidemic.InfectedIds.Count - 1; i >= 0; i--)
        {
            var gone = simulation.FindEntity(epidemic.InfectedIds[i]) is not Humanoid { Behaviour: PatientBehaviour patient } humanoid
                || (humanoid.IsDespawning && patient.Phase != PatientPhase.GoingHome) || patient.Phase == PatientPhase.Dying
                || (!epidemic.IsCoverUp && (patient.Phase == PatientPhase.GoingHome || patient.IsSetToDie));
            if (gone)
            {
                epidemic.InfectedIds.RemoveAt(i);
            }
        }
    }

    /// <summary><c>Epidemic:countInfectedPatients</c>: infected and not cured (vaccinated patients still count).</summary>
    public static int CountInfectedPatients(Simulation simulation, Epidemic epidemic)
    {
        var count = 0;
        foreach (var id in epidemic.InfectedIds)
        {
            if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour { IsInfected: true, IsCured: false } })
            {
                count++;
            }
        }

        return count;
    }

    /// <summary><c>Epidemic:clearAllInfectedPatients</c>: everyone infected counts as vaccinated.</summary>
    private static void ClearAllInfectedPatients(Simulation simulation, Epidemic epidemic)
    {
        foreach (var id in epidemic.InfectedIds)
        {
            if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour patient })
            {
                patient.IsVaccinated = true;
                patient.VaccinatingNurseId = 0;
            }
        }
    }

    /// <summary><c>Epidemic:cancelEpidemic</c>: the inspector goes home, and everyone infected counts as vaccinated.</summary>
    private static void Cancel(Simulation simulation, Epidemic epidemic)
    {
        if (simulation.FindEntity(epidemic.InspectorId) is Humanoid { Behaviour: InspectorBehaviour inspector } humanoid)
        {
            inspector.GoHome(simulation, humanoid);
        }

        ClearAllInfectedPatients(simulation, epidemic);
        epidemic.InfectedIds.Clear();
    }

    /// <summary>
    /// <c>Epidemic:determineFaxAndFines</c>: the fine for those still infected; nobody infected brings compensation from
    /// <c>EpidemicCompLo</c> to <c>EpidemicCompHi</c>; <c>EpidemicEvacMinimum</c> or more evacuate the hospital.
    /// </summary>
    private static void DetermineFines(Simulation simulation, Epidemic epidemic, int stillInfected)
    {
        var rules = simulation.Rules.Epidemics;
        epidemic.CoverUpFine = CalculateFine(simulation, stillInfected);
        if (stillInfected == 0)
        {
            epidemic.Compensation = simulation.Random.NextInclusive(rules.CompensationLow, rules.CompensationHigh);
            epidemic.Outcome = EpidemicOutcome.CoveredUp;
        }
        else if (stillInfected < rules.EvacuationMinimum)
        {
            epidemic.Outcome = EpidemicOutcome.Fined;
        }
        else
        {
            epidemic.Outcome = EpidemicOutcome.Evacuated;
        }
    }

    /// <summary>
    /// <c>Epidemic:applyOutcome</c>: a failed cover-up costs the fine and reputation (a hundredth of the fine, or a third
    /// of the reputation when the hospital is evacuated); one that worked brings the compensation.
    /// </summary>
    private static void ApplyOutcome(Simulation simulation, Hospital hospital, Epidemic epidemic)
    {
        if (epidemic.Compensation == 0)
        {
            if (epidemic.Outcome == EpidemicOutcome.Evacuated)
            {
                epidemic.ReputationHit = (int)Math.Floor((hospital.Reputation / 3m) + 0.5m);
                EvacuateHospital(simulation, hospital);
            }
            else
            {
                epidemic.ReputationHit = ReputationFromFine(epidemic.CoverUpFine);
            }

            hospital.Spend(simulation.Hours, epidemic.CoverUpFine, TransactionKind.EpidemicCoverUpFine, epidemic.DiseaseId);
            hospital.UnconditionalChangeReputation(-epidemic.ReputationHit);
            hospital.Epidemics.LastAmount = epidemic.CoverUpFine;
        }
        else
        {
            hospital.Receive(simulation.Hours, epidemic.Compensation, TransactionKind.EpidemicCompensation, epidemic.DiseaseId);
            hospital.Epidemics.LastAmount = epidemic.Compensation;
        }

        hospital.Epidemics.LastOutcome = epidemic.Outcome;
        hospital.Epidemics.Current = null;
    }

    /// <summary><c>Epidemic:evacuateHospital</c>: every patient past reception and not already leaving goes home.</summary>
    private static void EvacuateHospital(Simulation simulation, Hospital hospital)
    {
        foreach (var entity in simulation.Entities.ToList())
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour { HasPassedReception: true } patient, IsDespawning: false } humanoid
                && humanoid.HospitalPlayer == hospital.Player)
            {
                patient.Evacuate(simulation, humanoid);
            }
        }
    }

    /// <summary>
    /// <c>Epidemic:markPatientsAsPassedReception</c>: when an epidemic starts, the hospital's patients on its grounds and
    /// not queuing at a desk count as past reception, for the evacuation.
    /// </summary>
    private static void MarkPatientsAsPassedReception(Simulation simulation, int player)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false } humanoid && humanoid.HospitalPlayer == player
                && patient.Phase != PatientPhase.InReceptionQueue && IsInHospital(simulation, player, humanoid.Tile))
            {
                patient.HasPassedReception = true;
            }
        }
    }

    /// <summary><c>calculateInfectedFine</c>.</summary>
    private static int CalculateFine(Simulation simulation, int infected) => Math.Max(MinimumFine, infected * simulation.Rules.Epidemics.Fine);

    /// <summary><c>getBaseReputationFromFine</c>: a hundredth of the fine, rounded.</summary>
    private static int ReputationFromFine(int fine) => (int)Math.Floor((fine / 100m) + 0.5m);

    /// <summary><c>Hospital:isInHospital</c>: a hospital tile of a parcel the player owns.</summary>
    internal static bool IsInHospital(Simulation simulation, int player, TilePosition tile)
    {
        var map = simulation.Map;
        if (!map.Contains(tile.X, tile.Y))
        {
            return false;
        }

        var cell = map[tile.X, tile.Y];
        return cell.Has(TileFlags.Hospital) && cell.Parcel < map.ParcelCount && map.GetParcelOwner(cell.Parcel) == player + 1;
    }
}
