using System;

namespace RippersCasino.Client
{
    internal static class RevolverMotion
    {
        internal const double TriggerAt = .65;
        private static double Clamp(double t) => Math.Max(0, Math.Min(1, t));
        private static double Smooth(double t) { t = Clamp(t); return t * t * t * (10 + t * (-15 + t * 6)); }
        internal static float Cylinder(double elapsed, int sequence, bool trigger)
        {
            var rest = sequence * 60.0;
            if (elapsed < 0 || elapsed >= .55) return (float)(rest % 360);
            var start = trigger ? rest - 60 : rest;
            return (float)((start + (trigger ? 420 : 360) * Smooth(elapsed / .55)) % 360 + 360) % 360;
        }
        internal static float Hammer(double elapsed, bool trigger)
        {
            if (!trigger || elapsed < 0) return 24;
            if (elapsed < .32) return (float)(24 * (1 - Smooth(elapsed / .32)));
            return (float)(24 * Smooth((elapsed - (TriggerAt - .035)) / .035));
        }
        internal static float Trigger(double elapsed, bool trigger)
        {
            if (!trigger || elapsed < 0) return 0;
            return (float)(12 * Smooth((elapsed - .52) / .13) * (1 - Smooth((elapsed - .72) / .16)));
        }
    }
}
