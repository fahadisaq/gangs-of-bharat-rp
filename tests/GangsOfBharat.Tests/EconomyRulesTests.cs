using System.Collections.Generic;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Gameplay.Economy;
using GangsOfBharat.Shared.Gameplay.Market;

namespace GangsOfBharat.Tests
{
    public static class EconomyRulesTests
    {
        [Test]
        public static void LedgerTransferIsAtomicAndLogged()
        {
            ManualClock clock = new ManualClock();
            Ledger ledger = new Ledger(clock);
            Assert.Equal(TransferResult.Ok, ledger.Mint(Ledger.Cash("a"), 1000, "test_setup"));
            clock.AdvanceSeconds(5);
            TransactionRecord rec;
            Assert.Equal(TransferResult.Ok, ledger.Transfer(Ledger.Cash("a"), Ledger.Bank("a"), 400, "atm_deposit", out rec));
            Assert.Equal(600L, ledger.GetBalance(Ledger.Cash("a")));
            Assert.Equal(400L, ledger.GetBalance(Ledger.Bank("a")));
            Assert.Equal(2L, rec.Id);
            Assert.Equal(clock.UtcNow, rec.TimeUtc);
            Assert.Equal("cash:a", rec.From);
            Assert.Equal("bank:a", rec.To);
            Assert.Equal(400L, rec.Amount);
            Assert.Equal("atm_deposit", rec.Reason);
            Assert.Equal(2, ledger.TransactionCount);
            Assert.Equal(0L, ledger.NetBalance(), "money is only moved, never lost");
            Assert.Equal(1000L, ledger.SourceTotalFor("test_setup"));
        }

        [Test]
        public static void LedgerRejectsOverdraftAndBadAmounts()
        {
            Ledger ledger = new Ledger(new ManualClock());
            ledger.Mint(Ledger.Cash("a"), 100, "test_setup");
            Assert.Equal(TransferResult.InsufficientFunds, ledger.Transfer(Ledger.Cash("a"), Ledger.Cash("b"), 101, "upi"));
            Assert.Equal(TransferResult.InvalidAmount, ledger.Transfer(Ledger.Cash("a"), Ledger.Cash("b"), 0, "upi"));
            Assert.Equal(TransferResult.InvalidAmount, ledger.Transfer(Ledger.Cash("a"), Ledger.Cash("b"), -5, "upi"));
            Assert.Equal(TransferResult.SameAccount, ledger.Transfer(Ledger.Cash("a"), Ledger.Cash("a"), 5, "upi"));
            Assert.Equal(TransferResult.InvalidAccount, ledger.Transfer(null, Ledger.Cash("a"), 5, "upi"));
            Assert.Equal(TransferResult.MissingReason, ledger.Transfer(Ledger.Cash("a"), Ledger.Cash("b"), 5, ""));
            Assert.Equal(TransferResult.InsufficientFunds, ledger.Transfer(Ledger.CityTreasury, Ledger.Cash("b"), 1, "payday_salary"));
            Assert.Equal(100L, ledger.GetBalance(Ledger.Cash("a")), "failed transfers change nothing");
            Assert.Equal(0L, ledger.GetBalance(Ledger.Cash("b")));
            Assert.Equal(1, ledger.TransactionCount, "failed transfers are not logged");

            Ledger big = new Ledger(new ManualClock());
            Assert.Equal(TransferResult.Ok, big.Mint(Ledger.Cash("rich"), long.MaxValue, "test_setup"));
            Assert.Equal(TransferResult.Ok, big.Mint(Ledger.Cash("a"), 1, "test_setup"));
            Assert.Equal(TransferResult.Overflow, big.Transfer(Ledger.Cash("a"), Ledger.Cash("rich"), 1, "upi"));
            Assert.Equal(TransferResult.Overflow, big.Mint(Ledger.Cash("b"), 1, "test_setup"), "source cannot underflow");
        }

