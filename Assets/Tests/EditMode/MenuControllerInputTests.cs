using System;
using NUnit.Framework;
using UnityEngine;

public sealed class MenuControllerInputTests
{
    private static Vector2Int Gyro(MenuGestureNavigation input, Vector3 velocity, double time,
        bool guard = false, int generation = 0)
        => input.Read(Quaternion.identity, velocity, true, true, guard, generation, time);

    private static Vector2Int Pose(MenuGestureNavigation input, float angle, double time,
        bool guard = false, int generation = 0)
        => input.Read(Quaternion.AngleAxis(angle, Vector3.up), Vector3.zero,
            false, true, guard, generation, time);

    [TestCase(-361, 0, 0, 1)]
    [TestCase(361, 0, 0, -1)]
    [TestCase(0, 361, 1, 0)]
    [TestCase(0, -361, -1, 0)]
    public void WorldDirectionalSpeedAboveThreeSixtyMovesInTheSwingDirection(float x, float y, int dx, int dy)
    {
        var input = new MenuGestureNavigation();
        Gyro(input, Vector3.zero, 0);
        // No minimum tilt is needed before the angular velocity triggers.
        Assert.That(Gyro(input, new Vector3(x, y, 0f), .01), Is.EqualTo(new Vector2Int(dx, dy)));
    }

    [Test]
    public void ThresholdIsStrictAndUsesDirectionalSpeedInsteadOfMagnitude()
    {
        var input = new MenuGestureNavigation();
        Assert.That(MenuGestureNavigation.ThresholdDegreesPerSecond, Is.EqualTo(360f));
        Gyro(input, Vector3.zero, 0);
        Assert.That(Gyro(input, Vector3.up * 180f, .01), Is.EqualTo(Vector2Int.zero));
        Assert.That(Gyro(input, Vector3.up * 360f, .02), Is.EqualTo(Vector2Int.zero));
        // Total speed exceeds 360, but neither menu direction does.
        Assert.That(Gyro(input, new Vector3(-300f, 300f, 0f), .03), Is.EqualTo(Vector2Int.zero));
        Assert.That(Gyro(input, new Vector3(-520f, 400f, 0f), .04), Is.EqualTo(Vector2Int.up));
    }

