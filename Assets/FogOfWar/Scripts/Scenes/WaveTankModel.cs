using System;
namespace FogOfWar
{
    public sealed class WaveTankModel
    {
        public const int Size=64;
        public const double StepSize=1.0/120;
        public readonly double[] Height=new double[Size*Size], Velocity=new double[Size*Size];
        public readonly double Frequency, Phase, Speed;
        public readonly int Separation;
        readonly bool driven;
        public double Time { get; private set; }
        public WaveTankModel(double frequency=0.5, double phase=0, int separation=16, double speed=10, bool driven=true)
        {
            Frequency=LabMath.Range(frequency,0.15,1.5,nameof(frequency)); Phase=LabMath.Range(phase,0,Math.PI,nameof(phase));
            if(separation<4||separation>32) throw new ArgumentOutOfRangeException(nameof(separation)); Separation=separation;
            Speed=LabMath.Range(speed,4,12,nameof(speed)); this.driven=driven;
        }
        public void Pulse()
        {
            for(int y=1;y<Size-1;y++) for(int x=1;x<Size-1;x++)
                Height[y*Size+x]+=Math.Exp(-((x-32)*(x-32)+(y-40)*(y-40))/12.0);
        }
        public void Step()
        {
            for(int y=1;y<Size-1;y++) for(int x=1;x<Size-1;x++)
            {
                int i=y*Size+x;
                double lap=Height[i-1]+Height[i+1]+Height[i-Size]+Height[i+Size]-4*Height[i];
                Velocity[i]=(Velocity[i]+Speed*Speed*lap*StepSize)/(1+0.12*StepSize);
            }
            for(int i=0;i<Height.Length;i++) Height[i]+=Velocity[i]*StepSize;
            Time+=StepSize;
            if(driven)
            {
                for(int s=0;s<2;s++)
                {
                    int i=32*Size+32+(s==0?-Separation/2:Separation/2);
                    double angle=2*Math.PI*Frequency*Time+s*Phase;
                    Height[i]=Math.Sin(angle); Velocity[i]=2*Math.PI*Frequency*Math.Cos(angle);
                }
            }
        }
        public double Rms { get { double s=0; foreach(double v in Height) s+=v*v; return Math.Sqrt(s/Height.Length); } }
    }
}
