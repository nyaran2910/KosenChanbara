using UnityEngine;

namespace SchoolFestival.Combat
{
    public enum CombatOutcome { None, Hit, GuardSuccess, GuardBreak, Clash }
    public enum MatchWinner { None, Player1, Player2, Draw }

    public readonly struct CombatInput
    {
        public readonly float ForwardAngle;
        public readonly float AngularSpeed;
        public readonly bool GuardHeld;
        public readonly bool Valid;
        public readonly int PoseGeneration;

        public CombatInput(float forwardAngle, float angularSpeed, bool guardHeld = false,
            bool valid = true, int poseGeneration = 0)
        {
            ForwardAngle = forwardAngle;
            AngularSpeed = angularSpeed;
            GuardHeld = guardHeld;
            Valid = valid;
            PoseGeneration = poseGeneration;
        }
    }

    public readonly struct CombatResult
    {
        public readonly CombatOutcome Outcome;
        public readonly int Attacker;
        public readonly int PushedPlayer;
        public readonly float SignedPush;
        public readonly float HitStopSeconds;
        public readonly float ContactFraction;
        public readonly AttackKind Kind;

        public CombatResult(CombatOutcome outcome, int attacker, int pushedPlayer, float signedPush, AttackKind kind = AttackKind.Cut, float contactFraction = 1f)
        {
            Outcome = outcome;
            Attacker = attacker;
            PushedPlayer = pushedPlayer;
            SignedPush = signedPush;
            Kind = kind;
            ContactFraction = Mathf.Clamp01(contactFraction);
            HitStopSeconds = outcome == CombatOutcome.Hit || outcome == CombatOutcome.GuardBreak
                ? DuelMatch.HitStopSeconds : DuelMatch.GuardStopSeconds;
        }
    }

    // Gameplay state only: no transforms, audio, UI or transport lifecycle.
    public sealed class DuelMatch
    {
        public const float AttackAngle = 45f;
        public const float MinimumSpeed = AttackStroke.MinimumCutSpeed;
        public const float RearmAngle = 80f;
        public const float HitPush = 3f;
        public const float GuardPush = 2f;
        public const float GuardBreakPush = 1.5f;
        public const float HitStopSeconds = 0.06f;
        public const float GuardStopSeconds = 0.04f;
        public const float PushSeconds = 0.12f;
        public const float StunSeconds = CombatReaction.RecoverySeconds;

        private readonly AttackStroke[] gates = { new AttackStroke(), new AttackStroke() };
        private readonly CombatDamageWindow[] damageWindows = { new CombatDamageWindow(), new CombatDamageWindow() };
        private readonly double[] stunnedUntil = new double[2];
        private readonly double[] recoveringUntil = new double[2];
        private double resolvingUntil;

        public MatchWinner Winner { get; private set; }
        public bool IsResolving(double now) => now + 1e-9 < resolvingUntil;
        public bool IsStunned(int player, double now) => now < stunnedUntil[player];
        public bool IsRecovering(int player, double now) => now < recoveringUntil[player];
        public bool IsInvulnerable(int player, double now) => damageWindows[player].IsInvulnerable(now);

        public CombatResult Step(CombatInput first, CombatInput second, CombatPose firstPose, CombatPose secondPose, double now)
        {
            bool blocked = Winner != MatchWinner.None || IsResolving(now);
            bool firstAttack = gates[0].Update(first, firstPose, now, blocked || IsStunned(0, now));
            bool secondAttack = gates[1].Update(second, secondPose, now, blocked || IsStunned(1, now));
            if (!firstAttack && !secondAttack)
                return default;

            CombatResult result = CombatExchange.Resolve(firstAttack ? gates[0].Kind : AttackKind.None,
                secondAttack ? gates[1].Kind : AttackKind.None, firstPose, secondPose,
                first.Valid && first.GuardHeld,
                second.Valid && second.GuardHeld,
                firstInvulnerable: IsInvulnerable(0, now), secondInvulnerable: IsInvulnerable(1, now),
                firstHitStart: gates[0].HitStartFraction, secondHitStart: gates[1].HitStartFraction).Contact;
            if (result.Outcome == CombatOutcome.None) return default;

            resolvingUntil = now + CombatDamageWindow.Seconds;
            if (result.PushedPlayer >= 0)
            {
                stunnedUntil[result.PushedPlayer] = now + CombatReaction.Duration(result);
                damageWindows[result.PushedPlayer].Protect(now);
                if (CombatReaction.IsHit(result.Outcome))
                    recoveringUntil[result.PushedPlayer] = stunnedUntil[result.PushedPlayer];
            }
            // Preserve the observed stroke through hit stop so continuing the
            // same movement cannot score twice. Blocked updates still observe
            // a genuine reversal or rest without queuing another attack.
            gates[0].Interrupt();
            gates[1].Interrupt();
            return result;
        }

        public MatchWinner CheckRingOut(Vector2 firstRelativePosition, Vector2 secondRelativePosition, float radius)
        {
            if (Winner != MatchWinner.None)
                return Winner;

            float squaredRadius = radius * radius;
            bool firstOut = firstRelativePosition.sqrMagnitude >= squaredRadius;
            bool secondOut = secondRelativePosition.sqrMagnitude >= squaredRadius;
            if (firstOut || secondOut)
            {
                Winner = firstOut && secondOut ? MatchWinner.Draw
                    : firstOut ? MatchWinner.Player2 : MatchWinner.Player1;
            }
            return Winner;
        }

        public void Reset()
        {
            Winner = MatchWinner.None;
            resolvingUntil = 0;
            stunnedUntil[0] = stunnedUntil[1] = 0;
            recoveringUntil[0] = recoveringUntil[1] = 0;
            damageWindows[0].Reset(); damageWindows[1].Reset();
            gates[0].Reset();
            gates[1].Reset();
        }
    }
}
