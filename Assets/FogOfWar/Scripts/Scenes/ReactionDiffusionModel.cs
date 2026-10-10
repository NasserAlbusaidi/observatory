using System;
namespace FogOfWar
{
    public sealed class ReactionDiffusionModel
    {
        public const int Size=64;
        public double[] U { get; private set; } = new double[Size*Size];
        public double[] V { get; private set; } = new double[Size*Size];
        double[] nextU=new double[Size*Size], nextV=new double[Size*Size];
        public readonly double Feed, Kill;
        public ReactionDiffusionModel(double feed=0.035, double kill=0.062, bool seed=true)
        {
            Feed=LabMath.Range(feed,0.01,0.08,nameof(feed)); Kill=LabMath.Range(kill,0.04,0.075,nameof(kill));
            for(int i=0;i<U.Length;i++) U[i]=1;
            if(seed) Seed();
        }
        public void Seed()
        {
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
            {
                int dx=x-Size/2, dy=y-Size/2;
                if(dx*dx+dy*dy<36 || (x-20)*(x-20)+(y-22)*(y-22)<9)
                { int i=y*Size+x; U[i]=0.5; V[i]=0.25+0.015*Math.Sin(x*7+y*13); }
            }
        }
        public void Step()
        {
            // Unit lattice, periodic boundary, dt=1; D_u=.16 and D_v=.08 satisfy the explicit diffusion bound.
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
            {
                int i=y*Size+x, l=y*Size+(x+Size-1)%Size, r=y*Size+(x+1)%Size;
                int a=((y+Size-1)%Size)*Size+x, b=((y+1)%Size)*Size+x;
                double uvv=U[i]*V[i]*V[i];
                nextU[i]=U[i]+0.16*(U[l]+U[r]+U[a]+U[b]-4*U[i])-uvv+Feed*(1-U[i]);
                nextV[i]=V[i]+0.08*(V[l]+V[r]+V[a]+V[b]-4*V[i])+uvv-(Feed+Kill)*V[i];
            }
            var tmp=U; U=nextU; nextU=tmp; tmp=V; V=nextV; nextV=tmp;
        }
        public double MeanV { get { double s=0; foreach(double v in V) s+=v; return s/V.Length; } }
    }
}
