using System;
using System.Collections.Generic;

namespace GangsOfBharat.Shared.Gameplay.Justice
{
    /// <summary>
    /// Wanted stars and jail timers from docs/design/05-justice-and-crime.md. Wanted level is 0 (not
    /// wanted) or 1-5. Jail time is 5-30 minutes of play time: the timer only runs while the prisoner is
    /// online, so logging out does not shorten it.
    /// </summary>
    public sealed class WantedService
    {
        public const int MaxLevel = 5;
        public const int MinJailMinutes = 5;
        public const int MaxJailMinutes = 30;

        /// <summary>Jail minutes by wanted level 1-5. DRAFT - balancing.</summary>
        private static readonly int[] JailMinutesByLevel = { 0, 5, 10, 15, 20, 30 };

        private readonly Dictionary<string, int> _wanted = new Dictionary<string, int>();
        private readonly Dictionary<string, double> _jailRemainingSeconds = new Dictionary<string, double>();

        public static int JailMinutesForLevel(int level)
        {
            return JailMinutesByLevel[ClampLevel(level)];
        }

        public static int ClampLevel(int level)
        {
            return level < 0 ? 0 : (level > MaxLevel ? MaxLevel : level);
        }

        public int GetLevel(string playerId)
        {
            int v;
            return playerId != null && _wanted.TryGetValue(playerId, out v) ? v : 0;
        }

        /// <summary>Adds stars for a crime; the result is clamped to 1-5. Returns the new level.</summary>
        public int AddStars(string playerId, int stars)
        {
            if (string.IsNullOrEmpty(playerId) || stars <= 0)
            {
                return GetLevel(playerId);
            }
            int current = GetLevel(playerId);
            int next = stars > MaxLevel ? MaxLevel : current + stars;
            next = ClampLevel(next < 1 ? 1 : next);
            _wanted[playerId] = next;
            return next;
        }

        /// <summary>Sets the level directly (clamped 0-5; 0 clears).</summary>
        public void SetLevel(string playerId, int level)
        {
            int l = ClampLevel(level);
            if (l == 0)
            {
                _wanted.Remove(playerId);
            }
            else
            {
                _wanted[playerId] = l;
            }
        }

        public void Clear(string playerId)
        {
            _wanted.Remove(playerId);
        }

        public bool IsJailed(string playerId)
        {
            return playerId != null && _jailRemainingSeconds.ContainsKey(playerId);
        }

        public double JailRemainingSeconds(string playerId)
        {
            double s;
            return playerId != null && _jailRemainingSeconds.TryGetValue(playerId, out s) ? s : 0;
        }

        /// <summary>Arrest: jails for the minutes mapped from the current wanted level and clears it.</summary>
        public int Arrest(string playerId)
        {
            int level = GetLevel(playerId);
            if (level == 0)
            {
                return 0;
            }
            int minutes = JailMinutesForLevel(level);
            Jail(playerId, minutes);
            return minutes;
        }

        /// <summary>Court verdict or arrest: jail time clamped to 5-30 minutes. Clears the wanted level.</summary>
        public void Jail(string playerId, int minutes)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentNullException("playerId");
            }
            int m = minutes < MinJailMinutes ? MinJailMinutes : (minutes > MaxJailMinutes ? MaxJailMinutes : minutes);
            _jailRemainingSeconds[playerId] = m * 60.0;
            _wanted.Remove(playerId);
        }

        /// <summary>
        /// Advances a jailed player's timer by online play time. Returns true when this call released
        /// the player.
        /// </summary>
        public bool TickOnline(string playerId, double seconds)
        {
            double left;
            if (playerId == null || !_jailRemainingSeconds.TryGetValue(playerId, out left))
            {
                return false;
            }
            if (seconds > 0 && !double.IsNaN(seconds))
            {
                left -= seconds;
            }
            if (left <= 0)
            {
                _jailRemainingSeconds.Remove(playerId);
                return true;
            }
            _jailRemainingSeconds[playerId] = left;
            return false;
        }
    }
}
