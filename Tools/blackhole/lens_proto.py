# Prototype for the black hole lens table (visuals step 2): does a per-camera table of ray results, looked up by the
# pixel's direction in the camera (ZAMO) frame, reproduce the direct trace?
#
# The table is indexed on the direction sphere around the hole: psi = angle around the direction to the hole, rho =
# angle from it, warped as s = sign(rho - rho_c) log(1 + |rho - rho_c| / eps) about the shadow edge rho_c(psi), so cells
# pile up at the critical curve and no cell straddles capture/escape. One table replaces the brief's four (b, q) sign
# tables: (psi, rho) fixes b, q and both initial signs. Per cell: fate, crossing count, up to 3 equator crossings
# (r, dphi, p_r, dtheta/dlambda) and the escape direction (unit vector, camera phi = 0).
#
# Tracer: a float64 numpy copy of BlackHole.shader's loop (theta form, Mino time, RK4, first-integral projection,
# Hermite equator crossings, b nudge). Usage: python lens_proto.py [N]   (table N x N, default 512)
import sys, time
import numpy as np

KSTEP, NSTEPS = 0.2, 1000      # Balanced

def camera(pos_rs, a):
    p = np.array(pos_rs, float) * 2
    x, y, z = p
    t = x * x + y * y + z * z - a * a
    r = np.sqrt(0.5 * (t + np.sqrt(t * t + 4 * a * a * y * y)))
    mu = y / r; th = np.arccos(mu); ph = np.arctan2(z, x)
    st = np.sqrt(1 - mu * mu)
    rho2 = r * r + a * a * mu * mu; D = r * r - 2 * r + a * a
    S2 = (r * r + a * a) ** 2 - a * a * D * st * st
    return dict(r=r, mu=mu, th=th, ph=ph, st=st, rho=np.sqrt(rho2), D=D, lapse=np.sqrt(rho2 * D / S2),
                omega=2 * a * r / S2, varpi=np.sqrt(S2 / rho2) * st, a=a)

def consts(c, d):
    """d: (N, 3) unit view directions in the camera triad (r, theta, phi). Returns b, q, EF, initial state."""
    a = c['a']
    n = -d
    EF = 1 / (c['lapse'] + c['omega'] * c['varpi'] * n[:, 2])
    pr = EF * c['rho'] * n[:, 0] / np.sqrt(c['D'])
    pth = EF * c['rho'] * n[:, 1]
    b = EF * c['varpi'] * n[:, 2]
    b = np.where(b < 0, -1, 1) * np.maximum(np.abs(b), 1e-4)
    q = pth ** 2 + c['mu'] ** 2 * (b * b / max(c['st'] ** 2, 1e-6) - a * a)
    s = np.stack([np.full(len(b), c['r']), -c['D'] * pr, np.full(len(b), c['th']), -pth], 1)
    return b, q, EF, s

def deriv(s, b, q, a):
    r = s[:, 0]; sn = np.sin(s[:, 2]); cs = np.cos(s[:, 2]); st = np.maximum(np.abs(sn), 1e-7)
    P = r * r + a * a - a * b; K = (b - a) ** 2 + q; D = r * r - 2 * r + a * a
    php = -(a * P / D - a + b / (st * st))
    rpp = 2 * r * P - (r - 1) * K
    thpp = b * b * cs / st ** 3 - a * a * sn * cs
    return np.stack([s[:, 1], rpp, s[:, 3], thpp], 1), php

