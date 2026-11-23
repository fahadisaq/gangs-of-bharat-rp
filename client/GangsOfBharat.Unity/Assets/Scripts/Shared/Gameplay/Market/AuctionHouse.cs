using System;
using System.Collections.Generic;
using GangsOfBharat.Shared.Gameplay.Economy;

namespace GangsOfBharat.Shared.Gameplay.Market
{
    public enum AuctionResult : byte
    {
        Ok = 0,
        BelowSystemFloor = 1,
        InvalidDuration = 2,
        ListingNotFound = 3,
        AuctionEnded = 4,
        AuctionNotEnded = 5,
        BidTooLow = 6,
        SellerCannotBid = 7,
        InsufficientFunds = 8,
        AlreadySettled = 9,
        InvalidArgument = 10,
    }

    public enum AuctionStatus : byte
    {
        Open = 0,
        Sold = 1,
        Unsold = 2,
    }

    public sealed class AuctionListing
    {
        public readonly long Id;
        public readonly string SellerId;
        public readonly string ItemId;
        public readonly long MinimumPrice;
        public readonly DateTime CreatedUtc;
        public DateTime EndsUtc { get; internal set; }
        public string HighBidderId { get; internal set; }
        public long HighBid { get; internal set; }
        public int BidCount { get; internal set; }
        public AuctionStatus Status { get; internal set; }
        public long Commission { get; internal set; }
        public long SellerProceeds { get; internal set; }

        internal AuctionListing(long id, string sellerId, string itemId, long minimumPrice, DateTime createdUtc, DateTime endsUtc)
        {
            Id = id;
            SellerId = sellerId;
            ItemId = itemId;
            MinimumPrice = minimumPrice;
            CreatedUtc = createdUtc;
            EndsUtc = endsUtc;
        }

