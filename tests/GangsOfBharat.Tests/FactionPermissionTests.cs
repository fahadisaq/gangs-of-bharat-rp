using System;
using System.Collections.Generic;
using System.Linq;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Gameplay.Economy;
using GangsOfBharat.Shared.Gameplay.Factions;

namespace GangsOfBharat.Tests
{
    /// <summary>Chain of command, permissions and Mass Media rules from docs/design/04-factions.md.</summary>
    public static class FactionPermissionTests
    {
        // Army rank levels from FactionCatalog.
        private const int Colonel = 1;
        private const int Major = 2;
        private const int Captain = 3;
        private const int Lieutenant = 4;
        private const int Sergeant = 5;
        private const int Soldier = 6;
        private const int Recruit = 7;

        /// <summary>
        /// gen -> colA -> majA -> capA -> ltA -> solA and gen -> colB -> majB -> capB.
        /// </summary>
        private static FactionService NewArmy(out ManualClock clock)
        {
            clock = new ManualClock();
            FactionService f = new FactionService(clock);
            Assert.Equal(FactionResult.Ok, f.AppointHead("admin", FactionId.Army, "gen"));
            Assert.Equal(FactionResult.Ok, f.Hire("gen", "colA", Colonel));
            Assert.Equal(FactionResult.Ok, f.Hire("gen", "colB", Colonel));
            Assert.Equal(FactionResult.Ok, f.Hire("colA", "majA", Major));
            Assert.Equal(FactionResult.Ok, f.Hire("colB", "majB", Major));
            Assert.Equal(FactionResult.Ok, f.Hire("majA", "capA", Captain));
            Assert.Equal(FactionResult.Ok, f.Hire("majB", "capB", Captain));
            Assert.Equal(FactionResult.Ok, f.Hire("capA", "ltA", Lieutenant));
            Assert.Equal(FactionResult.Ok, f.Hire("ltA", "solA", Soldier));
            return f;
        }

        // ---- ranks, units and chain of command ----

        [Test]
        public static void ManyMembersCanHoldTheSameRankUpToSeatCount()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(2, f.RankCount(FactionId.Army, Colonel), "two Colonels");
            Assert.Equal(FactionResult.RankFull, f.Hire("gen", "colC", Colonel), "Colonel has 2 seats");
            Assert.Equal(FactionResult.Ok, f.Hire("gen", "majC", Major, "colB"), "third Major seat");
            Assert.Equal(FactionResult.RankFull, f.Hire("gen", "majD", Major), "Major has 3 seats");
            Assert.Equal("colB", f.GetMember("majC").SuperiorId);
            Assert.True(f.GetMember("gen").SuperiorId == null, "head has no superior");
            Assert.Equal("ltA", f.GetMember("solA").SuperiorId, "hire reports to the hiring leader");
        }

