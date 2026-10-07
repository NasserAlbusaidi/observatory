using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Attitude holds (SAS), relative to the dominant body's orbit or to the target.
    public enum Hold { Off, Prograde, Retrograde, Normal, AntiNormal, RadialOut, RadialIn, Target, AntiTarget, Custom }

    // Nav-computer plans flown by the autopilot. Pursue is a pirate's (Pursuit.cs); the player's plan pickers list theirs by hand.
    public enum Plan { None, Brachistochrone, MatchVelocity, HoldStation, Pursue }

    // The player's torch ship: a point mass under every body's gravity, with a main drive along its nose, RCS
    // translation, and a rigid-body attitude (finite angular acceleration and rate). Positions are heliocentric ecliptic
    // km, velocities km/s. Attitude lives in the render frame (Frame.U), which is fixed to the ecliptic.
    public class Ship
    {
        public const double G0 = 9.80665e-3;    // km/s^2
        public const double RcsAccel = 0.1 * G0; // RCS translation, all axes
        public const float AngAccel = 1.5f;      // rad/s^2 (reaction-wheel torque: electric, no reaction mass)
        public const float MaxRate = 0.6f;       // rad/s

        public D3 Pos, Vel;
        public Quaternion Att = Quaternion.identity;
        public Vector3 AngVel;                   // body frame, rad/s (x pitch nose-down, y yaw right, z roll left)
        public double MaxG = 1;
        public double ThrottleCmd;               // pilot's throttle, 0..1 of MaxG
        public double Throttle;                  // applied this step
        public Vector3 RotInput, RcsInput;       // pilot input this frame, body axes, −1..1 (RCS: x right, y up, z forward)
        public bool FlightAssist = true;
        public Hold Hold;
        public D3 CustomDir;
        public Plan Plan;
        public Body Target;
        public D3 StationOffset;
        public double DvUsed;
        public double BurnSeconds;               // sim seconds with the drive firing (career scan time leaves these out)
        // Reaction mass. vₑ is D–He³ class (PHYSICS-REVIEW §3); every burn and RCS puff spends mass by the rocket
        // equation. Sandbox and QA runs keep UnlimitedFuel. Masses in tonnes.
        public const double Ve = 10000;          // km/s
        public double DryMass = 300, Fuel = 200, Cargo;
        public bool UnlimitedFuel = true;
        public double Mass => DryMass + Fuel + Cargo;
        public bool Dry => !UnlimitedFuel && Fuel <= 0;
        public double CruiseCap;                 // km/s; a brachistochrone coasts at this speed instead (0 = none)
        bool cruising;
        public string Phase = "coasting";
        public Body Landed;
        bool parked;
        public bool RcsActive;  // RCS fired this step (pilot or autopilot trim)
        public float RcsLevel;  // the same, as a fraction of full RCS thrust (ship sound)
        // Bodies the ship could reach this frame (collision checks) and bodies big enough to steer around; the scene
        // refreshes both, so a step doesn't evaluate hundreds of moons.
        public readonly List<Body> Near = new List<Body>();
        public readonly List<Body> Obstacles = new List<Body>();

        public double AMax => MaxG * G0;
        public D3 Heading => Frame.FromU(Att * Vector3.forward).Norm;
        public bool PlanActive => Plan != Plan.None && Target != null;
        public bool ManualInput => RotInput.sqrMagnitude > 0 || RcsInput.sqrMagnitude > 0;

        // Standoff from a target's centre where the autopilot parks the ship in orbit. A craft (derelict, runner,
        // beacon) has no gravity or surface to keep clear of, so the ship holds 1 km off it, close enough to see it.
        public static double Standoff(Body b) => Career.IsCraft(b) ? b.Radius + 1 : Math.Max(b.Radius * 3, b.Radius + 300);

        // Smallest sphere of influence the ship is inside; the Sun otherwise.
        public static Body Dominant(SolarSystem sys, D3 p, double jd)
        {
            Body best = sys.Sun; double bestSoi = double.MaxValue;
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null || Career.IsCraft(b)) continue; // a ship (derelict, beacon) pulls nothing: never the frame
                double soi = Math.Max(b.A * Math.Pow(b.GM / b.Parent.GM, 0.4), b.Radius * 1.5);
                if (soi < bestSoi && (sys.Pos(b, jd) - p).Mag < soi) { best = b; bestSoi = soi; }
            }
            return best;
        }

        // Gravity at the end of the last step, reused as the start of the next when nothing has moved the ship since.
        D3 gEnd, gEndPos; double gEndJd = double.NaN; SolarSystem gEndSys;

        // Advance by h seconds at Julian date jd (the start of the step).
        public void Step(SolarSystem sys, double jd, double h, Body reference)
        {
            dominant = reference;
            var g = sys == gEndSys && jd == gEndJd && (Pos - gEndPos).Sqr == 0 ? gEnd : sys.Gravity(Pos, jd);
            // the pilot's stick or RCS takes over from the nav computer (RCS is not a trim on top of the plan)
            if (ManualInput && PlanActive) { Plan = Plan.None; Phase = "pilot override"; }
            StickDropsHold();

            D3? want = null;
            double throttle = ThrottleCmd;
            D3 autoRcs = D3.Zero;
            if (PlanActive)
            {
                var cmd = Guide(sys, jd, h, g);
                // Small corrections (trimming an orbit, holding station) go to the RCS, which pushes in any direction
                // without turning the ship; only real burns swing the nose round for the main drive. Otherwise a
                // jittery small command can keep the nose chasing it and the drive never lines up (QA-008).
                if (cmd.HasValue && cmd.Value.Mag <= RcsAccel) { autoRcs = cmd.Value; throttle = 0; }
                else if (cmd.HasValue) { want = cmd.Value.Norm; throttle = Math.Min(cmd.Value.Mag / AMax, 1); }
                else throttle = 0;
            }
            else
            {
                want = HoldDirMid(sys, jd, h, reference, g);
                Phase = ThrottleCmd > 0 ? "burning" : RcsInput.sqrMagnitude > 0 ? "RCS" : "coasting";
            }
            if (Dry) { throttle = 0; autoRcs = D3.Zero; if (PlanActive) Phase = "out of fuel"; }
            UpdateAttitude(want, h);
            // the drive fires only along the commanded line: flip first, then burn
            if (PlanActive && want.HasValue && D3.Dot(Heading, want.Value) < Math.Cos(3 * Math.PI / 180)) { throttle = 0; Phase = "turning"; }
            Throttle = throttle;

            var rcs = RcsInput.sqrMagnitude > 0 && !Dry ? Frame.FromU(Att * Vector3.ClampMagnitude(RcsInput, 1)) * RcsAccel : autoRcs;
            RcsActive = rcs.Sqr > 0;
            RcsLevel = (float)Math.Min(1, Math.Sqrt(rcs.Sqr) / RcsAccel);
            if (Landed != null)
            {
                var up = (Pos - sys.Pos(Landed, jd)).Norm;
                bool liftoff = D3.Dot(Heading * (Throttle * AMax) + rcs, up) > 0;
                if (!liftoff)
                {
                    // sitting on the surface: ride along with the body
                    Pos = sys.Pos(Landed, jd + h / 86400) + up * SurfaceRadius(Landed, up) * 1.0001;
                    Vel = sys.Vel(Landed, jd);
                    Phase = "landed on " + Landed.Name;
                    return;
                }
                Landed = null;
            }

            // Velocity Verlet, with gravity sampled at both ends of the step and the drive's thrust (constant across the
            // step) integrated exactly, so a burn in free space is exact at any step size; the old semi-implicit Euler was
            // first order (a 300 s 1 g burn ended h/300 long: 10 % at h = 30 s). Error budget against a 0.05 s reference
            // (Ship harness, issue #13): a 2 h 1 g burn 0.2 AU from Earth, 1 mm at h = 30 s (was 1,000 km); 30 min of
            // burns and coasts from low Earth orbit with a fixed heading, 45 m at h = 5 s, 0.2 km at 10 s, 1.6 km at 30 s
            // (was 37, 74, 222 km). The scene's substeps there are ~9 s (1 % of the orbit's radian time) and ≤ 30 s under
            // power anywhere. Holds that track the ship's motion are aimed at mid-step (HoldDirMid): a prograde hold on
            // the same LEO sequence is 0.38 km off at h = 10 s and 3.3 km at 30 s (was 45 and 136 km). The autopilot
            // still re-aims once per step; it is a feedback loop, so that shows as guidance error, not drift.
            var thrust = Heading * (Throttle * AMax) + rcs;
            double dv = (Throttle * AMax + rcs.Mag) * h;
            // reaction mass that runs out partway through the step drives the ship for only the fraction f of h it lasts
            double f = 1, dryMass = Mass - Fuel;
            if (!UnlimitedFuel && dv > 0 && dryMass > 0)
            {
                double dvLeft = Ve * Math.Log(Mass / dryMass);
                if (dvLeft < dv) f = dvLeft / dv;
            }
            // thrust on for f·h then off moves the ship a·h²·f(1 − f/2) and changes its velocity by a·f·h
            Pos += Vel * h + g * (0.5 * h * h) + thrust * (h * h * f * (1 - 0.5 * f));
            double jd1 = jd + h / 86400;
            gEnd = sys.Gravity(Pos, jd1); gEndPos = Pos; gEndJd = jd1; gEndSys = sys;
            Vel += (g + gEnd) * (0.5 * h) + thrust * (f * h);
            DvUsed += dv * f;
            if (Throttle > 0) BurnSeconds += h * f;
            if (!UnlimitedFuel) Fuel = f < 1 ? 0 : Math.Max(0, Fuel - Mass * (1 - Math.Exp(-dv / Ve)));
            CheckTouchdown(sys, jd1);
        }

        // Attitude only (coasting on rails): holds and manual rotation still move the nose.
        public void Turn(SolarSystem sys, double jd, double h, Body reference)
        {
            if (ManualInput && PlanActive) { Plan = Plan.None; Phase = "pilot override"; }
            StickDropsHold();
            UpdateAttitude(PlanActive ? (D3?)null : HoldDir(sys, jd, reference), h);
        }

        // The stick always wins: turning by hand releases the hold for good. A hold that came back when the key was
        // released swung the nose away from where the pilot had just put it. RCS translation keeps the hold.
        void StickDropsHold() { if (RotInput.sqrMagnitude > 0) Hold = Hold.Off; }

        // #27: the body velocity holds and the speed readout are measured against; null means the dominant body (the
        // orbit). The pilot can switch it to the target to brake against that instead (not saved).
        public Body VelRef;

        // #13: under power, a hold that follows the ship's own motion (prograde, normal, radial, target) is aimed at
        // the state predicted for the middle of the step, so the thrust it steers is second order in h. Aimed at the
        // start, it lagged half a step: 45 km after 30 min of prograde burns from LEO at h = 10 s.
        D3? HoldDirMid(SolarSystem sys, double jd, double h, Body reference, D3 g)
        {
            if (Hold == Hold.Off || Hold == Hold.Custom || ThrottleCmd <= 0 || !(h > 0)) return HoldDir(sys, jd, reference);
            D3 p = Pos, v = Vel, a = g + Heading * (ThrottleCmd * AMax);
            Pos = p + v * (0.5 * h) + a * (0.125 * h * h); Vel = v + a * (0.5 * h);
            var d = HoldDir(sys, jd + 0.5 * h / 86400, reference);
            Pos = p; Vel = v;
            return d;
        }

        D3? HoldDir(SolarSystem sys, double jd, Body reference)
        {
            if (Hold == Hold.Off) return null;
            if (Hold == Hold.Custom) return CustomDir;
            if ((Hold == Hold.Target || Hold == Hold.AntiTarget) && Target != null)
            {
                var d = (sys.Pos(Target, jd) - Pos).Norm;
                return Hold == Hold.Target ? d : -d;
            }
            var vr = VelRef ?? reference;
            var v = Vel - sys.Vel(vr, jd);
            var r = Pos - sys.Pos(vr, jd);
            if (v.Sqr < 1e-12) return null;
            var n = D3.Cross(r, v).Norm;
            switch (Hold)
            {
                case Hold.Prograde: return v.Norm;
                case Hold.Retrograde: return -v.Norm;
                case Hold.Normal: return n;
                case Hold.AntiNormal: return -n;
                case Hold.RadialOut: return D3.Cross(v, n).Norm;
                case Hold.RadialIn: return -D3.Cross(v, n).Norm;
            }
            return null;
        }

        // Rigid-body attitude. With flight assist on, the stick sets the turn rate (an axis stops when its key is
        // released, even with another held); with it off, the stick is torque and the spin stays. A hold or plan slews
        // the nose with the same acceleration and rate limits. Torque comes from reaction wheels (electric), so turning
        // spends no reaction mass and works with a dry tank. Long steps are split into short ones so the result doesn't
        // depend on frame rate or time compression (#16); only a hold or plan over a long step (time compression) slews
        // at the rate limit alone, and a free spin turns exactly.
        const float AttStep = 0.05f;  // s, longest rigid-body substep
        const int MaxAttSteps = 64;

        void UpdateAttitude(D3? want, double h)
        {
            float dt = (float)h;
            bool manual = RotInput.sqrMagnitude > 0;
            if (!manual && want.HasValue && dt > 0.25f)
            {
                var to = Quaternion.LookRotation(Frame.U(want.Value), Att * Vector3.up);
                Att = Quaternion.RotateTowards(Att, to, MaxRate * dt * Mathf.Rad2Deg);
                AngVel = Vector3.zero;
                FlipDone(want.Value);
                return;
            }
            if (!manual && !want.HasValue)
            {
                if (AngVel.sqrMagnitude == 0) return;
                if (!FlightAssist)
                {
                    // nothing acts on the ship: it keeps spinning about the same body axis
                    double turn = AngVel.magnitude * h % (2 * Math.PI);
                    Att = Quaternion.Normalize(Att * Quaternion.AngleAxis((float)turn * Mathf.Rad2Deg, AngVel));
                    return;
                }
            }
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / AttStep), 1, MaxAttSteps);
            for (int i = 0; i < n; i++) AttitudeStep(want, dt / n, manual);
            Att = Quaternion.Normalize(Att);
            if (!manual && want.HasValue) FlipDone(want.Value);
        }

        // A flip is one manoeuvre, not a standing hold: on arrival the wheels stop the ship and the hold ends, so the
        // nose doesn't keep going back to a direction fixed when the key was pressed.
        void FlipDone(D3 want)
        {
            if (Hold != Hold.Custom || AngVel.magnitude > 0.12f || Vector3.Angle(Att * Vector3.forward, Frame.U(want)) > 0.25f) return;
            Hold = Hold.Off; AngVel = Vector3.zero;
        }

        void AttitudeStep(D3? want, float dt, bool manual)
        {
            if (manual && FlightAssist) AngVel = Vector3.MoveTowards(AngVel, Vector3.ClampMagnitude(RotInput, 1) * MaxRate, AngAccel * dt);
            else if (manual) AngVel += RotInput * (AngAccel * dt);
            else if (want.HasValue)
            {
                var fwd = Att * Vector3.forward;
                var to = Frame.U(want.Value).normalized;
                var axis = Vector3.Cross(fwd, to);
                if (axis.sqrMagnitude < 1e-8f && Vector3.Dot(fwd, to) < 0) axis = Att * Vector3.up * 1e-3f;
                float angle = Mathf.Atan2(axis.magnitude, Vector3.Dot(fwd, to));
                // rate that stops exactly on target under constant deceleration, as body-frame angular velocity
                var wantW = axis.sqrMagnitude > 1e-12f ? axis.normalized * Mathf.Min(MaxRate, Mathf.Sqrt(2 * AngAccel * angle) * 0.9f) : Vector3.zero;
                var wBody = Quaternion.Inverse(Att) * wantW;
                AngVel = Vector3.MoveTowards(AngVel, wBody, AngAccel * dt);
            }
            else if (FlightAssist) AngVel = Vector3.MoveTowards(AngVel, Vector3.zero, AngAccel * dt);
            AngVel = Vector3.ClampMagnitude(AngVel, MaxRate);
            if (AngVel.sqrMagnitude > 0) Att = Att * Quaternion.AngleAxis(AngVel.magnitude * dt * Mathf.Rad2Deg, AngVel);
        }

        // Anything that hits a surface stops on it (no crash model yet). Bodies come from the scene's Near list, at
        // the given date; using the step's start date keeps the ephemeris cache warm.
        // #25: contact faster than this (km/s, relative to the body's centre, as the landed ship rides it) destroys the
        // ship; the scene then reloads the last save. Forgiving (the -fowqa sandbox) lands it anyway and says so.
        public const double SafeTouchdown = 0.02;
        public bool Forgiving;
        public Body Crashed;
        public double ImpactSpeed;

        public void CheckTouchdown(SolarSystem sys, double jd)
        {
            foreach (var b in Near)
            {
                if (b == sys.Sun) continue;
                var bp = sys.Pos(b, jd);
                var r = Pos - bp;
                double R = SurfaceRadius(b, r);
                if (r.Mag < R)
                {
                    // a step or coast arc can end below the surface; take the speed back up to it, since the fall from
                    // the surface to here (μ(1/r − 1/R)) would otherwise turn a gentle touchdown into a crash
                    double v = (Vel - sys.Vel(b, jd)).Mag;
                    ImpactSpeed = Math.Sqrt(Math.Max(0, v * v - 2 * b.GM * (1 / r.Mag - 1 / R)));
                    Pos = bp + r.Norm * R * 1.0001;
                    Vel = sys.Vel(b, jd);
                    Landed = b; Plan = Plan.None; ThrottleCmd = 0; Throttle = 0; AngVel = Vector3.zero;
                    bool giant = b.Look == Look.Gas || b.Look == Look.Ice;
                    Phase = giant ? "in " + b.Name + "'s cloud tops (no surface): climb out" : "landed on " + b.Name;
                    if (ImpactSpeed > SafeTouchdown)
                    {
                        if (Forgiving) Phase += $" (impact {ImpactSpeed * 1000:F0} m/s: sandbox, no damage)";
                        else { Crashed = b; Phase = $"destroyed: hit {b.Name} at {ImpactSpeed * 1000:F0} m/s"; }
                    }
                    break;
                }
            }
        }

        // Nav-computer guidance: the acceleration the plan wants this step (gravity compensated), or null to coast.
        D3? Guide(SolarSystem sys, double jd, double h, D3 gShip)
        {
            var T = Target;
            var tp = sys.Pos(T, jd);
            var tv = sys.Vel(T, jd);
            var r = tp - Pos;
            double dist = r.Mag;
            var vrel = Vel - tv;
            double aMax = AMax, tau = Math.Max(20, 4 * h);
            // a target under thrust accelerates by its drive, not by gravity
            var gRel = gShip - (T.PathAccel != null ? T.PathAccel(jd) : sys.Gravity(tp, jd, T));
            if (Plan == Plan.Pursue) return Pursue(sys, jd, r, dist, vrel, -gRel, tau);

            if (Plan == Plan.MatchVelocity)
            {
                if (vrel.Mag < 0.0005) { Phase = "velocity matched"; return null; }
                Phase = "matching velocity";
                return Budget(-vrel / tau, gRel);
            }
            if (Plan == Plan.HoldStation)
            {
                var want = (StationOffset - (Pos - tp)) * 0.002;
                var e = want - vrel;
                if (e.Mag < 0.0005 && (StationOffset - (Pos - tp)).Mag < 1) { Phase = "holding station"; return -gRel; }
                Phase = "station keeping";
                return Budget(e / tau, gRel);
            }

            // Brachistochrone: steer relative velocity onto v = sqrt(2·a·distance) toward the target, so the ship
            // accelerates, flips at the right moment and brakes. Near the target it parks in a circular orbit.
            double standoff = Standoff(T);
            D3 vDes, ff = D3.Zero;
            bool orbiting = dist < standoff * 1.25 && vrel.Mag < Math.Sqrt(T.GM / Math.Max(dist, 1)) * 2 + 0.5;
            if (!orbiting)
            {
                // one clearance guard for every body near the path: climb out before anything else
                var guard = Clearance(sys, jd);
                if (guard.HasValue) { cruising = false; parked = false; Phase = "climbing clear of " + guardBody.Name; return guard; }
                double vb = Math.Sqrt(2 * aMax * 0.85 * Math.Max(dist - standoff, 0));
                // burn–coast–burn: hold the cruise speed on rails until the braking point
                bool capped = CruiseCap > 0 && vb > CruiseCap;
                vDes = Avoid(sys, jd, r.Norm, dist) * (capped ? CruiseCap : vb);
                if (capped)
                {
                    cruising = (vDes - vrel).Mag < CruiseCap * (cruising ? 0.02 : 0.005);
                    if (cruising) { Phase = "cruising"; return null; }
                }
                else cruising = false;
                // On or above the braking curve, feed its own deceleration forward. A P-controller alone (tau up to
                // 120 s at 30 s steps) trails a curve falling at 0.85·a by about 0.85·a·tau, ~1 km/s at 1 g, and the
                // ship reaches the standoff too fast to stop: harmless around a planet it can orbit, but it flew
                // through Phobos (11 km) on the career suite's arrival from behind Mars.
                if (!capped && vb > 0 && D3.Dot(vrel, vDes.Norm) >= vb) ff = -vDes.Norm * (aMax * 0.85);
            }
            else
            {
                var radial = (-r).Norm;
                var tangent = D3.Cross(T.Pole, radial);
                if (tangent.Sqr < 1e-9) tangent = D3.Cross(new D3(1, 0, 0), radial);
                vDes = tangent.Norm * Math.Sqrt(T.GM / dist) + radial * (-(dist - standoff) * 0.002);
            }
            // bodies too small to orbit against their parent's tides (Phobos, Deimos): station-keep beside them instead
            bool hover = orbiting && T.GM < 1;
            if (hover)
            {
                vDes = (-r).Norm * (-(dist - standoff) * 0.002);
                // The braking feedforward ends at 1.25 standoffs, and the 20 s controller alone carried the ship 830 m
                // past a 1 km craft standoff. Keep braking at what it takes to stop by half the standoff: v²/2s fades
                // to nothing at rest, so it does not kick at the 30 s steps of a ship holding station.
                double closing = D3.Dot(vrel, r.Norm);
                if (closing > 0) ff = -r.Norm * Math.Min(aMax * 0.85, closing * closing / (2 * Math.Max(dist - standoff * 0.5, 0.05)));
            }
            var err = vDes - vrel;
            // in orbit, gravity is what keeps us there; otherwise cancel it first, then steer with what is left
            var cmd = orbiting && !hover ? err / tau : Budget(err / tau + ff, gRel);
            if (orbiting && !hover)
            {
                // Parked when the orbit has the right shape: speed near circular, little radial motion, radius near the
                // standoff. Judging by shape (with looser limits to leave) rather than by the controller error stops
                // a ship in a fine, slightly eccentric orbit from trimming forever.
                double vc = Math.Sqrt(T.GM / dist), vr = D3.Dot(vrel, (-r).Norm);
                double tol = parked ? 2 : 1;
                if (Math.Abs(vrel.Mag - vc) < 0.03 * vc * tol && Math.Abs(vr) < 0.03 * vc * tol && Math.Abs(dist - standoff) < 0.15 * standoff * tol)
                { parked = true; Phase = "in orbit"; return null; }
            }
            parked = false;
            Phase = hover ? "holding station" : orbiting ? "circularising" : D3.Dot(cmd, r) >= 0 ? "accelerating" : "flipped, braking";
            return cmd;
        }

        // Radius of the surface (or the 1-bar cloud tops) under a direction r from the body's centre: the oblate
        // spheroid R(φ) = Req (1 − f sin²φ), times any irregular-shape factor.
        public static double SurfaceRadius(Body b, D3 r)
        {
            double s = D3.Dot(r.Norm, b.Pole);
            return b.Radius * Math.Max(b.Shape.x, Math.Max(b.Shape.y, b.Shape.z)) * (1 - b.Flattening * s * s);
        }

        // Thrust allocation for a saturating drive: cancel the relative gravity first, then spend what is left of the
        // drive's limit along the steering command. A plain clamp of (steer − g) scales the gravity term away when the
        // steering demand is huge (leaving orbit for a target AUs away), and the ship falls into the planet (QA-019).
        D3 Budget(D3 steer, D3 gRel)
        {
            var comp = -gRel;
            double left = AMax - comp.Mag;
            if (left <= 0) return comp.Norm * AMax;
            double s = steer.Mag;
            return s > left ? comp + steer * (left / s) : comp + steer;
        }

        // Plan.Pursue, a pirate on the player (Pursuit.cs). The target thrusts, so comp (what matches its acceleration)
        // can take most of the drive. Budget leaves AMax − |comp| for steering in every direction, but the drive has more
        // when the steering opposes comp: a 2 g pirate behind a 1 g ship burning away could brake at only 1 g against a
        // curve that assumed 1.7 g, and flew past it at 16 km/s (pirate spike, S2b). Here steering gets all the drive has
        // left along it, and the braking curve all it has left along the braking line. With nothing left (|comp| ≥ AMax)
        // the pirate only matches: a hand burn at exactly its g leaves it no spare, and one of the same g behind the
        // autopilot's full burn closes only on Budget's reserve (about 4e-4 g on a Vesta–Mars leg), so
        // burning harder escapes. The curve counts on the target's present thrust: a target that cuts its drive or flips
        // under it, by hand or as the autopilot does at turnover and at a coast trip's brake start, takes that spare away.
        public const double PursueMargin = 0.85; // [design] the braking curve's share of that spare, as Brachistochrone's

        D3? Pursue(SolarSystem sys, double jd, D3 r, double dist, D3 vrel, D3 comp, double tau)
        {
            double standoff = Standoff(Target);
            var los = dist > 0 ? r / dist : Heading;
            bool hold = dist < standoff * 1.25 && vrel.Mag < 0.5;
            if (!hold && Clearance(sys, jd) is D3 guard) { Phase = "climbing clear of " + guardBody.Name; return guard; }
            if (comp.Sqr >= AMax * AMax) { Phase = "matching its burn"; return comp.Norm * AMax; }
            D3 want, ff = D3.Zero;
            if (hold)
            {
                // station-keeping beside it, as Brachistochrone's hover: back to the standoff, braking to stop by half of it
                want = los * ((dist - standoff) * 0.002);
                double closing = D3.Dot(vrel, los);
                if (closing > 0) ff = -los * Math.Min(PursueMargin * Spare(comp, -los, AMax), closing * closing / (2 * Math.Max(dist - standoff * 0.5, 0.05)));
                Phase = "holding station";
                return Share((want - vrel) / tau + ff, comp);
            }
            var dir = Avoid(sys, jd, los, dist);
            double brake = PursueMargin * Spare(comp, -dir, AMax), vb = Math.Sqrt(2 * brake * Math.Max(dist - standoff, 0));
            want = dir * vb;
            // on or above the braking curve, feed its own deceleration forward (see Brachistochrone)
            if (vb > 0 && D3.Dot(vrel, dir) >= vb) ff = -dir * brake;
            var cmd = Share((want - vrel) / tau + ff, comp);
            // what the drive does, not where the steering points: braking against a target that pulls away can need no thrust
            Phase = cmd.Mag <= RcsAccel ? "trimming (RCS)" : D3.Dot(cmd, los) >= 0 ? "closing" : "braking";
            return cmd;
        }

        // The most the drive has left along unit vector u once it matches the target: the largest k ≥ 0 with
        // |comp + k·u| ≤ aMax, k = −(comp·u) + sqrt((comp·u)² − |comp|² + aMax²). 0 when it cannot match at all.
        public static double Spare(D3 comp, D3 u, double aMax)
        {
            double cu = D3.Dot(comp, u), disc = cu * cu - comp.Sqr + aMax * aMax;
            return comp.Sqr >= aMax * aMax || disc <= 0 ? 0 : -cu + Math.Sqrt(disc);
        }

        // Pursue's Budget: comp first, then the steering, cut to what the drive has left along it.
        D3 Share(D3 steer, D3 comp)
        {
            double s = steer.Mag;
            return s > 0 ? comp + steer * (Math.Min(s, Spare(comp, steer / s, AMax)) / s) : comp;
        }

        Body guardBody;

        // If the two-body arc around the body we're inside would come within 10 % of its surface, burn radially out and
        // along the motion (raising periapsis) until it won't.
        Body dominant; // the scene's per-frame dominant body, passed into Step

        D3? Clearance(SolarSystem sys, double jd)
        {
            var dom = dominant ?? Dominant(sys, Pos, jd);
            foreach (var b in Obstacles)
                if (Climb(sys, jd, b) is D3 c) return c;
            return Obstacles.Contains(dom) ? null : Climb(sys, jd, dom);
        }

        // Climb away from b when the ship is falling toward it and either (a) its two-body arc would hit the body
        // (periapsis under 1.1 R), or (b) it is falling too fast to stop above the safe radius with the drive left over
        // after gravity (the case of a 1 g ship that kills its orbital speed to head for the Moon behind Earth).
        D3? Climb(SolarSystem sys, double jd, Body b)
        {
            if (b == null || b == sys.Sun || b == Target || b.GM < 1) return null;
            var r = Pos - sys.Pos(b, jd);
            double rm = r.Mag;
            if (rm > 40 * b.Radius) return null; // cheap distance cut before the (uncached) velocity lookup
            var v = Vel - sys.Vel(b, jd);
            var outward = r / rm;
            double vr = D3.Dot(v, outward), vin = -vr;
            if (vin <= 0 && rm > b.Radius * 1.2) return null; // climbing and clear
            double rp = Periapsis(r, v, b.GM), safe = b.Radius * 1.5 + 200;
            double net = AMax - b.GM / (rm * rm);
            bool impact = rp < b.Radius * 1.1;
            bool cannotStop = vin > 0 && rp < safe && rm - safe < vin * vin / Math.Max(net, 1e-9) + 0.3 * safe;
            if (!impact && !cannotStop) return null;
            guardBody = b;
            var along = v.Sqr > 1e-12 && (v - outward * vr).Sqr > 1e-12 ? (v - outward * vr).Norm : D3.Cross(b.Pole, outward).Norm;
            return (outward + along).Norm * AMax;
        }

        // Bend the straight-line course around any body (other than the target) it would pass through.
        D3 Avoid(SolarSystem sys, double jd, D3 dir, double dist)
        {
            foreach (var b in Obstacles)
            {
                if (b == Target || b.Parent == null) continue;
                var c = sys.Pos(b, jd);
                var rel = c - Pos;
                double d = rel.Mag;
                if (d > 40 * b.Radius || d > dist) continue;
                double safe = b.Radius * 1.5 + 200;
                if (d < safe) { dir = (dir + (-rel).Norm * 1.5).Norm; continue; } // too close: climb out first
                double t = D3.Dot(rel, dir);
                if (t <= 0) continue;
                var miss = Pos + dir * t - c;
                double m = miss.Mag;
                if (m >= safe) continue;
                var perp = m > 1e-3 ? miss / m : D3.Cross(dir, b.Pole).Norm;
                dir = ((c + perp * safe) - Pos).Norm; // aim at the tangent point just outside the safe radius
            }
            return dir;
        }

        // Two-body periapsis radius of relative state (r, v) about GM; for an escape orbit, the hyperbola's.
        static double Periapsis(D3 r, D3 v, double mu)
        {
            double h = D3.Cross(r, v).Mag, eps = v.Sqr / 2 - mu / r.Mag;
            double e = Math.Sqrt(Math.Max(0, 1 + 2 * eps * h * h / (mu * mu)));
            return h * h / mu / (1 + e);
        }

        // Flip-and-burn trip time to the target from rest: t = 2·sqrt(d / a).
        public double EtaSeconds(SolarSystem sys, double jd)
        {
            if (Target == null) return 0;
            double d = Math.Max((sys.Pos(Target, jd) - Pos).Mag - Standoff(Target), 0);
            return 2 * Math.Sqrt(d / AMax);
        }

        // QA-061: a brachistochrone from the ship's state, not from rest. u is the closing speed along the line to the
        // target (negative: moving apart, which costs a stop and the ground lost while stopping), w the sideways drift
        // the plan also cancels, cap the coast speed (0 = none). Braking is taken at the full a; the guidance brakes on
        // a 0.85·a curve, which costs a little less. Returns Δv (km/s) and burn-plus-coast time (s), without parking.
        public static (double dv, double t) TripFrom(double d, double a, double u, double w, double cap = 0)
        {
            double along, t;
            if (u > 0 && u * u / (2 * a) > d)
            {
                // too fast to stop short: brake, overshoot, and come back from rest
                double vb = Math.Sqrt(a * (u * u / (2 * a) - d));
                along = u + 2 * vb; t = along / a;
            }
            else
            {
                double vp = Math.Sqrt(a * d + u * u / 2);
                if (cap > 0 && cap < vp)
                {
                    // speed up (or slow down) to the cap, coast, brake from the cap
                    double d1 = (cap >= u ? cap * cap - u * u : u * u - cap * cap) / (2 * a);
                    along = Math.Abs(cap - u) + cap;
                    t = along / a + Math.Max(d - d1 - cap * cap / (2 * a), 0) / cap;
                }
                else { along = 2 * vp - u; t = along / a; }
            }
            return (Math.Sqrt(along * along + w * w), t);
        }

        // Closing speed toward the target (negative: moving apart) and the sideways drift, both relative to it.
        public (double u, double w) RelToTarget(SolarSystem sys, double jd)
        {
            if (Target == null) return (0, 0);
            var r = (sys.Pos(Target, jd) - Pos).Norm;
            var v = sys.Vel(Target, jd) - Vel;
            double u = -D3.Dot(v, r);
            return (u, (v + r * u).Mag);
        }

        // Hohmann transfer between the circular orbits of the ship's and the target's heliocentric distance (an
        // economy reference next to the brachistochrone): total Δv (km/s) and transfer time (s).
        public static (double dv, double t) Hohmann(double r1, double r2, double mu)
        {
            double at = (r1 + r2) / 2;
            double dv1 = Math.Abs(Math.Sqrt(mu / r1) * (Math.Sqrt(r2 / at) - 1));
            double dv2 = Math.Abs(Math.Sqrt(mu / r2) * (1 - Math.Sqrt(r1 / at)));
            return (dv1 + dv2, Math.PI * Math.Sqrt(at * at * at / mu));
        }

        // Save / load (doubles as round-trip strings).
        [Serializable] public class SaveData
        {
            public double jd, px, py, pz, vx, vy, vz, maxG, dv;
            public float qx, qy, qz, qw;
            public string target;
            public int warp;
            public bool assist;
            // what Continue does not fly on, kept so the load can say so; the spin and the landing it does keep (#18).
            // Older saves lack these and read as none.
            public int plan, hold;
            public double throttle;
            public float wx, wy, wz;
            public string landed;
        }

        public SaveData ToSave(double jd, int warp) => new SaveData
        {
            jd = jd, px = Pos.x, py = Pos.y, pz = Pos.z, vx = Vel.x, vy = Vel.y, vz = Vel.z, maxG = MaxG, dv = DvUsed,
            // a raider is not saved (D8), so neither is it as the target: the Continue line would name a ship that is gone
            qx = Att.x, qy = Att.y, qz = Att.z, qw = Att.w, target = Career.IsRaider(Target) ? null : Target?.Name, warp = warp, assist = FlightAssist,
            plan = (int)(PlanActive ? Plan : Plan.None), hold = (int)Hold, throttle = ThrottleCmd,
            wx = AngVel.x, wy = AngVel.y, wz = AngVel.z, landed = Landed?.Name,
        };

        public void FromSave(SaveData s, SolarSystem sys)
        {
            Pos = new D3(s.px, s.py, s.pz); Vel = new D3(s.vx, s.vy, s.vz); MaxG = s.maxG; DvUsed = s.dv;
            Att = Quaternion.Normalize(new Quaternion(s.qx, s.qy, s.qz, s.qw));
            // with flight assist off nothing damps a spin: keep it (assist on would have stopped it anyway)
            AngVel = s.assist ? Vector3.zero : Vector3.ClampMagnitude(new Vector3(s.wx, s.wy, s.wz), MaxRate);
            // a job's ship is rebuilt with the career: the scene resolves the target again after that
            Target = string.IsNullOrEmpty(s.target) ? null : sys[s.target];
            FlightAssist = s.assist; Plan = Plan.None; Hold = Hold.Off; ThrottleCmd = 0; Throttle = 0;
            Landed = string.IsNullOrEmpty(s.landed) ? null : sys[s.landed];
        }
    }
}
