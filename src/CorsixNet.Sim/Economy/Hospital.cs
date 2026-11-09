// Spending, receiving, the transaction log and monthly wages ported from CorsixTH CorsixTH/Lua/hospital.lua
// (Hospital:spendMoney, Hospital:receiveMoney, Hospital:logTransaction, Hospital:onEndMonth); the casebook,
// treatment prices and insurance from the same file (Hospital:Hospital, Hospital:receiveMoneyForTreatment,
// Hospital:getTreatmentPrice, Hospital:addInsuranceMoney, Hospital:onEndYear); reputation from the same file
// (reputation_changes, Hospital:changeReputation, unconditionalChangeReputation, isReputationChangeAllowed,
// getReputationChangeLikelihood, computePriceLevelImpact, updateNotCuredCounts) and entities/humanoids/patient.lua
// (Patient:getPriceDistortion); the hospital's value and the year's counts from the same file (Hospital:Hospital,
// spendMoney, receiveMoney, onEndYear) and the year end's trophies and awards from
// dialogs/fullscreen/annual_report.lua (UIAnnualReport:checkTrophiesAndAwards, updateAwards); the rooms discovered
// from the same file (Hospital:Hospital's room_discoveries, Hospital:isRoomDiscovered); heating from the same file
// (Hospital:onEndDay, onEndMonth, daysUntilNextDisaster, boilerBreakdown, _fixBoiler) and
// dialogs/fullscreen/town_map.lua (UITownMap:increaseHeat, decreaseHeat); the monthly statistics for the graphs from
// hospital.lua (Hospital:Hospital, onEndMonth, spendMoney, receiveMoney).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>What a transaction was for, as in CorsixTH's <c>_S.transactions</c>.</summary>
public enum TransactionKind : byte
{
    BuyObject = 0,
    SellObject = 1,
    BuildRoom = 2,
    HireStaff = 3,
    Wages = 4,

    /// <summary>A patient paid for a diagnosis or a cure; the subject is the (pseudo) disease.</summary>
    Treatment = 5,

    /// <summary>An insurance company paid what it owed from two months ago; the subject is its number (1 to 3).</summary>
    Insurance = 6,

    /// <summary>
    /// The drug for a cure in the pharmacy was bought; the subject is the drug company's number (1 to 5), as CorsixTH
    /// names one of five companies at random.
    /// </summary>
    DrugCost = 7,

    /// <summary>Money for the year's trophies (CorsixTH's <c>eoy_trophy_bonus</c>).</summary>
    TrophyBonus = 8,

    /// <summary>The year's award money less its fines, which may be negative (CorsixTH's <c>eoy_bonus_penalty</c>).</summary>
    AwardBonus = 9,

    /// <summary>Money borrowed from the bank (<c>bank_loan</c>).</summary>
    Loan = 10,

    /// <summary>Money paid back to the bank (<c>loan_repayment</c>).</summary>
    LoanRepayment = 11,

    /// <summary>The month's interest on the loan (<c>loan_interest</c>).</summary>
    LoanInterest = 12,

    /// <summary>The month's interest on an overdrawn balance (<c>overdraft</c>).</summary>
    Overdraft = 13,

    /// <summary>A parcel of land was bought; the subject is its number (<c>buy_land</c>).</summary>
    BuyLand = 14,

    /// <summary>The month's heating bill (<c>heating</c>).</summary>
    Heating = 15,

    /// <summary>Money from the money cheat (<c>cheat</c>).</summary>
    Cheat = 16,

    /// <summary>A month's wage for a member of staff who was fired (<c>severance</c>); the subject is their class.</summary>
    Severance = 17,

    /// <summary>A pleased VIP's grant (CorsixTH's <c>vip_award</c>).</summary>
    VipAward = 18,
}

/// <summary>
/// The hospital's figures at the end of a month, for the graphs (an entry of CorsixTH's <c>statistics</c>).
/// </summary>
/// <param name="MoneyIn">Money taken in during the month.</param>
/// <param name="MoneyOut">Money spent during the month.</param>
/// <param name="Wages">The month's wages.</param>
/// <param name="Balance">The balance at the month's end.</param>
/// <param name="Visitors">Patients so far.</param>
/// <param name="Cures">Patients cured so far.</param>
/// <param name="Deaths">Patients who died so far.</param>
/// <param name="Reputation">The reputation at the month's end.</param>
public readonly record struct MonthStatistics(long MoneyIn, long MoneyOut, int Wages, long Balance, int Visitors, int Cures, int Deaths, int Reputation);

/// <summary>A trophy, award or fine of the year end (CorsixTH's annual report).</summary>
public enum YearEndAwardKind : byte
{
    /// <summary>Trophy: the reputation stayed above <c>awards_trophies.Reputation</c> all year.</summary>
    ConsistentReputation,

    /// <summary>Trophy: nobody died or went home uncured.</summary>
    AllCuredTrophy,

    /// <summary>Trophy: nobody died.</summary>
    NoDeathsTrophy,

    /// <summary>Trophy: more cured than nine tenths of those not cured.</summary>
    ManyCuredTrophy,

    /// <summary>Award: a reputation above <c>ReputationAward</c>.</summary>
    HighReputation,

    /// <summary>Fine: a reputation below <c>ReputationPoor</c>.</summary>
    PoorReputation,

    /// <summary>Award (in reputation): a hospital worth more than <c>HospValueAward</c>.</summary>
    HighValue,

    /// <summary>Penalty (in reputation): a hospital worth less than <c>HospValuePoor</c>.</summary>
    LowValue,

    /// <summary>Award: more cures than <c>CuresAward</c>.</summary>
    ManyCures,

    /// <summary>Fine: fewer cures than <c>CuresPoor</c>.</summary>
    FewCures,

    /// <summary>Award: nobody died or went home uncured.</summary>
    AllCures,

    /// <summary>Award: fewer deaths than <c>DeathsAward</c>.</summary>
    FewDeaths,

    /// <summary>Fine: more deaths than <c>DeathsPoor</c>.</summary>
    ManyDeaths,

    /// <summary>Award: more than <c>CuresVDeathsAward</c> cures per death.</summary>
    CuresOverDeaths,

    /// <summary>Fine: at most <c>CuresVDeathsPoor</c> cures per death.</summary>
    DeathsOverCures,

    /// <summary>Trophy (in reputation): every VIP of the year left pleased.</summary>
    HappyVips,
}

/// <summary>A trophy, award or fine the hospital got at a year end, and what it brought.</summary>
/// <param name="Kind">Which.</param>
/// <param name="Money">Money it brought; negative for a fine.</param>
/// <param name="Reputation">Reputation it brought; negative for a penalty.</param>
public readonly record struct YearEndAward(YearEndAwardKind Kind, int Money, int Reputation);

/// <summary>Why a hospital's reputation changes, as the keys of CorsixTH's <c>reputation_changes</c>.</summary>
public enum ReputationChange : byte
{
    /// <summary>A patient was cured (+1).</summary>
    Cured,

    /// <summary>A patient died (−4).</summary>
    Death,

    /// <summary>A patient was sent home or gave up waiting (−3).</summary>
    Kicked,

