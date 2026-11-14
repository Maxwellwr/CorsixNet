// A VIP's visit, ported from CorsixTH CorsixTH/Lua/entities/humanoids/vip.lua (Vip:Vip, tickDay, getNextRoom, goHome,
// evaluateRoom, setVIPRating), humanoid_actions/vip_go_to_next_room.lua, humanoid_actions/seek_reception.lua,
// objects/reception_desk.lua (handleVIP) and hospital.lua (getAverageStaffAttribute, getAveragePatientAttribute,
// _getAverageHumanoidsAttribute, countSittingStanding, countStaffOfCategory), with utility.lua's rangeMapLookup.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Pathfinding;
using CorsixNet.Sim.Serialization;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Sim.People;

/// <summary>Where a VIP is in their visit.</summary>
public enum VipPhase : byte
{
    SeekReception,
    WalkingToReception,
    InReceptionQueue,

    /// <summary>Standing about until the day is out (CorsixTH's <c>waiting</c>), after reception or when a room went.</summary>
    Waiting,

    WalkingToRoom,

    /// <summary>Standing at a room's door, looking in.</summary>
    LookingAtRoom,

    GoingHome,
}

/// <summary>
/// A VIP visiting a hospital: they report at reception, look in at the rooms from their doors (the first one, then each
/// with a chance, and always a research department or ward), and on leaving rate the hospital from 1 (best) to 15.
/// </summary>
/// <remarks>
/// The arrival announcement is the client's (HospitalAnnouncements).
/// </remarks>
public sealed class VipBehaviour : HumanoidBehaviour, IQueueMember
{
    /// <summary>Ratings up to this bring a grant and more reputation.</summary>
    public const int PleasedRating = 7;

    /// <summary>How far around the VIP sees litter each day (<c>findObjectNear(self, "litter", 8)</c>).</summary>
    private const int LitterSightDistance = 8;

    /// <summary>The most litter seen that still counts as a clean hospital.</summary>
    private const int MaxCleanLitter = 10;

    /// <summary>The most litter ids read back from a save.</summary>
    private const int MaxFoundLitter = 1 << 16;

    /// <summary>Hours a VIP looks in at a room: CorsixTH's idle of 50, doubled by their slow animation.</summary>
    internal const int LookingHours = 100;

    /// <summary>The grant by rating (CorsixTH's documented rewards); none from 8 on.</summary>
    private static readonly int[] Rewards = [0, 4000, 2000, 1500, 1200, 800, 400, 200, 0];

    /// <summary>The reputation change by rating.</summary>
    private static readonly int[] ReputationChanges = [0, 50, 45, 40, 35, 30, 25, 20, 15, 10, 5, -5, -10, -15, -20, -25];

    /// <summary>Rooms a VIP never skips (<c>vip_must_visit</c>).</summary>
    private static readonly string[] MustVisit = ["research", "ward"];

    public VipBehaviour(int name, int rating)
    {
        Name = name;
        Rating = rating;
    }

    public override BehaviourKind Kind => BehaviourKind.Vip;

    /// <summary>Which of the VIP names (1 to 10).</summary>
    public int Name { get; }

    public VipPhase Phase { get; private set; }

    /// <summary>The rating so far: 12 less up to 5 on arrival, from 1 (best) to 15 once they leave.</summary>
    public int Rating { get; private set; }

    /// <summary>The grant the hospital gets when they are off the grounds.</summary>
    public int CashReward { get; private set; }

    /// <summary>The reputation the hospital gains or loses when they are off the grounds.</summary>
    public int ReputationReward { get; private set; }

    /// <summary>The hospital's deaths when the VIP came.</summary>
    public int EnterDeaths { get; init; }

    /// <summary>The hospital's visitors when the VIP came.</summary>
    public int EnterVisitors { get; init; }

    /// <summary>The hospital's cures when the VIP came.</summary>
    public int EnterCures { get; init; }

    /// <summary>The hospital's patients on the map when the VIP came.</summary>
    public int EnterPatients { get; init; }

