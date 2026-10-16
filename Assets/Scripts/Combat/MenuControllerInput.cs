using UnityEngine;

// Angular motion, independent of the combat recenter and sword-tip pose.
public static class MenuAngularMotion
{
    private static readonly Quaternion PhoneWorldToMenu = Quaternion.Euler(-90f, 0f, 0f);
    public static Vector2 Project(Quaternion previous, Quaternion current, Vector3 gyro,
        bool gyroValid, float seconds, bool phone)
    {
        Vector3 velocity;
        if (gyroValid) velocity = phone ? PhoneWorldToMenu * (current * gyro) : gyro;
        else
        {
            if (!(seconds > 0f)) return Vector2.zero;
            Quaternion delta = phone ? PhoneWorldToMenu * current * Quaternion.Inverse(PhoneWorldToMenu * previous)
                : Quaternion.Inverse(previous) * current;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            velocity = angle == 0f ? Vector3.zero : axis * (angle / seconds);
        }
        return new Vector2(velocity.y, -velocity.x);
    }
}

public sealed class MenuGestureNavigation
{
    public const float ThresholdDegreesPerSecond = 360f;
    private bool baseline, armed;
    private int generation = -1;
    private Quaternion previousRotation;
    private double previousSampleTime = double.NaN;

    public void Reset() { baseline = armed = false; previousSampleTime = double.NaN; }

    public Vector2Int Read(Quaternion rotation, bool valid, bool guard, int poseGeneration, double time)
        => Read(rotation, Vector3.zero, false, valid, guard, poseGeneration, time);

    public Vector2Int Read(Quaternion rotation, Vector3 gyro, bool gyroValid, bool valid,
        bool guard, int poseGeneration, double time, bool phone = false)
    {
        if (!valid) { Reset(); return Vector2Int.zero; }
        Vector2 velocity = MenuAngularMotion.Project(previousRotation, rotation, gyro, gyroValid, 0f, phone);
        if (!baseline || generation != poseGeneration)
        {
            baseline = true; generation = poseGeneration;
            previousRotation = rotation; previousSampleTime = time;
            armed = !guard && Mathf.Max(Mathf.Abs(velocity.x), Mathf.Abs(velocity.y)) <= ThresholdDegreesPerSecond + .001f;
            return Vector2Int.zero;
        }
        double elapsed = time - previousSampleTime;
        if (guard) armed = false;
        if (elapsed == 0) return Vector2Int.zero;
        Quaternion previous = previousRotation;
        previousRotation = rotation; previousSampleTime = time;
        if (!(elapsed > 0 && elapsed <= .5)) { armed = false; return Vector2Int.zero; }
        velocity = MenuAngularMotion.Project(previous, rotation, gyro, gyroValid, (float)elapsed, phone);
        if (guard) { armed = false; return Vector2Int.zero; }
        bool horizontal = Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y);
        float speed = horizontal ? velocity.x : velocity.y;
        if (Mathf.Abs(speed) <= ThresholdDegreesPerSecond + .001f) { armed = true; return Vector2Int.zero; }
        if (!armed) return Vector2Int.zero;
        armed = false;
        return horizontal ? (speed > 0 ? Vector2Int.right : Vector2Int.left)
            : (speed > 0 ? Vector2Int.up : Vector2Int.down);
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
