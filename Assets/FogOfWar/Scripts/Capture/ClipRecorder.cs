using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace FogOfWar
{
    // Records the camera (no HUD) to an MP4 by piping raw frames into ffmpeg. Time.captureFramerate fixes the frame
    // time, so the clip plays smoothly at its frame rate even when the game can't render that fast while recording.
    // Without ffmpeg the frames are written as PNGs next to an encode.bat.
    public class ClipRecorder
    {
        readonly CaptureRunner owner;
        public bool Active { get; private set; }
        public int Fps { get; private set; } = 30;
        public float Length { get; private set; }
        public float Elapsed => frames / (float)Fps;
        public string LastOutput { get; private set; }

        Camera cam;
        RenderTexture rt;
        int width, height, frames, maxFrames;
        string outPath, frameDir;
        Process ffmpeg;
        Thread writer;
        BlockingCollection<byte[]> queue;
        volatile string writeError;
        int prevCaptureRate;

        static string ffmpegPath; static bool searched;

        public ClipRecorder(CaptureRunner owner) { this.owner = owner; }

        // ffmpeg on the PATH, or next to the game (FogOfWar_Data/../ffmpeg/ffmpeg.exe); null when there is none
        public static string FfmpegPath
        {
            get
            {
                if (searched) return ffmpegPath;
                searched = true;
                var local = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ffmpeg", "ffmpeg.exe");
                if (File.Exists(local)) return ffmpegPath = local;
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    try
                    {
                        var p = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                        if (File.Exists(p)) return ffmpegPath = p;
                    }
                    catch (ArgumentException) { }
                }
                return null;
            }
        }

        public void Start(Camera camera, int seconds, int fps)
        {
            if (Active) return;
            cam = camera;
            Fps = fps;
            Length = seconds;
            maxFrames = seconds * fps;
            // even sizes for yuv420p; cap at 4K-class width so encoding keeps up
            int sw = camera.pixelWidth, sh = camera.pixelHeight;
            float k = Mathf.Min(1, 3840f / Mathf.Max(sw, sh));
            width = Mathf.Max(2, Mathf.RoundToInt(sw * k) & ~1);
            height = Mathf.Max(2, Mathf.RoundToInt(sh * k) & ~1);
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Clip", antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing) };
            Directory.CreateDirectory(GameSettings.CaptureDir);
            string stamp = "FogOfWar_" + Capture.Stamp();
            writeError = null;
            queue = new BlockingCollection<byte[]>(6);
            var ff = FfmpegPath;
            try
            {
                if (ff != null)
                {
                    outPath = Path.Combine(GameSettings.CaptureDir, stamp + ".mp4");
                    int crf = GameSettings.ClipQuality == 0 ? 26 : GameSettings.ClipQuality == 1 ? 20 : 14;
                    var psi = new ProcessStartInfo(ff,
                        $"-y -hide_banner -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - " +
                        $"-vf vflip -c:v libx264 -preset medium -crf {crf} -pix_fmt yuv420p -movflags +faststart \"{outPath}\"")
                    {
                        UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true,
                    };
                    ffmpeg = Process.Start(psi);
                    var stdin = ffmpeg.StandardInput.BaseStream;
                    var err = ffmpeg;
                    // drain stderr so ffmpeg never blocks on a full pipe
                    new Thread(() => { try { var e = err.StandardError.ReadToEnd(); if (!string.IsNullOrWhiteSpace(e)) writeError = e.Trim(); } catch (Exception) { } }) { IsBackground = true }.Start();
                    writer = new Thread(() =>
                    {
                        try { foreach (var f in queue.GetConsumingEnumerable()) stdin.Write(f, 0, f.Length); stdin.Flush(); stdin.Close(); }
                        catch (Exception e) { writeError = e.Message; }
                    }) { IsBackground = true, Name = "Clip writer" };
                }
                else
                {
                    frameDir = Path.Combine(GameSettings.CaptureDir, stamp);
                    Directory.CreateDirectory(frameDir);
                    outPath = frameDir;
                    int w = width, h = height; string dir = frameDir;
                    writer = new Thread(() =>
                    {
                        int n = 0;
                        try
                        {
                            foreach (var f in queue.GetConsumingEnumerable())
                            {
                                // readback rows run bottom-up, which is what EncodeArrayToPNG expects
                                File.WriteAllBytes(Path.Combine(dir, $"frame_{n++:00000}.png"), ImageConversion.EncodeArrayToPNG(f, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h));
                            }
                        }
                        catch (Exception e) { writeError = e.Message; }
                    }) { IsBackground = true, Name = "Clip writer" };
                    File.WriteAllText(Path.Combine(frameDir, "encode.bat"),
                        $"@echo off\r\nrem Turns these frames into an MP4. Needs ffmpeg (https://ffmpeg.org) on the PATH.\r\n" +
                        $"ffmpeg -y -framerate {fps} -i frame_%%05d.png -c:v libx264 -crf 18 -pix_fmt yuv420p \"..\\{stamp}.mp4\"\r\npause\r\n");
                }
                writer.Start();
            }
            catch (Exception e)
            {
                Debug.LogError("Fog of War: could not start recording: " + e.Message);
                owner.RecorderMessage("Recording failed", e.Message);
                Cleanup();
                return;
            }
            frames = 0;
            prevCaptureRate = Time.captureFramerate;
            Time.captureFramerate = fps;
            Active = true;
            owner.RecorderMessage("Recording", $"{seconds} s at {fps} fps, {width}×{height}");
        }


        public void LateUpdate()
        {
            if (!Active) return;
            if (writeError != null && ffmpeg != null && ffmpeg.HasExited) { Debug.LogError("Fog of War: ffmpeg: " + writeError); Stop(); return; }
            // render the camera on its own, so the HUD and menus stay out of the clip
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            req.WaitForCompletion(); // frames must stay in order; captureFramerate absorbs the stall
            if (!req.hasError)
            {
                var data = req.GetData<byte>().ToArray();
                if (!queue.TryAdd(data, 2000)) { writeError = "the encoder stopped taking frames"; Stop(); return; }
            }
            frames++;
            if (frames >= maxFrames) Stop();
        }

        public void Stop()
        {
            if (!Active) return;
            Active = false;
            Time.captureFramerate = prevCaptureRate;
            int n = frames;
            var path = outPath; var proc = ffmpeg; var q = queue; var w = writer;
            bool mp4 = proc != null;
            owner.RecorderMessage("Finishing clip…", $"{n / (float)Fps:0.0} s");
            // finish encoding without blocking the game (quit waits for it: QA-066)
            CaptureRunner.BeginSave();
            new Thread(() =>
            {
                try
                {
                    q.CompleteAdding();
                    w?.Join();
                    if (proc != null) { proc.WaitForExit(120000); }
                }
                catch (Exception) { }
                finally { CaptureRunner.EndSave(); }
                string err = writeError;
                owner.Post(() =>
                {
                    if (mp4 && !File.Exists(path)) owner.RecorderMessage("Clip failed", err ?? "ffmpeg wrote no file");
                    else { LastOutput = path; }
                    if (!mp4 || File.Exists(path)) owner.RecorderMessage(mp4 ? "Clip saved" : "Frames saved (run encode.bat for MP4)", Path.GetFileName(path));
                    proc?.Dispose();
                });
            }) { IsBackground = true }.Start();
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            ffmpeg = null; writer = null; queue = null;
        }

        void Cleanup()
        {
            try { ffmpeg?.Kill(); } catch (Exception) { }
            ffmpeg = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            Active = false;
        }
    }
}
