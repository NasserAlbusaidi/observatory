// Fits Chebyshev polynomials to JPL Horizons state vectors and writes Resources/Ephem/<Name>.bytes for the Open System.
//
//   node Tools/horizons/fit.mjs            (all bodies)
//   node Tools/horizons/fit.mjs Moon Mars  (some)
//
// Vectors: ecliptic J2000 (REF_PLANE=ECLIPTIC, REF_SYSTEM=ICRF), km, TDB. Planets are relative to the Sun's centre (@10),
// the Moon to Earth's centre (@399). Jupiter to Neptune are system barycentres (their centres wobble with their moons). Raw Horizons responses are cached in Tools/horizons/cache so re-fits are offline.
//
// File layout (little-endian): "FOWC" u32 version=1, f64 jdStart (TDB), f64 segDays, u32 nSeg, u32 nCoef,
// then nSeg × 3 × nCoef f64 Chebyshev coefficients (x, y, z), each segment mapped to τ ∈ [−1, 1].
import fs from "node:fs";
import path from "node:path";

const ROOT = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Z]:)/, "$1")), "../..");
const OUT = path.join(ROOT, "Assets/FogOfWar/Resources/Ephem");
const CACHE = path.join(ROOT, "Tools/horizons/cache");
const JD0 = 2451544.5;   // 2000-01-01 TDB
const JD1 = 2488069.5;   // 2100-01-01 TDB

// segment lengths and coefficient counts follow the JPL DE layout, which keeps fit errors well under a km
const BODIES = [
  { name: "Mercury", cmd: "199", center: "500@10", seg: 8, n: 14 },
  { name: "Venus", cmd: "299", center: "500@10", seg: 16, n: 12 },
  { name: "Earth", cmd: "399", center: "500@10", seg: 16, n: 13 },
  { name: "Moon", cmd: "301", center: "500@399", seg: 4, n: 13 },
  { name: "Mars", cmd: "499", center: "500@10", seg: 32, n: 12 },
  { name: "Jupiter", cmd: "5", center: "500@10", seg: 32, n: 10 },
  { name: "Saturn", cmd: "6", center: "500@10", seg: 32, n: 9 },
  { name: "Uranus", cmd: "7", center: "500@10", seg: 32, n: 8 },
  { name: "Neptune", cmd: "8", center: "500@10", seg: 32, n: 8 },
];

const MAX_LINES = 50000;

async function fetchChunk(b, startJd, stopJd, stepMin) {
  const key = `${b.name}_${startJd}_${stopJd}_${stepMin}.txt`;
  const file = path.join(CACHE, key);
  if (fs.existsSync(file)) return fs.readFileSync(file, "utf8");
  const q = new URLSearchParams({
    format: "text", COMMAND: `'${b.cmd}'`, EPHEM_TYPE: "VECTORS", CENTER: `'${b.center}'`,
    START_TIME: `'JD${startJd}'`, STOP_TIME: `'JD${stopJd}'`, STEP_SIZE: `'${stepMin} m'`,
    REF_PLANE: "ECLIPTIC", REF_SYSTEM: "ICRF", VEC_TABLE: "1", OUT_UNITS: "KM-S", CSV_FORMAT: "YES",
    OBJ_DATA: "NO", TIME_TYPE: "TDB",
  });
  for (let attempt = 0; attempt < 4; attempt++) {
    try {
      const res = await fetch("https://ssd.jpl.nasa.gov/api/horizons.api?" + q);
      const text = await res.text();
      if (!text.includes("$$SOE")) throw new Error(text.slice(0, 400));
      fs.mkdirSync(CACHE, { recursive: true });
      fs.writeFileSync(file, text);
      return text;
    } catch (e) {
      console.warn(`  ${b.name} chunk retry ${attempt + 1}: ${e.message.split("\n")[0]}`);
      await new Promise(r => setTimeout(r, 2000 * 2 ** attempt));
    }
  }
  throw new Error(`Horizons failed for ${b.name} ${startJd}`);
}

function parse(text) {
  const body = text.slice(text.indexOf("$$SOE") + 5, text.indexOf("$$EOE"));
  const rows = [];
  for (const line of body.split("\n")) {
    const f = line.split(",");
    if (f.length < 5) continue;
    rows.push([+f[0], +f[2], +f[3], +f[4]]);
  }
  return rows;
}

