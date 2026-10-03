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
        // Return true to take mouse drag / wheel input instead of the shared orbit camera.
        public virtual bool HandleDrag(Vector2 delta, int button) => false;
        public virtual bool HandleZoom(float wheel) => false;
        protected virtual void Teardown() { }

        // ---- UI Toolkit shell: a scene can draw its own HUD instead of the IMGUI instrument panel
        public virtual bool HasHud => false;
        public virtual void BuildHud(UI.UiShell shell, UnityEngine.UIElements.VisualElement layer) { }
        public virtual void UpdateHud(UI.UiShell shell) { }

        // ---- free camera (photo mode, cinematics). By default the rig sits at an offset from the orbit target.
        // Scenes that place the camera themselves every frame apply the rig in LateTick (RigInScene).
        public virtual bool RigInScene => false;
        public virtual void RigBegin(CamRig rig)
        {
            rig.Offset = Cam.transform.position - Orbit.target;
            rig.Rotation = Cam.transform.rotation;
            rig.Scale = Mathf.Max(Orbit.mode == OrbitCamera.Mode.Orbit ? Orbit.distance : 2f, 0.05f);
        }
        public virtual void RigApply(CamRig rig) => Cam.transform.SetPositionAndRotation(Orbit.target + rig.Offset, rig.Rotation);
        // world position (in the rig's units, relative to the anchor) of the thing the camera is looking at, for orbit shots
        public virtual Vector3 RigFocus => Vector3.zero;
    }
}
