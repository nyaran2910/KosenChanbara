using UnityEngine;

namespace SchoolFestival.Combat
{
    public enum AttackKind { None, Cut }

    // A stroke follows observed movement, not an absolute raised-sword pose.
    // These recognition thresholds are project tuning, not Nintendo constants.
    public sealed class AttackStroke
    {
        public const double CutSeconds = .9;
        public const float MinimumHitSweep = 60f;
        public float HitStartFraction { get; private set; } = float.PositiveInfinity;
        public const double QuietSeconds = .06;
        public const float MinimumCutSweep = 25f, MinimumCutSpeed = 150f, MinimumCutMotion = .25f, RearmSideAngle = 25f, CutForwardAngle = 65f;
        private bool baseline, previousGuard, consumed, oppositeSideReached;
        private int generation;
        private float previousAngle, travel, bladeTravel, takebackTravel;
        private double lastTime, until, quietSince = double.NaN;
        private Vector3 swingAxis, prepareSide, previousForward = Vector3.forward;
        public AttackKind Kind { get; private set; }
        public AttackKind StartedKind { get; private set; }
        public bool Active => Kind != AttackKind.None;

        public void Reset()
        {
            HitStartFraction = float.PositiveInfinity;
            baseline = consumed = false;
            Kind = StartedKind = AttackKind.None;
            travel = bladeTravel = takebackTravel = 0f; swingAxis = prepareSide = Vector3.zero; quietSince = double.NaN;
            oppositeSideReached = false;
        }

        public void Consume()
        {
            RememberCutSide();
            consumed = true; takebackTravel = 0f; travel = bladeTravel = 0f; Kind = AttackKind.None;
        }

        public void Interrupt()
        {
            RememberCutSide();
            if (Active) takebackTravel = 0f;
            consumed |= Active;
            travel = bladeTravel = 0f; Kind = StartedKind = AttackKind.None;
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
            HitStartFraction = float.PositiveInfinity;
            StartedKind = AttackKind.None;
            if (!input.Valid) { Reset(); return false; }
            if (!baseline || generation != input.PoseGeneration || now < lastTime)
            {
                Reset(); baseline = true;
                generation = input.PoseGeneration; previousGuard = input.GuardHeld;
                previousAngle = input.ForwardAngle; lastTime = now; previousForward = pose.Forward;
                return AttackAllowed(now);
            }
            if (previousGuard != input.GuardHeld)
            {
                // A guard transition can reposition the sword, but cannot
                // rearm the same consumed cut. Ignore its geometry jump and
                // keep observing real rest or reversal on subsequent frames.
                consumed |= Active;
                Kind = AttackKind.None; travel = bladeTravel = 0f; quietSince = double.NaN;
                previousGuard = input.GuardHeld;
                previousAngle = input.ForwardAngle; lastTime = now;
                Quaternion turn = Quaternion.FromToRotation(previousForward, pose.Forward);
                swingAxis = turn * swingAxis; prepareSide = turn * prepareSide;
                previousForward = pose.Forward;
                return AttackAllowed(now);
            }
            double elapsed = now - lastTime;
            lastTime = now; previousAngle = input.ForwardAngle;
            Quaternion facingTurn = Quaternion.FromToRotation(previousForward, pose.Forward);
            swingAxis = facingTurn * swingAxis; prepareSide = facingTurn * prepareSide;
            previousForward = pose.Forward;
            float step = Vector3.Angle(pose.PreviousBlade, pose.Blade);
            float measuredSpeed = elapsed > .000001 ? step / (float)elapsed : input.AngularSpeed;
            float speed = Mathf.Min(input.AngularSpeed, measuredSpeed);
            Vector3 axis = Vector3.Cross(pose.PreviousBlade, pose.Blade).normalized;
            if (step >= .05f && swingAxis.sqrMagnitude > .5f && Vector3.Dot(axis, swingAxis) < .25f)
            {
                if (Active || consumed) { consumed = true; Kind = AttackKind.None; travel = bladeTravel = 0f; }
                else ResetMotion();
            }
            if (step >= .05f) swingAxis = axis;
            if (blocked || input.GuardHeld)
            {
                // Keep a consumed stroke consumed through hit stop. Observe
                // guard/reversal/rest, but never accumulate a queued attack.
                if (Active) consumed = true;
                Kind = AttackKind.None; travel = bladeTravel = 0f;
                if (consumed) RearmFromPreparation(pose, speed);
                if (step < .05f || speed < 5f)
                {
                    if (double.IsNaN(quietSince)) quietSince = now;
                    ObserveRest(pose, now);
                }
                else quietSince = double.NaN;
                return false;
            }
            if (RearmFromPreparation(pose, speed)) return false;

            if (step < .05f || speed < 5f)
            {
                if (double.IsNaN(quietSince)) quietSince = now;
                ObserveRest(pose, now);
                if (now > until) Kind = AttackKind.None;
                return false;
            }
            quietSince = double.NaN;
            if (Kind == AttackKind.Cut && now > until) { Kind = AttackKind.None; consumed = true; }
            if (Kind == AttackKind.Cut && speed < MinimumCutSpeed)
            { Kind = AttackKind.None; consumed = true; return false; }
            float approach = CombatContact.CutApproachFraction(pose);
            if (approach <= 0f)
            {
                // Pulling away is preparation, not another damaging stroke.
                consumed |= Active;
                Kind = AttackKind.None; travel = bladeTravel = 0f;
                return false;
            }
            if (!consumed)
            {
                float before = travel;
                if (speed >= MinimumCutSpeed)
                {
                    if (travel <= 0f) prepareSide = Vector3.ProjectOnPlane(pose.PreviousBlade, pose.Forward).normalized;
                    travel += step * approach;
                    const float bladeMiddle = (CombatContact.BladeStart + CombatContact.BladeLength) * .5f;
                    Vector3 beforePoint = pose.PreviousGrip + pose.PreviousBlade.normalized * bladeMiddle;
                    Vector3 after = Vector3.Lerp(pose.PreviousGrip, pose.Grip, approach)
                        + Vector3.Slerp(pose.PreviousBlade, pose.Blade, approach).normalized * bladeMiddle;
                    bladeTravel += Vector3.Distance(beforePoint, after);
                }
                else travel = bladeTravel = 0f;
                if (travel >= MinimumHitSweep - .001f)
                    HitStartFraction = Mathf.Clamp01((MinimumHitSweep - before) / Mathf.Max(step, .00001f));
                if (!Active && travel >= MinimumCutSweep - .001f && bladeTravel >= MinimumCutMotion - .00001f && CrossesFront(pose))
                { Kind = StartedKind = AttackKind.Cut; RememberCutSide(); until = now + CutSeconds; }
            }
            return AttackAllowed(now);
        }

