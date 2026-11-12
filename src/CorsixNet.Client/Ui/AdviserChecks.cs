// What the adviser says and when, ported from CorsixTH CorsixTH/Lua/hospitals/player_hospital.lua
// (dailyAdviceChecks, _adviseMoney, _adviseStaffRoom, _adviseToilets, _adviseHeatingForPatients,
// _adviseHeatingForStaff, _adviseDrinksMachines, advisePlants, checkReceptionAdvice, giveAdvice, msgCured, msgKilled,
// onEndDay's VIP announcement), staff/staff.lua (the once-only pay rise advice), hospital.lua (boiler) and
// disasters/earthquake.lua; room.lua (onHumanoidEnter's desk warnings) and the staff classes'
// adviseWrongPersonForThisRoom. Message keys as in CorsixTH/Lua/languages/original_strings.lua; the no_desk messages
// are CorsixTH's own (languages/english.lua).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Content;
using CorsixNet.Sim;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Ui;

/// <summary>
/// One of the adviser's messages: the original's string (CorsixTH's 1-based <c>S[section][index]</c>) when the game's
/// texts are loaded, otherwise an English stand-in; <c>%s</c> and <c>%d</c> take the arguments in turn.
/// </summary>
internal sealed record Advice(int Section, int Index, string Fallback)
{
    public string Text(OriginalTexts? texts, params object[] arguments)
    {
        var template = (Section > 0 ? texts?.String(Section, Index) : null) ?? Fallback;
        var next = 0;
        var result = new System.Text.StringBuilder(template.Length);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '%' && i + 1 < template.Length && template[i + 1] is 's' or 'd')
            {
                result.Append(next < arguments.Length ? Convert.ToString(arguments[next++], CultureInfo.InvariantCulture) : string.Empty);
                i++;
            }
            else
            {
                result.Append(template[i]);
            }
        }

        return result.ToString();
    }
}

/// <summary>
/// Watches the local player's hospital and gives the adviser something to say, as CorsixTH's player hospital does:
/// checks on set days of the month from the fifth month on, the reception at month ends of the first year, and
/// events such as the first cure, a VIP walking in or the boiler breaking. It only reads the game, so the random
/// picks between messages are the interface's own and never touch the simulation.
/// </summary>
internal sealed class AdviserChecks(Adviser adviser, Random random)
{
    // money
    public static readonly Advice MoneyLow = new(11, 2, "Money is running low.");
    public static readonly Advice MoneyVeryLowTakeLoan = new(11, 73, "Money is very low. Think about a loan.");
    public static readonly Advice CashLowConsiderLoan = new(28, 16, "Cash is low. A loan from the bank could help.");
    public static readonly Advice BankruptcyImminent = new(11, 3, "The hospital is about to go bankrupt!");
    public static readonly Advice PayBackLoan = new(28, 57, "You have money to spare: pay back some of the loan.");

    // staff room, toilets, radiators
    public static readonly Advice BuildStaffroom = new(28, 10, "Build a staff room so your staff can rest.");
    public static readonly Advice NeedStaffroom = new(11, 74, "Your staff need a staff room.");
    public static readonly Advice StaffOverworked = new(11, 17, "Your staff are overworked.");
    public static readonly Advice StaffTired = new(28, 9, "Your staff are getting tired.");
    public static readonly Advice PlaceRadiators = new(62, 14, "Place some radiators to keep the hospital warm.");
    public static readonly Advice NeedToilets = new(11, 20, "Patients need toilets.");
    public static readonly Advice BuildToilets = new(28, 11, "Build some toilets.");
    public static readonly Advice BuildToiletNow = new(28, 39, "Build a toilet now!");

    // heating
    public static readonly Advice IncreaseHeating = new(62, 13, "Patients are cold: turn the heating up.");
    public static readonly Advice PatientsVeryCold = new(28, 54, "Patients are very cold.");
    public static readonly Advice PeopleFreezing = new(28, 14, "People are freezing in the hospital.");
    public static readonly Advice DecreaseHeating = new(62, 15, "Patients are hot: turn the heating down.");
    public static readonly Advice PatientsTooHot = new(11, 18, "Patients are too hot.");
    public static readonly Advice PatientsGettingHot = new(28, 53, "Patients are getting hot.");
    public static readonly Advice StaffVeryCold = new(28, 55, "Your staff are very cold.");
    public static readonly Advice StaffTooHot = new(11, 19, "Your staff are too hot.");
    public static readonly Advice BoilerMaximum = new(21, 11, "The boiler has broken: the heating is stuck at full.");
    public static readonly Advice BoilerMinimum = new(21, 12, "The boiler has broken: the heating is off.");
    public static readonly Advice BoilerResolved = new(21, 13, "The boiler has been repaired.");

