using System;
using System.Collections.Generic;

namespace GangsOfBharat.Shared.Gameplay.Factions
{
    public enum FactionResult : byte
    {
        Ok = 0,
        UnknownFaction = 1,
        ActorNotMember = 2,
        TargetNotMember = 3,
        DifferentFaction = 4,
        /// <summary>The target is of equal or higher rank than the actor, or the new rank is too high.</summary>
        InsufficientRank = 5,
        InvalidRank = 6,
        RankFull = 7,
        FactionFull = 8,
        AlreadyInFaction = 9,
        Cooldown = 10,
        ReasonRequired = 11,
        NoLocker = 12,
        NotAtLocker = 13,
        AlreadyOnDuty = 14,
        NotOnDuty = 15,
        NoFireRecord = 16,
        AppealWindowClosed = 17,
        AlreadyAppealed = 18,
        InvalidArgument = 19,
        /// <summary>The target (or the chosen superior) is outside the actor's unit.</summary>
        NotInUnit = 20,
        /// <summary>The actor does not hold the power the action needs.</summary>
        MissingPower = 21,
        /// <summary>The power does not exist in this faction, or can never be given (manage_permissions).</summary>
        PowerNotAllowed = 22,
        /// <summary>The spend would go over the remaining daily treasury limit.</summary>
        LimitExceeded = 23,
        /// <summary>
        /// The change would put a member at or above the rank of their superior (promotion) or put a direct
        /// report at or above the member's new rank (demotion). Reassign first.
        /// </summary>
        ReassignRequired = 24,
    }

    public sealed class FactionMember
    {
        public readonly string CharacterId;
        public readonly FactionId Faction;
        public int RankLevel { get; internal set; }
        public DateTime JoinedUtc { get; internal set; }
        public DateTime LastActiveUtc { get; internal set; }
        public bool OnDuty { get; internal set; }
        /// <summary>Faction gear, uniform and weapons. Only ever true while on duty.</summary>
        public bool HasGear { get; internal set; }
        /// <summary>
        /// Direct superior in the chain of command. Null for the head, and for the head's former reports
        /// while the faction waits for admins to appoint a new head.
        /// </summary>
        public string SuperiorId { get; internal set; }

        internal FactionMember(string characterId, FactionId faction, int rankLevel, string superiorId, DateTime nowUtc)
        {
            CharacterId = characterId;
            Faction = faction;
            RankLevel = rankLevel;
            SuperiorId = superiorId;
            JoinedUtc = nowUtc;
            LastActiveUtc = nowUtc;
        }
    }

    public sealed class FireRecord
    {
        public readonly string CharacterId;
        public readonly FactionId Faction;
        public readonly string ActorId;
        public readonly string Reason;
        public readonly DateTime FiredUtc;
        public readonly DateTime AppealDeadlineUtc;
        public bool Appealed { get; internal set; }
        public string AppealText { get; internal set; }

        internal FireRecord(string characterId, FactionId faction, string actorId, string reason, DateTime firedUtc, DateTime deadlineUtc)
        {
            CharacterId = characterId;
            Faction = faction;
            ActorId = actorId;
            Reason = reason;
            FiredUtc = firedUtc;
            AppealDeadlineUtc = deadlineUtc;
        }
    }

    public sealed class FactionAuditEntry
    {
        public readonly DateTime TimeUtc;
        public readonly FactionId Faction;
        public readonly string Action;
        public readonly string ActorId;
        public readonly string TargetId;
        public readonly string Detail;

        public FactionAuditEntry(DateTime timeUtc, FactionId faction, string action, string actorId, string targetId, string detail)
        {
            TimeUtc = timeUtc;
            Faction = faction;
            Action = action;
            ActorId = actorId;
            TargetId = targetId;
            Detail = detail;
        }
    }

    /// <summary>
    /// Faction membership, chain of command and permission rules from docs/design/04-factions.md. Rank
    /// level 0 is the head; a larger level is a lower rank. Every member except the head reports to one
    /// superior; a leader acts only on lower ranks inside their own unit (subtree) and only with the
    /// matching power. Pure in-memory state; the server persists it.
    /// </summary>
    public sealed class FactionService
    {
        private struct LeaveInfo
        {
            public DateTime LeftUtc;
            public FactionKind Kind;
        }

        private struct SpendDay
        {
            public long Day;
            public long Rupees;
        }

        private struct Locker
        {
            public float X;
            public float Z;
            public float Radius;
        }

