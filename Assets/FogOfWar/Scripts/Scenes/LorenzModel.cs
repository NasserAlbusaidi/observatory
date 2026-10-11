using System;
namespace FogOfWar
{
    public sealed class LorenzModel
    {
        public const double StepSize = 1.0/240;
        public readonly double Rho;
        public LabVector Position { get; private set; }
        readonly Func<LabVector,LabVector> derivative;
        public LorenzModel(double rho=28, double offset=0)
        {
            Rho=LabMath.Range(rho,0,50,nameof(rho)); LabMath.Range(offset,0,1,nameof(offset));
            Position=new LabVector(1+offset,1,1); derivative=Derivative;
        }
        public LabVector Derivative(LabVector p) => new LabVector(10*(p.y-p.x), p.x*(Rho-p.z)-p.y, p.x*p.y-8*p.z/3);
        public void Step(double h=StepSize) { LabMath.Range(h,1e-9,StepSize,nameof(h)); Position=LabMath.Rk4(Position,h,derivative); }
    }
}