    [TestCase(true, -1, 0, 0, 1)]
    [TestCase(true, 1, 0, 0, -1)]
    [TestCase(true, 0, 1, 1, 0)]
    [TestCase(true, 0, -1, -1, 0)]
    [TestCase(false, -1, 0, 0, 1)]
    [TestCase(false, 1, 0, 0, -1)]
    [TestCase(false, 0, 1, 1, 0)]
    [TestCase(false, 0, -1, -1, 0)]
    public void SlowReturnAndPauseRearmWithoutOriginalPose(bool gyroValid, float x, float y, int dx, int dy)
    {
        var input = new MenuGestureNavigation();
        Vector3 axis = new Vector3(x, y, 0f);
        double time = 0;
        Vector2Int Read(float angle, float speed)
            => input.Read(Quaternion.AngleAxis(angle, axis), axis * speed,
                gyroValid, true, false, 0, time += .05);
        Read(0f, 0f);
        Assert.That(Read(24f, 480f), Is.EqualTo(new Vector2Int(dx, dy)));
        Assert.That(Read(48f, 480f), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(72f, 480f), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(72f, 0f), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(96f, 480f), Is.EqualTo(new Vector2Int(dx, dy)),
            "Stopping at the far end is enough to accept another swing.");
        Assert.That(Read(120f, 480f), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(114f, -120f), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(108f, -120f), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(84f, -480f), Is.EqualTo(new Vector2Int(-dx, -dy)),
            "A slow partial return rearms input without reaching the original pose.");
        Assert.That(Read(60f, -480f), Is.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void TinyFlickRearmsOnLowSpeedWithoutReturning()
    {
        var input = new MenuGestureNavigation();
        Vector2Int Read(float angle, float speed, double time)
            => input.Read(Quaternion.AngleAxis(angle, Vector3.up), Vector3.up * speed,
                true, true, false, 0, time);
        Read(0f, 0f, 0);
        Assert.That(Read(1f, 480f, .01), Is.EqualTo(Vector2Int.right));
        Read(1f, 360f, .02);
        Assert.That(Read(2f, 480f, .03), Is.EqualTo(Vector2Int.right));
        Assert.That(Read(3f, 480f, .04), Is.EqualTo(Vector2Int.zero));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SlowLeadInAndPartialReturnDoNotBlockLaterSwings(bool gyroValid)
    {
        var input = new MenuGestureNavigation();
        Vector2Int Read(float angle, float speed, double time)
            => input.Read(Quaternion.AngleAxis(angle, Vector3.up), Vector3.up * speed,
                gyroValid, true, false, 0, time);
        Read(0f, 0f, 0);
        Assert.That(Read(6f, 60f, .1), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(30f, 480f, .15), Is.EqualTo(Vector2Int.right));
        Assert.That(Read(24f, -120f, .2), Is.EqualTo(Vector2Int.zero));
        Assert.That(Read(48f, 480f, .25), Is.EqualTo(Vector2Int.right),
            "A slow partial return must allow the next swing immediately.");
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void WorldSwingDirectionIsIndependentOfControllerPosture(bool phoneInput, bool gyroValid)
    {
        Quaternion[] postures = {
            Quaternion.identity, Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(0f, 0f, -90f), Quaternion.Euler(0f, 0f, 90f),
            Quaternion.Euler(180f, 0f, 0f), Quaternion.Euler(23f, -37f, 62f)
        };
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (Quaternion posture in postures)
            foreach (Vector2Int expected in directions)
            {
                var input = new MenuGestureNavigation();
                // Phone packets turn horizontally around Z; USB packets use Y.
                Vector3 worldAxis = phoneInput
                    ? new Vector3(-expected.y, 0f, expected.x)
                    : new Vector3(-expected.y, expected.x, 0f);
                Quaternion swung = Quaternion.AngleAxis(24f, worldAxis) * posture;
                Vector3 localVelocity = Quaternion.Inverse(swung) * (worldAxis * 480f);
                input.Read(posture, Vector3.zero, gyroValid, true, false, 0, 0, phoneInput);
                Assert.That(input.Read(swung, localVelocity, gyroValid, true, false, 0, .05, phoneInput),
                    Is.EqualTo(expected), "phone=" + phoneInput + " gyro=" + gyroValid + " pose=" + posture);
            }
    }

    [TestCase(-.70710677f, 0f, 0f, .70710677f, 480f, 0f, 0f, 0, 1)] // Screen up.
    [TestCase(-.70710677f, 0f, 0f, .70710677f, -480f, 0f, 0f, 0, -1)]
    [TestCase(-.70710677f, 0f, 0f, .70710677f, 0f, 480f, 0f, -1, 0)]
    [TestCase(-.70710677f, 0f, 0f, .70710677f, 0f, -480f, 0f, 1, 0)]
    [TestCase(-.5f, .5f, .5f, .5f, 0f, -480f, 0f, 0, 1)] // Screen left.
    [TestCase(-.5f, .5f, .5f, .5f, 0f, 480f, 0f, 0, -1)]
    [TestCase(-.5f, .5f, .5f, .5f, 480f, 0f, 0f, -1, 0)]
    [TestCase(-.5f, .5f, .5f, .5f, -480f, 0f, 0f, 1, 0)]
    [TestCase(-.5f, -.5f, .5f, .5f, 0f, 0f, -480f, 0, 1)] // Landscape, screen up.
    [TestCase(-.5f, -.5f, .5f, .5f, 0f, 0f, 480f, 0, -1)]
    [TestCase(-.5f, -.5f, .5f, .5f, 0f, 480f, 0f, -1, 0)]
    [TestCase(-.5f, -.5f, .5f, .5f, 0f, -480f, 0f, 1, 0)]
    public void BrowserPacketsNavigateWhenScreenFacesUpLeftOrLandscape(float qx, float qy, float qz,
        float qw, float gx, float gy, float gz, int dx, int dy)
    {
        // Values from controller.js's default axis mapping, before the codec's
        // handedness conversion. These fixtures exercise the actual wire frame.
        byte[] packet = new byte[56];
        packet[0] = 1;
        packet[1] = 6; // OrientationValid | GyroscopeValid.
        void Write(int offset, byte[] value) => Array.Copy(value, 0, packet, offset, value.Length);
        Write(16, BitConverter.GetBytes(qx)); Write(20, BitConverter.GetBytes(qy));
        Write(24, BitConverter.GetBytes(qz)); Write(28, BitConverter.GetBytes(qw));
        var input = new MenuGestureNavigation();
        ReadPhonePacket(input, packet, 0);
        Write(8, BitConverter.GetBytes(50d));
        Write(32, BitConverter.GetBytes(gx)); Write(36, BitConverter.GetBytes(gy));
        Write(40, BitConverter.GetBytes(gz));
        Assert.That(ReadPhonePacket(input, packet, .05), Is.EqualTo(new Vector2Int(dx, dy)));
    }

    [TestCase(0f, 0f, 480f, 0, -1)]
    [TestCase(0f, 0f, -480f, 0, 1)]
    [TestCase(-480f, 0f, 0f, 1, 0)]
    [TestCase(480f, 0f, 0f, -1, 0)]
    public void SavedPhoneAxisSwapKeepsDownAndRightInTheMenu(float gx, float gy, float gz, int dx, int dy)
    {
        // Golden wire values also asserted by the browser sender tests: screen
        // up with saved mapping X<-Z, Y<-Y, Z<-X.
        byte[] packet = new byte[56];
        packet[0] = 1; packet[1] = 6;
        void Write(int offset, byte[] value) => Array.Copy(value, 0, packet, offset, value.Length);
        Write(16, BitConverter.GetBytes(-.70710677f));
        Write(24, BitConverter.GetBytes(.70710677f));
        var input = new MenuGestureNavigation();
        ReadPhonePacket(input, packet, 0);
        Write(8, BitConverter.GetBytes(50d));
        Write(32, BitConverter.GetBytes(gx)); Write(36, BitConverter.GetBytes(gy));
        Write(40, BitConverter.GetBytes(gz));
        Assert.That(ReadPhonePacket(input, packet, .05), Is.EqualTo(new Vector2Int(dx, dy)));
    }

    private static Vector2Int ReadPhonePacket(MenuGestureNavigation input, byte[] packet, double time)
    {
        Type codec = Type.GetType("MotionPacketCodec, Assembly-CSharp", true);
        object[] args = { packet, time, null };
        Assert.That(codec.GetMethod("TryDecodeMotion").Invoke(null, args), Is.True);
        object sample = args[2];
        T Get<T>(string property) => (T)sample.GetType().GetProperty(property).GetValue(sample);
        return input.Read(Get<Quaternion>("Rotation"), Get<Vector3>("AngularVelocityDegrees"),
            Get<bool>("GyroscopeValid"), true, false, 0,
            Get<double>("BrowserTimestampMilliseconds") / 1000d, phoneInput: true);
    }

    [Test]
    public void ValidGyroscopeIsAuthoritativeEvenIfPosePacketsJump()
    {
        var input = new MenuGestureNavigation();
        Gyro(input, Vector3.zero, 0);
        Quaternion tilted = Quaternion.AngleAxis(20f, Vector3.up);
        Assert.That(input.Read(tilted, Vector3.zero, true, true, false, 0, .01), Is.EqualTo(Vector2Int.zero));
        Assert.That(input.Read(tilted, Vector3.up * -480f, true, true, false, 0, .02), Is.EqualTo(Vector2Int.left));
    }

    [Test]
    public void RotationAroundMenuForwardDoesNotNavigate()
    {
        var input = new MenuGestureNavigation();
        Gyro(input, Vector3.zero, 0);
        Assert.That(Gyro(input, Vector3.forward * 1000f, .01), Is.EqualTo(Vector2Int.zero));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HardwareSwingsAndTwistsUseTheGameplayReferenceRegardlessOfStartupPose(bool gyroValid)
    {
        Quaternion[] references = {
            Quaternion.identity, Quaternion.Euler(0f, 90f, 0f),
            Quaternion.Euler(0f, -90f, 0f), Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(-90f, 0f, 0f), Quaternion.Euler(37f, -82f, 14f)
        };
        Vector3[] axes = { Vector3.left, Vector3.right, Vector3.up,
            Vector3.down, Vector3.forward, Vector3.back };
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.right,
            Vector2Int.left, Vector2Int.zero, Vector2Int.zero };
        foreach (Quaternion reference in references)
            for (int i = 0; i < axes.Length; i++)
            {
                var input = new MenuGestureNavigation();
                input.Read(reference, Vector3.zero, gyroValid, true, false, 0, 0,
                    hardwareReferenceRotation: reference);
                Quaternion pose = reference * Quaternion.AngleAxis(24f, axes[i]);
                Assert.That(input.Read(pose, axes[i] * 480f, gyroValid, true, false, 0, .05,
                    hardwareReferenceRotation: reference), Is.EqualTo(directions[i]),
                    "reference=" + reference + " axis=" + axes[i]);
            }
    }

    [Test]
    public void RecordedControllerSwingsUseOneReferenceForUpAndRight()
    {
        // Decoded calibrated poses from the up/up/right/right USB capture.
        // This is the reset pose before all four swings, rather than a new
        // reference per swing or a basis taken from the sword's current pose.
        Quaternion reference = new Quaternion(.070984125f, .000366212f, .039184701f, .996707439f);
        Quaternion[,] poses = {
            { new Quaternion(-.160708979f,.020203065f,.019958919f,.986593187f),
              new Quaternion(-.275761008f,.030151824f,.009338520f,.960707903f) },
            { new Quaternion(-.306276500f,.043030076f,.025451833f,.950628996f),
              new Quaternion(-.456537813f,.051574122f,.026672062f,.887807310f) },
            { new Quaternion(-.007446414f,.242679864f,.066590473f,.967789710f),
              new Quaternion(-.028381841f,.334234327f,.073060356f,.939225316f) },
            { new Quaternion(-.020812752f,.498773634f,.075926878f,.863149405f),
              new Quaternion(-.057983607f,.591554821f,.085449524f,.799624443f) }
        };
        double[] elapsed = { .016, .017, .017, .018 };
        for (int i = 0; i < elapsed.Length; i++)
        {
            var input = new MenuGestureNavigation();
            input.Read(poses[i, 0], Vector3.zero, false, true, false, 0, 0,
                hardwareReferenceRotation: reference);
            Assert.That(input.Read(poses[i, 1], Vector3.zero, false, true, false, 0, elapsed[i],
                hardwareReferenceRotation: reference),
                Is.EqualTo(i < 2 ? Vector2Int.up : Vector2Int.right));
        }
    }

    [Test]
    public void PhoneNavigationIgnoresTheGameplayReference()
    {
        var input = new MenuGestureNavigation();
        Quaternion reference = Quaternion.Euler(37f, -82f, 14f);
        input.Read(Quaternion.identity, Vector3.zero, true, true, false, 0, 0,
            phoneInput: true, hardwareReferenceRotation: reference);
        Assert.That(input.Read(Quaternion.identity, Vector3.left * 480f, true, true, false, 0, .05,
            phoneInput: true, hardwareReferenceRotation: reference), Is.EqualTo(Vector2Int.up));
    }

    [Test]
    public void OrientationOnlyRemoteUsesPacketTimeAndSkipsRepeatedRenderFrames()
    {
        var input = new MenuGestureNavigation();
        Pose(input, 0f, 0);
        for (int packet = 1; packet <= 5; packet++)
            for (int frame = 0; frame < 6; frame++)
                Assert.That(Pose(input, packet * 3f, packet * .05), Is.EqualTo(Vector2Int.zero),
                    "Sparse 60 degrees/s packets must not become fast render-frame jumps.");
        Assert.That(Pose(input, 39f, .3), Is.EqualTo(Vector2Int.right));
        Assert.That(Pose(input, 39f, .3), Is.EqualTo(Vector2Int.zero));
        Assert.That(Pose(input, 63f, .35), Is.EqualTo(Vector2Int.zero),
            "Reading the same packet must not rearm the gesture.");
    }

    [Test]
    public void GuardSuppressesMotionUntilAReleasedSlowSample()
    {
        var input = new MenuGestureNavigation();
        Gyro(input, Vector3.zero, 0);
        Assert.That(Gyro(input, Vector3.up * 480f, .01, true), Is.EqualTo(Vector2Int.zero));
        Assert.That(Gyro(input, Vector3.up * 480f, .02), Is.EqualTo(Vector2Int.zero));
        Gyro(input, Vector3.zero, .03);
        Assert.That(Gyro(input, Vector3.up * 480f, .04), Is.EqualTo(Vector2Int.right));
    }

    [Test]
    public void NewSourceInvalidPoseAndMenuResetDoNotReplayFastGyro()
    {
        var input = new MenuGestureNavigation();
        Gyro(input, Vector3.zero, 0);
        Assert.That(Gyro(input, Vector3.up * 480f, .01), Is.EqualTo(Vector2Int.right));
        Assert.That(Gyro(input, Vector3.up * 480f, .02, generation: 1), Is.EqualTo(Vector2Int.zero));
        Gyro(input, Vector3.zero, .03, generation: 1);
        Assert.That(Gyro(input, Vector3.up * 480f, .04, generation: 1), Is.EqualTo(Vector2Int.right));
        input.Read(Quaternion.identity, Vector3.zero, true, false, false, 1, .05);
        Assert.That(Gyro(input, Vector3.up * 480f, .06, generation: 1), Is.EqualTo(Vector2Int.zero));
        input.Reset();
        Assert.That(Gyro(input, Vector3.up * 480f, .07, generation: 1), Is.EqualTo(Vector2Int.zero));
        Gyro(input, Vector3.zero, .08, generation: 1);
        Assert.That(Gyro(input, Vector3.up * -480f, .09, generation: 1), Is.EqualTo(Vector2Int.left));
    }

    [Test]
    public void DeviceClockRestartAndPacketGapResetVelocityHistory()
    {
        var input = new MenuGestureNavigation();
        Pose(input, 0f, 10);
        Assert.That(Pose(input, 5f, 0), Is.EqualTo(Vector2Int.zero));
        Assert.That(Pose(input, 29f, .05), Is.EqualTo(Vector2Int.zero));
        Pose(input, 29f, .1);
        Assert.That(Pose(input, 53f, .15), Is.EqualTo(Vector2Int.right));
        Assert.That(Pose(input, 0f, 1), Is.EqualTo(Vector2Int.zero));
        Pose(input, 0f, 1.05);
        Assert.That(Pose(input, -24f, 1.1), Is.EqualTo(Vector2Int.left));
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
