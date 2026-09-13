using System;

namespace Pal98Timer
{
    [TimerCoreDisplayName(Dx9TimingCategory.FastDisplayName)]
    public sealed class Pal98Dx9Fast800 : 仙剑98柔情DX9
    {
        public Pal98Dx9Fast800(GForm form) : base(form)
        {
            CoreName = Dx9TimingCategory.FastCore;
        }

        protected override int TimingModeMs { get { return 800; } }

        protected override void InitCheckPoints()
        {
            base.InitCheckPoints();
            // Share route predicates, never copy 1.2s records or default best times.
            foreach (CheckPoint point in CheckPoints)
                if (Best == null || !Best.ContainsKey(point.Name))
                    point.Best = TimeSpan.Zero;
        }
    }
}
