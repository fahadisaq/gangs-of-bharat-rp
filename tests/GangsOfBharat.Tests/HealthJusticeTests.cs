using System.Collections.Generic;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Gameplay.Economy;
using GangsOfBharat.Shared.Gameplay.Factions;
using GangsOfBharat.Shared.Gameplay.Health;
using GangsOfBharat.Shared.Gameplay.Justice;

namespace GangsOfBharat.Tests
{
    public static class HealthJusticeTests
    {
        private static InjuryService NewInjury(out ManualClock clock, out Ledger ledger)
        {
            clock = new ManualClock();
            ledger = new Ledger(clock);
            FactionService f = new FactionService(clock);
            f.AppointHead("admin", FactionId.Hospital, "cmo");
            f.Hire("cmo", "medic", 4);
            f.Hire("cmo", "medic_off", 4);
            f.AppointHead("admin", FactionId.Police, "cop");
            f.SetLocker(FactionId.Hospital, 0, 0, 10);
            f.SetLocker(FactionId.Police, 0, 0, 10);
            Assert.Equal(FactionResult.Ok, f.StartDuty("medic", 0, 0));
            Assert.Equal(FactionResult.Ok, f.StartDuty("cop", 0, 0));
            return new InjuryService(ledger, clock, f);
        }

        [Test]
        public static void ParamedicReviveWithinThreeMinutes()
        {
            ManualClock clock;
            Ledger ledger;
            InjuryService inj = NewInjury(out clock, out ledger);
            HospitalRespawn late;
            Assert.Equal(InjuryResult.NotKnockedDown, inj.Revive("medic", "p1", out late));
            Assert.Equal(InjuryResult.Ok, inj.Knockdown("p1"));
            Assert.Equal(InjuryResult.AlreadyKnockedDown, inj.Knockdown("p1"));
            clock.AdvanceSeconds(179);
            Assert.Equal(InjuryResult.NotParamedic, inj.Revive("medic_off", "p1", out late), "off-duty paramedic");
            Assert.Equal(InjuryResult.NotParamedic, inj.Revive("cop", "p1", out late), "police is not hospital");
            Assert.Equal(InjuryResult.Ok, inj.Revive("medic", "p1", out late));
            Assert.False(inj.IsKnockedDown("p1"));
            Assert.True(late == null);
        }

        [Test]
        public static void ReviveRejectedAfterThreeMinutes()
        {
            ManualClock clock;
            Ledger ledger;
            InjuryService inj = NewInjury(out clock, out ledger);
            ledger.Mint(Ledger.Cash("p1"), 3000, "test_setup");
            inj.Knockdown("p1");
            clock.AdvanceSeconds(180);
            HospitalRespawn late;
            Assert.Equal(InjuryResult.ReviveWindowExpired, inj.Revive("medic", "p1", out late));
            Assert.NotNull(late, "overdue automatic respawn performed");
            Assert.Equal(RespawnCause.Automatic, late.Cause);
            Assert.False(inj.IsKnockedDown("p1"));
        }

        [Test]
        public static void HospitalButtonUnlocksAtFiftySeconds()
        {
            ManualClock clock;
            Ledger ledger;
            InjuryService inj = NewInjury(out clock, out ledger);
            inj.Knockdown("p1");
            clock.AdvanceSeconds(49);
            HospitalRespawn r;
            Assert.False(inj.CanUseHospitalButton("p1"));
            Assert.Equal(InjuryResult.HospitalButtonNotReady, inj.GoToHospital("p1", out r), "rejected at 49 s");
            Assert.True(inj.IsKnockedDown("p1"));
            clock.AdvanceSeconds(1);
            Assert.True(inj.CanUseHospitalButton("p1"));
            Assert.Equal(InjuryResult.Ok, inj.GoToHospital("p1", out r));
            Assert.Equal(RespawnCause.HospitalButton, r.Cause);
            Assert.False(inj.IsKnockedDown("p1"));
        }

        [Test]
        public static void AutomaticHospitalRespawnAtThreeMinutes()
        {
            ManualClock clock;
            Ledger ledger;
            InjuryService inj = NewInjury(out clock, out ledger);
            inj.Knockdown("p1");
            clock.AdvanceSeconds(100);
            inj.Knockdown("p2");
            clock.AdvanceSeconds(79.9);
            Assert.Equal(0, inj.Update().Count, "179.9 s: not yet");
            clock.AdvanceSeconds(0.1);
            List<HospitalRespawn> done = inj.Update();
            Assert.Equal(1, done.Count, "180 s");
            Assert.Equal("p1", done[0].PlayerId);
            Assert.Equal(RespawnCause.Automatic, done[0].Cause);
            Assert.True(inj.IsKnockedDown("p2"));
            clock.AdvanceSeconds(100);
            Assert.Equal(1, inj.Update().Count);
            Assert.False(inj.IsKnockedDown("p2"));
        }

