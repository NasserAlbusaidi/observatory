# Checks for review #35, #38, #41 with the Python twin of KerrGeo.cs.
import math
exec(open('geo_test.py').read().split('for a in [0,0.9]')[0])
def precession(rp, ra, a, hscale=1.0):
    g = bound(rp, ra, a); peri = []
    while len(peri) < 3:
        w0, ph0 = g.w, g.phi
        g.step(a, g.hstep(a) * hscale)
        if w0 < 0 and g.w >= 0:
            f = -w0 / (g.w - w0); peri.append(ph0 + f * (g.phi - ph0))
    return math.degrees(peri[2] - peri[1] - 2 * math.pi)
print("14x60 a=0 precession", precession(14, 60, 0), "(review exact 60.15)")
print("14x60 a=0.9 precession", precession(14, 60, 0.9), "(review exact 43.8)")
def gl(n):
    xs, ws = [], []
    for i in range(n):
        z = math.cos(math.pi * (i + 0.75) / (n + 0.5))
        for _ in range(100):
            p0, p1 = 1.0, 0.0
            for j in range(1, n + 1): p2 = p1; p1 = p0; p0 = ((2*j-1)*z*p1 - (j-1)*p2)/j
            dp = n*(z*p0 - p1)/(z*z - 1); dz = p0/dp; z -= dz
            if abs(dz) < 1e-15: break
        xs.append(z); ws.append(2/((1-z*z)*dp*dp))
    return xs, ws
X, W = gl(32)
def tau(r, E, L):
    s = 0
    for x, w in zip(X, W):
        xx = 0.5*r*(x+1); v = E*E - (1-2/xx)*(1+L*L/(xx*xx))
        if v > 0: s += w/math.sqrt(v)
    return 0.5*r*s
print("tau E=1 L=0 from r=2:", tau(2, 1, 0), "(4/3 = 1.333)")
print("tau E=0.955 L=3.6 from r=2:", tau(2, 0.955, 3.6), "(review 0.687)")
for sgn in (+1, -1):
    rph = 3.0; g = Geo(r=rph*(1+sgn*1e-8), w=0, E=1, L=rph*rph/math.sqrt(rph*rph-2*rph), photon=True)
    n = 0
    while 2.05 < g.r < 50 and n < 200000: g.step(0, g.hstep(0)); n += 1
    print("photon", "+1e-8" if sgn > 0 else "-1e-8", "turns", g.phi/(2*math.pi), "ends r", g.r)
