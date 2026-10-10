using System;
using System.Collections.Generic;
using FogOfWar;

namespace Observatory.Checks
{
    public static class ExhibitChecks
    {
        static readonly List<string> results = new List<string>();
        static int failures;
        static void Check(string name, double error, double tolerance)
        {
            bool pass = !double.IsNaN(error) && !double.IsInfinity(error) && error <= tolerance;
            if (!pass) failures++;
            results.Add($"{(pass ? "PASS" : "FAIL")} {name}: {error:E3} <= {tolerance:E3}");
        }
        static void Assert(string name, bool value) => Check(name, value ? 0 : 1, 0);
        static void Reject(string name, Action action)
        {
            try { action(); Assert(name, false); }
            catch (ArgumentOutOfRangeException) { Assert(name, true); }
        }
        static void Steps(Action step, int count) { for (int i=0; i<count; i++) step(); }
        static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        static double Cross(LabVector p, LabVector v) => p.x*v.y-p.y*v.x;
        public static string[] Run()
        {
            results.Clear(); failures=0;
            Lorenz(); Reaction(); Waves(); Forces(); Lagrange(); Charges(); Resonance(); Flocks(); Galaxies();
            Reject("Lorenz rejects NaN", ()=>new LorenzModel(double.NaN));
            Reject("Lorenz rejects oversized step", ()=>new LorenzModel().Step(1));
            Reject("reaction rejects negative feed", ()=>new ReactionDiffusionModel(-1));
            Reject("wave rejects infinite speed", ()=>new WaveTankModel(speed:double.PositiveInfinity));
            Reject("central force rejects zero incoming speed", ()=>new CentralForceModel(-1,1,0));
            Reject("Lagrange rejects invalid mass ratio", ()=>new LagrangeModel(1));
            Reject("charge rejects zero magnetic field", ()=>new ChargedParticleModel(0));
            Reject("charge rejects non-unit charge", ()=>new ChargedParticleModel(charge:0));
            Reject("resonance rejects negative damping", ()=>new ResonanceModel(2,2,-1));
            Reject("flock rejects NaN weight", ()=>new FlockingModel(double.NaN));
            Reject("galaxy rejects invalid impact parameter", ()=>new GalaxyEncounterModel(0));
            if (failures>0) throw new Exception(string.Join(Environment.NewLine,results)+$"\n{failures} checks failed.");
            results.Add($"All {results.Count} exhibit checks passed. Unity rendering and input are not exercised.");
            return results.ToArray();
        }
        static void Lorenz()
        {
            var m=new LorenzModel(); double a=Math.Sqrt(8.0/3*27);
            Check("Lorenz origin equilibrium",m.Derivative(new LabVector()).Length,0);
            Check("Lorenz nonzero equilibrium",m.Derivative(new LabVector(a,a,27)).Length,1e-12);
            var fine=new LorenzModel(); Steps(()=>m.Step(),240); Steps(()=>fine.Step(LorenzModel.StepSize/2),480);
            Check("Lorenz RK4 refinement at t=1",(m.Position-fine.Position).Length,1e-4);
            var twin=new LorenzModel(); var replay=new LorenzModel();
            Steps(()=> { twin.Step(); replay.Step(); },7200);
            Check("Lorenz deterministic replay",(twin.Position-replay.Position).Length,0);
            foreach(double rho in new[]{1.0,10,28,45})
            {
                var x=new LorenzModel(rho); double radius=0;
                Steps(()=> { x.Step(); radius=Math.Max(radius,x.Position.Length); },24000);
                Check($"Lorenz rho={rho} remains bounded at t=100",radius,100);
                if(rho==10) { double e=Math.Sqrt(8.0/3*9); Check("Lorenz rho=10 settles to fixed point",Math.Min((x.Position-new LabVector(e,e,9)).Length,(x.Position-new LabVector(-e,-e,9)).Length),1e-6); }
            }
        }
        static void Reaction()
        {
            var blank=new ReactionDiffusionModel(seed:false); Steps(blank.Step,100);
            Check("reaction uniform equilibrium",blank.MeanV+Math.Abs(blank.U[0]-1),0);
            foreach(var pair in new[]{new[]{.035,.062},new[]{.029,.057},new[]{.0545,.062},new[]{.01,.04},new[]{.01,.075},new[]{.08,.04},new[]{.08,.075}})
            {
                var m=new ReactionDiffusionModel(pair[0],pair[1]); double max=0, min=0;
                Steps(()=> { m.Step(); for(int i=0;i<m.U.Length;i++) { max=Math.Max(max,Math.Max(m.U[i],m.V[i])); min=Math.Min(min,Math.Min(m.U[i],m.V[i])); } },3000);
                Check($"reaction F={pair[0]} k={pair[1]} concentration ceiling",max,1.01);
                Check($"reaction F={pair[0]} k={pair[1]} nonnegative",-min,1e-12);
            }
            var a=new ReactionDiffusionModel(); var b=new ReactionDiffusionModel(); Steps(()=>{a.Step();b.Step();},100);
            double difference=0; for(int i=0;i<a.V.Length;i++) difference+=Math.Abs(a.V[i]-b.V[i]);
            Check("reaction deterministic replay",difference,0);
            var edge=new ReactionDiffusionModel(seed:false); edge.V[0]=.1; edge.Step();
            Check("reaction periodic boundary transport",Math.Abs(edge.V[63]-.008)+Math.Abs(edge.V[63*64]-.008),1e-15);
        }
        static void Waves()
        {
            var quiet=new WaveTankModel(driven:false); Steps(quiet.Step,300); Check("wave still water",quiet.Rms,0);
            var pulse=new WaveTankModel(driven:false); pulse.Pulse(); double start=pulse.Rms; Steps(pulse.Step,3600);
            Assert("unforced wave damping dissipates pulse",pulse.Rms<start*.5);
            double edges=0; for(int i=0;i<64;i++) edges+=Math.Abs(pulse.Height[i])+Math.Abs(pulse.Height[63*64+i])+Math.Abs(pulse.Height[i*64])+Math.Abs(pulse.Height[i*64+63]);
            Check("wave fixed boundaries",edges,0);
            var same=new WaveTankModel(); var opposite=new WaveTankModel(phase:Math.PI);
            double constructive=0,cancellation=0;
            for(int i=0;i<180;i++) { same.Step(); opposite.Step(); constructive=Math.Max(constructive,Math.Abs(same.Height[32*64+32])); cancellation=Math.Max(cancellation,Math.Abs(opposite.Height[32*64+32])); }
            Assert("in-phase sources constructively reach midpoint",constructive>.1);
            Check("opposite-phase sources cancel before wall reflections",cancellation,1e-9);
            foreach(double frequency in new[]{.15,1.5}) foreach(double speed in new[]{4.0,12})
            {
                var m=new WaveTankModel(frequency,Math.PI,4,speed); double max=0;
                Steps(()=>{m.Step();max=Math.Max(max,m.Rms);},3600);
                Check($"wave f={frequency} c={speed} driven RMS bounded",max,3);
            }
        }
        static void Forces()
        {
            foreach(double k in new[]{-1.0,.5,3}) foreach(double impact in new[]{.8,4.0}) foreach(double speed in new[]{.8,Math.Sqrt(10)})
            {
                var m=new CentralForceModel(k,impact,speed); double energy=0, angular=0, initialL=m.AngularMomentum;
                Steps(()=>{m.Step();energy=Math.Max(energy,Math.Abs(m.Energy-m.InitialEnergy));angular=Math.Max(angular,Math.Abs(m.AngularMomentum-initialL));},72000);
                string n=$"central k={k} b={impact} v={speed:0.00}";
                Check(n+" absolute energy drift",energy,5e-4); Check(n+" angular momentum",angular,1e-9);
            }
            var head=new CentralForceModel(1,0,2); Steps(head.Step,60000);
            Assert("Rutherford head-on particle turns around",head.Stopped&&head.Velocity.x<0&&head.Position.x<0);
            foreach(double k in new[]{.5,3.0}) foreach(double speed in new[]{1.0,Math.Sqrt(10)}) foreach(double impact in new[]{0.0,.0625})
            {
                var beam=new CentralForceModel(k,impact,speed); double error=0;
                Steps(()=>{beam.Step();error=Math.Max(error,Math.Abs(beam.Energy-beam.InitialEnergy));},72000);
                Check($"Rutherford near-centre k={k} b={impact} v={speed:0.00} energy drift",error,.002);
                Assert("Rutherford near-centre trajectory remains outside cutoff",beam.Position.Length>.08&&Finite(beam.Energy));
            }
            var low=new CentralForceModel(-1,-1.4,1.2); var high=new CentralForceModel(-1,1.4,1.2); Steps(()=>{low.Step();high.Step();},72000);
            Check("gravity mirrored flybys",(new LabVector(low.Position.x,-low.Position.y)-high.Position).Length,1e-10);
            double original=Math.Sqrt(1.2*1.2+.8*.8);
            Assert("gravity trailing pass gains speed in moving frame",(low.Velocity+new LabVector(0,.8)).Length>original);
            Assert("gravity leading pass loses speed in moving frame",(high.Velocity+new LabVector(0,.8)).Length<original);
            var hit=new CentralForceModel(-1,0,1); Steps(hit.Step,72000);
            Assert("gravity central impact stops outside singularity",hit.Stopped&&hit.Position.Length>=.08&&hit.Position.Length<.1&&Finite(hit.Energy));
            var p=hit.Position; hit.Step(); Check("stopped central particle stays fixed",(hit.Position-p).Length,0);
        }
        static void Lagrange()
        {
            foreach(bool lower in new[]{false,true})
            {
                var equilibrium=new LagrangeModel(offset:0,lower:lower); var p=equilibrium.Position;
                Check("L4/L5 force balance",equilibrium.Acceleration(p,new LabVector()).Length,1e-14);
                Steps(equilibrium.Step,(int)(30/LagrangeModel.StepSize)); Check("L4/L5 stationary in rotating frame",(equilibrium.Position-p).Length,1e-10);
            }
            foreach(double mu in new[]{.001,.01215,.15}) foreach(double offset in new[]{-.15,.03,.15})
            {
                var m=new LagrangeModel(mu,offset); double drift=0;
                Steps(()=>{m.Step();drift=Math.Max(drift,Math.Abs(m.Jacobi-m.InitialJacobi));},(int)(60/LagrangeModel.StepSize));
                Check($"CR3BP mu={mu} offset={offset} Jacobi drift",drift,1e-3);
                Assert($"CR3BP mu={mu} offset={offset} finite probe",Finite(m.Position.Length)&&m.Position.Length<=8);
            }
        }
        static void Charges()
        {
            foreach(double q in new[]{-1.0,1}) foreach(double b in new[]{.3,3.0}) foreach(double e in new[]{0.0,1}) foreach(double pitch in new[]{0.0,75})
            {
                var m=new ChargedParticleModel(b,e,pitch,q); double drift=0;
                for(int i=0;i<=1000;i++) { double t=i*.1; drift=Math.Max(drift,Math.Abs(m.Velocity(t).Squared/2-.5-q*e*m.Position(t).x)); }
                Check($"Lorentz q={q} B={b} E={e} pitch={pitch} work-energy",drift,1e-12);
                double period=2*Math.PI/b; var p=m.Position(period);
                Check("Lorentz full-cycle E cross B drift",Math.Abs(p.x)+Math.Abs(p.y+e/b*period)+Math.Abs(p.z-m.Parallel*period),1e-12);
            }
        }
        static void Resonance()
        {
            foreach(double natural in new[]{1.0,2,3}) foreach(double drive in new[]{.7,2,3.3}) foreach(double damping in new[]{.05,1.0})
            {
                var m=new ResonanceModel(natural,drive,damping); Steps(m.Step,57600);
                Check($"resonance w0={natural} w={drive} gamma={damping} analytic response at t=240",Math.Abs(m.X-m.SteadyX),8e-5);
            }
            Assert("resonance peak exceeds detuned response",new ResonanceModel(2,2,.15).SteadyAmplitude>new ResonanceModel(1,2,.15).SteadyAmplitude);
        }
        static void Flocks()
        {
            var ballistic=new FlockingModel(0,0,0); var p=(LabVector[])ballistic.Positions.Clone(); var v=(LabVector[])ballistic.Velocities.Clone(); Steps(ballistic.Step,600);
            double error=0; for(int i=0;i<96;i++) error=Math.Max(error,(ballistic.Positions[i]-new LabVector(LabMath.Wrap(p[i].x+v[i].x*10,8),LabMath.Wrap(p[i].y+v[i].y*10,5))).Length);
            Check("zero-weight flock is ballistic with periodic wrapping",error,1e-10);
            foreach(double weight in new[]{0.0,1.5,3})
            {
                var a=new FlockingModel(weight,weight,weight); var b=new FlockingModel(weight,weight,weight); double max=0, min=3, bounds=0, replay=0;
                Steps(()=> { a.Step(); b.Step(); for(int i=0;i<96;i++) { max=Math.Max(max,a.Velocities[i].Length); min=Math.Min(min,a.Velocities[i].Length); bounds=Math.Max(bounds,Math.Max(Math.Abs(a.Positions[i].x)/8,Math.Abs(a.Positions[i].y)/5)); replay=Math.Max(replay,(a.Positions[i]-b.Positions[i]).Length); } },1200);
                Check($"flock weight={weight} speed ceiling",max,2.400000001); Check("flock speed floor",.6-min,1e-9); Check("flock world bounds",bounds,1); Check("flock deterministic replay",replay,0); Assert("flock order normalized",a.Order>=0&&a.Order<=1);
            }
        }
        static void Galaxies()
        {
            foreach(double impact in new[]{.2,1.5,4}) foreach(double speed in new[]{.2,.6,1.4})
            {
                var m=new GalaxyEncounterModel(impact,speed); double energy=0,angular=0, initialL=Cross(m.Cores[0],m.CoreV[0])+Cross(m.Cores[1],m.CoreV[1]); bool finite=true;
                Steps(()=> { m.Step(); energy=Math.Max(energy,Math.Abs(m.Energy-m.InitialEnergy)); angular=Math.Max(angular,Math.Abs(Cross(m.Cores[0],m.CoreV[0])+Cross(m.Cores[1],m.CoreV[1])-initialL)); foreach(var p in m.Stars) finite&=Finite(p.Length); },(int)(30/GalaxyEncounterModel.StepSize));
                Check($"galaxy impact={impact} speed={speed} core absolute energy drift",energy,.035);
                Check("galaxy core angular momentum",angular,1e-10); Check("galaxy core centre of mass",(m.Cores[0]+m.Cores[1]).Length,0); Check("galaxy core momentum",(m.CoreV[0]+m.CoreV[1]).Length,0); Assert("galaxy all 192 tracers remain finite",finite);
            }
            var a=new GalaxyEncounterModel(); var b=new GalaxyEncounterModel(retrograde:true);
            Check("retrograde reverses second disk relative velocities",(a.StarV[100]+b.StarV[100]-a.CoreV[1]*2).Length,1e-12);
            Check("retrograde preserves first disk",(a.StarV[0]-b.StarV[0]).Length,0);
            double parabolicSpeed=Math.Sqrt(GalaxyEncounterModel.Mass/Math.Sqrt(64+1.5*1.5+GalaxyEncounterModel.Softening*GalaxyEncounterModel.Softening));
            var nearZero=new GalaxyEncounterModel(1.5,parabolicSpeed);
            Check("galaxy parabolic initial total energy",Math.Abs(nearZero.InitialEnergy),1e-12);
            Assert("galaxy error scale stays positive at zero total energy",nearZero.EnergyScale>1&&Finite(nearZero.EnergyScale));
        }
    }
}
