using System;

namespace Pal98Timer
{
    [TimerCoreDisplayName(Dx9TimingCategory.SpeedDisplayName)]
    public sealed class Pal98Dx9Fast800Speed : 仙剑98柔情DX9
    {
        public Pal98Dx9Fast800Speed(GForm form) : base(form)
        { CoreName = Dx9TimingCategory.SpeedCore; }
        protected override int TimingModeMs { get { return 800; } }
        protected override int TimingMapSpeedTicks { get { return 9; } }
        protected override void InitCheckPoints()
        {
            base.InitCheckPoints();
            foreach (CheckPoint point in CheckPoints)
                if (Best == null || !Best.ContainsKey(point.Name)) point.Best = TimeSpan.Zero;
        }
    }
}