    /// <summary>The VIP looks in at a room after the first when a roll from 0 to this comes up this (1: one in two).</summary>
    public int RoomVisitChance { get; init; } = 1;

    /// <summary>Rooms looked in at so far.</summary>
    public int VisitedRooms { get; private set; }

    /// <summary>What the rooms looked in at scored together (fire extinguishers, bins, plants).</summary>
    public int RoomEvaluation { get; private set; }

    /// <summary>Pieces of rubbish the VIP has seen (CorsixTH's <c>num_vomit_noninducing</c>).</summary>
    public int RubbishFound { get; private set; }

    /// <summary>Puke, pee, dead rats and soot the VIP has seen (<c>num_vomit_inducing</c>).</summary>
    public int VomitFound { get; private set; }

    /// <summary>The litter already counted (<c>found_vomit</c>), by entity id.</summary>
    private readonly List<int> _foundLitter = [];

    /// <summary>The room the VIP is going to or looking at, or the last one; 0 before the first.</summary>
    public int RoomId { get; private set; }

    /// <summary>The desk whose queue they are in or heading for, or 0.</summary>
    public int DeskId { get; private set; }

    /// <summary>Days left standing about (CorsixTH's <c>waiting</c>), or 0.</summary>
    private int Waiting { get; set; }

    /// <summary>Hours left looking in at a room.</summary>
    private int LookingLeft { get; set; }

    private TilePosition QueueTile { get; set; }

    private TilePosition QueueFace { get; set; }

    private TilePosition HomeTile { get; set; }

    /// <inheritdoc/>
    public bool IsStandingInQueue(Humanoid humanoid) => !humanoid.IsMoving && humanoid.Tile == QueueTile;

    /// <inheritdoc/>
    public void TakeQueuePlace(Simulation simulation, Humanoid humanoid, TilePosition tile, TilePosition face)
    {
        QueueFace = face;
        if (QueueTile == tile && (humanoid.Tile == tile || humanoid.Destination == tile))
        {
            return;
        }

        QueueTile = tile;
        if (humanoid.Tile != tile || humanoid.IsMoving)
        {
            humanoid.WalkTo(simulation, tile);
        }
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        switch (Phase)
        {
            case VipPhase.SeekReception:
            case VipPhase.WalkingToReception:
                SeekReception(simulation, humanoid);
                break;
            case VipPhase.InReceptionQueue:
                if (humanoid.Tile == QueueTile)
                {
                    humanoid.FaceTowards(QueueFace);
                }

                break;
            case VipPhase.WalkingToRoom:
                if (simulation.FindEntity(RoomId) is not Room { DoorId: var doorId } room || simulation.FindEntity(doorId) is not RoomDoor door)
                {
                    StandAbout();
                    break;
                }

                humanoid.FaceTowards(door.InsideTile(room.Area));
                Phase = VipPhase.LookingAtRoom;
                LookingLeft = LookingHours;
                break;
            case VipPhase.LookingAtRoom:
                if (simulation.FindEntity(RoomId) is not Room looked)
                {
                    StandAbout();
                    break;
                }

                if (--LookingLeft <= 0)
                {
                    EvaluateRoom(simulation, humanoid, looked);
                }

                break;
            case VipPhase.GoingHome:
                if (humanoid.Tile == HomeTile || !humanoid.WalkTo(simulation, HomeTile))
                {
                    humanoid.IsDespawning = true;
                    if (humanoid.HospitalPlayer >= 0 && humanoid.HospitalPlayer < simulation.Hospitals.Count)
                    {
                        VipVisits.OnVipLeft(simulation, simulation.Hospitals[humanoid.HospitalPlayer], this);
                    }
                }

                break;
        }
    }

    /// <summary><c>Vip:tickDay</c>: a day of standing about is over; on to the next room, or home if there is none.</summary>
    internal void OnEndDay(Simulation simulation, Humanoid humanoid)
    {
        if (Waiting > 0 && --Waiting == 0)
        {
            // CorsixTH sends the VIP home without rooms, then still looks for the next room, which finds none.
            if (!VipVisits.RoomsOf(simulation, humanoid.HospitalPlayer).Any())
            {
                GoHome(simulation, humanoid);
            }
            else
            {
                GetNextRoom(simulation, humanoid);
            }
        }

        if (Phase != VipPhase.GoingHome)
        {
            CountLitter(simulation, humanoid);
        }
    }

