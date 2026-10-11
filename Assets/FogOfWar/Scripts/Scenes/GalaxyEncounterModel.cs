using System;
namespace FogOfWar
{
    // Two equal gravitating cores plus massless disk tracers, in a plane. No tracer self-gravity or backreaction.
    public sealed class GalaxyEncounterModel
    {
        public const int Count=192;
        public const double StepSize=1.0/960, Mass=8, Softening=0.4;
        public readonly LabVector[] Cores=new LabVector[2], CoreV=new LabVector[2], Stars=new LabVector[Count], StarV=new LabVector[Count];
        readonly LabVector[] coreA=new LabVector[2], starA=new LabVector[Count];
        public double InitialEnergy { get; private set; }
        public readonly double EnergyScale;
        public GalaxyEncounterModel(double impact=1.5,double speed=0.6,bool retrograde=false)
        {
            LabMath.Range(impact,0.2,4,nameof(impact)); LabMath.Range(speed,0.2,1.4,nameof(speed));
            Cores[0]=new LabVector(-4,-impact/2); Cores[1]=new LabVector(4,impact/2); CoreV[0]=new LabVector(speed,0); CoreV[1]=new LabVector(-speed,0);
            var random=new Random(41);
            for(int i=0;i<Count;i++)
            {
                int g=i/(Count/2); double r=0.35+1.3*Math.Sqrt(random.NextDouble()), a=i*2.399963229728653;
                var d=new LabVector(Math.Cos(a),Math.Sin(a)); Stars[i]=Cores[g]+d*r;
                double v=Math.Sqrt(Mass*r*r/Math.Pow(r*r+Softening*Softening,1.5))*(g==1&&retrograde?-1:1);
                StarV[i]=CoreV[g]+new LabVector(-d.y,d.x)*v;
            }
            Acceleration(); InitialEnergy=Energy;
            EnergyScale=Mass*(CoreV[0].Squared+CoreV[1].Squared)/2+Mass*Mass/Math.Sqrt((Cores[1]-Cores[0]).Squared+Softening*Softening);
        }
        static LabVector Pull(LabVector delta)=>delta*(Mass/Math.Pow(delta.Squared+Softening*Softening,1.5));
        void Acceleration()
        {
            coreA[0]=Pull(Cores[1]-Cores[0]); coreA[1]=coreA[0]*-1;
            for(int i=0;i<Count;i++) starA[i]=Pull(Cores[0]-Stars[i])+Pull(Cores[1]-Stars[i]);
        }
        public void Step()
        {
            for(int i=0;i<2;i++) { CoreV[i]+=coreA[i]*(StepSize/2); Cores[i]+=CoreV[i]*StepSize; }
            for(int i=0;i<Count;i++) { StarV[i]+=starA[i]*(StepSize/2); Stars[i]+=StarV[i]*StepSize; }
            Acceleration();
            for(int i=0;i<2;i++) CoreV[i]+=coreA[i]*(StepSize/2);
            for(int i=0;i<Count;i++) StarV[i]+=starA[i]*(StepSize/2);
        }
        public double Energy=>Mass*(CoreV[0].Squared+CoreV[1].Squared)/2-Mass*Mass/Math.Sqrt((Cores[1]-Cores[0]).Squared+Softening*Softening);
    }
}
