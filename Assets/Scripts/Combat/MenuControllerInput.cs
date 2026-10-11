using UnityEngine;

// One step when directional speed crosses the threshold; slowing rearms input.
public sealed class MenuGestureNavigation
{
    public const float ThresholdDegreesPerSecond = 360f;
    // controller.js keeps its world frame fixed while remapping local axes;
    // MotionPacketCodec then reflects Z. Convert that world frame into menu
    // coordinates without using the calibrated sword's current direction.
    public static readonly Quaternion PhoneWorldToMenuWorld = Quaternion.Euler(-90f, 0f, 0f);
    private bool baseline, armed;
    private int generation = -1;
    private Quaternion previousRotation;
    private double previousSampleTime = double.NaN;

    public void Reset()
    {
        baseline = armed = false;
        previousSampleTime = double.NaN;
    }

    private static Vector2 ScreenVelocity(Vector3 worldAngularVelocity)
        => new Vector2(worldAngularVelocity.y, -worldAngularVelocity.x);

    public Vector2Int Read(Quaternion sensorRotation, Vector3 localAngularVelocity, bool gyroscopeValid,
        bool valid, bool guardHeld, int poseGeneration, double sampleTime,
        bool phoneInput = false, Quaternion? hardwareReferenceRotation = null)
    {
        if (!valid) { Reset(); return Vector2Int.zero; }
        Quaternion menuRotation = phoneInput
            ? PhoneWorldToMenuWorld * sensorRotation
            // BNO world heading depends on its startup pose. Use the same fixed
            // reference as gameplay: pitch is X, horizontal swing is Y, twist is Z.
            : Quaternion.Inverse(hardwareReferenceRotation ?? Quaternion.identity) * sensorRotation;
        Vector2 velocity = gyroscopeValid
            ? ScreenVelocity(menuRotation * localAngularVelocity) : Vector2.zero;
        if (!baseline || generation != poseGeneration)
        {
            baseline = true; generation = poseGeneration;
            previousRotation = menuRotation; previousSampleTime = sampleTime;
            armed = !guardHeld && Mathf.Max(Mathf.Abs(velocity.x), Mathf.Abs(velocity.y)) <= ThresholdDegreesPerSecond;
            return Vector2Int.zero;
        }
        double elapsed = sampleTime - previousSampleTime;
        if (guardHeld) armed = false;
        if (elapsed == 0) return Vector2Int.zero; // Same packet on another render frame.
        Quaternion delta = menuRotation * Quaternion.Inverse(previousRotation);
        previousRotation = menuRotation; previousSampleTime = sampleTime;
        if (!(elapsed > 0 && elapsed <= .5))
        {
            armed = false;
            return Vector2Int.zero;
        }
        if (!gyroscopeValid)
        {
            // Orientation-only remotes use the device clock, never render timing.
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            velocity = angle == 0f ? Vector2.zero
                : ScreenVelocity(axis * (angle / (float)elapsed));
        }
        if (guardHeld)
        {
            armed = false;
            return Vector2Int.zero;
        }
        bool horizontal = Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y);
        float speed = horizontal ? velocity.x : velocity.y;
        if (Mathf.Abs(speed) <= ThresholdDegreesPerSecond)
        {
            armed = true;
            return Vector2Int.zero;
        }
        if (!armed) return Vector2Int.zero;
        armed = false;
        return horizontal ? (speed > 0f ? Vector2Int.right : Vector2Int.left)
            : (speed > 0f ? Vector2Int.up : Vector2Int.down);
    }
}

public enum MenuResetAction { None, ShortPress, LongPress }

// New remotes report the held bit and increment the counter on short release.
// Older remotes report only a counter; those retain short-reset behavior.
public sealed class MenuResetButton
{
    public const float LongPressSeconds = .5f;
    private bool baseline, wasHeld, longConsumed;
    private ushort counter;
    private float heldSince;
    private MenuResetAction pendingRelease;

    public MenuResetAction Read(bool connected, bool held, ushort resetCounter, float time)
    {
        if (!connected)
        {
            baseline = wasHeld = longConsumed = false;
            pendingRelease = MenuResetAction.None;
            return MenuResetAction.None;
        }
        if (!baseline)
        {
            baseline = true; counter = resetCounter; wasHeld = held; heldSince = time;
            longConsumed = false; pendingRelease = MenuResetAction.None;
            return MenuResetAction.None;
        }
        bool counterChanged = counter != resetCounter;
        counter = resetCounter;
        if (held)
        {
            if (!wasHeld)
            {
                heldSince = time; longConsumed = false;
                pendingRelease = MenuResetAction.None;
            }
            wasHeld = true;
            if (!longConsumed && time - heldSince >= LongPressSeconds)
            { longConsumed = true; return MenuResetAction.LongPress; }
            return MenuResetAction.None;
        }
        if (wasHeld)
        {
            wasHeld = false;
            bool longRelease = !longConsumed && time - heldSince >= LongPressSeconds;
            // A cancelled touch also clears held. Only the release sequence
            // proves a short press; it may arrive in a later transport packet.
            pendingRelease = !longConsumed && time - heldSince < LongPressSeconds
                ? MenuResetAction.ShortPress : MenuResetAction.LongPress;
            longConsumed = false;
            // The release packet can replace the last held packet before the
            // next Unity frame. Crossing the deadline still counts once.
            if (longRelease)
            {
                if (counterChanged) pendingRelease = MenuResetAction.None;
                return MenuResetAction.LongPress;
            }
        }
        if (!counterChanged) return MenuResetAction.None;
        // Consume a long press's delayed release increment without imposing a
        // timeout that could turn a late packet into an unwanted reset action.
        MenuResetAction action = pendingRelease == MenuResetAction.LongPress
            ? MenuResetAction.None : MenuResetAction.ShortPress;
        pendingRelease = MenuResetAction.None;
        return action;
    }
}
