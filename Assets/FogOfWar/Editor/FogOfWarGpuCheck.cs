using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    public static class FogOfWarGpuCheck
    {
        [Serializable] sealed class Fixtures { public RayFixture[] rays; }
        [Serializable] sealed class RayFixture
        {
            public double a, theta, radius, b, q, criticalOffset;
            public double dirx, diry, dirz;
            public bool hasDirection;
            public int polarSign, radialSign, expected;
        }
        static Material material;
        static Texture2D input, readback;
        static RenderTexture target;
        static BenchmarkReport report;
        static double wallMs;
        // ReadPixels fences native GPU completion. Timing is dispatch + GPU + synchronous readback, not a fabricated
        // GPU-only estimate. Image capture's GPU timings separately describe the actual interactive renderer.
        static Color[] Probe(float k, int budget, int output, bool table = true)
        {
            material.SetFloat("_StepK", k); material.SetInt("_Steps", budget);
            material.SetInt("_Output", output); material.SetInt("_TableProbe", table ? 1 : 0);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Graphics.Blit(null, target, material);
            var previous = RenderTexture.active; RenderTexture.active = target;
            readback.ReadPixels(new Rect(0,0,target.width,1),0,0); readback.Apply(false);
            RenderTexture.active = previous;
            wallMs = watch.Elapsed.TotalMilliseconds;
            return readback.GetPixels();
        }
        static bool Finite(Color c) => !float.IsNaN(c.r+c.g+c.b+c.a) && !float.IsInfinity(c.r+c.g+c.b+c.a);
        static double DirectionError(Color a, Color b) => (new Vector3(a.r,a.g,a.b)-new Vector3(b.r,b.g,b.b)).magnitude;

        [MenuItem("Fog of War/Native GPU benchmarks")]
        public static void Run()
        {
            report = new BenchmarkReport("native shader IEEE float32 / analytic fixture float64; G=c=M=1");
            try
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new Exception("Native graphics device required: do not use -nographics. GPU suite unavailable, not passed.");
                material = new Material(Shader.Find("Hidden/FogOfWar/BlackHoleBenchmark"));
                if (!material.shader.isSupported) throw new Exception("Benchmark shader unsupported");
                var fixtures = JsonUtility.FromJson<Fixtures>(File.ReadAllText("Tools/benchmarks/fixtures/rays.json"));
                report.Check("independent-direction-fixtures",fixtures.rays.Count(r=>r.expected==1 && !r.hasDirection),0,
                    "missing references","every independently escaping fixture must retain its adaptive double-precision direction");
                foreach (var group in fixtures.rays.GroupBy(r => (r.a,r.theta,r.radius)))
                {
                    var rays = group.ToArray(); int n = rays.Length;
                    input = new Texture2D(n,1,TextureFormat.RGBAFloat,false,true) {filterMode=FilterMode.Point};
                    input.SetPixels(rays.Select(r => new Color((float)r.b,(float)r.q,r.polarSign,r.radialSign)).ToArray()); input.Apply();
                    readback = new Texture2D(n,1,TextureFormat.RGBAFloat,false,true);
                    target = new RenderTexture(n,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); target.Create();
                    material.SetTexture("_Fixture",input);
                    double a=group.Key.a, theta=group.Key.theta, radius=group.Key.radius;
                    material.SetFloat("_Spin",(float)a); material.SetFloat("_RH",(float)Kerr.Horizon(a));
                    material.SetVector("_CamBL",new Vector4((float)radius,(float)Math.Cos(theta),0,(float)Math.Sin(theta)));
                    material.SetFloat("_CamTheta",(float)theta); material.SetFloat("_RMax",(float)Math.Max(400,8*radius));
                    material.SetFloat("_GuideOn",1); // full mode crosses the equator too; all guide radii/emitters remain zero
                    string id=$"a={a}/theta={theta*180/Math.PI:F0}/r={radius}";
                    var reference=Probe(0.025f,16000,0); var refSky=Probe(0.025f,16000,1);
                    var refCross=Probe(0.025f,16000,2);
                    double oracleError=0; int oracleWorst=-1;
                    for(int i=0;i<n;i++) if(rays[i].hasDirection && reference[i].r==1)
                    {
                        double error=DirectionError(refSky[i],new Color((float)rays[i].dirx,(float)rays[i].diry,(float)rays[i].dirz));
                        if(error>oracleError) {oracleError=error;oracleWorst=i;}
                    }
                    report.Check(id+"/independent-direction",oracleError,0.02,"unit-vector distance","float64 separated radial quadrature and analytic Jacobi polar motion, Teo equations 5-6",
                        oracleWorst<0 ? "" : $"b={rays[oracleWorst].b:R},q={rays[oracleWorst].q:R},sign={rays[oracleWorst].polarSign},offset={rays[oracleWorst].criticalOffset:R}; native={refSky[oracleWorst]}; independent=({rays[oracleWorst].dirx:R},{rays[oracleWorst].diry:R},{rays[oracleWorst].dirz:R})");
                    int wrong=0, unresolved=0, numerical=0;
                    for(int i=0;i<n;i++) { int reason=(int)reference[i].r; if(reason==2) unresolved++; else if(reason==4 || !Finite(reference[i])) numerical++; else if(reason!=rays[i].expected) wrong++; }
                    report.Check(id+"/reference-classification",wrong,0,"wrong resolved rays","quartic root oracle, Teo 2003 eq. 5b; critical curve +/-0.1% and +/-1%, near-polar inclinations included");
                    report.Check(id+"/reference-numerical-failures",numerical,0,"rays","finite native shader output");
                    report.Check(id+"/reference-unresolved",unresolved/(double)n,0.02,"fraction","unresolved explicitly counted; fine trace is convergence comparison, not truth",$"N={n}; k=.025; budget=16000");
                    foreach(var setting in new[]{(0.3f,1000,"Fast"),(0.2f,1000,"Balanced"),(0.15f,1200,"Sharp")})
                    {
                        var cls=Probe(setting.Item1,setting.Item2,0); double timing=wallMs;
                        var sky=Probe(setting.Item1,setting.Item2,1); var cross=Probe(setting.Item1,setting.Item2,2);
                        var full=Probe(setting.Item1,setting.Item2,0,false);
                        wrong=unresolved=numerical=0; int fallback=0, mismatchedMode=0, escaped=0, crossings=0;
                        double maxDir=0,maxCross=0,maxFallbackDir=0; int worst=-1;
                        for(int i=0;i<n;i++)
                        {
                            int reason=(int)cls[i].r;
                            if(reason==2) { unresolved++; if(cls[i].g>0) fallback++; }
                            else if(reason==4 || !Finite(cls[i]) || !Finite(sky[i])) numerical++;
                            else if(reason!=rays[i].expected) wrong++;
                            if(full[i].r!=cls[i].r) mismatchedMode++;
                            if(reason==1 && reference[i].r==1) { double error=DirectionError(sky[i],refSky[i]); if(error>maxDir){maxDir=error;worst=i;} escaped++; }
                            if(cls[i].g>0 && reference[i].r==1) maxFallbackDir=Math.Max(maxFallbackDir,DirectionError(sky[i],refSky[i]));
                            if(cross[i].r>0 && refCross[i].r>0) { maxCross=Math.Max(maxCross,Math.Abs(cross[i].r-refCross[i].r)/refCross[i].r); crossings++; }
                        }
                        string qid=id+"/"+setting.Item3;
                        report.Check(qid+"/classification",wrong,0,"wrong resolved rays","independent quartic root classification");
                        report.Check(qid+"/finite",numerical,0,"rays","native float output must be finite");
                        report.Check(qid+"/mode-consistency",mismatchedMode,0,"rays","full trace versus lens-table generation with no opaque emitters");
                        report.Check(qid+"/unresolved",unresolved/(double)n,0.1,"fraction","near-critical fixture budget ceiling; unresolved never counted as escaped",$"fallback={fallback}/{n}; N={n}; dispatch+fence={timing:F3} ms; k={setting.Item1}; budget={setting.Item2}; near-pole steps capped at k=.1");
                        string worstRay = worst<0 ? "" : $"; worst b={rays[worst].b:R},q={rays[worst].q:R},polarSign={rays[worst].polarSign},criticalOffset={rays[worst].criticalOffset:R}; sky={sky[worst]}; ref={refSky[worst]}";
                        report.Check(qid+"/escape-direction",maxDir,0.02,"unit-vector distance","fine native convergence reference k=.025/16000; classification independently checked",$"resolved escape comparisons={escaped}"+worstRay);
                        report.Check(qid+"/first-crossing",maxCross,0.02,"relative radius","first equatorial intersection versus fine convergence reference",$"crossing comparisons={crossings}");
                        // A fallback near the unstable critical orbit can have any sky direction. Report it as a
                        // display uncertainty bounded by the unit-sphere diameter; never present it as a converged ray.
                        report.Check(qid+"/fallback-direction",maxFallbackDir,2.000001,"unit-vector distance","display-only unresolved fallback; whole-sky uncertainty (NOT 0.02 resolved-ray budget)", $"fallbacks={fallback}; flagged cyan in diagnostics");
                    }
                    // Forced exhausted budget: outgoing finite ray offers a display fallback but must remain reason 2.
                    input.SetPixels(Enumerable.Repeat(new Color(1,1,1,1),n).ToArray()); input.Apply();
                    var limited=Probe(0.15f,1,0);
                    report.Check(id+"/forced-budget",limited.Count(c => c.r!=2 || c.g!=1),0,"misclassified rays","one outward step cannot reach RMax; fallback true, escaped false");
                    target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(readback);
                }
            }
            catch(Exception e) { report.Check("GPU-exception",1,0,"exceptions","native suite must run; unavailable is failure",e.ToString()); }
            finally { if(material!=null) UnityEngine.Object.DestroyImmediate(material); }
            report.Save("gpu-benchmarks");
            if(Application.isBatchMode) EditorApplication.Exit(report.failed==0?0:1);
        }
    }
}
