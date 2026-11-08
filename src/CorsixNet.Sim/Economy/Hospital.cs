// Spending, receiving and the transaction log ported from CorsixTH CorsixTH/Lua/hospital.lua
// (Hospital:spendMoney, Hospital:receiveMoney, Hospital:logTransaction).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Economy;

/// <summary>What a transaction was for, as in CorsixTH's <c>_S.transactions</c>.</summary>
public enum TransactionKind : byte
{
    BuyObject = 0,
    SellObject = 1,
    BuildRoom = 2,
    HireStaff = 3,
}

/// <summary>One entry of a hospital's transaction log.</summary>
/// <param name="Tick">When it happened.</param>
/// <param name="Kind">What it was for.</param>
/// <param name="Subject">Id of the room or object type it was for, or the staff class of a hire.</param>
/// <param name="Amount">Money received (positive) or spent (negative).</param>
/// <param name="Balance">The balance afterwards.</param>
public readonly record struct Transaction(long Tick, TransactionKind Kind, string Subject, int Amount, long Balance);

/// <summary>A player's hospital: for now, its money.</summary>
public sealed class Hospital
{
    /// <summary>How many transactions the log keeps; CorsixTH keeps about 20.</summary>
    public const int LogLength = 20;

    private readonly List<Transaction> _transactions = [];

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
    }

    internal static Hospital Read(SimReader reader, int player)
    {
        var hospital = new Hospital(player, reader.ReadInt64());
        var count = reader.ReadCount(LogLength);
        for (var i = 0; i < count; i++)
        {
            var tick = reader.ReadInt64();
            var kind = reader.ReadByte();
            if (kind > (byte)TransactionKind.HireStaff)
            {
                throw new InvalidDataException($"Unknown transaction kind {kind}.");
            }

            hospital._transactions.Add(new Transaction(tick, (TransactionKind)kind, reader.ReadString(), reader.ReadInt32(), reader.ReadInt64()));
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
