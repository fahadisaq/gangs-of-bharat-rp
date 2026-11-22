using System;
using System.Collections.Generic;
using System.Linq;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Gameplay.Economy;
using GangsOfBharat.Shared.Gameplay.Factions;

namespace GangsOfBharat.Tests
{
    public static class FactionRulesTests
    {
        // Police rank levels from FactionCatalog.
        private const int Dcp = 1;
        private const int Inspector = 3;
        private const int Si = 4;
        private const int HeadConstable = 5;
        private const int Constable = 6;
        private const int Trainee = 7;

        private static FactionService NewPolice(out ManualClock clock)
        {
            clock = new ManualClock();
            FactionService f = new FactionService(clock);
            Assert.Equal(FactionResult.Ok, f.AppointHead("admin", FactionId.Police, "cmr"));
            Assert.Equal(FactionResult.Ok, f.Hire("cmr", "insp", Inspector));
            Assert.Equal(FactionResult.Ok, f.Hire("cmr", "insp2", Inspector));
            Assert.Equal(FactionResult.Ok, f.Hire("cmr", "si", Si));
            Assert.Equal(FactionResult.Ok, f.Hire("cmr", "hc", HeadConstable));
            Assert.Equal(FactionResult.Ok, f.Hire("cmr", "con", Constable));
            Assert.Equal(FactionResult.Ok, f.Hire("cmr", "con2", Constable));
            return f;
        }

        [Test]
        public static void LowerRankCannotActOnHigherOrEqualRank()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            Assert.Equal(FactionResult.InsufficientRank, f.Fire("con", "insp", "insubordination"), "constable cannot fire inspector");
            Assert.Equal(FactionResult.InsufficientRank, f.Fire("con", "con2", "reason"), "equal rank");
            Assert.Equal(FactionResult.InsufficientRank, f.Demote("insp", "insp2"), "equal rank demote");
            Assert.Equal(FactionResult.InsufficientRank, f.Promote("con", "cmr"));
            Assert.Equal(FactionResult.InsufficientRank, f.Hire("insp", "newbie", Inspector), "hire at own rank");
            Assert.Equal(FactionResult.InsufficientRank, f.Hire("insp", "newbie", Dcp), "hire above own rank");
            Assert.Equal(FactionResult.Ok, f.Hire("insp", "newbie", Trainee));
            Assert.Equal(FactionResult.ActorNotMember, f.Hire("stranger", "x", Trainee));
            Assert.Equal(Inspector, f.GetMember("insp").RankLevel, "unchanged after rejected actions");
        }

