using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class ShowdownCourseTests
{
    private static ShowdownCourse Create(ShowdownProgress run, Vector2 center, float radius)
    {
        run.Reset();
        var course = new ShowdownCourse(run);
        course.Reset(new Vector3(center.x, 2f, center.y - radius * .1f), center, radius);
        return course;
    }

    [Test]
    public void TenChallengersMustAllBeDefeatedBeforeWalkingAndArrivalOpensNextGroup()
    {
        var run = new ShowdownProgress();
        ShowdownCourse course = Create(run, Vector2.zero, 15f);
        Vector3 player = course.StartPosition;
        Assert.That(course.RemainingChallengers, Is.EqualTo(10));
        for (int i = 0; i < 10; i++) Assert.That(course.TrySpawn(), Is.True);
        Assert.That(course.RemainingChallengers, Is.Zero);
        Assert.That(course.TrySpawn(), Is.False, "An eleventh challenger belongs to the next location.");
        for (int i = 0; i < 9; i++) Assert.That(run.Defeat(), Is.True);
        course.Refresh(player);
        Assert.That(course.Phase, Is.EqualTo(ShowdownCoursePhase.Fighting), "One remaining opponent prevents departure.");
        Assert.That(course.Move(player, 10f), Is.EqualTo(player));
        Assert.That(run.Defeat(), Is.True);
        course.Refresh(player);
        Assert.That(course.Phase, Is.EqualTo(ShowdownCoursePhase.Travelling));
        Assert.That(course.TrySpawn(), Is.False);
        Vector3 halfway = course.Move(player, .5f);
        Assert.That(Vector3.Distance(halfway, player), Is.EqualTo(1.4f).Within(.001f));
        Assert.That(course.SectionNumber, Is.EqualTo(1));
        Assert.That(course.TrySpawn(), Is.False, "No new opponent may appear on the road.");
        Vector3 destination = course.Destination;
        player = course.Move(halfway, 100f);
        Assert.That(player, Is.EqualTo(destination));
        Assert.That(course.SectionNumber, Is.EqualTo(2));
        Assert.That(course.Phase, Is.EqualTo(ShowdownCoursePhase.Fighting));
        Assert.That(course.RemainingChallengers, Is.EqualTo(10));
        Assert.That(course.TrySpawn(), Is.True);
        Assert.That(run.Spawned, Is.EqualTo(11));
    }

    [TestCase(15f)]
    [TestCase(30f)]
    [TestCase(83f)]
    public void EveryRoundSpreadsTenChallengersWithRoomToWanderAndApproach(float radius)
    {
        var run = new ShowdownProgress();
        Vector2 center = new Vector2(20f, -7f);
        ShowdownCourse course = Create(run, center, radius);
        Vector3 player = course.StartPosition;
        for (int section = 1; section <= ShowdownCourse.SectionCount; section++)
        {
            // Facing away from the field still leaves safe scattered starts.
            Vector3 forward = player - new Vector3(center.x, player.y, center.y);
            Assert.That(course.TryPlanRoundPositions(player, forward, out Vector3[] positions), Is.True);
            Assert.That(positions.Length, Is.EqualTo(10));
            float diameter = 0f;
            for (int i = 0; i < positions.Length; i++)
            {
                Assert.That(positions[i].y, Is.EqualTo(player.y));
                Assert.That(Vector3.Distance(positions[i], course.ConstrainOpponent(positions[i])), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(positions[i], player), Is.InRange(
                    ShowdownCombat.MinimumSpawnDistance, 10.46f));
                for (int other = 0; other < i; other++)
                {
                    float distance = Vector3.Distance(positions[i], positions[other]);
                    diameter = Mathf.Max(diameter, distance);
                    Assert.That(distance, Is.GreaterThanOrEqualTo(ShowdownCombat.MinimumSpacing + ShowdownCombat.WaitingWanderRadius * 2f),
                        "Even two enemies strolling towards each other retain the shared spacing.");
                }
                Assert.That(course.TrySpawn(), Is.True);
            }
            Assert.That(diameter, Is.GreaterThan(14f), "The round occupies a broad area rather than a clustered queue.");
            for (int i = 0; i < positions.Length; i++) Assert.That(run.Defeat(), Is.True);
            course.Refresh(player);
            if (section < ShowdownCourse.SectionCount) player = course.Move(player, 100f);
        }
        Assert.That(run.Spawned, Is.EqualTo(100));
        Assert.That(run.Cleared, Is.True);
    }

    [Test]
    public void RoundPlanningRejectsAnArenaTooSmallForTenSpacedChallengers()
    {
        var run = new ShowdownProgress();
        ShowdownCourse course = Create(run, Vector2.zero, 3f);
        Assert.That(course.TryPlanRoundPositions(course.StartPosition, Vector3.forward, out _), Is.False);
        Assert.That(run.Spawned, Is.Zero, "Planning positions does not spend opponent credit.");
    }

    [Test]
    public void VisibleWalkingCannotOpenTheNextSectionBeforeAnOpaqueTransfer()
    {
        var run = new ShowdownProgress();
        ShowdownCourse course = Create(run, Vector2.zero, 83f);
        Vector3 start = course.StartPosition;
        for (int i = 0; i < 10; i++) { course.TrySpawn(); run.Defeat(); }
        course.Refresh(start);
        Vector3 destination = course.Destination;
        Vector3 walked = course.WalkBeforeFade(start, ShowdownCombat.VisibleWalkSeconds);
        Assert.That(Vector3.Distance(walked, start), Is.EqualTo(ShowdownCourse.WalkingSpeed * 1f).Within(.0001f));
        Assert.That(course.ArriveUnderFade(walked, .999f), Is.EqualTo(walked));
        Assert.That(course.ArriveUnderFade(walked, float.NaN), Is.EqualTo(walked));
        Assert.That(course.SectionNumber, Is.EqualTo(1));
        Assert.That(course.TrySpawn(), Is.False);
        Assert.That(course.WalkBeforeFade(destination, .1f), Is.EqualTo(destination));
        Assert.That(course.SectionNumber, Is.EqualTo(1), "Being near the next stop still cannot place enemies before the fade.");
        Assert.That(course.ArriveUnderFade(walked, 1f), Is.EqualTo(destination));
        Assert.That(course.SectionNumber, Is.EqualTo(2));
        Assert.That(course.TravelProgress, Is.EqualTo(1f));
        Assert.That(course.TrySpawn(), Is.True);
        Assert.That(course.ArriveUnderFade(walked, 1f), Is.EqualTo(walked), "A second transfer cannot skip a section.");
    }

    [Test]
    public void AFullSizedStadiumKeepsShortTravelWhileAllowingItsActualBoundary()
    {
        var run = new ShowdownProgress();
        ShowdownCourse course = Create(run, new Vector2(20f, -7f), 83f);
        Vector3 farInside = new Vector3(80f, 2f, -7f);
        Assert.That(course.ConstrainOpponent(farInside), Is.EqualTo(farInside));
        Vector3 player = course.StartPosition;
        for (int section = 1; section < 10; section++)
        {
            for (int i = 0; i < 10; i++) { course.TrySpawn(); run.Defeat(); }
            course.Refresh(player);
            Assert.That(Vector3.Distance(course.Destination, player), Is.LessThan(25f),
                "The large playable floor must not stretch the route into a stadium-long walk.");
            player = course.Move(player, 100f);
        }
    }

    [TestCase(0f)]
    [TestCase(2.4f)]
    [TestCase(19.2f)]
    public void WaitingWanderStaysNearItsAnchorAtGroundHeight(float phase)
    {
        Vector3 anchor = new Vector3(12f, 2f, -8f);
        float travelled = 0f;
        Vector3 previous = ShowdownCombat.WaitingTarget(anchor, phase, 0d);
        for (int second = 0; second < 120; second++)
        {
            Vector3 next = ShowdownCombat.WaitingTarget(anchor, phase, second);
            Assert.That(next.y, Is.EqualTo(anchor.y));
            Assert.That(Vector3.Distance(anchor, next), Is.LessThanOrEqualTo(ShowdownCombat.WaitingWanderRadius + .00001f));
            travelled += Vector3.Distance(previous, next);
            previous = next;
        }
        Assert.That(travelled, Is.GreaterThan(5f), "Waiting is a gentle walk rather than a stationary guard.");
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void FullRunVisitsTenLocationsAndClearsOnTheHundredthOpponent(int fps)
    {
        var run = new ShowdownProgress();
        ShowdownCourse course = Create(run, new Vector2(20f, -7f), 15f);
        Vector3 player = course.StartPosition;
        int departures = 0;
        for (int location = 1; location <= 10; location++)
        {
            Assert.That(course.SectionNumber, Is.EqualTo(location));
            for (int opponent = 0; opponent < 10; opponent++)
            {
                Assert.That(course.TrySpawn(), Is.True);
                Assert.That(run.Defeat(), Is.True);
                course.Refresh(player);
                Assert.That(run.Cleared, Is.EqualTo(location == 10 && opponent == 9));
            }
            Assert.That(course.TrySpawn(), Is.False);
            if (location == 10) break;
            departures++;
            float previousProgress = 0f;
            int frames = 0;
            while (course.Phase == ShowdownCoursePhase.Travelling && frames++ < fps * 30)
            {
                Vector3 previous = player;
                player = course.Move(player, 1f / fps);
                Assert.That(Vector3.Distance(player, previous), Is.LessThanOrEqualTo(ShowdownCourse.WalkingSpeed / fps + .00001f));
                Assert.That(course.TravelProgress, Is.GreaterThanOrEqualTo(previousProgress));
                previousProgress = course.TravelProgress;
            }
            Assert.That(frames, Is.LessThan(fps * 30), "Walking must reach the next encounter instead of circling forever.");
        }
        Assert.That(departures, Is.EqualTo(9));
        Assert.That(run.Spawned, Is.EqualTo(100));
        Assert.That(run.Defeated, Is.EqualTo(100));
        Assert.That(run.Hearts, Is.EqualTo(ShowdownProgress.MaxHearts));
        Assert.That(course.Phase, Is.EqualTo(ShowdownCoursePhase.Complete));
        Assert.That(course.SectionNumber, Is.EqualTo(10));
        Assert.That(course.Move(player, 1000f), Is.EqualTo(player), "Clear cannot start an eleventh area.");
    }

    [TestCase(5f)]
    [TestCase(15f)]
    [TestCase(30f)]
    public void WalkAndOpponentPositionsStayInsideTheTranslatedCircularPlatform(float radius)
    {
        Vector2 center = new Vector2(-17f, 23f);
        var run = new ShowdownProgress();
        ShowdownCourse course = Create(run, center, radius);
        Vector3 player = course.StartPosition;
        for (int location = 0; location < 10; location++)
        {
            for (int direction = 0; direction < 360; direction += 15)
            {
                Vector3 proposed = player + Quaternion.Euler(0f, direction, 0f) * Vector3.forward * 100f;
                Vector3 safe = course.ConstrainOpponent(proposed);
                Assert.That(Vector2.Distance(new Vector2(safe.x, safe.z), center), Is.LessThan(radius * .9f));
                Assert.That(safe.y, Is.EqualTo(player.y));
            }
            for (int i = 0; i < 10; i++) { Assert.That(course.TrySpawn(), Is.True); Assert.That(run.Defeat(), Is.True); }
            course.Refresh(player);
            int steps = 0;
            while (course.Phase == ShowdownCoursePhase.Travelling && steps++ < 1000)
            {
                player = course.Move(player, .1f);
                Assert.That(Vector2.Distance(new Vector2(player.x, player.z), center), Is.LessThanOrEqualTo(radius * .51f));
                Assert.That(player.y, Is.EqualTo(2f));
            }
            Assert.That(steps, Is.LessThan(1000));
        }
    }

    [Test]
    public void PausedDeltaCannotMoveAndLossOrRetryClosesTheOldTravel()
    {
        var run = new ShowdownProgress();
        ShowdownCourse course = Create(run, Vector2.zero, 15f);
        Vector3 player = course.StartPosition;
        for (int i = 0; i < 10; i++) { course.TrySpawn(); run.Defeat(); }
        course.Refresh(player);
        for (int frame = 0; frame < 100; frame++) Assert.That(course.Move(player, 0f), Is.EqualTo(player));
        Assert.That(course.TravelProgress, Is.Zero);
        Assert.That(run.Damage(0), Is.True);
        Assert.That(run.Damage(1.5), Is.True);
        Assert.That(run.Damage(3), Is.True);
        double finishAt = 4.5d;
        while (!run.Finished) { Assert.That(run.Damage(finishAt), Is.True); finishAt += 1.5d; }
        course.Refresh(player);
        Assert.That(course.Phase, Is.EqualTo(ShowdownCoursePhase.Failed));
        Assert.That(course.Move(player, 100f), Is.EqualTo(player));
        Assert.That(course.TrySpawn(), Is.False);
        run.Reset(); course.Reset(player, Vector2.zero, 15f);
        Assert.That(course.Phase, Is.EqualTo(ShowdownCoursePhase.Fighting));
        Assert.That(course.SectionNumber, Is.EqualTo(1));
        Assert.That(course.TravelProgress, Is.Zero);
        Assert.That(course.TrySpawn(), Is.True);
    }
}
