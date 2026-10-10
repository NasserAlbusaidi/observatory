using System;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar;
using UnityEngine;
public static class SceneChecks
{
    static readonly List<string> lines=new List<string>();
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); lines.Add("PASS "+name); }
    static object Field(object o,string n)=>o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
    static double Time(SimScene s)=>s is ThreeBodyScene ? ((ThreeBodyModel)Field(s,"model")).Time : ((DoublePendulumModel[])Field(s,"models"))[0].Time;
    static void Press(SimScene s,string label) { var ui=new PanelUI{Press=label}; s.Controls(ui); Check(ui.Press==null,"button handled: "+label); }
    static void Tick(SimScene s,int frames,float dt) { for(int i=0;i<frames;i++) s.Tick(dt); }
    public static string[] Run()
    {
        PlayerPrefs.SetString("fow-key-Help-0","Alpha0");
        PlayerPrefs.SetString("fow-key-Screenshot-0","Alpha9");
        Check(Controls.Get(Controls.Action.Help,0)==KeyCode.F1,"old zero binding repaired");
        Check(Controls.Get(Controls.Action.Screenshot,0)==KeyCode.F12,"old nine binding repaired");
        for(int i=0;i<10;i++)
        {
            var key=i==9?KeyCode.Alpha0:KeyCode.Alpha1+i;
            Check(Controls.SceneIndex(key)==i,"digit selects scene "+(i+1));
            Check(Controls.SceneIndex(key,true)==i+10,"shift-digit selects scene "+(i+11));
            Check(Controls.FixedUse(key)!=null && Controls.Set(Controls.Action.Help,1,key)!=null,"scene digit cannot be rebound "+key);
        }
        Check(Controls.SceneIndex(KeyCode.H)==-1,"non-scene key rejected");
        Check(Controls.SceneIndex(KeyCode.H,true)==-1,"shift non-scene key rejected");
        foreach(var s in new SimScene[]{new ThreeBodyScene(),new DoublePendulumScene()})
        {
            var app=new FogOfWarApp(); s.Enter(app);
            Tick(s,30,1f/30); double at30=Time(s);
            Check(Math.Abs(at30-1)<0.003,s.Title+" elapsed time");
            int before=((Mesh)Field(s,"trailMesh")).vertices.Length;
            Press(s,"Pause"); Tick(s,20,1f/60);
            Check(Time(s)==at30,s.Title+" pause freezes model");
            Check(((Mesh)Field(s,"trailMesh")).vertices.Length==before,s.Title+" pause retains trails");
            Press(s,s is ThreeBodyScene?"Step 1/60 unit":"Step 1/60 s");
            Check(Math.Abs(Time(s)-at30-1.0/60)<1e-10,s.Title+" single step");
            Press(s,"Replay"); Check(Time(s)==0,s.Title+" replay resets time");
            Tick(s,120,1f/120);
            Check(Math.Abs(Time(s)-at30)<1e-10,s.Title+" 30/120 fps fixed-step equivalence");
            var controls=new PanelUI();
            if(s is ThreeBodyScene)
            {
                controls.Choices["Initial arrangement"]=2;
                controls.Sliders["Cyan launch nudge (restarts)"]=0.2f;
            }
            else
            {
                controls.Choices["Launch preset"]=2;
                controls.Sliders["Upper angle"]=179;
                controls.Sliders["Lower angle"]=-179;
                controls.Sliders["Twin's angle difference"]=0;
                controls.Sliders["Gravity"]=20;
            }
            controls.Sliders["Simulation speed"]=4;
            s.Controls(controls); Tick(s,60,1f/60);
            Check(Math.Abs(Time(s)-4)<0.003,s.Title+" extreme sliders and 4x speed");
            var readouts=new List<Readout>(); s.Readouts(readouts);
            foreach(var r in readouts) Check(!r.value.Contains("NaN")&&!r.value.Contains("Infinity"),s.Title+" finite "+r.label);
            s.Exit(); s.Enter(app); Tick(s,1,1f/60);
            Check(Time(s)>0&&Time(s)<0.1,s.Title+" scene re-entry rebuilds model"); s.Exit();
        }
        foreach(var s in new LabScene[]{new LorenzScene(),new ReactionDiffusionScene(),new WaveTankScene(),new GravityAssistScene(),new LagrangeScene(),new ChargedParticleScene(),new RutherfordScene(),new ResonanceScene(),new FlockingScene(),new GalaxyEncounterScene()})
            Lab(s);
        lines.Add($"All {lines.Count} headless scene checks passed against adapters; native Unity rendering/input not tested.");
        return lines.ToArray();
    }

    static Mesh Mesh(LabScene s,string name)=>(Mesh)typeof(LabScene).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);
    static double Geometry(LabScene s)
    {
        double sum=0;
        foreach(var m in new[]{Mesh(s,"lines"),Mesh(s,"dots")})
        {
            int i=1; foreach(var p in m.vertices) sum+=(p.x+p.y*7+p.z*13)*i++;
            Check(m.vertices.Length<=65535,s.Title+" bounded mesh vertex count");
        }
        var texture=(Texture2D)typeof(LabScene).GetField("field",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);
        if(texture!=null) foreach(var p in texture.Pixels) sum+=p.r+p.g*7+p.b*13;
        return sum;
    }
    static void Read(LabScene s)
    {
        var list=new List<Readout>(); s.Readouts(list); Check(list.Count>=4,s.Title+" model readouts available");
        foreach(var r in list) Check(!r.value.Contains("NaN")&&!r.value.Contains("Infinity"),s.Title+" finite "+r.label);
    }
    static void Lab(LabScene s)
    {
        string name=s.Title; var app=new FogOfWarApp(); s.Enter(app);
        double start=Geometry(s); s.Controls(new PanelUI());
        Tick(s,30,1f/30); double at30=s.Elapsed, after=Geometry(s);
        Check(at30>0&&start!=after,name+" moves after launch");
        s.Controls(new PanelUI()); Check(s.Elapsed==at30,name+" unchanged controls do not reset model");
        Press(s,"Pause"); Tick(s,30,1f/60);
        Check(s.Paused&&s.Elapsed==at30&&Geometry(s)==after,name+" pause freezes clock and geometry");
        Press(s,"One model step"); Check(s.Paused&&s.Elapsed>at30,name+" single step stays paused");
        Press(s,"Replay"); Check(!s.Paused&&s.Elapsed==0&&Geometry(s)==start,name+" replay reproduces initial geometry");
        Tick(s,120,1f/120);
        Check(Math.Abs(s.Elapsed-at30)<1e-8&&Math.Abs(Geometry(s)-after)<1e-6,name+" 30/120 fps equivalent trajectory");
        double elapsed=s.Elapsed; Press(s,"Reset view"); Check(s.Elapsed==elapsed,name+" camera reset preserves model");
        foreach(int extreme in new[]{-1,1})
        {
            var ui=new PanelUI{Extremes=extreme}; s.Controls(ui); s.Reset();
            Tick(s,60,1f/60); Read(s); Geometry(s);
            elapsed=s.Elapsed; s.Controls(ui); Check(s.Elapsed==elapsed,name+" stable extreme controls "+extreme);
        }
        foreach(int choice in new[]{0,1,2})
        {
            s.Controls(new PanelUI{AllChoices=choice}); Tick(s,2,1f/60); Read(s);
        }
        if(s is ReactionDiffusionScene)
        {
            Tick(s,12,1f/60); double before=Geometry(s); Press(s,"Plant seed"); s.Tick(0);
            Check(Geometry(s)!=before,name+" planting a seed changes concentrations");
        }
        if(s is WaveTankScene)
        {
            double before=Geometry(s); Press(s,"Drop pulse"); s.Tick(0); Check(Geometry(s)!=before,name+" pulse changes field");
        }
        if(s is GalaxyEncounterScene)
        {
            var ui=new PanelUI(); ui.Toggles["Second disk rotates backwards"]=true; s.Controls(ui);
            Check(s.Elapsed==0,name+" retrograde choice restarts encounter");
            s.HandleZoom(1); Check(!(bool)Field(s,"autoZoom"),name+" manual zoom disables auto zoom");
        }
        s.Controls(new PanelUI{Sliders={ ["Playback speed"]=1 }}); s.Reset();
        // Fill the 16-unit trail buffers, including the 17-particle beam, and check geometry remains bounded.
        Tick(s,540,1f/30); Geometry(s); Read(s);
        s.Exit(); s.Enter(app); Tick(s,1,1f/60); Read(s);
        Check(s.Elapsed>0&&s.Elapsed<3,name+" re-entry rebuilds clock and model"); s.Exit();
    }
}