    /// <summary>A patient found the price too high (−2).</summary>
    OverPriced,

    /// <summary>A patient found the price low (+1).</summary>
    UnderPriced,
}

/// <summary>One entry of a hospital's transaction log.</summary>
/// <param name="Tick">When it happened.</param>
/// <param name="Kind">What it was for.</param>
/// <param name="Subject">Id of the room or object type it was for, the staff class of a hire, or empty.</param>
/// <param name="Amount">Money received (positive) or spent (negative).</param>
/// <param name="Balance">The balance afterwards.</param>
public readonly record struct Transaction(long Tick, TransactionKind Kind, string Subject, int Amount, long Balance);

/// <summary>
/// The hospital's casebook entry for a disease or a diagnosis (CorsixTH's <c>disease_casebook</c>): the
/// price the player set, whether the disease is known, and what it earned.
/// </summary>
public sealed class DiseaseCase
{
    /// <summary>The standard price in percent; the player may set 50 to 200.</summary>
    public const int StandardPrice = 100;

    /// <summary>The lowest price the player may set, in percent.</summary>
    public const int MinPrice = 50;

    /// <summary>The highest price the player may set, in percent.</summary>
    public const int MaxPrice = 200;

    /// <summary>Cure effectiveness of a disease cured without a drug.</summary>
    public const int FullEffectiveness = 100;

    internal DiseaseCase(string diseaseId, bool discovered)
    {
        DiseaseId = diseaseId;
        Discovered = discovered;
    }

    /// <summary>How likely a cure works, in percent (CorsixTH's <c>cure_effectiveness</c>): the drug's rating, otherwise 100.</summary>
    public int CureEffectiveness { get; internal set; } = FullEffectiveness;

    /// <summary>What the drug of each cure costs, or 0 for a disease not cured with a drug (CorsixTH's <c>drug_cost</c>).</summary>
    public int DrugCost { get; internal set; }

    /// <summary>Patients cured of it so far.</summary>
    public int Recoveries { get; internal set; }

    /// <summary>Patients who died of it so far.</summary>
    public int Fatalities { get; internal set; }

    public string DiseaseId { get; }

    /// <summary>The price as a percentage of the standard price (CorsixTH's <c>price</c>, a multiplier).</summary>
    public int PricePercent { get; internal set; } = StandardPrice;

    /// <summary>Whether the hospital knows the disease, so that it can be guessed and shows in the casebook.</summary>
    public bool Discovered { get; internal set; }

    /// <summary>Money received for it so far, insurance payments included.</summary>
    public long MoneyEarned { get; internal set; }

    /// <summary>
    /// The hospital's reputation for this disease, which starts at 500 and moves with the hospital's (CorsixTH's
    /// casebook <c>reputation</c>); null for a diagnosis, which goes by the hospital's reputation.
    /// </summary>
    public int? Reputation { get; internal set; }

    /// <summary>Patients with it who were sent home or gave up (CorsixTH's <c>turned_away</c>).</summary>
    public int TurnedAway { get; internal set; }
}

/// <summary>A player's hospital: its money, its casebook and what insurance companies owe it.</summary>
public sealed class Hospital
{
    /// <summary>How many transactions the log keeps; CorsixTH keeps about 20.</summary>
    public const int LogLength = 20;

    /// <summary>Insurance companies a hospital deals with.</summary>
    public const int InsuranceCompanies = 3;

    /// <summary>Months an insurance company keeps what it owes: payments of one month are paid two months later.</summary>
    private const int InsuranceMonths = 3;

    /// <summary>Yearly inflation of prices (CorsixTH's <c>inflation_rate</c>).</summary>
    private const decimal InflationRate = 0.045m;

    private const int MaxCases = 256;

    /// <summary>Months of statistics a save may hold: over 5,000 years.</summary>
    private const int MaxStatisticsMonths = 1 << 16;

    /// <summary>
    /// Hours per segment of the opening watch: CorsixTH's <c>UIWatch</c> runs 100 days over 13 segments
    /// (<c>TICK_DAYS</c>, <c>TIMER_SEGMENTS</c>), rounded down.
    /// </summary>
    public const int OpeningTickRate = 100 * 50 / 13;

    /// <summary>Segments of the opening watch (CorsixTH's <c>open_timer</c> starts at 12).</summary>
    public const int OpeningSegments = 12;

    /// <summary>The reputation a disease starts with in the casebook, and above which prices rise with it.</summary>
    public const int AverageReputation = 500;

    /// <summary>The bank's yearly interest in hundredths of a percent (the level files' <c>InterestRate</c>, base_config.lua's 100).</summary>
    public const int DefaultInterestRate = 100;

    /// <summary>What overdrafts cost on top of the interest, likewise (<c>OverdraftDiff</c>, base_config.lua's 200).</summary>
    public const int DefaultOverdraftDifferential = 200;

    /// <summary>The steps in which money is borrowed and paid back.</summary>
    public const int LoanStep = 5000;

    /// <summary>What a hospital is worth at the start besides its land (CorsixTH's <c>+ 20000</c>).</summary>
    public const int StartValueBesidesLand = 20000;

    private const int MaxYearEndAwards = 64;

    public const int MinReputation = 0;

    public const int MaxReputation = 1000;

    /// <summary>
    /// Price distortion below which a patient may find a price low, at medium difficulty (CorsixTH's
    /// <c>under_priced_thresholds</c>: −0.3, −0.4, −0.5).
    /// </summary>
    private const decimal UnderPricedThreshold = -0.4m;

    /// <summary>Price distortion above which a patient may find a price too high, at medium difficulty (0.4, 0.3, 0.2).</summary>
    private const decimal OverPricedThreshold = 0.3m;

    private readonly List<Transaction> _transactions = [];
    private readonly SortedDictionary<string, DiseaseCase> _casebook = new(StringComparer.Ordinal);

    /// <summary>What each insurance company owes: this month, last month and the month before.</summary>
    private readonly long[,] _insuranceOwed = new long[InsuranceCompanies, InsuranceMonths];

    private readonly List<YearEndAward> _lastYearAwards = [];
    private readonly SortedSet<string> _discoveredRooms = new(StringComparer.Ordinal);
    private readonly List<MonthStatistics> _statistics = [];

    internal Hospital(int player, long balance, int reputation = AverageReputation, long value = 0, int interestRate = DefaultInterestRate, int overdraftDifferential = DefaultOverdraftDifferential)
    {
        InterestRate = interestRate;
        OverdraftDifferential = overdraftDifferential;
        Player = player;
        Balance = balance;
        Reputation = Math.Clamp(reputation, MinReputation, MaxReputation);
        LowestReputationThisYear = Reputation;
        Value = value;
        _statistics.Add(new MonthStatistics(0, 0, 0, Balance, 0, 0, 0, Reputation));
    }

    /// <summary>
    /// The figures of each month for the graphs, the first as the game began (CorsixTH's <c>statistics</c>): entry
    /// <c>i</c> holds them at the end of month <c>i</c> of the game.
    /// </summary>
    public IReadOnlyList<MonthStatistics> Statistics => _statistics;

