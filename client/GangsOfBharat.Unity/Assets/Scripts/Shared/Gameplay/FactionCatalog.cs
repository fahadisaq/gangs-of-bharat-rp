using System.Collections.Generic;

namespace GangsOfBharat.Shared.Gameplay
{
    /// <summary>Faction ids fit in 4 bits on the wire.</summary>
    public enum FactionId : byte
    {
        None = 0,
        Police = 1,
        Army = 2,
        Hospital = 3,
        Fire = 4,
        Government = 5,
        News = 6,
        GangSlum = 7,
        GangOldCity = 8,
        GangPort = 9,
        GangOutskirts = 10,
        MafiaFamily1 = 11,
        MafiaFamily2 = 12,
        Court = 13,
    }

    public enum FactionKind : byte
    {
        /// <summary>Paid from the city treasury on payday.</summary>
        Government = 0,
        /// <summary>No salary; earns from activities.</summary>
        Gang = 1,
        Mafia = 2,
    }

    public sealed class RankDef
    {
        /// <summary>0 = top rank.</summary>
        public readonly int Level;
        public readonly string Key;
        public readonly string Name;
        public readonly int Count;
        /// <summary>Rupees per payday (every 30 minutes of active play). 0 for gangs and mafia.</summary>
        public readonly long SalaryPerPaydayRupees;

        public RankDef(int level, string key, string name, int count, long salaryPerPaydayRupees)
        {
            Level = level;
            Key = key;
            Name = name;
            Count = count;
            SalaryPerPaydayRupees = salaryPerPaydayRupees;
        }
    }

    public sealed class FactionDef
    {
        public readonly FactionId Id;
        public readonly string Key;
        /// <summary>Localization key for the display name.</summary>
        public readonly string NameKey;
        public readonly FactionKind Kind;
        public readonly int SlotCap;
        public readonly RankDef[] Ranks;

        public FactionDef(FactionId id, string key, FactionKind kind, int slotCap, RankDef[] ranks)
        {
            Id = id;
            Key = key;
            NameKey = "faction." + key;
            Kind = kind;
            SlotCap = slotCap;
            Ranks = ranks;
        }

        public bool IsGovernment
        {
            get { return Kind == FactionKind.Government; }
        }

