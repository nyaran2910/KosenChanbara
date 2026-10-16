using UnityEngine;

namespace SchoolFestival.Combat
{
    public readonly struct CombatExchangeResult
    {
        public readonly CombatResult Contact;
        public readonly float Fraction;
        public CombatExchangeResult(CombatResult contact, float fraction)
        { Contact = contact; Fraction = fraction; }
    }

    // Both modes resolve the first physical contact in the same exchange.
    public static class CombatExchange
    {
        public static CombatExchangeResult Resolve(AttackKind firstKind, AttackKind secondKind,
            CombatPose first, CombatPose second, bool firstGuard, bool secondGuard,
            bool firstRecovering = false, bool secondRecovering = false,
            bool firstInvulnerable = false, bool secondInvulnerable = false,
            float firstHitStart = 0f, float secondHitStart = 0f)
        {
            // Recovery prevents this fighter's offence, not the opponent's turn
            // or a fresh defensive guard.
            if (firstRecovering) firstKind = AttackKind.None;
            if (secondRecovering) secondKind = AttackKind.None;
            CombatContactResult a = CombatContact.Resolve(firstKind, first, second, secondGuard, firstHitStart);
            CombatContactResult b = CombatContact.Resolve(secondKind, second, first, firstGuard, secondHitStart);
            if (secondInvulnerable && CombatReaction.IsHit(a.Outcome)) a = default;
            if (firstInvulnerable && CombatReaction.IsHit(b.Outcome)) b = default;
            if (firstKind != AttackKind.None && secondKind != AttackKind.None
                && CombatContact.TryBladesClash(first, second, out float clash, firstKind, secondKind, Mathf.Max(firstHitStart, secondHitStart))
                && (a.Outcome == CombatOutcome.None || clash <= a.Fraction)
                && (b.Outcome == CombatOutcome.None || clash <= b.Fraction))
                return new CombatExchangeResult(new CombatResult(CombatOutcome.Clash, -1, -1, 0f, contactFraction: clash), clash);

            if (a.Outcome == CombatOutcome.None && b.Outcome == CombatOutcome.None) return default;
            if (a.Outcome != CombatOutcome.None && b.Outcome != CombatOutcome.None
                && Mathf.Abs(a.Fraction - b.Fraction) <= .0001f)
                return new CombatExchangeResult(new CombatResult(CombatOutcome.Clash, -1, -1, 0f, contactFraction: Mathf.Min(a.Fraction, b.Fraction)),
                    Mathf.Min(a.Fraction, b.Fraction));
            int attacker = b.Outcome == CombatOutcome.None || (a.Outcome != CombatOutcome.None && a.Fraction <= b.Fraction) ? 0 : 1;
            CombatContactResult selected = attacker == 0 ? a : b;
            AttackKind kind = attacker == 0 ? firstKind : secondKind;
            float direction = attacker == 0 ? 1f : -1f;
            var result = selected.Outcome == CombatOutcome.GuardSuccess
                ? new CombatResult(selected.Outcome, attacker, attacker, -direction * DuelMatch.GuardPush, kind, selected.Fraction)
                : new CombatResult(selected.Outcome, attacker, 1 - attacker,
                    direction * (selected.Outcome == CombatOutcome.GuardBreak ? DuelMatch.GuardBreakPush
                        : DuelMatch.HitPush), kind, selected.Fraction);
            return new CombatExchangeResult(result, selected.Fraction);
        }
    }

    public sealed class CombatDamageWindow
    {
        public const double Seconds = .2;
        private double until = double.NegativeInfinity;
        public bool IsInvulnerable(double now) => now + 1e-9 < until;
        public void Protect(double now) => until = System.Math.Max(until, now + Seconds);
        public bool TryHit(double now)
        {
            if (IsInvulnerable(now)) return false;
            Protect(now); return true;
        }
        public void Reset() => until = double.NegativeInfinity;
    }

    public static class CombatReaction
    {
        public const float RecoverySeconds = 1f;
        public static bool IsHit(CombatOutcome outcome)
            => outcome == CombatOutcome.Hit || outcome == CombatOutcome.GuardBreak;
        public static float Duration(CombatResult result)
            => result.HitStopSeconds + RecoverySeconds;
        public static Vector3 Direction(AttackKind kind, CombatPose attack)
        {
            Vector3 direction = CombatContact.IncomingCutDirection(attack);
            return direction.sqrMagnitude > .0001f ? direction.normalized : attack.Forward.normalized;
        }
    }
}
