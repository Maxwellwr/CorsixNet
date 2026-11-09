// Ported from CorsixTH CorsixTH/Lua/research_department.lua (ResearchDepartment) and hospital.lua
// (canConcentrateResearch, getObjectBuildCost, the research cost at the end of the month).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Research;

/// <summary>What a research area works on.</summary>
public enum ResearchTargetKind : byte
{
    /// <summary>Nothing more to research in the area (CorsixTH's <c>current = nil</c>).</summary>
    None,

    /// <summary>The dummy that takes the area's points while nothing it could work on is there yet (<c>drain</c>).</summary>
    Drain,

    /// <summary>An object to discover or improve; <see cref="ResearchTarget.Id"/> is the object's id.</summary>
    Object,

    /// <summary>A drug to improve; <see cref="ResearchTarget.Id"/> is the disease's id.</summary>
    Drug,
}

/// <summary>What a research area works on.</summary>
public readonly record struct ResearchTarget(ResearchTargetKind Kind, string Id)
{
    public static ResearchTarget None => new(ResearchTargetKind.None, string.Empty);

    public static ResearchTarget Drain => new(ResearchTargetKind.Drain, string.Empty);

    public static ResearchTarget Object(string id) => new(ResearchTargetKind.Object, id);

    public static ResearchTarget Drug(string id) => new(ResearchTargetKind.Drug, id);

    public bool IsNone => Kind == ResearchTargetKind.None;
}

/// <summary>Research on an object (an entry of CorsixTH's <c>research_progress</c>).</summary>
public sealed class ObjectResearch
{
    /// <summary>Points gathered towards the next discovery or improvement.</summary>
    public decimal Points { get; internal set; }

    /// <summary>How many uses a new one takes before breaking down (<c>start_strength</c>).</summary>
    public int StartStrength { get; internal set; }

    /// <summary>What it costs now (<c>cost</c>), lowered by improvements.</summary>
    public int Cost { get; internal set; }

    public bool Discovered { get; internal set; }

    /// <summary>Improvements of its strength so far (<c>strength_imp</c>).</summary>
    public int StrengthImprovements { get; internal set; }

    /// <summary>Improvements of its cost so far (<c>cost_imp</c>).</summary>
    public int CostImprovements { get; internal set; }
}

/// <summary>One area of the research policy: its share of the points and what it works on.</summary>
public sealed class ResearchArea
{
    /// <summary>The area's share of the points, in percent (<c>frac</c>).</summary>
    public int Share { get; internal set; }

    public ResearchTarget Current { get; internal set; } = ResearchTarget.None;
}

/// <summary>
/// A hospital's research: points from the researchers are divided between the areas by the policy and, with a little
/// randomness, go to what each area works on; enough points discover an object, improve a drug's effectiveness or cost,
/// or a machine's strength or cost. Equipment can also come by itself in a month the level names. Discovering an object
/// makes the rooms that need it buildable once all their researchable objects are discovered.
/// </summary>
/// <remarks>
/// CorsixTH walks its research tables in Lua's <c>pairs</c> order, which is not fixed; here objects go in the order of its
/// objects folder (<see cref="ResearchObjects.All"/>), diseases in the catalogue's and the areas in the research window's.
/// </remarks>
public sealed class ResearchDepartment
{
    /// <summary>What research costs a day per researcher at a full policy (<c>researchCost</c>'s 3).</summary>
    private const int CostPerResearcher = 3;

    private const int MaxEntries = 1024;

    private readonly SortedDictionary<string, ObjectResearch> _objects = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, decimal> _drugs = new(StringComparer.Ordinal);
    private readonly ResearchArea[] _areas = [new(), new(), new(), new(), new()];

    /// <summary>A department with nothing to research.</summary>
    internal ResearchDepartment()
    {
    }

    /// <summary>Points the drain took so far; they are never used.</summary>
    public decimal DrainPoints { get; private set; }

    /// <summary>The sum of the areas' shares, in percent (<c>research_policy.total</c>).</summary>
    public int Total { get; private set; }

    /// <summary>Research costs gathered this month, paid at its end (CorsixTH's <c>acc_research_cost</c>).</summary>
    public int AccumulatedCost { get; private set; }

    /// <summary>The disease research concentrates on, or empty (<c>concentrate_research</c>).</summary>
    public string Concentrated { get; private set; } = string.Empty;

