using System;
using System.Collections.Generic;
using GangsOfBharat.Shared.Gameplay.Economy;

namespace GangsOfBharat.Shared.Gameplay.Factions
{
    public sealed class PaydayRecord
    {
        public readonly string CharacterId;
        public readonly FactionId Faction;
        public readonly int RankLevel;
        /// <summary>Full rank salary from <see cref="FactionCatalog"/>.</summary>
        public readonly long Salary;
        public readonly long PaidFromTreasury;
        /// <summary>Floor top-up created from <see cref="Ledger.Source"/> when the treasury was short.</summary>
        public readonly long PaidFromFloor;

        public PaydayRecord(string characterId, FactionId faction, int rankLevel, long salary, long paidFromTreasury, long paidFromFloor)
        {
            CharacterId = characterId;
            Faction = faction;
            RankLevel = rankLevel;
            Salary = salary;
            PaidFromTreasury = paidFromTreasury;
            PaidFromFloor = paidFromFloor;
        }

        public long Total
        {
            get { return PaidFromTreasury + PaidFromFloor; }
        }
    }

    /// <summary>
    /// Payday rules from docs/design/04-factions.md: every 30 minutes of active (non-AFK) play a member
    /// earns the rank salary into their bank account. Government factions are paid from the city
    /// treasury. Gangs and mafia have salary 0 and earn nothing here.
    ///
    /// Salary floor (documented choice): if the city treasury cannot cover a salary, the treasury pays
    /// what it has and the shortfall up to <see cref="FloorPercent"/> of the salary is created from
    /// <see cref="Ledger.Source"/> (state emergency fund). So pay never drops below the floor, and the
    /// treasury never goes negative.
    /// </summary>
    public sealed class PaydayService
    {
        public const int DefaultFloorPercent = 50;

        private readonly FactionService _factions;
        private readonly Ledger _ledger;
        private readonly Dictionary<string, double> _activeSeconds = new Dictionary<string, double>();
        private int _floorPercent = DefaultFloorPercent;

        public PaydayService(FactionService factions, Ledger ledger)
        {
            if (factions == null)
            {
                throw new ArgumentNullException("factions");
            }
            if (ledger == null)
            {
                throw new ArgumentNullException("ledger");
            }
            _factions = factions;
            _ledger = ledger;
        }

        public static double IntervalSeconds
        {
            get { return FactionCatalog.PaydayIntervalMinutes * 60.0; }
        }

        /// <summary>Minimum share of the rank salary that is always paid, 0-100.</summary>
        public int FloorPercent
        {
            get { return _floorPercent; }
            set
            {
                if (value < 0 || value > 100)
                {
                    throw new ArgumentOutOfRangeException("value");
                }
                _floorPercent = value;
            }
        }

        /// <summary>Active seconds accumulated towards the next payday.</summary>
        public double ActiveSeconds(string characterId)
        {
            double s;
            return _activeSeconds.TryGetValue(characterId, out s) ? s : 0;
        }

        /// <summary>
        /// Adds play time for a character. AFK time is ignored. Pays once per full interval reached and
        /// appends the paydays to <paramref name="paid"/> (may be null). Returns the number of paydays.
        /// </summary>
        public int AddPlayTime(string characterId, double seconds, bool afk, List<PaydayRecord> paid)
        {
            if (afk || seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return 0;
            }
            FactionMember m = _factions.GetMember(characterId);
            if (m == null)
            {
                _activeSeconds.Remove(characterId);
                return 0;
            }
            _factions.MarkActive(characterId);
            double total = ActiveSeconds(characterId) + seconds;
            int count = 0;
            while (total >= IntervalSeconds)
            {
                total -= IntervalSeconds;
                PaydayRecord rec = Pay(m);
                count++;
                if (paid != null && rec != null)
                {
                    paid.Add(rec);
                }
            }
            _activeSeconds[characterId] = total;
            return count;
        }

        private PaydayRecord Pay(FactionMember m)
        {
            FactionDef def = FactionCatalog.Get(m.Faction);
            long salary = def.Ranks[m.RankLevel].SalaryPerPaydayRupees;
            if (!def.IsGovernment || salary <= 0)
            {
                return new PaydayRecord(m.CharacterId, m.Faction, m.RankLevel, 0, 0, 0);
            }
            string bank = Ledger.Bank(m.CharacterId);
            long fromTreasury = Math.Min(salary, _ledger.GetBalance(Ledger.CityTreasury));
            if (fromTreasury > 0 && _ledger.Transfer(Ledger.CityTreasury, bank, fromTreasury, "payday_salary") != TransferResult.Ok)
            {
                fromTreasury = 0;
            }
            long floor = salary * _floorPercent / 100;
            long topUp = floor > fromTreasury ? floor - fromTreasury : 0;
            if (topUp > 0 && _ledger.Mint(bank, topUp, "payday_floor") != TransferResult.Ok)
            {
                topUp = 0;
            }
            _factions.Audit(m.Faction, "payday", "system", m.CharacterId,
                "treasury=" + fromTreasury + ";floor=" + topUp);
            return new PaydayRecord(m.CharacterId, m.Faction, m.RankLevel, salary, fromTreasury, topUp);
        }
    }
}
