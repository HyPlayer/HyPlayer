using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Controls.LyricControl
{
    partial class LyricControl
    {
        public abstract class EaseFunctionBase
        {
            public EasingMode EasingMode { get; set; }

            protected abstract double EaseInCore(double normalizedTime);

            public double Ease(double normalizedTime)
            {
                switch (EasingMode)
                {
                    case EasingMode.EaseIn:
                        return EaseInCore(normalizedTime);
                    case EasingMode.EaseOut:
                        // EaseOut is the same as EaseIn, except time is reversed & the result is flipped.
                        return 1.0 - EaseInCore(1.0 - normalizedTime);
                    case EasingMode.EaseInOut:
                    default:
                        // EaseInOut is a combination of EaseIn & EaseOut fit to the 0-1, 0-1 range.
                        return (normalizedTime < 0.5) ?
                            EaseInCore(normalizedTime * 2.0) * 0.5 :
                            (1.0 - EaseInCore((1.0 - normalizedTime) * 2.0)) * 0.5 + 0.5;
                }
            }
        }

        private class CircleEase : EaseFunctionBase
        {
            protected override double EaseInCore(double normalizedTime)
            {
                normalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
                return 1.0 - Math.Sqrt(1.0 - normalizedTime * normalizedTime);
            }
        }

        private class ExponentialEase : EaseFunctionBase
        {
            public double Exponent { get; set; } = 2.0d;

            protected override double EaseInCore(double normalizedTime)
            {
                double factor = Exponent;
                if (Math.Abs(factor) < 0.00001)
                {
                    return normalizedTime;
                }
                else
                {
                    return (Math.Exp(factor * normalizedTime) - 1.0) / (Math.Exp(factor) - 1.0);
                }
            }
        }

        private class SineEase : EaseFunctionBase
        {
            protected override double EaseInCore(double normalizedTime)
            {
                normalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
                return 1.0 - Math.Sin((1.0 - normalizedTime) * Math.PI * 0.5);
            }
        }
    }

}