        [Test]
        public static void HospitalRespawnChargesFeeAndDropsPocketCash()
        {
            ManualClock clock;
            Ledger ledger;
            InjuryService inj = NewInjury(out clock, out ledger);
            ledger.Mint(Ledger.Cash("p1"), 3000, "test_setup");
            ledger.Mint(Ledger.Bank("p1"), 10000, "test_setup");
            inj.Knockdown("p1");
            clock.AdvanceSeconds(60);
            HospitalRespawn r;
            inj.GoToHospital("p1", out r);
            Assert.Equal(1000L, r.FeeCharged);
            Assert.Equal(500L, r.FeeToHospital, "50% to hospital");
            Assert.Equal(500L, r.FeeToTreasury);
            Assert.Equal(2000L, r.CashLost);
            Assert.Equal(0L, ledger.GetBalance(Ledger.Cash("p1")), "pocket cash lost");
            Assert.Equal(10000L, ledger.GetBalance(Ledger.Bank("p1")), "bank is safe");
            Assert.Equal(500L, ledger.GetBalance(Ledger.FactionTreasury(FactionId.Hospital)));
            Assert.Equal(500L, ledger.GetBalance(Ledger.CityTreasury));
            Assert.Equal(2000L, ledger.SinkTotalFor("hospital_cash_lost"));

            // Low pocket cash: the rest of the fee comes from the bank.
            ledger.Mint(Ledger.Cash("p2"), 300, "test_setup");
            ledger.Mint(Ledger.Bank("p2"), 5000, "test_setup");
            inj.Knockdown("p2");
            clock.AdvanceSeconds(180);
            HospitalRespawn r2 = inj.Update()[0];
            Assert.Equal(1000L, r2.FeeCharged);
            Assert.Equal(0L, r2.CashLost);
            Assert.Equal(4300L, ledger.GetBalance(Ledger.Bank("p2")));
            Assert.Equal(1000L, ledger.GetBalance(Ledger.FactionTreasury(FactionId.Hospital)));

            // Broke player is still treated.
            inj.Knockdown("broke");
            clock.AdvanceSeconds(180);
            HospitalRespawn r3 = inj.Update()[0];
            Assert.Equal(0L, r3.FeeCharged);
            Assert.Equal(0L, ledger.NetBalance());
        }

        [Test]
        public static void WantedLevelClampedAndJailMapping()
        {
            WantedService w = new WantedService();
            Assert.Equal(0, w.GetLevel("p"));
            Assert.Equal(2, w.AddStars("p", 2));
            Assert.Equal(5, w.AddStars("p", 10), "clamped to 5");
            Assert.Equal(5, w.AddStars("p", 1));
            Assert.Equal(5, w.AddStars("q", int.MaxValue), "no overflow");
            Assert.Equal(1, w.AddStars("r", 1));
            Assert.Equal(1, w.GetLevel("r"));
            w.SetLevel("r", 9);
            Assert.Equal(5, w.GetLevel("r"));
            w.SetLevel("r", -3);
            Assert.Equal(0, w.GetLevel("r"));
            Assert.Equal(5, WantedService.JailMinutesForLevel(1));
            Assert.Equal(10, WantedService.JailMinutesForLevel(2));
            Assert.Equal(15, WantedService.JailMinutesForLevel(3));
            Assert.Equal(20, WantedService.JailMinutesForLevel(4));
            Assert.Equal(30, WantedService.JailMinutesForLevel(5));
            Assert.Equal(30, WantedService.JailMinutesForLevel(8), "clamped");
            int prev = 0;
            for (int l = 1; l <= 5; l++)
            {
                int m = WantedService.JailMinutesForLevel(l);
                Assert.True(m >= 5 && m <= 30 && m > prev, "5-30 minutes, increasing");
                prev = m;
            }
        }

        [Test]
        public static void JailReleaseTimerCountsOnlyOnlineTime()
        {
            WantedService w = new WantedService();
            w.AddStars("p", 3);
            Assert.Equal(15, w.Arrest("p"));
            Assert.Equal(0, w.GetLevel("p"), "arrest clears wanted");
            Assert.True(w.IsJailed("p"));
            Assert.Near(900, w.JailRemainingSeconds("p"), 1e-9);
            Assert.False(w.TickOnline("p", 600));
            // Offline: no ticks, time stays.
            Assert.Near(300, w.JailRemainingSeconds("p"), 1e-9);
            Assert.False(w.TickOnline("p", 299));
            Assert.True(w.TickOnline("p", 1), "released at 15 minutes");
            Assert.False(w.IsJailed("p"));
            Assert.Equal(0, w.Arrest("clean"), "not wanted -> no jail");

            w.Jail("v", 1);
            Assert.Near(5 * 60, w.JailRemainingSeconds("v"), 1e-9, "verdict clamped to 5 min");
            w.Jail("v2", 90);
            Assert.Near(30 * 60, w.JailRemainingSeconds("v2"), 1e-9, "verdict clamped to 30 min");
        }
    }
}
