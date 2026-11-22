using System;
using System.Collections.Generic;
using GangsOfBharat.Shared.Gameplay.Economy;
using GangsOfBharat.Shared.Gameplay.Factions;

namespace GangsOfBharat.Shared.Gameplay.Health
{
    public enum InjuryResult : byte
    {
        Ok = 0,
        NotKnockedDown = 1,
        AlreadyKnockedDown = 2,
        /// <summary>Revive attempted at or after 3 minutes; the patient has been sent to hospital.</summary>
        ReviveWindowExpired = 3,
        /// <summary>"Go to hospital" pressed before 50 seconds.</summary>
        HospitalButtonNotReady = 4,
        NotParamedic = 5,
        InvalidArgument = 6,
    }

    public enum RespawnCause : byte
    {
        HospitalButton = 0,
        Automatic = 1,
    }

    public sealed class HospitalRespawn
    {
        public readonly string PlayerId;
        public readonly RespawnCause Cause;
        public readonly long FeeCharged;
        public readonly long FeeToHospital;
        public readonly long FeeToTreasury;
        public readonly long CashLost;

        public HospitalRespawn(string playerId, RespawnCause cause, long feeCharged, long feeToHospital, long feeToTreasury, long cashLost)
        {
            PlayerId = playerId;
            Cause = cause;
            FeeCharged = feeCharged;
            FeeToHospital = feeToHospital;
            FeeToTreasury = feeToTreasury;
            CashLost = cashLost;
        }
    }

    /// <summary>
    /// Knockdown flow from docs/design/09-injury-health.md: a paramedic can revive within 3 minutes,
    /// "Go to hospital" unlocks at 50 seconds, automatic hospital respawn at 3 minutes. On hospital
    /// respawn the hospital fee is charged (50% to the Hospital faction treasury, the rest to the city
    /// treasury) and all pocket cash is lost to the sink; bank money is safe.
    /// Fee collection: from pocket cash first, then the bank; capped at what the player has so a broke
    /// player is still treated.
    /// </summary>
    public sealed class InjuryService
    {
        public static readonly TimeSpan ReviveWindow = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan HospitalButtonDelay = TimeSpan.FromSeconds(50);
        public static readonly TimeSpan AutoRespawnAfter = TimeSpan.FromMinutes(3);
        public const int HospitalFeeSharePercent = 50;
        /// <summary>Placeholder until balancing.</summary>
        public const long DefaultHospitalFeeRupees = 1000;

        private readonly Ledger _ledger;
        private readonly IClock _clock;
        private readonly FactionService _factions;
        private readonly Dictionary<string, DateTime> _knockedAt = new Dictionary<string, DateTime>();
        private long _hospitalFee = DefaultHospitalFeeRupees;

        /// <param name="factions">When set, only on-duty Hospital members can revive.</param>
        public InjuryService(Ledger ledger, IClock clock, FactionService factions)
        {
            if (ledger == null)
            {
                throw new ArgumentNullException("ledger");
            }
            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }
            _ledger = ledger;
            _clock = clock;
            _factions = factions;
        }