        private readonly IClock _clock;
        private readonly Dictionary<string, FactionMember> _members = new Dictionary<string, FactionMember>();
        private readonly Dictionary<string, LeaveInfo> _lastLeave = new Dictionary<string, LeaveInfo>();
        private readonly Dictionary<string, FireRecord> _fireRecords = new Dictionary<string, FireRecord>();
        private readonly Dictionary<FactionId, Locker> _lockers = new Dictionary<FactionId, Locker>();
        private readonly List<FactionAuditEntry> _audit = new List<FactionAuditEntry>();
        // Permissions: per-faction rank powers and rank limits (created from the defaults on first use),
        // personal grants and limits, and spend so far in the current IST day.
        private readonly Dictionary<FactionId, FactionPower[]> _rankPowers = new Dictionary<FactionId, FactionPower[]>();
        private readonly Dictionary<FactionId, long[]> _rankLimits = new Dictionary<FactionId, long[]>();
        private readonly Dictionary<string, FactionPower> _personPowers = new Dictionary<string, FactionPower>();
        private readonly Dictionary<string, long> _personLimits = new Dictionary<string, long>();
        private readonly Dictionary<string, SpendDay> _spentToday = new Dictionary<string, SpendDay>();

        public FactionService(IClock clock)
        {
            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }
            _clock = clock;
        }

        public IClock Clock
        {
            get { return _clock; }
        }

        // ---- queries ----

        public FactionMember GetMember(string characterId)
        {
            FactionMember m;
            return characterId != null && _members.TryGetValue(characterId, out m) ? m : null;
        }

        public int MemberCount(FactionId faction)
        {
            int n = 0;
            foreach (FactionMember m in _members.Values)
            {
                if (m.Faction == faction)
                {
                    n++;
                }
            }
            return n;
        }

        public int RankCount(FactionId faction, int rankLevel)
        {
            int n = 0;
            foreach (FactionMember m in _members.Values)
            {
                if (m.Faction == faction && m.RankLevel == rankLevel)
                {
                    n++;
                }
            }
            return n;
        }

        public List<FactionMember> GetMembers()
        {
            return new List<FactionMember>(_members.Values);
        }

        public FireRecord GetFireRecord(string characterId)
        {
            FireRecord r;
            return characterId != null && _fireRecords.TryGetValue(characterId, out r) ? r : null;
        }

        public List<FactionAuditEntry> GetAuditLog()
        {
            return new List<FactionAuditEntry>(_audit);
        }

        public bool IsOnDuty(string characterId)
        {
            FactionMember m = GetMember(characterId);
            return m != null && m.OnDuty;
        }

        /// <summary>Earliest time the character may join the given faction (cooldown after leaving).</summary>
        public DateTime JoinAllowedFromUtc(string characterId, FactionId target)
        {
            LeaveInfo info;
            if (!_lastLeave.TryGetValue(characterId, out info))
            {
                return DateTime.MinValue;
            }
            bool criminalToLaw = (info.Kind == FactionKind.Gang || info.Kind == FactionKind.Mafia) &&
                (target == FactionId.Police || target == FactionId.Army);
            int days = criminalToLaw ? FactionCatalog.GangToPoliceOrArmyCooldownDays : FactionCatalog.LeaveCooldownDays;
            return info.LeftUtc.AddDays(days);
        }

        // ---- admin ----

        /// <summary>
        /// Admins pick only the head (rank 0) of a faction, for a 30-day term. Returns RankFull while the
        /// faction has a head. Members left without a superior (the old head's reports) attach to the new head.
        /// </summary>
        public FactionResult AppointHead(string adminId, FactionId faction, string characterId)
        {
            FactionDef def = FactionCatalog.Get(faction);
            if (def == null)
            {
                return FactionResult.UnknownFaction;
            }
            if (string.IsNullOrEmpty(adminId) || string.IsNullOrEmpty(characterId))
            {
                return FactionResult.InvalidArgument;
            }
            FactionResult r = CheckCanJoin(def, characterId, 0);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            AddMember(def, characterId, 0, null);
            Audit(faction, "appoint_head", adminId, characterId, "term_days=" + FactionCatalog.HeadTermDays);
            List<FactionMember> orphans = new List<FactionMember>();
            foreach (FactionMember m in _members.Values)
            {
                if (m.Faction == faction && m.RankLevel > 0 && m.SuperiorId == null)
                {
                    orphans.Add(m);
                }
            }
            for (int i = 0; i < orphans.Count; i++)
            {
                orphans[i].SuperiorId = characterId;
                Audit(faction, "attach_to_head", "system", orphans[i].CharacterId, "superior=" + characterId);
            }
            return FactionResult.Ok;
        }

        // ---- chain of command ----

        /// <summary>
        /// True when <paramref name="memberId"/> is inside the leader's unit: a strict descendant through
        /// <see cref="FactionMember.SuperiorId"/>. The head's unit is the whole faction (except the head).
        /// </summary>
        public bool IsInUnit(string leaderId, string memberId)
        {
            FactionMember leader = GetMember(leaderId);
            FactionMember member = GetMember(memberId);
            if (leader == null || member == null || leader == member || leader.Faction != member.Faction)
            {
                return false;
            }
            if (leader.RankLevel == 0)
            {
                return true;
            }
            // Ranks strictly decrease up the chain, so the walk ends; the step cap is a safety net.
            int steps = 0;
            string up = member.SuperiorId;
            while (up != null && steps++ <= _members.Count)
            {
                if (up == leaderId)
                {
                    return true;
                }
                FactionMember next = GetMember(up);
                up = next != null ? next.SuperiorId : null;
            }
            return false;
        }