    // plants, drinks
    public static readonly Advice NeedHandymanPlants = new(11, 93, "Hire a handyman to water the plants.");

    // handymen (Patient:tickDay's litter, World:newObject, CallsDispatcher:callForRepair)
    public static readonly Advice NeedHandymanLitter = new(11, 70, "Patients have started to make a mess of your hospital: hire a handyman to clean up.");
    public static readonly Advice NeedHandymanMachines = new(11, 94, "Hire a handyman to keep your machines in order.");
    public static readonly Advice MachinesFallingApart = new(11, 4, "Your machines are falling apart: get your handymen to fix them!");
    public static readonly Advice MachineryDamaged2 = new(28, 34, "Your machinery is damaged: hire a handyman to repair it.");
    public static readonly Advice PatientsVeryThirsty = new(28, 6, "Patients are very thirsty.");
    public static readonly Advice PatientsThirsty = new(11, 16, "Patients are thirsty: place drinks machines.");
    public static readonly Advice PatientsThirsty2 = new(28, 5, "Patients are getting thirsty.");

    // cures and deaths
    public static readonly Advice FirstCure = new(11, 83, "Your first patient has been cured.");
    public static readonly Advice FirstDeath = new(11, 72, "Your first patient has died.");
    public static readonly Advice AnotherPatientCured = new(28, 37, "Another patient cured: that makes %d.");
    public static readonly Advice PatientsCured = new(11, 82, "%d patients cured so far.");
    public static readonly Advice ManyKilled = new(11, 77, "%d patients have died.");
    public static readonly Advice AnotherPatientKilled = new(28, 38, "Another patient has died: that makes %d.");

    // visitors and staff
    public static readonly Advice FirstVip = new(62, 9, "Your first VIP has arrived. Make a good impression!");
    public static readonly Advice VipArrived = new(21, 9, "%s has arrived.");
    public static readonly Advice PayRise = new(11, 29, "A member of staff wants a pay rise. Agree, or they will leave.");

    // earthquakes, research
    public static readonly Advice EarthquakeAlert = new(11, 107, "An earthquake is coming!");
    public static readonly Advice EarthquakeEnded = new(11, 78, "The earthquake is over. It measured %d on the Richter scale.");
    // research.lua's commandEnteringPatient: an autopsy came out
    public static readonly Advice AutopsyDiscovered = new(11, 123, "Your Automatic Autopsy Machine has been discovered. Expect an adverse reaction from the public.");

    // doctor.lua's updateSkill: promotions from training or on the job
    public static readonly Advice PromotionToDoctor = new(11, 23, "One of your junior doctors has become a doctor.");
    public static readonly Advice PromotionToSpecialist = new(11, 69, "One of your doctors has become a %s.");
    public static readonly Advice PromotionToConsultant = new(11, 138, "One of your doctors has become a consultant.");

    /// <summary>training.lua: a student placed in the training room finds no lecture chair (CorsixTH's own text).</summary>
    public static readonly Advice NotEnoughLectureChairs = new(0, 0, "Each student doctor needs a lecture chair to sit in!");

    // staff_place_advice: someone put into a room they cannot work in (Staff/Doctor/Nurse:adviseWrongPersonForThisRoom)
    public static readonly Advice OnlyResearchers = new(11, 124, "Only doctors with a research qualification can work in the research department.");
    public static readonly Advice OnlySurgeons = new(11, 125, "Only doctors with a surgeon qualification can work in the operating theatre.");
    public static readonly Advice OnlyPsychiatrists = new(11, 126, "Only doctors with a psychiatry qualification can work in psychiatry.");
    public static readonly Advice OnlyNursesInRoom = new(11, 127, "Only nurses can work in the %s.");
    public static readonly Advice DoctorsCannotWorkInRoom = new(11, 128, "Doctors cannot work in the %s.");
    public static readonly Advice NursesCannotWorkInRoom = new(11, 129, "Nurses cannot work in the %s.");
    public static readonly Advice OnlyDoctorsInRoom = new(11, 130, "Only doctors can work in the %s.");

