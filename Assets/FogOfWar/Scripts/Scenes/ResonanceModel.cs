using System;
namespace FogOfWar
{
    public sealed class ResonanceModel
    {
        public const double StepSize=1.0/240;
        public readonly double Natural, Drive, Damping;
        public double X { get; private set; }
        public double V { get; private set; }
        public double Time { get; private set; }
        public ResonanceModel(double natural, double drive, double damping)
        {
            Natural=LabMath.Range(natural,1,3,nameof(natural)); Drive=LabMath.Range(drive,0.7,3.3,nameof(drive)); Damping=LabMath.Range(damping,0.05,1,nameof(damping));
        }
        double A(double x,double v,double t)=>0.8*Math.Cos(Drive*t)-2*Damping*v-Natural*Natural*x;
        public void Step()
        {
            double h=StepSize, a=A(X,V,Time), vb=V+a*h/2, b=A(X+V*h/2,vb,Time+h/2);
            double vc=V+b*h/2, c=A(X+vb*h/2,vc,Time+h/2), vd=V+c*h, d=A(X+vc*h,vd,Time+h);
            X+=h*(V+2*vb+2*vc+vd)/6; V+=h*(a+2*b+2*c+d)/6; Time+=h;
        }
        public double SteadyAmplitude=>0.8/Math.Sqrt(Math.Pow(Natural*Natural-Drive*Drive,2)+4*Damping*Damping*Drive*Drive);
        public double SteadyX=>SteadyAmplitude*Math.Cos(Drive*Time-Math.Atan2(2*Damping*Drive,Natural*Natural-Drive*Drive));
    }
}
