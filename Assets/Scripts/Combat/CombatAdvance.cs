using UnityEngine;

namespace SchoolFestival.Combat
{
    public static class CombatAdvance
    {
        public const float MaximumSpeed = 10f;
        public static float Duration(float distance) => Mathf.Max(.24f, distance * 1.5f / MaximumSpeed);
        public static float Blend(double elapsed, float seconds)
            => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)elapsed / seconds));
    }
}
