using System.Collections.Generic;
namespace FogOfWar
{
    public sealed class ReactionDiffusionScene : LabScene
    {
        public override string Title=>"Reaction–Diffusion";
        public override string Eyebrow=>"Patterns without a pattern maker";
        public override string Short=>"Grow spots, stripes and coral";
        public override string Blurb=>"Two chemical concentrations diffuse and react on a periodic surface. A small seed can spread, split or disappear. Change the feed and removal rates to explore the pattern family, or plant another seed while it runs. Brighter pixels contain more of species V.";
        public override string Note=>"Dimensionless Gray–Scott model on a 64×64 periodic grid, explicit Euler dt=1 with diffusion rates 0.16 and 0.08. These are synthetic chemical patterns, not a biological growth model. The display advances 128 model steps per wall second at 1×.";
        protected override double StepSeconds=>1;
        protected override double ClockRate=>128;
        protected override int FieldSize=>ReactionDiffusionModel.Size;
        ReactionDiffusionModel model;
        float feed=0.035f,kill=0.062f;
        int preset;
        protected override void InitializeModel()=>model=new ReactionDiffusionModel(Value(feed),Value(kill));
        protected override void StepModel()=>model.Step();
        protected override void DrawModel()=>Paint(model.V,3);
        protected override void ModelControls(PanelUI ui)
        {
            int p=ui.Choice("Reaction recipe",preset,new[]{"Spots","Maze","Coral"});
            if(p!=preset) { preset=p; feed=p==0?0.035f:p==1?0.029f:0.0545f; kill=p==0?0.062f:p==1?0.057f:0.062f; Reset(); }
            if(Slider(ui,"Feed rate (restarts)",ref feed,0.01f,0.08f,0.0005f)|Slider(ui,"Removal rate (restarts)",ref kill,0.04f,0.075f,0.0005f)) Reset();
            if(ui.Buttons("Plant seed")==0) model.Seed();
        }
        protected override void ModelReadouts(List<Readout> list)
        { list.Add(new Readout("Mean V concentration",model.MeanV.ToString("0.0000"),Tone.Cold)); list.Add(new Readout("Feed / removal",feed.ToString("0.0000")+" / "+kill.ToString("0.0000"))); list.Add(new Readout("Boundary","Wraps at all edges")); }
    }
}
