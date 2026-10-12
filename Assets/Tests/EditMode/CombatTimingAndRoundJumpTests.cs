using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class CombatTimingAndRoundJumpTests
{
    [Test]
    public void DamageImmunityExpiresAtTwoTenthsWhileStaggerStillLastsOneSecond()
    {
        var window = new CombatDamageWindow();
        Assert.That(window.TryHit(1d), Is.True);
        Assert.That(window.TryHit(1.199999d), Is.False);
        Assert.That(window.TryHit(1.2d), Is.True);
        window.Reset(); Assert.That(window.TryHit(0d), Is.True);
        var player = new ShowdownProgress();
        Assert.That(player.Damage(0d), Is.True);
        Assert.That(player.Damage(.1d), Is.False);
        Assert.That(player.Hearts, Is.EqualTo(8));
        Assert.That(player.Damage(.2d), Is.True);
        Assert.That(player.Hearts, Is.EqualTo(6));
        Assert.That(player.IsRecovering(.5d), Is.True);
        Assert.That(CombatReaction.RecoverySeconds, Is.EqualTo(1f));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(5)] [TestCase(9)]
    public void SectionJumpSeedsOnlyTheEarlierDefeatsAndTheNewFullWave(int digit)
    {
        var progress = new ShowdownProgress();
        var course = new ShowdownCourse(progress);
        course.Reset(Vector3.zero, Vector2.zero, 83f);
        progress.Damage(1d);
        Vector3 start = course.JumpToSection(digit);
        Assert.That(progress.Spawned, Is.EqualTo(digit * 10));
        Assert.That(progress.Defeated, Is.EqualTo(digit * 10));
        Assert.That(progress.Hearts, Is.EqualTo(10));
        Assert.That(progress.IsInvulnerable(1d), Is.False);
        Assert.That(course.SectionNumber, Is.EqualTo(digit + 1));
        Assert.That(course.Phase, Is.EqualTo(ShowdownCoursePhase.Fighting));
        Assert.That(Vector3.Distance(start, course.ConstrainOpponent(start)), Is.LessThan(.001f));
        for (int i = 0; i < 10; i++) Assert.That(course.TrySpawn(), Is.True);
        Assert.That(course.TrySpawn(), Is.False);
        Assert.That(progress.Spawned, Is.EqualTo((digit + 1) * 10));
        for (int i = 0; i < 10; i++) Assert.That(progress.Defeat(), Is.True);
        course.Refresh(start);
        Assert.That(progress.Cleared, Is.EqualTo(digit == 9));
    }

    [TestCase(0f)] [TestCase(37f)] [TestCase(180f)]
    public void ADiagonalPreparationCannotMakeHorizontalRepeatsStricterThanVertical(float yaw)
    {
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        foreach (Vector3 ready in new[] { Vector3.up, Vector3.left, (Vector3.up + Vector3.left).normalized })
        {
            Vector3 tangent = Vector3.Cross(Vector3.forward, ready);
            Vector3 initial = (ready + tangent * .8f + Vector3.forward * .1f).normalized;
            var stroke = new AttackStroke();
            Vector3 Blade(float angle) => ready * Mathf.Sin(angle * Mathf.Deg2Rad) + Vector3.forward * Mathf.Cos(angle * Mathf.Deg2Rad);
            CombatPose Pose(Vector3 a, Vector3 b) => new CombatPose(turn * (Vector3.forward * .6f), turn * a,
                turn * (Vector3.forward * .6f), turn * b, Vector3.zero, turn * Vector3.forward,
                turn * (b - a), Vector3.zero, turn * Vector3.forward);
            bool Update(Vector3 a, Vector3 b, double time) => stroke.Update(new CombatInput(Vector3.Angle(b, Vector3.forward), 1500f), Pose(a,b), time);
            stroke.Update(new CombatInput(Vector3.Angle(initial, Vector3.forward), 0f), Pose(initial, initial), 0d);
            Assert.That(Update(initial, Blade(55f), .05d), Is.True);
            Assert.That(Update(Blade(55f), Blade(20f), .1d), Is.True);
            stroke.Consume();
            for (int hit = 1; hit < 3; hit++)
            {
                Assert.That(Update(Blade(20f), Blade(55f), hit * .3d), Is.False, "Takeback never damages.");
                Assert.That(Update(Blade(55f), Blade(20f), hit * .3d + .05d), Is.True, ready + " / yaw=" + yaw);
                stroke.Consume();
            }
        }
    }
}