    // room.lua's Room:onHumanoidEnter: a researcher or a ward nurse put into a room full of them (CorsixTH's own texts)
    public static readonly Advice[] ResearcherNeedsDesk =
    [
        new(0, 0, "A Researcher needs to have a desk to work at."),
        new(0, 0, "Your Researcher is pleased that you have allowed him to have a break. If you were intending to have more staff researching, then you need to provide them each with a desk to work from."),
        new(0, 0, "Each Researcher needs to have his own desk to work from."),
    ];

    public static readonly Advice[] NurseNeedsDesk =
    [
        new(0, 0, "Each Nurse needs to have her own desk to work from."),
        new(0, 0, "Your Nurse is pleased that you have allowed her to have a break. If you were intending to have more than one working in the ward, then you need to provide them each with a desk to work from."),
    ];

    // room_requirements, said when a room is finished (Room:roomFinished in rooms/*.lua)
    public static readonly Advice PsychiatryNeedPsychiatrist = new(11, 79, "You need to employ a psychiatrist for the psychiatric room.");
    public static readonly Advice PharmacyNeedNurse = new(11, 80, "You need to employ a nurse for the pharmacy.");
    public static readonly Advice TrainingRoomNeedConsultant = new(11, 81, "You need to employ a consultant to teach in the training room.");
    public static readonly Advice OpNeedTwoSurgeons = new(11, 85, "You need to employ two surgeons for the operating theatre.");
    public static readonly Advice OpNeedAnotherSurgeon = new(11, 86, "You need to employ another surgeon for the operating theatre.");
    public static readonly Advice OpNeedWard = new(11, 87, "You need to build a ward for patients to recover in after an operation.");
    public static readonly Advice WardNeedNurse = new(11, 88, "You need to employ a nurse for the ward.");
    public static readonly Advice GpsOfficeNeedDoctor = new(11, 105, "You need to employ a doctor for the GP's office.");

    public static readonly Advice NewMachineResearched = new(11, 24, "Research has produced a new %s.");

    // room requirements (ResearchRoom:roomFinished)
    public static readonly Advice ResearchRoomNeedResearcher = new(11, 84, "The research department needs a doctor who is a researcher: nobody else can work there.");

    // reception (CorsixTH's own texts)
    public static readonly Advice NoDesk = new(0, 0, "You should build a reception desk and hire a receptionist at some point!");
    public static readonly Advice NoDesk1 = new(0, 0, "If you want patients to come to your hospital, you will need to hire a receptionist and build a desk to work at!");
    public static readonly Advice NoDesk2 = new(0, 0, "Well done, that must be a world record: nearly a year and no patients! If you want to continue as Manager of this hospital, you will need to hire a receptionist and build a reception desk to work from!");
    public static readonly Advice NoDesk3 = new(0, 0, "That's just brilliant, nearly a year and you don't have a staffed reception! How do you expect to get any patients? Now get it sorted out and stop messing around!");
    public static readonly Advice NoDesk6 = new(0, 0, "You have a receptionist, so how about building a reception desk to work from?");
    public static readonly Advice NoDesk7 = new(0, 0, "You've built the reception desk, so how about hiring a receptionist? You won't see any patients until you get this sorted out you know!");

    private Snapshot? _last;
    private bool _temperatureAdvice;
    private bool _receptionAdvice;
    private bool _curedDiedMessage;
    private bool _payRiseAdvised;
    private int _vipsAnnounced;
    private readonly HashSet<int> _announcedVips = [];

