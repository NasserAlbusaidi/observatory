using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    public enum Tone { Normal, Hot, Cold, Bad }

    public struct Readout
    {
        public string label, value; public Tone tone;
        public Readout(string l, string v, Tone t = Tone.Normal) { label = l; value = v; tone = t; }
    }

    // One simulation. The app owns the camera and UI; a scene builds its objects under Root and reports readouts.
    public abstract class SimScene
    {
        public abstract string Title { get; }
        public abstract string Eyebrow { get; }
        public abstract string Short { get; }
        public abstract string Blurb { get; }
        public virtual string Note => null;

        protected FogOfWarApp App;
        protected Camera Cam => App.Cam;
        protected OrbitCamera Orbit => App.Orbit;
        protected Transform Root;

        public void Enter(FogOfWarApp app)
        {
            App = app;
            Root = new GameObject(GetType().Name).transform;
            Build();
        }

        public void Exit()
        {
            Teardown();
            if (Root != null) Gfx.Destroy(Root.gameObject);
            foreach (var o in owned) Gfx.Destroy(o);
            owned.Clear();
        }

        readonly List<Object> owned = new List<Object>();
        // Track meshes/materials/textures created at runtime so switching scenes doesn't leak GPU memory.
        protected T Own<T>(T o) where T : Object { owned.Add(o); return o; }
        protected Material OwnMat(Material m) => Own(m);

        protected abstract void Build();
        public virtual void Tick(float dt) { }
        public virtual void LateTick(float dt) { }
        public abstract void Readouts(List<Readout> list);
        public abstract void Controls(PanelUI ui);
        public virtual void OnGUIOverlay() { }
        protected virtual void Teardown() { }
    }
}
