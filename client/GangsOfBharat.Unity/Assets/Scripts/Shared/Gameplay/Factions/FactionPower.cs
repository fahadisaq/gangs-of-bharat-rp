using System;
using System.Text;

namespace GangsOfBharat.Shared.Gameplay.Factions
{
    /// <summary>
    /// Faction powers from docs/design/04-factions.md (Permissions). A rank or a person holds a set of
    /// these flags. The stable string keys are in <see cref="FactionPowers"/>.
    /// </summary>
    [Flags]
    public enum FactionPower : uint
    {
        None = 0,
        Recruit = 1u << 0,
        Fire = 1u << 1,
        Promote = 1u << 2,
        Demote = 1u << 3,
        ReassignUnit = 1u << 4,
        RunInterviews = 1u << 5,
        PostHiringAds = 1u << 6,
        MuteFactionChat = 1u << 7,
        SpendTreasury = 1u << 8,
        /// <summary>Army, Police, Gangs and Mafia only.</summary>
        StartConvoy = 1u << 9,
        ManageArmory = 1u << 10,
        /// <summary>Mass Media only.</summary>
        SellAds = 1u << 11,
        /// <summary>Mass Media only: start a live TV or radio broadcast.</summary>
        GoLive = 1u << 12,
        /// <summary>Mass Media only.</summary>
        PublishPaper = 1u << 13,
        /// <summary>Head only. Can never be granted to a rank or a person.</summary>
        ManagePermissions = 1u << 14,
    }

    /// <summary>Power keys, which powers exist in which faction, and the default permission table.</summary>
    public static class FactionPowers
    {
        /// <summary>Daily treasury limit value meaning "no limit" (the head).</summary>
        public const long UnlimitedRupees = -1;
        /// <summary>Default rank 1 spend_treasury limit per IST day. DRAFT - owner review.</summary>
        public const long Rank1DailyLimitRupees = 50000;
        /// <summary>
        /// Limit for a personal spend_treasury grant without a personal limit, and the starting limit of
        /// any rank below 1 that the head gives spend_treasury to. DRAFT - owner review.
        /// </summary>
        public const long DefaultGrantDailyLimitRupees = 10000;

        /// <summary>Powers in key order (bit order).</summary>
        public static readonly FactionPower[] Ordered = new[]
        {
            FactionPower.Recruit, FactionPower.Fire, FactionPower.Promote, FactionPower.Demote,
            FactionPower.ReassignUnit, FactionPower.RunInterviews, FactionPower.PostHiringAds,
            FactionPower.MuteFactionChat, FactionPower.SpendTreasury, FactionPower.StartConvoy,
            FactionPower.ManageArmory, FactionPower.SellAds, FactionPower.GoLive, FactionPower.PublishPaper,
            FactionPower.ManagePermissions,
        };

        private static readonly string[] OrderedKeys = new[]
        {
            "recruit", "fire", "promote", "demote", "reassign_unit", "run_interviews", "post_hiring_ads",
            "mute_faction_chat", "spend_treasury", "start_convoy", "manage_armory", "sell_ads", "go_live",
            "publish_paper", "manage_permissions",
        };

        /// <summary>Middle leader ranks: recruit, fire, promote, demote, reassign_unit, run_interviews, post_hiring_ads, mute_faction_chat.</summary>
        public const FactionPower LeaderPowers = FactionPower.Recruit | FactionPower.Fire | FactionPower.Promote |
            FactionPower.Demote | FactionPower.ReassignUnit | FactionPower.RunInterviews |
            FactionPower.PostHiringAds | FactionPower.MuteFactionChat;

        public const FactionPower MediaOnlyPowers = FactionPower.SellAds | FactionPower.GoLive | FactionPower.PublishPaper;

        private const FactionPower CommonPowers = LeaderPowers | FactionPower.SpendTreasury |
            FactionPower.ManageArmory | FactionPower.ManagePermissions;

        /// <summary>Stable key of a single power, or null if <paramref name="power"/> is not exactly one power.</summary>
        public static string Key(FactionPower power)
        {
            for (int i = 0; i < Ordered.Length; i++)
            {
                if (Ordered[i] == power)
                {
                    return OrderedKeys[i];
                }
            }
            return null;
        }

        /// <summary>Power for a stable key, or <see cref="FactionPower.None"/> when unknown.</summary>
        public static FactionPower FromKey(string key)
        {
            for (int i = 0; i < OrderedKeys.Length; i++)
            {
                if (OrderedKeys[i] == key)
                {
                    return Ordered[i];
                }
            }
            return FactionPower.None;
        }

        /// <summary>Comma separated keys in bit order, or "none". Used in audit log details.</summary>
        public static string Keys(FactionPower powers)
        {
            if (powers == FactionPower.None)
            {
                return "none";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Ordered.Length; i++)
            {
                if ((powers & Ordered[i]) != 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(OrderedKeys[i]);
                }
            }
            return sb.ToString();
        }

        public static bool HasConvoys(FactionId faction)
        {
            FactionDef def = FactionCatalog.Get(faction);
            if (def == null)
            {
                return false;
            }
            return faction == FactionId.Army || faction == FactionId.Police ||
                def.Kind == FactionKind.Gang || def.Kind == FactionKind.Mafia;
        }

        /// <summary>Every power that exists in the faction. This is what the head holds.</summary>
        public static FactionPower AllowedPowers(FactionId faction)
        {
            if (FactionCatalog.Get(faction) == null)
            {
                return FactionPower.None;
            }
            FactionPower p = CommonPowers;
            if (HasConvoys(faction))
            {
                p |= FactionPower.StartConvoy;
            }
            if (faction == FactionId.Media)
            {
                p |= MediaOnlyPowers;
            }
            return p;
        }

        /// <summary>
        /// Default permission table: rank 0 all powers; rank 1 all except manage_permissions; middle leader
        /// ranks (rank 2 or lower with at least two ranks below) <see cref="LeaderPowers"/>; the two lowest
        /// ranks none. Additions: start_convoy for rank 2 where convoys exist, go_live for Mass Media rank 2.
        /// </summary>
        public static FactionPower DefaultRankPowers(FactionDef def, int rankLevel)
        {
            if (def == null || rankLevel < 0 || rankLevel >= def.Ranks.Length)
            {
                return FactionPower.None;
            }
            FactionPower allowed = AllowedPowers(def.Id);
            if (rankLevel == 0)
            {
                return allowed;
            }
            if (rankLevel == 1)
            {
                return allowed & ~FactionPower.ManagePermissions;
            }
            if (rankLevel >= def.Ranks.Length - 2)
            {
                return FactionPower.None;
            }
            FactionPower p = LeaderPowers;
            if (rankLevel == 2)
            {
                if (HasConvoys(def.Id))
                {
                    p |= FactionPower.StartConvoy;
                }
                if (def.Id == FactionId.Media)
                {
                    p |= FactionPower.GoLive;
                }
            }
            return p;
        }

        /// <summary>
        /// Default spend_treasury daily limit for a rank: rank 0 <see cref="UnlimitedRupees"/>, rank 1
        /// <see cref="Rank1DailyLimitRupees"/>, other ranks <see cref="DefaultGrantDailyLimitRupees"/> (only
        /// used if the head gives that rank spend_treasury).
        /// </summary>
        public static long DefaultRankDailyLimit(int rankLevel)
        {
            if (rankLevel == 0)
            {
                return UnlimitedRupees;
            }
            return rankLevel == 1 ? Rank1DailyLimitRupees : DefaultGrantDailyLimitRupees;
        }
    }
}
