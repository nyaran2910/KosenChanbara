using System;

// One operator-enabled session; menus, pauses and retries do not grant more time.
namespace SchoolFestival.Combat
{
    public sealed class SessionPlayTimer
    {
        public const int DurationSeconds = 600;
        public bool Armed { get; private set; }
        public bool Started { get; private set; }
        public bool Expired { get; private set; }
        public double EndsAt { get; private set; } = double.PositiveInfinity;
        public double ExpiredAt { get; private set; } = double.PositiveInfinity;

        public void Toggle(double now)
        {
            Armed = !Armed;
            Started = Armed;
            Expired = false;
            EndsAt = Armed ? now + DurationSeconds : double.PositiveInfinity;
            ExpiredAt = double.PositiveInfinity;
        }

        // Returns true only once, when the notification should appear.
        public bool Tick(double now)
        {
            if (!Armed) return false;
            if (Expired || now < EndsAt) return false;
            Expired = true; ExpiredAt = now;
            return true;
        }

        public int RemainingSeconds(double now) => !Started ? DurationSeconds
            : (int)Math.Ceiling(Math.Max(0d, Math.Min(DurationSeconds, EndsAt - now)));
    }

}
