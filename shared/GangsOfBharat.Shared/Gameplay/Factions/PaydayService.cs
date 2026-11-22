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
    /// calls <see cref="RecordMediaAdIncome"/>. <see cref="MediaAdSharePercent"/> of that income is split
    /// equally among the Mass Media members at that moment and held per member; the integer remainder is
    /// kept for the next split. Each member receives what they are owed at their own payday (paydays are
    /// per-player 30-minute timers, so there is no shared payday event). If the Mass Media treasury is
    /// short, the member gets what the treasury holds and the rest stays owed. A member who leaves Mass
    /// Media forfeits the unpaid share; it goes back into the next split.
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
        private readonly Dictionary<string, long> _mediaOwed = new Dictionary<string, long>();
        private long _mediaIncomeTotal;
        private long _mediaPoolCreated;
        private long _mediaUnallocated;

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

        /// <summary>Ad share owed to a Mass Media member and not yet paid (paid at their next payday).</summary>
        public long MediaAdShareOwed(string characterId)
        {
            long v;
            return characterId != null && _mediaOwed.TryGetValue(characterId, out v) ? v : 0;
        }

        /// <summary>Ad share not yet split: the integer remainder, forfeited shares, or income while Mass Media had no members.</summary>
        public long MediaAdShareUnallocated
        {
            get { return _mediaUnallocated; }
        }

        /// <summary>
        /// Records ad income that was credited to the Mass Media treasury and splits
        /// <see cref="MediaAdSharePercent"/> of it equally among the current Mass Media members.
        /// </summary>
        public void RecordMediaAdIncome(long rupees)
        {
            if (rupees <= 0)
            {
                throw new ArgumentOutOfRangeException("rupees");
            }
            _mediaIncomeTotal = rupees > long.MaxValue - _mediaIncomeTotal ? long.MaxValue : _mediaIncomeTotal + rupees;
            // 30% of the running total, so rounding never loses or invents rupees across many small ads.
            long target = _mediaIncomeTotal / 100 * MediaAdSharePercent + _mediaIncomeTotal % 100 * MediaAdSharePercent / 100;
            _mediaUnallocated += target - _mediaPoolCreated;
            _mediaPoolCreated = target;

            List<string> media = new List<string>();
            foreach (FactionMember m in _factions.GetMembers())
            {
                if (m.Faction == FactionId.Media)
                {
                    media.Add(m.CharacterId);
                }
            }
            ReclaimForfeitedShares();
            if (media.Count == 0)
            {
                return;
            }
            long each = _mediaUnallocated / media.Count;
            if (each <= 0)
            {
                return;
            }
            for (int i = 0; i < media.Count; i++)
            {
                _mediaOwed[media[i]] = MediaAdShareOwed(media[i]) + each;
            }
            _mediaUnallocated -= each * media.Count;
        }

        private void ReclaimForfeitedShares()
        {
            List<string> gone = null;
            foreach (KeyValuePair<string, long> kv in _mediaOwed)
            {
                FactionMember m = _factions.GetMember(kv.Key);
                if (m == null || m.Faction != FactionId.Media)
                {
                    if (gone == null)
                    {
                        gone = new List<string>();
                    }
                    gone.Add(kv.Key);
                }
            }
            if (gone == null)
            {
                return;
            }
            for (int i = 0; i < gone.Count; i++)
            {
                _mediaUnallocated += _mediaOwed[gone[i]];
                _mediaOwed.Remove(gone[i]);
            }
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

        /// <summary>Adds play time for several characters (one server tick). Returns the total number of paydays.</summary>
        public int AddPlayTime(IList<PlayTimeEntry> entries, List<PaydayRecord> paid)
        {
            if (entries == null)
            {
                throw new ArgumentNullException("entries");
            }
            int paydays = 0;
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
                for (int k = 0; k < count; k++)
                {
                    PaydayRecord rec = Pay(m);
                    paydays++;
                    if (paid != null)
                    {
                        paid.Add(rec);
                    }
                }
            }
            return paydays;
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
            long adShare = 0;
            if (m.Faction == FactionId.Media)
            {
                string mediaTreasury = Ledger.FactionTreasury(FactionId.Media);
                adShare = Math.Min(MediaAdShareOwed(m.CharacterId), _ledger.GetBalance(mediaTreasury));
                if (adShare > 0 && _ledger.Transfer(mediaTreasury, bank, adShare, "payday_ad_share") != TransferResult.Ok)
                {
                    adShare = 0;
                }
                if (adShare > 0)
                {
                    // Only what was actually paid leaves the owed balance; a short treasury carries the rest over.
                    _mediaOwed[m.CharacterId] = MediaAdShareOwed(m.CharacterId) - adShare;
                }
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
