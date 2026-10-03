"""Download the fidelity sources listed in sources.txt into D:\\SpaceSimArtSrc\\fidelity with parallel HTTP range
segments (the NASA hosts throttle per connection), resuming finished segments, then write SHA256SUMS.

    python Tools/fidelity/fetch.py [name-filter]
"""
import concurrent.futures as cf
import hashlib
import os
import sys
import time
import urllib.request

import fidelity_common as C

SEGMENTS = 8
HDR = {"User-Agent": "FogOfWar-fidelity/1.0"}


def size_of(url):
    req = urllib.request.Request(url, method="HEAD", headers=HDR)
    with urllib.request.urlopen(req, timeout=60) as r:
        return int(r.headers["Content-Length"]), r.geturl()


def fetch_range(url, part, a, b):
    have = os.path.getsize(part) if os.path.exists(part) else 0
    if have >= b - a + 1:
        return
    for attempt in range(6):
        try:
            req = urllib.request.Request(url, headers={**HDR, "Range": f"bytes={a + have}-{b}"})
            with urllib.request.urlopen(req, timeout=120) as r, open(part, "ab") as f:
                while True:
                    chunk = r.read(1 << 20)
                    if not chunk:
                        break
                    f.write(chunk)
            have = os.path.getsize(part)
            if have >= b - a + 1:
                return
        except Exception as e:
            print("retry", os.path.basename(part), e, flush=True)
            time.sleep(2 ** attempt)
            have = os.path.getsize(part) if os.path.exists(part) else 0
    raise RuntimeError("failed " + part)


def fetch(url):
    name = url.rsplit("/", 1)[1]
    dst = os.path.join(C.SRC_NEW, name)
    n, real = size_of(url)
    if os.path.exists(dst) and os.path.getsize(dst) == n:
        return name, n
    seg = -(-n // SEGMENTS)
    parts = []
    with cf.ThreadPoolExecutor(SEGMENTS) as ex:
        futs = []
        for i in range(SEGMENTS):
            a, b = i * seg, min(n, (i + 1) * seg) - 1
            if a > b:
                break
            part = f"{dst}.part{i}"
            parts.append(part)
            futs.append(ex.submit(fetch_range, real, part, a, b))
        for f in futs:
            f.result()
    with open(dst + ".tmp", "wb") as out:
        for p in parts:
            with open(p, "rb") as f:
                while chunk := f.read(1 << 24):
                    out.write(chunk)
    if os.path.getsize(dst + ".tmp") != n:
        raise RuntimeError("size mismatch " + name)
    os.replace(dst + ".tmp", dst)
    for p in parts:
        os.remove(p)
    print("done", name, n, flush=True)
    return name, n


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(1 << 24):
            h.update(chunk)
    return h.hexdigest()


if __name__ == "__main__":
    flt = sys.argv[1] if len(sys.argv) > 1 else ""
    urls = [u.strip() for u in open(os.path.join(os.path.dirname(__file__), "sources.txt"))
            if u.strip() and not u.startswith("#") and flt in u]
    os.makedirs(C.SRC_NEW, exist_ok=True)
    with cf.ThreadPoolExecutor(4) as ex:
        for name, n in ex.map(fetch, urls):
            pass
    with open(os.path.join(C.SRC_NEW, "SHA256SUMS"), "w") as f:
        for u in urls:
            name = u.rsplit("/", 1)[1]
            f.write(f"{sha(os.path.join(C.SRC_NEW, name))}  {name}  {u}\n")
    print("ALLDONE", flush=True)
