// What the adviser says and when, ported from CorsixTH CorsixTH/Lua/hospitals/player_hospital.lua
// (dailyAdviceChecks, _adviseMoney, _adviseStaffRoom, _adviseToilets, _adviseHeatingForPatients,
// _adviseHeatingForStaff, _adviseDrinksMachines, advisePlants, checkReceptionAdvice, giveAdvice, msgCured, msgKilled,
// onEndDay's VIP announcement), staff/staff.lua (the once-only pay rise advice), hospital.lua (boiler) and
// disasters/earthquake.lua. Message keys as in CorsixTH/Lua/languages/original_strings.lua; the no_desk messages
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

    /// <summary>What the checks compare from one frame to the next.</summary>
    private readonly record struct Snapshot(
        long Hours,
        int Cured,
        int Deaths,
        int RaiseRequests,
        bool HeatingBroken,
        bool Earthquake,
        int EarthquakeSize,
        bool HasStaffedDesk,
        IReadOnlySet<string> Discovered)
    {
        public static Snapshot Of(Simulation simulation, int player)
        {
            var hospital = simulation.Hospitals[player];
            var staff = Staff(simulation, player).ToList();
            return new Snapshot(
                simulation.Hours,
                hospital.Cured,
                hospital.Deaths,
                staff.Count(s => s.RaiseRequest > 0),
                hospital.IsHeatingBroken,
                simulation.Earthquakes.IsActive,
                simulation.Earthquakes.Size,
                staff.Any(s => s.Profile!.Class == StaffClass.Receptionist) && Count(simulation, player, ReceptionDesks.ObjectId) > 0,
                hospital.Research.Objects.Where(pair => pair.Value.Discovered).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal));
        }
    }
}