def trace(c, d, maxc=3):
    a = c['a']; N = len(d)
    b, q, EF, s = consts(c, d)
    ph = np.full(N, c['ph'])
    rh = 1 + np.sqrt(1 - a * a); rcap = rh + 1e-3; rmax = max(400, 8 * c['r'])
    projT = 1e-3 * (q + b * b + 1)
    fate = np.full(N, 2)                     # 0 captured, 1 escaped, 2 out of steps
    cnt = np.zeros(N, int)
    X = np.full((N, maxc, 4), np.nan)        # r, dphi (from camera phi), p_r, dtheta/dlambda
    alive = np.arange(N)
    for k in range(NSTEPS):
        if len(alive) == 0: break
        S = s[alive]; B = b[alive]; Q = q[alive]; PH = ph[alive]
        k1, p1 = deriv(S, B, Q, a)
        h = KSTEP / (np.abs(S[:, 1]) / S[:, 0] + np.sqrt(np.abs(k1[:, 1]) / S[:, 0]) + np.abs(p1)
                     + np.abs(S[:, 3]) / np.maximum(np.abs(np.sin(S[:, 2])), 1e-7) + np.sqrt(np.abs(k1[:, 3])))
        H = h[:, None]
        k2, p2 = deriv(S + 0.5 * H * k1, B, Q, a)
        k3, p3 = deriv(S + 0.5 * H * k2, B, Q, a)
        k4, p4 = deriv(S + H * k3, B, Q, a)
        Sn = S + H / 6 * (k1 + 2 * k2 + 2 * k3 + k4)
        PHn = PH + h / 6 * (p1 + 2 * p2 + 2 * p3 + p4)
        P_ = Sn[:, 0] ** 2 + a * a - a * B
        Rn = P_ * P_ - (Sn[:, 0] ** 2 - 2 * Sn[:, 0] + a * a) * ((B - a) ** 2 + Q)
        m = Rn > 1e-3 * Sn[:, 0] ** 4
        Sn[m, 1] = np.where(Sn[m, 1] < 0, -1, 1) * np.sqrt(Rn[m])
        sn_, cs_ = np.sin(Sn[:, 2]), np.cos(Sn[:, 2])
        Tn = Q + cs_ ** 2 * (a * a - B * B / np.maximum(sn_ ** 2, 1e-14))
        m = Tn > projT[alive]
        Sn[m, 3] = np.where(Sn[m, 3] < 0, -1, 1) * np.sqrt(Tn[m])
        e0 = S[:, 2] - np.pi / 2; e1 = Sn[:, 2] - np.pi / 2
        cr = np.nonzero((e0 > 0) != (e1 > 0))[0]
        if len(cr):
            t = e0[cr] / (e0[cr] - e1[cr]); d0 = S[cr, 3] * h[cr]; d1 = Sn[cr, 3] * h[cr]
            E0, E1 = e0[cr], e1[cr]
            for _ in range(2):
                tt = t * t; ttt = tt * t
                f = (2 * ttt - 3 * tt + 1) * E0 + (ttt - 2 * tt + t) * d0 + (-2 * ttt + 3 * tt) * E1 + (ttt - tt) * d1
                fp = (6 * tt - 6 * t) * E0 + (3 * tt - 4 * t + 1) * d0 + (-6 * tt + 6 * t) * E1 + (3 * tt - 2 * t) * d1
                t = np.clip(t - f / np.where(np.abs(fp) > 1e-6, fp, 1e-6), 0, 1)
            t2 = t * t; t3 = t2 * t
            r0, r1 = S[cr, 0], Sn[cr, 0]
            rH = (2 * t3 - 3 * t2 + 1) * r0 + (t3 - 2 * t2 + t) * S[cr, 1] * h[cr] + (-2 * t3 + 3 * t2) * r1 + (t3 - t2) * Sn[cr, 1] * h[cr]
            rH = np.clip(rH, np.minimum(r0, r1), np.maximum(r0, r1))
            phH = PH[cr] + (PHn[cr] - PH[cr]) * t
            DH = np.maximum(rH * rH - 2 * rH + a * a, 1e-4)
            prH = -(S[cr, 1] + (Sn[cr, 1] - S[cr, 1]) * t) / DH
            thdH = S[cr, 3] + (Sn[cr, 3] - S[cr, 3]) * t
            gi = alive[cr]
            slot = cnt[gi]
            ok = slot < maxc
            X[gi[ok], slot[ok]] = np.stack([rH, phH - c['ph'], prH, thdH], 1)[ok]
            cnt[gi] += 1
        s[alive] = Sn; ph[alive] = PHn
        capd = Sn[:, 0] < rcap
        escd = (Sn[:, 0] > rmax) & (Sn[:, 1] > 0)
        fate[alive[capd]] = 0; fate[alive[escd]] = 1
        alive = alive[~(capd | escd)]
    # rays out of steps: the shader takes the sky if moving out
    oos = fate == 2
    fate[oos & (s[:, 1] > 0)] = 1
    fate[oos & (s[:, 1] <= 0)] = 0
    # escape direction (world, camera phi = 0)
    st, ct = np.sin(s[:, 2]), np.cos(s[:, 2]); dph = ph - c['ph']; sp, cp = np.sin(dph), np.cos(dph)
    er = np.stack([st * cp, ct, st * sp], 1); eth = np.stack([ct * cp, -st, ct * sp], 1); eph = np.stack([-sp, 0 * cp, cp], 1)
    _, p1 = deriv(s, b, q, a)
    v = er * s[:, 1:2] + eth * (s[:, 0] * s[:, 3])[:, None] + eph * (s[:, 0] * st * p1)[:, None]
    v /= np.linalg.norm(v, axis=1)[:, None]
    return dict(fate=fate, cnt=cnt, X=X, sky=v, b=b, q=q)

