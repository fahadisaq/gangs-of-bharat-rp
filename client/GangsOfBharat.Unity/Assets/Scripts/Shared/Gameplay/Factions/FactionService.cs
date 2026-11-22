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

        internal FactionMember(string characterId, FactionId faction, int rankLevel, DateTime nowUtc)
        {
            CharacterId = characterId;
            Faction = faction;
            RankLevel = rankLevel;
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
    /// Faction membership and hierarchy rules from docs/design/04-factions.md. Rank level 0 is the head;
    /// a larger level is a lower rank. Pure in-memory state; the server persists it.
    /// </summary>
    public sealed class FactionService
    {
        private struct LeaveInfo
        {
            public DateTime LeftUtc;
            public FactionKind Kind;
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

        /// <summary>Admins pick only the head (rank 0) of a faction, for a 30-day term.</summary>
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
            AddMember(def, characterId, 0);
            Audit(faction, "appoint_head", adminId, characterId, "term_days=" + FactionCatalog.HeadTermDays);
            return FactionResult.Ok;
        }

        // ---- hierarchy actions ----

        /// <summary>Adds a character to the actor's faction at a rank strictly below the actor's own.</summary>
        public FactionResult Hire(string actorId, string targetId, int rankLevel)
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
            FactionResult r = CheckCanJoin(def, targetId, rankLevel);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            AddMember(def, targetId, rankLevel);
            Audit(def.Id, "hire", actorId, targetId, "rank=" + def.Ranks[rankLevel].Key);
            return FactionResult.Ok;
        }

        /// <summary>Removes a lower-ranked member. Needs a written reason; opens a 3-day appeal window.</summary>
        public FactionResult Fire(string actorId, string targetId, string reason)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, out actor, out target);
            if (r != FactionResult.Ok)
            {
                return r;
            }
            if (reason == null || reason.Trim().Length == 0)
            {
                return FactionResult.ReasonRequired;
            }
            DateTime now = _clock.UtcNow;
            RemoveMember(target, now);
            _fireRecords[targetId] = new FireRecord(targetId, target.Faction, actorId, reason.Trim(), now,
                now.AddDays(FactionCatalog.FireAppealDays));
            Audit(target.Faction, "fire", actorId, targetId, reason.Trim());
            return FactionResult.Ok;
        }

        /// <summary>Moves a lower-ranked member up one rank, at most to one rank below the actor.</summary>
        public FactionResult Promote(string actorId, string targetId)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, out actor, out target);
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
            string from = def.Ranks[target.RankLevel].Key;
            target.RankLevel = newLevel;
            Audit(def.Id, "promote", actorId, targetId, from + "->" + def.Ranks[newLevel].Key);
            return FactionResult.Ok;
        }

        /// <summary>Moves a lower-ranked member down one rank.</summary>
        public FactionResult Demote(string actorId, string targetId)
        {
            FactionMember actor;
            FactionMember target;
            FactionResult r = CheckActOn(actorId, targetId, out actor, out target);
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
            string from = def.Ranks[target.RankLevel].Key;
            target.RankLevel = newLevel;
            Audit(def.Id, "demote", actorId, targetId, from + "->" + def.Ranks[newLevel].Key);
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
            RemoveMember(m, _clock.UtcNow);
            Audit(m.Faction, "leave", characterId, characterId, null);
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
                RemoveMember(stale[i], now);
                Audit(stale[i].Faction, "auto_remove_inactive", "system", stale[i].CharacterId, null);
                removed.Add(stale[i].CharacterId);
            }
            return removed;
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

        private FactionResult CheckActOn(string actorId, string targetId, out FactionMember actor, out FactionMember target)
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

        private void AddMember(FactionDef def, string characterId, int rankLevel)
        {
            _members[characterId] = new FactionMember(characterId, def.Id, rankLevel, _clock.UtcNow);
        }

        private void RemoveMember(FactionMember m, DateTime now)
        {
            m.OnDuty = false;
            m.HasGear = false;
            _members.Remove(m.CharacterId);
            _lastLeave[m.CharacterId] = new LeaveInfo { LeftUtc = now, Kind = FactionCatalog.Get(m.Faction).Kind };
        }

        internal void Audit(FactionId faction, string action, string actorId, string targetId, string detail)
        {
            _audit.Add(new FactionAuditEntry(_clock.UtcNow, faction, action, actorId, targetId, detail));
        }
    }
}