        [Test]
        public static void LeaderActsOnlyInsideOwnUnit()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.NotInUnit, f.Fire("colA", "majB", "reason"), "other Colonel's Major");
            Assert.Equal(FactionResult.NotInUnit, f.Demote("colA", "majB"));
            Assert.Equal(FactionResult.NotInUnit, f.Promote("colA", "capB"));
            Assert.Equal(FactionResult.NotInUnit, f.ReassignUnit("colA", "capB", "majA"));
            Assert.Equal(FactionResult.NotInUnit, f.ReassignUnit("colA", "capA", "majB"), "cannot move into another unit");
            Assert.Equal(FactionResult.NotInUnit, f.CanMuteInFactionChat("colA", "capB"));
            Assert.NotNull(f.GetMember("majB"), "unchanged");

            Assert.True(f.IsInUnit("colA", "solA"), "deep subtree is in the unit");
            Assert.False(f.IsInUnit("colA", "capB"));
            Assert.False(f.IsInUnit("majA", "colA"), "superior is not in the unit");
            Assert.SequenceEqual(new[] { "capA", "ltA", "majA", "solA" },
                f.GetUnit("colA").Select(m => m.CharacterId).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(8, f.GetUnit("gen").Count, "head's unit is the whole faction");

            Assert.Equal(FactionResult.Ok, f.Fire("colA", "solA", "skipped drills"), "own unit");
            Assert.Equal(FactionResult.Ok, f.CanMuteInFactionChat("colA", "capA"));
            Assert.Equal(FactionResult.Ok, f.Fire("gen", "capB", "head acts anywhere"));
        }

        [Test]
        public static void SameRankMembersCannotActOnEachOther()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.InsufficientRank, f.Fire("colA", "colB", "reason"));
            Assert.Equal(FactionResult.InsufficientRank, f.Demote("colA", "colB"));
            Assert.Equal(FactionResult.InsufficientRank, f.Fire("majA", "majB", "reason"));
            Assert.Equal(FactionResult.InsufficientRank, f.ReassignUnit("colA", "colB", "colA"));
            Assert.Equal(FactionResult.InsufficientRank, f.CanMuteInFactionChat("majA", "majB"));
            // Extra powers never allow acting on equal rank.
            Assert.Equal(FactionResult.Ok, f.GrantPower("gen", "majA", FactionPower.SpendTreasury));
            Assert.Equal(FactionResult.InsufficientRank, f.Demote("majA", "majB"));
        }

        [Test]
        public static void PromotionKeepsSuperiorAndCannotReachSuperiorRank()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.ReassignRequired, f.Promote("colA", "capA"), "capA would equal majA");
            Assert.Equal(Captain, f.GetMember("capA").RankLevel);
            Assert.Equal(FactionResult.Ok, f.ReassignUnit("colA", "capA", "colA"));
            Assert.Equal(FactionResult.Ok, f.Promote("colA", "capA"));
            Assert.Equal(Major, f.GetMember("capA").RankLevel);
            Assert.Equal("colA", f.GetMember("capA").SuperiorId, "superior kept");
            Assert.Equal(FactionResult.InsufficientRank, f.Promote("colA", "capA"), "only up to one rank below the actor");
            Assert.Equal(FactionResult.Ok, f.Promote("ltA", "solA"), "lieutenant promotes soldier to sergeant");
            Assert.Equal(Sergeant, f.GetMember("solA").RankLevel);
        }

        [Test]
        public static void DemotionRefusedWhileReportWouldReachSameRank()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.ReassignRequired, f.Demote("colA", "capA"), "ltA is a Lieutenant");
            Assert.Equal(FactionResult.Ok, f.ReassignUnit("colA", "ltA", "majA"));
            Assert.Equal(FactionResult.Ok, f.Demote("colA", "capA"));
            Assert.Equal(Lieutenant, f.GetMember("capA").RankLevel);
        }

        [Test]
        public static void FiredLeaderReportsMoveUpToTheirSuperior()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.Ok, f.Fire("colA", "majA", "absent"));
            Assert.Equal("colA", f.GetMember("capA").SuperiorId, "report moved up");
            Assert.True(f.IsInUnit("colA", "solA"), "subtree still in the unit");
            FactionAuditEntry moved = f.GetAuditLog().Single(e => e.Action == "reassign_on_remove");
            Assert.Equal("capA", moved.TargetId);
            Assert.Equal("majA->colA", moved.Detail);

            Assert.Equal(FactionResult.Ok, f.Leave("ltA"));
            Assert.Equal("capA", f.GetMember("solA").SuperiorId, "leave also moves reports up");

            clock.Advance(TimeSpan.FromDays(7));
            f.MarkActive("gen");
            f.MarkActive("colB");
            f.MarkActive("majB");
            f.MarkActive("capB");
            f.MarkActive("capA");
            f.MarkActive("solA");
            f.RemoveInactive();
            Assert.Equal("gen", f.GetMember("capA").SuperiorId, "inactive colA removed, capA moves to the head");
        }

        [Test]
        public static void HeadReportsWaitForNewHead()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.RankFull, f.AppointHead("admin", FactionId.Army, "gen2"), "faction has a head");
            Assert.Equal(FactionResult.Ok, f.Leave("gen"));
            Assert.True(f.GetMember("colA").SuperiorId == null, "no superior until a new head");
            Assert.True(f.GetMember("colB").SuperiorId == null);
            Assert.Equal("colA", f.GetMember("majA").SuperiorId, "rest of the chain unchanged");
            Assert.Equal(FactionResult.Ok, f.AppointHead("admin", FactionId.Army, "gen2"));
            Assert.Equal("gen2", f.GetMember("colA").SuperiorId);
            Assert.Equal("gen2", f.GetMember("colB").SuperiorId);
            Assert.Equal(2, f.GetAuditLog().Count(e => e.Action == "attach_to_head"));
        }

        [Test]
        public static void HireIntoSubUnitOnlyInsideOwnUnit()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.Ok, f.Hire("colA", "rec1", Recruit, "ltA"));
            Assert.Equal("ltA", f.GetMember("rec1").SuperiorId);
            Assert.Equal(FactionResult.NotInUnit, f.Hire("colA", "rec2", Recruit, "capB"), "superior in another unit");
            Assert.Equal(FactionResult.NotInUnit, f.Hire("majA", "rec2", Recruit, "colA"), "superior above the actor");
            Assert.Equal(FactionResult.InsufficientRank, f.Hire("colA", "rec2", Lieutenant, "ltA"), "superior must outrank the hire");
            Assert.Equal(FactionResult.TargetNotMember, f.Hire("colA", "rec2", Recruit, "ghost"));
            Assert.True(f.GetMember("rec2") == null, "nobody hired by rejected calls");
        }

        [Test]
        public static void ReassignWithinUnitRejectsCyclesAndLowerSuperiors()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.Ok, f.ReassignUnit("colA", "ltA", "majA"));
            Assert.Equal("majA", f.GetMember("ltA").SuperiorId);
            Assert.Equal(FactionResult.InvalidArgument, f.ReassignUnit("colA", "majA", "capA"), "cycle");
            Assert.Equal(FactionResult.InvalidArgument, f.ReassignUnit("colA", "majA", "majA"), "self");
            Assert.Equal(FactionResult.InsufficientRank, f.ReassignUnit("colA", "capA", "ltA"), "lower-ranked superior");
            Assert.Equal(FactionResult.Ok, f.Hire("gen", "rec", Recruit, "solA"));
            Assert.Equal(FactionResult.MissingPower, f.ReassignUnit("solA", "rec", "solA"), "soldier has no powers");
            Assert.Equal(FactionResult.Ok, f.ReassignUnit("gen", "capB", "majA"), "head moves across units");
            Assert.True(f.IsInUnit("colA", "capB"));
            FactionAuditEntry e = f.GetAuditLog().Last(x => x.Action == "reassign_unit");
            Assert.Equal("majB->majA", e.Detail);
        }

        // ---- permissions ----

        [Test]
        public static void DefaultPowersFollowTheTable()
        {
            FactionDef army = FactionCatalog.Get(FactionId.Army);
            FactionPower armyAll = FactionPowers.AllowedPowers(FactionId.Army);
            Assert.Equal(armyAll, FactionPowers.DefaultRankPowers(army, 0));
            Assert.True((armyAll & FactionPower.ManagePermissions) != 0 && (armyAll & FactionPower.StartConvoy) != 0);
            Assert.Equal(FactionPower.None, armyAll & FactionPowers.MediaOnlyPowers, "no media powers in the army");
            Assert.Equal(armyAll & ~FactionPower.ManagePermissions, FactionPowers.DefaultRankPowers(army, Colonel));
            Assert.Equal(FactionPowers.LeaderPowers | FactionPower.StartConvoy, FactionPowers.DefaultRankPowers(army, Major));
            Assert.Equal(FactionPowers.LeaderPowers, FactionPowers.DefaultRankPowers(army, Sergeant));
            Assert.Equal(FactionPower.None, FactionPowers.DefaultRankPowers(army, Soldier));
            Assert.Equal(FactionPower.None, FactionPowers.DefaultRankPowers(army, Recruit));

            Assert.Equal(FactionPowers.LeaderPowers | FactionPower.StartConvoy,
                FactionPowers.DefaultRankPowers(FactionCatalog.Get(FactionId.Police), 2), "ACP convoys");
            Assert.Equal(FactionPowers.LeaderPowers | FactionPower.StartConvoy,
                FactionPowers.DefaultRankPowers(FactionCatalog.Get(FactionId.GangPort), 2), "Area Commander convoys");
            Assert.Equal(FactionPowers.LeaderPowers | FactionPower.StartConvoy,
                FactionPowers.DefaultRankPowers(FactionCatalog.Get(FactionId.MafiaFamily2), 2), "mafia Captain convoys");
            Assert.Equal(FactionPowers.LeaderPowers,
                FactionPowers.DefaultRankPowers(FactionCatalog.Get(FactionId.Hospital), 2), "Doctor, no convoys");
            Assert.Equal(FactionPower.None, FactionPowers.AllowedPowers(FactionId.Hospital) & FactionPower.StartConvoy);
            Assert.Equal(FactionPower.None,
                FactionPowers.DefaultRankPowers(FactionCatalog.Get(FactionId.Fire), 2), "Firefighter is one of the two lowest");

            FactionDef media = FactionCatalog.Get(FactionId.Media);
            FactionPower dept = FactionPowers.DefaultRankPowers(media, 1);
            Assert.Equal(FactionPowers.MediaOnlyPowers, dept & FactionPowers.MediaOnlyPowers, "department heads sell ads, go live, publish");
            Assert.Equal(FactionPowers.LeaderPowers | FactionPower.GoLive, FactionPowers.DefaultRankPowers(media, 2));
            Assert.Equal(FactionPower.None, FactionPowers.DefaultRankPowers(media, 3));
            Assert.Equal(FactionPower.None, FactionPowers.DefaultRankPowers(media, 4));

            for (int i = 0; i < FactionPowers.Ordered.Length; i++)
            {
                Assert.Equal(FactionPowers.Ordered[i], FactionPowers.FromKey(FactionPowers.Key(FactionPowers.Ordered[i])), "key round trip");
            }
            Assert.Equal("recruit", FactionPowers.Key(FactionPower.Recruit));
            Assert.Equal("manage_permissions", FactionPowers.Key(FactionPower.ManagePermissions));

            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.True(f.HasPower("gen", FactionPower.ManagePermissions));
            Assert.False(f.HasPower("colA", FactionPower.ManagePermissions));
            Assert.True(f.HasPower("capA", FactionPower.Recruit));
            Assert.Equal(FactionPower.None, f.GetEffectivePowers("solA"));
            Assert.Equal(FactionResult.MissingPower, f.Hire("solA", "rec", Recruit), "soldier has no recruit power");
        }

        [Test]
        public static void HeadCanDisableAPowerForARank()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            FactionPower noRecruit = FactionPowers.LeaderPowers & ~FactionPower.Recruit;
            Assert.Equal(FactionResult.Ok, f.SetRankPowers("gen", Captain, noRecruit));
            Assert.Equal(noRecruit, f.GetRankPowers(FactionId.Army, Captain));
            Assert.Equal(FactionResult.MissingPower, f.Hire("capA", "rec", Recruit));
            Assert.Equal(FactionResult.MissingPower, f.Hire("capB", "rec", Recruit));
            Assert.Equal(FactionResult.Ok, f.Fire("capA", "solA", "other powers stay"));
            Assert.Equal(FactionResult.Ok, f.Hire("majA", "rec", Recruit), "other ranks unaffected");
        }

        [Test]
        public static void HeadCanGrantAPowerToOnePerson()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.Ok, f.SetRankPowers("gen", Captain, FactionPower.None));
            Assert.Equal(FactionResult.Ok, f.GrantPower("gen", "capA", FactionPower.Recruit));
            Assert.Equal(FactionPower.Recruit, f.GetPersonalGrants("capA"));
            Assert.Equal(FactionResult.Ok, f.Hire("capA", "rec1", Recruit), "Captain A can recruit");
            Assert.Equal(FactionResult.MissingPower, f.Hire("capB", "rec2", Recruit), "Captain B cannot");
            Assert.Equal(FactionResult.Ok, f.RevokePower("gen", "capA", FactionPower.Recruit));
            Assert.Equal(FactionResult.MissingPower, f.Hire("capA", "rec3", Recruit));

            // Grants survive promotion and end when the member leaves.
            Assert.Equal(FactionResult.Ok, f.GrantPower("gen", "solA", FactionPower.ManageArmory));
            Assert.Equal(FactionResult.Ok, f.Promote("ltA", "solA"));
            Assert.True(f.HasPower("solA", FactionPower.ManageArmory), "kept after promotion");
            Assert.Equal(FactionResult.Ok, f.Demote("ltA", "solA"));
            Assert.True(f.HasPower("solA", FactionPower.ManageArmory), "kept after demotion");
            Assert.Equal(FactionResult.Ok, f.Leave("solA"));
            Assert.Equal(FactionPower.None, f.GetPersonalGrants("solA"), "removed on leaving");
        }

        [Test]
        public static void OnlyTheHeadChangesPermissions()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.MissingPower, f.SetRankPowers("colA", Captain, FactionPower.None));
            Assert.Equal(FactionResult.MissingPower, f.GrantPower("colA", "majA", FactionPower.Recruit));
            Assert.Equal(FactionResult.MissingPower, f.RevokePower("colA", "majA", FactionPower.Recruit));
            Assert.Equal(FactionResult.MissingPower, f.SetTreasuryDailyLimit("colA", Major, 1000));
            Assert.Equal(FactionResult.MissingPower, f.SetPersonTreasuryDailyLimit("colA", "majA", 1000));
            Assert.Equal(FactionResult.ActorNotMember, f.SetRankPowers("stranger", Captain, FactionPower.None));

            Assert.Equal(FactionResult.PowerNotAllowed, f.GrantPower("gen", "colA", FactionPower.ManagePermissions), "never grantable");
            Assert.Equal(FactionResult.PowerNotAllowed, f.SetRankPowers("gen", Colonel, FactionPower.ManagePermissions | FactionPower.Recruit));
            Assert.Equal(FactionResult.InvalidRank, f.SetRankPowers("gen", 0, FactionPower.None), "head powers are fixed");
            Assert.Equal(FactionResult.InvalidRank, f.SetRankPowers("gen", 8, FactionPower.None));
            Assert.Equal(FactionResult.InvalidRank, f.SetTreasuryDailyLimit("gen", 0, 5));
            Assert.Equal(FactionResult.InsufficientRank, f.GrantPower("gen", "gen", FactionPower.Recruit));
            Assert.Equal(FactionResult.InvalidArgument, f.GrantPower("gen", "colA", FactionPower.None));
            Assert.False(f.HasPower("colA", FactionPower.ManagePermissions));
            Assert.Equal(0, f.GetAuditLog().Count(e => e.Action.StartsWith("perm_", StringComparison.Ordinal)), "rejected changes not logged");
        }

        [Test]
        public static void FactionSpecificPowersOnlyInsideTheirFaction()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Assert.Equal(FactionResult.PowerNotAllowed, f.GrantPower("gen", "colA", FactionPower.SellAds), "sell_ads is Mass Media only");
            Assert.Equal(FactionResult.PowerNotAllowed, f.SetRankPowers("gen", Major, FactionPower.GoLive));
            Assert.Equal(FactionResult.PowerNotAllowed, f.GrantPower("gen", "majA", FactionPower.PublishPaper));

            Assert.Equal(FactionResult.Ok, f.AppointHead("admin", FactionId.Hospital, "cmo"));
            Assert.Equal(FactionResult.Ok, f.Hire("cmo", "doc", 2));
            Assert.Equal(FactionResult.PowerNotAllowed, f.GrantPower("cmo", "doc", FactionPower.StartConvoy), "no hospital convoys");
            Assert.Equal(FactionResult.Ok, f.GrantPower("cmo", "doc", FactionPower.ManageArmory), "manage_armory everywhere");

            Assert.Equal(FactionResult.Ok, f.AppointHead("admin", FactionId.Media, "mh"));
            Assert.Equal(FactionResult.Ok, f.Hire("mh", "rep", 3));
            Assert.False(f.HasPower("rep", FactionPower.GoLive), "reporters cannot go live by default");
            Assert.Equal(FactionResult.Ok, f.GrantPower("mh", "rep", FactionPower.SellAds | FactionPower.GoLive));
            Assert.True(f.HasPower("rep", FactionPower.GoLive | FactionPower.SellAds));
            Assert.Equal(FactionResult.PowerNotAllowed, f.GrantPower("mh", "rep", FactionPower.StartConvoy), "no media convoys");
            Assert.Equal(FactionResult.DifferentFaction, f.GrantPower("mh", "doc", FactionPower.Recruit));
        }

        [Test]
        public static void PermissionChangesAndMutesAreAudited()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            int before = f.GetAuditLog().Count;
            Assert.Equal(FactionResult.Ok, f.SetRankPowers("gen", Captain, FactionPower.Recruit | FactionPower.Fire));
            Assert.Equal(FactionResult.Ok, f.GrantPower("gen", "solA", FactionPower.Recruit));
            Assert.Equal(FactionResult.Ok, f.RevokePower("gen", "solA", FactionPower.Recruit));
            Assert.Equal(FactionResult.Ok, f.SetTreasuryDailyLimit("gen", Colonel, 20000));
            Assert.Equal(FactionResult.Ok, f.SetPersonTreasuryDailyLimit("gen", "colB", 5000));
            Assert.Equal(FactionResult.Ok, f.ClearPersonTreasuryDailyLimit("gen", "colB"));
            Assert.Equal(FactionResult.Ok, f.MuteInFactionChat("colA", "majA", 10));
            Assert.Equal(FactionResult.NotInUnit, f.MuteInFactionChat("colA", "majB", 10));

            List<FactionAuditEntry> log = f.GetAuditLog().Skip(before).ToList();
            Assert.SequenceEqual(new[] { "perm_rank_set", "perm_grant", "perm_revoke", "treasury_limit_set",
                "treasury_limit_set", "treasury_limit_set", "faction_mute" }, log.Select(e => e.Action));
            Assert.True(log.All(e => e.Faction == FactionId.Army), "faction recorded");
            Assert.Equal("gen", log[0].ActorId);
            Assert.Equal("rank:captain", log[0].TargetId);
            Assert.Equal(FactionPowers.Keys(FactionPowers.LeaderPowers) + "->recruit,fire", log[0].Detail);
            Assert.Equal("recruit,fire,promote,demote,reassign_unit,run_interviews,post_hiring_ads,mute_faction_chat->recruit,fire",
                log[0].Detail);
            Assert.Equal("solA", log[1].TargetId);
            Assert.Equal("none->recruit", log[1].Detail);
            Assert.Equal("recruit->none", log[2].Detail);
            Assert.Equal("rank:colonel", log[3].TargetId);
            Assert.Equal("50000->20000", log[3].Detail);
            Assert.Equal("colB", log[4].TargetId);
            Assert.Equal("none->5000", log[4].Detail);
            Assert.Equal("5000->none", log[5].Detail);
            Assert.Equal("majA", log[6].TargetId);
            Assert.Equal("minutes=10", log[6].Detail);
        }

        [Test]
        public static void TreasuryDailyLimitBlocksOverspendAndResetsAtIstMidnight()
        {
            // ManualClock starts at 2025-01-01 00:00 UTC = 05:30 IST.
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Ledger ledger = new Ledger(clock);
            string treasury = Ledger.FactionTreasury(FactionId.Army);
            ledger.Mint(treasury, 100000000, "test_setup");
            string to = Ledger.Escrow("supplier");
            Assert.Equal(50000L, f.GetTreasuryDailyLimit("colA"), "rank 1 default");
            Assert.Equal(FactionPowers.UnlimitedRupees, f.GetTreasuryDailyLimit("gen"), "head unlimited");
            Assert.Equal(FactionResult.Ok, f.SpendTreasury("colA", ledger, to, 30000, "rations"));
            Assert.Equal(30000L, ledger.GetBalance(to), "money moved in the same call");
            Assert.Equal(FactionResult.LimitExceeded, f.SpendTreasury("colA", ledger, to, 25000, "fuel"));
            Assert.Equal(FactionResult.Ok, f.SpendTreasury("colA", ledger, to, 20000, "fuel"));
            Assert.Equal(0L, f.GetTreasuryRemainingToday("colA"));
            Assert.Equal(FactionResult.LimitExceeded, f.SpendTreasury("colA", ledger, to, 1, "more"));
            Assert.Equal(FactionResult.Ok, f.SpendTreasury("gen", ledger, to, 10000000, "weapon crates"));
            Assert.Equal(FactionResult.MissingPower, f.SpendTreasury("majA", ledger, to, 1, "no power"));
            Assert.Equal(FactionResult.ReasonRequired, f.SpendTreasury("colA", ledger, to, 1, " "));
            Assert.Equal(FactionResult.InvalidArgument, f.SpendTreasury("colA", ledger, to, 0, "zero"));
            Assert.Equal(FactionResult.InvalidArgument, f.SpendTreasury("colA", null, to, 1, "no ledger"));
            Assert.Equal(10050000L, ledger.GetBalance(to), "failed calls move nothing");

            FactionAuditEntry spend = f.GetAuditLog().First(e => e.Action == "treasury_spend");
            Assert.Equal("colA", spend.ActorId);
            Assert.Equal("amount=30000;to=escrow:supplier;reason=rations;remaining=20000", spend.Detail);

            clock.Advance(new TimeSpan(18, 29, 59));
            Assert.Equal(FactionResult.LimitExceeded, f.SpendTreasury("colA", ledger, to, 1, "23:59:59 IST"));
            clock.AdvanceSeconds(1);
            Assert.Equal(FactionResult.Ok, f.SpendTreasury("colA", ledger, to, 50000, "00:00 IST reset"));

            // Personal grant without a personal limit uses the 10,000 default; a personal limit replaces the rank limit.
            Assert.Equal(FactionResult.Ok, f.GrantPower("gen", "majA", FactionPower.SpendTreasury));
            Assert.Equal(10000L, f.GetTreasuryDailyLimit("majA"));
            Assert.Equal(FactionResult.LimitExceeded, f.SpendTreasury("majA", ledger, to, 10001, "too much"));
            Assert.Equal(FactionResult.Ok, f.SpendTreasury("majA", ledger, to, 10000, "repairs"));
            Assert.Equal(FactionResult.Ok, f.SetPersonTreasuryDailyLimit("gen", "colB", 1000));
            Assert.Equal(FactionResult.LimitExceeded, f.SpendTreasury("colB", ledger, to, 1500, "over personal limit"));
            Assert.Equal(FactionResult.Ok, f.SpendTreasury("colB", ledger, to, 1000, "within personal limit"));
            Assert.Equal(FactionResult.Ok, f.SetTreasuryDailyLimit("gen", Colonel, 0));
            Assert.Equal(FactionResult.LimitExceeded, f.SpendTreasury("colA", ledger, to, 1, "rank limit now 0"));
        }

        [Test]
        public static void TreasurySpendFailingInLedgerKeepsLimitAndLogsNothing()
        {
            ManualClock clock;
            FactionService f = NewArmy(out clock);
            Ledger ledger = new Ledger(clock);
            string treasury = Ledger.FactionTreasury(FactionId.Army);
            ledger.Mint(treasury, 5000, "test_setup");
            string to = Ledger.Escrow("supplier");
            int auditBefore = f.GetAuditLog().Count;

            Assert.Equal(FactionResult.InsufficientFunds, f.SpendTreasury("colA", ledger, to, 6000, "too much"));
            Assert.Equal(FactionResult.InvalidArgument, f.SpendTreasury("colA", ledger, treasury, 100, "to itself"));
            Assert.Equal(0L, f.GetTreasurySpentToday("colA"), "limit not consumed");
            Assert.Equal(50000L, f.GetTreasuryRemainingToday("colA"));
            Assert.Equal(auditBefore, f.GetAuditLog().Count, "no treasury_spend entry for money that never moved");
            Assert.Equal(5000L, ledger.GetBalance(treasury));

            Assert.Equal(FactionResult.Ok, f.SpendTreasury("colA", ledger, to, 5000, "all of it"));
            Assert.Equal(5000L, f.GetTreasurySpentToday("colA"));
            Assert.Equal(0L, ledger.GetBalance(treasury));
        }

        // ---- Mass Media payday ----

        private static FactionService NewMedia(ManualClock clock)
        {
            FactionService f = new FactionService(clock);
            Assert.Equal(FactionResult.Ok, f.AppointHead("admin", FactionId.Media, "mh"));
            Assert.Equal(FactionResult.Ok, f.Hire("mh", "rep", 3));
            Assert.Equal(FactionResult.Ok, f.Hire("mh", "intern", 4));
            Assert.Equal(FactionResult.Ok, f.AppointHead("admin", FactionId.Police, "cmr"));
            return f;
        }

        [Test]
        public static void MediaAdShareSplitAmongMembersAndPaidAtEachOwnPayday()
        {
            ManualClock clock = new ManualClock();
            FactionService f = NewMedia(clock);
            Ledger ledger = new Ledger(clock);
            ledger.Mint(Ledger.CityTreasury, 1000000, "test_setup");
            string mediaTreasury = Ledger.FactionTreasury(FactionId.Media);
            string advertiser = Ledger.Bank("advertiser");
            ledger.Mint(advertiser, 20010, "test_setup");
            PaydayService pay = new PaydayService(f, ledger);
            Assert.Equal(TransferResult.Ok, pay.CreditMediaAdIncome(advertiser, 10005, "ad_chat"));

            // 30% of 10,005 = 3,001 is locked; the treasury gets the other 7,004 in the same call.
            Assert.Equal(10005L, ledger.GetBalance(advertiser));
            Assert.Equal(3001L, ledger.GetBalance(PaydayService.MediaAdShareAccount));
            Assert.Equal(7004L, ledger.GetBalance(mediaTreasury));
            // Split among the 3 Media members = 1,000 each; 1 kept for the next split.
            Assert.Equal(1000L, pay.MediaAdShareOwed("mh"));
            Assert.Equal(1000L, pay.MediaAdShareOwed("rep"));
            Assert.Equal(1000L, pay.MediaAdShareOwed("intern"));
            Assert.Equal(0L, pay.MediaAdShareOwed("cmr"), "police get no ad share");
            Assert.Equal(1L, pay.MediaAdShareUnallocated);

            // Paydays in different ticks: each member gets their own share, not the first one the whole pool.
            List<PaydayRecord> paid = new List<PaydayRecord>();
            Assert.Equal(1, pay.AddPlayTime("mh", 1800, false, paid));
            Assert.Equal(1000L, paid[0].AdShare);
            Assert.Equal(8000L + 1000L, paid[0].Total, "salary plus ad share");
            Assert.Equal(0L, pay.MediaAdShareOwed("mh"));

            pay.AddPlayTime("rep", 900, false, paid);
            pay.AddPlayTime("cmr", 1800, false, paid);
            Assert.Equal(0L, paid[1].AdShare, "police payday");
            pay.AddPlayTime("rep", 900, false, paid);
            Assert.Equal(1000L, paid[2].AdShare, "rep paid at their own later payday");
            Assert.Equal(2600L + 1000L, ledger.GetBalance(Ledger.Bank("rep")));

            pay.AddPlayTime("intern", 1800, true, paid);
            Assert.Equal(3, paid.Count, "AFK intern not paid");
            Assert.Equal(1000L, pay.MediaAdShareOwed("intern"), "share waits for the intern's payday");
            Assert.Equal(3001L - 2000L, ledger.GetBalance(PaydayService.MediaAdShareAccount));
            Assert.Equal(7004L, ledger.GetBalance(mediaTreasury), "ad shares never come out of the treasury");

            // Next income: running-total rounding gives exactly 30% of 20,010 = 6,003 overall.
            Assert.Equal(TransferResult.Ok, pay.CreditMediaAdIncome(advertiser, 10005, "ad_chat"));
            Assert.Equal(7004L + 7003L, ledger.GetBalance(mediaTreasury));
            Assert.Equal(1000L + 1001L, pay.MediaAdShareOwed("intern"));
            Assert.Equal(1001L, pay.MediaAdShareOwed("mh"));
            Assert.Equal(0L, pay.MediaAdShareUnallocated, "3,002 new + 1 carried = 3 x 1,001");

            paid.Clear();
            pay.AddPlayTime("rep", 1800, false, paid);
            pay.AddPlayTime("rep", 1800, false, paid);
            Assert.Equal(1001L, paid[0].AdShare);
            Assert.Equal(0L, paid[1].AdShare, "nothing owed until new income");
            Assert.Equal(2600L, paid[1].Total);
        }

        [Test]
        public static void MediaAdShareLockedFromHeadSpendingAndLeaverForfeits()
        {
            ManualClock clock = new ManualClock();
            FactionService f = NewMedia(clock);
            Ledger ledger = new Ledger(clock);
            ledger.Mint(Ledger.CityTreasury, 1000000, "test_setup");
            string mediaTreasury = Ledger.FactionTreasury(FactionId.Media);
            string advertiser = Ledger.Bank("advertiser");
            ledger.Mint(advertiser, 30010, "test_setup");
            PaydayService pay = new PaydayService(f, ledger);
            Assert.Equal(TransferResult.Ok, pay.CreditMediaAdIncome(advertiser, 30000, "ad_chat"));
            Assert.Equal(3000L, pay.MediaAdShareOwed("rep"));
            Assert.Equal(9000L, ledger.GetBalance(PaydayService.MediaAdShareAccount));
            Assert.Equal(21000L, ledger.GetBalance(mediaTreasury));

            // The head (unlimited daily limit) cannot reach the 9,000 members are owed.
            Assert.Equal(FactionResult.InsufficientFunds, f.SpendTreasury("mh", ledger, Ledger.Escrow("studio"), 21001, "studio rent"));
            Assert.Equal(FactionResult.Ok, f.SpendTreasury("mh", ledger, Ledger.Escrow("studio"), 21000, "studio rent"));
            Assert.Equal(0L, ledger.GetBalance(mediaTreasury));
            Assert.Equal(9000L, ledger.GetBalance(PaydayService.MediaAdShareAccount), "shares untouched by the spend");

            List<PaydayRecord> paid = new List<PaydayRecord>();
            pay.AddPlayTime("rep", 1800, false, paid);
            Assert.Equal(3000L, paid[0].AdShare, "full share paid with an empty treasury");
            Assert.Equal(0L, pay.MediaAdShareOwed("rep"));

            // The intern leaves with 3,000 unpaid: it stays locked and returns to the pool at the next split.
            Assert.Equal(FactionResult.Ok, f.Leave("intern"));
            Assert.Equal(TransferResult.Ok, pay.CreditMediaAdIncome(advertiser, 10, "ad_chat"));
            Assert.Equal(0L, pay.MediaAdShareOwed("intern"));
            // (3,000 forfeited + 3 new) / 2 = 1,501 each, 1 kept; mh still has their own unpaid 3,000.
            Assert.Equal(3000L + 1501L, pay.MediaAdShareOwed("mh"));
            Assert.Equal(1501L, pay.MediaAdShareOwed("rep"));
            Assert.Equal(1L, pay.MediaAdShareUnallocated);
            Assert.Equal(4501L + 1501L + 1L, ledger.GetBalance(PaydayService.MediaAdShareAccount), "locked balance covers every owed share");
            Assert.Equal(7L, ledger.GetBalance(mediaTreasury));
        }

        [Test]
        public static void MediaAdIncomeIsAllOrNothing()
        {
            ManualClock clock = new ManualClock();
            FactionService f = NewMedia(clock);
            Ledger ledger = new Ledger(clock);
            string mediaTreasury = Ledger.FactionTreasury(FactionId.Media);
            string advertiser = Ledger.Bank("advertiser");
            ledger.Mint(advertiser, 500, "test_setup");
            PaydayService pay = new PaydayService(f, ledger);
            int txBefore = ledger.TransactionCount;

            Assert.Equal(TransferResult.InsufficientFunds, pay.CreditMediaAdIncome(advertiser, 501, "ad_chat"));
            Assert.Equal(TransferResult.InvalidAmount, pay.CreditMediaAdIncome(advertiser, 0, "ad_chat"));
            Assert.Equal(TransferResult.MissingReason, pay.CreditMediaAdIncome(advertiser, 100, ""));
            Assert.Equal(TransferResult.InvalidAccount, pay.CreditMediaAdIncome(null, 100, "ad_chat"));
            Assert.Equal(TransferResult.SameAccount, pay.CreditMediaAdIncome(mediaTreasury, 100, "ad_chat"));
            Assert.Equal(TransferResult.SameAccount, pay.CreditMediaAdIncome(PaydayService.MediaAdShareAccount, 100, "ad_chat"));
            Assert.Equal(txBefore, ledger.TransactionCount, "no money moved");
            Assert.Equal(500L, ledger.GetBalance(advertiser));
            Assert.Equal(0L, pay.MediaAdShareOwed("rep"));
            Assert.Equal(0L, pay.MediaAdShareUnallocated);

            // Small ads: the running total keeps the locked share at exactly 30% overall.
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(TransferResult.Ok, pay.CreditMediaAdIncome(advertiser, 1, "ad_chat"));
            }
            Assert.Equal(3L, ledger.GetBalance(PaydayService.MediaAdShareAccount));
            Assert.Equal(7L, ledger.GetBalance(mediaTreasury));
            Assert.Equal(1L, pay.MediaAdShareOwed("rep"));
        }
    }
}