        private void RememberCutSide()
        {
            if (Kind != AttackKind.Cut) return;
            Vector3 side = -Vector3.Cross(swingAxis, previousForward);
            if (side.sqrMagnitude > .00001f) prepareSide = side.normalized;
        }

        private void ResetMotion()
        { Kind = AttackKind.None; consumed = oppositeSideReached = false; travel = bladeTravel = takebackTravel = 0f;
          swingAxis = prepareSide = Vector3.zero; quietSince = double.NaN; }

        private bool RearmFromPreparation(CombatPose pose, float speed)
        {
            if (!consumed) return false;
            float side = prepareSide.sqrMagnitude > .5f ? Vector3.Dot(pose.Blade.normalized, prepareSide) : 0f;
            float ready = Mathf.Sin(RearmSideAngle * Mathf.Deg2Rad);
            float oldSide = Vector3.Dot(pose.PreviousBlade.normalized, prepareSide);
            // A completed cut has its own preparation on the far side. Once
            // the sword has actually reached it, the return across the front
            // is a fresh cut, including left/right and up/down reversals.
            if (oldSide <= -ready + .00001f || side <= -ready + .00001f) oppositeSideReached = true;
            if (oppositeSideReached && side > oldSide + .00001f && speed >= MinimumCutSpeed
                && CombatContact.CutApproachFraction(pose) > 0f)
            {
                ResetMotion();
                prepareSide = Vector3.ProjectOnPlane(pose.PreviousBlade, pose.Forward).normalized;
                return false;
            }
            if (side > oldSide + .00001f) takebackTravel += Vector3.Angle(pose.PreviousBlade, pose.Blade);
            else if (side < oldSide - .00001f) takebackTravel = 0f;
            bool reachedOldSide = side >= ready - .00001f && takebackTravel >= MinimumCutSweep - .001f;
            if (reachedOldSide || prepareSide.sqrMagnitude < .5f
                && Vector3.Angle(pose.Blade, pose.Forward) >= RearmSideAngle)
            {
                // Reaching the old preparation side is harmless in its entirety,
                // even if a coarse return frame also crossed the opponent.
                ResetMotion(); return true;
            }
            return false;
        }

        private void ObserveRest(CombatPose pose, double now)
        {
            if (!consumed)
            {
                if (now - quietSince >= QuietSeconds) ResetMotion();
                return;
            }
            if (prepareSide.sqrMagnitude < .5f)
            {
                if (now - quietSince >= QuietSeconds) ResetMotion();
                return;
            }
            if (Vector3.Dot(pose.Blade.normalized, prepareSide) >= Mathf.Sin(RearmSideAngle * Mathf.Deg2Rad)
                && now - quietSince >= QuietSeconds)
            { ResetMotion(); return; }
            if (Vector3.Dot(pose.Blade.normalized, prepareSide) <= -Mathf.Sin(RearmSideAngle * Mathf.Deg2Rad))
                oppositeSideReached = true;
        }

        private bool AttackAllowed(double now) => Kind == AttackKind.Cut;

        private static bool CrossesFront(CombatPose pose)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(pose.PreviousBlade, pose.Blade) / 5f));
            for (int i = 0; i <= steps; i++)
                if (Vector3.Angle(Vector3.Slerp(pose.PreviousBlade, pose.Blade, (float)i / steps), pose.Forward) <= CutForwardAngle + .001f)
                    return true;
            return false;
        }
    }
}
