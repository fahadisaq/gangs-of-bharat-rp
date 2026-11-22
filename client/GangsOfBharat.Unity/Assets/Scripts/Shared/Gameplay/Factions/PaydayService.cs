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
        /// <summary>Mass Media ad share paid from the Mass Media treasury (0 for other factions).</summary>
        public readonly long AdShare;

        public PaydayRecord(string characterId, FactionId faction, int rankLevel, long salary, long paidFromTreasury, long paidFromFloor)
            : this(characterId, faction, rankLevel, salary, paidFromTreasury, paidFromFloor, 0)
        {
        }

        public PaydayRecord(string characterId, FactionId faction, int rankLevel, long salary, long paidFromTreasury, long paidFromFloor, long adShare)
        {
            CharacterId = characterId;
            Faction = faction;
            RankLevel = rankLevel;
            Salary = salary;
            PaidFromTreasury = paidFromTreasury;
            PaidFromFloor = paidFromFloor;
            AdShare = adShare;
        }

        public long Total
        {
            get { return PaidFromTreasury + PaidFromFloor + AdShare; }
        }
    }

    /// <summary>Play time for one character in a <see cref="PaydayService.AddPlayTime(IList{PlayTimeEntry}, List{PaydayRecord})"/> batch.</summary>
    public struct PlayTimeEntry
    {
        public string CharacterId;
        public double Seconds;
        public bool Afk;

        public PlayTimeEntry(string characterId, double seconds, bool afk)
        {
            CharacterId = characterId;
            Seconds = seconds;
            Afk = afk;
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
    ///
    /// Mass Media ad share: whoever credits ad income to <c>Ledger.FactionTreasury(FactionId.Media)</c>
    /// calls <see cref="RecordMediaAdIncome"/>. A payday is one call to AddPlayTime (the server passes
    /// every online player's play time for a tick in one batch). When Mass Media members are paid in a
    /// payday, <see cref="MediaAdSharePercent"/> of the ad income recorded since the previous Mass Media
    /// payday (capped by the Mass Media treasury balance) is split equally among those paydays; the
    /// integer remainder stays in the Mass Media treasury.
    /// </summary>
    public sealed class PaydayService
    {
        public const int DefaultFloorPercent = 50;
        /// <summary>Share of Mass Media ad income paid out at payday. DRAFT - owner review.</summary>
        public const int MediaAdSharePercent = 30;

        private readonly FactionService _factions;
        private readonly Ledger _ledger;
        private readonly Dictionary<string, double> _activeSeconds = new Dictionary<string, double>();
        private int _floorPercent = DefaultFloorPercent;
        private long _mediaAdIncome;

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

        /// <summary>Ad income credited to the Mass Media treasury since the previous Mass Media payday.</summary>
        public long MediaAdIncomeSincePayday
        {
            get { return _mediaAdIncome; }
        }

        /// <summary>Records ad income that was credited to the Mass Media treasury (for the payday ad share).</summary>
        public void RecordMediaAdIncome(long rupees)
        {
            if (rupees <= 0)
            {
                throw new ArgumentOutOfRangeException("rupees");
            }
            _mediaAdIncome = rupees > long.MaxValue - _mediaAdIncome ? long.MaxValue : _mediaAdIncome + rupees;
        }

        /// <summary>
        /// Adds play time for a character. AFK time is ignored. Pays once per full interval reached and
        /// appends the paydays to <paramref name="paid"/> (may be null). Returns the number of paydays.
        /// </summary>
        public int AddPlayTime(string characterId, double seconds, bool afk, List<PaydayRecord> paid)
        {
            PlayTimeEntry[] one = new PlayTimeEntry[1];
            one[0] = new PlayTimeEntry(characterId, seconds, afk);
            return AddPlayTime(one, paid);
        }

        /// <summary>
        /// Adds play time for several characters as one payday batch (see the class summary for the Mass
        /// Media ad share). Returns the total number of paydays.
        /// </summary>
        public int AddPlayTime(IList<PlayTimeEntry> entries, List<PaydayRecord> paid)
        {
            if (entries == null)
            {
                throw new ArgumentNullException("entries");
            }
            FactionMember[] members = new FactionMember[entries.Count];
            int[] counts = new int[entries.Count];
            int mediaPaydays = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PlayTimeEntry e = entries[i];
                if (e.Afk || e.Seconds <= 0 || double.IsNaN(e.Seconds) || double.IsInfinity(e.Seconds) || e.CharacterId == null)
                {
                    continue;
                }
                FactionMember m = _factions.GetMember(e.CharacterId);
                if (m == null)
                {
                    _activeSeconds.Remove(e.CharacterId);
                    continue;
                }
                _factions.MarkActive(e.CharacterId);
                double total = ActiveSeconds(e.CharacterId) + e.Seconds;
                int count = 0;
                while (total >= IntervalSeconds)
                {
                    total -= IntervalSeconds;
                    count++;
                }
                _activeSeconds[e.CharacterId] = total;
                members[i] = m;
                counts[i] = count;
                if (m.Faction == FactionId.Media)
                {
                    mediaPaydays += count;
                }
            }

            long sharePerPayday = 0;
            if (mediaPaydays > 0)
            {
                long pool = _mediaAdIncome / 100 * MediaAdSharePercent + _mediaAdIncome % 100 * MediaAdSharePercent / 100;
                pool = Math.Min(pool, _ledger.GetBalance(Ledger.FactionTreasury(FactionId.Media)));
                sharePerPayday = pool / mediaPaydays;
                _mediaAdIncome = 0;
            }

            int paydays = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                for (int k = 0; k < counts[i]; k++)
                {
                    long adShare = members[i].Faction == FactionId.Media ? sharePerPayday : 0;
                    PaydayRecord rec = Pay(members[i], adShare);
                    paydays++;
                    if (paid != null)
                    {
                        paid.Add(rec);
                    }
                }
            }
            return paydays;
        }

        private PaydayRecord Pay(FactionMember m, long adShare)
        {
            FactionDef def = FactionCatalog.Get(m.Faction);
            long salary = def.Ranks[m.RankLevel].SalaryPerPaydayRupees;
            if (!def.IsGovernment || salary <= 0)
            {
                return new PaydayRecord(m.CharacterId, m.Faction, m.RankLevel, 0, 0, 0);
            }
            string bank = Ledger.Bank(m.CharacterId);
            if (adShare > 0 && _ledger.Transfer(Ledger.FactionTreasury(m.Faction), bank, adShare, "payday_ad_share") != TransferResult.Ok)
            {
                adShare = 0;
            }
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
                "treasury=" + fromTreasury + ";floor=" + topUp + (adShare > 0 ? ";ad_share=" + adShare : ""));
            return new PaydayRecord(m.CharacterId, m.Faction, m.RankLevel, salary, fromTreasury, topUp, adShare);
        }
    }
}
