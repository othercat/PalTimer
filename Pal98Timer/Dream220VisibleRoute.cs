using System;
using System.Collections.Generic;

namespace Pal98Timer
{
    public sealed class Dream220VisibleEnemyState
    {
        public Dream220VisibleEnemyState(short id, short hitPoints)
        {
            Id = id;
            HitPoints = hitPoints;
        }

        public short Id { get; private set; }
        public short HitPoints { get; private set; }
    }

    /// <summary>
    /// Pure route predicates for the PAL98-hosted Dream 2.20 visible-blood core.
    /// Scene/object identities are resource-level matches. Real PAL98 runtime route
    /// acceptance remains a separate manual gate.
    /// </summary>
    public static class Dream220VisibleRoute
    {
        public static bool IsPositionAround(short area, short x, short y, int expectedArea, int expectedX, int expectedY, int radius)
        {
            return area == expectedArea &&
                   x >= expectedX - (16 * radius) && x <= expectedX + (16 * radius) &&
                   y >= expectedY - (8 * radius) && y <= expectedY + (8 * radius);
        }

        public static bool IsBattleDefeated(
            short area,
            IEnumerable<Dream220VisibleEnemyState> enemies,
            short[] acceptedAreas,
            params short[] requiredEnemyIds)
        {
            if (!Contains(acceptedAreas, area) || enemies == null || requiredEnemyIds == null || requiredEnemyIds.Length == 0)
            {
                return false;
            }

            bool sawEnemy = false;
            HashSet<short> required = new HashSet<short>(requiredEnemyIds);
            HashSet<short> seenRequired = new HashSet<short>();
            foreach (Dream220VisibleEnemyState enemy in enemies)
            {
                if (enemy == null)
                {
                    return false;
                }
                sawEnemy = true;
                if (enemy.HitPoints > 0)
                {
                    return false;
                }
                if (required.Contains(enemy.Id))
                {
                    seenRequired.Add(enemy.Id);
                }
            }

            return sawEnemy && seenRequired.SetEquals(required);
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
