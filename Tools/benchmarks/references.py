"""Regenerate independent retained fixtures. Python 3 + numpy; network only for Horizons.

The GPU geometry oracle uses roots of the radial polynomial (Teo 2003, eq. 5b),
not a transcription of the production stepper. Critical rays come from eq. 11.
"""
import argparse
import concurrent.futures
import datetime
import hashlib
import json
from pathlib import Path
import re
import struct
import time
import urllib.parse
import urllib.request
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Tools/benchmarks/fixtures"


def geometry():
    rays = []
    for a in (0.0, 0.5, 0.9, 0.998):
        for deg in (1, 30, 60, 89):
            th = np.radians(deg)
            if a == 0:
                chi = np.linspace(0, 2 * np.pi, 4096, endpoint=False)
                bs, qs = np.sqrt(27) * np.sin(chi), 27 * np.cos(chi) ** 2
            else:
                r = np.linspace(2*(1+np.cos(2/3*np.arccos(-a))) + 1e-6,
                                2*(1+np.cos(2/3*np.arccos(a))) - 1e-6, 8192)
                bs = -(r**3-3*r*r+a*a*r+a*a)/(a*(r-1))
                qs = r**3*(4*a*a-r*(r-3)**2)/(a*a*(r-1)**2)
            polar = qs - np.cos(th)**2 * (bs*bs / np.sin(th)**2 - a*a)
            valid = np.flatnonzero(polar > 1e-5)
            for idx in valid[np.linspace(0, len(valid)-1, 24).astype(int)]:
                for scale in (0.99, 0.999, 1.001, 1.01):
                    b, q = float(bs[idx]*scale), float(qs[idx]*scale*scale)
                    polar2 = q-np.cos(th)**2*(b*b/np.sin(th)**2-a*a)
                    if polar2 < 0:
                        continue
                    for rc in (12.0, 48.0):
                        roots = np.roots([1, 0, a*a-b*b-q, 2*((a-b)**2+q), -a*a*q])
                        turning = [r.real for r in roots if abs(r.imag) < 1e-7 and
                                   1+np.sqrt(1-a*a) < r.real < rc]
                        rays.append(dict(a=a, theta=float(th), radius=rc, b=b, q=q,
                                         polarSign=1 if idx % 2 else -1, radialSign=-1,
                                         expected=1 if turning else 0, criticalOffset=scale-1))
    payload = dict(source="Teo 2003 equations 5b and 11; polynomial roots, no ray integration",
                   url="https://www.physics.nus.edu.sg/wp-content/uploads/sites/5/2019/10/paper.pdf",
                   generator="references.py geometry", numpy=np.__version__, precision="float64",
                   rays=rays)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/"rays.json").write_text(json.dumps(payload, indent=2)+"\n")
    print(f"Retained {len(rays)} independent capture/escape fixtures")


