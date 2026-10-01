using UnityEngine;

namespace SchoolFestival.Combat
{
    public static class SwordContact
    {
        // Closest points on finite segments, including parallel and degenerate cases.
        public static Vector3 ClosestMidpoint(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 u = b - a, v = d - c, w = a - c;
            float aa = Vector3.Dot(u, u), bb = Vector3.Dot(u, v), cc = Vector3.Dot(v, v);
            float dd = Vector3.Dot(u, w), ee = Vector3.Dot(v, w);
            float s = 0f, t = 0f;
            if (aa < 0.000001f && cc < 0.000001f) return (a + c) * 0.5f;
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
            return (a + u * s + c + v * t) * 0.5f;
        }
    }
}