def dirs_from(psi, rho):
    return np.stack([-np.cos(rho), np.sin(rho) * np.cos(psi), np.sin(rho) * np.sin(psi)], 1)

def shadow_edge(c, npsi):
    psi = (np.arange(npsi) + 0.5) / npsi * 2 * np.pi
    lo = np.zeros(npsi); hi = np.full(npsi, 1.5)
    for _ in range(36):
        m = 0.5 * (lo + hi)
        f = trace(c, dirs_from(psi, m), maxc=0)['fate']
        cap = f == 0
        lo = np.where(cap, m, lo); hi = np.where(cap, hi, m)
    return psi, 0.5 * (lo + hi)

EPS = 2e-4
RTOL, PTOL = 1e9, 1e9   # four-cell agreement on the first crossing: relative radius, azimuth (rad)
def to_uv(psi, rho, rc_of):
    rc = rc_of(psi)
    inside = rho < rc
    v_in = 0.5 * (1 - np.log1p((rc - rho) / EPS) / np.log1p(rc / EPS))
    v_out = 0.5 + 0.5 * np.log1p((rho - rc) / EPS) / np.log1p((np.pi - rc) / EPS)
    return psi / (2 * np.pi) % 1.0, np.where(inside, v_in, v_out)

def from_uv(u, v, rc_of):
    psi = u * 2 * np.pi; rc = rc_of(psi)
    rin = rc - EPS * np.expm1((1 - 2 * v) * np.log1p(rc / EPS))
    rout = rc + EPS * np.expm1((2 * v - 1) * np.log1p((np.pi - rc) / EPS))
    return psi, np.where(v < 0.5, rin, rout)

