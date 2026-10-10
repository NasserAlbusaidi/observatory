using System;
using FogOfWar.OpenWorld;

namespace FogOfWar
{
    // A raider's chase in the scene (Career.Pirates.cs, docs/design-world.md W4): the pirate flies each span the player's
    // substep loop used, the warp stays under the career's cap, and Divert sets the course the career asks for. The
    // encounter lives in the Career, so a new game (a new Career and system) or a load (LoadCareer) leaves none behind.
    public partial class OpenSystemScene
    {
        double frameWarp; // review checks: the warp the last frame ran at, after the cap and the frame budget

        // The player just stepped from (p0, v0) at jd0 to now: the pirate flies the same span. Every branch but the
        // landed one calls this; a landing ends the chase at the career tick, so a long surface ride is never flown.
        void Chase(double jd0, D3 p0, D3 v0) => career.Encounter?.Advance(sys, jd0, (jd - jd0) * 86400, p0, v0, ship.Vel);

        // Under the career's cap (D7), before a frame's sim time is set and again after the career tick. Its own choice:
        // TransferInProgress is cleared on every career tick. Phase 5 point 1: the cap follows the next event and rises once
        // it passes, so what the cap took is given back as it rises, up to the warp it took it from, in the same chase, while
        // nothing else has moved the warp since (the player, DropWarp, a call card). Combat 4c's 1× hold is one such cap.
        int heldFrom = -1;     // the warp the cap lowered, or -1
        int heldAt = -1;       // the warp the cap left it at; any other warp means something else moved it
        Encounter heldFor;     // the chase it was held in
        void CapWarp()
        {
            double cap = career.WarpCap;
            if (heldFrom >= 0 && (warpIdx != heldAt || career.Encounter != heldFor || career.Call != null)) heldFrom = -1;
            if (heldFrom >= 0) while (warpIdx < heldFrom && cap > 0 && Warps[warpIdx + 1] <= cap) warpIdx++;
            int before = warpIdx;
            if (cap > 0) while (warpIdx > 0 && Warps[warpIdx] > cap) warpIdx--;
            if (warpIdx < before && heldFrom < 0) { heldFrom = before; heldFor = career.Encounter; }
            if (heldFrom >= 0 && warpIdx >= heldFrom) heldFrom = -1;
            heldAt = warpIdx;
        }

        // The time compression a frame runs at. Under the cap it is the cap itself, up to the warp the cap took the
        // setting from: the setting under the cap would hold the last 100 s before every event at 1×, ten times EventS
        // (Phase 5 point 1). Anything that moved the setting since CapWarp (TrackRoute's flip) runs at the setting.
        double FrameRate()
        {
            double w = Warps[warpIdx], cap = career.WarpCap;
            return heldFrom >= 0 && warpIdx == heldAt && cap > w ? Math.Min(Warps[heldFrom], cap) : w;
        }

        // D6, the career's Divert: the port's body as the target, the Fast plan engaged. A pilot running from a raider
        // goes even when the trip planner says the fuel is short (TogglePlan asks once, as for any engage).
        bool DivertTo(Body port)
        {
            SetTarget(port);
            if (ship.PlanActive && ship.Plan == Plan.Brachistochrone && cruiseIdx == 0) return true;
            ship.Plan = Plan.None;
            cruiseIdx = 0;
            selectedPlan = Plan.Brachistochrone;
            TogglePlan();
            if (!ship.PlanActive && ship.Phase.StartsWith("fuel short")) TogglePlan();
            return ship.PlanActive;
        }
    }
}