def horizons():
    spec = (ROOT/"Tools/horizons/fit.mjs").read_text()
    bodies = re.findall(r'name: "([^"]+)", cmd: "([^"]+)", center: "([^"]+)"', spec)
    rawdir = OUT/"horizons-raw"
    rawdir.mkdir(parents=True, exist_ok=True)

    def fetch(b):
        name, cmd, center = b
        binary = (ROOT/f"Assets/FogOfWar/Resources/Ephem/{name}.bytes").read_bytes()
        _, _, start, seg, count, _ = struct.unpack("<4sIddII", binary[:32])
        end = start + seg * min(count, int((2488069.5-start)/seg))
        boundary = start+seg*300
        dates = [start, start+seg/2, boundary-1e-3, boundary, boundary+1e-3,
                 2461316.5, end-seg/2, end-1e-3, end]
        args = dict(format="text", COMMAND=f"'{cmd}'", CENTER=f"'{center}'", EPHEM_TYPE="VECTORS",
                    TLIST="'"+",".join(map(str, dates))+"'", REF_PLANE="ECLIPTIC", REF_SYSTEM="ICRF",
                    VEC_TABLE="2", OUT_UNITS="KM-S", CSV_FORMAT="YES", VEC_CORR="NONE", TIME_TYPE="TDB")
        url = "https://ssd.jpl.nasa.gov/api/horizons.api?"+urllib.parse.urlencode(args)
        rawfile = rawdir/(name+".txt")
        if rawfile.exists():
            raw = rawfile.read_text()
        else:
            for attempt in range(4):
                try:
                    with urllib.request.urlopen(url, timeout=30) as response:
                        raw = response.read().decode()
                    break
                except Exception:
                    print(f"Retry {name} ({attempt+1}/4)", flush=True)
                    if attempt == 3:
                        raise
                    time.sleep(2**attempt)
            if "$$SOE" not in raw:
                raise RuntimeError(f"Horizons {name}: {raw[:600]}")
            rawfile.write_text(raw)
        vectors = []
        for line in raw.split("$$SOE")[1].split("$$EOE")[0].splitlines():
            cols = line.split(",")
            if len(cols) < 8:
                continue
            nums = [float(x) for x in cols[2:8]]
            vectors.append(dict(body=name, jd=float(cols[0]), x=nums[0], y=nums[1], z=nums[2],
                                vx=nums[3], vy=nums[4], vz=nums[5], outside=float(cols[0]) >= end))
        version = [line.strip() for line in raw.splitlines() if "source:" in line or "JPL/HORIZONS" in line]
        return dict(body=name, query=url, responseSha256=hashlib.sha256(raw.encode()).hexdigest(),
                    fitSha256=hashlib.sha256(binary).hexdigest(), sourceVersion=version, vectors=vectors)

    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
        records = list(executor.map(fetch, bodies))
    payload = dict(retrievedUTC=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   frame="ICRF ecliptic J2000, geometric, parent-centred", timescale="TDB", units="km, km/s",
                   provenance=records, vectors=[v for b in records for v in b["vectors"]])
    (OUT/"ephemeris.json").write_text(json.dumps(payload, indent=2)+"\n")
    print(f"Retained {len(payload['vectors'])} Horizons vectors for {len(records)} fitted bodies")


def fit_source(cache):
    """Retain actual fitting inputs, including their original service header and hash.

    A fresh Horizons integration is also retained separately: even an unchanged
    small-body solution number does not guarantee identical long-range vectors.
    Do not silently relabel source-version differences as Chebyshev fit error.
    """
    cache = Path(cache)
    records, vectors = [], []
    for binary in sorted((ROOT/"Assets/FogOfWar/Resources/Ephem").glob("*.bytes")):
        name = binary.stem
        data = binary.read_bytes()
        _, _, start, seg, count, _ = struct.unpack("<4sIddII", data[:32])
        end = start + seg * min(count, int((2488069.5-start)/seg))
        wanted = [start, start+seg/2, start+seg*300-0.001, start+seg*300, start+seg*300+0.001,
                  2461316.5, end-seg/2, end-0.001]
        rows = []
        headers = []
        for source in sorted(cache.glob(name+"_*.txt")):
            text = source.read_text()
            if "$$SOE" not in text:
                continue
            headers.append(dict(file=source.name, sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                                header=text.split("$$SOE")[0]))
            for line in text.split("$$SOE")[1].split("$$EOE")[0].splitlines():
                f = line.split(",")
                if len(f) >= 5:
                    j = float(f[0])
                    if start <= j < end:
                        rows.append((j,*map(float,f[2:5])))
        if not rows:
            raise RuntimeError(f"Missing original fit inputs for {name}")
        chosen = sorted(set(min(rows,key=lambda r:abs(r[0]-jd)) for jd in wanted))
        for row in chosen:
            vectors.append(dict(body=name,jd=row[0],x=row[1],y=row[2],z=row[3]))
        records.append(dict(body=name,fitSha256=hashlib.sha256(data).hexdigest(),sources=headers))
    payload = dict(reference="Actual retained JPL Horizons fitting inputs; nearest available original samples to boundary/interior fixtures",
                   frame="ICRF ecliptic J2000 geometric parent-centred", timescale="TDB", units="km",
                   provenance=records,vectors=vectors)
    (OUT/"fit-inputs.json").write_text(json.dumps(payload,indent=2)+"\n")
    print(f"Retained {len(vectors)} original fit-input vectors for {len(records)} bodies")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("suite", choices=["geometry", "horizons", "fit-source"])
    parser.add_argument("--cache", help="original fit.mjs cache directory, required for fit-source")
    args = parser.parse_args()
    if args.suite == "fit-source":
        if not args.cache:
            parser.error("fit-source requires --cache")
        fit_source(args.cache)
    else:
        {"geometry": geometry, "horizons": horizons}[args.suite]()