        public int RankSeatTotal
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Ranks.Length; i++)
                {
                    total += Ranks[i].Count;
                }
                return total;
            }
        }
    }

    /// <summary>
    /// Faction slots, ranks and salaries for a 1000-player server, from docs/design/04-factions.md.
    /// Civilians are the remainder (1000 - 480 = 520).
    /// </summary>
    public static class FactionCatalog
    {
        public const int ServerPlayerCap = 1000;
        public const int PaydayIntervalMinutes = 30;
        public const int LeaveCooldownDays = 7;
        public const int GangToPoliceOrArmyCooldownDays = 14;
        public const int InactivityRemovalDays = 7;
        public const int HeadTermDays = 30;
        public const int FireAppealDays = 3;

        private static RankDef R(int level, string key, string name, int count, long salary)
        {
            return new RankDef(level, key, name, count, salary);
        }

        private static RankDef[] GangRanks()
        {
            return new[]
            {
                R(0, "bhai", "Bhai (boss)", 1, 0),
                R(1, "right_hand", "Right Hand", 1, 0),
                R(2, "area_commander", "Area Commander", 4, 0),
                R(3, "shooter", "Shooter", 10, 0),
                R(4, "member", "Member", 14, 0),
                R(5, "recruit", "Recruit", 10, 0),
            };
        }

        private static RankDef[] MafiaRanks()
        {
            return new[]
            {
                R(0, "don", "Don", 1, 0),
                R(1, "consigliere", "Consigliere", 1, 0),
                R(2, "captain", "Captain", 3, 0),
                R(3, "soldier", "Soldier", 10, 0),
                R(4, "associate", "Associate", 15, 0),
            };
        }

        public static readonly FactionDef[] All = new[]
        {
            new FactionDef(FactionId.Police, "police", FactionKind.Government, 80, new[]
            {
                R(0, "commissioner", "Commissioner", 1, 12000),
                R(1, "dcp", "DCP", 2, 8000),
                R(2, "acp", "ACP", 4, 6000),
                R(3, "inspector", "Inspector", 8, 4500),
                R(4, "si", "SI (Sub-Inspector)", 12, 3500),
                R(5, "head_constable", "Head Constable", 15, 2500),
                R(6, "constable", "Constable", 25, 2000),
                R(7, "trainee", "Trainee", 13, 1500),
            }),
            new FactionDef(FactionId.Army, "army", FactionKind.Government, 60, new[]
            {
                R(0, "general", "General", 1, 12000),
                R(1, "colonel", "Colonel", 2, 8500),
                R(2, "major", "Major", 3, 6500),
                R(3, "captain", "Captain", 5, 4800),
                R(4, "lieutenant", "Lieutenant", 7, 3800),
                R(5, "sergeant", "Sergeant", 10, 2800),
                R(6, "soldier", "Soldier", 20, 2200),
                R(7, "recruit", "Recruit", 12, 1500),
            }),
            new FactionDef(FactionId.Hospital, "hospital", FactionKind.Government, 40, new[]
            {
                R(0, "cmo", "CMO (Chief Medical Officer)", 1, 11000),
                R(1, "senior_doctor", "Senior Doctor", 4, 6500),
                R(2, "doctor", "Doctor", 8, 4500),
                R(3, "nurse", "Nurse", 10, 2800),
                R(4, "paramedic", "Paramedic", 12, 2200),
                R(5, "intern", "Intern", 5, 1500),
            }),
            new FactionDef(FactionId.Fire, "fire", FactionKind.Government, 20, new[]
            {
                R(0, "chief", "Chief", 1, 9000),
                R(1, "station_officer", "Station Officer", 3, 5000),
                R(2, "firefighter", "Firefighter", 10, 2800),
                R(3, "trainee", "Trainee", 6, 1500),
            }),
            new FactionDef(FactionId.Government, "government", FactionKind.Government, 25, new[]
            {
                R(0, "mayor", "Mayor (elected every 30 days)", 1, 13000),
                R(1, "deputy_mayor", "Deputy Mayor", 1, 9000),
                R(2, "minister", "Minister", 3, 7000),
                R(3, "officer", "Officer", 8, 2800),
                R(4, "security", "Security", 12, 2000),
            }),
            new FactionDef(FactionId.News, "news", FactionKind.Government, 20, new[]
            {
                R(0, "editor", "Editor", 1, 7000),
                R(1, "senior_reporter", "Senior Reporter", 3, 4000),
                R(2, "reporter", "Reporter", 6, 2800),
                R(3, "cameraman", "Cameraman", 5, 2000),
                R(4, "intern", "Intern", 5, 1200),
            }),
            // Gang names: DRAFT - owner review (one gang per city area).
            new FactionDef(FactionId.GangSlum, "gang_slum", FactionKind.Gang, 40, GangRanks()),
            new FactionDef(FactionId.GangOldCity, "gang_old_city", FactionKind.Gang, 40, GangRanks()),
            new FactionDef(FactionId.GangPort, "gang_port", FactionKind.Gang, 40, GangRanks()),
            new FactionDef(FactionId.GangOutskirts, "gang_outskirts", FactionKind.Gang, 40, GangRanks()),
            new FactionDef(FactionId.MafiaFamily1, "mafia_1", FactionKind.Mafia, 30, MafiaRanks()),
            new FactionDef(FactionId.MafiaFamily2, "mafia_2", FactionKind.Mafia, 30, MafiaRanks()),
            // Court seat split and salaries: DRAFT - owner review. Salaries are 0 until decided.
            new FactionDef(FactionId.Court, "court", FactionKind.Government, 15, new[]
            {
                R(0, "chief_justice", "Chief Justice", 1, 0),
                R(1, "judge", "Judge", 4, 0),
                R(2, "prosecutor", "Prosecutor", 4, 0),
                R(3, "clerk", "Clerk", 6, 0),
            }),
        };

        private static readonly Dictionary<FactionId, FactionDef> ById = BuildIndex();

        private static Dictionary<FactionId, FactionDef> BuildIndex()
        {
            Dictionary<FactionId, FactionDef> map = new Dictionary<FactionId, FactionDef>();
            for (int i = 0; i < All.Length; i++)
            {
                map[All[i].Id] = All[i];
            }
            return map;
        }

        public static FactionDef Get(FactionId id)
        {
            FactionDef def;
            return ById.TryGetValue(id, out def) ? def : null;
        }

        public static FactionDef FindByKey(string key)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Key == key)
                {
                    return All[i];
                }
            }
            return null;
        }

        public static int TotalFactionSlots
        {
            get
            {
                int total = 0;
                for (int i = 0; i < All.Length; i++)
                {
                    total += All[i].SlotCap;
                }
                return total;
            }
        }

        public static int CivilianSlots
        {
            get { return ServerPlayerCap - TotalFactionSlots; }
        }
    }
}
