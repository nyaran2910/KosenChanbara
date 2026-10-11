namespace SchoolFestival.Combat
{
    public enum AttackKind { None, Cut, Thrust }

    // One gate for every player: input starts a stroke, contact consumes it.
    public sealed class AttackStroke
    {
        public const double CutSeconds = .7, ThrustSeconds = ThrustPose.DurationSeconds;
        public const float ThrustAngle = 25f;
        private bool baseline, armed, previousGuard;
        private int generation;
        private float previousAngle;
        private double until;
        public AttackKind Kind { get; private set; }
        public bool Active => Kind != AttackKind.None;

        public void Reset() { baseline = armed = false; Kind = AttackKind.None; }
        public void Consume() { armed = false; Kind = AttackKind.None; }

        public bool Update(CombatInput input, double now, bool blocked = false)
        {
            if (!input.Valid || blocked) { Reset(); return false; }
            if (!baseline || generation != input.PoseGeneration || previousGuard != input.GuardHeld)
            {
                baseline = true; generation = input.PoseGeneration; previousGuard = input.GuardHeld;
                previousAngle = input.ForwardAngle; armed = input.ForwardAngle >= DuelMatch.RearmAngle;
                Kind = AttackKind.None;
                // A sensor thrust is an explicit, freshly detected impulse,
                // so it can hand off controllers without reusing an old cut.
                if (input.ThrustRequested && !input.GuardHeld && input.ForwardAngle <= ThrustAngle)
                { Kind = AttackKind.Thrust; until = now + ThrustSeconds; armed = false; }
                return Active;
            }
            if (input.ForwardAngle >= DuelMatch.RearmAngle) { armed = true; Kind = AttackKind.None; }
            if (input.GuardHeld) Kind = AttackKind.None;
            else if (input.ThrustRequested && input.ForwardAngle <= ThrustAngle && !Active)
            {
                Kind = AttackKind.Thrust; until = now + ThrustSeconds; armed = false;
            }
            else if (armed && input.ForwardAngle <= DuelMatch.AttackAngle
                && input.AngularSpeed >= DuelMatch.MinimumSpeed && input.ForwardAngle < previousAngle)
            {
                Kind = AttackKind.Cut; until = now + CutSeconds; armed = false;
            }
            if (now > until || (Kind == AttackKind.Cut && input.ForwardAngle > previousAngle + .25f)
                || (Kind == AttackKind.Thrust && input.ForwardAngle > ThrustAngle)) Kind = AttackKind.None;
            previousAngle = input.ForwardAngle;
            return Active;
        }
    }
}
