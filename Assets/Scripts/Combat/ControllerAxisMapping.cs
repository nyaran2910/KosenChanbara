using System;
using UnityEngine;

namespace SchoolFestival.Combat
{
    // Six permutations for menu samples, with the third axis signed to
    // preserve a rotation basis. Combat always reads the original sample.
    public readonly struct ControllerAxisMapping
    {
        public const int PresetCount = 6;
        public readonly bool IsCalibrated;
        public readonly int PresetIndex;
        public readonly Quaternion PoseBasis, GyroBasis;
        private static readonly string[] Names = {
            "X / Y / Z（標準）", "X / Z / −Y", "Y / X / −Z",
            "Y / Z / X", "Z / X / Y", "Z / Y / −X"
        };
        private static readonly int[,] Orders = { {0,1,2}, {0,2,1}, {1,0,2}, {1,2,0}, {2,0,1}, {2,1,0} };

        private ControllerAxisMapping(int index, Quaternion basis)
        { IsCalibrated = true; PresetIndex = index; PoseBasis = GyroBasis = basis; }

        public static string PresetName(int index)
        {
            if (index < 0 || index >= PresetCount) throw new ArgumentOutOfRangeException(nameof(index));
            return Names[index];
        }

        public static ControllerAxisMapping FromPreset(int index)
        {
            if (index < 0 || index >= PresetCount) throw new ArgumentOutOfRangeException(nameof(index));
            if (index == 0) return default; // The original adjustment, including phone menu framing.
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            Vector3 x = axes[Orders[index, 0]], y = axes[Orders[index, 1]];
            Quaternion basis = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(x, y), y));
            return new ControllerAxisMapping(index, basis);
        }

        public Quaternion Rotation(Quaternion relative) => IsCalibrated
            ? PoseBasis * relative * Quaternion.Inverse(PoseBasis) : relative;
        public Vector3 Gyroscope(Vector3 value) => IsCalibrated ? GyroBasis * value : value;
        public Vector3 Acceleration(Vector3 value) => IsCalibrated ? PoseBasis * value : value;
    }

    // Unlike menu navigation, the display follows direction changes during a
    // continuous fast swing. Test movements never select a different preset.
    public sealed class AxisDirectionMonitor
    {
        private Quaternion previous;
        private double previousTime = double.NaN;
        public void Reset() => previousTime = double.NaN;

        public Vector2Int Read(Quaternion rotation, Vector3 gyro, bool gyroValid, bool valid, double time)
        {
            if (!valid) { Reset(); return Vector2Int.zero; }
            if (time == previousTime) return Vector2Int.zero;
            double elapsed = time - previousTime;
            Quaternion delta = rotation * Quaternion.Inverse(previous);
            bool baseline = double.IsNaN(previousTime);
            previous = rotation; previousTime = time;
            if (!baseline && !(elapsed > 0 && elapsed <= .5)) return Vector2Int.zero;
            Vector3 velocity;
            if (gyroValid) velocity = rotation * gyro;
            else
            {
                if (baseline) return Vector2Int.zero;
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                velocity = angle == 0f ? Vector3.zero : axis * (angle / (float)elapsed);
            }
            float horizontal = velocity.y, vertical = -velocity.x;
            float speed = Mathf.Abs(horizontal) > Mathf.Abs(vertical) ? horizontal : vertical;
            if (!(Mathf.Abs(speed) > MenuGestureNavigation.ThresholdDegreesPerSecond)) return Vector2Int.zero;
            return Mathf.Abs(horizontal) > Mathf.Abs(vertical)
                ? (speed > 0 ? Vector2Int.right : Vector2Int.left)
                : (speed > 0 ? Vector2Int.up : Vector2Int.down);
        }
    }
}
