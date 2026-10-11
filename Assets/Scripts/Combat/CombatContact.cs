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
        public const float GuardPadding = .34f;

        public static CombatContactResult Resolve(AttackKind kind, CombatPose attack, CombatPose defence, bool guard)
        {
            if (!AcceptsMovement(kind, attack)
                || Vector3.Dot(defence.BodyCenter - attack.BodyCenter, attack.Forward) <= 0f) return default;
            bool body = TrySweptBladeTouchesBody(attack.PreviousGrip, attack.PreviousBlade, attack.Grip,
                attack.Blade, defence.BodyCenter, out float bodyHit,
                kind == AttackKind.Thrust ? ThrustExtraPadding : 0f);
            bool facing = Vector3.Dot(attack.BodyCenter - defence.BodyCenter, defence.Forward) > 0f;
            bool ready = guard && facing;
            if (ready && kind == AttackKind.Thrust && body
                && TrySweptBladeTouchesBlade(attack.PreviousGrip, attack.PreviousBlade, attack.Grip, attack.Blade,
                    defence.Grip, defence.Blade, out _))
                return new CombatContactResult(CombatOutcome.GuardSuccess, bodyHit);
            // A held guard blocks through its finite blade. Cut velocity includes
            // forward travel and authored aiming corrections, so an extra
            // velocity-angle gate unfairly rejects real sword interceptions.
            if (ready && kind == AttackKind.Cut)
            {
                if (TrySweptBladeTouchesBlade(attack.PreviousGrip, attack.PreviousBlade, attack.Grip, attack.Blade,
                    defence.Grip, defence.Blade, out float guardHit))
                    return new CombatContactResult(CombatOutcome.GuardSuccess, guardHit);
                if (body && !TouchesGuardHilt(attack, defence)
                    && GuardInterceptsCommittedCut(attack.Grip, attack.Blade, attack.EndGrip,
                    attack.EndBlade, defence.Grip, defence.Blade))
                    return new CombatContactResult(CombatOutcome.GuardSuccess, bodyHit);
            }
            return body ? new CombatContactResult(ready ? CombatOutcome.GuardBreak : CombatOutcome.Hit, bodyHit) : default;
        }

        public static bool BladesClash(CombatPose first, CombatPose second)
            => TryBladesClash(first, second, out _);

        private static bool AcceptsMovement(AttackKind kind, CombatPose pose)
            => kind == AttackKind.Cut ? pose.AngularMoving : kind == AttackKind.Thrust
                && Vector3.Dot(pose.Grip - pose.PreviousGrip, pose.Forward) > .0001f;

        public static bool TryBladesClash(CombatPose first, CombatPose second, out float fraction,
            AttackKind firstKind = AttackKind.Cut, AttackKind secondKind = AttackKind.Cut)
        {
            fraction = float.PositiveInfinity;
            if (!AcceptsMovement(firstKind, first) || !AcceptsMovement(secondKind, second)) return false;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Vector3.Angle(first.PreviousBlade, first.Blade),
                Vector3.Angle(second.PreviousBlade, second.Blade)) / 5f), Mathf.CeilToInt(Mathf.Max(
                Vector3.Distance(first.PreviousGrip, first.Grip), Vector3.Distance(second.PreviousGrip, second.Grip)) / .08f));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
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
            fraction = float.PositiveInfinity;
            Vector3 guardAxis = guardDirection.normalized;
            if (guardAxis.sqrMagnitude < .5f) return false;
            Vector3 guardStart = guardGrip + guardAxis * BladeStart;
            Vector3 guardEnd = guardGrip + guardAxis * BladeLength;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(previousDirection, direction) / 5f),
                Mathf.CeilToInt(Vector3.Distance(previousGrip, grip) / .08f));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 origin = Vector3.Lerp(previousGrip, grip, t);
                Vector3 axis = Vector3.Slerp(previousDirection, direction, t).normalized;
                Vector3 a = origin + axis * BladeStart, b = origin + axis * BladeLength;
                // A flat base prevents the generous blade radius extending into
                // the hilt/hand. Only the blade side of this plane can block.
                float da = Vector3.Dot(a - guardStart, guardAxis);
                float db = Vector3.Dot(b - guardStart, guardAxis);
                if (da < 0f && db < 0f) continue;
                if (da < 0f) a = Vector3.Lerp(a, b, -da / (db - da));
                else if (db < 0f) b = Vector3.Lerp(a, b, da / (da - db));
                if (SwordContact.DistanceSquared(a, b, guardStart, guardEnd) <= GuardPadding * GuardPadding)
                { fraction = t; return true; }
            }
            return false;
        }

        private static bool TouchesGuardHilt(CombatPose attack, CombatPose defence)
        {
            // Exported handle/pommel: -.24..+.20m, guard: +.22..+.29m.
            // A predicted continuation must never turn a real hilt strike
            // into a successful blade guard.
            Vector3 axis = defence.Blade.normalized;
            Vector3 start = defence.Grip - axis * .24f, end = defence.Grip + axis * .29f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(attack.PreviousBlade, attack.Blade) / 5f),
                Mathf.CeilToInt(Vector3.Distance(attack.PreviousGrip, attack.Grip) / .08f));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 grip = Vector3.Lerp(attack.PreviousGrip, attack.Grip, t);
                Vector3 blade = Vector3.Slerp(attack.PreviousBlade, attack.Blade, t).normalized;
                if (SwordContact.DistanceSquared(grip + blade * BladeStart,
                    grip + blade * BladeLength, start, end) <= GuardPadding * GuardPadding) return true;
            }
            return false;
        }

        public static bool GuardInterceptsCommittedCut(Vector3 grip, Vector3 blade, Vector3 endGrip,
            Vector3 endBlade, Vector3 guardGrip, Vector3 guardBlade)
            => endBlade.sqrMagnitude >= .5f && TrySweptBladeTouchesBlade(grip, blade, endGrip, endBlade, guardGrip, guardBlade, out _);
    }
}
