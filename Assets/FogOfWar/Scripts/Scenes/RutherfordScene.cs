using System;
using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class RutherfordScene : LabScene
    {
        public override string Title=>"Rutherford Scattering";
        public override string Eyebrow=>"A small target, a large deflection";
        public override string Short=>"Fire a beam at a charged nucleus";
        public override string Blurb=>"Seventeen independent particles approach a repulsive centre. Near misses bend more; a head-on particle turns back. Change the beam energy, spread or central charge and replay the same beam. The coloured tracks show which particles sampled the strongest field.";
        public override string Note=>"Classical repulsive inverse-square force, unit projectile mass and a fixed point nucleus. No electrons, nuclear force, quantum scattering or recoil. Launches start at finite radius; these are trajectories, not a calibrated differential cross section. Enlarged markers and arbitrary units.";
        protected override double StepSeconds=>CentralForceModel.StepSize;
        readonly CentralForceModel[] particles=new CentralForceModel[17];
        readonly LabTrail[] trails=new LabTrail[17];
        float energy=2,spread=3,charge=1;
        protected override void InitializeModel()
        {
            for(int i=0;i<particles.Length;i++)
            { particles[i]=new CentralForceModel(Value(charge),(i-8)*Value(spread)/8,Math.Sqrt(2*Value(energy))); if(trails[i]==null) trails[i]=new LabTrail(); trails[i].Clear(); }
        }
        protected override void StepModel() { foreach(var p in particles) p.Step(); }
        protected override void Sample() { for(int i=0;i<particles.Length;i++) if(!particles[i].Stopped) trails[i].Add(World(particles[i].Position,0.35f)); }
        protected override void DrawModel()
        {
            Grid(); Dot(Vector3.zero,0.2f,Coral); Ring(Vector3.zero,0.6f,new Color(1,0.4f,0.4f,0.25f));
            for(int i=0;i<particles.Length;i++)
            { var color=Color.Lerp(Amber,Cyan,Math.Abs(i-8)/8f); trails[i].Append(lineVertices,lineColors,color); Dot(World(particles[i].Position,0.35f),0.045f,color); }
        }
        protected override void ModelControls(PanelUI ui)
        { if(Slider(ui,"Launch kinetic energy",ref energy,0.5f,5,0.25f)|Slider(ui,"Beam half-width",ref spread,0.5f,5,0.25f)|Slider(ui,"Repulsive strength",ref charge,0.5f,3,0.25f)) Reset(); }
        protected override void ModelReadouts(List<Readout> list)
        {
            int backwards=0,stopped=0; double error=0;
            foreach(var p in particles) { if(p.Velocity.x<0) backwards++; if(p.Stopped) stopped++; error=Math.Max(error,Math.Abs(p.Energy-p.InitialEnergy)); }
            list.Add(new Readout("Travelling backwards",backwards+" / 17",Tone.Hot)); list.Add(new Readout("Left the lab",stopped+" / 17")); list.Add(new Readout("Max energy error",error.ToString("0.00E+0"),Tone.Cold));
        }
    }
}
