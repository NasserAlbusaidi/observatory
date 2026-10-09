// Builds Resources/Ephem/Satellites.json for the Open System from JPL SSD tables:
//   https://ssd.jpl.nasa.gov/sats/elem/      mean elements referred to the local Laplace planes
//   https://ssd.jpl.nasa.gov/sats/phys_par/  GM and mean radius (where measured)
// The tables round each period to 4-5 digits, which would drift resonant moons by degrees per year.
// So for moons with periods under 100 days, the mean motion n and the mean longitude at J2000 are re-fitted to JPL
// Horizons positions over 2000-2100. The script then checks the Io-Europa-Ganymede Laplace angle.
//
//   node Tools/satellites/build.mjs
import fs from "node:fs";
import path from "node:path";

const ROOT = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Z]:)/, "$1")), "../..");
const CACHE = path.join(ROOT, "Tools/satellites/cache");
const OUT = path.join(ROOT, "Assets/FogOfWar/Resources/Ephem/Satellites.json");
const PLANETS = { Jupiter: "599", Saturn: "699", Uranus: "799", Neptune: "899", Mars: "499" };
const JD0 = 2451545.0, JD1 = 2488069.5;
// Each planet's spin pole by the right-hand rule (RA, Dec), for rows in the "equatorial" frame. Uranus's IAU north
// pole (257.311, -15.175) points against its spin, so it is flipped here.
const SPIN_POLE = { Mars: [317.681, 52.887], Jupiter: [268.057, 64.495], Saturn: [40.589, 83.537], Uranus: [77.311, 15.175], Neptune: [299.36, 43.46] };
fs.mkdirSync(CACHE, { recursive: true });

async function cached(name, url) {
  const f = path.join(CACHE, name);
  if (fs.existsSync(f)) return fs.readFileSync(f, "utf8");
  for (let i = 0; i < 4; i++) {
    try {
      const t = await (await fetch(url)).text();
      fs.writeFileSync(f, t);
      return t;
    } catch (e) { await new Promise(r => setTimeout(r, 2000 * 2 ** i)); }
  }
  throw new Error("fetch failed: " + url);
}

function table(html) {
  return [...html.matchAll(/<tr[^>]*>([\s\S]*?)<\/tr>/g)].map(m =>
    [...m[1].matchAll(/<t[dh][^>]*>([\s\S]*?)<\/t[dh]>/g)].map(c => c[1].replace(/<[^>]*>/g, " ").replace(/&nbsp;/g, " ").replace(/\s+/g, " ").trim()));
}

const elemHtml = await cached("elem.html", "https://ssd.jpl.nasa.gov/sats/elem/");
const physHtml = await cached("phys.html", "https://ssd.jpl.nasa.gov/sats/phys_par/");
const laplace = elemHtml.slice(elemHtml.indexOf("Orbital Reference Frame: Laplace"));
const phys = new Map();
for (const r of table(physHtml)) if (r.length >= 6 && /^\d+$/.test(r[2])) phys.set(r[2], { gm: parseFloat(r[3]), radius: parseFloat(r[4]) });

const moons = [];
for (const r of table(laplace)) {
  // ID, Planet, Satellite, Code, Ephemeris, Frame, Epoch, a, e, w, M, i, node, P, Papsis, Pnode, RA, Dec, Tilt, Ref
  if (r.length < 19 || !/^\d+$/.test(r[3]) || !(r[1] in PLANETS)) continue;
  const num = s => (s === "n/a" || s === "" ? null : parseFloat(s));
  const epoch = r[6].match(/(\d+)-(\d+)-(\d+(\.\d+)?)/);
  const y = +epoch[1], mo = +epoch[2], d = +epoch[3];
  const epochJd = Date.UTC(y, mo - 1, Math.floor(d)) / 86400000 + 2440587.5 + (d - Math.floor(d));
  const p = phys.get(r[3]);
  moons.push({
    name: r[2], parent: r[1], code: r[3], frame: r[5], epochJd,
    a: num(r[7]), e: num(r[8]), w: num(r[9]), M: num(r[10]), i: num(r[11]), node: num(r[12]),
    periodDays: num(r[13]), pApsisYears: num(r[14]), pNodeYears: num(r[15]),
    laplaceRa: num(r[16]), laplaceDec: num(r[17]),
    gm: p ? p.gm : 0, radiusKm: p ? p.radius : null, n: 360 / num(r[13]), calibrated: false,
  });
}
{
  const best = new Map();
  for (const m of moons) {
    const o = best.get(m.code);
    if (!o || (o.frame !== "Laplace" && m.frame === "Laplace") || (o.frame === m.frame && m.epochJd > o.epochJd)) best.set(m.code, m);
  }
  moons.length = 0; moons.push(...best.values());
}
for (const m of moons) {
  if (m.frame === "equatorial") [m.laplaceRa, m.laplaceDec] = SPIN_POLE[m.parent];
  else if (m.frame !== "Laplace") m.laplaceRa = m.laplaceDec = null;
}
console.log(`${moons.length} moons from the JPL tables`);

// --- calibrate n and the J2000 mean longitude against Horizons for the short-period moons -------------------------
async function horizons(code, center, stepMin, start, stop) {
  const q = new URLSearchParams({
    format: "text", COMMAND: `'${code}'`, EPHEM_TYPE: "VECTORS", CENTER: `'500@${center}'`, START_TIME: `'JD${start}'`,
    STOP_TIME: `'JD${stop}'`, STEP_SIZE: `'${stepMin} m'`, REF_PLANE: "ECLIPTIC", REF_SYSTEM: "ICRF", VEC_TABLE: "1",
    OUT_UNITS: "KM-S", CSV_FORMAT: "YES", OBJ_DATA: "NO", TIME_TYPE: "TDB",
  });
  const t = await cached(`h_${code}_${start}_${stop}_${stepMin}.txt`, "https://ssd.jpl.nasa.gov/api/horizons.api?" + q);
  if (!t.includes("$$SOE")) return null;
  return t.slice(t.indexOf("$$SOE") + 5, t.indexOf("$$EOE")).split("\n").map(l => l.split(",")).filter(f => f.length > 4).map(f => [+f[0], +f[2], +f[3], +f[4]]);
}

