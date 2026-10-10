using System;
using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class WaveTankScene : LabScene
    {
        public override string Title=>"Wave Interference";
        public override string Eyebrow=>"When a wave meets a wave";
        public override string Short=>"Build and erase a ripple";
        public override string Blurb=>"Two point drivers push the same wave tank. Amber shows positive displacement and cyan negative displacement. Change the phase between the drivers to move the quiet zones. Drop a pulse and watch it reflect from the walls.";
        public override string Note=>"A damped scalar 2D wave equation on a 64×64 unit grid with fixed reflecting edges. Drivers prescribe displacement at two cells. This is a linear wave field, not a fluid solver; no water depth, turbulence or breaking waves. Time is in model units.";
        protected override double StepSeconds=>WaveTankModel.StepSize;
        protected override int FieldSize=>WaveTankModel.Size;
        WaveTankModel model;
        float frequency=0.5f,phase=0,separation=16,waveSpeed=10;
        protected override void InitializeModel() { model=new WaveTankModel(Value(frequency),phase*Math.PI/180,(int)separation,Value(waveSpeed)); model.Pulse(); }
        protected override void StepModel()=>model.Step();
        protected override void DrawModel()
        {
            Paint(model.Height,1.7,true);
            for(int i=0;i<2;i++) Dot(new Vector3((32+(i==0?-1:1)*separation/2+0.5f)*12/64-6,0.09375f,-0.03f),0.09f,i==0?Amber:Cyan);
        }
        protected override void ModelControls(PanelUI ui)
        {
            if(Slider(ui,"Frequency (restarts)",ref frequency,0.15f,1.5f,0.05f)|Slider(ui,"Phase difference (degrees)",ref phase,0,180,15)|Slider(ui,"Source separation (cells)",ref separation,4,32,2)|Slider(ui,"Wave speed (cells / time)",ref waveSpeed,4,12,1)) Reset();
            if(ui.Buttons("Drop pulse")==0) model.Pulse();
        }
        protected override void ModelReadouts(List<Readout> list)
        { list.Add(new Readout("RMS displacement",model.Rms.ToString("0.000"),Tone.Cold)); list.Add(new Readout("Wavelength",(waveSpeed/frequency).ToString("0.0")+" cells")); list.Add(new Readout("Source phase",phase.ToString("0")+"°")); }
    }
}