// Least-squares Chebyshev fit of degree n-1 to samples (t in [-1,1]).
function chebFit(ts, ys, n) {
  const m = ts.length;
  const A = ts.map(t => { const T = [1, t]; for (let k = 2; k < n; k++) T.push(2 * t * T[k - 1] - T[k - 2]); return T.slice(0, n); });
  // normal equations, solved with Gaussian elimination (n ≤ 14, well conditioned for Chebyshev on [-1,1])
  const N = Array.from({ length: n }, () => new Float64Array(n + 1));
  for (let i = 0; i < m; i++) for (let r = 0; r < n; r++) { for (let c = 0; c < n; c++) N[r][c] += A[i][r] * A[i][c]; N[r][n] += A[i][r] * ys[i]; }
  for (let p = 0; p < n; p++) {
    let best = p; for (let r = p + 1; r < n; r++) if (Math.abs(N[r][p]) > Math.abs(N[best][p])) best = r;
    [N[p], N[best]] = [N[best], N[p]];
    for (let r = 0; r < n; r++) if (r !== p) { const f = N[r][p] / N[p][p]; for (let c = p; c <= n; c++) N[r][c] -= f * N[p][c]; }
  }
  return Array.from({ length: n }, (_, k) => N[k][n] / N[k][k]);
}

function chebEval(c, t) { let b0 = 0, b1 = 0, b2 = 0; for (let k = c.length - 1; k >= 0; k--) { b2 = b1; b1 = b0; b0 = 2 * t * b1 - b2 + c[k]; } return (b0 - b2) / 2 + c[0] / 2; }

async function fitBody(b) {
  const stepMin = Math.round((b.seg * 1440) / (2.5 * b.n));
  const perChunkDays = Math.floor((MAX_LINES * stepMin) / 1440 / b.seg) * b.seg;
  const rows = [];
  for (let s = JD0; s < JD1; s += perChunkDays) {
    const e = Math.min(s + perChunkDays, JD1);
    const chunk = parse(await fetchChunk(b, s, e, stepMin));
    for (const r of chunk) if (!rows.length || r[0] > rows[rows.length - 1][0] + 1e-9) rows.push(r);
    process.stdout.write(`\r  ${b.name}: ${rows.length} samples`);
  }
  const nSeg = Math.round((JD1 - JD0) / b.seg);
  const coefs = new Float64Array(nSeg * 3 * b.n);
  let maxErr = 0, j = 0;
  for (let k = 0; k < nSeg; k++) {
    const a = JD0 + k * b.seg, z = a + b.seg;
    while (j > 0 && rows[j][0] > a) j--;
    while (j < rows.length && rows[j][0] < a - 1e-9) j++;
    const ts = [], xs = [[], [], []];
    for (let i = j; i < rows.length && rows[i][0] <= z + 1e-9; i++) {
      ts.push((2 * (rows[i][0] - a)) / b.seg - 1);
      for (let d = 0; d < 3; d++) xs[d].push(rows[i][d + 1]);
    }
    if (ts.length < b.n + 2) throw new Error(`${b.name}: segment ${k} has only ${ts.length} samples`);
    for (let d = 0; d < 3; d++) {
      const c = chebFit(ts, xs[d], b.n);
      coefs.set(c, (k * 3 + d) * b.n);
      for (let i = 0; i < ts.length; i++) maxErr = Math.max(maxErr, Math.abs(chebEval(c, ts[i]) - xs[d][i]));
    }
  }
  const head = Buffer.alloc(4 + 4 + 8 + 8 + 4 + 4);
  head.write("FOWC", 0, "ascii"); head.writeUInt32LE(1, 4); head.writeDoubleLE(JD0, 8); head.writeDoubleLE(b.seg, 16);
  head.writeUInt32LE(nSeg, 24); head.writeUInt32LE(b.n, 28);
  fs.mkdirSync(OUT, { recursive: true });
  fs.writeFileSync(path.join(OUT, b.name + ".bytes"), Buffer.concat([head, Buffer.from(coefs.buffer)]));
  console.log(`\r  ${b.name}: ${rows.length} samples, ${nSeg} segments × ${b.n} coefs, max fit error ${maxErr.toFixed(4)} km`);
}

const only = process.argv.slice(2);
for (const b of BODIES) if (!only.length || only.includes(b.name)) await fitBody(b);
