using UnityEngine;

namespace SchoolFestival.Combat
{
    public static class SwordContact
    {
        // Closest points on finite segments, including parallel and degenerate cases.
        public static Vector3 ClosestMidpoint(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            ClosestPoints(a, b, c, d, out Vector3 first, out Vector3 second);
            return (first + second) * .5f;
        }

        public static float DistanceSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            ClosestPoints(a, b, c, d, out Vector3 first, out Vector3 second);
            return (first - second).sqrMagnitude;
        }

        public static void ClosestPoints(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 first, out Vector3 second)
        {
            Vector3 u = b - a, v = d - c, w = a - c;
            float aa = Vector3.Dot(u, u), bb = Vector3.Dot(u, v), cc = Vector3.Dot(v, v);
            float dd = Vector3.Dot(u, w), ee = Vector3.Dot(v, w);
            float s = 0f, t = 0f;
            if (aa < 0.000001f && cc < 0.000001f) { first = a; second = c; return; }
            if (aa < 0.000001f) t = Mathf.Clamp01(ee / cc);
            else if (cc < 0.000001f) s = Mathf.Clamp01(-dd / aa);
            else
            {
                float denominator = aa * cc - bb * bb;
                if (denominator > 0.000001f) s = Mathf.Clamp01((bb * ee - cc * dd) / denominator);
                t = (bb * s + ee) / cc;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-dd / aa); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - dd) / aa); }
            }
            first = a + u * s; second = c + v * t;
        }
    }
}
