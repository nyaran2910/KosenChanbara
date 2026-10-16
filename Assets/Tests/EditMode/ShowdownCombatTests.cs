using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ShowdownCombatTests
{
    [TestCase(-90f)] [TestCase(0f)] [TestCase(90f)] [TestCase(32.5f)]
    public void RetryDirectionsStayContinuousAboveTheBodyAndChangeLane(float previous)
    {
        for (int i = 0; i <= 100; i++)
        {
            float angle = ShowdownCombat.AlternativeReadyAngle(previous, i / 100f);
            Assert.That(angle, Is.InRange(-90f, 90f));
            Assert.That(Mathf.Abs(angle - previous), Is.GreaterThanOrEqualTo(34.999f));
            Assert.That(ShowdownCombat.UpperReadyDirection(angle).y, Is.GreaterThanOrEqualTo(-.00001f));
        }
    }

    [Test]
    public void MultiHeartEnemiesAppearTwiceAsOftenAcrossTheFullRun()
    {
        int two = 0, three = 0;
        for (int i = 1; i <= 100; i++)
        {
            int hearts = ShowdownCombat.EnemyHearts(i);
            if (hearts == 2) two++;
            if (hearts == 3) three++;
        }
        Assert.That(two, Is.EqualTo(16));
        Assert.That(three, Is.EqualTo(8));
    }

    [Test]
    public void NormalGraduallySpeedsFromEasyStartToPointSixteenWhileEasyStaysConstant()
    {
        float step = Mathf.Pow(1.35f, 1f / 9f);
        for (int section = 1; section <= 10; section++)
        {
            float t = (section - 1) / 9f;
            var normal = new ShowdownDifficulty(section);
            var explicitNormal = new ShowdownDifficulty(section, false);
            var easy = new ShowdownDifficulty(section, true);
            Assert.That(normal.SpawnInterval, Is.EqualTo(Mathf.Lerp(1.85f, .95f, t)));
            Assert.That(normal.WindupSeconds, Is.EqualTo(Mathf.Lerp(.825f, .5f, t)).Within(.000001f));
            Assert.That(normal.SwingSeconds, Is.EqualTo(.216f / Mathf.Pow(step, section - 1)).Within(.000001f));
            if (section > 1)
            {
                Assert.That(new ShowdownDifficulty(section - 1).SwingSeconds / normal.SwingSeconds, Is.EqualTo(step).Within(.000001f));
                Assert.That(new ShowdownDifficulty(section - 1, true).SwingSeconds, Is.EqualTo(easy.SwingSeconds));
            }
            Assert.That(explicitNormal.SpawnInterval, Is.EqualTo(normal.SpawnInterval));
            Assert.That(explicitNormal.WindupSeconds, Is.EqualTo(normal.WindupSeconds));
            Assert.That(explicitNormal.SwingSeconds, Is.EqualTo(normal.SwingSeconds));
            Assert.That(easy.SpawnInterval, Is.EqualTo(normal.SpawnInterval * 1.5f));
            Assert.That(easy.WindupSeconds, Is.EqualTo(.825f).Within(.000001f));
            Assert.That(easy.SwingSeconds, Is.EqualTo(.216f).Within(.000001f));
            Assert.That(easy.MaximumLiving, Is.EqualTo(normal.MaximumLiving));
            Assert.That(ShowdownCombat.OpponentHandoff(easy), Is.EqualTo(ShowdownCombat.OpponentHandoff(normal) * 1.5f).Within(.00001f));
            Assert.That(easy.ApproachSpeed, Is.EqualTo(normal.ApproachSpeed));
            Assert.That(easy.AttackWait, Is.EqualTo(normal.AttackWait));
        }
        Assert.That(new ShowdownDifficulty(1).SwingSeconds, Is.EqualTo(new ShowdownDifficulty(1, true).SwingSeconds));
        Assert.That(new ShowdownDifficulty(10).SwingSeconds, Is.EqualTo(.16f).Within(.000001f));
        Assert.That(new ShowdownDifficulty(0).SwingSeconds, Is.EqualTo(new ShowdownDifficulty(1).SwingSeconds));
        Assert.That(new ShowdownDifficulty(100).SwingSeconds, Is.EqualTo(new ShowdownDifficulty(10).SwingSeconds));
        Assert.That(new ShowdownDifficulty(100, true).SwingSeconds, Is.EqualTo(new ShowdownDifficulty(1, true).SwingSeconds));
    }

    [Test]
    public void OpeningAttackersAreMixedAndTheAttackCueExistsOnlyJustBeforeTheCut()
    {
        int attacking = 0;
        for (int i = 1; i <= 10; i++) if (ShowdownCombat.BeginsWithAttack(i)) attacking++;
        Assert.That(attacking, Is.EqualTo(4));
        Assert.That(ShowdownCombat.BeginsWithAttack(0), Is.False);
        var empty = new Vector3[0];
        Assert.That(ShowdownCombat.CanAttack(Vector3.forward * 3.2f, Vector3.zero, empty), Is.False);
        Assert.That(ShowdownCombat.CanAttack(Vector3.forward * 3.2f, Vector3.zero, empty, ShowdownCombat.OpeningAttackRange), Is.True);
        Assert.That(ShowdownCombat.AttackCueProgress(-1f, .3f), Is.EqualTo(-1f));
        Assert.That(ShowdownCombat.AttackCueProgress(.801f, .3f), Is.EqualTo(-1f));
        Assert.That(ShowdownCombat.AttackCueProgress(.8f, .3f), Is.EqualTo(0f).Within(.001f));
        Assert.That(ShowdownCombat.AttackCueProgress(.55f, .3f), Is.EqualTo(.5f).Within(.001f));
        Assert.That(ShowdownCombat.AttackCueProgress(.3f, .3f), Is.EqualTo(-1f));
        Assert.That(ShowdownCombat.AttackCueProgress(.15f, .3f), Is.EqualTo(-1f));
    }

    [Test]
    public void APreparedBladeCanInterceptTheCommittedCutButADistantBladeCannot()
    {
        Vector3 grip = new Vector3(0f, .35f, 1.25f);
        Vector3 aim = (new Vector3(0f, .5735f, .55f) - grip).normalized;
        Vector3 start = (aim * .42f + Vector3.right * .9075f).normalized;
        Vector3 end = (aim * .42f - Vector3.right * .9075f).normalized;
        Vector3 guardGrip = new Vector3(0f, .0535f, .55f);
        Assert.That(ShowdownCombat.GuardInterceptsCommittedCut(grip, start, end, guardGrip, Vector3.up), Is.True);
        Assert.That(ShowdownCombat.GuardInterceptsCommittedCut(grip, start, end, guardGrip + Vector3.right * 4f, Vector3.up), Is.False);
        Assert.That(ShowdownCombat.GuardInterceptsCommittedCut(grip, start, Vector3.zero, guardGrip, Vector3.up), Is.False);
    }

    [Test]
    public void EveryRoundContainsTenChallengersAndOccupiedStepsCannotOverlapBodies()
    {
        for (int section = 1; section <= ShowdownCourse.SectionCount; section++)
            Assert.That(new ShowdownDifficulty(section).MaximumLiving, Is.EqualTo(ShowdownCourse.ChallengersPerSection));
        Assert.That(new ShowdownDifficulty(10).SpawnInterval, Is.GreaterThanOrEqualTo(.95f));
        var others = new[] { Vector3.forward * 2f };
        Assert.That(ShowdownCombat.CanOccupy(Vector3.forward * 3f, Vector3.zero, others), Is.False);
        Assert.That(ShowdownCombat.CanOccupy(Vector3.forward * 3.2f, Vector3.zero, others), Is.True);
        Assert.That(ShowdownCombat.CanOccupy(Vector3.forward, Vector3.zero, new Vector3[0]), Is.False);
    }

    [Test]
    public void GuardsNeedPhysicalBladeContactAndSweepReportsFirstBodyContact()
    {
        Vector3 grip = Vector3.up * .1f;
        Assert.That(ShowdownCombat.TrySweptBladeTouchesBlade(grip, Vector3.forward, grip, Vector3.forward,
            new Vector3(2f, -.5f, 1f), Vector3.up, out _), Is.False,
            "A perpendicular blade outside the cut is not an invisible guard plane.");
        Assert.That(ShowdownCombat.TrySweptBladeTouchesBlade(grip, Vector3.forward, grip, Vector3.forward,
            new Vector3(0f, -.5f, 1f), Vector3.up, out _), Is.True);
        Assert.That(ShowdownCombat.TrySweptBladeTouchesBody(grip, Vector3.right, grip, Vector3.forward,
            new Vector3(1.2f, 0f, .4f), out float first), Is.True);
        Assert.That(ShowdownCombat.TrySweptBladeTouchesBody(grip, Vector3.right, grip, Vector3.forward,
            new Vector3(.4f, 0f, 1.2f), out float second), Is.True);
        Assert.That(first, Is.LessThan(second), "Resolve the first body along the arc, not an arbitrary nearest NPC.");
    }

    [Test]
    public void SpawnSearchUsesAnAdjacentLaneWhenThePreferredArcIsCompletelyOccupied()
    {
        var course = new ShowdownCourse(new ShowdownProgress());
        course.Reset(Vector3.zero, Vector2.zero, 9f);
        var crowded = new Vector3[9];
        for (int i = 0; i < crowded.Length; i++)
            crowded[i] = Quaternion.Euler(0f, -70f + i * 17.5f, 0f) * Vector3.forward * 7f;
        // Their spacing covers the whole preferred radius, including its edges.
        for (int step = 0; step <= 60; step++)
        {
            Vector3 candidate = Quaternion.Euler(0f, -75f + step * 2.5f, 0f) * Vector3.forward * 7f;
            bool blocked = false;
            foreach (Vector3 other in crowded)
                blocked |= Vector3.Distance(candidate, other) < ShowdownCombat.MinimumSpacing;
            Assert.That(blocked, Is.True);
        }
        Assert.That(ShowdownCombat.TryFindSpawn(Vector3.zero, Vector3.forward, crowded,
            course.ConstrainOpponent, out Vector3 spawn), Is.True);
        Assert.That(spawn.magnitude, Is.EqualTo(5.75f).Within(.001f));
        foreach (Vector3 other in crowded)
            Assert.That(Vector3.Distance(spawn, other), Is.GreaterThanOrEqualTo(ShowdownCombat.MinimumSpacing));
        Assert.That(spawn, Is.EqualTo(course.ConstrainOpponent(spawn)));
    }

    [Test]
    public void SpawnSearchRejectsBoundaryClampingThatWouldAppearTooCloseToThePlayer()
    {
        var course = new ShowdownCourse(new ShowdownProgress());
        Vector3 player = new Vector3(6f, 0f, 0f);
        course.Reset(player, Vector2.zero, 8f);
        Assert.That(ShowdownCombat.TryFindSpawn(player, Vector3.right, new Vector3[0],
            course.ConstrainOpponent, out Vector3 spawn), Is.True);
        Assert.That(Vector3.Distance(spawn, player), Is.GreaterThanOrEqualTo(ShowdownCombat.MinimumSpawnDistance));
        Assert.That(spawn.magnitude, Is.LessThanOrEqualTo(8f * .88f + .001f));
    }

    [Test]
    public void EachTenOpponentSectionIncreasesPressureWithoutRemovingWindup()
    {
        ShowdownDifficulty previous = new ShowdownDifficulty(1);
        for (int section = 2; section <= ShowdownCourse.SectionCount; section++)
        {
            var next = new ShowdownDifficulty(section);
            Assert.That(next.SpawnInterval, Is.LessThan(previous.SpawnInterval));
            Assert.That(next.ApproachSpeed, Is.GreaterThan(previous.ApproachSpeed));
            Assert.That(next.AttackWait, Is.LessThan(previous.AttackWait));
            Assert.That(next.WindupSeconds, Is.GreaterThanOrEqualTo(.3f));
            Assert.That(next.SwingSeconds, Is.GreaterThan(.1f));
            previous = next;
        }
        Assert.That(new ShowdownDifficulty(0).ApproachSpeed, Is.EqualTo(new ShowdownDifficulty(1).ApproachSpeed));
        Assert.That(new ShowdownDifficulty(100).WindupSeconds, Is.EqualTo(previous.WindupSeconds));
        Assert.That(new ShowdownDifficulty(100).SwingSeconds, Is.EqualTo(previous.SwingSeconds));
    }

    [Test]
    public void ApproachPreparationCanCloseItsExtraRangeDuringTheFullWarning()
    {
        for (int section = 1; section <= 10; section++)
        {
            var difficulty = new ShowdownDifficulty(section);
            float range = ShowdownCombat.ApproachAttackRange(difficulty);
            Assert.That(range, Is.GreaterThan(ShowdownCombat.AttackRange));
            Assert.That(range - difficulty.ApproachSpeed * difficulty.WindupSeconds,
                Is.EqualTo(ShowdownCombat.AttackRange).Within(.0001f));
        }
    }

    [Test]
    public void EverySectionHasTheSameFullRoundAndReadyWarningBeforeEnemiesCanMove()
    {
        for (int section = 1; section <= ShowdownCourse.SectionCount; section++)
            Assert.That(ShowdownCombat.IntroSeconds(section), Is.EqualTo(2.5f), "section " + section);
    }

    [Test]
    public void EngagementRequiresBothPlayerFacingAndTheActualCameraAim()
    {
        Vector3 opponent = Vector3.forward * 3.5f;
        Vector3 view = new Vector3(4f, 1.6f, -3.4f);
        Vector3 aim = opponent + Vector3.up - view;
        Assert.That(ShowdownCombat.FocusReady(Vector3.zero, Vector3.forward, view, aim, opponent), Is.True);
        Assert.That(ShowdownCombat.FocusReady(Vector3.zero, Vector3.right, view, aim, opponent), Is.False,
            "A camera already looking at the enemy cannot bypass an unfinished player turn.");
        Assert.That(ShowdownCombat.FocusReady(Vector3.zero, Vector3.forward, view, Vector3.forward, opponent), Is.False,
            "The camera's real origin and direction matter even when the player faces the enemy.");
        Assert.That(ShowdownCombat.FocusReady(Vector3.zero, Vector3.forward, view, -aim, opponent), Is.False);
        Assert.That(ShowdownCombat.FocusReady(Vector3.zero, Vector3.forward, view, aim, Vector3.zero), Is.False);
    }

    [Test]
    public void EightDistinctReadyDirectionsMoveBothTheHandAndBladeThroughTheSharedPose()
    {
        var settings = new SwordPoseSettings(Vector3.up * .02f);
        for (int octant = 0; octant < 8; octant++)
        {
            Vector3 ready = ShowdownCombat.ReadyDirection(octant);
            Assert.That(ready.magnitude, Is.EqualTo(1f).Within(.0001f));
            Assert.That(Vector3.Angle(ready, ShowdownCombat.ReadyDirection(octant + 1)), Is.EqualTo(45f).Within(.001f));
            SwordPose start = ShowdownCombat.CutPose(Vector3.forward, ready, 0f, settings);
            SwordPose middle = ShowdownCombat.CutPose(Vector3.forward, ready, .5f, settings);
            SwordPose end = ShowdownCombat.CutPose(Vector3.forward, ready, 1f, settings);
            Assert.That(Vector3.Dot(start.Direction, ready), Is.GreaterThan(.9f));
            Assert.That(Vector3.Dot(end.Direction, ready), Is.LessThan(-.9f));
            Assert.That(Vector3.Angle(middle.Direction, Vector3.forward), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(start.Position, end.Position), Is.GreaterThan(.7f),
                "The grip travels with the arms instead of remaining fixed while the blade pivots.");
            Assert.That(middle.Position.y, Is.EqualTo(settings.ChestCenter.y + settings.GuardHeight).Within(.0001f),
                "The forward hand rises to the chest-front defence centre instead of passing beneath the guard.");
            Assert.That(middle.Position.z, Is.EqualTo(settings.MinimumForward).Within(.0001f),
                "The chamber keeps the blade root in front of the torso and behind a close opponent's guard.");
            Vector3 remembered = Vector3.up;
            SwordPose sharedStart = SwordPoseMath.Calculate(start.Rotation, false, settings, ref remembered);
            SwordPose sharedEnd = SwordPoseMath.Calculate(end.Rotation, false, settings, ref remembered);
            Assert.That(Vector3.Distance(start.Position - end.Position, sharedStart.Position - sharedEnd.Position),
                Is.LessThan(.0001f), "Preparation and follow-through retain the shared hand kinematics.");
        }
        Assert.That(Vector3.Angle(ShowdownCombat.ReadyDirection(-1), ShowdownCombat.ReadyDirection(7)), Is.LessThan(.01f));
    }

    [Test]
    public void AttackPreparationUsesFiveUpperOrSideDirectionsWhileGuardKeepsEight()
    {
        int[] octants = { 0, 1, 2, 6, 7 };
        Assert.That(ShowdownCombat.AttackReadyDirectionCount, Is.EqualTo(octants.Length));
        for (int index = 0; index < octants.Length; index++)
        {
            Vector3 direction = ShowdownCombat.AttackReadyDirection(index);
            Assert.That(direction.y, Is.GreaterThanOrEqualTo(-.000001f));
            Assert.That(Vector3.Angle(direction, ShowdownCombat.ReadyDirection(octants[index])), Is.LessThan(.01f));
            for (int previous = 0; previous < index; previous++)
                Assert.That(Vector3.Angle(direction, ShowdownCombat.AttackReadyDirection(previous)), Is.GreaterThan(44f));
        }
        Assert.That(ShowdownCombat.AttackReadyDirection(-1), Is.EqualTo(ShowdownCombat.AttackReadyDirection(4)));
        Assert.That(ShowdownCombat.AttackReadyDirection(5), Is.EqualTo(Vector3.up));
        foreach (int octant in new[] { 3, 4, 5 })
            Assert.That(ShowdownCombat.ReadyDirection(octant).y, Is.LessThan(-.7f));
    }

    [TestCase(-90f)]
    [TestCase(-67.3f)]
    [TestCase(-22.7f)]
    [TestCase(0f)]
    [TestCase(46.6f)]
    [TestCase(90f)]
    public void ContinuousWindupStaysReadableAndNeverPreparesFromBelow(float angle)
    {
        Vector3 announced = ShowdownCombat.UpperReadyDirection(angle);
        foreach (float phase in new[] { .7f, 3.4f })
        {
            Vector3 previous = ShowdownCombat.WindupReadyDirection(angle, phase, 0f);
            for (int sample = 1; sample <= 120; sample++)
            {
                Vector3 direction = ShowdownCombat.WindupReadyDirection(angle, phase, sample / 120f);
                Assert.That(direction.y, Is.GreaterThanOrEqualTo(0f));
                Assert.That(direction.z, Is.EqualTo(0f));
                Assert.That(Vector3.Angle(announced, direction), Is.LessThanOrEqualTo(4.01f));
                Assert.That(Vector3.Angle(previous, direction), Is.LessThanOrEqualTo(40f / 120f + .01f));
                previous = direction;
            }
            Assert.That(Vector3.Distance(previous, announced), Is.LessThan(.000001f));
        }

    }

    [Test]
    public void WaitingFightersWithDifferentGaitsDoNotWalkInLockstep()
    {
        Vector3 anchor = Vector3.forward * 8f;
        Vector3 first = ShowdownCombat.WaitingTarget(anchor, .7f, 2d, .57f, .83f, .8f);
        Vector3 second = ShowdownCombat.WaitingTarget(anchor, .7f, 2d, 1.08f, .46f, 1.15f);
        Vector3 laterFirst = ShowdownCombat.WaitingTarget(anchor, .7f, 3d, .57f, .83f, .8f);
        Vector3 laterSecond = ShowdownCombat.WaitingTarget(anchor, .7f, 3d, 1.08f, .46f, 1.15f);
        Assert.That(Vector3.Distance(first, second), Is.GreaterThan(.1f));
        Assert.That(Vector3.Angle(laterFirst - first, laterSecond - second), Is.GreaterThan(15f));
    }

    [TestCase(30, 0f)]
    [TestCase(60, .3f)]
    [TestCase(144, -.2f)]
    public void EveryReadyGuardAndReturnKeepsTheFiniteBladeOutsideItsOwnTorso(int fps, float elevation)
    {
        var settings = new SwordPoseSettings(new Vector3(.03f, .02f, .015f));
        Vector3 aim = (Vector3.forward + Vector3.up * elevation).normalized;
        void AssertClear(SwordPose pose)
        {
            Assert.That(pose.Direction.z, Is.GreaterThanOrEqualTo(-.00001f), "An NPC transition cannot choose a rear-facing half turn.");
            Vector3 start = pose.Position + pose.Direction * ShowdownCombat.BladeStart;
            Vector3 end = pose.Position + pose.Direction * ShowdownCombat.BladeLength;
            for (int i = -1; i <= 1; i++)
                Assert.That(CombatContact.PointSegmentDistanceSquared(settings.ChestCenter + Vector3.up * (i * .48f), start, end),
                    Is.GreaterThan(ShowdownCombat.BodyRadius * ShowdownCombat.BodyRadius), "The finite blade cannot pierce the NPC body capsule.");
        }
        for (int readyIndex = 0; readyIndex < ShowdownCombat.AttackReadyDirectionCount; readyIndex++)
        for (int guardOctant = 0; guardOctant < 8; guardOctant++)
        {
            Vector3 remembered = Vector3.up;
            SwordPose guard = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, ShowdownCombat.ReadyDirection(guardOctant)),
                true, settings, ref remembered);
            Vector3 direction = ShowdownCombat.AttackReadyDirection(readyIndex);
            SwordPose ready = ShowdownCombat.CutPose(aim, direction, 0f, settings);
            for (int frame = 0; frame <= fps; frame++)
            {
                float progress = (float)frame / fps;
                AssertClear(ShowdownCombat.BlendFrontPose(guard, ready, Mathf.SmoothStep(0f, 1f, progress)));
                AssertClear(ShowdownCombat.CutPose(aim, direction, progress, settings));
            }
            SwordPose returning = ShowdownCombat.CutPose(aim, direction, 1f, settings);
            for (int frame = 0; frame < fps * 2; frame++)
            {
                Quaternion rotation = ShowdownCombat.TurnBladeThroughFront(returning.Rotation, guard.Rotation, 300f / fps);
                Vector3 position = Vector3.MoveTowards(returning.Position, guard.Position, 3f / fps);
                Assert.That(Quaternion.Angle(returning.Rotation, rotation), Is.LessThanOrEqualTo(300f / fps + .02f));
                returning = new SwordPose(position, rotation);
                AssertClear(returning);
            }
            Assert.That(Vector3.Distance(returning.Position, guard.Position), Is.LessThan(.0001f));
            Assert.That(Vector3.Angle(returning.Direction, guard.Direction), Is.LessThan(.02f), "Returning around the front must still reach the chosen guard.");
        }
    }

    [TestCase(8, 0f)]
    [TestCase(30, 0f)]
    [TestCase(60, 0f)]
    [TestCase(144, 0f)]
    [TestCase(8, .3f)]
    [TestCase(30, .3f)]
    [TestCase(60, .3f)]
    [TestCase(144, .3f)]
    public void EveryFastCutHitsFromTheSharedAttackConeEvenWhenAFrameSkipsMostOfTheSwing(int fps, float elevation)
    {
        var settings = new SwordPoseSettings(Vector3.up * .02f, frontOffset: 0f);
        float duration = new ShowdownDifficulty(10).SwingSeconds;
        Vector3 aim = (Vector3.forward + Vector3.up * elevation).normalized;
        for (int octant = 0; octant < 8; octant++)
        {
            Vector3 ready = ShowdownCombat.ReadyDirection(octant);
            SwordPose previous = ShowdownCombat.CutPose(aim, ready, 0f, settings);
            SwordPose end = ShowdownCombat.CutPose(aim, ready, 1f, settings);
            // Preparation is harmless. Even a skipped frame starts contact
            // inside duel's forward cone, avoiding an ambiguous 180° sweep.
            Vector3 coneDirection = Vector3.RotateTowards(Vector3.forward, ready,
                DuelMatch.AttackAngle * Mathf.Deg2Rad, 0f);
            Vector3 remembered = Vector3.up;
            SwordPose cone = SwordPoseMath.Calculate(Quaternion.FromToRotation(Vector3.forward, coneDirection),
                false, settings, ref remembered);
            bool hit = false;
            for (float elapsed = 1f / fps; elapsed < duration + 1f / fps; elapsed += 1f / fps)
            {
                SwordPose current = ShowdownCombat.CutPose(aim, ready, elapsed / duration, settings);
                bool enteredCone = Vector3.Angle(current.Direction, Vector3.forward) <= DuelMatch.AttackAngle
                    || Vector3.Dot(current.Direction, ready) < 0f;
                if (!enteredCone) { previous = current; continue; }
                if (!hit && Vector3.Angle(previous.Direction, Vector3.forward) > DuelMatch.AttackAngle)
                    previous = cone;
                var attack = new CombatPose(previous.Position, previous.Direction, current.Position, current.Direction,
                    settings.ChestCenter, Vector3.forward, -ready, end.Position, end.Direction);
                var defence = new CombatPose(Vector3.zero, Vector3.up, Vector3.zero, Vector3.up,
                    Vector3.forward * 2f + settings.ChestCenter, Vector3.back, Vector3.zero, Vector3.zero, Vector3.up);
                CombatContactResult result = CombatContact.Resolve(AttackKind.Cut, attack, defence, false);
                hit |= result.Outcome == CombatOutcome.Hit;
                previous = current;
            }
            Assert.That(hit, Is.True, "Fast visible slash from octant " + octant + " missed at " + fps + " FPS.");
            Assert.That(ShowdownCombat.SweptBladeTouchesBody(previous.Position, previous.Direction,
                previous.Position, previous.Direction, Vector3.forward * 5f), Is.False);
        }
    }

    [Test]
    public void AttackLaneGeometryAllowsSpacedEnemiesButRejectsRearLaneAndRemoteEnemies()
    {
        Vector3 player = Vector3.zero;
        Vector3 first = new Vector3(-1.3f, 0f, 1.6f), second = new Vector3(1.3f, 0f, 1.6f);
        Assert.That(ShowdownCombat.CanAttack(first, player, new[] { second }), Is.True);
        Assert.That(ShowdownCombat.CanAttack(second, player, new[] { first }), Is.True);
        Assert.That(ShowdownCombat.CanAttack(new Vector3(0f, 0f, 2.2f), player,
            new[] { new Vector3(0f, 0f, 1.4f) }), Is.False);
        Assert.That(ShowdownCombat.CanAttack(new Vector3(0f, 0f, 4f), player, new Vector3[0]), Is.False);
        Assert.That(ShowdownCombat.CanAttack(new Vector3(0f, 0f, .8f), player, new Vector3[0]), Is.False);
    }

    [Test]
    public void SweptBladeHitsBetweenSamplesButNeverBehindOrBeyondItsActualReach()
    {
        Vector3 grip = new Vector3(0f, .1f, 2f);
        Vector3 before = new Vector3(.9075f, 0f, -.42f).normalized;
        Vector3 after = new Vector3(-.9075f, 0f, -.42f).normalized;
        Assert.That(ShowdownCombat.SweptBladeTouchesBody(grip, before, grip, after, Vector3.zero), Is.True,
            "The target is only touched in the middle of this low frame rate swing.");
        Assert.That(ShowdownCombat.SweptBladeTouchesBody(grip, before, grip, after, new Vector3(0f, 0f, 4f)), Is.False,
            "A sword swinging toward negative Z cannot hit a body behind the attacker.");
        Assert.That(ShowdownCombat.SweptBladeTouchesBody(grip, before, grip, after, new Vector3(0f, 0f, -2f)), Is.False);
        Assert.That(ShowdownCombat.SweptBladeTouchesBody(grip, Vector3.up, grip, Vector3.up, Vector3.zero), Is.False,
            "Holding an upright sword near a player is not blade contact.");
    }

    [Test]
    public void BladeSweepFollowsArcInsteadOfTipChordAndRespectsBodyHeight()
    {
        Assert.That(ShowdownCombat.SweptBladeTouchesSphere(Vector3.zero, Vector3.right,
            Vector3.zero, Vector3.left, Vector3.zero, .1f), Is.False,
            "The grip and empty inner arc cannot damage a body just because the tip chord crosses it.");
        Assert.That(ShowdownCombat.SweptBladeTouchesBody(Vector3.up * 4f, Vector3.forward,
            Vector3.up * 4f, Vector3.forward, Vector3.forward), Is.False);
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void AcceleratingTurnSettlesQuicklyAndBrakesWhenTargetReverses(int fps)
    {
        Quaternion rotation = Quaternion.identity;
        float speed = 0f;
        for (int frame = 0; frame < fps; frame++)
            rotation = ShowdownCombat.Turn(rotation, Vector3.right, ref speed, 1f / fps);
        Assert.That(Quaternion.Angle(rotation, Quaternion.LookRotation(Vector3.right)), Is.LessThan(.1f));
        Assert.That(speed, Is.Zero);
        for (int frame = 0; frame < fps; frame++)
            rotation = ShowdownCombat.Turn(rotation, Vector3.left, ref speed, 1f / fps);
        Assert.That(Quaternion.Angle(rotation, Quaternion.LookRotation(Vector3.left)), Is.LessThan(.1f));
        Assert.That(speed, Is.Zero);
    }
}
