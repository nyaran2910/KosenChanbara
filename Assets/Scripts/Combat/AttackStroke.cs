using UnityEngine;

namespace SchoolFestival.Combat
{
    public enum AttackKind { None, Cut, Thrust }

    // A stroke follows observed movement, not an absolute raised-sword pose.
    // These recognition thresholds are project tuning, not Nintendo constants.
    public sealed class AttackStroke
    {
        public const double CutSeconds = .9, ThrustSeconds = ThrustPose.DurationSeconds;
        public const double QuietSeconds = .06, ReverseReadySeconds = .12;
        public const float ThrustAngle = 25f, MinimumCutSweep = 25f, MinimumCutSpeed = 75f, MinimumCutMotion = .25f, ReverseCutSpeed = 150f, RearmSideAngle = 25f, CutForwardAngle = 65f;
        private bool baseline, previousGuard, consumed, reversePrepared;
        private int generation;
        private float previousAngle, travel, bladeTravel, takebackTravel, requiredSpeed = MinimumCutSpeed;
        private double lastTime, until, thrustStartedAt, quietSince = double.NaN;
        private Vector3 swingAxis, prepareSide, previousForward = Vector3.forward;
        public AttackKind Kind { get; private set; }
        public AttackKind StartedKind { get; private set; }
        public bool Active => Kind != AttackKind.None;

        public void Reset()
        {
            baseline = consumed = false;
            Kind = StartedKind = AttackKind.None;
            travel = bladeTravel = takebackTravel = 0f; swingAxis = prepareSide = Vector3.zero; quietSince = double.NaN;
            reversePrepared = false; requiredSpeed = MinimumCutSpeed;
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
            StartedKind = AttackKind.None;
            if (!input.Valid) { Reset(); return false; }
            if (!baseline || generation != input.PoseGeneration || now < lastTime)
            {
                Reset(); baseline = true;
                generation = input.PoseGeneration; previousGuard = input.GuardHeld;
                previousAngle = input.ForwardAngle; lastTime = now; previousForward = pose.Forward;
                if (!blocked && input.ThrustRequested && !input.GuardHeld && input.ForwardAngle <= ThrustAngle) StartThrust(now);
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
                if (!blocked && !input.GuardHeld && input.ThrustRequested && input.ForwardAngle <= ThrustAngle) StartThrust(now);
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
            if (Kind != AttackKind.Thrust && step >= .05f && swingAxis.sqrMagnitude > .5f && Vector3.Dot(axis, swingAxis) < .25f)
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
            if (Kind == AttackKind.Thrust && now > until) { Kind = AttackKind.None; consumed = true; }
            if (input.ThrustRequested && input.ForwardAngle <= ThrustAngle && Kind != AttackKind.Thrust)
            { StartThrust(now); return AttackAllowed(now); }
            if (Kind == AttackKind.Thrust)
            {
                if (now > until || input.ForwardAngle > ThrustAngle) { Kind = AttackKind.None; consumed = true; }
                return AttackAllowed(now);
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
            if (Kind == AttackKind.Cut && speed < requiredSpeed)
            { Kind = AttackKind.None; consumed = true; return false; }
            float approach = CombatContact.CutApproachFraction(pose);
            if (approach <= 0f)
            {
                // Pulling away is preparation, not another damaging stroke.
                consumed |= Active;
                Kind = AttackKind.None; travel = bladeTravel = 0f;
                return false;
            }
            if (!consumed && !Active)
            {
                if (speed >= requiredSpeed)
                {
                    if (travel <= 0f) prepareSide = Vector3.ProjectOnPlane(pose.PreviousBlade, pose.Forward).normalized;
                    travel += step * approach;
                    const float bladeMiddle = (CombatContact.BladeStart + CombatContact.BladeLength) * .5f;
                    Vector3 before = pose.PreviousGrip + pose.PreviousBlade.normalized * bladeMiddle;
                    Vector3 after = Vector3.Lerp(pose.PreviousGrip, pose.Grip, approach)
                        + Vector3.Slerp(pose.PreviousBlade, pose.Blade, approach).normalized * bladeMiddle;
                    bladeTravel += Vector3.Distance(before, after);
                }
                else travel = bladeTravel = 0f;
                if (travel >= MinimumCutSweep - .001f && bladeTravel >= MinimumCutMotion - .00001f && CrossesFront(pose))
                { Kind = StartedKind = AttackKind.Cut; RememberCutSide(); until = now + CutSeconds; }
            }
            return AttackAllowed(now);
        }

        private void StartThrust(double now)
        { Kind = StartedKind = AttackKind.Thrust; until = now + ThrustSeconds; thrustStartedAt = now; consumed = false; travel = bladeTravel = takebackTravel = 0f; swingAxis = prepareSide = Vector3.zero; reversePrepared = false; requiredSpeed = MinimumCutSpeed; }

        private void RememberCutSide()
        {
            if (Kind != AttackKind.Cut) return;
            Vector3 side = -Vector3.Cross(swingAxis, previousForward);
            if (side.sqrMagnitude > .00001f) prepareSide = side.normalized;
        }

        private void ResetMotion()
        { Kind = AttackKind.None; consumed = reversePrepared = false; travel = bladeTravel = takebackTravel = 0f;
          swingAxis = prepareSide = Vector3.zero; requiredSpeed = MinimumCutSpeed; quietSince = double.NaN; }

        private bool RearmFromPreparation(CombatPose pose, float speed)
        {
            if (!consumed) return false;
            float side = prepareSide.sqrMagnitude > .5f ? Vector3.Dot(pose.Blade.normalized, prepareSide) : 0f;
            float ready = Mathf.Sin(RearmSideAngle * Mathf.Deg2Rad);
            float oldSide = Vector3.Dot(pose.PreviousBlade.normalized, prepareSide);
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
            if (reversePrepared && speed >= ReverseCutSpeed && CombatContact.CutApproachFraction(pose) > 0f)
            {
                ResetMotion(); requiredSpeed = ReverseCutSpeed;
                prepareSide = Vector3.ProjectOnPlane(pose.PreviousBlade, pose.Forward).normalized;
                // The opposite stance already rested deliberately. Count this
                // new fast stroke, so a coarse first frame cannot discard most
                // of a short counter while fine frames still recognize it.
                return false;
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
            float opposite = Vector3.Dot(pose.Blade.normalized, prepareSide);
            if (opposite <= -Mathf.Sin(RearmSideAngle * Mathf.Deg2Rad)
                && now - quietSince >= ReverseReadySeconds) reversePrepared = true;
        }

        private bool AttackAllowed(double now)
            => Kind == AttackKind.Cut || Kind == AttackKind.Thrust
                && now - thrustStartedAt >= ThrustPose.ChamberSeconds
                && now - thrustStartedAt < ThrustPose.ChamberSeconds + ThrustPose.DriveSeconds;

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
