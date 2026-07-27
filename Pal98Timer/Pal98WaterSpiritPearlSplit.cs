namespace Pal98Timer
{
    internal sealed class Pal98WaterSpiritPearlGate
    {
        // Traditional SSS.MKF: event 4663 runs auto script 0x87DE, which moves
        // child Li to tile (32, 102, 1) => world coordinate (1040, 1640).
        internal const int NormalExchangeArea = 267;
        internal const int NormalExchangeX = 1040;
        internal const int NormalExchangeY = 1640;
        internal const int NormalExchangeXRadius = 96;
        internal const int NormalExchangeYRadius = 48;

        // Scene 0x00CC enter script 0x76ED sets the party at 0x76F6 to
        // tile (36, 47, 1) => world coordinate (1168, 760).
        internal const int DaliReturnArea = 204;
        internal const int DaliReturnX = 1168;
        internal const int DaliReturnY = 760;
        internal const int DaliReturnXRadius = 96;
        internal const int DaliReturnYRadius = 48;

        private bool IsInNormalExchangeRegion;
        private int NormalExchangeBaselineCount;
        private bool NormalExchangeIncreaseSeen;
        private bool DaliReturnPositionSeen;

        internal void ObserveGameState(int area, int x, int y, int waterSpiritPearlCount)
        {
            bool inNormalExchangeRegion = IsInsideRange(
                area,
                x,
                y,
                NormalExchangeArea,
                NormalExchangeX,
                NormalExchangeY,
                NormalExchangeXRadius,
                NormalExchangeYRadius);

            if (!inNormalExchangeRegion)
            {
                IsInNormalExchangeRegion = false;
            }
            else if (!IsInNormalExchangeRegion)
            {
                IsInNormalExchangeRegion = true;
                NormalExchangeBaselineCount = waterSpiritPearlCount;
            }
            else if (waterSpiritPearlCount > NormalExchangeBaselineCount)
            {
                NormalExchangeIncreaseSeen = true;
            }

            if (waterSpiritPearlCount > 0 && IsInsideRange(
                area,
                x,
                y,
                DaliReturnArea,
                DaliReturnX,
                DaliReturnY,
                DaliReturnXRadius,
                DaliReturnYRadius))
            {
                DaliReturnPositionSeen = true;
            }
        }

        internal bool CanComplete()
        {
            return NormalExchangeIncreaseSeen || DaliReturnPositionSeen;
        }

        internal void Reset()
        {
            IsInNormalExchangeRegion = false;
            NormalExchangeBaselineCount = 0;
            NormalExchangeIncreaseSeen = false;
            DaliReturnPositionSeen = false;
        }

        private static bool IsInsideRange(
            int area,
            int x,
            int y,
            int targetArea,
            int targetX,
            int targetY,
            int xRadius,
            int yRadius)
        {
            return area == targetArea &&
                x >= targetX - xRadius &&
                x <= targetX + xRadius &&
                y >= targetY - yRadius &&
                y <= targetY + yRadius;
        }
    }

    internal sealed class Pal98WaterSpiritPearlSplit
    {
        private readonly Pal98WaterSpiritPearlGate Gate = new Pal98WaterSpiritPearlGate();

        internal void Observe(int area, int x, int y, int waterSpiritPearlCount)
        {
            Gate.ObserveGameState(area, x, y, waterSpiritPearlCount);
        }

        internal bool CanComplete()
        {
            return Gate.CanComplete();
        }

        internal void ResetRouteState()
        {
            Gate.Reset();
        }

        internal void Detach()
        {
            Gate.Reset();
        }
    }
}
