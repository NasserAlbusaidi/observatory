using System;
using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class GravityAssistScene : LabScene
    {
        public override string Title=>"Gravity Assist";
        public override string Eyebrow=>"Borrow a little planetary motion";
        public override string Short=>"Same flyby, two energy stories";
        public override string Blurb=>"The planet sits still in this view. The amber probe bends around it; the cyan arrow is its velocity after adding the planet's motion. Flip the side of the encounter to compare a speed gain with a speed loss in that second frame.";
        public override string Note=>"Newtonian test particle around one point mass, GM=1. The second inertial frame adds a constant planet velocity along +y; there is no Sun force or full mission trajectory. Units are arbitrary. Integration stops below r=0.08 or beyond r=40. Arrows show velocity, not force.";
        protected override double StepSeconds=>CentralForceModel.StepSize;
        CentralForceModel model;
        readonly LabTrail trail=new LabTrail();
        float impact=1.4f,incoming=1.2f,planetSpeed=0.8f;
        int side;
        double startSpeed;
        protected override void InitializeModel()
        { model=new CentralForceModel(-1,(side==0?-1:1)*Value(impact),Value(incoming)); startSpeed=(model.Velocity+new LabVector(0,planetSpeed)).Length; trail.Clear(); }
        protected override void StepModel()=>model.Step();
        protected override void Sample() { if(!model.Stopped) trail.Add(World(model.Position,0.35f)); }
        protected override void DrawModel()
        {
            Grid(); Ring(Vector3.zero,0.35f,new Color(0.3f,0.7f,1,0.35f)); Dot(Vector3.zero,0.055f,Cyan);
            trail.Append(lineVertices,lineColors,Amber); var p=World(model.Position,0.35f); Dot(p,0.08f,Amber);
            Line(p,p+World(model.Velocity,0.5f),Amber); Line(p,p+World(model.Velocity+new LabVector(0,planetSpeed),0.5f),Cyan);
        }
        protected override void ModelControls(PanelUI ui)
        {
            int s=ui.Choice("Pass side",side,new[]{"Behind: gain speed","Ahead: lose speed"});
            bool changed=Slider(ui,"Miss distance (restarts)",ref impact,0.8f,4,0.1f)|Slider(ui,"Incoming relative speed",ref incoming,0.8f,2.5f,0.1f)|Slider(ui,"Planet frame speed",ref planetSpeed,0,1.5f,0.1f);
            if(s!=side||changed) { side=s; Reset(); }
        }
        protected override void ModelReadouts(List<Readout> list)
        {
            double other=(model.Velocity+new LabVector(0,planetSpeed)).Length;
            list.Add(new Readout("Second-frame speed change",(other-startSpeed).ToString("+0.000;-0.000;0.000"),Tone.Hot));
            list.Add(new Readout("Planet-frame energy drift",Math.Abs(model.Energy-model.InitialEnergy).ToString("0.00E+0"),Tone.Cold));
            list.Add(new Readout("Probe radius",model.Position.Length.ToString("0.00"))); list.Add(new Readout("Encounter",model.Stopped?"Stopped at lab boundary":"In flight"));
        }
    }
}
