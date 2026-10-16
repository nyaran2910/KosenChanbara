using System;
using UnityEngine;

namespace SchoolFestival.Combat
{
    // Keep the original quarter-turn sensor mapping, then account for the PCB
    // facing the opposite side of the upright grip. This half-turn around the
    // blade axis reverses pitch/yaw while preserving twist. Apply it once at
    // the hardware boundary so combat, menu and acceleration share the mount.
    public static class HardwareControllerFrame
    {
        private static readonly Quaternion SensorToGrip =
            Quaternion.AngleAxis(180f, Vector3.forward) * Quaternion.Euler(0f, 90f, 0f);

        public static Quaternion Rotation(Quaternion relative) =>
            Quaternion.Normalize(SensorToGrip * relative * Quaternion.Inverse(SensorToGrip));

        public static Vector3 Vector(Vector3 value) => SensorToGrip * value;
    }

    // The diagnostic display follows direction changes during one continuous
    // fast movement, using the same angular velocity as menu navigation.
    public sealed class AxisDirectionMonitor
    {
        private Quaternion previous;
        private double previousTime = double.NaN;
        public void Reset() => previousTime = double.NaN;

        public Vector2Int Read(Quaternion rotation, bool valid, double time)
            => Read(rotation, Vector3.zero, false, valid, time);

        public Vector2Int Read(Quaternion rotation, Vector3 gyro, bool gyroValid, bool valid, double time, bool phone = false)
        {
            if (!valid) { Reset(); return Vector2Int.zero; }
            if (time == previousTime) return Vector2Int.zero;
            double elapsed = time - previousTime;
            bool baseline = double.IsNaN(previousTime);
            Quaternion prior = previous;
            previous = rotation; previousTime = time;
            if (baseline || !(elapsed > 0 && elapsed <= .5)) return Vector2Int.zero;
            Vector2 velocity = MenuAngularMotion.Project(prior, rotation, gyro, gyroValid, (float)elapsed, phone);
            float horizontal = velocity.x, vertical = velocity.y;
            float speed = Mathf.Abs(horizontal) > Mathf.Abs(vertical) ? horizontal : vertical;
            if (!(Mathf.Abs(speed) > MenuGestureNavigation.ThresholdDegreesPerSecond)) return Vector2Int.zero;
            return Mathf.Abs(horizontal) > Mathf.Abs(vertical)
                ? (speed > 0 ? Vector2Int.right : Vector2Int.left)
                : (speed > 0 ? Vector2Int.up : Vector2Int.down);
        }
    }
}