const rad = Math.PI / 180, OBL = 23.4392911 * rad;
const eqToEcl = v => [v[0], v[1] * Math.cos(OBL) + v[2] * Math.sin(OBL), -v[1] * Math.sin(OBL) + v[2] * Math.cos(OBL)];
const radec = (ra, de) => eqToEcl([Math.cos(de * rad) * Math.cos(ra * rad), Math.cos(de * rad) * Math.sin(ra * rad), Math.sin(de * rad)]);
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const norm = a => { const m = Math.hypot(...a); return a.map(x => x / m); };

for (const m of moons) {
  if (!(m.periodDays < 100)) continue;
  // Laplace frame: z = pole, x = node of the Laplace plane on the ICRF equator (as in Body.Kepler)
  const z = m.laplaceRa != null ? radec(m.laplaceRa, m.laplaceDec) : [0, 0, 1];
  const x = norm(cross(eqToEcl([0, 0, 1]), z)), y = cross(z, x);
  const stepMin = Math.max(10, Math.round((m.periodDays * 1440) / 12));
  const span = Math.min(JD1 - JD0, Math.floor((40000 * stepMin) / 1440)); // one request, ≤ 40k lines
  const rows = await horizons(m.code, PLANETS[m.parent], stepMin, JD0, JD0 + span);
  if (!rows || rows.length < 100) { console.log(`  ${m.name}: no Horizons data, table values kept`); continue; }
  // true longitude in the Laplace plane, unwrapped; mean longitude = linear fit (the equation of centre averages out)
  let prev = null, turns = 0;
  const ts = [], ls = [];
  for (const [jd, ...p] of rows) {
    let lam = Math.atan2(dot(p, y), dot(p, x)) / rad;
    if (prev !== null) { if (lam - prev < -180) turns++; else if (lam - prev > 180) turns--; }
    prev = lam;
    ts.push(jd - JD0); ls.push(lam + 360 * turns);
  }
  const n = ts.length, st = ts.reduce((a, b) => a + b) / n, sl = ls.reduce((a, b) => a + b) / n;
  let num = 0, den = 0;
  for (let k = 0; k < n; k++) { num += (ts[k] - st) * (ls[k] - sl); den += (ts[k] - st) ** 2; }
  let slope = num / den, L0 = sl - slope * st;
  if (m.i > 90) {
    // retrograde: the in-plane angle runs backwards; mean longitude L = Ω + u with u ≈ Ω − λ, so L = 2Ω − λ
    const node2000 = m.node + (m.pNodeYears ? -360 / (m.pNodeYears * 365.25) : 0) * (JD0 - m.epochJd);
    slope = -slope; L0 = 2 * node2000 - L0;
  }
  // table L at J2000 for comparison
  const nodePrec = m.pNodeYears ? -360 / (m.pNodeYears * 365.25) : 0;
  const tableL = m.node + m.w + m.M + m.n * (JD0 - m.epochJd);
  if (Math.abs(slope / (360 / m.periodDays) - 1) > 0.01) { console.log(`  ${m.name}: Horizons fit disagrees with the table (n ${slope.toFixed(3)}), table values kept`); continue; }
  m.n = slope; m.L2000 = ((L0 % 360) + 360) % 360; m.calibrated = true;
  console.log(`  ${m.name.padEnd(10)} n ${slope.toFixed(7)} °/d (table ${(360 / m.periodDays).toFixed(7)}), L2000 ${m.L2000.toFixed(2)}° (table ${(((tableL % 360) + 360) % 360).toFixed(2)}°), ${n} samples over ${(span / 365.25).toFixed(0)} yr`);
}

// Laplace resonance check: λ_Io − 3 λ_Europa + 2 λ_Ganymede ≈ 180°
const g = k => moons.find(m => m.name === k);
const [io, eu, ga] = ["Io", "Europa", "Ganymede"].map(g);
if (io && eu && ga && io.calibrated) {
  for (const yr of [0, 25, 50, 100]) {
    const d = yr * 365.25;
    const phi = io.L2000 + io.n * d - 3 * (eu.L2000 + eu.n * d) + 2 * (ga.L2000 + ga.n * d);
    console.log(`  Laplace angle at J2000+${yr} yr: ${(((phi % 360) + 360) % 360).toFixed(2)}°`);
  }
}

for (const m of moons) {
  m.dNode = m.pNodeYears ? -360 / (m.pNodeYears * 365.25) : 0;
  m.dPeri = m.dNode + (m.pApsisYears ? 360 / (m.pApsisYears * 365.25) : 0);
  if (m.calibrated) {
    const dt = JD0 - m.epochJd;
    m.node += m.dNode * dt; m.peri = m.node + m.w + (m.dPeri - m.dNode) * dt; m.L = m.L2000; m.epochJd = JD0;
  } else { m.peri = m.node + m.w; m.L = m.peri + m.M; }
}
fs.writeFileSync(OUT, JSON.stringify({ source: "JPL SSD sats/elem (Laplace) + sats/phys_par; n, L recalibrated to Horizons", moons }, null, 1));
console.log(`wrote ${OUT}`);