    /// <summary>The research on the objects the level has, by id.</summary>
    public IReadOnlyDictionary<string, ObjectResearch> Objects => _objects;

    /// <summary>Points gathered towards each drug's next improvement, by disease id.</summary>
    public IReadOnlyDictionary<string, decimal> Drugs => _drugs;

    public ResearchArea Area(ResearchCategory category) => _areas[(int)category];

    /// <summary>
    /// <c>ResearchDepartment:initResearch</c>: the level's researchable objects and the drugs of its diseases; each area
    /// gets 20 % if it has something to work on, and the shares of finished areas go to the first that has.
    /// </summary>
    internal static ResearchDepartment Create(LevelRules rules, Hospital hospital)
    {
        var research = new ResearchDepartment();
        ResearchObject? cure = null, diagnosis = null, improve = null;
        foreach (var obj in ResearchObjects.All)
        {
            if (rules.Research.Object(obj.ObjectType) is not { ForLevel: true } entry || obj.Category is null)
            {
                continue;
            }

            research._objects[obj.Id] = new ObjectResearch
            {
                StartStrength = entry.StartStrength,
                Cost = rules.FreeBuild ? 0 : entry.StartCost,
                Discovered = entry.StartAvail,
            };
            if (!entry.StartAvail)
            {
                if (obj.Category == ResearchCategory.Cure)
                {
                    cure = obj;
                }
                else
                {
                    diagnosis = obj;
                }
            }

            if (obj.IsMachine && entry.StartAvail)
            {
                improve = obj;
            }
        }

        string? drug = null;
        foreach (var disease in rules.Diseases.AvailableDiseases())
        {
            if (IsDrugDisease(disease))
            {
                research._drugs[disease.Id] = 0;
                if (hospital.CaseOf(disease, rules.Diseases).Discovered)
                {
                    drug = disease.Id;
                }
            }
        }

        research.Area(ResearchCategory.Cure).Share = cure is null ? 0 : 20;
        research.Area(ResearchCategory.Cure).Current = cure is null ? ResearchTarget.None : ResearchTarget.Object(cure.Id);
        research.Area(ResearchCategory.Diagnosis).Share = diagnosis is null ? 0 : 20;
        research.Area(ResearchCategory.Diagnosis).Current = diagnosis is null ? ResearchTarget.None : ResearchTarget.Object(diagnosis.Id);
        research.Area(ResearchCategory.Drugs).Share = 20;
        research.Area(ResearchCategory.Drugs).Current = drug is null ? ResearchTarget.Drain : ResearchTarget.Drug(drug);
        research.Area(ResearchCategory.Improvements).Share = improve is null ? 0 : 20;
        research.Area(ResearchCategory.Improvements).Current = improve is null ? ResearchTarget.None : ResearchTarget.Object(improve.Id);
        research.Area(ResearchCategory.Specialisation).Share = 20;
        research.Area(ResearchCategory.Specialisation).Current = ResearchTarget.Drain;

        var sum = research._areas.Sum(area => area.Share);
        if (sum == 20)
        {
            // No research to be done.
            research.Area(ResearchCategory.Specialisation).Share = 0;
            research.Total = 0;
        }
        else
        {
            // If some category is already done, put the free points in another one.
            if (sum < 100)
            {
                foreach (var area in research._areas)
                {
                    if (area.Share > 0)
                    {
                        area.Share += 100 - sum;
                        sum = 100;
                        break;
                    }
                }
            }

            research.Total = sum;
        }

        return research;
    }

    /// <summary>CorsixTH's <c>disease.drug</c>: diseases cured in the pharmacy.</summary>
    internal static bool IsDrugDisease(DiseaseDefinition disease) => disease.TreatmentRooms is ["pharmacy", ..];

    /// <summary><c>Hospital:getObjectBuildCost</c>: what an object costs now; research may have lowered a machine's cost.</summary>
    public int ObjectCost(LevelRules rules, string objectId) =>
        rules.FreeBuild ? 0 : _objects.TryGetValue(objectId, out var research) ? research.Cost : rules.ObjectCost(objectId);

