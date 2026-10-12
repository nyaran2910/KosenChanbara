using UnityEngine;

namespace SchoolFestival.Combat
{
    public readonly struct CombatPose
    {
        public readonly Vector3 PreviousGrip, PreviousBlade, Grip, Blade, BodyCenter, Forward, CutDirection, EndGrip, EndBlade;
        public CombatPose(Vector3 previousGrip, Vector3 previousBlade, Vector3 grip, Vector3 blade,
            Vector3 bodyCenter, Vector3 forward, Vector3 cutDirection, Vector3 endGrip, Vector3 endBlade)
        {
            PreviousGrip = previousGrip; PreviousBlade = previousBlade; Grip = grip; Blade = blade;
            BodyCenter = bodyCenter; Forward = forward; CutDirection = cutDirection;
            EndGrip = endGrip; EndBlade = endBlade;
        }
        public bool Moving => Vector3.Angle(PreviousBlade, Blade) >= .05f || (PreviousGrip - Grip).sqrMagnitude >= .000001f;
        public bool AngularMoving => Vector3.Angle(PreviousBlade, Blade) >= .05f;
    }

    public readonly struct CombatContactResult
    {
        public readonly CombatOutcome Outcome;
        public readonly float Fraction;
        public CombatContactResult(CombatOutcome outcome, float fraction) { Outcome = outcome; Fraction = fraction; }
    }

    // Shared finite geometry and resolution. No mode-specific hit or guard rule.
    public static class CombatContact
    {
        public static Vector3 CutVelocity(Vector3 previousVelocity, Vector3 previousDirection, Vector3 direction, float dt)
            => dt <= 0f ? previousVelocity : Vector3.Lerp(previousVelocity,
                (direction - previousDirection) / dt, 1f - Mathf.Exp(-dt / .06f));

        public const float BladeStart = .55f, BladeLength = 2.1f, BodyRadius = .43f;
        public const float BladePadding = .23f, ThrustExtraPadding = .1f;
        public const float GuardPadding = .34f, GuardBladeStart = .30f;
        // Reconstruction tolerances, not published Nintendo implementation values.
        public const float GuardCrossingAngle = 45f;

        public static Vector3 IncomingCutDirection(CombatPose attack)
        {
            Vector3 direction = Vector3.Cross(Vector3.Cross(attack.PreviousBlade, attack.Blade), attack.Forward);
            if (direction.sqrMagnitude < .0000000001f)
                direction = Vector3.ProjectOnPlane(attack.CutDirection, attack.Forward);
            return direction.normalized;
        }

        // Only the observed portion moving toward the opponent is offensive.
        // Preserve its original frame fraction for impact effects and freezing.
        public static float CutApproachFraction(CombatPose pose)
        {
            Vector3 before = pose.PreviousBlade.normalized, after = pose.Blade.normalized;
            float angle = Vector3.Angle(before, after) * Mathf.Deg2Rad;
            if (angle < .0001f) return 0f;
            Vector3 tangent = angle > Mathf.PI - .001f
                ? Vector3.Slerp(before, after, .5f).normalized
                : (after - before * Mathf.Cos(angle)).normalized;
            Vector3 forward = pose.Forward.normalized;
            float a = Vector3.Dot(before, forward), b = Vector3.Dot(tangent, forward);
            float peakAngle = Mathf.Atan2(b, a);
            float fraction = peakAngle < 0f
                ? (Vector3.Dot(after, forward) > a + .000001f ? 1f : 0f)
                : Mathf.Clamp01(peakAngle / angle);
            return Vector3.Dot(Vector3.Slerp(before, after, fraction).normalized, forward) > a + .000001f
                ? fraction : 0f;
        }

        public static float ThrustForwardFraction(CombatPose pose)
        {
            if (Vector3.Angle(pose.PreviousBlade, pose.Forward) <= 1f) return 0f;
            float low = 0f, high = 1f;
            for (int step = 0; step < 18; step++)
            {
                float middle = (low + high) * .5f;
                if (Vector3.Angle(Vector3.Slerp(pose.PreviousBlade, pose.Blade, middle), pose.Forward) <= 1f)
                    high = middle;
                else low = middle;
            }
            return high;
        }

        private static bool CutCrossesGuard(CombatPose attack, Vector3 guardedBlade, Vector3 defenceForward)
        {
            Vector3 cut = Vector3.ProjectOnPlane(IncomingCutDirection(attack), defenceForward);
            Vector3 blade = Vector3.ProjectOnPlane(guardedBlade.normalized, defenceForward);
            if (cut.sqrMagnitude < .000001f || blade.sqrMagnitude < .01f) return false;
            float angle = Vector3.Angle(cut, blade);
            return Mathf.Min(angle, 180f - angle) >= GuardCrossingAngle - .001f;
        }