    /// <summary>Money taken in this month so far (CorsixTH's <c>money_in</c>).</summary>
    public long MoneyInThisMonth { get; private set; }

    /// <summary>Money spent this month so far (CorsixTH's <c>money_out</c>).</summary>
    public long MoneyOutThisMonth { get; private set; }

    /// <summary>
    /// What the hospital is worth (CorsixTH's <c>value</c>): its land and 20,000 at the start, plus what was paid for
    /// rooms and objects, less what objects were sold for.
    /// </summary>
    public long Value { get; private set; }

    /// <summary>What the hospital owes the bank (CorsixTH's <c>loan</c>).</summary>
    public long Loan { get; private set; }

    /// <summary>The bank's yearly interest on the loan, in hundredths of a percent.</summary>
    public int InterestRate { get; private set; }

    /// <summary>What an overdraft costs a year on top of <see cref="InterestRate"/>, in hundredths of a percent.</summary>
    public int OverdraftDifferential { get; private set; }

    /// <summary>
    /// What the hospital may owe the bank (<c>UIBankManager:increaseLoan</c>): a third of its value, rounded down to
    /// 5,000, and 10,000 more.
    /// </summary>
    public long MaxLoan => (long)decimal.Floor(Value * 0.33m / LoanStep) * LoanStep + 10000;

    /// <summary>Interest on the loan run up this month (CorsixTH's <c>acc_loan_interest</c>).</summary>
    private decimal AccruedLoanInterest { get; set; }

    /// <summary>Interest on the overdraft run up this month (<c>acc_overdraft</c>).</summary>
    private decimal AccruedOverdraft { get; set; }

    /// <summary>Patients who came this year (CorsixTH's <c>num_visitors_ty</c>).</summary>
    public int VisitorsThisYear { get; internal set; }

    /// <summary>Patients cured this year (CorsixTH's <c>num_cured_ty</c>).</summary>
    public int CuredThisYear { get; internal set; }

    /// <summary>Patients who died this year (CorsixTH's <c>num_deaths_this_year</c>).</summary>
    public int DeathsThisYear { get; internal set; }

    /// <summary>Patients who went home uncured this year (CorsixTH's <c>not_cured_ty</c>).</summary>
    public int NotCuredThisYear { get; internal set; }

    /// <summary>
    /// The lowest reputation since the year began; while it stays above the level's <c>awards_trophies.Reputation</c>
    /// the hospital is on its way to the consistent reputation trophy (CorsixTH's <c>has_impressive_reputation</c>).
    /// </summary>
    public int LowestReputationThisYear { get; private set; }

    /// <summary>
    /// Cured patients in percent of all who were cured, died or went home uncured, rounded (CorsixTH's
    /// <c>percentage_cured</c> from <c>Hospital:updatePercentages</c>); 0 until the first of them.
    /// </summary>
    public int PercentageCured
    {
        get
        {
            var all = Cured + NotCured + Deaths;
            return all == 0 ? 0 : RoundPercent(Cured, all);
        }
    }

    /// <summary>
    /// Deaths in percent of the cured and dead, rounded (CorsixTH's <c>percentage_killed</c>); 0 until the first
    /// patient leaves. CorsixTH computes 0 / 0 when patients have only gone home uncured, which is null here.
    /// </summary>
    public int? PercentageKilled => Cured + Deaths > 0 ? RoundPercent(Deaths, Cured + Deaths) : NotCured > 0 ? null : 0;

    /// <summary>Whether the hospital has won or lost the level.</summary>
    public EndOutcome Outcome { get; private set; }

    /// <summary>The goal the hospital lost to, if it lost.</summary>
    public EndGoal? LostTo { get; private set; }

    /// <summary>The trophies, awards and fines of the last year end, in the order of CorsixTH's annual report.</summary>
    public IReadOnlyList<YearEndAward> LastYearAwards => _lastYearAwards;

    /// <summary>The player (0-based) the hospital belongs to.</summary>
    public int Player { get; }

    /// <summary>Money in the bank; may go below zero.</summary>
    public long Balance { get; private set; }

    /// <summary>The latest transactions, newest first.</summary>
    public IReadOnlyList<Transaction> Transactions => _transactions;

    /// <summary>Patients who came to the hospital so far (CorsixTH's <c>num_visitors</c>).</summary>
    public int Visitors { get; internal set; }

    /// <summary>Patients cured so far (CorsixTH's <c>num_cured</c>).</summary>
    public int Cured { get; internal set; }

    /// <summary>Patients who died so far (CorsixTH's <c>num_deaths</c>).</summary>
    public int Deaths { get; internal set; }

    /// <summary>Patients who went home without a cure so far: sent home, gave up or found it too dear (CorsixTH's <c>not_cured</c>).</summary>
    public int NotCured { get; internal set; }

    /// <summary>The hospital's reputation, from 0 to 1000; levels start at their <c>StartRep</c>, usually 500.</summary>
    public int Reputation { get; private set; }

    /// <summary>Year ends so far, each of which raised prices by the inflation rate.</summary>
    public int InflationYears { get; private set; }

    /// <summary>
    /// Whether the hospital has opened, so that patients come (CorsixTH's <c>Hospital.opened</c>): when the opening
    /// watch has run down, or when the player opened it early.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>The room types the hospital may build, in id order: those discovered at the start or since by research.</summary>
    public IReadOnlyCollection<string> DiscoveredRooms => _discoveredRooms;

    /// <summary>Whether the hospital may build rooms of this type (CorsixTH's <c>Hospital:isRoomDiscovered</c>).</summary>
    public bool IsRoomDiscovered(string roomId) => _discoveredRooms.Contains(roomId);

    /// <summary>Lets the hospital build rooms of this type: at the start, or when research discovers it.</summary>
    internal void DiscoverRoom(string roomId) => _discoveredRooms.Add(roomId);

    /// <summary>Whether the player has used a cheat (CorsixTH's <c>cheated</c>).</summary>
    public bool Cheated { get; internal set; }

    /// <summary>Patients sent to the hospital so far, whether they came or not (CorsixTH's <c>spawn_attempts</c>).</summary>
    public long SpawnAttempts { get; internal set; }

    /// <summary>The radiators' heat at the start, in tenths (CorsixTH's <c>radiator_heat</c> of 0.5).</summary>
    public const int DefaultRadiatorHeat = 5;

    /// <summary>The highest heat the player may set, in tenths.</summary>
    public const int MaxRadiatorHeat = 10;

    /// <summary>The lowest heat the player may set, in tenths.</summary>
    public const int MinRadiatorHeat = 1;

    /// <summary>How hot the radiators are, in tenths (CorsixTH's <c>heating.radiator_heat</c>): 0 or 10 while the boiler is broken.</summary>
    public int RadiatorHeat { get; private set; } = DefaultRadiatorHeat;

    /// <summary>The hospital policy window's settings.</summary>
    public HospitalPolicies Policies { get; internal set; } = new();

    /// <summary>VIPs asking to visit, on their way and gone (<see cref="VipVisits"/>).</summary>
    public VipVisits Vips { get; internal set; } = new();

