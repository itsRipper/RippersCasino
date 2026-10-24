using System;
namespace RippersCasino.Client
{
    internal sealed class BlackjackBetSelection
    {
        internal const int Minimum = 2, StepSize = 2;
        internal string Text { get; set; } = "2";
        internal int Value => int.TryParse(Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        internal int Maximum { get; private set; } = -1;
        internal bool Valid => Value >= Minimum && Value <= Maximum && Value % 2 == 0;
        internal void Refresh(long stack, int limit)
        {
            var maximum = (int)Math.Max(0, Math.Min(stack, limit));
            maximum -= maximum % 2;
            if (Maximum == maximum) return;
            Maximum = maximum;
            if (Value < Minimum || Value > Maximum)
                Text = (Maximum < Minimum ? 0 : Math.Min(Maximum, Math.Max(Minimum, Value))).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        internal void Step(int direction)
        {
            var current = Math.Max(Minimum, Value);
            var next = direction < 0 ? ((long)current - 1) / StepSize * StepSize : ((long)current / StepSize + 1) * StepSize;
            Text = (Maximum < Minimum ? 0 : Math.Min(Maximum, Math.Max(Minimum, next))).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    internal sealed class RoundWagerSelection
    {
        internal string Text { get; set; } = "5";
        internal int Maximum { get; private set; } = -1;
        internal int Value => int.TryParse(Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        internal bool Valid => Value >= 1 && Value <= Maximum;
        internal void Refresh(long bankroll)
        {
            var maximum = (int)Math.Max(0, Math.Min(int.MaxValue, bankroll));
            if (maximum == Maximum) return;
            Maximum = maximum;
            if (Value > maximum || Value == 0) Text = Math.Min(maximum, Math.Max(1, Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        internal void Step(int direction)
        {
            var next = Value < 1 ? 1 : (long)Value + Math.Sign(direction);
            Text = Math.Max(0, Math.Min(Maximum, Math.Max(1, next))).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    internal sealed class RoulettePresentation
    {
        private string _round = string.Empty;
        private double _started;
        internal void Observe(string round, long closedAt, long serverTime, double now)
        {
            if (_round == round) return;
            _round = round;
            _started = now - Math.Max(0, Math.Min(4, serverTime - closedAt));
        }
        internal double Progress(double now) => Math.Max(0, Math.Min(1, (now - _started) / 4));
        internal static string Caption(int result, double progress, bool compact) => compact ? string.Empty : result >= 0 && progress >= 1 ? result.ToString() : "EUROPEAN\nSINGLE ZERO";
        internal static double WheelAngle(double progress) => -720 * Math.Pow(1 - progress, 3);
        internal static double BallAngle(double progress, int pocket) => pocket * 360.0 / 37 - 90 + 1440 * Math.Pow(1 - progress, 3);
    }
}
