using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;

public sealed class HitSweepTests
{
    static Vector3 Blade(float angle) => Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
    static CombatPose Pose(float before, float after) => new CombatPose(Vector3.zero, Blade(before), Vector3.zero,
        Blade(after), Vector3.zero, Vector3.forward, Vector3.left, Vector3.zero, Vector3.forward);
    static CombatInput Input(float angle) => new CombatInput(Mathf.Abs(angle), 1000f);
    [Test]
    public void AShortWristCutHasNoHitWindowAndTheWindowStartsAtSixtyDegrees()
    {
        var cut = new AttackStroke(); cut.Update(Input(80), Pose(80,80), 0);
        cut.Update(Input(50), Pose(80,50), .03);
        Assert.That(cut.HitStartFraction, Is.EqualTo(float.PositiveInfinity));
        cut.Update(Input(21), Pose(50,21), .06);
        Assert.That(cut.HitStartFraction, Is.EqualTo(float.PositiveInfinity));
        cut.Update(Input(10), Pose(21,10), .08);
        Assert.That(cut.HitStartFraction, Is.EqualTo(1f/11f).Within(.001f));
        cut.Consume(); cut.Update(Input(-80), Pose(10,-80), .18, true);
        cut.Update(Input(-50), Pose(-80,-50), .22);
        Assert.That(cut.HitStartFraction, Is.EqualTo(float.PositiveInfinity), "A reverse stroke needs its own 60 degrees.");
    }
    [Test]
    public void ACoarseFrameCannotBackdateAnEarlyTouchUsingItsLaterFollowThrough()
    {
        var cut = new AttackStroke(); cut.Update(Input(80), Pose(80,80), 0);
        cut.Update(Input(0), Pose(80,0), .08);
        var enemy = new CombatPose(Vector3.zero,Vector3.up,Vector3.zero,Vector3.up,Blade(60)*1.5f,
            Vector3.back,Vector3.zero,Vector3.zero,Vector3.up);
        Assert.That(CombatContact.Resolve(AttackKind.Cut,Pose(80,0),enemy,false).Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(CombatExchange.Resolve(AttackKind.Cut,AttackKind.None,Pose(80,0),enemy,false,false,
            firstHitStart:cut.HitStartFraction).Contact.Outcome, Is.EqualTo(CombatOutcome.None));
        var front = new CombatPose(Vector3.zero,Vector3.up,Vector3.zero,Vector3.up,Vector3.forward*2f,
            Vector3.back,Vector3.zero,Vector3.zero,Vector3.up);
        var hit = CombatExchange.Resolve(AttackKind.Cut,AttackKind.None,Pose(80,0),front,false,false,
            firstHitStart:cut.HitStartFraction);
        Assert.That(hit.Contact.Outcome, Is.EqualTo(CombatOutcome.Hit));
        Assert.That(hit.Fraction, Is.GreaterThanOrEqualTo(.75f));
    }
    [TestCase(30)] [TestCase(60)] [TestCase(144)]
    public void RepeatedSmallFastWristMotionsNeverAccumulateIntoAHit(int fps)
    {
        var cut = new AttackStroke(); float previous = -20;
        cut.Update(Input(previous),Pose(previous,previous),0);
        for(int i=1;i<fps;i++)
        {
            float current = i%2==0 ? -20 : 20;
            cut.Update(Input(current),Pose(previous,current),i/(double)fps);
            Assert.That(cut.HitStartFraction, Is.EqualTo(float.PositiveInfinity)); previous=current;
        }
    }
}