        /// <summary>Everyone inside the leader's unit (their subtree). Empty if the leader is not a member.</summary>
        public List<FactionMember> GetUnit(string leaderId)
        {
            List<FactionMember> unit = new List<FactionMember>();
            FactionMember leader = GetMember(leaderId);
            if (leader == null)
            {
                return unit;
            }
            foreach (FactionMember m in _members.Values)
            {
                if (m.Faction == leader.Faction && IsInUnit(leaderId, m.CharacterId))
                {
                    unit.Add(m);
                }
            }
            return unit;
        }

        /// <summary>Members whose direct superior is <paramref name="characterId"/>.</summary>
        public List<FactionMember> GetDirectReports(string characterId)
        {
            List<FactionMember> reports = new List<FactionMember>();
            if (characterId == null)
            {
                return reports;
            }
            foreach (FactionMember m in _members.Values)
            {
                if (m.SuperiorId == characterId)
                {
                    reports.Add(m);
                }
            }
            return reports;
        }

        // ---- hierarchy actions ----

        /// <summary>Hires into the actor's faction, reporting directly to the actor.</summary>
        public FactionResult Hire(string actorId, string targetId, int rankLevel)
        {
            return Hire(actorId, targetId, rankLevel, null);
        }

        /// <summary>
        /// Hires into the actor's faction at a rank strictly below the actor's own. The new member reports
        /// to <paramref name="superiorId"/> (the actor when null), which must be the actor or a member of
        /// the actor's unit with a higher rank than the new rank. Needs <see cref="FactionPower.Recruit"/>.
        /// </summary>
        public FactionResult Hire(string actorId, string targetId, int rankLevel, string superiorId)
        {
            FactionMember actor = GetMember(actorId);
            if (actor == null)
            {
                return FactionResult.ActorNotMember;
            }
            if (string.IsNullOrEmpty(targetId) || targetId == actorId)
            {
                return FactionResult.InvalidArgument;
            }
            FactionDef def = FactionCatalog.Get(actor.Faction);
            if (rankLevel < 0 || rankLevel >= def.Ranks.Length)
            {
                return FactionResult.InvalidRank;
            }
            if (rankLevel <= actor.RankLevel)
            {
                return FactionResult.InsufficientRank;
            }
            FactionMember superior = actor;
            if (superiorId != null && superiorId != actorId)
            {
                superior = GetMember(superiorId);
                if (superior == null)
                {
                    return FactionResult.TargetNotMember;
                }
                if (superior.Faction != actor.Faction)
                {
                    return FactionResult.DifferentFaction;
                }
                if (!IsInUnit(actorId, superiorId))
                {
                    return FactionResult.NotInUnit;
                }
                if (rankLevel <= superior.RankLevel)
                {
                    return FactionResult.InsufficientRank;
                }
            }
            if (!HasPower(actorId, FactionPower.Recruit))
            {
                return FactionResult.MissingPower;
            }
            FactionResult r = CheckCanJoin(def, targetId, rankLevel);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            AddMember(def, targetId, rankLevel, superior.CharacterId);
            Audit(def.Id, "hire", actorId, targetId, "rank=" + def.Ranks[rankLevel].Key + ";superior=" + superior.CharacterId);
            return FactionResult.Ok;
        }