def run(name, pos, a, fov, W, H, N, ntest):
    t0 = time.time()
    c = camera(pos, a)
    epsi, erc = shadow_edge(c, N)
    def rc_of(psi):
        x = (psi / (2 * np.pi) * N - 0.5) % N
        i = np.floor(x).astype(int); f = x - i
        return erc[i] * (1 - f) + erc[(i + 1) % N] * f
    # table at cell centres
    uu, vv = np.meshgrid((np.arange(N) + 0.5) / N, (np.arange(N) + 0.5) / N)
    psi, rho = from_uv(uu.ravel(), vv.ravel(), rc_of)
    T = trace(c, dirs_from(psi, rho))
    t1 = time.time()
    # test pixels: random pixels of a camera at pos looking at the hole
    rng = np.random.default_rng(3)
    look = -np.array(pos, float); look /= np.linalg.norm(look)
    upw = np.array([0, 1.0, 0]); right = np.cross(upw, look); right /= np.linalg.norm(right); up = np.cross(look, right)
    tf = np.tan(np.radians(fov) / 2); px = 2 * tf / H
    xs = rng.uniform(-1, 1, ntest) * W / H * tf; ys = rng.uniform(-1, 1, ntest) * tf
    def world_dirs(xs, ys):
        w = look[None] + right[None] * xs[:, None] + up[None] * ys[:, None]
        return w / np.linalg.norm(w, axis=1)[:, None]
    # camera triad in world (as Kerr.SetCamera)
    r, mu, ph = c['r'], c['mu'], c['ph']; s_ = np.sqrt(r * r + a * a); st = np.sqrt(1 - mu * mu)
    er = np.array([r / s_ * st * np.cos(ph), mu, r / s_ * st * np.sin(ph)]); er /= np.linalg.norm(er)
    eth = np.array([s_ * mu * np.cos(ph), -r * st, s_ * mu * np.sin(ph)]); eth -= eth @ er * er; eth /= np.linalg.norm(eth)
    eph = np.array([-np.sin(ph), 0, np.cos(ph)])
    def tri(w):
        d = np.stack([w @ er, w @ eth, w @ eph], 1); return d / np.linalg.norm(d, axis=1)[:, None]
    d0 = tri(world_dirs(xs, ys)); d1 = tri(world_dirs(xs + px, ys))
    ref = trace(c, d0); ref1 = trace(c, d1)
    # lookup
    psi_t = np.arctan2(d0[:, 2], d0[:, 1]) % (2 * np.pi); rho_t = np.arccos(np.clip(-d0[:, 0], -1, 1))
    u, v = to_uv(psi_t, rho_t, rc_of)
    x = u * N - 0.5; y = v * N - 0.5
    i0 = np.floor(x).astype(int); j0 = np.clip(np.floor(y).astype(int), 0, N - 2); fx = x - i0; fy = np.clip(y - j0, 0, 1)
    idx = lambda i, j: (j * N + (i % N))
    cells = [idx(i0, j0), idx(i0 + 1, j0), idx(i0, j0 + 1), idx(i0 + 1, j0 + 1)]
    wts = [(1 - fx) * (1 - fy), fx * (1 - fy), (1 - fx) * fy, fx * fy]
    fates = np.stack([T['fate'][cc] for cc in cells]); cnts = np.stack([np.minimum(T['cnt'][cc], 3) for cc in cells])
    agree = (fates.min(0) == fates.max(0)) & (cnts.min(0) == cnts.max(0))
    # in-plane camera: near-equatorial rays (|q| small) also fall back
    # the four cells must also see the same crossings: same first-crossing radius and azimuth within a tolerance
    # (a ray just above the near disc and one just below it can both cross once, at very different places)
    xr = np.stack([T['X'][cc, 0, 0] for cc in cells]); xp = np.stack([T['X'][cc, 0, 1] for cc in cells])
    spread = np.where(np.isnan(xr).all(0), 0, (np.nanmax(xr, 0) - np.nanmin(xr, 0)) / np.nanmin(np.abs(xr), 0))
    pspread = np.where(np.isnan(xp).all(0), 0, np.nanmax(xp, 0) - np.nanmin(xp, 0))
    fb = ~agree | (np.abs(ref['q']) < 1e-3) | (spread > RTOL) | (pspread > PTOL)
    def interp(arr):
        return sum(w[:, None] * arr[cc] for w, cc in zip(wts, cells)) if arr.ndim == 2 else sum(w * arr[cc] for w, cc in zip(wts, cells))
    ok = ~fb
    out = dict(name=name, build_s=round(t1 - t0, 1), fallback=fb.mean())
    # fate / count mismatch where the lookup is used
    out['fate_wrong'] = (fates[0][ok] != ref['fate'][ok]).mean()
    out['count_wrong'] = (np.minimum(ref['cnt'], 3)[ok] != cnts[0][ok]).mean()
    # sky direction error in pixels
    sk = interp(T['sky']); sk /= np.linalg.norm(sk, axis=1)[:, None]
    esc = ok & (ref['fate'] == 1) & (fates[0] == 1)
    ang = np.arccos(np.clip((sk[esc] * ref['sky'][esc]).sum(1), -1, 1))
    per_px = np.arccos(np.clip((ref['sky'][esc] * ref1['sky'][esc]).sum(1), -1, 1))
    epx = ang / np.maximum(per_px, 1e-9)
    out['sky_err_px_median'] = np.median(epx); out['sky_err_px_p99'] = np.percentile(epx, 99)
    # first crossing position error, in screen pixels: map the error in (r, r*phi) through the image Jacobian
    d2 = tri(world_dirs(xs, ys + px)); ref2 = trace(c, d2)
    hasx = ok & (ref['cnt'] >= 1) & (cnts[0] >= 1) & (ref1['cnt'] >= 1) & (ref2['cnt'] >= 1)
    def xy(X): return np.stack([X[:, 0], X[:, 0] * X[:, 1]], 1)
    rt = sum(w[:, None] * T['X'][cc, 0, :2] for w, cc in zip(wts, cells))
    P0 = xy(ref['X'][hasx, 0, :2]); Pt = xy(rt[hasx]); Px = xy(ref1['X'][hasx, 0, :2]); Py = xy(ref2['X'][hasx, 0, :2])
    J = np.stack([Px - P0, Py - P0], 2)                      # (n, 2, 2): columns = per-pixel change
    good = np.abs(np.linalg.det(J)) > 1e-12
    e = np.linalg.solve(J[good], (Pt - P0)[good][:, :, None])[:, :, 0]
    epx = np.linalg.norm(e, axis=1)
    out['hit_rel_err_median'] = np.median(np.abs(rt[hasx, 0] - ref['X'][hasx, 0, 0]) / ref['X'][hasx, 0, 0])
    out['hit_err_px_median'] = np.median(epx); out['hit_err_px_p99'] = np.percentile(epx, 99)
    out['hit_err_px_frac_over_0.5'] = (epx > 0.5).mean()
    # what a viewer sees: a disc-like intensity at the hit, F(r) g^4 with a texture in phi (as the shader's scales)
    def shade(r, ph, bb):
        x = np.sqrt(r / 6); F = np.where(r > 6, (1 - 1 / x) / r ** 3, 0)
        om = 1 / r ** 1.5; ut = r ** 0.75 / np.sqrt(np.maximum(r ** 1.5 - 3 * r ** 0.5, 1e-9)) * 1 / r ** 0.75 * r ** 0.75
        g = 1 / np.maximum(ut * (1 - om * bb), 1e-4)
        return F * g ** 4 * (1 + 0.3 * np.sin(6 * ph + 1.3 * r) * np.sin(2 * r))
    bb = ref['b'][hasx]
    Iref = shade(ref['X'][hasx, 0, 0], ref['X'][hasx, 0, 1], bb); Itab = shade(rt[hasx, 0], rt[hasx, 1], bb)
    scale = np.percentile(Iref[Iref > 0], 95) if (Iref > 0).any() else 1
    dI = np.abs(Itab - Iref) / scale
    out['shade_err_mean_pct'] = dI.mean() * 100; out['shade_err_p99_pct'] = np.percentile(dI, 99) * 100
    out['shade_err_frac_over_1pct'] = (dI > 0.01).mean()
    out['pixels_with_disc_crossing'] = (ref['cnt'] >= 1).mean()
    # grazing-crossing fallback (visuals thread): mu = sqrt(q) / (r E_emit) at the first crossing below 0.05
    def mu_of(r, bb, qq):
        om = 1 / r ** 1.5; ut = 1 / np.sqrt(np.maximum(1 - 3 / r, 1e-6)); Eem = ut * (1 - om * bb)
        return np.sqrt(np.maximum(qq, 0)) / (r * np.maximum(Eem, 1e-4))
    r_tab0 = sum(w * T['X'][cc, 0, 0] for w, cc in zip(wts, cells))
    mu_all = np.where(np.isnan(r_tab0), 1.0, mu_of(np.nan_to_num(r_tab0, nan=10.0), ref['b'], ref['q']))
    for MU in (0.03, 0.05, 0.08):
        graze = mu_all < MU
        fbm = fb | graze
        out[f'mu<{MU}: total_fallback'] = fbm.mean()
        keep = ~graze[hasx][good]
        out[f'mu<{MU}: hit_err_px_p99'] = np.percentile(epx[keep], 99); out[f'mu<{MU}: hit_err_px_frac_over_0.5'] = (epx[keep] > 0.5).mean()
        k2 = ~graze[hasx]
        out[f'mu<{MU}: shade_err_mean_pct'] = dI[k2].mean() * 100; out[f'mu<{MU}: shade_err_frac_over_1pct'] = (dI[k2] > 0.01).mean()
    for Q in (3, 10):
        keep = np.abs(ref['q'][hasx]) >= Q
        out[f'q>={Q}: total_fallback'] = (fb | (np.abs(ref['q']) < Q)).mean()
        out[f'q>={Q}: shade_err_p99_pct'] = np.percentile(dI[keep], 99) * 100
        out[f'q>={Q}: shade_err_frac_over_1pct'] = (dI[keep] > 0.01).mean()
        kk = np.zeros(hasx.sum(), bool); kk[np.nonzero(good)[0]] = True
        e_keep = epx[keep[kk]] if False else epx[np.abs(ref['q'][hasx][good]) >= Q]
        out[f'q>={Q}: hit_err_px_frac_over_0.5'] = (e_keep > 0.5).mean(); out[f'q>={Q}: hit_err_px_p99'] = np.percentile(e_keep, 99)
    rel = np.abs(rt[hasx, 0] - ref['X'][hasx, 0, 0]) / ref['X'][hasx, 0, 0]
    dphi = np.abs(rt[hasx, 1] - ref['X'][hasx, 0, 1])
    out['hit_rel_err_p99'] = np.percentile(rel, 99); out['hit_dphi_err_p99'] = np.percentile(dphi, 99)
    big = np.zeros(hasx.sum(), bool); big[np.nonzero(good)[0]] = epx > 0.5
    # where the big ones are: condition number of the Jacobian, distance to the shadow edge
    cond = np.linalg.cond(J)
    out['big_cond_median'] = np.median(cond[big]) if big.any() else 0; out['all_cond_median'] = np.median(cond)
    out['big_rel_err_median'] = np.median(rel[big]) if big.any() else 0
    qh = np.abs(ref['q'][hasx][good]); bigg = epx > 0.5
    for Q in (1e-3, 0.1, 1, 3, 10, 30):
        print(f'    |q|<{Q}: fallback +{(np.abs(ref["q"]) < Q).mean():.3f} of all pixels; big errors left {(bigg & (qh >= Q)).mean():.4f} of crossing pixels; p99 px {np.percentile(epx[qh >= Q], 99) if (qh>=Q).any() else 0:.2f}')
    out['big_dist_edge_px_median'] = np.median(np.abs(rho_t[hasx][big] - rc_of(psi_t[hasx][big])) / px) if big.any() else 0
    esk = ang / np.maximum(per_px, 1e-9)
    # how far from the shadow edge the bad ones are
    bad = esc.copy(); bad[esc] = esk > 0.5
    out['sky_bad_frac'] = bad.sum() / max(esc.sum(), 1)
    out['sky_bad_rho_from_edge_px_median'] = np.median(np.abs(rho_t[bad] - rc_of(psi_t[bad])) / px) if bad.any() else 0
    for kk, vv_ in out.items(): print(f'  {kk}: {vv_:.4g}' if isinstance(vv_, float) else f'  {kk}: {vv_}')
    return out

if __name__ == '__main__':
    N = int(sys.argv[1]) if len(sys.argv) > 1 else 512
    NT = int(sys.argv[2]) if len(sys.argv) > 2 else 100000
    print('default view (spin 0, 24 rs, 42 deg, 1920x1080)')
    run('default', (0, 0.9, 24), 0.0, 42, 1920, 1080, N, NT)
    print('bh-kerr-0.998')
    run('kerr998', (0, 0.9, 24), 0.998, 42, 1920, 1080, N, NT)
