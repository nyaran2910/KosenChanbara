using SchoolFestival.Combat;
using NUnit.Framework;
using UnityEngine;

public sealed class MenuControllerInputTests
{
    private static Vector2Int Read(MenuGestureNavigation input, Quaternion pose, double time,
        bool guard = false, int generation = 0, Vector3 gyro = default, bool gyroValid = true)
        => input.Read(pose, true, guard, generation, time);

    [Test]
    public void CanonicalSwordTipTravelDrivesNavigationAndTwistDoesNot()
    {
        var input = new MenuGestureNavigation();
        Quaternion rest = Quaternion.identity;
        Read(input, rest, 0, gyro: Vector3.up * 1000f);
        Assert.That(Read(input, Quaternion.AngleAxis(24f, Vector3.up), .05, gyro: Vector3.zero),
            Is.EqualTo(Vector2Int.right));

        input.Reset();
        Read(input, rest, 0);
        Assert.That(Read(input, Quaternion.AngleAxis(90f, Vector3.forward), .05,
            gyro: Vector3.up * 1000f), Is.EqualTo(Vector2Int.zero),
            "Rotation around the sword axis leaves the tip stationary.");
    }

    [Test]
    public void TipDirectionPreviewMatchesNavigationAndThresholdIsStrict()
    {
        var input = new MenuGestureNavigation();
        var preview = new AxisDirectionMonitor();
        Quaternion rest = Quaternion.identity;
        Read(input, rest, 0);
        preview.Read(rest, true, 0);
        Quaternion slow = Quaternion.AngleAxis(18f, Vector3.up);
        Assert.That(Read(input, slow, .05), Is.EqualTo(Vector2Int.zero));
        Assert.That(preview.Read(slow, true, .05), Is.EqualTo(Vector2Int.zero));
        Quaternion fast = Quaternion.AngleAxis(42f, Vector3.up);
        Assert.That(Read(input, fast, .10), Is.EqualTo(Vector2Int.right));
        Assert.That(preview.Read(fast, true, .10), Is.EqualTo(Vector2Int.right));
    }

