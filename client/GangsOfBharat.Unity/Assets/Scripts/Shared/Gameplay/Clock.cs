using System;

namespace GangsOfBharat.Shared.Gameplay
{
    /// <summary>Time source for rule timers. Inject a <see cref="ManualClock"/> in tests.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }

    /// <summary>Clock that only moves when told to. Used by tests and offline simulations.</summary>
    public sealed class ManualClock : IClock
    {
        private DateTime _now;

        public ManualClock(DateTime startUtc)
        {
            _now = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        }

        public ManualClock() : this(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        {
        }

        public DateTime UtcNow
        {
            get { return _now; }
        }

        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException("delta", "clock cannot go backwards");
            }
            _now = _now + delta;
        }

        public void AdvanceSeconds(double seconds)
        {
            Advance(TimeSpan.FromSeconds(seconds));
        }
    }
}
