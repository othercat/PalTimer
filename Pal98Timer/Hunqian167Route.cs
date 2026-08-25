using System.Collections.Generic;

namespace Pal98Timer
{
    public static class Hunqian167Route
    {
        public static bool IsPositionAround(short area, short x, short y, int expectedArea, int expectedX, int expectedY, int radius)
        {
            return area == expectedArea &&
                   x >= expectedX - (16 * radius) && x <= expectedX + (16 * radius) &&
                   y >= expectedY - (8 * radius) && y <= expectedY + (8 * radius);
        }

        public static bool IsBattlePresent(short area, IEnumerable<Dream220VisibleEnemyState> enemies, short[] acceptedAreas, short enemyId)
        {
            if (!Contains(acceptedAreas, area) || enemies == null)
            {
                return false;
            }
            foreach (Dream220VisibleEnemyState enemy in enemies)
            {
                if (enemy != null && enemy.Id == enemyId && enemy.HitPoints > 0)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsBattleDefeated(
            short area,
            IEnumerable<Dream220VisibleEnemyState> enemies,
            short[] acceptedAreas,
            params short[] requiredEnemyIds)
        {
            return Dream220VisibleRoute.IsBattleDefeated(area, enemies, acceptedAreas, requiredEnemyIds);
        }

        private static bool Contains(short[] values, short expected)
        {
            if (values == null)
            {
                return false;
            }
            foreach (short value in values)
            {
                if (value == expected)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
