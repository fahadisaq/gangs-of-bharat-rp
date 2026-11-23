using System;
using System.Collections.Generic;

namespace GangsOfBharat.Shared.Gameplay.Economy
{
    public enum TransferResult : byte
    {
        Ok = 0,
        InvalidAmount = 1,
        InvalidAccount = 2,
        SameAccount = 3,
        InsufficientFunds = 4,
        Overflow = 5,
        MissingReason = 6,
    }

    /// <summary>One immutable line of the transaction log. Money is whole rupees (long).</summary>
    public sealed class TransactionRecord
    {
        public readonly long Id;
        public readonly DateTime TimeUtc;
        public readonly string From;
        public readonly string To;
        public readonly long Amount;
        public readonly string Reason;

        public TransactionRecord(long id, DateTime timeUtc, string from, string to, long amount, string reason)
        {
            Id = id;
            TimeUtc = timeUtc;
            From = from;
            To = to;
            Amount = amount;
            Reason = reason;
        }
    }

    /// <summary>
    /// Server-authoritative money ledger (docs/design/07-economy.md: "All money changes happen on the
    /// server and are written to a transaction log").
    /// Accounts are string ids created on first use with balance 0. Only <see cref="Source"/> may go
    /// negative: it represents money entering the economy (job pay, rewards, salary floor top-ups).
    /// Money sent to <see cref="Sink"/> leaves the economy (fees, commissions) and is tracked per reason
    /// for economy charts. Every operation is atomic and thread-safe (single lock).
    /// </summary>
    public sealed class Ledger
    {
        public const string Source = "source";
        public const string Sink = "sink";
        public const string CityTreasury = "city";

        public static string Cash(string playerId)
        {
            return "cash:" + playerId;
        }

        public static string Bank(string playerId)
        {
            return "bank:" + playerId;
        }

        public static string FactionTreasury(FactionId faction)
        {
            return "faction:" + ((int)faction).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Escrow(string key)
        {
            return "escrow:" + key;
        }

        private readonly object _lock = new object();
        private readonly IClock _clock;
        private readonly Dictionary<string, long> _balances = new Dictionary<string, long>();
        private readonly List<TransactionRecord> _log = new List<TransactionRecord>();
        private readonly Dictionary<string, long> _sinkByReason = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _sourceByReason = new Dictionary<string, long>();
        private long _nextId = 1;
        private long _sinkTotal;
        private long _sourceTotal;

        public Ledger(IClock clock)
        {
            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }
            _clock = clock;
        }

        public long GetBalance(string account)
        {
            lock (_lock)
            {
                long b;
                return account != null && _balances.TryGetValue(account, out b) ? b : 0;
            }
        }

        /// <summary>Moves money from one account to another, or does nothing and returns the failure reason.</summary>
        public TransferResult Transfer(string from, string to, long amount, string reason)
        {
            TransactionRecord unused;
            return Transfer(from, to, amount, reason, out unused);
        }

        public TransferResult Transfer(string from, string to, long amount, string reason, out TransactionRecord record)
        {
            record = null;
            if (amount <= 0)
            {
                return TransferResult.InvalidAmount;
            }
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                return TransferResult.InvalidAccount;
            }
            if (from == to)
            {
                return TransferResult.SameAccount;
            }
            if (string.IsNullOrEmpty(reason))
            {
                return TransferResult.MissingReason;
            }
            lock (_lock)
            {
                long fromBalance;
                _balances.TryGetValue(from, out fromBalance);
                long toBalance;
                _balances.TryGetValue(to, out toBalance);
                if (from != Source && fromBalance < amount)
                {
                    return TransferResult.InsufficientFunds;
                }
                if (toBalance > long.MaxValue - amount || (from == Source && fromBalance < long.MinValue + amount))
                {
                    return TransferResult.Overflow;
                }
                _balances[from] = fromBalance - amount;
                _balances[to] = toBalance + amount;
                record = new TransactionRecord(_nextId++, _clock.UtcNow, from, to, amount, reason);
                _log.Add(record);
                if (to == Sink)
                {
                    _sinkTotal += amount;
                    Add(_sinkByReason, reason, amount);
                }
                if (from == Source)
                {
                    _sourceTotal += amount;
                    Add(_sourceByReason, reason, amount);
                }
                return TransferResult.Ok;
            }
        }

        /// <summary>Creates money from <see cref="Source"/> (job pay, rewards, test setup).</summary>
        public TransferResult Mint(string to, long amount, string reason)
        {
            return Transfer(Source, to, amount, reason);
        }

        /// <summary>Removes money from the economy into <see cref="Sink"/> (fees, commissions).</summary>
        public TransferResult Burn(string from, long amount, string reason)
        {
            return Transfer(from, Sink, amount, reason);
        }

        public int TransactionCount
        {
            get
            {
                lock (_lock)
                {
                    return _log.Count;
                }
            }
        }

        /// <summary>Copy of the transaction log (records themselves are immutable).</summary>
        public List<TransactionRecord> GetTransactions()
        {
            lock (_lock)
            {
                return new List<TransactionRecord>(_log);
            }
        }

        public long SinkTotal
        {
            get
            {
                lock (_lock)
                {
                    return _sinkTotal;
                }
            }
        }

        public long SourceTotal
        {
            get
            {
                lock (_lock)
                {
                    return _sourceTotal;
                }
            }
        }

        public long SinkTotalFor(string reason)
        {
            lock (_lock)
            {
                long v;
                return _sinkByReason.TryGetValue(reason, out v) ? v : 0;
            }
        }

        public long SourceTotalFor(string reason)
        {
            lock (_lock)
            {
                long v;
                return _sourceByReason.TryGetValue(reason, out v) ? v : 0;
            }
        }

        /// <summary>Snapshot of sink totals per reason, for economy charts.</summary>
        public Dictionary<string, long> GetSinkTotalsByReason()
        {
            lock (_lock)
            {
                return new Dictionary<string, long>(_sinkByReason);
            }
        }

        /// <summary>Sum of all balances, Source included. Always 0: money is only ever moved.</summary>
        public long NetBalance()
        {
            lock (_lock)
            {
                long sum = 0;
                foreach (KeyValuePair<string, long> kv in _balances)
                {
                    sum += kv.Value;
                }
                return sum;
            }
        }

        private static void Add(Dictionary<string, long> map, string key, long amount)
        {
            long v;
            map.TryGetValue(key, out v);
            map[key] = v + amount;
        }
    }
}