    [Test]
    public void SamePosePacketsDoNotReplayMotionAndInvalidIntervalsResetArming()
    {
        var input = new MenuGestureNavigation();
        Read(input, Quaternion.identity, 0);
        Quaternion swung = Quaternion.AngleAxis(24f, Vector3.up);
        Assert.That(Read(input, swung, .05), Is.EqualTo(Vector2Int.right));
        Assert.That(Read(input, swung, .05), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(input, Quaternion.identity, .7), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(input, Quaternion.AngleAxis(-24f, Vector3.up), .75), Is.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void GuardAndPoseGenerationSuppressOldMotion()
    {
        var input = new MenuGestureNavigation();
        Read(input, Quaternion.identity, 0);
        Assert.That(Read(input, Quaternion.AngleAxis(-30f, Vector3.up), .02, guard: true), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(input, Quaternion.AngleAxis(-15f, Vector3.up), .04), Is.EqualTo(Vector2Int.zero));
        Read(input, Quaternion.AngleAxis(-15f, Vector3.up), .06);
        Assert.That(Read(input, Quaternion.AngleAxis(15f, Vector3.up), .08), Is.EqualTo(Vector2Int.right));
        Assert.That(Read(input, Quaternion.identity, .10, generation: 1), Is.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void ShortResetOnlyActsOnReleaseAndConsumesReleaseCounter()
    {
        var input = new MenuResetButton(); input.Read(true, false, 7, 0f);
        Assert.That(input.Read(true, true, 7, .10f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, true, 7, .59f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 8, .599f), Is.EqualTo(MenuResetAction.ShortPress));
        Assert.That(input.Read(true, false, 8, .70f), Is.EqualTo(MenuResetAction.None));
    }

    [Test]
    public void ResetLongPressStartsAtHalfASecondAndDoesNotRepeatOrRecenter()
    {
        var input = new MenuResetButton(); input.Read(true, false, 7, -1f);
        input.Read(true, true, 7, 0f);
        Assert.That(input.Read(true, true, 7, .499f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, true, 7, .5f), Is.EqualTo(MenuResetAction.LongPress));
        Assert.That(input.Read(true, true, 7, .6f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 7, .7f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 8, 1.2f), Is.EqualTo(MenuResetAction.None),
            "An older sender's delayed release counter must not also recenter.");
        input.Read(true, true, 8, 2f);
        Assert.That(input.Read(true, false, 9, 2.499f), Is.EqualTo(MenuResetAction.ShortPress));
    }

    [Test]
    public void ReleasePacketAtHalfASecondStillActsWhenItReplacesTheHeldPacket()
    {
        var input = new MenuResetButton(); input.Read(true, false, 7, -1f);
        input.Read(true, true, 7, 0f);
        Assert.That(input.Read(true, true, 7, .499f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 7, .5f), Is.EqualTo(MenuResetAction.LongPress));
        Assert.That(input.Read(true, false, 7, .6f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 8, .7f), Is.EqualTo(MenuResetAction.None));
        input.Read(true, true, 8, 1f);
        Assert.That(input.Read(true, false, 9, 1.5f), Is.EqualTo(MenuResetAction.LongPress),
            "A legacy sender's counter in the release packet must also be consumed.");
        Assert.That(input.Read(true, false, 9, 1.6f), Is.EqualTo(MenuResetAction.None));
    }

    [Test]
    public void CancelledTouchWithoutReleaseCounterDoesNotAct()
    {
        var input = new MenuResetButton(); input.Read(true, false, 7, 0f);
        input.Read(true, true, 7, .10f);
        Assert.That(input.Read(true, false, 7, .30f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 7, 1.50f), Is.EqualTo(MenuResetAction.None));
        // A subsequent intentional press still works after the cancelled one.
        input.Read(true, true, 7, 2f);
        Assert.That(input.Read(true, false, 8, 2.20f), Is.EqualTo(MenuResetAction.ShortPress));
    }

    [Test]
    public void DelayedShortReleaseCounterActsOnce()
    {
        var input = new MenuResetButton(); input.Read(true, false, 7, 0f);
        input.Read(true, true, 7, .10f);
        Assert.That(input.Read(true, false, 7, .30f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 7, .60f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 8, .90f), Is.EqualTo(MenuResetAction.ShortPress));
        Assert.That(input.Read(true, false, 8, 1.10f), Is.EqualTo(MenuResetAction.None));
    }

    [Test]
    public void LongResetSuppressesReleaseCounterEvenWhenTransportIsLate()
    {
        var input = new MenuResetButton(); input.Read(true, false, 7, 0f);
        input.Read(true, true, 7, .10f);
        Assert.That(input.Read(true, true, 7, 1.20f), Is.EqualTo(MenuResetAction.LongPress));
        Assert.That(input.Read(true, false, 7, 1.30f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 8, 2f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 8, 2.10f), Is.EqualTo(MenuResetAction.None));
        input.Read(true, true, 8, 3f);
        Assert.That(input.Read(true, false, 9, 3.20f), Is.EqualTo(MenuResetAction.ShortPress));
    }

    [Test]
    public void LongResetActsOnceAndNeverLeaksIntoBackOnRelease()
    {
        var input = new MenuResetButton(); input.Read(true, false, 0, 0f);
        input.Read(true, true, 0, .10f);
        Assert.That(input.Read(true, true, 0, 1.10f), Is.EqualTo(MenuResetAction.LongPress));
        Assert.That(input.Read(true, true, 0, 2.10f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 0, 2.20f), Is.EqualTo(MenuResetAction.None));
        Assert.That(input.Read(true, false, 1, 2.25f), Is.EqualTo(MenuResetAction.None));
        input.Read(true, true, 1, 3f);
        Assert.That(input.Read(true, false, 2, 3.1f), Is.EqualTo(MenuResetAction.ShortPress));
    }

    [Test]
    public void LegacyCounterWrapActsOnceAndReconnectDoesNotReplayReset()
    {
        var input = new MenuResetButton(); input.Read(true, false, ushort.MaxValue, 0f);
        Assert.That(input.Read(true, false, 0, .5f), Is.EqualTo(MenuResetAction.ShortPress));
        Assert.That(input.Read(true, false, 0, .6f), Is.EqualTo(MenuResetAction.None));
        input.Read(false, false, 0, .7f);
        Assert.That(input.Read(true, false, 12, 1f), Is.EqualTo(MenuResetAction.None));
    }
}
