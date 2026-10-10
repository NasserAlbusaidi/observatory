using System;
using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class ChargedParticleScene : LabScene
    {
        public override string Title=>"Charged Particle";
        public override string Eyebrow=>"Turn, drift, spiral";
        public override string Short=>"Ride electric and magnetic fields";
        public override string Blurb=>"A uniform magnetic field bends the particle into a helix. Add a sideways electric field and its orbit drifts. Switch the charge sign: the gyration reverses, while the electric-cross-magnetic drift keeps its direction. The camera follows the particle.";
        public override string Note=>"Exact nonrelativistic Lorentz-force solution with unit mass and charge magnitude. E points along x; B along z. No radiation, collisions, relativistic corrections or field backreaction. Axes and field strengths use normalized units. The magnetic field alone does no work.";
        protected override double StepSeconds=>1.0/120;
        protected override Vector3 CameraPosition=>new Vector3(7,5,-16);
        ChargedParticleModel model;
        readonly LabTrail trail=new LabTrail();
        float magnetic=1,electric=0.3f,pitch=25;
        int sign;
        double time;
        protected override void InitializeModel() { model=new ChargedParticleModel(Value(magnetic),Value(electric),Value(pitch),sign==0?1:-1); time=0; trail.Clear(); }
        protected override void StepModel()=>time+=StepSeconds;
        protected override void Sample()=>trail.Add(World(model.Position(time)));
        protected override void DrawModel()
        {
            var p=World(model.Position(time)); trail.Append(lineVertices,lineColors,Amber); Dot(p,0.12f,Amber);
            for(int x=-2;x<=2;x++) for(int y=-2;y<=2;y++)
            { var a=p+new Vector3(x*1.3f,y*1.3f,-3); Line(a,a+new Vector3(0,0,6),new Color(0.25f,0.6f,1,0.18f)); }
            Line(p,p+World(model.Velocity(time)),Cyan);
            if(App.Rig==null) Orbit.target=p;
        }
        protected override void ModelControls(PanelUI ui)
        {
            int s=ui.Choice("Charge",sign,new[]{"Positive","Negative"});
            bool change=Slider(ui,"Magnetic field B",ref magnetic,0.3f,3,0.1f)|Slider(ui,"Electric field E",ref electric,0,1,0.05f)|Slider(ui,"Launch pitch (degrees)",ref pitch,0,75,5);
            if(s!=sign||change) { sign=s; Reset(); }
        }
        protected override void ModelReadouts(List<Readout> list)
        {
            double balance=model.Velocity(time).Squared/2-0.5-model.Charge*model.E*model.Position(time).x;
            list.Add(new Readout("Cyclotron period",(2*Math.PI/model.B).ToString("0.000")));
            list.Add(new Readout("Drift along −y",(model.E/model.B).ToString("0.000"),Tone.Cold));
            list.Add(new Readout("Work-energy residual",Math.Abs(balance).ToString("0.00E+0")));
        }
    }
}
