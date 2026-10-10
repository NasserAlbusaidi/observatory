using System;
using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class LagrangeScene : LabScene
    {
        public override string Title=>"Lagrange-Point Lab";
        public override string Eyebrow=>"The moving frame stands still";
        public override string Short=>"Park a probe near L4 or L5";
        public override string Blurb=>"The two primaries are stationary because the view rotates with their circular orbit. A test probe starts near a triangular equilibrium. Nudge its position, then increase the secondary's mass and see when the neighbourhood stops being stable.";
        public override string Note=>"Planar circular restricted three-body problem: unit separation, angular rate and total mass. The probe is massless. Integration stops within 0.03 of a primary or beyond radius 8; no collisions or station keeping. The Jacobi constant belongs to the rotating frame.";
        protected override double StepSeconds=>LagrangeModel.StepSize;
        LagrangeModel model;
        readonly LabTrail trail=new LabTrail();
        float mu=0.01215f,offset=0.03f;
        int point;
        protected override void InitializeModel() { model=new LagrangeModel(Value(mu),Value(offset),point==1); trail.Clear(); }
        protected override void StepModel()=>model.Step();
        protected override void Sample() { if(!model.Stopped) trail.Add(World(model.Position,4)); }
        protected override void DrawModel()
        {
            Grid(); Dot(new Vector3(-mu*4,0,0),0.23f,Amber); Dot(new Vector3((1-mu)*4,0,0),0.13f,Cyan);
            for(int i=0;i<2;i++) Ring(new Vector3((0.5f-mu)*4,(i==0?1:-1)*(float)Math.Sqrt(3)*2,0),0.15f,new Color(0.4f,1,0.65f,0.6f));
            trail.Append(lineVertices,lineColors,Coral); Dot(World(model.Position,4),0.07f,Coral);
        }
        protected override void ModelControls(PanelUI ui)
        {
            int p=ui.Choice("Launch point",point,new[]{"L4: leading","L5: trailing"});
            bool change=Slider(ui,"Secondary mass fraction",ref mu,0.001f,0.15f)|Slider(ui,"Position nudge (restarts)",ref offset,-0.15f,0.15f,0.01f);
            if(p!=point||change) { point=p; Reset(); }
        }
        protected override void ModelReadouts(List<Readout> list)
        {
            list.Add(new Readout("Jacobi constant",model.Jacobi.ToString("0.000000"),Tone.Cold));
            list.Add(new Readout("Jacobi drift",Math.Abs(model.Jacobi-model.InitialJacobi).ToString("0.00E+0")));
            list.Add(new Readout("Linear stability",27*mu*(1-mu)<1?"Stable triangular points":"Unstable triangular points",27*mu*(1-mu)<1?Tone.Cold:Tone.Hot));
            list.Add(new Readout("Probe",model.Stopped?"Stopped at lab boundary":"Free trajectory"));
        }
    }
}