        [Test]
        public static void PromoteOnlyUpToOneRankBelowActor()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            Assert.Equal(FactionResult.InsufficientRank, f.Promote("insp", "si"), "inspector cannot promote to inspector");
            Assert.Equal(Si, f.GetMember("si").RankLevel);
            Assert.Equal(FactionResult.Ok, f.Promote("insp", "hc"), "inspector can promote to SI");
            Assert.Equal(Si, f.GetMember("hc").RankLevel);
            Assert.Equal(FactionResult.Ok, f.Demote("insp", "hc"));
            Assert.Equal(HeadConstable, f.GetMember("hc").RankLevel);
            Assert.Equal(FactionResult.Ok, f.Demote("insp", "con"));
            Assert.Equal(Trainee, f.GetMember("con").RankLevel);
            Assert.Equal(FactionResult.InvalidRank, f.Demote("insp", "con"), "no rank below trainee");
            FactionService other = new FactionService(clock);
            other.AppointHead("admin", FactionId.Army, "gen");
            Assert.Equal(FactionResult.TargetNotMember, other.Promote("gen", "si"));
        }

        [Test]
        public static void FireNeedsReasonAndOpensAppealWindow()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            Assert.Equal(FactionResult.ReasonRequired, f.Fire("insp", "con", null));
            Assert.Equal(FactionResult.ReasonRequired, f.Fire("insp", "con", "   "));
            Assert.NotNull(f.GetMember("con"), "still a member");
            Assert.Equal(FactionResult.Ok, f.Fire("insp", "con", "skipped duty 5 times"));
            Assert.True(f.GetMember("con") == null, "removed");
            FireRecord rec = f.GetFireRecord("con");
            Assert.Equal("skipped duty 5 times", rec.Reason);
            Assert.Equal(clock.UtcNow.AddDays(3), rec.AppealDeadlineUtc);
            clock.Advance(TimeSpan.FromDays(2));
            Assert.Equal(FactionResult.Ok, f.FileAppeal("con", "I was on leave"));
            Assert.Equal(FactionResult.AlreadyAppealed, f.FileAppeal("con", "again"));

            Assert.Equal(FactionResult.Ok, f.Fire("insp", "con2", "abuse of power"));
            clock.Advance(TimeSpan.FromDays(3) + TimeSpan.FromSeconds(1));
            Assert.Equal(FactionResult.AppealWindowClosed, f.FileAppeal("con2", "late"));
            Assert.Equal(FactionResult.NoFireRecord, f.FileAppeal("si", "never fired"));
        }

        [Test]
        public static void RankAndFactionSlotCapsEnforced()
        {
            ManualClock clock = new ManualClock();
            FactionService f = new FactionService(clock);
            FactionDef police = FactionCatalog.Get(FactionId.Police);
            Assert.Equal(FactionResult.Ok, f.AppointHead("admin", FactionId.Police, "p0"));
            Assert.Equal(FactionResult.RankFull, f.AppointHead("admin", FactionId.Police, "p0b"), "one commissioner");
            Assert.Equal(FactionResult.Ok, f.Hire("p0", "dcp1", Dcp));
            Assert.Equal(FactionResult.Ok, f.Hire("p0", "dcp2", Dcp));
            Assert.Equal(FactionResult.RankFull, f.Hire("p0", "dcp3", Dcp), "DCP count is 2");
            for (int level = 2; level < police.Ranks.Length; level++)
            {
                for (int i = 0; i < police.Ranks[level].Count; i++)
                {
                    Assert.Equal(FactionResult.Ok, f.Hire("p0", "p" + level + "_" + i, level));
                }
            }
            Assert.Equal(police.SlotCap, f.MemberCount(FactionId.Police));
            Assert.Equal(FactionResult.FactionFull, f.Hire("p0", "extra", Trainee), "80 slot cap");

            // Promotion into a full rank is rejected.
            Assert.Equal(FactionResult.RankFull, f.Promote("dcp1", "p3_0"), "ACP seats full");
        }

        [Test]
        public static void OneFactionPerCharacter()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            f.AppointHead("admin", FactionId.Army, "gen");
            Assert.Equal(FactionResult.AlreadyInFaction, f.Hire("gen", "con", 7));
            Assert.Equal(FactionResult.AlreadyInFaction, f.Hire("cmr", "insp", Trainee));
            Assert.Equal(FactionResult.AlreadyInFaction, f.AppointHead("admin", FactionId.Hospital, "si"));
        }

        [Test]
        public static void SwitchingCooldowns()
        {
            ManualClock clock = new ManualClock();
            FactionService f = new FactionService(clock);
            f.AppointHead("admin", FactionId.Police, "cmr");
            f.AppointHead("admin", FactionId.Army, "gen");
            f.AppointHead("admin", FactionId.Hospital, "cmo");
            f.AppointHead("admin", FactionId.GangSlum, "bhai");
            Assert.Equal(FactionResult.Ok, f.Hire("cmr", "cop", Trainee));
            Assert.Equal(FactionResult.Ok, f.Hire("bhai", "goon", 5));
            Assert.Equal(FactionResult.Ok, f.Hire("bhai", "goon2", 5));

            Assert.Equal(FactionResult.Ok, f.Leave("cop"));
            Assert.Equal(FactionResult.Ok, f.Leave("goon"));
            Assert.Equal(FactionResult.Ok, f.Leave("goon2"));
            clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
            Assert.Equal(FactionResult.Cooldown, f.Hire("gen", "cop", 7), "7-day cooldown");
            Assert.Equal(FactionResult.Cooldown, f.Hire("cmo", "goon2", 5));
            clock.AdvanceSeconds(1);
            Assert.Equal(FactionResult.Ok, f.Hire("gen", "cop", 7), "police -> army after 7 days");
            Assert.Equal(FactionResult.Ok, f.Hire("cmo", "goon2", 5), "gang -> hospital after 7 days");
            Assert.Equal(FactionResult.Cooldown, f.Hire("cmr", "goon", Trainee), "gang -> police needs 14 days");
            clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
            Assert.Equal(FactionResult.Cooldown, f.Hire("cmr", "goon", Trainee), "13.99 days");
            clock.AdvanceSeconds(1);
            Assert.Equal(FactionResult.Ok, f.Hire("cmr", "goon", Trainee), "gang -> police after 14 days");
        }

        [Test]
        public static void InactiveMembersAutoRemovedAfterSevenDays()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            clock.Advance(TimeSpan.FromDays(6));
            f.MarkActive("cmr");
            f.MarkActive("si");
            Assert.Equal(0, f.RemoveInactive().Count);
            clock.Advance(TimeSpan.FromDays(1));
            List<string> removed = f.RemoveInactive();
            removed.Sort(StringComparer.Ordinal);
            Assert.SequenceEqual(new[] { "con", "con2", "hc", "insp", "insp2" }, removed);
            Assert.NotNull(f.GetMember("cmr"));
            Assert.NotNull(f.GetMember("si"));
            Assert.Equal(5, f.GetAuditLog().Count(e => e.Action == "auto_remove_inactive"));
        }

        [Test]
        public static void EveryFactionActionIsAudited()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            f.Promote("insp", "hc");
            f.Demote("insp", "hc");
            f.Fire("insp", "con", "reason");
            f.Leave("con2");
            f.Fire("con", "insp", "rejected so not logged");
            List<string> actions = f.GetAuditLog().Select(e => e.Action).ToList();
            Assert.SequenceEqual(new[] { "appoint_head", "hire", "hire", "hire", "hire", "hire", "hire",
                "promote", "demote", "fire", "leave" }, actions);
            FactionAuditEntry fire = f.GetAuditLog()[9];
            Assert.Equal("insp", fire.ActorId);
            Assert.Equal("con", fire.TargetId);
            Assert.Equal("reason", fire.Detail);
            Assert.Equal(FactionId.Police, fire.Faction);
        }

        [Test]
        public static void DutyOnlyAtLockerAndGearOnlyOnDuty()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            Assert.Equal(FactionResult.NoLocker, f.StartDuty("con", 100, 100));
            f.SetLocker(FactionId.Police, 100, 100, 5);
            Assert.Equal(FactionResult.ActorNotMember, f.StartDuty("civilian", 100, 100));
            Assert.Equal(FactionResult.NotAtLocker, f.StartDuty("con", 106, 100));
            Assert.False(f.GetMember("con").HasGear);
            Assert.Equal(FactionResult.NotOnDuty, f.EndDuty("con", 100, 100));
            Assert.Equal(FactionResult.Ok, f.StartDuty("con", 103, 104));
            Assert.True(f.IsOnDuty("con"));
            Assert.True(f.GetMember("con").HasGear, "gear issued at locker");
            Assert.Equal(FactionResult.AlreadyOnDuty, f.StartDuty("con", 100, 100));
            Assert.Equal(FactionResult.NotAtLocker, f.EndDuty("con", 500, 500), "must return to locker");
            Assert.True(f.GetMember("con").HasGear);
            Assert.Equal(FactionResult.Ok, f.EndDuty("con", 100, 100));
            Assert.False(f.GetMember("con").HasGear, "gear returned");
            Assert.False(f.IsOnDuty("con"));

            f.StartDuty("con2", 100, 100);
            f.Fire("insp", "con2", "reason");
            Assert.False(f.IsOnDuty("con2"), "fired member loses duty");
        }

        // ---- payday ----

        [Test]
        public static void PaydayPaysRankSalaryEveryThirtyActiveMinutes()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            Ledger ledger = new Ledger(clock);
            ledger.Mint(Ledger.CityTreasury, 1000000, "test_setup");
            PaydayService pay = new PaydayService(f, ledger);
            List<PaydayRecord> paid = new List<PaydayRecord>();
            Assert.Equal(0, pay.AddPlayTime("con", 1799, false, paid));
            Assert.Equal(1, pay.AddPlayTime("con", 1, false, paid));
            Assert.Equal(2000L, paid[0].Total, "constable salary");
            Assert.Equal(2000L, ledger.GetBalance(Ledger.Bank("con")), "salary to bank");
            Assert.Equal(998000L, ledger.GetBalance(Ledger.CityTreasury), "paid from the city treasury");
            Assert.Equal(2, pay.AddPlayTime("insp", 3700, false, paid), "two paydays in one call");
            Assert.Equal(9000L, ledger.GetBalance(Ledger.Bank("insp")));
            Assert.Near(100, pay.ActiveSeconds("insp"), 1e-9, "remainder carried over");
            Assert.Equal(0, pay.AddPlayTime("nobody", 5000, false, paid), "non-members earn nothing");
            Assert.True(f.GetAuditLog().Any(e => e.Action == "payday" && e.TargetId == "insp"), "payouts audited");
        }

        [Test]
        public static void PaydaySkipsAfkTime()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            Ledger ledger = new Ledger(clock);
            ledger.Mint(Ledger.CityTreasury, 1000000, "test_setup");
            PaydayService pay = new PaydayService(f, ledger);
            Assert.Equal(0, pay.AddPlayTime("si", 1000, false, null));
            Assert.Equal(0, pay.AddPlayTime("si", 7200, true, null), "AFK does not count");
            Assert.Near(1000, pay.ActiveSeconds("si"), 1e-9);
            Assert.Equal(0L, ledger.GetBalance(Ledger.Bank("si")));
            Assert.Equal(1, pay.AddPlayTime("si", 800, false, null));
            Assert.Equal(3500L, ledger.GetBalance(Ledger.Bank("si")));
        }

        [Test]
        public static void GangSalaryIsZero()
        {
            ManualClock clock = new ManualClock();
            FactionService f = new FactionService(clock);
            f.AppointHead("admin", FactionId.GangPort, "bhai");
            f.AppointHead("admin", FactionId.MafiaFamily1, "don");
            Ledger ledger = new Ledger(clock);
            ledger.Mint(Ledger.CityTreasury, 1000000, "test_setup");
            PaydayService pay = new PaydayService(f, ledger);
            List<PaydayRecord> paid = new List<PaydayRecord>();
            Assert.Equal(1, pay.AddPlayTime("bhai", 1800, false, paid));
            Assert.Equal(1, pay.AddPlayTime("don", 1800, false, paid));
            Assert.Equal(0L, paid[0].Total);
            Assert.Equal(0L, paid[1].Total);
            Assert.Equal(0L, ledger.GetBalance(Ledger.Bank("bhai")));
            Assert.Equal(1000000L, ledger.GetBalance(Ledger.CityTreasury));
        }

        [Test]
        public static void PaydayFloorWhenTreasuryLow()
        {
            ManualClock clock;
            FactionService f = NewPolice(out clock);
            Ledger ledger = new Ledger(clock);
            ledger.Mint(Ledger.CityTreasury, 500, "test_setup");
            PaydayService pay = new PaydayService(f, ledger);
            List<PaydayRecord> paid = new List<PaydayRecord>();
            pay.AddPlayTime("insp", 1800, false, paid);
            Assert.Equal(4500L, paid[0].Salary);
            Assert.Equal(500L, paid[0].PaidFromTreasury);
            Assert.Equal(1750L, paid[0].PaidFromFloor, "topped up to 50% floor");
            Assert.Equal(2250L, ledger.GetBalance(Ledger.Bank("insp")));
            Assert.Equal(0L, ledger.GetBalance(Ledger.CityTreasury), "treasury never negative");
            pay.AddPlayTime("insp2", 1800, false, paid);
            Assert.Equal(2250L, paid[1].Total, "empty treasury still pays the floor");
            Assert.Equal(0L, paid[1].PaidFromTreasury);
            Assert.Equal(1750L + 2250L, ledger.SourceTotalFor("payday_floor"), "floor top-ups are tracked as a source");
        }
    }
}
