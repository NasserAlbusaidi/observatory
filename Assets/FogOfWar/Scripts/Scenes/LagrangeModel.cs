using System;
namespace FogOfWar
{
    public sealed class LagrangeModel
    {
        public const double StepSize=1.0/2400;
        public readonly double Mu;
        public LabVector Position { get; private set; }
        public LabVector Velocity { get; private set; }
        public bool Stopped { get; private set; }
        public double InitialJacobi { get; private set; }
        public LagrangeModel(double mu=0.01215, double offset=0.03, bool lower=false)
        {
            Mu=LabMath.Range(mu,0.001,0.15,nameof(mu)); LabMath.Range(offset,-0.15,0.15,nameof(offset));
            Position=new LabVector(0.5-mu+offset,(lower?-1:1)*Math.Sqrt(3)/2); InitialJacobi=Jacobi;
        }
        public LabVector Acceleration(LabVector p, LabVector v)
        {
            var a=p-new LabVector(-Mu,0); var b=p-new LabVector(1-Mu,0);
            return new LabVector(p.x+2*v.y,p.y-2*v.x)-a*((1-Mu)/Math.Pow(Math.Max(a.Length,0.01),3))-b*(Mu/Math.Pow(Math.Max(b.Length,0.01),3));
        }
        public void Step()
        {
            if(Stopped) return;
            double h=StepSize; var p=Position; var v=Velocity;
            var a=Acceleration(p,v); var vb=v+a*(h/2); var b=Acceleration(p+v*(h/2),vb);
            var vc=v+b*(h/2); var c=Acceleration(p+vb*(h/2),vc);
            var vd=v+c*h; var d=Acceleration(p+vc*h,vd);
            var next=p+(v+vb*2+vc*2+vd)*(h/6);
            if((next-new LabVector(-Mu,0)).Length<0.03 || (next-new LabVector(1-Mu,0)).Length<0.03 || next.Length>8) { Stopped=true; return; }
            Position=next; Velocity=v+(a+b*2+c*2+d)*(h/6);
        }
        public double Jacobi => Position.Squared+2*((1-Mu)/(Position-new LabVector(-Mu,0)).Length+Mu/(Position-new LabVector(1-Mu,0)).Length)-Velocity.Squared;
    }
}
