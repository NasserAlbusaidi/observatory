using System;
namespace FogOfWar
{
    public sealed class FlockingModel
    {
        public const int Count=96;
        public const double StepSize=1.0/60;
        public LabVector[] Positions { get; private set; }=new LabVector[Count];
        public LabVector[] Velocities { get; private set; }=new LabVector[Count];
        LabVector[] nextP=new LabVector[Count], nextV=new LabVector[Count];
        public readonly double Separation, Alignment, Cohesion;
        public FlockingModel(double separation=1.5,double alignment=1,double cohesion=0.7)
        {
            Separation=LabMath.Range(separation,0,3,nameof(separation)); Alignment=LabMath.Range(alignment,0,3,nameof(alignment)); Cohesion=LabMath.Range(cohesion,0,3,nameof(cohesion));
            var random=new Random(137);
            for(int i=0;i<Count;i++) { Positions[i]=new LabVector(random.NextDouble()*16-8,random.NextDouble()*10-5); double a=random.NextDouble()*2*Math.PI; Velocities[i]=new LabVector(Math.Cos(a),Math.Sin(a))*1.8; }
        }
        public void Step()
        {
            for(int i=0;i<Count;i++)
            {
                var sep=new LabVector(); var align=new LabVector(); var centre=new LabVector(); int n=0;
                for(int j=0;j<Count;j++) if(i!=j)
                {
                    var d=Positions[j]-Positions[i]; d.x=LabMath.Wrap(d.x,8); d.y=LabMath.Wrap(d.y,5); double r2=d.Squared;
                    if(r2<6.25) { n++; align+=Velocities[j]; centre+=d; if(r2<0.64) sep-=d/Math.Max(r2,0.01); }
                }
                var a=sep*Separation;
                if(n>0) a+=(align/n-Velocities[i])*Alignment+centre/n*Cohesion;
                if(a.Length>3) a=a.Unit*3;
                var v=Velocities[i]+a*StepSize;
                if(v.Length>2.4) v=v.Unit*2.4; else if(v.Length<0.6) v=(v.Length>1e-10?v.Unit:Velocities[i].Unit)*0.6;
                var p=Positions[i]+v*StepSize; p.x=LabMath.Wrap(p.x,8); p.y=LabMath.Wrap(p.y,5); nextP[i]=p; nextV[i]=v;
            }
            var tmp=Positions; Positions=nextP; nextP=tmp; tmp=Velocities; Velocities=nextV; nextV=tmp;
        }
        public double Order { get { var v=new LabVector(); double total=0; foreach(var x in Velocities) { v+=x; total+=x.Length; } return v.Length/total; } }
    }
}