    /// <summary>Looks at the game once a frame; advice is given when the day, the figures or the state have changed.</summary>
    public void Observe(Simulation simulation, int player, OriginalTexts? texts, long now)
    {
        var snapshot = Snapshot.Of(simulation, player);
        if (_last is not { } last || snapshot.Hours < last.Hours)
        {
            // A new game or a loaded one: start watching from here.
            _last = snapshot;
            return;
        }

        _last = snapshot;
        void Say(Advice advice, params object[] arguments) => adviser.Say(advice.Text(texts, arguments), now);

        bool Give(Advice[] messages, double fraction = 1)
        {
            var count = fraction is > 0 and < 1 ? (int)Math.Floor(messages.Length / fraction) : messages.Length;
            var index = count == 1 ? 0 : random.Next(count);
            if (index < messages.Length)
            {
                Say(messages[index]);
                return true;
            }

            return false;
        }

        var hospital = simulation.Hospitals[player];

        // msgCured / msgKilled: the first always, then now and then, once a month.
        if (snapshot.Cured > last.Cured)
        {
            if (snapshot.Cured == 1)
            {
                Say(FirstCure);
            }
            else if (!_curedDiedMessage)
            {
                var count = (int)Math.Floor(2 / (2.0 / 15));
                var index = random.Next(count);
                _curedDiedMessage = index < 2;
                if (_curedDiedMessage)
                {
                    Say(index == 0 ? AnotherPatientCured : PatientsCured, snapshot.Cured);
                }
            }
        }

        if (snapshot.AutopsiesDiscovered > last.AutopsiesDiscovered)
        {
            Say(AutopsyDiscovered);
        }

        HandymanAdvice(snapshot, last, Say);

        if (snapshot.Deaths > last.Deaths)
        {
            if (last.Deaths == 0)
            {
                Say(FirstDeath);
            }
            else if (!_curedDiedMessage)
            {
                var index = random.Next((int)Math.Floor(2 / 0.6));
                _curedDiedMessage = index < 2;
                if (_curedDiedMessage)
                {
                    Say(index == 0 ? ManyKilled : AnotherPatientKilled, snapshot.Deaths);
                }
            }
        }

        // Staff:requestRaise: the first time anyone asks, the adviser explains.
        if (!_payRiseAdvised && snapshot.RaiseRequests > last.RaiseRequests)
        {
            _payRiseAdvised = true;
            Say(PayRise);
        }

        // The boiler (Hospital:boilerBreakdown, _fixBoiler).
        if (snapshot.HeatingBroken && !last.HeatingBroken)
        {
            Say(hospital.RadiatorHeat >= Sim.Economy.Hospital.MaxRadiatorHeat ? BoilerMaximum : BoilerMinimum);
        }
        else if (!snapshot.HeatingBroken && last.HeatingBroken)
        {
            Say(BoilerResolved);
        }

        // Earthquakes: the warning and the end.
        if (snapshot.Earthquake && !last.Earthquake)
        {
            Say(EarthquakeAlert);
        }
        else if (!snapshot.Earthquake && last.Earthquake)
        {
            Say(EarthquakeEnded, last.EarthquakeSize);
        }

        // ResearchDepartment:discoverObject.
        foreach (var (id, research) in hospital.Research.Objects)
        {
            if (research.Discovered && !last.Discovered.Contains(id))
            {
                Say(NewMachineResearched, simulation.Content.FindObject(id)?.Name ?? id);
            }
        }

        // Doctor:updateSkill: a doctor who becomes a specialist, a doctor or a consultant.
        foreach (var (id, rank) in snapshot.Ranks)
        {
            if (!last.Ranks.TryGetValue(id, out var before))
            {
                continue;
            }

            foreach (var (gained, title, fallback) in new[]
            {
                (rank.Surgeon && !before.Surgeon, 6, "surgeon"),
                (rank.Psychiatrist && !before.Psychiatrist, 7, "psychiatrist"),
                (rank.Researcher && !before.Researcher, 9, "researcher"),
            })
            {
                if (gained)
                {
                    Say(PromotionToSpecialist, texts?.String(34, title) ?? fallback);
                }
            }

            if (before.Junior && !rank.Junior)
            {
                Say(PromotionToDoctor);
            }
            else if (!before.Consultant && rank.Consultant)
            {
                Say(PromotionToConsultant);
            }
        }

        // Room:roomFinished: a room just finished says which staff it still lacks.
        foreach (var roomId in snapshot.ActiveRooms)
        {
            if (!last.ActiveRooms.Contains(roomId) && simulation.FindEntity(roomId) is Room room)
            {
                foreach (var advice in RoomRequirements(simulation, player, room.RoomType))
                {
                    Say(advice);
                }
            }
        }

        // PlayerHospital:onEndDay: a VIP is announced once inside the hospital.
        foreach (var vip in simulation.Entities.OfType<Humanoid>())
        {
            if (vip.Behaviour is VipBehaviour behaviour && !_announcedVips.Contains(vip.Id) && IsInHospital(simulation.Map, player, vip.Tile))
            {
                _announcedVips.Add(vip.Id);
                if (_vipsAnnounced++ == 0 && hospital.Vips.Count == 0)
                {
                    Say(FirstVip);
                }
                else
                {
                    Say(VipArrived, texts?.VipName(behaviour.Name) ?? string.Create(CultureInfo.InvariantCulture, $"VIP {behaviour.Name}"));
                }
            }
        }

        var date = simulation.Date;
        var lastDate = GameDate.FromHours(last.Hours);
        if ((date.Year, date.Month, date.Day) == (lastDate.Year, lastDate.Month, lastDate.Day))
        {
            return;
        }

        if (date.Month != lastDate.Month || date.Year != lastDate.Year)
        {
            // PlayerHospital:onEndMonth.
            _curedDiedMessage = false;
            if (!snapshot.HasStaffedDesk)
            {
                CheckReception(simulation, player, lastDate, Say);
            }
        }

        if (!hospital.IsOpen || (date.Year == 1 && date.Month < 5))
        {
            return;
        }

        switch (date.Day)
        {
            case 1:
                AdviseMoney(hospital, date, Give);
                break;
            case 3 when HasRoomType(simulation, "staff_room") && !HasRoom(simulation, player, "staff_room"):
                Give([BuildStaffroom, NeedStaffroom, StaffOverworked, StaffTired]);
                break;
            case 5 when Count(simulation, player, "radiator") == 0:
                Say(PlaceRadiators);
                break;
            case 8 when HasRoomType(simulation, "toilets") && !HasRoom(simulation, player, "toilets"):
                Give([NeedToilets, BuildToilets, BuildToiletNow]);
                break;
            case 15 when !_temperatureAdvice && !hospital.IsHeatingBroken:
                var patientWarmth = Average(Patients(simulation, player), h => h.Warmth, 0.3m);
                if (patientWarmth < 0.22m)
                {
                    Give([IncreaseHeating, PatientsVeryCold, PeopleFreezing]);
                    _temperatureAdvice = true;
                }
                else if (patientWarmth >= 0.36m)
                {
                    Give([DecreaseHeating, PatientsTooHot, PatientsGettingHot]);
                    _temperatureAdvice = true;
                }

                break;
            case 18 when !Staff(simulation, player).Any(s => s.Profile!.Class == StaffClass.Handyman) && Count(simulation, player, "plant") > 0:
                Say(NeedHandymanPlants);
                break;
            case 20 when !_temperatureAdvice && !hospital.IsHeatingBroken:
                var staffWarmth = Average(Staff(simulation, player), h => h.Warmth, 0.3m);
                if (staffWarmth < 0.22m)
                {
                    Say(StaffVeryCold);
                    _temperatureAdvice = true;
                }
                else if (staffWarmth >= 0.36m)
                {
                    Say(StaffTooHot);
                    _temperatureAdvice = true;
                }

                break;
            case 24:
                var thirst = Average(Patients(simulation, player), h => ((PatientBehaviour)h.Behaviour!).Thirst, 0m);
                if (thirst > (date.Year == 1 ? 0.9m : 0.8m))
                {
                    Say(PatientsVeryThirsty);
                }
                else if (thirst > 0.6m)
                {
                    Give([PatientsThirsty, PatientsThirsty2]);
                }

                break;
            case 28:
                _temperatureAdvice = false;
                break;
        }
    }