        private static void Sample(CombatPose pose, float fraction, out Vector3 grip, out Vector3 blade)
        {
            grip = Vector3.Lerp(pose.PreviousGrip, pose.Grip, fraction);
            blade = Vector3.Slerp(pose.PreviousBlade, pose.Blade, fraction).normalized;
        }

        public static SwordPose SampleSwordPose(CombatPose pose, Quaternion currentRotation, float fraction)
        {
            Sample(pose, Mathf.Clamp01(fraction), out Vector3 grip, out Vector3 blade);
            return new SwordPose(grip, Quaternion.FromToRotation(pose.Blade, blade) * currentRotation);
        }

        public static CombatContactResult Resolve(AttackKind kind, CombatPose attack, CombatPose defence, bool guard)
        {
            if (!AcceptsMovement(kind, attack)
                || Vector3.Dot(defence.BodyCenter - attack.BodyCenter, attack.Forward) <= 0f) return default;
            float until = kind == AttackKind.Cut ? CutApproachFraction(attack) : 1f;
            float from = kind == AttackKind.Thrust ? ThrustForwardFraction(attack) : 0f;
            Sample(attack, from, out Vector3 startingGrip, out Vector3 startingBlade);
            Sample(attack, until, out Vector3 offensiveGrip, out Vector3 offensiveBlade);
            bool body = TrySweptBladeTouchesBody(startingGrip, startingBlade, offensiveGrip,
                offensiveBlade, defence.BodyCenter, out float bodyHit,
                kind == AttackKind.Thrust ? ThrustExtraPadding : 0f);
            bodyHit = from + bodyHit * (until - from);
            bool facing = Vector3.Dot(attack.BodyCenter - defence.BodyCenter, defence.Forward) > 0f;
            bool ready = guard && facing;
            float guardHit = float.PositiveInfinity;
            bool intercepted = ready && TryGuardInterception(kind, attack, defence,
                body ? bodyHit : until, out guardHit, from);
            if (intercepted && (kind == AttackKind.Cut || body))
                return new CombatContactResult(CombatOutcome.GuardSuccess, guardHit);
            if (ready && kind == AttackKind.Cut && body && !TouchesGuardHilt(attack, defence, bodyHit)
                && GuardCoversCut(attack, defence, bodyHit))
                return new CombatContactResult(CombatOutcome.GuardSuccess, bodyHit);
            return body ? new CombatContactResult(ready ? CombatOutcome.GuardBreak : CombatOutcome.Hit, bodyHit) : default;
        }

        public static Vector3 ContactPoint(CombatResult result, CombatPose first, CombatPose second)
        {
            Sample(first, result.ContactFraction, out Vector3 a, out Vector3 u);
            Sample(second, result.ContactFraction, out Vector3 b, out Vector3 v);
            if (result.Outcome == CombatOutcome.Clash)
                return SwordContact.ClosestMidpoint(a + u * BladeStart, a + u * BladeLength,
                    b + v * BladeStart, b + v * BladeLength);
            CombatPose defence = result.Attacker == 0 ? second : first;
            Vector3 grip = result.Attacker == 0 ? a : b, blade = result.Attacker == 0 ? u : v;
            if (result.Outcome == CombatOutcome.GuardSuccess)
            {
                Vector3 guardedGrip = result.Attacker == 0 ? b : a, guardedBlade = result.Attacker == 0 ? v : u;
                SwordContact.ClosestPoints(grip + blade * BladeStart, grip + blade * BladeLength,
                    guardedGrip + guardedBlade * GuardBladeStart, guardedGrip + guardedBlade * BladeLength,
                    out _, out Vector3 onGuard);
                return onGuard;
            }
            SwordContact.ClosestPoints(grip + blade * BladeStart, grip + blade * BladeLength,
                defence.BodyCenter - Vector3.up * .48f, defence.BodyCenter + Vector3.up * .48f,
                out Vector3 onBlade, out Vector3 onBody);
            Vector3 normal = (onBlade - onBody).normalized;
            return onBody + normal * BodyRadius;
        }

        public static bool BladesClash(CombatPose first, CombatPose second)
            => TryBladesClash(first, second, out _);

        private static bool AcceptsMovement(AttackKind kind, CombatPose pose)
            => kind == AttackKind.Cut ? pose.AngularMoving && CutApproachFraction(pose) > 0f : kind == AttackKind.Thrust
                && Vector3.Angle(pose.Blade, pose.Forward) <= 1f
                && Vector3.Dot(pose.Grip - pose.PreviousGrip, pose.Forward) > .0001f;

