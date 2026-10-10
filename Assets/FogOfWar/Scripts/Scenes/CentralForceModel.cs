using System;
namespace FogOfWar
{
    // k<0 attracts (gravity assist), k>0 repels (Rutherford). Unit test-particle mass, U=k/r.
    public sealed class CentralForceModel
    {
        public const double StepSize=1.0/1200;
        public readonly double K;
        public LabVector Position { get; private set; }
        public LabVector Velocity { get; private set; }
        public bool Stopped { get; private set; }
        public double InitialEnergy { get; private set; }
        public CentralForceModel(double k, double impact, double speed)
        {
            K=LabMath.Range(k,-3,3,nameof(k)); LabMath.Range(impact,-6,6,nameof(impact)); LabMath.Range(speed,0.5,4,nameof(speed));
            Position=new LabVector(-12,impact); Velocity=new LabVector(speed,0); InitialEnergy=Energy;
        }
        LabVector Acceleration(LabVector p) => p*(K/Math.Pow(Math.Max(p.Length,0.04),3));
        public void Step()
        {
            if(Stopped) return;
            var v=Velocity+Acceleration(Position)*(StepSize/2); var p=Position+v*StepSize;
            // Stop before unresolved impact/singularity, or after the particle has left the laboratory.
            if(p.Length<0.08 || p.Length>40) { Stopped=true; return; }
            Velocity=v+Acceleration(p)*(StepSize/2); Position=p;
        }
        public double Energy => Velocity.Squared/2+K/Position.Length;
        public double AngularMomentum => Position.x*Velocity.y-Position.y*Velocity.x;
    }
}
