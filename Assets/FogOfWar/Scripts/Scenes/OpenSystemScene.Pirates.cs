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
        // TransferInProgress is cleared on every career tick. Combat 4c: the 1× hold before an impact gives the warp back
        // after it, in the same chase, unless something else dropped it since (DropWarp, a call card), as a raider fires
        // again at once and its next flight is 1,000 s
        int heldFrom = -1;     // the warp the impact hold lowered, or -1
        Encounter heldFor;     // the chase it was held in
        void CapWarp()
        {
            double cap = career.WarpCap;
            if (cap == 1 && heldFrom < 0 && warpIdx > 0) { heldFrom = warpIdx; heldFor = career.Encounter; }
            else if (cap != 1 && heldFrom >= 0)
            {
                if (warpIdx == 0 && career.Encounter == heldFor && career.Call == null) warpIdx = heldFrom;
                heldFrom = -1; heldFor = null;
            }
            if (cap > 0) while (warpIdx > 0 && Warps[warpIdx] > cap) warpIdx--;
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
