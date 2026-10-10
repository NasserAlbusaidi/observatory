// Test adapters only: no rendering, native input, camera transforms or Unity object lifecycle is certified.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { }
    public class Mesh : Object
    {
        public string name;
        public Vector3[] vertices = Array.Empty<Vector3>();
        public Vector2[] uv;
        public Color[] colors;
        public int[] triangles;
        public void Clear() { vertices=Array.Empty<Vector3>(); colors=Array.Empty<Color>(); triangles=Array.Empty<int>(); }
        public void SetVertices(List<Vector3> v) { foreach(var p in v) if(!float.IsFinite(p.magnitude)) throw new Exception("Invalid marker coordinate"); vertices=v.ToArray(); }
        public void SetColors(List<Color> c) { if(c.Count!=vertices.Length) throw new Exception("Marker colors mismatch"); colors=c.ToArray(); }
        public void SetTriangles(List<int> t,int submesh) { foreach(int i in t) if(i<0||i>=vertices.Length) throw new Exception("Triangle index"); triangles=t.ToArray(); }
    }
    public class Material : Object { public void SetTexture(string name,Texture2D value) { } }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Bilinear }
    public enum TextureWrapMode { Clamp }
    public class Texture2D : Object
    {
        readonly int count;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Color[] Pixels;
        public Texture2D(int w,int h,TextureFormat format,bool mip) { count=w*h; }
        public void SetPixels(Color[] c) { if(c.Length!=count) throw new Exception("Texture size mismatch"); foreach(var p in c) if(!float.IsFinite(p.r+p.g+p.b+p.a)) throw new Exception("Invalid field pixel"); Pixels=(Color[])c.Clone(); }
        public void Apply(bool mip) { }
    }
    public class Camera { public Transform transform = new Transform(); }
    public class Transform
    {
        public GameObject gameObject;
        public Vector3 localScale, localPosition, position;
        public Quaternion rotation;
        public void SetParent(Transform p, bool world) { }
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
    }
    public class GameObject : Object
    {
        public Transform transform;
        public GameObject(string name) { transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : new() => new T();
    }
    public class LineRenderer
    {
        public bool useWorldSpace, receiveShadows;
        public int positionCount;
        public float widthMultiplier;
        public Material sharedMaterial;
        public Color startColor, endColor;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public void SetPosition(int index, Vector3 value) { if (index >= positionCount) throw new Exception("Rod index"); }
    }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x;this.y=y; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public float magnitude => (float)Math.Sqrt(x*x+y*y+z*z);
        public Vector3 normalized => this * (1 / magnitude);
        public static Vector3 zero => new Vector3();
        public static Vector3 one => new Vector3(1,1,1);
        public static Vector3 operator *(Vector3 p, float k) => new Vector3(p.x*k,p.y*k,p.z*k);
        public static Vector3 operator +(Vector3 p, Vector3 q) => new Vector3(p.x+q.x,p.y+q.y,p.z+q.z);
        public static Vector3 operator -(Vector3 p, Vector3 q) => new Vector3(p.x-q.x,p.y-q.y,p.z-q.z);
    }
    public struct Quaternion { }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public static Color white => new Color(1,1,1);
        public static Color Lerp(Color a,Color b,float t) { t=Mathf.Clamp01(t); return new Color(a.r+(b.r-a.r)*t,a.g+(b.g-a.g)*t,a.b+(b.b-a.b)*t,a.a+(b.a-a.a)*t); }
        public static Color operator *(Color c,float k) => new Color(c.r*k,c.g*k,c.b*k,c.a*k);
    }
    public static class Shader { public static void SetGlobalVector(string n, Vector3 v) { } }
    public static class Mathf
    {
        public static int Min(int a,int b) => Math.Min(a,b);
        public static float Max(float a,float b) => Math.Max(a,b);
        public static float Clamp(float x,float a,float b) => Math.Max(a,Math.Min(x,b));
        public static float Clamp01(float x)=>Clamp(x,0,1);
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
        public static float Pow(float x,float y) => (float)Math.Pow(x,y);
    }
    public enum KeyCode { None=0, Escape=27, Alpha0=48, Alpha1=49, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9, H=104, F1=282, F2, F9=290, F10, F11, F12, LeftShift=304, LeftControl=306, Period=46, Comma=44, LeftBracket=91, RightBracket=93, KeypadPlus=270, KeypadMinus=269 }
    public static class Input { public static bool GetKey(KeyCode k)=>false; public static bool GetKeyDown(KeyCode k)=>false; }
    public static class PlayerPrefs
    {
        static readonly Dictionary<string,string> values = new Dictionary<string,string>();
        public static string GetString(string k,string d=null)=>values.TryGetValue(k,out var v)?v:d;
        public static void SetString(string k,string v)=>values[k]=v;
        public static void Save() { }
    }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace UnityEngine.UIElements { public class VisualElement { } }
namespace FogOfWar.UI { public class UiShell { } }
namespace FogOfWar
{
    using UnityEngine;
    public class CamRig { public Vector3 Offset; public Quaternion Rotation; public float Scale; }
    public class OrbitCamera
    {
        public enum Mode { Orbit, Look }
        public Mode mode;
        public float distance;
        public bool enabled=true;
        public Vector3 target;
        public void Set(Vector3 p, Vector3 t, float min, float max) { target=t; distance=(p-t).magnitude; }
    }
    public class FogOfWarApp { public OrbitCamera Orbit=new OrbitCamera(); public Camera Cam=new Camera(); public CamRig Rig; }
    public class PanelUI
    {
        public string Press;
        public readonly Dictionary<string,float> Sliders = new Dictionary<string,float>();
        public readonly Dictionary<string,int> Choices = new Dictionary<string,int>();
        public readonly Dictionary<string,bool> Toggles = new Dictionary<string,bool>();
        public int? AllChoices;
        public int Extremes; // -1 minimum, +1 maximum
        public int Buttons(params string[] labels) { int i=Array.IndexOf(labels,Press); if(i>=0) Press=null; return i; }
        public int Choice(string label,int v,string[] choices)=>Choices.TryGetValue(label,out var n)?n:AllChoices.HasValue?Math.Min(AllChoices.Value,choices.Length-1):v;
        public float Slider(string label,float v,float min,float max,Func<float,string> fmt,float step=0)
        {
            float next=Sliders.TryGetValue(label,out var n)?n:Extremes<0?min:Extremes>0?max:v;
            return step>0?(float)Math.Round(next/step)*step:next;
        }
        public bool Toggle(string label,bool v)=>Toggles.TryGetValue(label,out var n)?n:v;
        public void Section(string text) { }
    }
    public static class Gfx
    {
        public static void Destroy(Object o) { }
        public static Mesh UVSphere(int a,int b)=>new Mesh();
        public static Mesh LineMesh(string n)=>new Mesh();
        public static Material Lit(Color c,Color emission=default,float spec=.4f)=>new Material();
        public static Material Unlit(Color c,bool additive=false,bool zwrite=false)=>new Material();
        public static GameObject Obj(string n,Transform p,Mesh m,Material mat)=>new GameObject(n);
        public static GameObject Starfield(Transform p,int count=5000,float gain=1)=>new GameObject("Stars");
        public static void SetLines(Mesh m,List<Vector3> p,List<Color> c)
        {
            if(p.Count!=c.Count || p.Count%2!=0) throw new Exception("Line geometry mismatch");
            foreach(var v in p) if(float.IsNaN(v.magnitude)||float.IsInfinity(v.magnitude)) throw new Exception("Invalid line coordinate");
            m.vertices=p.ToArray();
        }
    }
}
