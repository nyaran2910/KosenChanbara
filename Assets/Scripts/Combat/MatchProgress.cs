using System;

namespace SchoolFestival.Combat
{
    public enum RoundPhase { Versus, Round, Ready, Fight, Result, Complete }

    // Uses the gameplay clock, so menus, disconnect configuration and debugging
    // cannot consume a countdown or award a point while the simulation is paused.
    public sealed class DuelSeries
    {
        public const int WinsRequired = 3;
        public const double FightSeconds = 60;
        public RoundPhase Phase { get; private set; }
        public int FirstScore { get; private set; }
        public int SecondScore { get; private set; }
        public int RoundNumber { get; private set; }
        public MatchWinner RoundWinner { get; private set; }
        public bool IsFinalRound { get; private set; }
        public MatchWinner Winner => FirstScore >= WinsRequired && FirstScore > SecondScore ? MatchWinner.Player1
            : SecondScore >= WinsRequired && SecondScore > FirstScore ? MatchWinner.Player2 : MatchWinner.None;
        public double PhaseStarted { get; private set; }
        public double Remaining(double now) => IsFinalRound ? double.PositiveInfinity : Math.Max(0, FightSeconds - (now - PhaseStarted));
        public void Reset(double now)
        {
            FirstScore = SecondScore = 0; RoundNumber = 1;
            IsFinalRound = false;
            RoundWinner = MatchWinner.None; Enter(RoundPhase.Versus, now);
        }
        public bool Tick(double now)
        {
            double elapsed = now - PhaseStarted;
            switch (Phase)
            {
                case RoundPhase.Versus: if (elapsed >= 3.2) return Enter(RoundPhase.Round, now); break;
                case RoundPhase.Round: if (elapsed >= 1.3) return Enter(RoundPhase.Ready, now); break;
                case RoundPhase.Ready: if (elapsed >= 1.2) return Enter(RoundPhase.Fight, now); break;
                case RoundPhase.Fight: if (!IsFinalRound && elapsed >= FightSeconds) return EndRound(MatchWinner.Draw, now); break;
                case RoundPhase.Result:
                    if (elapsed >= 3.4)
                    {
                        if (Winner != MatchWinner.None) return Enter(RoundPhase.Complete, now);
                        RoundNumber++; RoundWinner = MatchWinner.None;
                        return Enter(RoundPhase.Round, now);
                    }
                    break;
            }
            return false;
        }
        public bool EndRound(MatchWinner winner, double now)
        {
            if (Phase != RoundPhase.Fight || winner == MatchWinner.None) return false;
            RoundWinner = winner;
            if (winner == MatchWinner.Player1 || winner == MatchWinner.Draw) FirstScore++;
            if (winner == MatchWinner.Player2 || winner == MatchWinner.Draw) SecondScore++;
            if (FirstScore >= WinsRequired && FirstScore == SecondScore) IsFinalRound = true;
            return Enter(RoundPhase.Result, now);
        }
        private bool Enter(RoundPhase phase, double now) { Phase = phase; PhaseStarted = now; return true; }
    }

    public sealed class ShowdownProgress
    {
        public const int Target = 100;
        public const int MaxHearts = 10;
        public int Spawned { get; private set; }
        public int Defeated { get; private set; }
        public int Hearts { get; private set; } = MaxHearts;
        public bool Cleared => Defeated == Target;
        public bool Finished => Cleared || Hearts == 0;
        private double stunnedUntil;
        public bool IsRecovering(double now) => now < stunnedUntil;
        public bool TrySpawn()
        {
            if (Finished || Spawned >= Target) return false;
            Spawned++; return true;
        }
        public bool Defeat()
        {
            if (Finished || Defeated >= Spawned) return false;
            Defeated++; return true;
        }
        public bool Damage(double now, bool guardHeld = false)
        {
            if (Finished) return false;
            Hearts = Math.Max(0, Hearts - (guardHeld ? 1 : 2));
            stunnedUntil = now + DuelMatch.HitStopSeconds + CombatReaction.RecoverySeconds; return true;
        }
        public void Reset()
        {
            Spawned = Defeated = 0; Hearts = MaxHearts; stunnedUntil = double.NegativeInfinity;
        }
    }
}
