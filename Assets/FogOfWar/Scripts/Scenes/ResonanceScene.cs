using System;
using System.Collections.Generic;
using UnityEngine;
namespace FogOfWar
{
    public sealed class ResonanceScene : LabScene
    {
        public override string Title=>"Resonance Bank";
        public override string Eyebrow=>"One driver. Nineteen responses.";
        public override string Short=>"Find the frequency that takes over";
        public override string Blurb=>"Every oscillator feels the same periodic force, but each spring has a different natural frequency. Find the spring that responds most strongly. Lower damping to sharpen the response; raise it to broaden the peak. The cyan curve underneath is the predicted steady amplitude.";
        public override string Note=>"Independent unit-mass driven damped oscillators: x″+2γx′+ω₀²x=0.8 cos(ωt). They start at rest and are not coupled to each other. Natural angular frequencies run from 1 to 3 rad per model-time unit. Displacements are drawn at 0.4 display units per unit x.";
        protected override double StepSeconds=>ResonanceModel.StepSize;
        readonly ResonanceModel[] bank=new ResonanceModel[19];
        float drive=2,damping=0.15f;
        protected override void InitializeModel() { for(int i=0;i<bank.Length;i++) bank[i]=new ResonanceModel(1+i/9.0,Value(drive),Value(damping)); }
        protected override void StepModel() { foreach(var m in bank) m.Step(); }
        protected override void DrawModel()
        {
            Grid();
            for(int i=0;i<bank.Length;i++)
            {
                float x=(i-9)*0.55f,y=-0.6f+(float)bank[i].X*0.4f;
                var color=Color.Lerp(Cyan,Amber,(float)Math.Min(1,Math.Abs(bank[i].X)/3));
                var prev=new Vector3(x,3.2f,0);
                for(int j=1;j<=16;j++) { var next=new Vector3(x+(j==16?0:j%2==0?-0.07f:0.07f),Mathf.Lerp(3.2f,y,j/16f),0); Line(prev,next,color); prev=next; }
                Dot(new Vector3(x,y,0),0.1f,color);
                if(i>0) Line(new Vector3(x-0.55f,-4+(float)bank[i-1].SteadyAmplitude*0.2f,0),new Vector3(x,-4+(float)bank[i].SteadyAmplitude*0.2f,0),Cyan);
            }
        }
        protected override void ModelControls(PanelUI ui)
        { if(Slider(ui,"Drive angular frequency",ref drive,0.7f,3.3f,0.05f)|Slider(ui,"Damping gamma",ref damping,0.05f,1,0.05f)) Reset(); }
        protected override void ModelReadouts(List<Readout> list)
        {
            int best=0; for(int i=1;i<bank.Length;i++) if(bank[i].SteadyAmplitude>bank[best].SteadyAmplitude) best=i;
            list.Add(new Readout("Strongest steady response",bank[best].Natural.ToString("0.000")+" rad / time",Tone.Hot));
            list.Add(new Readout("Predicted amplitude",bank[best].SteadyAmplitude.ToString("0.000"),Tone.Cold)); list.Add(new Readout("Decay timescale",(1/damping).ToString("0.00")));
        }
    }
}
