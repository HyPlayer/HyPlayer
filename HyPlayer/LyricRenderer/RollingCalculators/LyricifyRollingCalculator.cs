using HyPlayer.LyricRenderer.Abstraction.Render;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Copyright WXRIW, in Lyricify

namespace HyPlayer.LyricRenderer.RollingCalculators
{
    internal class LyricifyRollingCalculator : LineRollingCalculator
    {
        const double duration = 620;

        const double a = 0.882;
        const double k = 0.836;
        const double m = 3.08;
        const double n = 3.14;

        protected static double f(double x)
        {
            if (x >= 0 && x <= a)
            {
                return Math.Pow(x / a, m) * k / g(1);
            }
            else
            {
                return g(x) / g(1);
            }
        }

        protected static double g(double x)
        {
            return 1 - Math.Pow((1 - x) * 3 / 4 / (1 - a) + 1.0 / 4, n) * (1 - k);
        }


        public override double CalculateCurrentY(double fromY, double targetY, int gap, long startTime,
            long currentTime)
        {
            var progress = 1.0;
            
            if (!(fromY < targetY) && gap >= 0)
            {
                var theoryDuration = (duration /* * (Math.Log10(Math.Max(gap, 0.9)) + 1)*/);
                progress = Math.Clamp((currentTime - startTime) / theoryDuration, 0, 1);
                progress = 1 - progress;
                progress = f(progress);
                progress = 1 - progress;
            }
            else
            {
                progress = Math.Clamp((currentTime - startTime)*1.0 / 300, 0, 1);
            }
            return fromY + (targetY - fromY) * progress;
        }
    }
}