        public string EscrowAccount
        {
            get { return Ledger.Escrow("auction:" + Id.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        }
    }

    /// <summary>
    /// Auction house rules from docs/design/08-market-houses.md. Bids are locked from the bidder's bank
    /// account into a per-listing escrow account through the <see cref="Ledger"/>; an outbid bidder is
    /// refunded at once. Item ownership transfer is the caller's job after a Sold settlement.
    /// </summary>
    public sealed class AuctionHouse
    {
        public const int MinDurationHours = 24;
        public const int MaxDurationHours = 72;
        public const int BidStepPercent = 5;
        public const int MinCommissionPercent = 5;
        public const int MaxCommissionPercent = 10;
        public static readonly TimeSpan AntiSnipeWindow = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan AntiSnipeExtension = TimeSpan.FromMinutes(2);
        /// <summary>Placeholder until balancing.</summary>
        public const long DefaultSystemFloorRupees = 1000;

        private readonly Ledger _ledger;
        private readonly IClock _clock;
        private readonly Dictionary<long, AuctionListing> _listings = new Dictionary<long, AuctionListing>();
        private readonly long _systemFloor;
        private readonly int _commissionPercent;
        private long _nextId = 1;

        public AuctionHouse(Ledger ledger, IClock clock, long systemFloorRupees, int commissionPercent)
        {
            if (ledger == null)
            {
                throw new ArgumentNullException("ledger");
            }
            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }
            if (systemFloorRupees < 1)
            {
                throw new ArgumentOutOfRangeException("systemFloorRupees");
            }
            if (commissionPercent < MinCommissionPercent || commissionPercent > MaxCommissionPercent)
            {
                throw new ArgumentOutOfRangeException("commissionPercent", "commission must be 5-10%");
            }
            _ledger = ledger;
            _clock = clock;
            _systemFloor = systemFloorRupees;
            _commissionPercent = commissionPercent;
        }

        public AuctionHouse(Ledger ledger, IClock clock) : this(ledger, clock, DefaultSystemFloorRupees, MinCommissionPercent)
        {
        }

        public long SystemFloorRupees
        {
            get { return _systemFloor; }
        }

        public int CommissionPercent
        {
            get { return _commissionPercent; }
        }

        public AuctionListing Get(long id)
        {
            AuctionListing l;
            return _listings.TryGetValue(id, out l) ? l : null;
        }

        /// <summary>Smallest valid next bid: the start price, or the current bid plus 5% (rounded up).</summary>
        public static long MinimumNextBid(AuctionListing l)
        {
            if (l.BidCount == 0)
            {
                return l.MinimumPrice;
            }
            long step = CeilPercent(l.HighBid, BidStepPercent);
            if (step < 1)
            {
                step = 1;
            }
            return l.HighBid > long.MaxValue - step ? long.MaxValue : l.HighBid + step;
        }

        /// <summary>Commission on a final price, rounded down.</summary>
        public long CommissionFor(long price)
        {
            return price / 100 * _commissionPercent + price % 100 * _commissionPercent / 100;
        }

        public AuctionResult CreateListing(string sellerId, string itemId, long minimumPrice, int durationHours, out AuctionListing listing)
        {
            listing = null;
            if (string.IsNullOrEmpty(sellerId) || string.IsNullOrEmpty(itemId))
            {
                return AuctionResult.InvalidArgument;
            }
            if (minimumPrice < _systemFloor)
            {
                return AuctionResult.BelowSystemFloor;
            }
            if (durationHours < MinDurationHours || durationHours > MaxDurationHours)
            {
                return AuctionResult.InvalidDuration;
            }
            DateTime now = _clock.UtcNow;
            listing = new AuctionListing(_nextId++, sellerId, itemId, minimumPrice, now, now.AddHours(durationHours));
            _listings[listing.Id] = listing;
            return AuctionResult.Ok;
        }

        public AuctionResult PlaceBid(long listingId, string bidderId, long amount)
        {
            AuctionListing l = Get(listingId);
            if (l == null)
            {
                return AuctionResult.ListingNotFound;
            }
            if (string.IsNullOrEmpty(bidderId))
            {
                return AuctionResult.InvalidArgument;
            }
            DateTime now = _clock.UtcNow;
            if (l.Status != AuctionStatus.Open || now >= l.EndsUtc)
            {
                return AuctionResult.AuctionEnded;
            }
            if (bidderId == l.SellerId)
            {
                return AuctionResult.SellerCannotBid;
            }
            if (amount < MinimumNextBid(l))
            {
                return AuctionResult.BidTooLow;
            }
            string escrow = l.EscrowAccount;
            // Lock the new bid first; if that fails nothing has changed.
            if (_ledger.Transfer(Ledger.Bank(bidderId), escrow, amount, "auction_bid_escrow") != TransferResult.Ok)
            {
                return AuctionResult.InsufficientFunds;
            }
            if (l.BidCount > 0)
            {
                _ledger.Transfer(escrow, Ledger.Bank(l.HighBidderId), l.HighBid, "auction_outbid_refund");
            }
            l.HighBidderId = bidderId;
            l.HighBid = amount;
            l.BidCount++;
            if (l.EndsUtc - now <= AntiSnipeWindow)
            {
                l.EndsUtc = l.EndsUtc + AntiSnipeExtension;
            }
            return AuctionResult.Ok;
        }

        /// <summary>
        /// Closes an ended auction: pays the seller the final price minus commission and sends the
        /// commission to the sink. Without bids the listing becomes Unsold.
        /// </summary>
        public AuctionResult Settle(long listingId)
        {
            AuctionListing l = Get(listingId);
            if (l == null)
            {
                return AuctionResult.ListingNotFound;
            }
            if (l.Status != AuctionStatus.Open)
            {
                return AuctionResult.AlreadySettled;
            }
            if (_clock.UtcNow < l.EndsUtc)
            {
                return AuctionResult.AuctionNotEnded;
            }
            if (l.BidCount == 0)
            {
                l.Status = AuctionStatus.Unsold;
                return AuctionResult.Ok;
            }
            long commission = CommissionFor(l.HighBid);
            long proceeds = l.HighBid - commission;
            string escrow = l.EscrowAccount;
            if (commission > 0)
            {
                _ledger.Transfer(escrow, Ledger.Sink, commission, "auction_commission");
            }
            if (proceeds > 0)
            {
                _ledger.Transfer(escrow, Ledger.Bank(l.SellerId), proceeds, "auction_sale");
            }
            l.Commission = commission;
            l.SellerProceeds = proceeds;
            l.Status = AuctionStatus.Sold;
            return AuctionResult.Ok;
        }

        /// <summary>Settles every ended auction. Call periodically from the server.</summary>
        public int SettleEnded()
        {
            DateTime now = _clock.UtcNow;
            List<long> due = new List<long>();
            foreach (AuctionListing l in _listings.Values)
            {
                if (l.Status == AuctionStatus.Open && now >= l.EndsUtc)
                {
                    due.Add(l.Id);
                }
            }
            for (int i = 0; i < due.Count; i++)
            {
                Settle(due[i]);
            }
            return due.Count;
        }

        private static long CeilPercent(long value, int percent)
        {
            long whole = value / 100 * percent;
            long rest = value % 100 * percent;
            return whole + (rest + 99) / 100;
        }
    }
}
