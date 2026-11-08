// Spending, receiving, the transaction log and monthly wages ported from CorsixTH CorsixTH/Lua/hospital.lua
// (Hospital:spendMoney, Hospital:receiveMoney, Hospital:logTransaction, Hospital:onEndMonth); the casebook,
// treatment prices and insurance from the same file (Hospital:Hospital, Hospital:receiveMoneyForTreatment,
// Hospital:getTreatmentPrice, Hospital:addInsuranceMoney, Hospital:onEndYear).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Sim.Diseases;
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

    /// <summary>
    /// Hours per segment of the opening watch: CorsixTH's <c>UIWatch</c> runs 100 days over 13 segments
    /// (<c>TICK_DAYS</c>, <c>TIMER_SEGMENTS</c>), rounded down.
    /// </summary>
    public const int OpeningTickRate = 100 * 50 / 13;

    /// <summary>Segments of the opening watch (CorsixTH's <c>open_timer</c> starts at 12).</summary>
    public const int OpeningSegments = 12;

    private readonly List<Transaction> _transactions = [];
    private readonly SortedDictionary<string, DiseaseCase> _casebook = new(StringComparer.Ordinal);

    /// <summary>What each insurance company owes: this month, last month and the month before.</summary>
    private readonly long[,] _insuranceOwed = new long[InsuranceCompanies, InsuranceMonths];

    internal Hospital(int player, long balance)
    {
        Player = player;
        Balance = balance;
    }

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

    /// <summary>Year ends so far, each of which raised prices by the inflation rate.</summary>
    public int InflationYears { get; private set; }

    /// <summary>
    /// Whether the hospital has opened, so that patients come (CorsixTH's <c>Hospital.opened</c>): when the opening
    /// watch has run down, or when the player opened it early.
    /// </summary>
    public bool IsOpen { get; private set; }

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
            entry = new DiseaseCase(disease.Id, rules.IsKnown(disease.ExpertiseId));
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
    /// <c>Hospital:getTreatmentPrice</c>: the standard price with inflation, times the player's percentage, rounded
    /// up. Reputation is not modelled yet; at CorsixTH's starting reputation of 500 it does not change the price.
    /// </summary>
    public int TreatmentPrice(DiseaseDefinition disease, DiseaseRules rules)
    {
        var inflation = 1m;
        for (var i = 0; i < InflationYears; i++)
        {
            inflation *= 1 + InflationRate;
        }

        var price = rules.CurePrice(disease) * inflation * CaseOf(disease, rules).PricePercent / DiseaseCase.StandardPrice;
        return (int)decimal.Ceiling(price);
    }

    internal void Spend(long tick, int amount, TransactionKind kind, string subject)
    {
        Balance -= amount;
        Log(new Transaction(tick, kind, subject, -amount, Balance));
    }

    internal void Receive(long tick, int amount, TransactionKind kind, string subject)
    {
        Balance += amount;
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

    /// <summary><c>Hospital:onEndYear</c>: prices go up by the inflation rate.</summary>
    internal void OnEndYear() => InflationYears++;

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
        writer.WriteInt32(InflationYears);
        writer.WriteBool(IsOpen);
        writer.WriteInt32(OpenTimer);
        writer.WriteInt32(OpenTickTimer);
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
            if (kind > (byte)TransactionKind.DrugCost)
            {
                throw new InvalidDataException($"Unknown transaction kind {kind}.");
            }

            hospital._transactions.Add(new Transaction(tick, (TransactionKind)kind, reader.ReadString(), reader.ReadInt32(), reader.ReadInt64()));
        }

        hospital.Visitors = reader.ReadInt32();
        hospital.Cured = reader.ReadInt32();
        hospital.Deaths = reader.ReadInt32();
        hospital.InflationYears = reader.ReadInt32();
        hospital.IsOpen = reader.ReadBool();
        hospital.OpenTimer = reader.ReadInt32();
        hospital.OpenTickTimer = reader.ReadInt32();
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