    /// <summary><c>_adviseMoney</c>.</summary>
    private static void AdviseMoney(Sim.Economy.Hospital hospital, GameDate date, Func<Advice[], double, bool> give)
    {
        if (hospital.Balance is < 2000 and >= -500)
        {
            give([MoneyLow, MoneyVeryLowTakeLoan, CashLowConsiderLoan], 1);
        }
        else if (hospital.Balance < -2000 && date.Month > 8)
        {
            give([BankruptcyImminent], 1);
        }
        else if (hospital.Balance > 6000 && hospital.Loan > 0)
        {
            give([PayBackLoan], 1);
        }
    }

    /// <summary><c>checkReceptionAdvice</c>, at the end of <paramref name="month"/>, in the first year only.</summary>
    private void CheckReception(Simulation simulation, int player, GameDate month, Action<Advice, object[]> say)
    {
        if (month.Year > 1)
        {
            return;
        }

        var receptionists = Staff(simulation, player).Count(s => s.Profile!.Class == StaffClass.Receptionist);
        var desks = Count(simulation, player, ReceptionDesks.ObjectId);
        if (receptionists != 0 && month.Month > 2 && desks == 0 && !_receptionAdvice)
        {
            say(NoDesk6, []);
            _receptionAdvice = true;
        }
        else if (receptionists == 0 && month.Month > 2 && desks != 0)
        {
            say(NoDesk7, []);
        }
        else if (month.Month == 3)
        {
            say(NoDesk, []);
        }
        else if (month.Month == 8)
        {
            say(NoDesk1, []);
        }
        else if (month.Month == 11)
        {
            say(simulation.Hospitals[player].Visitors == 0 ? NoDesk2 : NoDesk3, []);
        }
    }

