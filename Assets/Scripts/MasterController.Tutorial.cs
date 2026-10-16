using SchoolFestival.Combat;
using UnityEngine;

public sealed partial class MasterController
{
    private enum TutorialStep { ResetFive, GuardFive, CutFive, CounterFive, HitGuardFive, Completed }

    public bool IsTutorialMode { get; private set; }
    public bool TutorialComplete => tutorialStep == TutorialStep.Completed;
    public string TutorialInstruction
    {
        get
        {
            switch (tutorialStep)
            {
                case TutorialStep.Completed:
                    return "チュートリアルクリア！　おつかれさまでした！";
                case TutorialStep.ResetFive:
                    return $"コントローラは不安定だ。基本姿勢からリセットをして合わせろ({tutorialProgress}/5)";
                case TutorialStep.GuardFive:
                    return $"攻撃の向きに合わせて剣を動かし、ガードボタン（親指でガード！）でガード({tutorialProgress}/5)";
                case TutorialStep.CutFive:
                    return $"相手のガードに平行する向きで攻撃を当てよう！({tutorialProgress}/5)";
                case TutorialStep.CounterFive:
                    return $"ガードされた直後は攻撃ができない！ガードして反撃を防げ！ガードされてから反撃をガードで防ぐ({tutorialProgress}/5)";
                case TutorialStep.HitGuardFive:
                    return $"攻撃に当たった直後も攻撃はできない！ガードをして攻撃を止めろ！攻撃をされた直後にガードをして攻撃を止める({tutorialProgress}/5)";
                default:
                    return string.Empty;
            }
        }
    }

    private const int TutorialResetTarget = 5;
    private const int TutorialGuardTarget = 5;
    private const int TutorialCutTarget = 5;
    private const int TutorialCounterTarget = 5;
    private const int TutorialHitGuardTarget = 5;
    private TutorialStep tutorialStep;
    private int tutorialProgress;
    private bool tutorialCounterWaitingForDefense;
    private bool tutorialHitWaitingForGuard;
    private bool tutorialCompletionPending;
    private SingleNpc tutorialNpc;
    private double tutorialCompletedAt;

    public void SetTutorialMode(bool enabled)
    {
        if (!IsPaused || IsTutorialMode == enabled) return;
        IsTutorialMode = enabled;
        if (!enabled && presentation != null) presentation.SetTutorialHud(false);
    }

    // Called only for MenuResetAction.ShortPress from the shared reset input path.
    public void NotifyTutorialReset()
    {
        if (!IsTutorialMode || IsPaused || tutorialStep != TutorialStep.ResetFive) return;
        if (++tutorialProgress >= TutorialResetTarget) AdvanceTutorial(TutorialStep.GuardFive);
    }

    private void ResetTutorialMode()
    {
        tutorialStep = TutorialStep.ResetFive;
        tutorialProgress = 0;
        tutorialCounterWaitingForDefense = false;
        tutorialHitWaitingForGuard = false;
        tutorialCompletionPending = false;
        tutorialNpc = null;
        if (presentation != null) presentation.SetTutorialHud(IsTutorialMode);
    }

    private void SpawnTutorialOpponent()
    {
        SpawnSingleNpcAt(null);
        if (singleNpcs.Count == 0) return;
        tutorialNpc = singleNpcs[singleNpcs.Count - 1];
        Transform root = tutorialNpc.Root.transform;
        root.position = first.Player.position + first.Player.forward * FightingDistance;
        root.rotation = Quaternion.LookRotation(first.Player.position - root.position);
        tutorialNpc.WaitingAnchor = root.position;
        tutorialNpc.OpeningAttack = tutorialNpc.ApproachAttack = false;
        tutorialNpc.Engaged = true;
        tutorialNpc.FacingSince = matchTime;
        tutorialNpc.NextAttack = matchTime + 1.1d;
        singleActiveNpc = tutorialNpc;
        tutorialNpc.Status.Activate(matchTime);
        UpdateNpcGuard(tutorialNpc);
    }

