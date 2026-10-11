using System;
using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class GalaxyEncounterScene : LabScene
    {
        public override string Title=>"Galaxy Encounter";
        public override string Eyebrow=>"Gravity writes tidal tails";
        public override string Short=>"Send two stellar disks past each other";
        public override string Blurb=>"Two gravitating cores carry disks of test stars into an encounter. Watch the disks stretch into bridges and tails. Flip the second disk's rotation, change the offset or raise the approach speed, then compare the replay. Amber and cyan identify each star's original disk.";
        public override string Note=>"Restricted planar encounter: two equal cores of mass 8, 192 massless tracers, G=1 and softening 0.4. Tracers feel both cores but exert no gravity. No gas, halos, friction or merger prescription. Energy error is scaled by initial kinetic plus absolute potential energy. This is not a reconstruction of a real galaxy pair.";
        protected override double StepSeconds=>GalaxyEncounterModel.StepSize;
        GalaxyEncounterModel model;
        readonly LabTrail a=new LabTrail(),b=new LabTrail();
        float impact=1.5f,approach=0.6f;
        bool retrograde,autoZoom=true;
        protected override void InitializeModel() { model=new GalaxyEncounterModel(Value(impact),Value(approach),retrograde); a.Clear(); b.Clear(); }
        protected override void StepModel()=>model.Step();
        protected override void Sample() { a.Add(World(model.Cores[0],0.75f)); b.Add(World(model.Cores[1],0.75f)); }
        protected override void DrawModel()
        {
            a.Append(lineVertices,lineColors,new Color(1,0.71f,0.28f,0.3f)); b.Append(lineVertices,lineColors,new Color(0.3f,0.82f,1,0.3f));
            float radius=4;
            for(int i=0;i<GalaxyEncounterModel.Count;i++)
            { var p=World(model.Stars[i],0.75f); Dot(p,0.035f,i<GalaxyEncounterModel.Count/2?Amber:Cyan); radius=Mathf.Max(radius,p.magnitude); }
            Dot(World(model.Cores[0],0.75f),0.15f,Amber); Dot(World(model.Cores[1],0.75f),0.15f,Cyan);
            if(autoZoom&&App.Rig==null) Orbit.distance=Mathf.Clamp(radius*3,18,80);
        }
        public override bool HandleZoom(float wheel) { autoZoom=false; return false; }
        protected override void ModelControls(PanelUI ui)
        {
            bool r=ui.Toggle("Second disk rotates backwards",retrograde);
            bool change=Slider(ui,"Encounter offset (restarts)",ref impact,0.2f,4,0.1f)|Slider(ui,"Core approach speed",ref approach,0.2f,1.4f,0.1f);
            if(r!=retrograde||change) { retrograde=r; Reset(); }
            autoZoom=ui.Toggle("Auto zoom on encounter",autoZoom);
        }
        protected override void ModelReadouts(List<Readout> list)
        {
            double drift=Math.Abs(model.Energy-model.InitialEnergy)/model.EnergyScale;
            list.Add(new Readout("Core separation",(model.Cores[1]-model.Cores[0]).Length.ToString("0.000")));
            list.Add(new Readout("Core energy error",(100*drift).ToString("0.0000")+" %",drift>0.005?Tone.Bad:Tone.Cold)); list.Add(new Readout("Massless disk tracers","192"));
        }
    }
}
