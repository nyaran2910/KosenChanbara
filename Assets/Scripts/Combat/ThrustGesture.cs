using UnityEngine;

namespace SchoolFestival.Combat
{
    // Acceleration is gravity-free, expressed in the player's local coordinates.
    // A forward impulse must last several fresh samples, separated by a release of
    // forward acceleration. Cross-axis hand movement need not stop completely.
    public sealed class ThrustGesture
    {
        public const float MaximumForwardAngle = AttackStroke.ThrustAngle;
        public const float MinimumAcceleration = 3f;
        public const float ReleaseAcceleration = 1.5f;
        public const float MinimumImpulse = .16f;
        public const double MinimumDuration = .025;
        public const double QuietDuration = .06;
        public const double Cooldown = .32;

        private bool baseline;
        private bool armed;
        private int generation;
        private double previousTime;
        private double quietSince = double.NaN;
        private double impulseDuration;
        private float impulse;
        private int impulseSamples;
        private double nextAllowed;

        public void Reset()
        {
            baseline = false;
            armed = false;
            quietSince = double.NaN;
            ResetImpulse();
            nextAllowed = 0;
        }

        public bool Update(Vector3 bladeDirection, Vector3 acceleration, bool valid,
            bool guardHeld, double sampleTime, int poseGeneration)
        {
            if (!valid || guardHeld || !IsFinite(bladeDirection) || !IsFinite(acceleration)
                || double.IsNaN(sampleTime) || double.IsInfinity(sampleTime))
            {
                Reset();
                return false;
            }

            if (!baseline || generation != poseGeneration)
            {
                BeginSample(acceleration, sampleTime, poseGeneration);
                return false;
            }

            double elapsed = sampleTime - previousTime;
            if (elapsed == 0) return false;
            if (elapsed < 0 || elapsed > .12)
            {
                // A source clock can restart without reaching its old value
                // again. Rebase it rather than waiting indefinitely for it.
                BeginSample(acceleration, sampleTime, poseGeneration);
                return false;
            }
            previousTime = sampleTime;

            if (IsReleased(acceleration))
            {
                if (double.IsNaN(quietSince)) quietSince = sampleTime;
                // Remember recovery during cooldown. Otherwise a short rest
                // between two thrusts is lost as soon as the hand moves again.
                if (sampleTime - quietSince >= QuietDuration)
                    armed = true;
            }
            else quietSince = double.NaN;

            bool forward = bladeDirection.sqrMagnitude > .000001f
                && Vector3.Angle(bladeDirection, Vector3.forward) <= MaximumForwardAngle;
            // Require a clear forward push to start. Once it has started, a
            // softer tail or small cross-axis wobble should not erase it.
            bool continuing = impulseSamples > 0;
            float threshold = continuing ? ReleaseAcceleration : MinimumAcceleration;
            float lateralWeight = continuing ? .8f : 1f;
            bool pushing = forward && acceleration.z >= threshold
                && acceleration.z > lateralWeight * new Vector2(acceleration.x, acceleration.y).magnitude;
            if (!armed || !pushing || sampleTime < nextAllowed)
            {
                ResetImpulse();
                return false;
            }

            impulseDuration += elapsed;
            impulse += acceleration.z * (float)elapsed;
            impulseSamples++;
            // A slow frame can span the entire duration threshold. It still
            // cannot turn one isolated acceleration packet into an attack.
            if (impulseSamples < 2 || impulseDuration < MinimumDuration || impulse < MinimumImpulse) return false;

            armed = false;
            ResetImpulse();
            nextAllowed = sampleTime + Cooldown;
            return true;
        }

        private void ResetImpulse()
        {
            impulseDuration = 0;
            impulse = 0;
            impulseSamples = 0;
        }

        private void BeginSample(Vector3 acceleration, double sampleTime, int poseGeneration)
        {
            Reset();
            baseline = true;
            generation = poseGeneration;
            previousTime = sampleTime;
            // A connection/calibration or clock-reset packet is never an attack.
            if (IsReleased(acceleration)) quietSince = sampleTime;
        }

        // Decelerating the push and pulling the hand back both release the
        // forward impulse. Requiring stillness here loses continuous thrusts.
        private static bool IsReleased(Vector3 acceleration) => acceleration.z < ReleaseAcceleration;
        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