        public long HospitalFeeRupees
        {
            get { return _hospitalFee; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value");
                }
                _hospitalFee = value;
            }
        }

        public bool IsKnockedDown(string playerId)
        {
            return playerId != null && _knockedAt.ContainsKey(playerId);
        }

        public TimeSpan TimeDown(string playerId)
        {
            DateTime at;
            return playerId != null && _knockedAt.TryGetValue(playerId, out at) ? _clock.UtcNow - at : TimeSpan.Zero;
        }

        public bool CanUseHospitalButton(string playerId)
        {
            return IsKnockedDown(playerId) && TimeDown(playerId) >= HospitalButtonDelay;
        }

        public InjuryResult Knockdown(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return InjuryResult.InvalidArgument;
            }
            if (_knockedAt.ContainsKey(playerId))
            {
                return InjuryResult.AlreadyKnockedDown;
            }
            _knockedAt[playerId] = _clock.UtcNow;
            return InjuryResult.Ok;
        }

        /// <summary>
        /// Paramedic revive on the spot, allowed while less than 3 minutes have passed. A late attempt
        /// performs the overdue automatic respawn and returns <see cref="InjuryResult.ReviveWindowExpired"/>.
        /// </summary>
        public InjuryResult Revive(string paramedicId, string patientId, out HospitalRespawn lateRespawn)
        {
            lateRespawn = null;
            if (!IsKnockedDown(patientId))
            {
                return InjuryResult.NotKnockedDown;
            }
            if (TimeDown(patientId) >= ReviveWindow)
            {
                lateRespawn = Respawn(patientId, RespawnCause.Automatic);
                return InjuryResult.ReviveWindowExpired;
            }
            if (string.IsNullOrEmpty(paramedicId) || paramedicId == patientId)
            {
                return InjuryResult.NotParamedic;
            }
            if (_factions != null)
            {
                FactionMember m = _factions.GetMember(paramedicId);
                if (m == null || m.Faction != FactionId.Hospital || !m.OnDuty)
                {
                    return InjuryResult.NotParamedic;
                }
            }
            _knockedAt.Remove(patientId);
            return InjuryResult.Ok;
        }

        /// <summary>The "Go to hospital" button, available from 50 seconds after knockdown.</summary>
        public InjuryResult GoToHospital(string playerId, out HospitalRespawn respawn)
        {
            respawn = null;
            if (!IsKnockedDown(playerId))
            {
                return InjuryResult.NotKnockedDown;
            }
            if (TimeDown(playerId) < HospitalButtonDelay)
            {
                return InjuryResult.HospitalButtonNotReady;
            }
            respawn = Respawn(playerId, RespawnCause.HospitalButton);
            return InjuryResult.Ok;
        }

        /// <summary>Call from the server tick. Respawns everyone down for 3 minutes or more.</summary>
        public List<HospitalRespawn> Update()
        {
            List<string> due = null;
            DateTime now = _clock.UtcNow;
            foreach (KeyValuePair<string, DateTime> kv in _knockedAt)
            {
                if (now - kv.Value >= AutoRespawnAfter)
                {
                    if (due == null)
                    {
                        due = new List<string>();
                    }
                    due.Add(kv.Key);
                }
            }
            List<HospitalRespawn> result = new List<HospitalRespawn>(due == null ? 0 : due.Count);
            if (due != null)
            {
                for (int i = 0; i < due.Count; i++)
                {
                    result.Add(Respawn(due[i], RespawnCause.Automatic));
                }
            }
            return result;
        }

        private HospitalRespawn Respawn(string playerId, RespawnCause cause)
        {
            _knockedAt.Remove(playerId);
            string cash = Ledger.Cash(playerId);
            string bank = Ledger.Bank(playerId);

            long fee = _hospitalFee;
            long fromCash = Math.Min(fee, _ledger.GetBalance(cash));
            long fromBank = Math.Min(fee - fromCash, _ledger.GetBalance(bank));
            long charged = fromCash + fromBank;
            long toHospital = charged * HospitalFeeSharePercent / 100;
            long toTreasury = charged - toHospital;

            // Split each payer's part between the two receivers so every transfer is covered.
            long hospitalLeft = toHospital;
            long hospFromCash = Math.Min(hospitalLeft, fromCash);
            hospitalLeft -= hospFromCash;
            long hospFromBank = hospitalLeft;
            Pay(cash, Ledger.FactionTreasury(FactionId.Hospital), hospFromCash);
            Pay(cash, Ledger.CityTreasury, fromCash - hospFromCash);
            Pay(bank, Ledger.FactionTreasury(FactionId.Hospital), hospFromBank);
            Pay(bank, Ledger.CityTreasury, fromBank - hospFromBank);

            long cashLost = _ledger.GetBalance(cash);
            if (cashLost > 0 && _ledger.Burn(cash, cashLost, "hospital_cash_lost") != TransferResult.Ok)
            {
                cashLost = 0;
            }
            return new HospitalRespawn(playerId, cause, charged, toHospital, toTreasury, cashLost);
        }

        private void Pay(string from, string to, long amount)
        {
            if (amount > 0)
            {
                _ledger.Transfer(from, to, amount, "hospital_fee");
            }
        }
    }
}