        [Test]
        public static void LedgerTracksSinksSeparately()
        {
            Ledger ledger = new Ledger(new ManualClock());
            ledger.Mint(Ledger.Cash("a"), 5000, "job_pay");
            Assert.Equal(TransferResult.Ok, ledger.Burn(Ledger.Cash("a"), 500, "ad_chat_fee"));
            Assert.Equal(TransferResult.Ok, ledger.Burn(Ledger.Cash("a"), 200, "fuel"));
            Assert.Equal(TransferResult.Ok, ledger.Burn(Ledger.Cash("a"), 300, "ad_chat_fee"));
            Assert.Equal(1000L, ledger.SinkTotal);
            Assert.Equal(800L, ledger.SinkTotalFor("ad_chat_fee"));
            Assert.Equal(200L, ledger.SinkTotalFor("fuel"));
            Assert.Equal(2, ledger.GetSinkTotalsByReason().Count);
            Assert.Equal(5000L, ledger.SourceTotal);
            Assert.Equal(4000L, ledger.GetBalance(Ledger.Cash("a")));
        }

        private static AuctionHouse NewAuctionHouse(out ManualClock clock, out Ledger ledger, int commissionPercent)
        {
            clock = new ManualClock();
            ledger = new Ledger(clock);
            ledger.Mint(Ledger.Bank("b1"), 1000000, "test_setup");
            ledger.Mint(Ledger.Bank("b2"), 1000000, "test_setup");
            return new AuctionHouse(ledger, clock, 10000, commissionPercent);
        }

