# Prototype of the Kerr Mino-time backward ray tracer planned for BlackHole.shader. Units M=1 (rs=2).
import numpy as np
def bl_from_cart(X, a):
    x,y,z = X
    R2 = x*x+y*y+z*z
    t = R2 - a*a
    r2 = 0.5*(t + np.sqrt(t*t + 4*a*a*y*y))
    r = np.sqrt(r2)
    mu = y/np.maximum(r,1e-9)
    ph = np.arctan2(z, x)
    return r, mu, ph
def triad(r, mu, ph, a):
    st = np.sqrt(1-mu*mu); s=np.sqrt(r*r+a*a)
    cp, sp = np.cos(ph), np.sin(ph)
    # x = s st cp, y = r mu, z = s st sp
    er = np.array([r/s*st*cp, mu, r/s*st*sp])
    et = np.array([s*mu*cp, -r*st, s*mu*sp])
    ep = np.array([-sp, 0*cp, cp])
    er = er/np.linalg.norm(er,axis=0)
    et = et - (et*er).sum(0)*er; et/=np.linalg.norm(et,axis=0)
    return er, et, ep
def init(cam, dirs, a, dt=np.float64):
    """cam: (3,) world pos in M units; dirs: (3,N) unit view dirs. Returns state and constants."""
    r, mu, ph = bl_from_cart(cam, a)
    er, et, ep = triad(r, mu, ph, a)
    dr = (dirs*er[:,None]).sum(0); dth=(dirs*et[:,None]).sum(0); dph=(dirs*ep[:,None]).sum(0)
    st2 = 1-mu*mu
    rho2 = r*r + a*a*mu*mu; rho=np.sqrt(rho2)
    D = r*r - 2*r + a*a
    S2 = (r*r+a*a)**2 - a*a*D*st2
    alpha = np.sqrt(rho2*D/S2); omega = 2*a*r/S2; varpi = np.sqrt(S2/rho2*st2)
    nr, nth, nph = -dr, -dth, -dph
    EF = 1/(alpha + omega*varpi*nph)
    pr = EF*rho*nr/np.sqrt(D); pth = EF*rho*nth
    b = EF*varpi*nph
    q = pth**2 + mu*mu*(b*b/st2 - a*a)
    st = dict(r=np.full_like(b, r), rp=-D*pr, mu=np.full_like(b, mu), mup=np.sqrt(st2)*pth, ph=np.full_like(b, ph))
    return st, b, q, EF, (r, mu, ph, er, et, ep)
def Rpot(r,b,q,a):
    P=r*r+a*a-a*b; return P*P-(r*r-2*r+a*a)*((b-a)**2+q)
def deriv(s, b, q, a):
    r, rp, mu, mup = s
    P = r*r + a*a - a*b
    K = (b-a)**2 + q
    D = r*r - 2*r + a*a
    rpp = 2*r*P - (r-1)*K
    mupp = (a*a - b*b - q)*mu - 2*a*a*mu**3
    php = -(a*P/D - a + b/np.maximum(1-mu*mu,1e-6))
    return np.array([rp, rpp, mup, mupp]), php
PROJ=0.0
def trace(cam, dirs, a, k=0.06, nmax=600, rmax=1000.0, disk=(None,None), record=False):
    st, b, q, EF, cinfo = init(cam, dirs, a)
    N = b.size
    s = np.array([st['r'], st['rp'], st['mu'], st['mup']])
    ph = st['ph'].copy()
    rh = 1+np.sqrt(1-a*a)
    alive = np.ones(N,bool); cap=np.zeros(N,bool); esc=np.zeros(N,bool)
    hits=[]  # list of (mask, r_hit, ph_hit, rp_hit, order)
    order=np.zeros(N,int)
    steps=np.zeros(N,int)
    for it in range(nmax):
        r, rp, mu, mup = s
        d, php = deriv(s,b,q,a)
        h = k/ (np.abs(rp)/r + np.sqrt(np.abs(d[1])/r) + np.abs(php) + np.abs(mup) + np.sqrt(np.abs(d[3])))
        h = np.where(alive, h, 0)
        # RK4 on (r, rp, mu, mup), phi alongside
        k1, p1 = d, php
        k2, p2 = deriv(s+0.5*h*k1, b,q,a)
        k3, p3 = deriv(s+0.5*h*k2, b,q,a)
        k4, p4 = deriv(s+h*k3, b,q,a)
        sn = s + h/6*(k1+2*k2+2*k3+k4)
        phn = ph + h/6*(p1+2*p2+2*p3+p4)
        cross = alive & (s[2]*sn[2] < 0)
        if cross.any():
            t = s[2]/(s[2]-sn[2])
            rh_ = s[0]+(sn[0]-s[0])*t; phh = ph+(phn-ph)*t; rph = s[1]+(sn[1]-s[1])*t
            hits.append((cross.copy(), rh_, phh, rph, order.copy()))
            order = order + cross
        if PROJ:
            Rn = Rpot(sn[0],b,q,a)
            proj = Rn > PROJ*sn[0]**4
            sn[1] = np.where(proj, np.sign(sn[1])*np.sqrt(np.maximum(Rn,0)), sn[1])
            Mn = q + (a*a-b*b-q)*sn[2]**2 - a*a*sn[2]**4
            pm = Mn > PROJ*(q+b*b+1)
            sn[3] = np.where(pm, np.sign(sn[3])*np.sqrt(np.maximum(Mn,0)), sn[3])
        s = np.where(alive, sn, s); ph = np.where(alive, phn, ph)
        steps += alive
        c = alive & (s[0] < rh + 0.02); cap |= c; alive &= ~c
        e = alive & (s[0] > rmax) & (s[1] > 0); esc |= e; alive &= ~e
        if not alive.any(): break
    return dict(s=s, ph=ph, cap=cap, esc=esc, alive=alive, hits=hits, b=b, q=q, EF=EF, steps=steps, cinfo=cinfo)

if __name__ == "__main__":
    # (c) null constraint at the camera
    rng=np.random.default_rng(1)
    for a in [0,0.5,0.998]:
        cam=np.array([20.0, 6.0, -30.0])
        dd=rng.normal(size=(3,2000)); dd/=np.linalg.norm(dd,axis=0)
        st,b,q,EF,_=init(cam,dd,a)
        R=Rpot(st['r'],b,q,a); 
        print(f"a={a}: max |rp^2-R|/R = {np.max(np.abs(st['rp']**2-R)/np.maximum(np.abs(R),1e-9)):.2e}, theta check min Theta={np.min(q - st['mu']**2*(b*b/(1-st['mu']**2)-a*a)):.3e}")
    # (a) Schwarzschild shadow from r = 48 M (24 rs), edge on
    for rc in [48.0, 18.0, 14.0]:
        cam=np.array([0,0,-rc])
        def edge(axis):
            lo,hi=0.0,0.6
            for _ in range(40):
                m=(lo+hi)/2
                d=np.array([[np.sin(m)],[0],[np.cos(m)]]) if axis==0 else np.array([[0],[np.sin(m)],[np.cos(m)]])
                o=trace(cam,d,0.0)
                if o['esc'][0]: hi=m
                else: lo=m
            return (lo+hi)/2
        ex=np.arcsin(np.sqrt(27)/rc*np.sqrt(1-2/rc))
        e0=edge(0); e1=edge(1)
        print(f"a=0 rcam={rc/2} rs: edge {np.degrees(e0):.4f}/{np.degrees(e1):.4f} deg, exact {np.degrees(ex):.4f}, err {100*(e0/ex-1):+.3f}%")
