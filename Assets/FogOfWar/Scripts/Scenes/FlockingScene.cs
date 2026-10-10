using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class FlockingScene : LabScene
    {
        public override string Title=>"Flocking Lab";
        public override string Eyebrow=>"Coordination without a leader";
        public override string Short=>"Three local rules, one moving flock";
        public override string Blurb=>"Each of the 96 agents avoids close neighbours, aligns its heading and steers toward nearby agents. None sees the whole flock. Turn a rule down to see what breaks. An agent leaving an edge re-enters on the opposite side.";
        public override string Note=>"Deterministic Reynolds-style steering on a periodic 2D domain. A behavioural toy model, not bird aerodynamics. Speeds and steering accelerations are capped; agents have no physical collision shapes. Order measures heading alignment, not intelligence or a biological fit.";
        protected override double StepSeconds=>FlockingModel.StepSize;
        protected override Vector3 CameraPosition=>new Vector3(0,0,-26);
        FlockingModel model;
        float separation=1.5f,alignment=1,cohesion=0.7f;
        int preset;
        protected override void InitializeModel()=>model=new FlockingModel(Value(separation),Value(alignment),Value(cohesion));
        protected override void StepModel()=>model.Step();
        protected override void DrawModel()
        {
            for(int i=0;i<FlockingModel.Count;i++)
            {
                var p=World(model.Positions[i]); var v=World(model.Velocities[i].Unit); var c=Color.Lerp(Cyan,Amber,i/(float)FlockingModel.Count);
                Dot(p,0.055f,c); Line(p-v*0.17f,p+v*0.23f,c);
                var side=new Vector3(-v.y,v.x,0)*0.09f;
                Line(p+v*0.23f,p+side,c); Line(p+v*0.23f,p-side,c);
            }
            var dim=new Color(0.35f,0.55f,0.8f,0.4f);
            Line(new Vector3(-8,-5,0),new Vector3(8,-5,0),dim); Line(new Vector3(8,-5,0),new Vector3(8,5,0),dim);
            Line(new Vector3(8,5,0),new Vector3(-8,5,0),dim); Line(new Vector3(-8,5,0),new Vector3(-8,-5,0),dim);
        }
        protected override void ModelControls(PanelUI ui)
        {
            int p=ui.Choice("Steering recipe",preset,new[]{"Flock","Separation only","Ballistic: no steering"});
            if(p!=preset) { preset=p; separation=p==0?1.5f:p==1?2:0; alignment=p==0?1:0; cohesion=p==0?0.7f:0; Reset(); }
            if(Slider(ui,"Separation (restarts)",ref separation,0,3,0.1f)|Slider(ui,"Alignment (restarts)",ref alignment,0,3,0.1f)|Slider(ui,"Cohesion (restarts)",ref cohesion,0,3,0.1f)) Reset();
        }
        protected override void ModelReadouts(List<Readout> list)
        { list.Add(new Readout("Heading order",(100*model.Order).ToString("0.0")+" %",Tone.Cold)); list.Add(new Readout("Agents","96")); list.Add(new Readout("Neighbourhood radius","2.5 / separation 0.8")); }
    }
}
