using NUnit.Framework;
using SchoolFestival.Combat;

public sealed class SessionPlayTimerTests
{
    [Test]
    public void OnlyToggleStartsAndStoppingPreventsAutomaticRestart()
    {
        var timer = new SessionPlayTimer();
        Assert.That(timer.Tick(100), Is.False);
        Assert.That(timer.Started, Is.False);
        timer.Toggle(1000);
        Assert.That(timer.Started, Is.True);
        Assert.That(timer.EndsAt, Is.EqualTo(1600));
        Assert.That(timer.RemainingSeconds(1001), Is.EqualTo(599));
        timer.Toggle(1100);
        Assert.That(timer.Armed, Is.False);
        Assert.That(timer.Tick(2000), Is.False);
        Assert.That(timer.Started, Is.False);
        timer.Toggle(2000);
        Assert.That(timer.RemainingSeconds(2000), Is.EqualTo(600));
        Assert.That(timer.EndsAt, Is.EqualTo(2600));
    }

    [Test]
    public void ExpiryNotifiesOnceAndToggleClearsIt()
    {
        var timer = new SessionPlayTimer(); timer.Toggle(10);
        Assert.That(timer.Tick(609.9), Is.False);
        Assert.That(timer.RemainingSeconds(609.9), Is.EqualTo(1));
        Assert.That(timer.Tick(610), Is.True);
        Assert.That(timer.RemainingSeconds(610), Is.Zero);
        Assert.That(timer.Tick(1000), Is.False);
        timer.Toggle(1000);
        Assert.That(timer.Armed, Is.False);
        Assert.That(timer.Expired, Is.False);
        timer.Toggle(2000);
        Assert.That(timer.EndsAt, Is.EqualTo(2600));
    }
}