    private static bool HasRoomType(Simulation simulation, string roomType) => simulation.Content.FindRoom(roomType) is not null;

    /// <summary>
    /// What the rooms' <c>roomFinished</c> say: gp.lua, pharmacy.lua, ward.lua, psych.lua, training.lua, research.lua and
    /// operating_theatre.lua (a ward first, then two surgeons). CorsixTH's other rooms, the DNA fixer's included, say
    /// nothing.
    /// </summary>
    private static IEnumerable<Advice> RoomRequirements(Simulation simulation, int player, string roomType)
    {
        var staff = Staff(simulation, player).Select(h => h.Profile!).ToList();
        switch (roomType)
        {
            case "gp" when !staff.Any(p => p.Class == StaffClass.Doctor):
                yield return GpsOfficeNeedDoctor;
                break;
            case "pharmacy" when !staff.Any(p => p.Class == StaffClass.Nurse):
                yield return PharmacyNeedNurse;
                break;
            case "ward" when !staff.Any(p => p.Class == StaffClass.Nurse):
                yield return WardNeedNurse;
                break;
            case "psych" when !staff.Any(p => p.Class == StaffClass.Doctor && p.IsPsychiatrist):
                yield return PsychiatryNeedPsychiatrist;
                break;
            case "training" when !staff.Any(p => p.Class == StaffClass.Doctor && p.IsConsultant):
                yield return TrainingRoomNeedConsultant;
                break;
            case "research" when !staff.Any(p => p.Class == StaffClass.Doctor && p.IsResearcher):
                yield return ResearchRoomNeedResearcher;
                break;
            case "operating_theatre":
                if (!simulation.Entities.OfType<Room>().Any(r => r.RoomType == "ward" && r.IsActive && RoomBuilder.IsOwnedBy(simulation.Map, r, player)))
                {
                    yield return OpNeedWard;
                }

                var surgeons = staff.Count(p => p.Class == StaffClass.Doctor && p.IsSurgeon);
                if (surgeons < 2)
                {
                    yield return surgeons == 0 ? OpNeedTwoSurgeons : OpNeedAnotherSurgeon;
                }

                break;
        }
    }

    private static bool HasRoom(Simulation simulation, int player, string roomType) =>
        simulation.Entities.OfType<Room>().Any(room => room.RoomType == roomType && RoomBuilder.IsOwnedBy(simulation.Map, room, player));

    private static int Count(Simulation simulation, int player, string objectType) =>
        simulation.Entities.OfType<RoomObject>().Count(o => o.ObjectType == objectType && IsInHospital(simulation.Map, player, o.Tile));