    /// <summary>
    /// <c>ResearchDepartment:checkAutomaticDiscovery</c>, at the end of each month: equipment the level brings in this
    /// month of the game (<c>objects[n].WhenAvail</c>) is discovered by itself.
    /// </summary>
    internal void CheckAutomaticDiscovery(Simulation simulation, Hospital hospital, int monthOfGame)
    {
        foreach (var obj in ResearchObjects.All)
        {
            if (obj.IsMachine && _objects.TryGetValue(obj.Id, out var research) && !research.Discovered
                && simulation.Rules.Research.Object(obj.ObjectType) is { WhenAvail: not 0 and var month } && monthOfGame >= month)
            {
                DiscoverObject(simulation, hospital, obj);
            }
        }
    }

    /// <summary>
    /// <c>ResearchDepartment:addResearchPoints</c>: the points, divided by the level's divisor and lowered when the
    /// policy's total is under 100 %, go to each area's work by its share, times 0.75, 1 or 1.25 at random; enough points
    /// discover an object, improve a drug or improve a machine.
    /// </summary>
    public void AddPoints(Simulation simulation, Hospital hospital, int points)
    {
        var rules = simulation.Rules.Research;
        var share = (decimal)Math.Ceiling(points * Total / (100m * rules.PointsDivisor));
        foreach (var category in Enum.GetValues<ResearchCategory>())
        {
            var area = Area(category);
            if (area.Current.IsNone)
            {
                continue;
            }

            var factor = simulation.Random.NextInclusive(1, 3) switch { 1 => 0.75m, 2 => 1m, _ => 1.25m };
            var added = factor * share * area.Share / 100m;
            var target = area.Current;
            switch (target.Kind)
            {
                case ResearchTargetKind.Drain:
                    DrainPoints += added;
                    break;
                case ResearchTargetKind.Drug when _drugs.TryGetValue(target.Id, out var drugPoints):
                    drugPoints += added;
                    if (Required(simulation, target) is { } drugRequired && drugRequired < drugPoints)
                    {
                        _drugs[target.Id] = 0;
                        ImproveDrug(simulation, hospital, target.Id);
                    }
                    else
                    {
                        _drugs[target.Id] = drugPoints;
                    }

                    break;
                case ResearchTargetKind.Object when _objects.TryGetValue(target.Id, out var research):
                    research.Points += added;
                    if (Required(simulation, target) is { } required && required < research.Points)
                    {
                        research.Points = 0;
                        var obj = ResearchObjects.Find(target.Id)!;
                        if (!research.Discovered)
                        {
                            DiscoverObject(simulation, hospital, obj);
                        }
                        else
                        {
                            ImproveMachine(simulation, hospital, obj);
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// <c>ResearchDepartment:getResearchRequired</c>: an object's <c>RschReqd</c> (or its fallback expertise's), and once
    /// discovered a share of it for each improvement, growing with each; a drug's expertise's <c>RschReqd</c>.
    /// </summary>
    public decimal? Required(Simulation simulation, ResearchTarget target)
    {
        var rules = simulation.Rules.Research;
        switch (target.Kind)
        {
            case ResearchTargetKind.Object when ResearchObjects.Find(target.Id) is { } obj && _objects.TryGetValue(obj.Id, out var research):
                decimal? required = rules.Object(obj.ObjectType)?.ResearchRequired ?? (obj.Fallback > 0 ? rules.ExpertiseRequired(obj.Fallback) : null);
                if (required is { } points && research.Discovered)
                {
                    var percent = rules.ImproveCostPercent + (rules.ImproveIncrementPercent * research.CostImprovements);
                    required = points * percent / 100m;
                }

                return required;
            case ResearchTargetKind.Drug when DiseaseCatalog.Find(target.Id) is { } disease:
                return rules.ExpertiseRequired(disease.ExpertiseId);
            default:
                return null;
        }
    }

    /// <summary>
    /// <c>ResearchDepartment:improveDrug</c>: the drug gets cheaper by a tenth (not below <c>MinDrugCost</c>) or more
    /// effective by <c>DrugImproveRate</c> (up to 100 %), at random, and one time in seven both; then the drugs area moves
    /// on to the drug that needs it most.
    /// </summary>
    private void ImproveDrug(Simulation simulation, Hospital hospital, string diseaseId)
    {
        var rules = simulation.Rules.Research;
        var disease = DiseaseCatalog.Find(diseaseId)!;
        var entry = hospital.CaseOf(disease, simulation.Rules.Diseases);
        var atMaxEffectiveness = entry.CureEffectiveness >= 100;
        var atMinCost = entry.DrugCost <= rules.MinDrugCost;
        if (!(atMaxEffectiveness && atMinCost))
        {
            bool cost, effectiveness;
            if (atMaxEffectiveness)
            {
                (cost, effectiveness) = (true, false);
            }
            else if (atMinCost)
            {
                (cost, effectiveness) = (false, true);
            }
            else
            {
                cost = simulation.Random.NextInclusive(1, 2) == 1;
                effectiveness = !cost;
                if (simulation.Random.NextInclusive(1, 7) == 1)
                {
                    (cost, effectiveness) = (true, true);
                }
            }

            if (cost)
            {
                entry.DrugCost = Math.Max(rules.MinDrugCost, (int)Math.Floor(entry.DrugCost * 0.9m));
            }

            if (effectiveness)
            {
                entry.CureEffectiveness = Math.Min(100, entry.CureEffectiveness + rules.DrugImproveRate);
            }
        }

        // decideNextResearch.
        var target = ResearchTarget.Drug(diseaseId);
        if (entry.CureEffectiveness >= 100 && Area(ResearchCategory.Specialisation).Current == target)
        {
            Area(ResearchCategory.Specialisation).Current = ResearchTarget.Drain;
            Concentrated = string.Empty;
            SetConcentration(simulation, hospital);
        }

        if (Area(ResearchCategory.Drugs).Current == target)
        {
            NextResearch(simulation, hospital, ResearchCategory.Drugs);
        }
    }

    /// <summary>
    /// <c>ResearchDepartment:improveMachine</c>: strength and cost improve in turn; the strength by <c>ResearchIncrement</c>,
    /// the cost by an eighth rounded to tens, after which the improvements area moves on to the machine that needs it most.
    /// </summary>
    private void ImproveMachine(Simulation simulation, Hospital hospital, ResearchObject machine)
    {
        var rules = simulation.Rules.Research;
        var research = _objects[machine.Id];
        if (research.StrengthImprovements > research.CostImprovements)
        {
            var decrease = (int)Math.Round(research.Cost * 0.125m / 10m, MidpointRounding.AwayFromZero) * 10;
            research.Cost -= decrease;
            research.CostImprovements++;
            var target = ResearchTarget.Object(machine.Id);
            if (research.StartStrength >= rules.MaxObjectStrength && Area(ResearchCategory.Specialisation).Current == target)
            {
                Area(ResearchCategory.Specialisation).Current = ResearchTarget.Drain;
                Concentrated = string.Empty;
                SetConcentration(simulation, hospital);
            }

            if (Area(ResearchCategory.Improvements).Current == target)
            {
                NextResearch(simulation, hospital, ResearchCategory.Improvements);
            }
        }
        else
        {
            research.StartStrength += rules.ResearchIncrement;
            research.StrengthImprovements++;
        }
    }

    /// <summary>
    /// <c>ResearchDepartment:discoverObject</c>: the object is discovered; rooms whose researchable objects are all
    /// discovered can now be built; the improvements area takes the weakest discovered machine if it had nothing; then
    /// the object's area moves on.
    /// </summary>
    private void DiscoverObject(Simulation simulation, Hospital hospital, ResearchObject obj)
    {
        _objects[obj.Id].Discovered = true;
        foreach (var room in simulation.Content.Rooms)
        {
            if (hospital.IsRoomDiscovered(room.Id) || !simulation.Rules.IsRoomAvailable(room))
            {
                continue;
            }

            if (LevelRules.Needed(room).Any(needed => _objects.TryGetValue(needed.ObjectId, out var research) && !research.Discovered))
            {
                continue;
            }

            hospital.DiscoverRoom(room.Id);
            var improvements = Area(ResearchCategory.Improvements);
            if (improvements.Current.Kind is ResearchTargetKind.None or ResearchTargetKind.Drain)
            {
                var weakest = WeakestMachine(simulation);
                if (weakest is not null)
                {
                    improvements.Current = ResearchTarget.Object(weakest);
                }
            }
        }

        if (obj.Category is { } category)
        {
            NextResearch(simulation, hospital, category);
        }
    }

    /// <summary>The discovered machine with the lowest strength below the most, or null.</summary>
    private string? WeakestMachine(Simulation simulation)
    {
        var minimum = simulation.Rules.Research.MaxObjectStrength;
        string? weakest = null;
        foreach (var obj in ResearchObjects.All)
        {
            if (obj.IsMachine && _objects.TryGetValue(obj.Id, out var research) && research.Discovered && research.StartStrength < minimum)
            {
                weakest = obj.Id;
                minimum = research.StartStrength;
            }
        }

        return weakest;
    }

    /// <summary>
    /// <c>ResearchDepartment:nextResearch</c>: once the area's work is discovered (or it is a drug), it picks its next:
    /// the least effective known drug, the weakest discovered machine, or an object of its kind still to discover. If what
    /// needs doing is not known yet, the drain takes the points; if nothing is left, the area's share goes to the others.
    /// </summary>
    private void NextResearch(Simulation simulation, Hospital hospital, ResearchCategory category)
    {
        var area = Area(category);
        var current = area.Current;
        if (current.Kind == ResearchTargetKind.Drain || current.IsNone
            || (current.Kind == ResearchTargetKind.Object && !(_objects.TryGetValue(current.Id, out var progress) && progress.Discovered)))
        {
            return;
        }

        area.Current = ResearchTarget.None;
        var foundOne = false;
        switch (category)
        {
            case ResearchCategory.Drugs:
                var worst = 100;
                foreach (var disease in simulation.Rules.Diseases.AvailableDiseases())
                {
                    var entry = hospital.CaseOf(disease, simulation.Rules.Diseases);
                    if (entry.CureEffectiveness < worst)
                    {
                        foundOne = true;
                        if (entry.Discovered && IsDrugDisease(disease))
                        {
                            area.Current = ResearchTarget.Drug(disease.Id);
                            worst = entry.CureEffectiveness;
                        }
                    }
                }

                break;
            case ResearchCategory.Improvements:
                var minimum = simulation.Rules.Research.MaxObjectStrength;
                foreach (var obj in ResearchObjects.All)
                {
                    if (obj.IsMachine && _objects.TryGetValue(obj.Id, out var research) && research.StartStrength < simulation.Rules.Research.MaxObjectStrength)
                    {
                        if (research.Discovered && research.StartStrength < minimum)
                        {
                            area.Current = ResearchTarget.Object(obj.Id);
                            minimum = research.StartStrength;
                        }
                        else
                        {
                            foundOne = true;
                        }
                    }
                }

                break;
            default:
                foreach (var obj in ResearchObjects.All)
                {
                    if (obj.Category == category && _objects.TryGetValue(obj.Id, out var research) && !research.Discovered)
                    {
                        area.Current = ResearchTarget.Object(obj.Id);
                    }
                }

                break;
        }

        if (foundOne && area.Current.IsNone)
        {
            // There is a drug or machine which needs improving, but it has not been discovered yet.
            area.Current = ResearchTarget.Drain;
            DrainPoints = 0;
        }

        if (area.Current.IsNone)
        {
            // Nothing more to research.
            area.Share = 0;
            RedistributePoints();
        }
    }

    /// <summary><c>ResearchDepartment:redistributeResearchPoints</c>: a finished area's share goes to the others.</summary>
    private void RedistributePoints()
    {
        ResearchCategory[] policy = [ResearchCategory.Cure, ResearchCategory.Diagnosis, ResearchCategory.Drugs, ResearchCategory.Improvements];
        var specialisation = Area(ResearchCategory.Specialisation).Share;
        var sum = 0;
        var allFinished = true;
        foreach (var category in policy)
        {
            sum += Area(category).Share;
            if (Area(category).Current.Kind is not (ResearchTargetKind.None or ResearchTargetKind.Drain))
            {
                allFinished = false;
            }
        }

        if (allFinished)
        {
            Total = 0;
            Area(ResearchCategory.Specialisation).Current = ResearchTarget.None;
            return;
        }

        Total -= specialisation;
        if (sum == 0)
        {
            var working = policy.Where(category => !Area(category).Current.IsNone).ToList();
            foreach (var category in working)
            {
                Area(category).Share = Total / working.Count;
            }

            sum = Total / working.Count * working.Count;
            foreach (var category in working)
            {
                if (sum == Total)
                {
                    break;
                }

                Area(category).Share++;
                sum++;
            }
        }
        else
        {
            var newSum = specialisation;
            var maxValue = 0;
            ResearchCategory? maxCategory = null;
            foreach (var category in policy)
            {
                var area = Area(category);
                if (area.Share > maxValue)
                {
                    maxValue = area.Share;
                    maxCategory = category;
                }

                area.Share = (int)Math.Floor(Total * area.Share / (decimal)sum);
                newSum += area.Share;
            }

            // If there are still some points left redistribute them to the area with the most.
            if (newSum < Total && maxCategory is { } most)
            {
                Area(most).Share += Total - newSum;
            }
        }

        Total += specialisation;
    }

    /// <summary>
    /// <c>ResearchDepartment:discoverDisease</c>, once a disease is diagnosed for the first time: if the drugs area had
    /// nothing to work on and the disease has a drug, it takes it; and research may concentrate on it.
    /// </summary>
    internal void OnDiseaseDiscovered(Simulation simulation, Hospital hospital, DiseaseDefinition disease)
    {
        var drugs = Area(ResearchCategory.Drugs);
        if (drugs.Current.Kind is ResearchTargetKind.None or ResearchTargetKind.Drain && IsDrugDisease(disease) && _drugs.ContainsKey(disease.Id))
        {
            drugs.Current = ResearchTarget.Drug(disease.Id);
        }

        SetConcentration(simulation, hospital);
    }

    /// <summary>
    /// <c>ResearchDepartment:setResearchConcentration</c>: with nothing to concentrate on, research concentrates on the first
    /// known disease it can.
    /// </summary>
    private void SetConcentration(Simulation simulation, Hospital hospital)
    {
        if (Area(ResearchCategory.Specialisation).Current.Kind != ResearchTargetKind.Drain)
        {
            return;
        }

        foreach (var disease in simulation.Rules.Diseases.AvailableDiseases())
        {
            if (hospital.CaseOf(disease, simulation.Rules.Diseases).Discovered && CanConcentrate(simulation, hospital, disease))
            {
                Concentrate(simulation, hospital, disease);
                return;
            }
        }
    }

    /// <summary>
    /// <c>Hospital:canConcentrateResearch</c>: a disease whose room is not discovered yet, a drug not fully effective, or
    /// a disease whose machine is not as strong as it gets. Only diseases whose last room is in the game's content count.
    /// </summary>
    internal bool CanConcentrate(Simulation simulation, Hospital hospital, DiseaseDefinition disease)
    {
        if (ConcentrationRoom(simulation, disease) is not { } room)
        {
            return false;
        }

        if (!disease.IsPseudo && !hospital.IsRoomDiscovered(room.Id))
        {
            return true;
        }

        if (IsDrugDisease(disease))
        {
            return hospital.CaseOf(disease, simulation.Rules.Diseases).CureEffectiveness < 100;
        }

        var machine = LevelRules.Needed(room).Select(needed => ResearchObjects.Find(needed.ObjectId)).FirstOrDefault(obj => obj is { IsMachine: true });
        return machine is not null && _objects.TryGetValue(machine.Id, out var research)
            && research.StartStrength < simulation.Rules.Research.MaxObjectStrength;
    }

    /// <summary>
    /// <c>ResearchDepartment:concentrateResearch</c>, the casebook's concentrate button: again on the same disease cancels
    /// it; otherwise the specialisation area works on the disease's drug if its cabinet is discovered, or else on discovering
    /// or improving the object its room needs.
    /// </summary>
    public bool Concentrate(Simulation simulation, Hospital hospital, DiseaseDefinition disease)
    {
        var specialisation = Area(ResearchCategory.Specialisation);
        if (string.Equals(Concentrated, disease.Id, StringComparison.Ordinal))
        {
            Concentrated = string.Empty;
            specialisation.Current = ResearchTarget.Drain;
            return true;
        }

        if (ConcentrationRoom(simulation, disease) is not { } room)
        {
            return false;
        }

        ResearchObject? obj = null;
        foreach (var (objectId, _) in LevelRules.Needed(room))
        {
            if (ResearchObjects.Find(objectId) is { } candidate && _objects.ContainsKey(candidate.Id))
            {
                obj = candidate;
            }
        }

        if (obj is null)
        {
            return false;
        }

        Concentrated = disease.Id;
        specialisation.Current = IsDrugDisease(disease) && _objects[obj.Id].Discovered && _drugs.ContainsKey(disease.Id)
            ? ResearchTarget.Drug(disease.Id)
            : ResearchTarget.Object(obj.Id);
        return true;
    }

    /// <summary>The room research on a disease is about: its last treatment room, or a diagnosis's own room.</summary>
    private static Definitions.RoomDefinition? ConcentrationRoom(Simulation simulation, DiseaseDefinition disease)
    {
        var roomType = disease.IsPseudo
            ? disease.Id.StartsWith("diag_", StringComparison.Ordinal) ? disease.Id[5..] : null
            : disease.TreatmentRooms.Count > 0 ? disease.TreatmentRooms[^1] : null;
        return roomType is null ? null : simulation.Content.FindRoom(roomType);
    }

    /// <summary>
    /// <c>UIResearch:adjustResearch</c>: an area's share goes down to no less than 0, or up while the total is under 100 %
    /// and the area has something to work on.
    /// </summary>
    public void AdjustShare(ResearchCategory category, int change)
    {
        var area = Area(category);
        if (change < 0)
        {
            area.Share = Math.Max(0, area.Share + change);
        }
        else if (change > 0 && Total < 100 && !area.Current.IsNone)
        {
            area.Share += Math.Min(change, 100 - Total);
        }

        Total = _areas.Sum(a => a.Share);
    }

    /// <summary>
    /// <c>ResearchDepartment:researchCost</c>, at the end of each day: 3 a researcher at work, by the share of the areas
    /// that work on something real, rounded up.
    /// </summary>
    internal void AccumulateCost(int researchers)
    {
        var fraction = _areas.Where(area => area.Current.Kind is ResearchTargetKind.Object or ResearchTargetKind.Drug).Sum(area => area.Share);
        AccumulatedCost += (int)Math.Ceiling(CostPerResearcher * researchers * fraction / 100m);
    }

    /// <summary>Takes this month's research costs to be paid.</summary>
    internal int TakeAccumulatedCost()
    {
        var cost = AccumulatedCost;
        AccumulatedCost = 0;
        return cost;
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(_objects.Count);
        foreach (var (id, research) in _objects)
        {
            writer.WriteString(id);
            writer.WriteDecimal(research.Points);
            writer.WriteInt32(research.StartStrength);
            writer.WriteInt32(research.Cost);
            writer.WriteBool(research.Discovered);
            writer.WriteInt32(research.StrengthImprovements);
            writer.WriteInt32(research.CostImprovements);
        }

        writer.WriteInt32(_drugs.Count);
        foreach (var (id, points) in _drugs)
        {
            writer.WriteString(id);
            writer.WriteDecimal(points);
        }

        foreach (var area in _areas)
        {
            writer.WriteInt32(area.Share);
            writer.WriteByte((byte)area.Current.Kind);
            writer.WriteString(area.Current.Id);
        }

        writer.WriteDecimal(DrainPoints);
        writer.WriteInt32(Total);
        writer.WriteInt32(AccumulatedCost);
        writer.WriteString(Concentrated);
    }

    internal static ResearchDepartment Read(SimReader reader)
    {
        var research = new ResearchDepartment();
        var objects = reader.ReadCount(MaxEntries);
        for (var i = 0; i < objects; i++)
        {
            research._objects[reader.ReadString()] = new ObjectResearch
            {
                Points = reader.ReadDecimal(),
                StartStrength = reader.ReadInt32(),
                Cost = reader.ReadInt32(),
                Discovered = reader.ReadBool(),
                StrengthImprovements = reader.ReadInt32(),
                CostImprovements = reader.ReadInt32(),
            };
        }

        var drugs = reader.ReadCount(MaxEntries);
        for (var i = 0; i < drugs; i++)
        {
            research._drugs[reader.ReadString()] = reader.ReadDecimal();
        }

        foreach (var area in research._areas)
        {
            area.Share = reader.ReadInt32();
            var kind = reader.ReadByte();
            area.Current = new ResearchTarget(
                kind <= (byte)ResearchTargetKind.Drug ? (ResearchTargetKind)kind : throw new InvalidDataException($"Unknown research target {kind}."),
                reader.ReadString());
        }

        research.DrainPoints = reader.ReadDecimal();
        research.Total = reader.ReadInt32();
        research.AccumulatedCost = reader.ReadInt32();
        research.Concentrated = reader.ReadString();
        return research;
    }
}
