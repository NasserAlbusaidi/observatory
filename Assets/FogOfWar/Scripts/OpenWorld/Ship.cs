using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Attitude holds (SAS), relative to the dominant body's orbit or to the target.
    public enum Hold { Off, Prograde, Retrograde, Normal, AntiNormal, RadialOut, RadialIn, Target, AntiTarget, Custom }

    // Nav-computer plans flown by the autopilot.
    public enum Plan { None, Brachistochrone, MatchVelocity, HoldStation }

    // The player's torch ship: a point mass under every body's gravity, with a main drive along its nose, RCS
    // translation, and a rigid-body attitude (finite angular acceleration and rate). Positions are heliocentric ecliptic
    // km, velocities km/s. Attitude lives in the render frame (Frame.U), which is fixed to the ecliptic.
    public class Ship
    {
        public const double G0 = 9.80665e-3;    // km/s^2
        public const double RcsAccel = 0.1 * G0; // RCS translation, all axes
        public const float AngAccel = 1.5f;      // rad/s^2 (RCS torque)
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

        // Standoff from a target's centre where the autopilot parks the ship in orbit.
        public static double Standoff(Body b) => Math.Max(b.Radius * 3, b.Radius + 300);

        // Smallest sphere of influence the ship is inside; the Sun otherwise.
        public static Body Dominant(SolarSystem sys, D3 p, double jd)
        {
            Body best = sys.Sun; double bestSoi = double.MaxValue;
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null) continue;
                double soi = Math.Max(b.A * Math.Pow(b.GM / b.Parent.GM, 0.4), b.Radius * 1.5);
                if (soi < bestSoi && (sys.Pos(b, jd) - p).Mag < soi) { best = b; bestSoi = soi; }
            }
            return best;
        }

        // Advance by h seconds at Julian date jd (the start of the step).
        public void Step(SolarSystem sys, double jd, double h, Body reference)
        {
            dominant = reference;
            var g = sys.Gravity(Pos, jd);
            if (RotInput.sqrMagnitude > 0 && PlanActive) { Plan = Plan.None; Phase = "pilot override"; }

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
                want = HoldDir(sys, jd, reference);
                Phase = ThrottleCmd > 0 ? "burning" : RcsInput.sqrMagnitude > 0 ? "RCS" : "coasting";
            }
            if (Dry) { throttle = 0; autoRcs = D3.Zero; if (PlanActive) Phase = "out of reaction mass"; }
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
                    Pos = sys.Pos(Landed, jd + h / 86400) + up * Landed.Radius * (1 - Landed.Flattening * 0.5) * 1.0001;
                    Vel = sys.Vel(Landed, jd);
                    Phase = "landed on " + Landed.Name;
                    return;
                }
                Landed = null;
            }

            var a = g + Heading * (Throttle * AMax) + rcs;
            Vel += a * h;
            Pos += Vel * h;
            double dv = (Throttle * AMax + rcs.Mag) * h;
            DvUsed += dv;
            if (!UnlimitedFuel) Fuel = Math.Max(0, Fuel - Mass * (1 - Math.Exp(-dv / Ve)));
            CheckTouchdown(sys, jd);
        }

        // Attitude only (coasting on rails): holds and manual rotation still move the nose.
        public void Turn(SolarSystem sys, double jd, double h, Body reference)
        {
            if (RotInput.sqrMagnitude > 0 && PlanActive) Plan = Plan.None;
            UpdateAttitude(PlanActive ? (D3?)null : HoldDir(sys, jd, reference), h);
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
            var v = Vel - sys.Vel(reference, jd);
            var r = Pos - sys.Pos(reference, jd);
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

        // Rigid-body attitude. Pilot input accelerates the rotation; with flight assist on, the ship stops rotating when
        // the stick is released. A hold or plan slews the nose with the same acceleration and rate limits. Long time
        // steps (time compression) use the rate limit alone.
        void UpdateAttitude(D3? want, double h)
        {
            float dt = (float)h;
            bool manual = RotInput.sqrMagnitude > 0;
            if (dt > 0.25f)
            {
                if (manual) Att = Att * Quaternion.Euler(RotInput * (MaxRate * dt * Mathf.Rad2Deg));
                else if (want.HasValue)
                {
                    var to = Quaternion.LookRotation(Frame.U(want.Value), Att * Vector3.up);
                    Att = Quaternion.RotateTowards(Att, to, MaxRate * dt * Mathf.Rad2Deg);
                }
                else if (!FlightAssist && AngVel.sqrMagnitude > 0) Att = Att * Quaternion.AngleAxis(Mathf.Min(AngVel.magnitude * dt, 3.14f) * Mathf.Rad2Deg, AngVel);
                if (FlightAssist || want.HasValue) AngVel = Vector3.zero;
                return;
            }
            if (manual) AngVel += RotInput * (AngAccel * dt);
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
            Att = Quaternion.Normalize(Att);
        }

        // Anything that hits a surface stops on it (no crash model yet). Bodies come from the scene's Near list, at
        // the given date; using the step's start date keeps the ephemeris cache warm.
        public void CheckTouchdown(SolarSystem sys, double jd)
        {
            foreach (var b in Near)
            {
                if (b == sys.Sun) continue;
                var bp = sys.Pos(b, jd);
                var r = Pos - bp;
                double R = SurfaceRadius(b, r) * 0.999;
                if (r.Mag < R)
                {
                    Pos = bp + r.Norm * SurfaceRadius(b, r) * 1.0001;
                    Vel = sys.Vel(b, jd);
                    Landed = b; Plan = Plan.None; ThrottleCmd = 0; Throttle = 0; AngVel = Vector3.zero;
                    bool giant = b.Look == Look.Gas || b.Look == Look.Ice;
                    Phase = giant ? "in " + b.Name + "'s cloud tops (no surface): climb out" : "landed on " + b.Name;
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
            var gRel = gShip - sys.Gravity(tp, jd, T);

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
            D3 vDes;
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
            if (hover) vDes = (-r).Norm * (-(dist - standoff) * 0.002);
            var err = vDes - vrel;
            // in orbit, gravity is what keeps us there; otherwise cancel it first, then steer with what is left
            var cmd = orbiting && !hover ? err / tau : Budget(err / tau, gRel);
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
        }

        public SaveData ToSave(double jd, int warp) => new SaveData
        {
            jd = jd, px = Pos.x, py = Pos.y, pz = Pos.z, vx = Vel.x, vy = Vel.y, vz = Vel.z, maxG = MaxG, dv = DvUsed,
            qx = Att.x, qy = Att.y, qz = Att.z, qw = Att.w, target = Target?.Name, warp = warp, assist = FlightAssist,
        };

        public void FromSave(SaveData s, SolarSystem sys)
        {
            Pos = new D3(s.px, s.py, s.pz); Vel = new D3(s.vx, s.vy, s.vz); MaxG = s.maxG; DvUsed = s.dv;
            Att = Quaternion.Normalize(new Quaternion(s.qx, s.qy, s.qz, s.qw)); AngVel = Vector3.zero;
            Target = string.IsNullOrEmpty(s.target) ? null : sys[s.target];
            FlightAssist = s.assist; Plan = Plan.None; Hold = Hold.Off; ThrottleCmd = 0; Landed = null;
        }
    }
}
