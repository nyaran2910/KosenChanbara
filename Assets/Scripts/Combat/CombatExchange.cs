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
            bool firstRecovering = false, bool secondRecovering = false)
        {
            // Recovery prevents this fighter's offence, not the opponent's turn
            // or a fresh defensive guard.
            if (firstRecovering) firstKind = AttackKind.None;
            if (secondRecovering) secondKind = AttackKind.None;
            CombatContactResult a = CombatContact.Resolve(firstKind, first, second, secondGuard);
            CombatContactResult b = CombatContact.Resolve(secondKind, second, first, firstGuard);
            if (firstKind != AttackKind.None && secondKind != AttackKind.None
                && CombatContact.TryBladesClash(first, second, out float clash, firstKind, secondKind)
                && (a.Outcome == CombatOutcome.None || clash <= a.Fraction)
                && (b.Outcome == CombatOutcome.None || clash <= b.Fraction))
                return new CombatExchangeResult(new CombatResult(CombatOutcome.Clash, -1, -1, 0f), clash);

            if (a.Outcome == CombatOutcome.None && b.Outcome == CombatOutcome.None) return default;
            int attacker = b.Outcome == CombatOutcome.None || (a.Outcome != CombatOutcome.None && a.Fraction <= b.Fraction) ? 0 : 1;
            CombatContactResult selected = attacker == 0 ? a : b;
            AttackKind kind = attacker == 0 ? firstKind : secondKind;
            float direction = attacker == 0 ? 1f : -1f;
            var result = selected.Outcome == CombatOutcome.GuardSuccess
                ? new CombatResult(selected.Outcome, attacker, attacker, -direction * DuelMatch.GuardPush, kind)
                : new CombatResult(selected.Outcome, attacker, 1 - attacker,
                    direction * (selected.Outcome == CombatOutcome.GuardBreak ? DuelMatch.GuardBreakPush
                        : kind == AttackKind.Thrust ? DuelMatch.ThrustPush : DuelMatch.HitPush), kind);
            return new CombatExchangeResult(result, selected.Fraction);
        }
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
            Vector3 direction = kind == AttackKind.Thrust ? attack.Forward : attack.CutDirection;
            return direction.sqrMagnitude > .0001f ? direction.normalized : attack.Forward.normalized;
        }
    }
}