        /// <summary>
        /// Removes a lower-ranked member of the actor's unit. Needs <see cref="FactionPower.Fire"/> and a
        /// written reason; opens a 3-day appeal window. The member's direct reports move up to their superior.
        /// </summary>
        public FactionResult Fire(string actorId, string targetId, string reason)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, FactionPower.Fire, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (reason == null || reason.Trim().Length == 0)
            {
                return FactionResult.ReasonRequired;
            }
            DateTime now = _clock.UtcNow;
            _fireRecords[targetId] = new FireRecord(targetId, target.Faction, actorId, reason.Trim(), now,
                now.AddDays(FactionCatalog.FireAppealDays));
            Audit(target.Faction, "fire", actorId, targetId, reason.Trim());
            RemoveMember(target, now);
            return FactionResult.Ok;
        }

        /// <summary>
        /// Moves a member of the actor's unit up one rank, at most to one rank below the actor. The member
        /// keeps their superior, so the promotion is refused with <see cref="FactionResult.ReassignRequired"/>
        /// if it would reach the superior's rank. Needs <see cref="FactionPower.Promote"/>.
        /// </summary>
        public FactionResult Promote(string actorId, string targetId)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, FactionPower.Promote, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            int newLevel = target.RankLevel - 1;
            if (newLevel <= actor.RankLevel)
            {
                return FactionResult.InsufficientRank;
            }
            FactionDef def = FactionCatalog.Get(target.Faction);
            if (RankCount(def.Id, newLevel) >= def.Ranks[newLevel].Count)
            {
                return FactionResult.RankFull;
            }
            FactionMember superior = GetMember(target.SuperiorId);
            if (superior != null && newLevel <= superior.RankLevel)
            {
                return FactionResult.ReassignRequired;
            }
            string from = def.Ranks[target.RankLevel].Key;
            target.RankLevel = newLevel;
            Audit(def.Id, "promote", actorId, targetId, from + "->" + def.Ranks[newLevel].Key);
            return FactionResult.Ok;
        }

        /// <summary>
        /// Moves a member of the actor's unit down one rank. Refused with
        /// <see cref="FactionResult.ReassignRequired"/> while a direct report would end at the same rank.
        /// Needs <see cref="FactionPower.Demote"/>.
        /// </summary>
        public FactionResult Demote(string actorId, string targetId)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, FactionPower.Demote, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            FactionDef def = FactionCatalog.Get(target.Faction);
            int newLevel = target.RankLevel + 1;
            if (newLevel >= def.Ranks.Length)
            {
                return FactionResult.InvalidRank;
            }
            if (RankCount(def.Id, newLevel) >= def.Ranks[newLevel].Count)
            {
                return FactionResult.RankFull;
            }
            List<FactionMember> reports = GetDirectReports(targetId);
            for (int i = 0; i < reports.Count; i++)
            {
                if (reports[i].RankLevel <= newLevel)
                {
                    return FactionResult.ReassignRequired;
                }
            }
            string from = def.Ranks[target.RankLevel].Key;
            target.RankLevel = newLevel;
            Audit(def.Id, "demote", actorId, targetId, from + "->" + def.Ranks[newLevel].Key);
            return FactionResult.Ok;
        }

        /// <summary>
        /// Moves a member of the actor's unit to another sub-unit: the new superior is the actor or a member
        /// of the actor's unit with a higher rank than the moved member. Needs <see cref="FactionPower.ReassignUnit"/>.
        /// </summary>
        public FactionResult ReassignUnit(string actorId, string targetId, string newSuperiorId)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, FactionPower.ReassignUnit, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (string.IsNullOrEmpty(newSuperiorId) || newSuperiorId == targetId)
            {
                return FactionResult.InvalidArgument;
            }
            FactionMember superior = GetMember(newSuperiorId);
            if (superior == null)
            {
                return FactionResult.TargetNotMember;
            }
            if (superior.Faction != actor.Faction)
            {
                return FactionResult.DifferentFaction;
            }
            if (superior != actor && !IsInUnit(actorId, newSuperiorId))
            {
                return FactionResult.NotInUnit;
            }
            if (IsInUnit(targetId, newSuperiorId))
            {
                // Would create a cycle in the chain of command.
                return FactionResult.InvalidArgument;
            }
            if (superior.RankLevel >= target.RankLevel)
            {
                return FactionResult.InsufficientRank;
            }
            string from = target.SuperiorId ?? "none";
            target.SuperiorId = newSuperiorId;
            Audit(actor.Faction, "reassign_unit", actorId, targetId, from + "->" + newSuperiorId);
            return FactionResult.Ok;
        }

        /// <summary>Voluntary leave. Starts the switching cooldown.</summary>
        public FactionResult Leave(string characterId)
        {
            FactionMember m = GetMember(characterId);
            if (m == null)
            {
                return FactionResult.TargetNotMember;
            }
            Audit(m.Faction, "leave", characterId, characterId, null);
            RemoveMember(m, _clock.UtcNow);
            return FactionResult.Ok;
        }

        /// <summary>Fired member appeals (to the head, then admins) within the 3-day window.</summary>
        public FactionResult FileAppeal(string characterId, string text)
        {
            FireRecord rec = GetFireRecord(characterId);
            if (rec == null)
            {
                return FactionResult.NoFireRecord;
            }
            if (rec.Appealed)
            {
                return FactionResult.AlreadyAppealed;
            }
            if (_clock.UtcNow > rec.AppealDeadlineUtc)
            {
                return FactionResult.AppealWindowClosed;
            }
            if (text == null || text.Trim().Length == 0)
            {
                return FactionResult.ReasonRequired;
            }
            rec.Appealed = true;
            rec.AppealText = text.Trim();
            Audit(rec.Faction, "appeal", characterId, rec.ActorId, rec.AppealText);
            return FactionResult.Ok;
        }

        // ---- activity ----

        /// <summary>Records real (non-AFK) activity for the inactivity rule.</summary>
        public void MarkActive(string characterId)
        {
            FactionMember m = GetMember(characterId);
            if (m != null)
            {
                m.LastActiveUtc = _clock.UtcNow;
            }
        }

        /// <summary>Removes members inactive for 7 days or more. Returns the removed character ids.</summary>
        public List<string> RemoveInactive()
        {
            DateTime now = _clock.UtcNow;
            TimeSpan limit = TimeSpan.FromDays(FactionCatalog.InactivityRemovalDays);
            List<FactionMember> stale = new List<FactionMember>();
            foreach (FactionMember m in _members.Values)
            {
                if (now - m.LastActiveUtc >= limit)
                {
                    stale.Add(m);
                }
            }
            List<string> removed = new List<string>(stale.Count);
            for (int i = 0; i < stale.Count; i++)
            {
                Audit(stale[i].Faction, "auto_remove_inactive", "system", stale[i].CharacterId, null);
                RemoveMember(stale[i], now);
                removed.Add(stale[i].CharacterId);
            }
            return removed;
        }

        // ---- permissions ----

        /// <summary>India Standard Time offset. Daily treasury limits reset at 00:00 IST.</summary>
        public static readonly TimeSpan IstOffset = new TimeSpan(5, 30, 0);

        /// <summary>Day number in IST for a UTC time. Changes at 00:00 IST (18:30 UTC).</summary>
        public static long IstDay(DateTime utc)
        {
            return (utc.Ticks + IstOffset.Ticks) / TimeSpan.TicksPerDay;
        }

        /// <summary>Powers of a rank in a faction. Rank 0 (head) always has every power of the faction.</summary>
        public FactionPower GetRankPowers(FactionId faction, int rankLevel)
        {
            FactionDef def = FactionCatalog.Get(faction);
            if (def == null || rankLevel < 0 || rankLevel >= def.Ranks.Length)
            {
                return FactionPower.None;
            }
            if (rankLevel == 0)
            {
                return FactionPowers.AllowedPowers(faction);
            }
            return RankPowersFor(def)[rankLevel];
        }

        /// <summary>Daily spend_treasury limit of a rank in rupees; <see cref="FactionPowers.UnlimitedRupees"/> for the head.</summary>
        public long GetRankTreasuryDailyLimit(FactionId faction, int rankLevel)
        {
            FactionDef def = FactionCatalog.Get(faction);
            if (def == null || rankLevel < 0 || rankLevel >= def.Ranks.Length)
            {
                return 0;
            }
            if (rankLevel == 0)
            {
                return FactionPowers.UnlimitedRupees;
            }
            return RankLimitsFor(def)[rankLevel];
        }

        /// <summary>Powers granted to this person on top of their rank.</summary>
        public FactionPower GetPersonalGrants(string characterId)
        {
            FactionPower p;
            return characterId != null && _personPowers.TryGetValue(characterId, out p) ? p : FactionPower.None;
        }

        /// <summary>Effective powers = rank powers + personal grants. None for non-members.</summary>
        public FactionPower GetEffectivePowers(string characterId)
        {
            FactionMember m = GetMember(characterId);
            if (m == null)
            {
                return FactionPower.None;
            }
            return GetRankPowers(m.Faction, m.RankLevel) | GetPersonalGrants(characterId);
        }

        /// <summary>True when the character holds every power in <paramref name="power"/>.</summary>
        public bool HasPower(string characterId, FactionPower power)
        {
            return power != FactionPower.None && (GetEffectivePowers(characterId) & power) == power;
        }

        /// <summary>
        /// Effective daily spend_treasury limit: the personal limit if set; otherwise the rank limit when the
        /// rank has spend_treasury; otherwise <see cref="FactionPowers.DefaultGrantDailyLimitRupees"/> for a
        /// personal grant; 0 without the power. <see cref="FactionPowers.UnlimitedRupees"/> means no limit.
        /// </summary>
        public long GetTreasuryDailyLimit(string characterId)
        {
            FactionMember m = GetMember(characterId);
            if (m == null || !HasPower(characterId, FactionPower.SpendTreasury))
            {
                return 0;
            }
            long personal;
            if (_personLimits.TryGetValue(characterId, out personal))
            {
                return personal;
            }
            if ((GetRankPowers(m.Faction, m.RankLevel) & FactionPower.SpendTreasury) != 0)
            {
                return GetRankTreasuryDailyLimit(m.Faction, m.RankLevel);
            }
            return FactionPowers.DefaultGrantDailyLimitRupees;
        }

        /// <summary>Rupees authorized for this character since 00:00 IST today.</summary>
        public long GetTreasurySpentToday(string characterId)
        {
            SpendDay d;
            if (characterId == null || !_spentToday.TryGetValue(characterId, out d) || d.Day != IstDay(_clock.UtcNow))
            {
                return 0;
            }
            return d.Rupees;
        }

        /// <summary>Rupees the character may still spend today; <see cref="FactionPowers.UnlimitedRupees"/> for no limit.</summary>
        public long GetTreasuryRemainingToday(string characterId)
        {
            long limit = GetTreasuryDailyLimit(characterId);
            if (limit == FactionPowers.UnlimitedRupees)
            {
                return FactionPowers.UnlimitedRupees;
            }
            long left = limit - GetTreasurySpentToday(characterId);
            return left > 0 ? left : 0;
        }

        /// <summary>Head only: sets the powers of a rank below the head. Audited as perm_rank_set.</summary>
        public FactionResult SetRankPowers(string actorId, int rankLevel, FactionPower powers)
        {
            FactionMember actor;
            FactionResult r = CheckManagePermissions(actorId, out actor);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            FactionDef def = FactionCatalog.Get(actor.Faction);
            if (rankLevel <= 0 || rankLevel >= def.Ranks.Length)
            {
                return FactionResult.InvalidRank;
            }
            r = CheckPowersAllowed(def.Id, powers);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            FactionPower[] ranks = RankPowersFor(def);
            FactionPower old = ranks[rankLevel];
            ranks[rankLevel] = powers;
            Audit(def.Id, "perm_rank_set", actorId, "rank:" + def.Ranks[rankLevel].Key,
                FactionPowers.Keys(old) + "->" + FactionPowers.Keys(powers));
            return FactionResult.Ok;
        }

        /// <summary>Head only: gives one person extra powers on top of their rank. Audited as perm_grant.</summary>
        public FactionResult GrantPower(string actorId, string targetId, FactionPower power)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, FactionPower.ManagePermissions, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (power == FactionPower.None)
            {
                return FactionResult.InvalidArgument;
            }
            r = CheckPowersAllowed(actor.Faction, power);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            FactionPower old = GetPersonalGrants(targetId);
            FactionPower now = old | power;
            _personPowers[targetId] = now;
            Audit(actor.Faction, "perm_grant", actorId, targetId, FactionPowers.Keys(old) + "->" + FactionPowers.Keys(now));
            return FactionResult.Ok;
        }

        /// <summary>
        /// Head only: removes personal grants from one person (rank powers are unchanged). Audited as perm_revoke.
        /// </summary>
        public FactionResult RevokePower(string actorId, string targetId, FactionPower power)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, FactionPower.ManagePermissions, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (power == FactionPower.None)
            {
                return FactionResult.InvalidArgument;
            }
            r = CheckPowersAllowed(actor.Faction, power);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            FactionPower old = GetPersonalGrants(targetId);
            FactionPower now = old & ~power;
            if (now == FactionPower.None)
            {
                _personPowers.Remove(targetId);
            }
            else
            {
                _personPowers[targetId] = now;
            }
            Audit(actor.Faction, "perm_revoke", actorId, targetId, FactionPowers.Keys(old) + "->" + FactionPowers.Keys(now));
            return FactionResult.Ok;
        }

        /// <summary>Head only: sets the daily spend_treasury limit of a rank below the head. Audited as treasury_limit_set.</summary>
        public FactionResult SetTreasuryDailyLimit(string actorId, int rankLevel, long rupees)
        {
            FactionMember actor;
            FactionResult r = CheckManagePermissions(actorId, out actor);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            FactionDef def = FactionCatalog.Get(actor.Faction);
            if (rankLevel <= 0 || rankLevel >= def.Ranks.Length)
            {
                return FactionResult.InvalidRank;
            }
            if (rupees < 0)
            {
                return FactionResult.InvalidArgument;
            }
            long[] limits = RankLimitsFor(def);
            long old = limits[rankLevel];
            limits[rankLevel] = rupees;
            Audit(def.Id, "treasury_limit_set", actorId, "rank:" + def.Ranks[rankLevel].Key,
                FormatLimit(old) + "->" + FormatLimit(rupees));
            return FactionResult.Ok;
        }

        /// <summary>Head only: sets a personal daily limit that replaces the rank limit. Audited as treasury_limit_set.</summary>
        public FactionResult SetPersonTreasuryDailyLimit(string actorId, string targetId, long rupees)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, FactionPower.ManagePermissions, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (rupees < 0)
            {
                return FactionResult.InvalidArgument;
            }
            string old = FormatPersonLimit(targetId);
            _personLimits[targetId] = rupees;
            Audit(actor.Faction, "treasury_limit_set", actorId, targetId, old + "->" + FormatLimit(rupees));
            return FactionResult.Ok;
        }

        /// <summary>Head only: removes a personal daily limit so the rank (or grant default) limit applies again.</summary>
        public FactionResult ClearPersonTreasuryDailyLimit(string actorId, string targetId)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, FactionPower.ManagePermissions, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            string old = FormatPersonLimit(targetId);
            _personLimits.Remove(targetId);
            Audit(actor.Faction, "treasury_limit_set", actorId, targetId, old + "->none");
            return FactionResult.Ok;
        }

        /// <summary>
        /// Checks spend_treasury and the remaining daily limit, then records and audits the spend
        /// (treasury_spend). The caller moves the money from <c>Ledger.FactionTreasury</c> only on Ok.
        /// </summary>
        public FactionResult AuthorizeTreasurySpend(string actorId, long rupees, string reason)
        {
            FactionMember actor = GetMember(actorId);
            if (actor == null)
            {
                return FactionResult.ActorNotMember;
            }
            if (rupees <= 0)
            {
                return FactionResult.InvalidArgument;
            }
            if (reason == null || reason.Trim().Length == 0)
            {
                return FactionResult.ReasonRequired;
            }
            if (!HasPower(actorId, FactionPower.SpendTreasury))
            {
                return FactionResult.MissingPower;
            }
            long remaining = GetTreasuryRemainingToday(actorId);
            if (remaining != FactionPowers.UnlimitedRupees && rupees > remaining)
            {
                return FactionResult.LimitExceeded;
            }
            SpendDay d;
            d.Day = IstDay(_clock.UtcNow);
            d.Rupees = GetTreasurySpentToday(actorId) + rupees;
            _spentToday[actorId] = d;
            long after = GetTreasuryRemainingToday(actorId);
            Audit(actor.Faction, "treasury_spend", actorId, null,
                "amount=" + rupees + ";reason=" + reason.Trim() + ";remaining=" + FormatLimit(after));
            return FactionResult.Ok;
        }

        /// <summary>Ok when the actor holds mute_faction_chat and the target is a lower rank inside their unit.</summary>
        public FactionResult CanMuteInFactionChat(string actorId, string targetId)
        {
            FactionMember actor;
            FactionMember target;
            return CheckActOn(actorId, targetId, FactionPower.MuteFactionChat, out actor, out target);
        }

        /// <summary>Checks <see cref="CanMuteInFactionChat"/> and audits the mute (faction_mute). Chat applies the mute.</summary>
        public FactionResult MuteInFactionChat(string actorId, string targetId, int minutes)
        {
            FactionResult r = CanMuteInFactionChat(actorId, targetId);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (minutes <= 0)
            {
                return FactionResult.InvalidArgument;
            }
            Audit(GetMember(actorId).Faction, "faction_mute", actorId, targetId, "minutes=" + minutes);
            return FactionResult.Ok;
        }

        private FactionResult CheckManagePermissions(string actorId, out FactionMember actor)
        {
            actor = GetMember(actorId);
            if (actor == null)
            {
                return FactionResult.ActorNotMember;
            }
            return HasPower(actorId, FactionPower.ManagePermissions) ? FactionResult.Ok : FactionResult.MissingPower;
        }

        private static FactionResult CheckPowersAllowed(FactionId faction, FactionPower powers)
        {
            if ((powers & FactionPower.ManagePermissions) != 0)
            {
                return FactionResult.PowerNotAllowed;
            }
            if ((powers & ~FactionPowers.AllowedPowers(faction)) != 0)
            {
                return FactionResult.PowerNotAllowed;
            }
            return FactionResult.Ok;
        }

        private FactionPower[] RankPowersFor(FactionDef def)
        {
            FactionPower[] ranks;
            if (!_rankPowers.TryGetValue(def.Id, out ranks))
            {
                ranks = new FactionPower[def.Ranks.Length];
                for (int i = 0; i < ranks.Length; i++)
                {
                    ranks[i] = FactionPowers.DefaultRankPowers(def, i);
                }
                _rankPowers[def.Id] = ranks;
            }
            return ranks;
        }

        private long[] RankLimitsFor(FactionDef def)
        {
            long[] limits;
            if (!_rankLimits.TryGetValue(def.Id, out limits))
            {
                limits = new long[def.Ranks.Length];
                for (int i = 0; i < limits.Length; i++)
                {
                    limits[i] = FactionPowers.DefaultRankDailyLimit(i);
                }
                _rankLimits[def.Id] = limits;
            }
            return limits;
        }

        private string FormatPersonLimit(string characterId)
        {
            long v;
            return _personLimits.TryGetValue(characterId, out v) ? FormatLimit(v) : "none";
        }

        private static string FormatLimit(long rupees)
        {
            return rupees == FactionPowers.UnlimitedRupees
                ? "unlimited"
                : rupees.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---- duty ----

        /// <summary>Configures the base locker in a faction HQ where duty starts and ends.</summary>
        public void SetLocker(FactionId faction, float x, float z, float radius)
        {
            if (radius <= 0f || float.IsNaN(radius) || float.IsNaN(x) || float.IsNaN(z))
            {
                throw new ArgumentOutOfRangeException("radius");
            }
            _lockers[faction] = new Locker { X = x, Z = z, Radius = radius };
        }

        public FactionResult StartDuty(string characterId, float x, float z)
        {
            FactionMember m = GetMember(characterId);
            if (m == null)
            {
                return FactionResult.ActorNotMember;
            }
            FactionResult r = CheckAtLocker(m.Faction, x, z);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (m.OnDuty)
            {
                return FactionResult.AlreadyOnDuty;
            }
            m.OnDuty = true;
            m.HasGear = true;
            Audit(m.Faction, "duty_start", characterId, characterId, "gear_issued");
            return FactionResult.Ok;
        }

        /// <summary>Ends duty at the locker and returns gear to it.</summary>
        public FactionResult EndDuty(string characterId, float x, float z)
        {
            FactionMember m = GetMember(characterId);
            if (m == null)
            {
                return FactionResult.ActorNotMember;
            }
            FactionResult r = CheckAtLocker(m.Faction, x, z);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (!m.OnDuty)
            {
                return FactionResult.NotOnDuty;
            }
            m.OnDuty = false;
            m.HasGear = false;
            Audit(m.Faction, "duty_end", characterId, characterId, "gear_returned");
            return FactionResult.Ok;
        }

        // ---- internals ----

        private FactionResult CheckAtLocker(FactionId faction, float x, float z)
        {
            Locker l;
            if (!_lockers.TryGetValue(faction, out l))
            {
                return FactionResult.NoLocker;
            }
            float dx = x - l.X;
            float dz = z - l.Z;
            if (float.IsNaN(dx) || float.IsNaN(dz) || dx * dx + dz * dz > l.Radius * l.Radius)
            {
                return FactionResult.NotAtLocker;
            }
            return FactionResult.Ok;
        }

        /// <summary>
        /// Shared check for acting on another member: same faction, target strictly lower rank, target inside
        /// the actor's unit, and the actor holds <paramref name="power"/> (checked in that order).
        /// </summary>
        private FactionResult CheckActOn(string actorId, string targetId, FactionPower power, out FactionMember actor, out FactionMember target)
        {
            actor = GetMember(actorId);
            target = null;
            if (actor == null)
            {
                return FactionResult.ActorNotMember;
            }
            target = GetMember(targetId);
            if (target == null)
            {
                return FactionResult.TargetNotMember;
            }
            if (target.Faction != actor.Faction)
            {
                return FactionResult.DifferentFaction;
            }
            if (target.RankLevel <= actor.RankLevel)
            {
                return FactionResult.InsufficientRank;
            }
            if (!IsInUnit(actorId, targetId))
            {
                return FactionResult.NotInUnit;
            }
            if (power != FactionPower.None && !HasPower(actorId, power))
            {
                return FactionResult.MissingPower;
            }
            return FactionResult.Ok;
        }

        private FactionResult CheckCanJoin(FactionDef def, string characterId, int rankLevel)
        {
            if (_members.ContainsKey(characterId))
            {
                return FactionResult.AlreadyInFaction;
            }
            if (_clock.UtcNow < JoinAllowedFromUtc(characterId, def.Id))
            {
                return FactionResult.Cooldown;
            }
            if (MemberCount(def.Id) >= def.SlotCap)
            {
                return FactionResult.FactionFull;
            }
            if (RankCount(def.Id, rankLevel) >= def.Ranks[rankLevel].Count)
            {
                return FactionResult.RankFull;
            }
            return FactionResult.Ok;
        }

        private void AddMember(FactionDef def, string characterId, int rankLevel, string superiorId)
        {
            _members[characterId] = new FactionMember(characterId, def.Id, rankLevel, superiorId, _clock.UtcNow);
        }

        private void RemoveMember(FactionMember m, DateTime now)
        {
            m.OnDuty = false;
            m.HasGear = false;
            _members.Remove(m.CharacterId);
            // Personal grants, personal limits and today's spend end with membership.
            _personPowers.Remove(m.CharacterId);
            _personLimits.Remove(m.CharacterId);
            _spentToday.Remove(m.CharacterId);
            // Direct reports move up to the removed member's superior (none if the head left).
            List<FactionMember> reports = GetDirectReports(m.CharacterId);
            for (int i = 0; i < reports.Count; i++)
            {
                reports[i].SuperiorId = m.SuperiorId;
                Audit(m.Faction, "reassign_on_remove", "system", reports[i].CharacterId,
                    m.CharacterId + "->" + (m.SuperiorId ?? "none"));
            }
            _lastLeave[m.CharacterId] = new LeaveInfo { LeftUtc = now, Kind = FactionCatalog.Get(m.Faction).Kind };
        }

        internal void Audit(FactionId faction, string action, string actorId, string targetId, string detail)
        {
            _audit.Add(new FactionAuditEntry(_clock.UtcNow, faction, action, actorId, targetId, detail));
        }
    }
}
