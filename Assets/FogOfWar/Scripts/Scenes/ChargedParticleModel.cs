using System;
namespace FogOfWar
{
    // Exact nonrelativistic solution, m=1, q=+/-1, E along x, B along z.
    public sealed class ChargedParticleModel
    {
        public readonly double B, E, Charge, Parallel, Perpendicular;
        public ChargedParticleModel(double magnetic=1, double electric=0.3, double pitch=25, double charge=1)
        {
            B=LabMath.Range(magnetic,0.3,3,nameof(magnetic)); E=LabMath.Range(electric,0,1,nameof(electric));
            LabMath.Range(pitch,0,75,nameof(pitch)); if(charge!=1&&charge!=-1) throw new ArgumentOutOfRangeException(nameof(charge)); Charge=charge;
            Parallel=Math.Sin(pitch*Math.PI/180); Perpendicular=Math.Cos(pitch*Math.PI/180);
        }
        public LabVector Position(double t)
        {
            double w=Charge*B, s=Math.Sin(w*t), c=Math.Cos(w*t), u=E/B;
            return new LabVector(Perpendicular*s/w+u*(1-c)/w, -u*t+Perpendicular*(c-1)/w+u*s/w, Parallel*t);
        }
        public LabVector Velocity(double t)
        { double w=Charge*B; return new LabVector(Perpendicular*Math.Cos(w*t)+E/B*Math.Sin(w*t), -E/B-Perpendicular*Math.Sin(w*t)+E/B*Math.Cos(w*t), Parallel); }
    }
}
