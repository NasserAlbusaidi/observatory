using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // A body's height pyramid from the terrain pack (Tools/terrain/pyramid.py): equirect int16 tiles cut from a real
    // DEM, level 0 = the product's own samples, each level above it box-averaged 2×. Heights are metres above a sphere
    // of RefRadiusKm. Tiles load on demand from any thread and are kept in an LRU cache.
    //
    // The pack is not in the build: the game looks for -fowterrain <dir>, then a Terrain folder next to the exe (or the
    // project). Without it, bodies fall back to their 8K global maps.
    public class TerrainData
    {
        public const int T = 256;
        [Serializable] class Level { public int w, h, tilesX, tilesY; }
        [Serializable] class Header { public string body, product, sourceFile, datum; public float nativeResolutionM, scale, refRadiusKm; public Level[] levels; }

        public readonly string Body, Product, Datum;  // datum: "sphere" (heights above RefRadiusKm) or "areoid"
        public readonly double RefRadiusKm, NativeKm;
        public int Levels => lv.Length;
        readonly Level[] lv;
        readonly double scaleKm;
        readonly string dir;
        readonly short[][] minMax;   // per level: (min, max) per tile, raw units
        readonly FileStream[] files;
        readonly object fileLock = new object();

        // LRU tile cache, shared by every thread that samples this body
        const int MaxTiles = 768;    // 768 × 128 KB ≈ 96 MB per body
        readonly Dictionary<long, LinkedListNode<(long key, short[] data)>> cache = new Dictionary<long, LinkedListNode<(long, short[])>>();
        readonly LinkedList<(long key, short[] data)> lru = new LinkedList<(long, short[])>();
        public int TilesLoaded { get; private set; }

        static string root;
        static bool rootSearched;

        public static string Root
        {
            get
            {
                if (rootSearched) return root;
                rootSearched = true;
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-fowterrain");
                var tries = new List<string>();
                // an explicit -fowterrain is the only place looked (-fowterrain none runs on the 8K globals, as a player without the pack does)
                if (i >= 0 && i + 1 < args.Length) tries.Add(args[i + 1]);
                else
                {
                    tries.Add(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Terrain"));
                    tries.Add(@"D:\SpaceSimArtSrc\terrain"); // the dev machine's pack
                }
                foreach (var t in tries) if (Directory.Exists(t)) { root = t; break; }
                Debug.Log("Fog of War: terrain pack " + (root ?? "not found (8K globals only)"));
                return root;
            }
        }

        public static TerrainData Open(string body)
        {
            var r = Root;
            if (r == null) return null;
            var d = Path.Combine(r, body);
            var hp = Path.Combine(d, "terrain.json");
            if (!File.Exists(hp)) return null;
            try { return new TerrainData(d, JsonUtility.FromJson<Header>(File.ReadAllText(hp))); }
            catch (Exception e) { Debug.LogError($"Fog of War: terrain pack for {body} unreadable: {e.Message}"); return null; }
        }

        TerrainData(string dir, Header h)
        {
            this.dir = dir;
            Body = h.body; Product = h.product; Datum = string.IsNullOrEmpty(h.datum) ? "sphere" : h.datum;
            RefRadiusKm = h.refRadiusKm;
            NativeKm = h.nativeResolutionM / 1000.0;
            scaleKm = h.scale / 1000.0;
            lv = h.levels;
            minMax = new short[lv.Length][];
            files = new FileStream[lv.Length];
            for (int n = 0; n < lv.Length; n++)
            {
                var b = File.ReadAllBytes(Path.Combine(dir, $"L{n}.mm"));
                minMax[n] = new short[b.Length / 2];
                Buffer.BlockCopy(b, 0, minMax[n], 0, b.Length);
            }
        }

        // Ground spacing at the equator of level n, km.
        public double SpacingKm(int n) => 2 * Math.PI * RefRadiusKm / lv[Math.Min(n, lv.Length - 1)].w;

        // Coarsest level whose spacing is still at or below the given spacing (finest data never exceeds level 0).
        public int LevelFor(double spacingKm)
        {
            int n = 0;
            while (n + 1 < lv.Length && SpacingKm(n + 1) <= spacingKm) n++;
            return n;
        }

        short[] Tile(int n, int tx, int ty)
        {
            long key = ((long)n << 40) | ((long)ty << 20) | (uint)tx;
            lock (cache)
            {
                if (cache.TryGetValue(key, out var node)) { lru.Remove(node); lru.AddFirst(node); return node.Value.data; }
            }
            var data = new short[T * T];
            var bytes = new byte[T * T * 2];
            lock (fileLock)
            {
                if (files[n] == null) files[n] = new FileStream(Path.Combine(dir, $"L{n}.bin"), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                files[n].Position = ((long)ty * lv[n].tilesX + tx) * T * T * 2;
                int got = 0;
                while (got < bytes.Length) { int r = files[n].Read(bytes, got, bytes.Length - got); if (r <= 0) break; got += r; }
            }
            Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
            lock (cache)
            {
                if (cache.TryGetValue(key, out var node)) return node.Value.data; // another thread won
                cache[key] = lru.AddFirst((key, data));
                TilesLoaded++;
                while (cache.Count > MaxTiles) { var last = lru.Last; lru.RemoveLast(); cache.Remove(last.Value.key); }
            }
            return data;
        }

        // One sample (raw units) at integer coordinates of level n: longitude wraps, latitude clamps.
        short Raw(int n, int x, int y, ref short[] tile, ref long tileKey)
        {
            var L = lv[n];
            x %= L.w; if (x < 0) x += L.w;
            y = y < 0 ? 0 : y >= L.h ? L.h - 1 : y;
            int tx = x / T, ty = y / T;
            long k = ((long)n << 40) | ((long)ty << 20) | (uint)tx;
            if (tile == null || k != tileKey) { tile = Tile(n, tx, ty); tileKey = k; }
            return tile[(y - ty * T) * T + (x - tx * T)];
        }

        // Height in km above the reference sphere at a body-frame direction (x prime meridian, y north, z east),
        // bilinear in level n. Thread-safe.
        public double HeightKm(int n, double dx, double dy, double dz)
        {
            short[] tile = null; long key = -1;
            return HeightKm(n, dx, dy, dz, ref tile, ref key);
        }

        public double HeightKm(int n, double dx, double dy, double dz, ref short[] tile, ref long key)
        {
            var L = lv[n];
            double lon = Math.Atan2(dz, dx), lat = Math.Asin(Math.Max(-1, Math.Min(1, dy / Math.Sqrt(dx * dx + dy * dy + dz * dz))));
            double u = 0.5 + lon / (2 * Math.PI), v = 0.5 - lat / Math.PI;
            double fx = u * L.w - 0.5, fy = v * L.h - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double ax = fx - x0, ay = fy - y0;
            double h00 = Raw(n, x0, y0, ref tile, ref key), h10 = Raw(n, x0 + 1, y0, ref tile, ref key);
            double h01 = Raw(n, x0, y0 + 1, ref tile, ref key), h11 = Raw(n, x0 + 1, y0 + 1, ref tile, ref key);
            return ((h00 * (1 - ax) + h10 * ax) * (1 - ay) + (h01 * (1 - ax) + h11 * ax) * ay) * scaleKm;
        }

        // Height range (km) over the tiles of level n that a lat/lon box touches, without loading tiles.
        public void RangeKm(int n, double latMin, double latMax, double lonMin, double lonMax, out double lo, out double hi)
        {
            var L = lv[n];
            int ty0 = Clamp((int)((0.5 - latMax / Math.PI) * L.h) / T, L.tilesY), ty1 = Clamp((int)((0.5 - latMin / Math.PI) * L.h) / T, L.tilesY);
            int tx0 = (int)Math.Floor((0.5 + lonMin / (2 * Math.PI)) * L.w / T), tx1 = (int)Math.Floor((0.5 + lonMax / (2 * Math.PI)) * L.w / T);
            if (tx1 - tx0 >= L.tilesX) { tx0 = 0; tx1 = L.tilesX - 1; }
            int mn = short.MaxValue, mx = short.MinValue;
            for (int ty = ty0; ty <= ty1; ty++)
                for (int t = tx0; t <= tx1; t++)
                {
                    int tx = ((t % L.tilesX) + L.tilesX) % L.tilesX, i = (ty * L.tilesX + tx) * 2;
                    mn = Math.Min(mn, minMax[n][i]); mx = Math.Max(mx, minMax[n][i + 1]);
                }
            lo = mn * scaleKm; hi = mx * scaleKm;
        }

        static int Clamp(int t, int n) => t < 0 ? 0 : t >= n ? n - 1 : t;

        public void Close()
        {
            lock (fileLock) for (int n = 0; n < files.Length; n++) { files[n]?.Dispose(); files[n] = null; }
            lock (cache) { cache.Clear(); lru.Clear(); }
        }
    }
}