    /// <summary>Whether the boiler is broken, so that the radiators are stuck at full or no heat (CorsixTH's <c>heating_broke</c>).</summary>
    public bool IsHeatingBroken { get; private set; }

    /// <summary>The heat the player had set before the boiler broke (CorsixTH's <c>saved_radiator_heat</c>).</summary>
    private int SavedRadiatorHeat { get; set; } = DefaultRadiatorHeat;

    /// <summary>Repair work left on the broken boiler (CorsixTH's <c>boiler_repair_count</c>).</summary>
    public int BoilerRepairCount { get; private set; }

    /// <summary>Days until the next chance of a disaster, such as the boiler breaking (CorsixTH's <c>disasterless_days</c>).</summary>
    public int DisasterlessDays { get; private set; }

    /// <summary>The heating run up this month and not yet paid (CorsixTH's <c>acc_heating</c>).</summary>
    public decimal AccruedHeating { get; private set; }

    /// <summary>The opening watch's segments still to go, from 12 down to −1 (CorsixTH's <c>open_timer</c>).</summary>
    public int OpenTimer { get; private set; } = OpeningSegments;

    /// <summary>Hours left in the current segment of the opening watch (CorsixTH's <c>tick_timer</c>).</summary>
    private int OpenTickTimer { get; set; } = OpeningTickRate;

    /// <summary>Hours until the hospital opens by itself; 0 once open.</summary>
    public int HoursUntilOpen => IsOpen ? 0 : OpenTimer < 0 ? 1 : OpenTickTimer + 1 + (OpenTimer * (OpeningTickRate + 1)) + 1;

    /// <summary>Casebook entries made so far, by disease id.</summary>
    public IReadOnlyDictionary<string, DiseaseCase> Casebook => _casebook;

    /// <summary>What insurance company <paramref name="company"/> (1 to 3) owes in all.</summary>
    public long InsuranceOwed(int company)
    {
        long owed = 0;
        for (var month = 0; month < InsuranceMonths; month++)
        {
            owed += _insuranceOwed[company - 1, month];
        }

        return owed;
    }

    /// <summary>
    /// The casebook entry for a disease, made on first use: discovered if the level makes it known; a disease cured in
    /// the pharmacy gets the level's drug rating and cost (<c>gbv.StartRating</c>, <c>gbv.StartCost</c>).
    /// </summary>
    public DiseaseCase CaseOf(DiseaseDefinition disease, DiseaseRules rules)
    {
        if (!_casebook.TryGetValue(disease.Id, out var entry))
        {
            entry = new DiseaseCase(disease.Id, rules.IsKnown(disease.ExpertiseId)) { Reputation = disease.IsPseudo ? null : AverageReputation };
            if (disease.TreatmentRooms is ["pharmacy", ..])
            {
                entry.CureEffectiveness = rules.DrugStartRating;
                entry.DrugCost = rules.DrugStartCost;
            }

            _casebook.Add(disease.Id, entry);
        }

        return entry;
    }

    /// <summary>
    /// Sets the price of a disease or diagnosis the hospital knows, held to <see cref="DiseaseCase.MinPrice"/> to
    /// <see cref="DiseaseCase.MaxPrice"/> percent; one it does not know is not in the casebook to set.
    /// </summary>
    /// <returns>Whether the price was set.</returns>
    public bool SetTreatmentPrice(DiseaseDefinition disease, DiseaseRules rules, int percent)
    {
        var entry = CaseOf(disease, rules);
        if (!entry.Discovered)
        {
            return false;
        }

        entry.PricePercent = Math.Clamp(percent, DiseaseCase.MinPrice, DiseaseCase.MaxPrice);
        return true;
    }

    /// <summary>
    /// <c>Hospital:getTreatmentPrice</c>: the standard price with inflation, times the player's percentage, and with a
    /// reputation for the disease above 500 times reputation / 500, rounded up.
    /// </summary>
    public int TreatmentPrice(DiseaseDefinition disease, DiseaseRules rules)
    {
        var inflation = 1m;
        for (var i = 0; i < InflationYears; i++)
        {
            inflation *= 1 + InflationRate;
        }

        var entry = CaseOf(disease, rules);
        var price = rules.CurePrice(disease) * inflation * entry.PricePercent / DiseaseCase.StandardPrice;
        var reputation = entry.Reputation ?? Reputation;
        if (reputation >= AverageReputation)
        {
            price = price * reputation / AverageReputation;
        }

        return (int)decimal.Ceiling(price);
    }

    /// <summary>
    /// Pays money out; what is paid for land, a room or an object adds to the hospital's value (CorsixTH's
    /// <c>spendMoney</c> with <c>changeValue</c>).
    /// </summary>
    internal void Spend(long tick, int amount, TransactionKind kind, string subject)
    {
        if (kind is TransactionKind.BuyObject or TransactionKind.BuildRoom or TransactionKind.BuyLand)
        {
            Value += amount;
        }

        Balance -= amount;
        MoneyOutThisMonth += amount;
        Log(new Transaction(tick, kind, subject, -amount, Balance));
    }

    /// <summary>Takes money in; what an object is sold for is taken off the hospital's value (<c>receiveMoney</c>).</summary>
    internal void Receive(long tick, int amount, TransactionKind kind, string subject)
    {
        if (kind == TransactionKind.SellObject)
        {
            Value -= amount;
        }

        Balance += amount;
        MoneyInThisMonth += amount;
        Log(new Transaction(tick, kind, subject, amount, Balance));
    }

    /// <summary>
    /// Pays the month's wages of all staff in one transaction, as CorsixTH's
    /// <c>Hospital:onEndMonth</c> does; nothing is logged when they come to nothing.
    /// </summary>
    internal void PayWages(long tick, int wages)
    {
        if (wages != 0)
        {
            Spend(tick, wages, TransactionKind.Wages, string.Empty);
        }
    }

    /// <summary>
    /// <c>Hospital:receiveMoneyForTreatment</c>: the patient pays, or their insurance company owes it until two
    /// months later.
    /// </summary>
    /// <param name="tick">When.</param>
    /// <param name="entry">The casebook entry of what was paid for.</param>
    /// <param name="amount">What the patient agreed to pay.</param>
    /// <param name="insuranceCompany">The patient's insurance company (1 to 3), or 0 when they pay themselves.</param>
    internal void ReceiveForTreatment(long tick, DiseaseCase entry, int amount, int insuranceCompany)
    {
        if (insuranceCompany > 0)
        {
            _insuranceOwed[insuranceCompany - 1, 0] += amount;
        }
        else
        {
            Receive(tick, amount, TransactionKind.Treatment, entry.DiseaseId);
        }

        entry.MoneyEarned += amount;
    }

    /// <summary>
    /// The insurance part of <c>Hospital:onEndMonth</c>: each company pays what it owed from two months ago, and
    /// a new month begins with nothing owed.
    /// </summary>
    internal void PayInsurance(long tick)
    {
        for (var company = 0; company < InsuranceCompanies; company++)
        {
            var payout = _insuranceOwed[company, InsuranceMonths - 1];
            if (payout > 0)
            {
                Receive(tick, checked((int)payout), TransactionKind.Insurance, (company + 1).ToString(CultureInfo.InvariantCulture));
            }

            for (var month = InsuranceMonths - 1; month > 0; month--)
            {
                _insuranceOwed[company, month] = _insuranceOwed[company, month - 1];
            }

            _insuranceOwed[company, 0] = 0;
        }
    }