    /// <summary>
    /// <c>Vip:tickDay</c>'s <c>findObjectNear(self, "litter", 8)</c>: the litter within 8 steps that the VIP has not seen
    /// yet, counted as rubbish or as the worse kinds.
    /// </summary>
    private void CountLitter(Simulation simulation, Humanoid humanoid)
    {
        var litterAt = new Dictionary<TilePosition, Litter>();
        foreach (var entity in simulation.Entities)
        {
            // World:getObject gives the first litter object of a tile.
            if (entity is Litter { IsRemoved: false } litter)
            {
                litterAt.TryAdd(litter.Tile, litter);
            }
        }

        if (litterAt.Count == 0)
        {
            return;
        }

        ObjectVisitor.Visit(simulation.Map, humanoid.Tile, LitterSightDistance, tile =>
        {
            if (litterAt.TryGetValue(tile, out var litter) && !_foundLitter.Contains(litter.Id))
            {
                _foundLitter.Add(litter.Id);
                if (litter.IsRubbish)
                {
                    RubbishFound++;
                }
                else
                {
                    VomitFound++;
                }
            }

            return false;
        });
    }

    /// <summary><c>handleVIP</c>: reception has seen the VIP, who stands there for the rest of the day.</summary>
    internal void OnSeenAtReception()
    {
        Phase = VipPhase.Waiting;
        DeskId = 0;
        Waiting = 1;
    }

    /// <summary>
    /// <c>action_seek_reception_start</c>: to the best staffed desk, joining its queue once close enough. Without one
    /// the VIP waits where they are and looks again (CorsixTH has them meander meanwhile).
    /// </summary>
    private void SeekReception(Simulation simulation, Humanoid humanoid)
    {
        Phase = ReceptionDesks.Approach(simulation, humanoid, out var deskId) switch
        {
            DeskApproach.Joined => VipPhase.InReceptionQueue,
            DeskApproach.Walking => VipPhase.WalkingToReception,
            _ => VipPhase.SeekReception,
        };
        if (deskId != 0)
        {
            DeskId = deskId;
        }
    }

    /// <summary>The room went while the VIP was on the way or looking (the queue's <c>expect</c> callback).</summary>
    private void StandAbout()
    {
        Phase = VipPhase.Waiting;
        Waiting = 1;
    }

    /// <summary>
    /// <c>Vip:getNextRoom</c>: the next room in order. The first is always visited; after that each is skipped unless a
    /// roll comes up or the VIP must see it, and rooms not yet built are passed over.
    /// </summary>
    private void GetNextRoom(Simulation simulation, Humanoid humanoid)
    {
        var rooms = VipVisits.RoomsOf(simulation, humanoid.HospitalPlayer).ToList();
        var next = NextRoom(rooms, RoomId);
        if (next is not null && VisitedRooms > 0)
        {
            var roll = simulation.Random.NextInclusive(0, RoomVisitChance);
            while (roll != RoomVisitChance && !MustVisit.Contains(next.RoomType))
            {
                next = NextRoom(rooms, next.Id);
                if (next is null)
                {
                    break;
                }

                roll = simulation.Random.NextInclusive(0, RoomVisitChance);
            }
        }

        while (next is { IsActive: false })
        {
            next = NextRoom(rooms, next.Id);
        }

        GoToNextRoom(simulation, humanoid, next);
    }

    /// <summary><c>VipGoToNextRoomAction</c>: to the room's door, or home when there are no rooms left.</summary>
    private void GoToNextRoom(Simulation simulation, Humanoid humanoid, Room? room)
    {
        if (room is null || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            GoHome(simulation, humanoid);
            return;
        }

        RoomId = room.Id;
        var inside = door.InsideTile(room.Area);
        var outside = inside == door.Tile ? door.OtherSide : door.Tile;
        Phase = VipPhase.WalkingToRoom;
        if (humanoid.Tile != outside && !humanoid.WalkTo(simulation, outside))
        {
            // CorsixTH's walk would fail and the VIP stand about; here they stand about at once.
            StandAbout();
        }
    }