    private static IEnumerable<Humanoid> Staff(Simulation simulation, int player) =>
        simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is not null && h.Employer == player && !h.IsFired);

    private static IEnumerable<Humanoid> Patients(Simulation simulation, int player) =>
        simulation.Entities.OfType<Humanoid>().Where(h => h.Behaviour is PatientBehaviour && h.HospitalPlayer == player && IsInHospital(simulation.Map, player, h.Tile));

    /// <summary><c>getAveragePatientAttribute</c> / <c>getAverageStaffAttribute</c>: the default when there is nobody.</summary>
    private static decimal Average(IEnumerable<Humanoid> people, Func<Humanoid, decimal> value, decimal empty)
    {
        var values = people.Select(value).ToList();
        return values.Count == 0 ? empty : values.Average();
    }

    private static bool IsInHospital(LevelMap map, int player, TilePosition tile) =>
        map.Contains(tile.X, tile.Y) && map[tile.X, tile.Y].Has(TileFlags.Hospital) && map.GetParcelOwner(map[tile.X, tile.Y].Parcel) == player + 1;

    /// <summary>A doctor's grade and specialisms, to notice promotions.</summary>
    private readonly record struct Rank(bool Junior, bool Consultant, bool Surgeon, bool Psychiatrist, bool Researcher);

    /// <summary>
    /// training.lua's <c>commandEnteringStaff</c>: a student doctor put into a training room whose lecture chairs are all
    /// taken is told so (and leaves).
    /// </summary>
    public static bool LacksLectureChair(Simulation simulation, StaffProfile profile, TilePosition tile, int movingId)
    {
        if (profile.Class != StaffClass.Doctor || profile.IsConsultant || !simulation.Map.Contains(tile.X, tile.Y)
            || simulation.FindEntity(simulation.Map[tile.X, tile.Y].RoomId) is not Room { RoomType: "training", IsActive: true } room)
        {
            return false;
        }

        var students = simulation.Entities.Count(e => e is Humanoid { Behaviour: DoctorBehaviour doctor, IsFired: false } h
            && doctor.RoomId == room.Id && h.Id != room.StaffId && h.Id != movingId);
        var chairs = simulation.Entities.Count(e => e is RoomObject { ObjectType: "lecture_chair" } chair && chair.RoomId == room.Id);
        return students >= chairs;
    }

    /// <summary>
    /// What the adviser says when a member of staff is put down in a room (room.lua's <c>Room:onHumanoidEnter</c>): a
    /// researcher or ward nurse in a room already full of them is told to get a desk of their own (and takes over from
    /// one who is free), and someone the room cannot use is told who can work there
    /// (<c>adviseWrongPersonForThisRoom</c> in staff.lua, doctor.lua and nurse.lua). Null when there is nothing to say.
    /// </summary>
    /// <param name="simulation">The simulation.</param>
    /// <param name="profile">Who is put down.</param>
    /// <param name="tile">Where.</param>
    /// <param name="movingId">The member of staff being moved, or 0 for one being hired.</param>
    /// <param name="pick">Picks one of a number of messages: given a count, returns an index below it.</param>
    public static (Advice Advice, string? Argument)? StaffPlacement(Simulation simulation, StaffProfile profile, TilePosition tile, int movingId, Func<int, int> pick)
    {
        if (profile.Class is not (StaffClass.Doctor or StaffClass.Nurse) || !simulation.Map.Contains(tile.X, tile.Y)
            || simulation.FindEntity(simulation.Map[tile.X, tile.Y].RoomId) is not Room { IsActive: true } room)
        {
            return null;
        }

        var name = simulation.Content.FindRoom(room.RoomType)?.Name ?? room.RoomType;
        var doctor = profile.Class == StaffClass.Doctor;
        switch (room.RoomType)
        {
            case "staff_room":
                return null;
            case "training":
                // A doctor is a student or the lecturer, or told about the lecture chairs.
                return doctor ? null : (NursesCannotWorkInRoom, name);
            case "research" when doctor && profile.IsResearcher:
                {
                    var researchers = simulation.Entities.Count(e => e is Humanoid { Behaviour: DoctorBehaviour d, IsFired: false } h && d.RoomId == room.Id && h.Id != movingId);
                    return room.StaffId != 0 && researchers >= CountIn(simulation, room, "desk") ? (Pick(ResearcherNeedsDesk), null) : null;
                }

            case "ward" when !doctor:
                {
                    var nurses = simulation.Entities.Count(e => e is Humanoid { Behaviour: NurseBehaviour n, IsFired: false } h && n.RoomId == room.Id && h.Id != movingId);
                    return room.StaffId != 0 && room.StaffId != movingId && nurses >= Math.Max(1, CountIn(simulation, room, "desk")) ? (Pick(NurseNeedsDesk), null) : null;
                }
        }

        var required = RoomVisits.RequiredStaff(room.RoomType);
        if (required is null)
        {
            // Rooms without staff: only the toilets say so to a doctor.
            return doctor ? (room.RoomType == "toilets" ? (DoctorsCannotWorkInRoom, name) : null) : (NursesCannotWorkInRoom, name);
        }

        if (!doctor)
        {
            return required == BehaviourKind.Nurse ? null : (NursesCannotWorkInRoom, name);
        }

        if (required == BehaviourKind.Doctor && RoomVisits.IsQualifiedFor(room.RoomType, profile))
        {
            return null;
        }

        return required == BehaviourKind.Nurse ? (OnlyNursesInRoom, name)
            : room.RoomType switch
            {
                "operating_theatre" => (OnlySurgeons, null),
                "research" or "dna_fixer" => (OnlyResearchers, null),
                "psych" => (OnlyPsychiatrists, null),
                _ => (OnlyDoctorsInRoom, name),
            };

        Advice Pick(Advice[] messages) => messages[pick(messages.Length)];
    }

    /// <summary>
    /// The handyman advice: the first litter in a hospital without handymen (<c>need_handyman_litter</c>), a machine placed
    /// without one (<c>World:newObject</c>, <c>need_handyman_machines</c>), and a machine's call for repair
    /// (<c>CallsDispatcher:callForRepair</c>): urgent, straight below four uses left, says machines are falling apart;
    /// otherwise, below six uses left or called from the machine window, it asks for a handyman if there is none.
    /// </summary>
    private static void HandymanAdvice(Snapshot snapshot, Snapshot last, Action<Advice, object[]> say)
    {
        if (snapshot.Littered && !last.Littered && !snapshot.HasHandyman)
        {
            say(NeedHandymanLitter, []);
        }

        var placed = false;
        var urgent = false;
        var damaged = false;
        foreach (var (id, machine) in snapshot.Machines)
        {
            if (!last.Machines.TryGetValue(id, out var before))
            {
                placed = true;
                continue;
            }

            if (machine.RemainingUses < 4 && before.RemainingUses >= 6)
            {
                urgent = true;
            }
            else if ((machine.RemainingUses < 6 && before.RemainingUses >= 6) || (machine.IsRepairCalled && !before.IsRepairCalled))
            {
                damaged = true;
            }
        }

        if (placed && !snapshot.HasHandyman)
        {
            say(NeedHandymanMachines, []);
        }

        if (urgent)
        {
            say(MachinesFallingApart, []);
        }
        else if (damaged && !snapshot.HasHandyman)
        {
            say(MachineryDamaged2, []);
        }
    }

    private static int CountIn(Simulation simulation, Room room, string objectType) =>
        simulation.Entities.Count(e => e is RoomObject placed && placed.RoomId == room.Id && string.Equals(placed.ObjectType, objectType, StringComparison.Ordinal));

    /// <summary>What the checks compare from one frame to the next.</summary>
    private readonly record struct Snapshot(
        long Hours,
        int Cured,
        int Deaths,
        int AutopsiesDiscovered,
        int RaiseRequests,
        bool HeatingBroken,
        bool Earthquake,
        int EarthquakeSize,
        bool HasStaffedDesk,
        IReadOnlySet<string> Discovered,
        IReadOnlyDictionary<int, Rank> Ranks,
        IReadOnlySet<int> ActiveRooms,
        bool Littered,
        bool HasHandyman,
        IReadOnlyDictionary<int, (int RemainingUses, bool IsRepairCalled)> Machines)
    {
        public static Snapshot Of(Simulation simulation, int player)
        {
            var hospital = simulation.Hospitals[player];
            var staff = Staff(simulation, player).ToList();
            return new Snapshot(
                simulation.Hours,
                hospital.Cured,
                hospital.Deaths,
                hospital.AutopsiesDiscovered,
                staff.Count(s => s.RaiseRequest > 0),
                hospital.IsHeatingBroken,
                simulation.Earthquakes.IsActive,
                simulation.Earthquakes.Size,
                staff.Any(s => s.Profile!.Class == StaffClass.Receptionist) && Count(simulation, player, ReceptionDesks.ObjectId) > 0,
                hospital.Research.Objects.Where(pair => pair.Value.Discovered).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal),
                staff.Where(s => s.Profile!.Class == StaffClass.Doctor).ToDictionary(s => s.Id, s => new Rank(s.Profile!.IsJunior, s.Profile.IsConsultant, s.Profile.IsSurgeon, s.Profile.IsPsychiatrist, s.Profile.IsResearcher)),
                simulation.Entities.OfType<Room>().Where(room => room.IsActive && RoomBuilder.IsOwnedBy(simulation.Map, room, player)).Select(room => room.Id).ToHashSet(),
                hospital.Littered,
                staff.Any(s => s.Profile!.Class == StaffClass.Handyman),
                simulation.Entities.OfType<RoomObject>()
                    .Where(o => o.Machine is not null && !o.IsDestroyed && IsInHospital(simulation.Map, player, o.Tile))
                    .ToDictionary(o => o.Id, o => (o.Machine!.RemainingUses, o.Machine.IsRepairCalled)));
        }
    }
}
