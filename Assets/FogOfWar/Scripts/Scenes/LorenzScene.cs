using System;
using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class LorenzScene : LabScene
    {
        public override string Title=>"Lorenz Attractor";
        public override string Eyebrow=>"The butterfly in phase space";
        public override string Short=>"Two almost identical futures";
        public override string Blurb=>"Two trajectories start just a little apart. Follow them around the butterfly-shaped attractor, then turn the convection parameter down and watch the motion settle. Drag to see the full three-dimensional shape.";
        public override string Note=>"Lorenz's three-variable convection model: sigma=10, beta=8/3, adjustable rho. Axes are state variables, not positions in a weather map. Twin separation is not a forecast horizon or a measured Lyapunov exponent. Trails retain 16 model-time units.";
        protected override double StepSeconds=>LorenzModel.StepSize;
        protected override Vector3 CameraPosition=>new Vector3(7,4,-18);
        LorenzModel a,b;
        readonly LabTrail ta=new LabTrail(),tb=new LabTrail();
        float rho=28, offsetPower=-4;
        int preset;
        Vector3 View(LabVector p)=>new Vector3((float)p.x*0.16f,((float)p.z-25)*0.16f,(float)p.y*0.16f);
        protected override void InitializeModel() { a=new LorenzModel(Value(rho)); b=new LorenzModel(Value(rho),Math.Pow(10,offsetPower)); ta.Clear(); tb.Clear(); }
        protected override void StepModel() { a.Step(); b.Step(); }
        protected override void Sample() { ta.Add(View(a.Position)); tb.Add(View(b.Position)); }
        protected override void DrawModel()
        { ta.Append(lineVertices,lineColors,Amber); tb.Append(lineVertices,lineColors,Cyan); Dot(View(a.Position),0.09f,Amber); Dot(View(b.Position),0.07f,Cyan); }
        protected override void ModelControls(PanelUI ui)
        {
            int p=ui.Choice("Convection regime",preset,new[]{"Butterfly (rho 28)","Settling (rho 10)","Wide wings (rho 40)"});
            if(p!=preset) { preset=p; rho=p==0?28:p==1?10:40; Reset(); }
            if(Slider(ui,"Convection rho (restarts)",ref rho,1,45,1)|Slider(ui,"Initial offset: log10",ref offsetPower,-6,-1,1)) Reset();
        }
        protected override void ModelReadouts(List<Readout> list)
        { list.Add(new Readout("Twin separation",(a.Position-b.Position).Length.ToString("0.000000"),Tone.Hot)); list.Add(new Readout("rho / sigma / beta",rho.ToString("0")+" / 10 / 2.667")); }
    }
}
