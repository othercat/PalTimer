// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;

namespace Pal98Timer
{
    // Refs #55. Ownership is not use: observe a real count decrease in the
    // audited scene, and then its destination/state. Epoch/import resets are
    // owned by SdlpalRunObserver, so loading a save cannot manufacture a use.
    internal sealed class SdlpalItemConsumptionGate
    {
        private bool armed, consumed;
        private int previousCount;

        private void Reset() { armed = consumed = false; previousCount = 0; }
        internal bool Observe(SdlpalSnapshot state, SdlpalSplitRule rule)
        {
            if (!state.started || state.ItemDataError != null || state.inventory == null ||
                state.inventory.Any(i => i == null || i.amount < 0 || i.amount > 999 || i.id < 0 || i.id >= 700) ||
                (state.scene != rule.Scene && state.scene != rule.ReturnScene))
            { Reset(); return false; }
            int count = state.inventory.Where(i => i.id == rule.Item).Sum(i => i.amount);
            if (!armed)
            {
                if (state.scene == rule.Scene && count > 0) { armed = true; previousCount = count; }
                return false;
            }
            bool eventMatches = !rule.Event.HasValue || state.EventDataError == null && state.events != null &&
                state.events.Any(e => e.id == rule.Event && e.state == rule.EventState);
            if (count < previousCount && eventMatches) consumed = true;
            previousCount = count;
            if (consumed && state.scene == rule.ReturnScene && eventMatches) return true;
            if (state.scene != rule.Scene && !consumed) Reset();
            return false;
        }
    }
}
