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

        public CombatResult(CombatOutcome outcome, int attacker, int pushedPlayer, float signedPush)
        {
            Outcome = outcome;
            Attacker = attacker;
            PushedPlayer = pushedPlayer;
            SignedPush = signedPush;
            HitStopSeconds = outcome == CombatOutcome.Hit || outcome == CombatOutcome.GuardBreak
                ? DuelMatch.HitStopSeconds : DuelMatch.GuardStopSeconds;
        }
    }

    // Gameplay state only: no transforms, audio, UI or transport lifecycle.
    public sealed class DuelMatch
    {
        public const float AttackAngle = 45f;
        public const float MinimumSpeed = 40f;
        public const float RearmAngle = 80f;
        public const float GuardAngle = 70f;
        public const float HitPush = 3f;
        public const float GuardPush = 1.5f;
        public const float HitStopSeconds = 0.06f;
        public const float GuardStopSeconds = 0.04f;
        public const float PushSeconds = 0.12f;
        public const float StunSeconds = 0.25f;

        private sealed class AttackGate
        {
            private bool hasBaseline;
            private bool armed;
            private int generation;
            private float previousAngle;

            public void Reset()
            {
                hasBaseline = false;
                armed = false;
            }

            public bool Update(CombatInput input, bool blocked)
            {
                if (!input.Valid || blocked)
                {
                    Reset();
                    return false;
                }

                if (!hasBaseline || generation != input.PoseGeneration)
                {
                    generation = input.PoseGeneration;
                    hasBaseline = true;
                    armed = input.ForwardAngle >= RearmAngle;
                    previousAngle = input.ForwardAngle;
                    return false;
                }

                if (input.ForwardAngle >= RearmAngle)
                    armed = true;

                bool attack = armed && !input.GuardHeld && input.ForwardAngle <= AttackAngle
                    && input.AngularSpeed >= MinimumSpeed && input.ForwardAngle < previousAngle;
                previousAngle = input.ForwardAngle;
                if (attack)
                    armed = false;
                return attack;
            }
        }

        private readonly AttackGate[] gates = { new AttackGate(), new AttackGate() };
        private readonly double[] stunnedUntil = new double[2];
        private double resolvingUntil;

        public MatchWinner Winner { get; private set; }
        public bool IsResolving(double now) => now < resolvingUntil;
        public bool IsStunned(int player, double now) => now < stunnedUntil[player];

        public CombatResult Step(CombatInput first, CombatInput second, float acuteSwordAngle, double now)
        {
            bool blocked = Winner != MatchWinner.None || IsResolving(now);
            bool firstAttack = gates[0].Update(first, blocked || IsStunned(0, now));
            bool secondAttack = gates[1].Update(second, blocked || IsStunned(1, now));
            if (!firstAttack && !secondAttack)
                return default;

            CombatResult result;
            if (firstAttack && secondAttack)
            {
                result = new CombatResult(CombatOutcome.Clash, -1, -1, 0f);
            }
            else
            {
                int attacker = firstAttack ? 0 : 1;
                int defender = 1 - attacker;
                CombatInput defenderInput = firstAttack ? second : first;
                float direction = firstAttack ? 1f : -1f;
                bool guard = defenderInput.Valid && defenderInput.GuardHeld && !IsStunned(defender, now);
                result = !guard
                    ? new CombatResult(CombatOutcome.Hit, attacker, defender, direction * HitPush)
                    : acuteSwordAngle > GuardAngle
                        ? new CombatResult(CombatOutcome.GuardSuccess, attacker, attacker, -direction * GuardPush)
                        : new CombatResult(CombatOutcome.GuardBreak, attacker, defender, direction * GuardPush);
            }

            resolvingUntil = now + result.HitStopSeconds + PushSeconds;
            if (result.PushedPlayer >= 0)
                stunnedUntil[result.PushedPlayer] = now + StunSeconds;
            // Input during the reaction cannot be saved up as the next swing.
            gates[0].Reset();
            gates[1].Reset();
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
            gates[0].Reset();
            gates[1].Reset();
        }
    }
}