        public static bool TryBladesClash(CombatPose first, CombatPose second, out float fraction,
            AttackKind firstKind = AttackKind.Cut, AttackKind secondKind = AttackKind.Cut)
        {
            fraction = float.PositiveInfinity;
            if (!AcceptsMovement(firstKind, first) || !AcceptsMovement(secondKind, second)) return false;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Vector3.Angle(first.PreviousBlade, first.Blade),
                Vector3.Angle(second.PreviousBlade, second.Blade)) / 5f), Mathf.CeilToInt(Mathf.Max(
                Vector3.Distance(first.PreviousGrip, first.Grip), Vector3.Distance(second.PreviousGrip, second.Grip)) / .08f));
            float from = Mathf.Max(firstKind == AttackKind.Thrust ? ThrustForwardFraction(first) : 0f,
                secondKind == AttackKind.Thrust ? ThrustForwardFraction(second) : 0f);
            float until = Mathf.Min(firstKind == AttackKind.Cut ? CutApproachFraction(first) : 1f,
                secondKind == AttackKind.Cut ? CutApproachFraction(second) : 1f);
            if (from > until) return false;
            int samples = Mathf.CeilToInt((until - from) * steps);
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Min(from + (float)i / steps, until);
                Vector3 a = Vector3.Lerp(first.PreviousGrip, first.Grip, t), b = Vector3.Lerp(second.PreviousGrip, second.Grip, t);
                Vector3 u = Vector3.Slerp(first.PreviousBlade, first.Blade, t).normalized;
                Vector3 v = Vector3.Slerp(second.PreviousBlade, second.Blade, t).normalized;
                if (SwordContact.DistanceSquared(a + u * BladeStart, a + u * BladeLength,
                    b + v * BladeStart, b + v * BladeLength) <= GuardPadding * GuardPadding)
                { fraction = t; return true; }
            }
            return false;
        }

        public static float PointSegmentDistanceSquared(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            float t = delta.sqrMagnitude < .000001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude);
            return (point - (start + delta * t)).sqrMagnitude;
        }

        public static bool TrySweptBladeTouchesSphere(Vector3 previousGrip, Vector3 previousDirection,
            Vector3 grip, Vector3 direction, Vector3 center, float radius, out float fraction)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(previousDirection, direction) / 5f),
                Mathf.CeilToInt(Vector3.Distance(previousGrip, grip) / .08f));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 origin = Vector3.Lerp(previousGrip, grip, t);
                Vector3 axis = Vector3.Slerp(previousDirection, direction, t).normalized;
                if (PointSegmentDistanceSquared(center, origin + axis * BladeStart, origin + axis * BladeLength)
                    <= radius * radius) { fraction = t; return true; }
            }
            fraction = float.PositiveInfinity; return false;
        }

        public static bool TrySweptBladeTouchesBody(Vector3 previousGrip, Vector3 previousDirection,
            Vector3 grip, Vector3 direction, Vector3 bodyCenter, out float fraction, float extraPadding = 0f)
        {
            fraction = float.PositiveInfinity;
            for (int i = -1; i <= 1; i++)
                if (TrySweptBladeTouchesSphere(previousGrip, previousDirection, grip, direction,
                    bodyCenter + Vector3.up * (i * .48f), BodyRadius + BladePadding + extraPadding,
                    out float hit)) fraction = Mathf.Min(fraction, hit);
            return !float.IsPositiveInfinity(fraction);
        }

        public static bool TrySweptBladeTouchesBlade(Vector3 previousGrip, Vector3 previousDirection,
            Vector3 grip, Vector3 direction, Vector3 guardGrip, Vector3 guardDirection, out float fraction)
        {
            var attack = new CombatPose(previousGrip, previousDirection, grip, direction,
                Vector3.zero, Vector3.forward, Vector3.zero, grip, direction);
            var defence = new CombatPose(guardGrip, guardDirection, guardGrip, guardDirection,
                Vector3.zero, Vector3.back, Vector3.zero, guardGrip, guardDirection);
            return TryGuardInterception(AttackKind.None, attack, defence, 1f, out fraction);
        }

        private static bool TryGuardInterception(AttackKind kind, CombatPose attack, CombatPose defence,
            float until, out float fraction, float from = 0f)
        {
            fraction = float.PositiveInfinity;
            int steps = Mathf.Max(SweepSteps(attack), SweepSteps(defence));
            int samples = Mathf.CeilToInt(Mathf.Clamp01(until - from) * steps);
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Min(from + (float)i / steps, until);
                Sample(attack, t, out Vector3 origin, out Vector3 axis);
                Sample(defence, t, out Vector3 guardGrip, out Vector3 guardAxis);
                if (guardAxis.sqrMagnitude < .5f
                    || kind == AttackKind.Cut && !CutCrossesGuard(attack, guardAxis, defence.Forward)) continue;
                Vector3 guardStart = guardGrip + guardAxis * GuardBladeStart;
                Vector3 guardEnd = guardGrip + guardAxis * BladeLength;
                Vector3 a = origin + axis * BladeStart, b = origin + axis * BladeLength;
                float da = Vector3.Dot(a - guardStart, guardAxis), db = Vector3.Dot(b - guardStart, guardAxis);
                if (da < 0f && db < 0f) continue;
                if (da < 0f) a = Vector3.Lerp(a, b, -da / (db - da));
                else if (db < 0f) b = Vector3.Lerp(a, b, da / (da - db));
                if (SwordContact.DistanceSquared(a, b, guardStart, guardEnd) <= GuardPadding * GuardPadding)
                { fraction = t; return true; }
            }
            return false;
        }

        private static int SweepSteps(CombatPose pose)
            => Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(pose.PreviousBlade, pose.Blade) / 5f),
                Mathf.CeilToInt(Vector3.Distance(pose.PreviousGrip, pose.Grip) / .08f));

        private static bool GuardCoversCut(CombatPose attack, CombatPose defence, float impact)
        {
            Sample(defence, impact, out Vector3 grip, out Vector3 blade);
            Vector3 forward = defence.Forward.normalized;
            if ((grip - defence.BodyCenter).sqrMagnitude > 4f
                || Vector3.Dot(grip - defence.BodyCenter, forward) < .1f
                || !CutCrossesGuard(attack, blade, forward)) return false;
            Sample(attack, impact, out Vector3 attackGrip, out Vector3 attackBlade);
            SwordContact.ClosestPoints(attackGrip + attackBlade * BladeStart,
                attackGrip + attackBlade * BladeLength, defence.BodyCenter - Vector3.up * .48f,
                defence.BodyCenter + Vector3.up * .48f, out Vector3 center, out _);
            Vector3 start = Vector3.ProjectOnPlane(grip + blade * GuardBladeStart - center, forward);
            Vector3 end = Vector3.ProjectOnPlane(grip + blade * BladeLength - center, forward);
            Vector3 cut = Vector3.ProjectOnPlane(IncomingCutDirection(attack), forward).normalized;
            Vector3 normal = Vector3.Cross(forward, cut).normalized;
            float first = Vector3.Dot(start, normal), last = Vector3.Dot(end, normal);
            float divisor = first - last;
            if (Mathf.Abs(divisor) < .000001f) return false;
            float along = first / divisor;
            // The broad tolerance may extend beyond the tip, never backwards
            // through the flat blade base into the excluded hand and guard.
            if (along < 0f) return false;
            return Mathf.Abs(Mathf.Lerp(first, last, Mathf.Min(1f, along))) <= GuardPadding;
        }

        private static bool TouchesGuardHilt(CombatPose attack, CombatPose defence, float until)
        {
            // Exported handle/pommel: -.24..+.20m, guard: +.22..+.29m.
            // Chest-front coverage must never turn a real hilt strike into
            // a successful blade guard.
            int steps = Mathf.Max(SweepSteps(attack), SweepSteps(defence));
            int samples = Mathf.CeilToInt(Mathf.Clamp01(until) * steps);
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Min((float)i / steps, until);
                Sample(attack, t, out Vector3 grip, out Vector3 blade);
                Sample(defence, t, out Vector3 guardGrip, out Vector3 guardBlade);
                Vector3 start = guardGrip - guardBlade * .24f, end = guardGrip + guardBlade * .29f;
                if (SwordContact.DistanceSquared(grip + blade * BladeStart,
                    grip + blade * BladeLength, start, end) <= GuardPadding * GuardPadding) return true;
            }
            return false;
        }

        // Legacy geometry utility: Resolve never predicts unfinished movement.
        public static bool GuardInterceptsCommittedCut(Vector3 grip, Vector3 blade, Vector3 endGrip,
            Vector3 endBlade, Vector3 guardGrip, Vector3 guardBlade)
            => endBlade.sqrMagnitude >= .5f && TrySweptBladeTouchesBlade(grip, blade, endGrip, endBlade, guardGrip, guardBlade, out _);
    }
}
