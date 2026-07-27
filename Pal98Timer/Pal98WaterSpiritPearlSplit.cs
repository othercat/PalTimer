namespace Pal98Timer
{
    internal sealed class Pal98WaterSpiritPearlGate
    {
        // In the traditional route, child Li ends at tile (34, 101, 0) =>
        // actual map coordinate (1088, 1616). Its manual trigger mode is 2.
        internal const int NormalExchangeArea = 267;
        internal const int NormalExchangeObjectX = 1088;
        internal const int NormalExchangeObjectY = 1616;
        internal const int NormalExchangeTriggerMode = 2;

        // Scene 0x00CC enter script 0x76ED sets the party at 0x76F6 to
        // tile (36, 47, 1) => world coordinate (1168, 760).
        internal const int DaliReturnArea = 204;
        internal const int DaliReturnX = 1168;
        internal const int DaliReturnY = 760;
        internal const int DaliReturnCountThreshold = 1;

        private bool IsInNormalExchangeTriggerRange;
        private int NormalExchangeBaselineCount;
        private bool NormalExchangeIncreaseSeen;
        private bool DaliReturnPositionSeen;

        internal void ObserveGameState(
            int area,
            int actualX,
            int actualY,
            int partyDirection,
            int waterSpiritPearlCount)
        {
            bool inNormalExchangeTriggerRange = area == NormalExchangeArea &&
                IsManualTriggerPosition(
                    actualX,
                    actualY,
                    partyDirection,
                    NormalExchangeObjectX,
                    NormalExchangeObjectY,
                    NormalExchangeTriggerMode);

            if (!inNormalExchangeTriggerRange)
            {
                IsInNormalExchangeTriggerRange = false;
            }
            else if (!IsInNormalExchangeTriggerRange)
            {
                IsInNormalExchangeTriggerRange = true;
                NormalExchangeBaselineCount = waterSpiritPearlCount;
            }
            else if (waterSpiritPearlCount > NormalExchangeBaselineCount)
            {
                NormalExchangeIncreaseSeen = true;
            }

            if (area == DaliReturnArea &&
                actualX == DaliReturnX &&
                actualY == DaliReturnY &&
                waterSpiritPearlCount > DaliReturnCountThreshold)
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
            IsInNormalExchangeTriggerRange = false;
            NormalExchangeBaselineCount = 0;
            NormalExchangeIncreaseSeen = false;
            DaliReturnPositionSeen = false;
        }

        private static bool IsManualTriggerPosition(
            int partyX,
            int partyY,
            int partyDirection,
            int objectX,
            int objectY,
            int triggerMode)
        {
            if (partyDirection < 0 || partyDirection > 3 ||
                triggerMode < 1 || triggerMode > 3)
            {
                return false;
            }

            int maxSearchIndex = triggerMode * 6 - 4;
            int xOffset = partyDirection == 2 || partyDirection == 3 ? 16 : -16;
            int yOffset = partyDirection == 3 || partyDirection == 0 ? 8 : -8;
            int searchX = partyX;
            int searchY = partyY;
            int searchIndex = 0;

            if (IsSameMapLocation(objectX, objectY, searchX, searchY))
            {
                return true;
            }

            while (searchIndex < maxSearchIndex)
            {
                searchIndex++;
                if (IsSameMapLocation(
                    objectX,
                    objectY,
                    searchX + xOffset,
                    searchY + yOffset))
                {
                    return true;
                }

                if (searchIndex >= maxSearchIndex)
                {
                    break;
                }

                searchIndex++;
                if (IsSameMapLocation(
                    objectX,
                    objectY,
                    searchX,
                    searchY + yOffset * 2))
                {
                    return true;
                }

                if (searchIndex >= maxSearchIndex)
                {
                    break;
                }

                searchIndex++;
                if (IsSameMapLocation(
                    objectX,
                    objectY,
                    searchX + xOffset * 2,
                    searchY))
                {
                    return true;
                }

                searchX += xOffset;
                searchY += yOffset;
            }

            return false;
        }

        private static bool IsSameMapLocation(int x1, int y1, int x2, int y2)
        {
            return x1 / 32 == x2 / 32 &&
                y1 / 16 == y2 / 16 &&
                (x1 % 32 == 0) == (x2 % 32 == 0);
        }

    }

    internal sealed class Pal98WaterSpiritPearlSplit
    {
        private readonly Pal98WaterSpiritPearlGate Gate = new Pal98WaterSpiritPearlGate();

        internal void Observe(
            int area,
            int actualX,
            int actualY,
            int partyDirection,
            int waterSpiritPearlCount)
        {
            Gate.ObserveGameState(area, actualX, actualY, partyDirection, waterSpiritPearlCount);
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