    private void TickTutorialMode(double now, float dt)
    {
        if (tutorialCompletionPending)
        {
            // Finish the current contact before resetting the arena/mode.
            tutorialCompletionPending = false;
            AdvanceTutorial(TutorialStep.Completed);
            tutorialCompletedAt = now;
            if (tutorialNpc != null) { tutorialNpc.ContinuousAttack = false; CancelSingleNpcAttack(tutorialNpc); }
        }
        SetSingleCharacterHidden(true);
        AdvanceSingleReaction(now);
        if (TutorialComplete)
        {
            TickSingleNpcAvatars(now, dt);
            presentation.PoseCharacters(now, dt, first.TargetRotation, Quaternion.identity, true, false, 0f, 0f, false);
            presentation.Tick(now, false);
            if (now - tutorialCompletedAt >= 2d && gameFlow != null) gameFlow.ReturnToTitle();
            return;
        }
        if (tutorialNpc == null) SpawnTutorialOpponent();

        CombatInput input = ReadInput(first, true);
        SingleNpc npc = tutorialNpc;
        bool npcShouldAttack = TutorialNpcShouldAttack;
        if (npc != null && !npc.Defeated)
        {
            npc.ContinuousAttack = npcShouldAttack;
            MoveSingleNpcsWithPolicy(dt, npcShouldAttack);
            FaceSingleTarget(dt);
        }

        TickSingleNpcAvatars(now, dt);
        presentation.SetFocusTarget(0, npc != null && !npc.Defeated ? npc.Root.transform : singleFocusAnchor);
        presentation.PoseCharacters(now, dt, first.TargetRotation, Quaternion.identity,
            input.Valid && input.GuardHeld, false, input.Valid ? input.AngularSpeed : 0f, 0f, false);
        UpdatePose(first, input.Valid && input.GuardHeld);
        ApplyPose(first, first.TargetPose);
        if (npc != null && !npc.Defeated)
        {
            if (npcShouldAttack)
            {
                singleStroke.Reset();
                ResolveSingleNpcAttacks(input);
            }
            else if (tutorialStep == TutorialStep.CutFive || tutorialStep == TutorialStep.CounterFive)
                UpdateSingleAttack(input);
        }
        if (showdown.Finished && !showdown.Cleared)
        {
            // Training has unlimited life and never clears scored mistakes.
            tutorialNpc = null;
            ResetMatch();
            return;
        }
        presentation.Tick(now, false);
    }

    private bool TutorialNpcShouldAttack => tutorialStep == TutorialStep.GuardFive
        || tutorialStep == TutorialStep.CounterFive && tutorialCounterWaitingForDefense
        || tutorialStep == TutorialStep.HitGuardFive;

    private void AdvanceTutorial(TutorialStep step)
    {
        tutorialStep = step;
        tutorialProgress = 0;
        tutorialCounterWaitingForDefense = false;
        tutorialHitWaitingForGuard = false;
    }

    private void RecordTutorialContact(SingleNpc npc, CombatResult result)
    {
        if (!IsTutorialMode || npc != tutorialNpc) return;
        switch (tutorialStep)
        {
            case TutorialStep.GuardFive:
                if (result.Attacker == 1 && result.Outcome == CombatOutcome.GuardSuccess
                    && ++tutorialProgress >= TutorialGuardTarget)
                    AdvanceTutorial(TutorialStep.CutFive);
                break;
            case TutorialStep.CutFive:
                if (result.Attacker == 0 && CombatReaction.IsHit(result.Outcome))
                {
                    npc.Hearts = npc.MaximumHearts;
                    if (++tutorialProgress >= TutorialCutTarget) AdvanceTutorial(TutorialStep.CounterFive);
                }
                break;
            case TutorialStep.CounterFive:
                if (result.Attacker == 0 && result.Outcome == CombatOutcome.GuardSuccess)
                {
                    // The player's strike was blocked; the shared NPC AI now launches its counter.
                    tutorialCounterWaitingForDefense = true;
                    npc.ContinuousAttack = true;
                    npc.NextAttack = matchTime + .3d;
                }
                else if (tutorialCounterWaitingForDefense && result.Attacker == 1
                    && result.Outcome == CombatOutcome.GuardSuccess)
                {
                    tutorialCounterWaitingForDefense = false;
                    if (++tutorialProgress >= TutorialCounterTarget) AdvanceTutorial(TutorialStep.HitGuardFive);
                    else npc.NextAttack = double.PositiveInfinity;
                }
                else if (result.Attacker == 1 && CombatReaction.IsHit(result.Outcome))
                {
                    // Missing the counter restarts this attempt, not its score.
                    tutorialCounterWaitingForDefense = false;
                    npc.ContinuousAttack = false;
                    npc.NextAttack = double.PositiveInfinity;
                }
                break;
            case TutorialStep.HitGuardFive:
                if (result.Attacker == 1 && CombatReaction.IsHit(result.Outcome))
                {
                    tutorialHitWaitingForGuard = true;
                }
                else if (tutorialHitWaitingForGuard && result.Attacker == 1
                    && result.Outcome == CombatOutcome.GuardSuccess)
                {
                    tutorialHitWaitingForGuard = false;
                    if (++tutorialProgress >= TutorialHitGuardTarget) tutorialCompletionPending = true;
                }
                break;
        }

        if (result.Attacker == 0 && CombatReaction.IsHit(result.Outcome))
            npc.Hearts = npc.MaximumHearts;
        if (result.Attacker == 1 && CombatReaction.IsHit(result.Outcome)) showdown.Reset();
        // End each counter attempt in a real guard, including a missed counter.
        // Apply after the shared combat code, which otherwise enables hit chains.
        npc.ContinuousAttack = TutorialNpcShouldAttack;
        if (!npc.ContinuousAttack)
        {
            npc.NextAttack = double.PositiveInfinity;
            CancelSingleNpcAttack(npc);
        }
    }

}
