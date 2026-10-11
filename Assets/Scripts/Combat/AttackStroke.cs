using UnityEngine;

namespace SchoolFestival.Combat
{
    public enum AttackKind { None, Cut, Thrust }

    // A stroke follows observed movement, not an absolute raised-sword pose.
    // These recognition thresholds are project tuning, not Nintendo constants.
    public sealed class AttackStroke
    {
        public const double CutSeconds = .9, ThrustSeconds = ThrustPose.DurationSeconds;
        public const double QuietSeconds = .06;
        public const float ThrustAngle = 25f, MinimumCutSweep = 15f, CutForwardAngle = 65f;
        private bool baseline, previousGuard, consumed;
        private int generation;
        private float previousAngle, travel;
        private double lastTime, until, quietSince = double.NaN;
        private Vector3 swingAxis, previousForward = Vector3.forward;
        public AttackKind Kind { get; private set; }
        public AttackKind StartedKind { get; private set; }
        public bool Active => Kind != AttackKind.None;

        public void Reset()
        {
            baseline = consumed = false;
            Kind = StartedKind = AttackKind.None;
            travel = 0f; swingAxis = Vector3.zero; quietSince = double.NaN;
        }

        public void Consume() { consumed = true; travel = 0f; Kind = AttackKind.None; }

        public void Interrupt()
        {
            consumed |= Active;
            travel = 0f; Kind = StartedKind = AttackKind.None;
        }

        // Angle-only callers use the same recognizer with an observed vertical
        // arc. Gameplay supplies the real blade endpoints through the overload.
        public bool Update(CombatInput input, double now, bool blocked = false)
        {
            Vector3 previous = Quaternion.AngleAxis(-previousAngle, Vector3.right) * Vector3.forward;
            Vector3 current = Quaternion.AngleAxis(-input.ForwardAngle, Vector3.right) * Vector3.forward;
            return Update(input, new CombatPose(Vector3.zero, previous, Vector3.zero, current,
                Vector3.zero, Vector3.forward, Vector3.zero, Vector3.zero, Vector3.forward), now, blocked);
        }

        public bool Update(CombatInput input, CombatPose pose, double now, bool blocked = false)
        {
            StartedKind = AttackKind.None;
            if (!input.Valid) { Reset(); return false; }
            if (!baseline || generation != input.PoseGeneration || now < lastTime)
            {
                Reset(); baseline = true;
                generation = input.PoseGeneration; previousGuard = input.GuardHeld;
                previousAngle = input.ForwardAngle; lastTime = now; previousForward = pose.Forward;
                if (!blocked && input.ThrustRequested && !input.GuardHeld && input.ForwardAngle <= ThrustAngle) StartThrust(now);
                return Active;
            }
            if (previousGuard != input.GuardHeld)
            {
                // A guard transition can reposition the sword, but cannot
                // rearm the same consumed cut. Ignore its geometry jump and
                // keep observing real rest or reversal on subsequent frames.
                consumed |= Active;
                Kind = AttackKind.None; travel = 0f; quietSince = double.NaN;
                previousGuard = input.GuardHeld;
                previousAngle = input.ForwardAngle; lastTime = now;
                swingAxis = Quaternion.FromToRotation(previousForward, pose.Forward) * swingAxis;
                previousForward = pose.Forward;
                if (!blocked && !input.GuardHeld && input.ThrustRequested && input.ForwardAngle <= ThrustAngle) StartThrust(now);
                return Active;
            }
            double elapsed = now - lastTime;
            lastTime = now; previousAngle = input.ForwardAngle;
            swingAxis = Quaternion.FromToRotation(previousForward, pose.Forward) * swingAxis;
            previousForward = pose.Forward;
            float step = Vector3.Angle(pose.PreviousBlade, pose.Blade);
            float measuredSpeed = elapsed > .000001 ? step / (float)elapsed : input.AngularSpeed;
            float speed = Mathf.Min(input.AngularSpeed, measuredSpeed);
            Vector3 axis = Vector3.Cross(pose.PreviousBlade, pose.Blade).normalized;
            if (Kind != AttackKind.Thrust && step >= .05f && swingAxis.sqrMagnitude > .5f && Vector3.Dot(axis, swingAxis) < .25f) ResetMotion();
            if (step >= .05f) swingAxis = axis;
            if (blocked || input.GuardHeld)
            {
                // Keep a consumed stroke consumed through hit stop. Observe
                // guard/reversal/rest, but never accumulate a queued attack.
                if (Active) consumed = true;
                Kind = AttackKind.None; travel = 0f;
                if (step < .05f || speed < 5f)
                {
                    if (double.IsNaN(quietSince)) quietSince = now;
                    if (now - quietSince >= QuietSeconds) ResetMotion();
                }
                else quietSince = double.NaN;
                return false;
            }
            if (input.ThrustRequested && input.ForwardAngle <= ThrustAngle && Kind != AttackKind.Thrust)
            { StartThrust(now); return true; }
            if (Kind == AttackKind.Thrust)
            {
                if (now > until || input.ForwardAngle > ThrustAngle) { Kind = AttackKind.None; consumed = true; }
                return Active;
            }

            if (step < .05f || speed < 5f)
            {
                if (double.IsNaN(quietSince)) quietSince = now;
                if (now - quietSince >= QuietSeconds) ResetMotion();
                if (now > until) Kind = AttackKind.None;
                return false;
            }
            quietSince = double.NaN;
            if (Kind == AttackKind.Cut && now > until) { Kind = AttackKind.None; consumed = true; }
            if (!consumed && !Active)
            {
                if (speed >= DuelMatch.MinimumSpeed) travel += step;
                else travel = 0f;
                if (travel >= MinimumCutSweep && CrossesFront(pose))
                { Kind = StartedKind = AttackKind.Cut; until = now + CutSeconds; }
            }
            return Active;
        }

        private void StartThrust(double now)
        { Kind = StartedKind = AttackKind.Thrust; until = now + ThrustSeconds; consumed = false; travel = 0f; swingAxis = Vector3.zero; }

        private void ResetMotion()
        { Kind = AttackKind.None; consumed = false; travel = 0f; swingAxis = Vector3.zero; quietSince = double.NaN; }

        private static bool CrossesFront(CombatPose pose)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(pose.PreviousBlade, pose.Blade) / 5f));
            for (int i = 0; i <= steps; i++)
                if (Vector3.Angle(Vector3.Slerp(pose.PreviousBlade, pose.Blade, (float)i / steps), pose.Forward) <= CutForwardAngle)
                    return true;
            return false;
        }
    }
}