    /// <summary><c>Hospital:paySupplierForDrug</c>: the drug of a cure is paid to one of five drug companies.</summary>
    internal void PayForDrug(long tick, DiseaseCase entry, int company)
    {
        if (entry.DrugCost != 0)
        {
            Spend(tick, entry.DrugCost, TransactionKind.DrugCost, company.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// <c>Hospital:changeReputation</c>: the hospital's reputation changes if <see cref="IsReputationChangeAllowed"/>
    /// says so, and the disease's always.
    /// </summary>
    internal void ChangeReputation(SimRandom random, ReputationChange reason, DiseaseCase? entry = null)
    {
        var amount = reason switch
        {
            ReputationChange.Cured => 1,
            ReputationChange.Death => -4,
            ReputationChange.Kicked => -3,
            ReputationChange.OverPriced => -2,
            _ => 1,
        };
        ChangeReputation(random, amount);
        if (entry is { Reputation: { } diseaseReputation })
        {
            // CorsixTH does not keep a disease's reputation within 0 to 1000.
            entry.Reputation = diseaseReputation + amount;
        }
    }

    /// <summary><c>Hospital:changeReputation</c> by an amount, with no disease (the year end's <c>year_end</c>).</summary>
    internal void ChangeReputation(SimRandom random, int amount)
    {
        if (IsReputationChangeAllowed(random, amount))
        {
            UnconditionalChangeReputation(amount);
        }
    }

    /// <summary><c>Hospital:unconditionalChangeReputation</c>: the reputation changes, held to 0–1000.</summary>
    internal void UnconditionalChangeReputation(int amount)
    {
        Reputation = Math.Clamp(Reputation + amount, MinReputation, MaxReputation);
        LowestReputationThisYear = Math.Min(LowestReputationThisYear, Reputation);
    }

    /// <summary>
    /// <c>Hospital:isReputationChangeAllowed</c>: a gain at or below 500 and a loss at or above it always count; otherwise
    /// the change counts with a chance that falls to 0 towards 0 and 1000 (<c>getReputationChangeLikelihood</c>).
    /// </summary>
    private bool IsReputationChangeAllowed(SimRandom random, int amount)
    {
        if ((amount > 0 && Reputation <= AverageReputation) || (amount < 0 && Reputation >= AverageReputation) || amount == 0)
        {
            return true;
        }

        return random.NextUInt32() / 4294967296m <= ReputationChangeLikelihood;
    }

    /// <summary>
    /// <c>Hospital:getReputationChangeLikelihood</c>: 1 − (a·x² − b·x + c) with x the reputation, which is 1 at 500
    /// and 0 at 0 and 1000.
    /// </summary>
    public decimal ReputationChangeLikelihood
    {
        get
        {
            const decimal a = 0.000004008m;
            const decimal b = 0.004008m;
            const decimal c = 1m;
            decimal x = Reputation;
            return 1 - ((a * x * x) - (b * x) + c);
        }
    }

    /// <summary>
    /// <c>Hospital:updateNotCuredCounts</c>: a patient went home without a cure, sent home (<see cref="ReputationChange.Kicked"/>)
    /// or because of the price (<see cref="ReputationChange.OverPriced"/>).
    /// </summary>
    internal void UpdateNotCuredCounts(SimRandom random, ReputationChange reason, DiseaseCase entry)
    {
        ChangeReputation(random, reason, entry);
        NotCured++;
        NotCuredThisYear++;
        if (reason == ReputationChange.Kicked)
        {
            entry.TurnedAway++;
        }
    }

    /// <summary>
    /// <c>Hospital:computePriceLevelImpact</c> for a patient paying themselves, with <c>Patient:getPriceDistortion</c>:
    /// the price level (0 at half price, 1 at double) against what the patient expects from the reputation for the
    /// disease, the cure's effectiveness and their happiness. The patient's happiness drops by half the distortion (or
    /// rises, at a low price); now and then a price far off what they expect changes the reputation.
    /// </summary>
    internal void ComputePriceLevelImpact(Simulation simulation, Humanoid patient, DiseaseCase entry)
    {
        const decimal happinessWeight = 0.1m;
        const decimal reputationWeight = 0.6m;
        const decimal effectivenessWeight = 0.3m;
        var random = simulation.Random;
        var reputation = entry.Reputation ?? Reputation;
        var expected = (happinessWeight * patient.Happiness) + (reputationWeight * reputation / 1000m) + (effectivenessWeight * entry.CureEffectiveness / 100m);
        var priceLevel = (((entry.PricePercent / 100m) - 0.5m) / 3) * 2;
        var distortion = priceLevel - expected;
        Feelings.ChangeHappiness(simulation, patient, -(distortion / 2));
        if (distortion < UnderPricedThreshold)
        {
            if (random.NextInclusive(1, 100) == 1)
            {
                ChangeReputation(random, ReputationChange.UnderPriced);
            }
        }
        else if (distortion > OverPricedThreshold)
        {
            if (random.NextInclusive(1, 100) == 1)
            {
                ChangeReputation(random, ReputationChange.OverPriced);
            }
        }
        else if (Math.Abs(distortion) <= 0.15m)
        {
            // CorsixTH lets the adviser praise fair prices now and then; the chance is drawn all the same.
            random.NextInclusive(1, 200);
        }
    }

    /// <summary>Borrows 5,000, or as much as allowed, if the loan may grow by 5,000 (<c>UIBankManager:increaseLoan</c>).</summary>
    internal void Borrow(long tick, bool asMuchAsAllowed)
    {
        var max = MaxLoan;
        if (Loan + LoanStep > max)
        {
            return;
        }

        var amount = asMuchAsAllowed ? max - Loan : LoanStep;
        Loan += amount;
        Receive(tick, checked((int)amount), TransactionKind.Loan, string.Empty);
    }

    /// <summary>
    /// Pays back 5,000, or with <paramref name="asMuchAsPossible"/> and more than 5,000 in the bank as much of the loan
    /// as the balance allows in steps of 5,000, if there is a loan and the money (<c>UIBankManager:decreaseLoan</c>).
    /// </summary>
    internal void Repay(long tick, bool asMuchAsPossible)
    {
        long amount = LoanStep;
        if (asMuchAsPossible && Balance > LoanStep)
        {
            amount = Math.Min(Loan, Balance / LoanStep * LoanStep);
        }

        if (Loan > 0 && Balance >= amount)
        {
            Loan -= amount;
            Spend(tick, checked((int)amount), TransactionKind.LoanRepayment, string.Empty);
        }
    }

    /// <summary>
    /// <c>Hospital:onEndDay</c>: a day's interest on the loan, and on the overdraft if the balance is below zero, is run
    /// up (a year is 365 days).
    /// </summary>
    internal void OnEndDay()
    {
        AccruedLoanInterest += Loan * (InterestRate / 10000m) / 365;
        if (Balance < 0)
        {
            AccruedOverdraft += -Balance * ((InterestRate + OverdraftDifferential) / 10000m) / 365;
        }
    }

    /// <summary>
    /// The end of <c>Hospital:onEndMonth</c>, after the bills and insurance: the month's figures go into
    /// <see cref="Statistics"/> and the month's money in and out start afresh.
    /// </summary>
    internal void RecordMonth(int wages)
    {
        _statistics.Add(new MonthStatistics(MoneyInThisMonth, MoneyOutThisMonth, wages, Balance, Visitors, Cured, Deaths, Reputation));
        MoneyInThisMonth = 0;
        MoneyOutThisMonth = 0;
    }

    /// <summary>
    /// <c>UITownMap:increaseHeat</c> and <c>decreaseHeat</c>: sets the radiators' heat, held to
    /// <see cref="MinRadiatorHeat"/>–<see cref="MaxRadiatorHeat"/> tenths, unless the boiler is broken.
    /// </summary>
    /// <returns>Whether the heat was set.</returns>
    public bool SetRadiatorHeat(int tenths)
    {
        if (IsHeatingBroken)
        {
            return false;
        }

        RadiatorHeat = Math.Clamp(tenths, MinRadiatorHeat, MaxRadiatorHeat);
        return true;
    }

    /// <summary><c>Hospital:daysUntilNextDisaster</c> at medium difficulty: 200 days, give or take 10.</summary>
    internal void ScheduleNextDisaster(SimRandom random) => DisasterlessDays = 200 + random.NextInclusive(1, 21) - 11;

    /// <summary>
    /// The heating part of <c>Hospital:onEndDay</c>: work on the boiler, perhaps a disaster, and the day's share of
    /// the month's heating bill, which is the heat × 10 × the radiators × 7.5 over the month.
    /// </summary>
    /// <param name="random">The simulation's random numbers.</param>
    /// <param name="radiators">The radiators in the hospital.</param>
    /// <param name="handymen">The handymen the hospital employs.</param>
    /// <param name="daysInMonth">Days in the month that the day ends.</param>
    internal void OnEndDayHeating(SimRandom random, int radiators, int handymen, int daysInMonth)
    {
        FixBoiler(radiators, handymen);
        DisasterlessDays--;
        if (DisasterlessDays <= 0)
        {
            ScheduleNextDisaster(random);

            // 1 is no disaster; the vomit wave (4) is not there in CorsixTH either.
            switch (random.NextInclusive(1, 3))
            {
                case 2:
                    BreakBoiler(random, MaxRadiatorHeat, radiators, handymen);
                    break;
                case 3:
                    BreakBoiler(random, 0, radiators, handymen);
                    break;
            }
        }

        AccruedHeating += RadiatorHeat * radiators * 7.5m / daysInMonth;
    }

    /// <summary><c>Hospital:onEndMonth</c> after the wages: the month's heating is paid, rounded, unless it rounds to nothing.</summary>
    internal void PayHeating(long tick)
    {
        var heating = RoundHalfUp(AccruedHeating);
        if (heating > 0)
        {
            Spend(tick, heating, TransactionKind.Heating, string.Empty);
            AccruedHeating = 0;
        }
    }

    /// <summary>
    /// <c>Hospital:boilerBreakdown</c>: an open hospital with radiators and too few handymen to look after them (one per
    /// 8 radiators) has its boiler break, stuck at <paramref name="brokenHeat"/>, with 10 to 30 repairs to do.
    /// </summary>
    private void BreakBoiler(SimRandom random, int brokenHeat, int radiators, int handymen)
    {
        if (!IsOpen || IsHeatingBroken || radiators == 0 || radiators <= 8 * handymen)
        {
            return;
        }

        SavedRadiatorHeat = RadiatorHeat;
        RadiatorHeat = brokenHeat;
        BoilerRepairCount = random.NextInclusive(10, 30);
        IsHeatingBroken = true;
    }

    /// <summary><c>Hospital:_fixBoiler</c>: a day's repairs, the more the more handymen there are for the radiators.</summary>
    private void FixBoiler(int radiators, int handymen)
    {
        if (!IsHeatingBroken)
        {
            return;
        }

        BoilerRepairCount -= radiators < 5 * handymen ? 3 : radiators < 8 * handymen ? 2 : 1;
        if (BoilerRepairCount <= 0)
        {
            RadiatorHeat = SavedRadiatorHeat;
            IsHeatingBroken = false;
        }
    }

    /// <summary>
    /// <c>Hospital:onEndMonth</c> after the wages: the month's interest on the loan and the overdraft is paid, rounded,
    /// unless it rounds to nothing, in which case it waits for the next month.
    /// </summary>
    internal void PayInterest(long tick)
    {
        var interest = RoundHalfUp(AccruedLoanInterest);
        if (interest > 0)
        {
            Spend(tick, interest, TransactionKind.LoanInterest, string.Empty);
            AccruedLoanInterest = 0;
        }

        var overdraft = RoundHalfUp(AccruedOverdraft);
        if (overdraft > 0)
        {
            Spend(tick, overdraft, TransactionKind.Overdraft, string.Empty);
            AccruedOverdraft = 0;
        }
    }

    private static int RoundHalfUp(decimal value) => (int)decimal.Floor(value + 0.5m);

    /// <summary>Records that the hospital won or lost; the first outcome stands.</summary>
    internal void End(EndOutcome outcome, EndGoal? lostTo)
    {
        if (Outcome == EndOutcome.None && outcome != EndOutcome.None)
        {
            Outcome = outcome;
            LostTo = lostTo;
        }
    }

    /// <summary>Lua's <c>math.round</c>, <c>floor(x + 0.5)</c>, of a part in percent.</summary>
    private static int RoundPercent(int part, int all) => (int)decimal.Floor((part * 100m / all) + 0.5m);

    /// <summary>Opens the hospital (CorsixTH's <c>Hospital:open</c>), when the watch runs down or the player clicks it.</summary>
    internal void Open() => IsOpen = true;

    /// <summary>
    /// One hour of the opening watch, as CorsixTH's <c>UIWatch:onWorldTick</c> for "initial_opening": each segment
    /// takes <see cref="OpeningTickRate"/> + 1 hours, and an hour after the last one the hospital opens.
    /// </summary>
    internal void TickOpeningWatch()
    {
        if (IsOpen)
        {
            return;
        }

        if (OpenTickTimer == 0 && OpenTimer >= 0)
        {
            OpenTickTimer = OpeningTickRate;
            OpenTimer--;
        }
        else if (OpenTimer == -1)
        {
            Open();
        }
        else
        {
            OpenTickTimer--;
        }
    }

    /// <summary>
    /// The year end: CorsixTH's annual report judges the year (<c>UIAnnualReport:checkTrophiesAndAwards</c>), then
    /// <c>Hospital:onEndYear</c> starts the year's counts afresh and raises prices by the inflation rate, and then the
    /// trophy and award money and reputation are given (<c>UIAnnualReport:updateAwards</c>).
    /// </summary>
    internal void OnEndYear(long tick, SimRandom random, AwardRules prices)
    {
        JudgeYear(random, prices);

        VisitorsThisYear = 0;
        Vips.ThisYear = 0;
        Vips.PleasedThisYear = 0;
        CuredThisYear = 0;
        NotCuredThisYear = 0;
        DeathsThisYear = 0;
        LowestReputationThisYear = Reputation;
        InflationYears++;

        var trophyMoney = 0;
        var awardMoney = 0;
        var reputation = 0;
        foreach (var award in _lastYearAwards)
        {
            if (award.Kind <= YearEndAwardKind.ManyCuredTrophy)
            {
                trophyMoney += award.Money;
            }
            else
            {
                awardMoney += award.Money;
            }

            reputation += award.Reputation;
        }

        if (trophyMoney != 0)
        {
            Receive(tick, trophyMoney, TransactionKind.TrophyBonus, string.Empty);
        }

        if (awardMoney != 0)
        {
            Receive(tick, awardMoney, TransactionKind.AwardBonus, string.Empty);
        }

        if (reputation != 0)
        {
            ChangeReputation(random, reputation);
        }
    }

    /// <summary>
    /// <c>UIAnnualReport:checkTrophiesAndAwards</c>. Nothing counts unless more than one patient was cured this year,
    /// except the fines. The trophy for drinks sold is left out until drinks are sold.
    /// </summary>
    private void JudgeYear(SimRandom random, AwardRules prices)
    {
        _lastYearAwards.Clear();
        var anybodyCured = CuredThisYear > 1;

        // Trophies.
        if (anybodyCured && Vips.ThisYear > 0 && Vips.PleasedThisYear == Vips.ThisYear)
        {
            _lastYearAwards.Add(new(YearEndAwardKind.HappyVips, 0, (prices[AwardRules.TrophyMayorBonus] * Vips.PleasedThisYear) + random.NextInclusive(1, 5)));
        }

        if (anybodyCured && LowestReputationThisYear > prices[AwardRules.Reputation])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.ConsistentReputation, prices[AwardRules.TrophyReputationBonus], 0));
        }

        if (anybodyCured && DeathsThisYear == 0 && NotCuredThisYear == 0)
        {
            _lastYearAwards.Add(new(YearEndAwardKind.AllCuredTrophy, prices[AwardRules.TrophyAllCuredBonus], 0));
        }
        else if (anybodyCured && DeathsThisYear == 0)
        {
            _lastYearAwards.Add(new(YearEndAwardKind.NoDeathsTrophy, prices[AwardRules.TrophyDeathBonus], 0));
        }
        else if (anybodyCured && CuredThisYear > NotCuredThisYear * 0.9m)
        {
            _lastYearAwards.Add(new(YearEndAwardKind.ManyCuredTrophy, prices[AwardRules.TrophyCuresBonus], 0));
        }

        // Awards and fines.
        if (anybodyCured && Reputation > prices[AwardRules.ReputationAward])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.HighReputation, prices[AwardRules.AwardReputationBonus], 0));
        }
        else if (Reputation < prices[AwardRules.ReputationPoor])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.PoorReputation, prices[AwardRules.AwardReputationPenalty], 0));
        }

        if (anybodyCured && Value > prices[AwardRules.HospValueAward])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.HighValue, 0, prices[AwardRules.HospValueBonus] * random.NextInclusive(1, 15)));
        }
        else if (Value < prices[AwardRules.HospValuePoor])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.LowValue, 0, prices[AwardRules.HospValuePenalty] * random.NextInclusive(1, 15)));
        }

        if (anybodyCured && CuredThisYear > prices[AwardRules.CuresAward])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.ManyCures, prices[AwardRules.CuresBonus], 0));
        }
        else if (CuredThisYear < prices[AwardRules.CuresPoor])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.FewCures, prices[AwardRules.CuresPenalty], 0));
        }

        if (anybodyCured && DeathsThisYear == 0 && NotCuredThisYear == 0)
        {
            _lastYearAwards.Add(new(YearEndAwardKind.AllCures, prices[AwardRules.AllCuresBonus], 0));
        }
        else if (anybodyCured && DeathsThisYear < prices[AwardRules.DeathsAward])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.FewDeaths, prices[AwardRules.DeathsBonus], 0));
        }
        else if (DeathsThisYear > prices[AwardRules.DeathsPoor])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.ManyDeaths, prices[AwardRules.DeathsPenalty], 0));
        }

        var cureRatio = DeathsThisYear > 0 ? CuredThisYear / (decimal)DeathsThisYear : 100;
        if (anybodyCured && cureRatio > prices[AwardRules.CuresVDeathsAward])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.CuresOverDeaths, prices[AwardRules.CuresVDeathsBonus], 0));
        }
        else if (cureRatio <= prices[AwardRules.CuresVDeathsPoor])
        {
            _lastYearAwards.Add(new(YearEndAwardKind.DeathsOverCures, prices[AwardRules.CuresVDeathsPenalty], 0));
        }
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt64(Balance);
        writer.WriteInt32(_transactions.Count);
        foreach (var transaction in _transactions)
        {
            writer.WriteInt64(transaction.Tick);
            writer.WriteByte((byte)transaction.Kind);
            writer.WriteString(transaction.Subject);
            writer.WriteInt32(transaction.Amount);
            writer.WriteInt64(transaction.Balance);
        }

        writer.WriteInt32(Visitors);
        writer.WriteInt32(Cured);
        writer.WriteInt32(Deaths);
        writer.WriteInt32(NotCured);
        writer.WriteInt32(Reputation);
        writer.WriteInt32(InflationYears);
        writer.WriteInt64(Value);
        writer.WriteInt32(VisitorsThisYear);
        writer.WriteInt32(CuredThisYear);
        writer.WriteInt32(DeathsThisYear);
        writer.WriteInt32(NotCuredThisYear);
        writer.WriteInt32(LowestReputationThisYear);
        writer.WriteInt32(_lastYearAwards.Count);
        foreach (var award in _lastYearAwards)
        {
            writer.WriteByte((byte)award.Kind);
            writer.WriteInt32(award.Money);
            writer.WriteInt32(award.Reputation);
        }

        writer.WriteInt64(Loan);
        writer.WriteInt32(InterestRate);
        writer.WriteInt32(OverdraftDifferential);
        writer.WriteDecimal(AccruedLoanInterest);
        writer.WriteDecimal(AccruedOverdraft);
        writer.WriteByte((byte)Outcome);
        writer.WriteBool(LostTo is not null);
        if (LostTo is { } lostTo)
        {
            writer.WriteByte((byte)lostTo.Criterion);
            writer.WriteInt32(lostTo.Group);
            writer.WriteBool(lostTo.AtLeast);
            writer.WriteInt32(lostTo.Value);
            writer.WriteInt32(lostTo.Bound);
            writer.WriteInt32(lostTo.Start);
        }

        writer.WriteInt32(_discoveredRooms.Count);
        foreach (var room in _discoveredRooms)
        {
            writer.WriteString(room);
        }

        writer.WriteInt64(MoneyInThisMonth);
        writer.WriteInt64(MoneyOutThisMonth);
        writer.WriteInt32(_statistics.Count);
        foreach (var month in _statistics)
        {
            writer.WriteInt64(month.MoneyIn);
            writer.WriteInt64(month.MoneyOut);
            writer.WriteInt32(month.Wages);
            writer.WriteInt64(month.Balance);
            writer.WriteInt32(month.Visitors);
            writer.WriteInt32(month.Cures);
            writer.WriteInt32(month.Deaths);
            writer.WriteInt32(month.Reputation);
        }

        writer.WriteBool(Cheated);
        writer.WriteInt64(SpawnAttempts);
        writer.WriteInt32(RadiatorHeat);
        writer.WriteBool(IsHeatingBroken);
        writer.WriteInt32(SavedRadiatorHeat);
        writer.WriteInt32(BoilerRepairCount);
        writer.WriteInt32(DisasterlessDays);
        writer.WriteDecimal(AccruedHeating);
        writer.WriteBool(IsOpen);
        writer.WriteInt32(OpenTimer);
        writer.WriteInt32(OpenTickTimer);
        Policies.Write(writer);
        Vips.Write(writer);
        for (var company = 0; company < InsuranceCompanies; company++)
        {
            for (var month = 0; month < InsuranceMonths; month++)
            {
                writer.WriteInt64(_insuranceOwed[company, month]);
            }
        }

        writer.WriteInt32(_casebook.Count);
        foreach (var entry in _casebook.Values)
        {
            writer.WriteString(entry.DiseaseId);
            writer.WriteInt32(entry.PricePercent);
            writer.WriteBool(entry.Discovered);
            writer.WriteInt64(entry.MoneyEarned);
            writer.WriteInt32(entry.CureEffectiveness);
            writer.WriteInt32(entry.DrugCost);
            writer.WriteInt32(entry.Recoveries);
            writer.WriteInt32(entry.Fatalities);
            writer.WriteBool(entry.Reputation is not null);
            writer.WriteInt32(entry.Reputation ?? 0);
            writer.WriteInt32(entry.TurnedAway);
        }
    }

    internal static Hospital Read(SimReader reader, int player)
    {
        var hospital = new Hospital(player, reader.ReadInt64());
        var count = reader.ReadCount(LogLength);
        for (var i = 0; i < count; i++)
        {
            var tick = reader.ReadInt64();
            var kind = reader.ReadByte();
            if (kind > (byte)TransactionKind.VipAward)
            {
                throw new InvalidDataException($"Unknown transaction kind {kind}.");
            }

            hospital._transactions.Add(new Transaction(tick, (TransactionKind)kind, reader.ReadString(), reader.ReadInt32(), reader.ReadInt64()));
        }

        hospital.Visitors = reader.ReadInt32();
        hospital.Cured = reader.ReadInt32();
        hospital.Deaths = reader.ReadInt32();
        hospital.NotCured = reader.ReadInt32();
        hospital.Reputation = reader.ReadInt32();
        hospital.InflationYears = reader.ReadInt32();
        hospital.Value = reader.ReadInt64();
        hospital.VisitorsThisYear = reader.ReadInt32();
        hospital.CuredThisYear = reader.ReadInt32();
        hospital.DeathsThisYear = reader.ReadInt32();
        hospital.NotCuredThisYear = reader.ReadInt32();
        hospital.LowestReputationThisYear = reader.ReadInt32();
        var awards = reader.ReadCount(MaxYearEndAwards);
        for (var i = 0; i < awards; i++)
        {
            var kind = reader.ReadByte();
            if (kind > (byte)YearEndAwardKind.HappyVips)
            {
                throw new InvalidDataException($"Unknown year end award {kind}.");
            }

            hospital._lastYearAwards.Add(new((YearEndAwardKind)kind, reader.ReadInt32(), reader.ReadInt32()));
        }

        hospital.Loan = reader.ReadInt64();
        hospital.InterestRate = reader.ReadInt32();
        hospital.OverdraftDifferential = reader.ReadInt32();
        hospital.AccruedLoanInterest = reader.ReadDecimal();
        hospital.AccruedOverdraft = reader.ReadDecimal();
        var outcome = reader.ReadByte();
        if (outcome > (byte)EndOutcome.Lost)
        {
            throw new InvalidDataException($"Unknown outcome {outcome}.");
        }

        hospital.Outcome = (EndOutcome)outcome;
        if (reader.ReadBool())
        {
            hospital.LostTo = new EndGoal((EndCriterion)reader.ReadByte(), reader.ReadInt32(), reader.ReadBool(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }

        var rooms = reader.ReadCount(MaxCases);
        for (var i = 0; i < rooms; i++)
        {
            hospital._discoveredRooms.Add(reader.ReadString());
        }

        hospital.MoneyInThisMonth = reader.ReadInt64();
        hospital.MoneyOutThisMonth = reader.ReadInt64();
        hospital._statistics.Clear();
        var months = reader.ReadCount(MaxStatisticsMonths);
        for (var i = 0; i < months; i++)
        {
            hospital._statistics.Add(new MonthStatistics(
                reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        }

        hospital.Cheated = reader.ReadBool();
        hospital.SpawnAttempts = reader.ReadInt64();
        hospital.RadiatorHeat = reader.ReadInt32();
        hospital.IsHeatingBroken = reader.ReadBool();
        hospital.SavedRadiatorHeat = reader.ReadInt32();
        hospital.BoilerRepairCount = reader.ReadInt32();
        hospital.DisasterlessDays = reader.ReadInt32();
        hospital.AccruedHeating = reader.ReadDecimal();
        hospital.IsOpen = reader.ReadBool();
        hospital.OpenTimer = reader.ReadInt32();
        hospital.OpenTickTimer = reader.ReadInt32();
        hospital.Policies = HospitalPolicies.Read(reader);
        hospital.Vips = VipVisits.Read(reader);
        for (var company = 0; company < InsuranceCompanies; company++)
        {
            for (var month = 0; month < InsuranceMonths; month++)
            {
                hospital._insuranceOwed[company, month] = reader.ReadInt64();
            }
        }

        var cases = reader.ReadCount(MaxCases);
        for (var i = 0; i < cases; i++)
        {
            var id = reader.ReadString();
            var entry = new DiseaseCase(id, discovered: false) { PricePercent = reader.ReadInt32() };
            entry.Discovered = reader.ReadBool();
            entry.MoneyEarned = reader.ReadInt64();
            entry.CureEffectiveness = reader.ReadInt32();
            entry.DrugCost = reader.ReadInt32();
            entry.Recoveries = reader.ReadInt32();
            entry.Fatalities = reader.ReadInt32();
            var hasReputation = reader.ReadBool();
            var reputation = reader.ReadInt32();
            entry.Reputation = hasReputation ? reputation : null;
            entry.TurnedAway = reader.ReadInt32();
            if (!hospital._casebook.TryAdd(id, entry))
            {
                throw new InvalidDataException($"Casebook entry '{id}' is given twice.");
            }
        }

        return hospital;
    }

    private void Log(Transaction transaction)
    {
        _transactions.Insert(0, transaction);
        if (_transactions.Count > LogLength)
        {
            _transactions.RemoveAt(_transactions.Count - 1);
        }
    }
}
