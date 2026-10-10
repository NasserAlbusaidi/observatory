using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Shared playback and batched geometry for the ten small exhibits. Models remain independent of Unity.
    public abstract class LabScene : SimScene
    {
        protected static readonly Color Amber=new Color(1,0.71f,0.28f), Cyan=new Color(0.3f,0.82f,1), Coral=new Color(1,0.38f,0.4f);
        protected readonly List<Vector3> lineVertices=new List<Vector3>(8000);
        protected readonly List<Color> lineColors=new List<Color>(8000);
        readonly List<Vector3> dotVertices=new List<Vector3>(2400);
        readonly List<Color> dotColors=new List<Color>(2400);
        readonly List<int> dotIndices=new List<int>(10000);
        Mesh lines, dots;
        Texture2D field;
        Color[] pixels;
        double accumulator, sampleClock;
        float speed=1;
        bool paused;
        public double Elapsed { get; private set; }
        public bool Paused => paused;
        protected abstract double StepSeconds { get; }
        protected virtual double ClockRate=>1;
        protected virtual int FieldSize=>0;
        protected virtual Vector3 CameraPosition=>new Vector3(0,0,-22);
        protected abstract void InitializeModel();
        protected abstract void StepModel();
        protected abstract void DrawModel();
        protected abstract void ModelControls(PanelUI ui);
        protected abstract void ModelReadouts(List<Readout> list);
        protected virtual void Sample() { }
        protected static double Value(float value)=>Math.Round(value,6);
        protected static Vector3 World(LabVector v, float scale=1)=>new Vector3((float)v.x*scale,(float)v.y*scale,(float)v.z*scale);

        protected override void Build()
        {
            lines=Own(Gfx.LineMesh("Lab lines")); dots=Own(Gfx.LineMesh("Lab markers"));
            var material=OwnMat(Gfx.Unlit(Color.white,true));
            Gfx.Obj("Lab lines",Root,lines,material); Gfx.Obj("Lab markers",Root,dots,material);
            if(FieldSize>0)
            {
                field=Own(new Texture2D(FieldSize,FieldSize,TextureFormat.RGBA32,false));
                field.filterMode=FilterMode.Bilinear; field.wrapMode=TextureWrapMode.Clamp; pixels=new Color[FieldSize*FieldSize];
                var plane=Own(new Mesh { name="Lab field" });
                plane.vertices=new[]{new Vector3(-6,-6,0.1f),new Vector3(6,-6,0.1f),new Vector3(6,6,0.1f),new Vector3(-6,6,0.1f)};
                plane.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
                plane.colors=new[]{Color.white,Color.white,Color.white,Color.white}; plane.triangles=new[]{0,1,2,0,2,3};
                var mat=OwnMat(Gfx.Unlit(Color.white)); mat.SetTexture("_MainTex",field); Gfx.Obj("Lab field",Root,plane,mat);
            }
            Gfx.Starfield(Root,2000); ResetView(); Reset();
        }
        void ResetView()=>Orbit.Set(CameraPosition,Vector3.zero,3,100);
        public void Reset()
        {
            accumulator=sampleClock=Elapsed=0; paused=false;
            InitializeModel(); Sample(); Draw();
        }
        void Advance()
        {
            StepModel(); Elapsed+=StepSeconds; sampleClock+=StepSeconds;
            if(sampleClock+1e-12>=1.0/60) { Sample(); sampleClock-=Math.Floor((sampleClock+1e-12)*60)/60; }
        }
        public sealed override void Tick(float dt)
        {
            if(!Paused)
            {
                accumulator+=Math.Max(0,Math.Min(dt,0.05f))*speed*ClockRate;
                while(accumulator+1e-12>=StepSeconds) { Advance(); accumulator-=StepSeconds; }
            }
            Draw();
        }
        void Draw()
        {
            lineVertices.Clear(); lineColors.Clear(); dotVertices.Clear(); dotColors.Clear(); dotIndices.Clear();
            DrawModel(); Gfx.SetLines(lines,lineVertices,lineColors);
            dots.Clear(); dots.SetVertices(dotVertices); dots.SetColors(dotColors); dots.SetTriangles(dotIndices,0);
        }
        protected void Line(Vector3 a,Vector3 b,Color c) { lineVertices.Add(a); lineVertices.Add(b); lineColors.Add(c); lineColors.Add(c); }
        protected void Dot(Vector3 p,float r,Color color)
        {
            int n=dotVertices.Count;
            dotVertices.Add(p+new Vector3(r,0,0)); dotVertices.Add(p+new Vector3(-r,0,0));
            dotVertices.Add(p+new Vector3(0,r,0)); dotVertices.Add(p+new Vector3(0,-r,0));
            dotVertices.Add(p+new Vector3(0,0,r)); dotVertices.Add(p+new Vector3(0,0,-r));
            for(int i=0;i<6;i++) dotColors.Add(color);
            for(int pole=4;pole<=5;pole++)
            { Triangle(n+pole,n,n+2); Triangle(n+pole,n+2,n+1); Triangle(n+pole,n+1,n+3); Triangle(n+pole,n+3,n); }
        }
        void Triangle(int a,int b,int c) { dotIndices.Add(a); dotIndices.Add(b); dotIndices.Add(c); }
        protected void Ring(Vector3 centre,float radius,Color color)
        {
            for(int i=0;i<80;i++)
            { double a=i*2*Math.PI/80,b=(i+1)*2*Math.PI/80; Line(centre+new Vector3((float)Math.Cos(a)*radius,(float)Math.Sin(a)*radius,0),centre+new Vector3((float)Math.Cos(b)*radius,(float)Math.Sin(b)*radius,0),color); }
        }
        protected void Grid(float half=6)
        {
            var c=new Color(0.3f,0.4f,0.6f,0.18f);
            for(int i=-(int)half;i<=half;i++) { Line(new Vector3(i,-half,0.2f),new Vector3(i,half,0.2f),c); Line(new Vector3(-half,i,0.2f),new Vector3(half,i,0.2f),c); }
        }
        protected void Paint(double[] values,double gain,bool signed=false)
        {
            for(int i=0;i<pixels.Length;i++)
            {
                float v=(float)Math.Tanh(values[i]*gain);
                var dark=new Color(0.015f,0.035f,0.08f);
                pixels[i]=signed ? Color.Lerp(dark,v<0?Cyan:Amber,Math.Abs(v)) : Color.Lerp(dark,Color.Lerp(Cyan,Amber,Mathf.Clamp01(v*1.4f-0.3f)),Mathf.Clamp01(v));
            }
            field.SetPixels(pixels); field.Apply(false);
        }
        protected bool Slider(PanelUI ui,string label,ref float value,float min,float max,float step=0)
        { float next=ui.Slider(label,value,min,max,v=>v.ToString("0.####"),step); bool changed=Math.Abs(next-value)>1e-6*Math.Max(1,Math.Abs(value)); value=next; return changed; }
        public sealed override void Controls(PanelUI ui)
        {
            int action=ui.Buttons(Paused?"Resume":"Pause","Replay","Reset view");
            if(action==0) paused=!paused; if(action==1) Reset(); if(action==2) ResetView();
            if(Paused&&ui.Buttons("One model step")==0) { Advance(); Draw(); }
            speed=ui.Slider("Playback speed",speed,0.25f,4,v=>v.ToString("0.00")+"×",0.25f);
            ModelControls(ui);
        }
        public sealed override void Readouts(List<Readout> list)
        { list.Add(new Readout("Playback",Paused?"PAUSED":"RUNNING",Paused?Tone.Cold:Tone.Hot)); list.Add(new Readout("Model clock",Elapsed.ToString("0.00"))); ModelReadouts(list); }
        protected override void Teardown()
        { lineVertices.Clear(); lineColors.Clear(); dotVertices.Clear(); dotColors.Clear(); dotIndices.Clear(); pixels=null; field=null; }
    }
}