    /// <summary>
    /// <c>Vip:evaluateRoom</c>: a fire extinguisher and a bin count a point each, plants one point together, plus if
    /// most are healthy and minus if most are dying, and each machine a point, minus if it smokes; a research department
    /// with a patient in it (for an autopsy) worsens the rating by 6. Then on to the next room.
    /// </summary>
    private void EvaluateRoom(Simulation simulation, Humanoid humanoid, Room room)
    {
        VisitedRooms++;

        // A player about to kill a live patient for research is punished hard.
        if (string.Equals(room.RoomType, RoomVisits.ResearchRoom, StringComparison.Ordinal) && room.PatientId != 0)
        {
            Rating += 6;
        }

        var extinguisher = false;
        var bin = false;
        var plants = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not RoomObject placed || placed.RoomId != room.Id)
            {
                continue;
            }

            switch (placed.ObjectType)
            {
                case "extinguisher" when !extinguisher:
                    RoomEvaluation++;
                    extinguisher = true;
                    break;
                case "plant":
                    plants += placed.Plant is { IsDying: true } ? -1 : 1;
                    break;
                case "bin" when !bin:
                    RoomEvaluation++;
                    bin = true;
                    break;
                case var _ when placed.Machine is { } machine:
                    // A smoking machine counts against the room, any other for it.
                    RoomEvaluation += machine.IsBreaking ? -1 : 1;
                    break;
            }
        }

        RoomEvaluation += Math.Sign(plants);
        GetNextRoom(simulation, humanoid);
    }

    /// <summary><c>Vip:goHome</c>: the VIP makes up their mind and walks off the map.</summary>
    private void GoHome(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == VipPhase.GoingHome)
        {
            return;
        }

        if (simulation.FindEntity(DeskId) is RoomObject { Desk: { } desk })
        {
            desk.Queue.Remove(simulation, humanoid.Id);
        }

        DeskId = 0;
        SetRating(simulation, humanoid.HospitalPlayer);
        Phase = VipPhase.GoingHome;
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        HomeTile = points.Count > 0 ? points[simulation.Random.Next(points.Count)] : humanoid.Tile;
        if (!humanoid.WalkTo(simulation, HomeTile))
        {
            HomeTile = humanoid.Tile;
        }
    }

    /// <summary><c>Vip:setVIPRating</c>: the rating from litter, staff, patients, doctors and rooms, and what it brings.</summary>
    private void SetRating(Simulation simulation, int player)
    {
        if (player < 0 || player >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[player];
        var rooms = VipVisits.RoomsOf(simulation, player).ToList();
        var maxQueue = 0;
        foreach (var room in rooms)
        {
            maxQueue = Math.Max(maxQueue, room.DoorQueue?.Count ?? 0);
        }

        // 1. Litter: ten pieces or fewer seen count as clean.
        Rating += RubbishFound + VomitFound <= MaxCleanLitter ? -1 : 1;

        // 2. Staff tiredness.
        var staff = StaffOf(simulation, player);
        if (staff.Count > 1)
        {
            if (staff.Any(member => Tiredness.IsVeryTired(simulation, member.Fatigue)))
            {
                Rating += 2;
            }

            Rating += Average(simulation, player, staff, member => member.Fatigue, 1m, 1) >= 0.5m ? 1 : -1;
        }
        else
        {
            Rating += 4;
        }

        // 3. Patients, if any came during the visit.
        var patientsThisVisit = EnterPatients + hospital.Visitors - EnterVisitors;
        if (patientsThisVisit > 0)
        {
            var patients = PatientsOf(simulation, player);
            Rating += Average(simulation, player, patients, patient => ((PatientBehaviour)patient.Behaviour!).Health, 0.19m, 4) >= 0.2m ? -1 : 1;

            if (Average(simulation, player, patients, patient => patient.Warmth, null, 4) is { } warmth)
            {
                Rating += warmth <= 0.22m ? 2 : warmth <= 0.36m ? -1 : 2;
            }

            if (Average(simulation, player, patients, patient => patient.Happiness, null, 4) is { } happiness)
            {
                Rating += happiness <= 0.2m ? 3 : happiness <= 0.4m ? 2 : happiness <= 0.6m ? 1 : happiness <= 0.8m ? 0 : -1;
            }

            var deaths = hospital.Deaths - EnterDeaths;
            if (deaths != 0)
            {
                var ratio = patientsThisVisit / (decimal)deaths;
                Rating += ratio <= 2 ? 4 : ratio <= 4 ? 3 : ratio <= 8 ? 2 : ratio <= 12 ? 1 : 0;
            }

            var cures = hospital.Cured - EnterCures;
            if (cures != 0)
            {
                var ratio = patientsThisVisit / (decimal)cures;
                Rating += ratio <= 2 ? -1 : ratio <= 3 ? 0 : ratio <= 4 ? 1 : ratio <= 5 ? 2 : 3;
            }
            else
            {
                Rating += 3;
            }

            // Patients sitting on benches do not count as standing.
            var standing = Standing(simulation, patients);
            if (standing != 0)
            {
                Rating++;
            }

            Rating += maxQueue == 0 ? -1 : maxQueue <= 3 ? -1 : maxQueue <= 6 ? 0 : maxQueue <= 9 ? 1 : 2;
        }

        // 4. Doctors: CorsixTH's comment says 2 points either way, its code 1.
        var doctors = staff.Count(member => member.Profile!.Class == StaffClass.Doctor);
        if (doctors == 0)
        {
            Rating += 4;
        }
        else
        {
            var consultants = staff.Count(member => member.Profile is { Class: StaffClass.Doctor, IsConsultant: true });
            var juniors = staff.Count(member => member.Profile is { Class: StaffClass.Doctor, IsJunior: true });
            if (consultants / (decimal)doctors > 0.5m)
            {
                Rating--;
            }
            else if (juniors / (decimal)doctors > 0.5m)
            {
                Rating++;
            }
        }

        // 5. Rooms (none crash yet).
        if (rooms.Count < 1)
        {
            Rating += 4;
        }
        else
        {
            if (rooms.Count < 3)
            {
                Rating++;
            }

            if (VisitedRooms != 0)
            {
                var average = RoomEvaluation / (decimal)VisitedRooms;
                Rating += average <= 1.5m ? 3 : average <= 2 ? 1 : average <= 3 ? 0 : -1;
            }
        }

        Rating = Math.Clamp(Rating, 1, ReputationChanges.Length - 1);
        CashReward = Rating < Rewards.Length ? Rewards[Rating] : 0;
        ReputationReward = ReputationChanges[Rating];
        hospital.Vips.ThisYear++;
    }

    /// <summary>The room after <paramref name="afterId"/> in order (<c>next(world.rooms, ...)</c>), or null.</summary>
    private static Room? NextRoom(List<Room> rooms, int afterId)
    {
        foreach (var room in rooms)
        {
            if (room.Id > afterId)
            {
                return room;
            }
        }

        return null;
    }

    private static List<Humanoid> StaffOf(Simulation simulation, int player)
    {
        var staff = new List<Humanoid>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Profile: not null, IsFired: false } member && member.Employer == player)
            {
                staff.Add(member);
            }
        }

        return staff;
    }

    private static List<Humanoid> PatientsOf(Simulation simulation, int player)
    {
        var patients = new List<Humanoid>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: PatientBehaviour, IsDespawning: false } patient && patient.HospitalPlayer == player)
            {
                patients.Add(patient);
            }
        }

        return patients;
    }

    /// <summary>
    /// <c>_getAverageHumanoidsAttribute</c>: the average over those inside the hospital; with fewer than
    /// <paramref name="minCount"/> of them the default makes up the rest, or is the answer if it is null.
    /// </summary>
    private static decimal? Average(Simulation simulation, int player, List<Humanoid> humanoids, Func<Humanoid, decimal> attribute, decimal? fallback, int minCount)
    {
        var sum = 0m;
        var count = 0;
        foreach (var humanoid in humanoids)
        {
            if (IsInHospital(simulation, player, humanoid.Tile))
            {
                sum += attribute(humanoid);
                count++;
            }
        }

        if (count < minCount)
        {
            if (fallback is not { } value)
            {
                return null;
            }

            sum += value * (minCount - count);
            count = minCount;
        }

        return sum / count;
    }

    /// <summary>
    /// <c>Hospital:countSittingStanding</c>'s standing: patients idle outside rooms, which here means standing still in
    /// a queue.
    /// </summary>
    private static int Standing(Simulation simulation, List<Humanoid> patients)
    {
        var standing = 0;
        foreach (var patient in patients)
        {
            if (patient.Behaviour is PatientBehaviour { Phase: PatientPhase.InReceptionQueue or PatientPhase.InDoorQueue } behaviour
                && behaviour.IsStandingInQueue(patient) && simulation.Map[patient.Tile.X, patient.Tile.Y].RoomId == 0)
            {
                standing++;
            }
        }

        return standing;
    }

    private static bool IsInHospital(Simulation simulation, int player, TilePosition tile)
    {
        var map = simulation.Map;
        var cell = map[tile.X, tile.Y];
        return cell.Has(TileFlags.Hospital) && cell.Parcel < map.ParcelCount && map.GetParcelOwner(cell.Parcel) == player + 1;
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(Name);
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(Rating);
        writer.WriteInt32(CashReward);
        writer.WriteInt32(ReputationReward);
        writer.WriteInt32(EnterDeaths);
        writer.WriteInt32(EnterVisitors);
        writer.WriteInt32(EnterCures);
        writer.WriteInt32(EnterPatients);
        writer.WriteInt32(RoomVisitChance);
        writer.WriteInt32(VisitedRooms);
        writer.WriteInt32(RoomEvaluation);
        writer.WriteInt32(RoomId);
        writer.WriteInt32(DeskId);
        writer.WriteInt32(Waiting);
        writer.WriteInt32(LookingLeft);
        writer.WriteTile(QueueTile);
        writer.WriteTile(QueueFace);
        writer.WriteTile(HomeTile);
        writer.WriteInt32(RubbishFound);
        writer.WriteInt32(VomitFound);
        writer.WriteInt32(_foundLitter.Count);
        foreach (var id in _foundLitter)
        {
            writer.WriteInt32(id);
        }
    }

    internal static VipBehaviour ReadState(SimReader reader)
    {
        var name = reader.ReadInt32();
        var phase = reader.ReadByte();
        if (phase > (byte)VipPhase.GoingHome)
        {
            throw new InvalidDataException($"Unknown VIP phase {phase}.");
        }

        var vip = new VipBehaviour(name, 0)
        {
            Phase = (VipPhase)phase,
            Rating = reader.ReadInt32(),
            CashReward = reader.ReadInt32(),
            ReputationReward = reader.ReadInt32(),
            EnterDeaths = reader.ReadInt32(),
            EnterVisitors = reader.ReadInt32(),
            EnterCures = reader.ReadInt32(),
            EnterPatients = reader.ReadInt32(),
            RoomVisitChance = reader.ReadInt32(),
            VisitedRooms = reader.ReadInt32(),
            RoomEvaluation = reader.ReadInt32(),
            RoomId = reader.ReadInt32(),
            DeskId = reader.ReadInt32(),
            Waiting = reader.ReadInt32(),
            LookingLeft = reader.ReadInt32(),
            QueueTile = reader.ReadTile(),
            QueueFace = reader.ReadTile(),
            HomeTile = reader.ReadTile(),
            RubbishFound = reader.ReadInt32(),
            VomitFound = reader.ReadInt32(),
        };
        var found = reader.ReadCount(MaxFoundLitter);
        for (var i = 0; i < found; i++)
        {
            vip._foundLitter.Add(reader.ReadInt32());
        }

        return vip;
    }
}
