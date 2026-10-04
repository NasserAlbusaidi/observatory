# Pole column (|b| small) in the lens table: does a table split at b = 0 (psi = 0 and pi), with columns crowding the
# split, follow the direct trace there? Only the table columns near psi = 0 and pi are traced. CPU only.
# python lens_pole.py [N]   (table N x N; default 1024)
import sys
import numpy as np
from lens_proto import camera, trace, dirs_from, to_uv, from_uv
from shadow_edge_check import analytic_edge

N = int(sys.argv[1]) if len(sys.argv) > 1 else 1024
M = N // 2
pos, a, fov, W, H = (0, 0.9, 24), 0.0, 42, 1920, 1080
c = camera(pos, a)
EDGE = analytic_edge(c, 8192)
def rc_of(psi):
    x = (np.asarray(psi) / (2 * np.pi) * 8192 - 0.5) % 8192
    i = np.floor(x).astype(int); f = x - i
    return EDGE[i] * (1 - f) + EDGE[(i + 1) % 8192] * f

# test pixels: the centre column +-12 px, sky rows (above and below the hole), as lens_proto's camera
rng = np.random.default_rng(5)
look = -np.array(pos, float); look /= np.linalg.norm(look)
right = np.cross([0, 1.0, 0], look); right /= np.linalg.norm(right); up = np.cross(look, right)
tf = np.tan(np.radians(fov) / 2); px = 2 * tf / H
nt = 6000
xs = (rng.uniform(-12, 12, nt)) * px
ys = np.where(rng.random(nt) < 0.5, rng.uniform(0.35, 1, nt), -rng.uniform(0.35, 1, nt)) * tf
r, mu, ph = c['r'], c['mu'], c['ph']; s_ = np.sqrt(r * r + a * a); st = np.sqrt(1 - mu * mu)
er = np.array([r / s_ * st * np.cos(ph), mu, r / s_ * st * np.sin(ph)]); er /= np.linalg.norm(er)
eth = np.array([s_ * mu * np.cos(ph), -r * st, s_ * mu * np.sin(ph)]); eth -= eth @ er * er; eth /= np.linalg.norm(eth)
eph = np.array([-np.sin(ph), 0, np.cos(ph)])
def tri(xs, ys):
    w = look[None] + right[None] * xs[:, None] + up[None] * ys[:, None]
    w /= np.linalg.norm(w, axis=1)[:, None]
    d = np.stack([w @ er, w @ eth, w @ eph], 1); return d / np.linalg.norm(d, axis=1)[:, None]
d0 = tri(xs, ys); d1 = tri(xs + px, ys)
ref = trace(c, d0, maxc=0); ref1 = trace(c, d1, maxc=0)
psi_t = np.arctan2(d0[:, 2], d0[:, 1]) % (2 * np.pi); rho_t = np.arccos(np.clip(-d0[:, 0], -1, 1))
_, v_t = to_uv(psi_t, rho_t, rc_of)
y = v_t * N - 0.5
j0 = np.clip(np.floor(y).astype(int), 0, N - 2); fy = np.clip(y - j0, 0, 1)
per_px = np.arccos(np.clip((ref['sky'] * ref1['sky']).sum(1), -1, 1))
esc = ref['fate'] == 1

def evaluate(name, col_psi, locate):
    """col_psi: psi of every table column; locate(psi) -> (i0, i1, fx) column indices and fraction."""
    i0, i1, fx = locate(psi_t)
    cols = np.unique(np.concatenate([i0, i1]))
    rows = np.unique(np.concatenate([j0, j0 + 1]))
    cc, rr = np.meshgrid(cols, rows)
    psi = col_psi[cc.ravel()]
    _, rho = from_uv(np.zeros_like(psi), (rr.ravel() + 0.5) / N, lambda p_: rc_of(psi))
    T = trace(c, dirs_from(psi, rho), maxc=0)
    ci = {v: k for k, v in enumerate(cols)}; ri = {v: k for k, v in enumerate(rows)}
    idx = lambda i, j: np.array([ri[jj] * len(cols) + ci[ii] for ii, jj in zip(i, j)])
    cells = [idx(i0, j0), idx(i1, j0), idx(i0, j0 + 1), idx(i1, j0 + 1)]
    wts = [(1 - fx) * (1 - fy), fx * (1 - fy), (1 - fx) * fy, fx * fy]
    fates = np.stack([T['fate'][k] for k in cells])
    ok = esc & (fates.min(0) == 1) & (fates.max(0) == 1)
    sk = sum(w[:, None] * T['sky'][k] for w, k in zip(wts, cells)); sk /= np.linalg.norm(sk, axis=1)[:, None]
    e = np.arccos(np.clip((sk * ref['sky']).sum(1), -1, 1)) / np.maximum(per_px, 1e-9)
    e = e[ok]
    print(f'{name:34s} sky error px: median {np.median(e):.3f}  p99 {np.percentile(e, 99):.3f}  >0.5 px {np.mean(e > 0.5) * 100:.2f}%  (n {ok.sum()}, {len(cols)} columns traced)')

# current: N columns uniform in psi, wrapping
uni_psi = (np.arange(N) + 0.5) / N * 2 * np.pi
def uni_loc(p):
    x = p / (2 * np.pi) * N - 0.5
    i0 = np.floor(x).astype(int); fx = x - i0
    return i0 % N, (i0 + 1) % N, fx
evaluate('uniform psi (current)', uni_psi, uni_loc)

# split at b = 0: each half has M columns with nodes at |b| ~ 1e-4 at both ends; warp beta crowds the ends
for beta in (0.0, 0.5, 1.0):
    t = np.arange(M) / (M - 1)
    x = (1 - beta) * t + beta * (0.5 - 0.5 * np.cos(np.pi * t))
    e0 = 1e-4
    half = np.pi * (e0 + x * (1 - 2 * e0))
    sp_psi = np.concatenate([half, np.pi + half])
    def sp_loc(p, beta=beta):
        h = (p >= np.pi).astype(int)
        xx = np.clip(((p - h * np.pi) / np.pi - e0) / (1 - 2 * e0), 0, 1)
        # invert x(t) by bisection (monotone)
        lo, hi = np.zeros_like(xx), np.ones_like(xx)
        for _ in range(40):
            m = 0.5 * (lo + hi)
            xm = (1 - beta) * m + beta * (0.5 - 0.5 * np.cos(np.pi * m))
            lo = np.where(xm < xx, m, lo); hi = np.where(xm < xx, hi, m)
        cc_ = 0.5 * (lo + hi) * (M - 1)
        i0 = np.clip(np.floor(cc_).astype(int), 0, M - 2); fx = np.clip(cc_ - i0, 0, 1)
        return h * M + i0, h * M + i0 + 1, fx
    evaluate(f'split at b=0, end warp beta={beta}', sp_psi, sp_loc)