        [Test]
        public static void AuctionListingRules()
        {
            ManualClock clock;
            Ledger ledger;
            AuctionHouse ah = NewAuctionHouse(out clock, out ledger, 5);
            AuctionListing l;
            Assert.Equal(AuctionResult.BelowSystemFloor, ah.CreateListing("s", "car:18", 9999, 24, out l));
            Assert.Equal(AuctionResult.InvalidDuration, ah.CreateListing("s", "car:18", 50000, 23, out l));
            Assert.Equal(AuctionResult.InvalidDuration, ah.CreateListing("s", "car:18", 50000, 73, out l));
            Assert.Equal(AuctionResult.Ok, ah.CreateListing("s", "car:18", 50000, 72, out l));
            Assert.Equal(clock.UtcNow.AddHours(72), l.EndsUtc);
            Assert.Equal(AuctionResult.SellerCannotBid, ah.PlaceBid(l.Id, "s", 60000));
            Assert.Equal(AuctionResult.BidTooLow, ah.PlaceBid(l.Id, "b1", 49999), "below seller minimum");
            Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, "b1", 50000));
            Assert.Equal(52500L, AuctionHouse.MinimumNextBid(l), "5% step");
            Assert.Equal(AuctionResult.BidTooLow, ah.PlaceBid(l.Id, "b2", 52499));
            Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, "b2", 52500));

            bool threw = false;
            try
            {
                new AuctionHouse(ledger, clock, 10000, 11);
            }
            catch (System.ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Assert.True(threw, "commission above 10% rejected");
        }

        [Test]
        public static void AuctionEscrowRefundsOutbidBidder()
        {
            ManualClock clock;
            Ledger ledger;
            AuctionHouse ah = NewAuctionHouse(out clock, out ledger, 5);
            AuctionListing l;
            ah.CreateListing("s", "house:7", 100000, 24, out l);
            Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, "b1", 100000));
            Assert.Equal(900000L, ledger.GetBalance(Ledger.Bank("b1")), "bid locked");
            Assert.Equal(100000L, ledger.GetBalance(l.EscrowAccount));
            Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, "b2", 120000));
            Assert.Equal(1000000L, ledger.GetBalance(Ledger.Bank("b1")), "outbid bidder refunded");
            Assert.Equal(880000L, ledger.GetBalance(Ledger.Bank("b2")));
            Assert.Equal(120000L, ledger.GetBalance(l.EscrowAccount));

            ledger.Mint(Ledger.Bank("poor"), 1000, "test_setup");
            Assert.Equal(AuctionResult.InsufficientFunds, ah.PlaceBid(l.Id, "poor", 200000));
            Assert.Equal("b2", l.HighBidderId, "failed bid keeps previous high bidder");
            Assert.Equal(120000L, ledger.GetBalance(l.EscrowAccount));

            // Raising your own bid swaps the escrow amount.
            Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, "b2", 130000));
            Assert.Equal(870000L, ledger.GetBalance(Ledger.Bank("b2")));
            Assert.Equal(130000L, ledger.GetBalance(l.EscrowAccount));
        }

        [Test]
        public static void AuctionAntiSnipeExtendsEnd()
        {
            ManualClock clock;
            Ledger ledger;
            AuctionHouse ah = NewAuctionHouse(out clock, out ledger, 5);
            AuctionListing l;
            ah.CreateListing("s", "plate:MH01", 20000, 24, out l);
            System.DateTime originalEnd = l.EndsUtc;
            clock.Advance(System.TimeSpan.FromHours(23));
            Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, "b1", 20000));
            Assert.Equal(originalEnd, l.EndsUtc, "early bid does not extend");
            clock.Advance(System.TimeSpan.FromMinutes(58) + System.TimeSpan.FromSeconds(30)); // 90 s left
            Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, "b2", 21000));
            Assert.Equal(originalEnd.AddMinutes(2), l.EndsUtc, "late bid adds 2 minutes");
            clock.AdvanceSeconds(90); // past the original end, inside the extension
            Assert.Equal(AuctionResult.AuctionNotEnded, ah.Settle(l.Id));
            Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, "b1", 22050));
            Assert.Equal(originalEnd.AddMinutes(4), l.EndsUtc);
            clock.Advance(System.TimeSpan.FromMinutes(5));
            Assert.Equal(AuctionResult.AuctionEnded, ah.PlaceBid(l.Id, "b2", 30000));
        }

        [Test]
        public static void AuctionSettlementCommissionMath()
        {
            ManualClock clock;
            Ledger ledger;
            AuctionHouse ah = NewAuctionHouse(out clock, out ledger, 7);
            AuctionListing l;
            ah.CreateListing("s", "car:30", 123456, 24, out l);
            ah.PlaceBid(l.Id, "b1", 123456);
            Assert.Equal(AuctionResult.AuctionNotEnded, ah.Settle(l.Id));
            clock.Advance(System.TimeSpan.FromHours(24));
            Assert.Equal(1, ah.SettleEnded());
            long commission = 123456L * 7 / 100; // 8641 (rounded down)
            Assert.Equal(8641L, commission);
            Assert.Equal(AuctionStatus.Sold, l.Status);
            Assert.Equal(commission, l.Commission);
            Assert.Equal(123456L - commission, ledger.GetBalance(Ledger.Bank("s")), "seller gets price minus commission");
            Assert.Equal(commission, ledger.SinkTotalFor("auction_commission"), "commission goes to the sink");
            Assert.Equal(0L, ledger.GetBalance(l.EscrowAccount), "escrow emptied");
            Assert.Equal(AuctionResult.AlreadySettled, ah.Settle(l.Id));

            AuctionListing empty;
            ah.CreateListing("s", "car:31", 50000, 24, out empty);
            clock.Advance(System.TimeSpan.FromHours(24));
            Assert.Equal(AuctionResult.Ok, ah.Settle(empty.Id));
            Assert.Equal(AuctionStatus.Unsold, empty.Status);

            AuctionHouse ten = new AuctionHouse(ledger, clock, 10000, 10);
            Assert.Equal(10000L, ten.CommissionFor(100000));
            Assert.Equal(5L * 10 / 100 + 1000000L / 10, ten.CommissionFor(1000005), "large price without overflow");
            Assert.Equal(0L, ledger.NetBalance());
        }

        [Test]
        public static void AuctionMoneyStaysBalancedAcrossManyBids()
        {
            ManualClock clock;
            Ledger ledger;
            AuctionHouse ah = NewAuctionHouse(out clock, out ledger, 5);
            AuctionListing l;
            ah.CreateListing("s", "rare:1", 10000, 48, out l);
            long bid = 10000;
            List<string> bidders = new List<string> { "b1", "b2" };
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(AuctionResult.Ok, ah.PlaceBid(l.Id, bidders[i % 2], bid));
                bid = AuctionHouse.MinimumNextBid(l);
            }
            long locked = ledger.GetBalance(l.EscrowAccount);
            Assert.Equal(l.HighBid, locked, "escrow holds exactly the high bid");
            Assert.Equal(2000000L, ledger.GetBalance(Ledger.Bank("b1")) + ledger.GetBalance(Ledger.Bank("b2")) + locked);
        }
    }
}
