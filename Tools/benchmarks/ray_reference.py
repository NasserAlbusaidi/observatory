"""Independent separated quadrature/Jacobi ray reference, from Teo 2003 eqs. 5-6.

Uses radial quadrature and exact Jacobi polar motion, not theta/RK4/projection.
Regenerates escaped-ray directions at the same outer boundary. Polynomial roots
in references.py remain the independent classification oracle.
"""
import concurrent.futures
import json
import math
from pathlib import Path
import numpy as np
import scipy
from scipy.integrate import solve_ivp
from scipy.integrate import quad
from scipy.special import ellipj, ellipk, ellipkinc

PATH = Path(__file__).resolve().parent / "fixtures/rays.json"


def diagnostic_ivp(ray):
    # Development cross-check only, NOT the oracle: even tight IVP tolerances allowed invariant drift near the
    # prograde critical orbit at a=.998. The separated quadrature below enforces the constants exactly.
    if ray["expected"] != 1:
        return ray
    # Validate native arithmetic against the exact values representable by its uniforms/texture.
    a,b,q,rc,theta = [float(np.float32(ray[k])) for k in ("a","b","q","radius","theta")]
    u = math.cos(theta)
    def radial(r):
        return r**4+(a*a-b*b-q)*r*r+2*((a-b)**2+q)*r-a*a*q
    w = ray["radialSign"]*math.sqrt(radial(rc))
    udot = -math.sin(theta)*ray["polarSign"]*math.sqrt(max(0,q+u*u*(a*a-b*b/(1-u*u))))
    initial = [rc,w,u,udot,0]
    cap=1+math.sqrt(1-a*a)+0.001
    outer=max(400,8*rc)
    def rhs(lam,y):
        r,w,u,v,phi=y
        delta=r*r-2*r+a*a
        p=r*r+a*a-a*b
        phi_prime=-(a*p/delta-a+(b/max(1-u*u,1e-20) if abs(b)>1e-6 else 0))
        return [w,2*r**3+(a*a-b*b-q)*r+(a-b)**2+q,v,-(q+b*b-a*a)*u-2*a*a*u**3,phi_prime]
    def escape(lam,y): return y[0]-outer
    def capture(lam,y): return y[0]-cap
    def polar(lam,y): return y[3]
    escape.terminal=capture.terminal=True
    escape.direction=1
    capture.direction=-1
    result=solve_ivp(rhs,(0,30),initial,method="DOP853",rtol=2e-12,atol=2e-13,
                     events=(escape,capture,polar),max_step=0.005)
    if not result.success or not len(result.t_events[0]):
        raise RuntimeError(f"Independent reference did not escape: {ray} {result.message}")
    r,w,u,v,phi=result.y[:,-1]
    if abs(b)<1e-6:
        phi+=math.pi*sum(abs(event[2])>1-1e-7 for event in result.y_events[2])
    st=math.sqrt(max(1-u*u,1e-30))
    cp,sp=math.cos(phi),math.sin(phi)
    er=np.array([st*cp,u,st*sp])
    et=np.array([u*cp,-st,u*sp])
    ep=np.array([-sp,0,cp])
    d=er*w+et*(r*(-v/st))+ep*(r*st*rhs(0,result.y[:,-1])[4])
    d/=np.linalg.norm(d)
    ray.update(dirx=float(d[0]),diry=float(d[1]),dirz=float(d[2]),hasDirection=True)
    return ray


def quadrature(ray):
    """Separated radial quadrature and analytic Jacobi polar motion, avoiding IVP invariant drift."""
    if ray["radialSign"] != -1:
        raise ValueError("quadrature supports inbound fixture rays only (radialSign must be -1)")
    if ray["expected"] != 1:
        return ray
    a,b,q,rc,theta=[float(np.float32(ray[k])) for k in ("a","b","q","radius","theta")]
    c2=a*a-b*b-q
    c1=2*((a-b)**2+q)
    c0=-a*a*q
    roots=np.roots([1,0,c2,c1,c0])
    horizon=1+math.sqrt(1-a*a)
    rt=max(x.real for x in roots if abs(x.imag)<1e-8 and horizon<x.real<rc)
    outer=max(400,8*rc)
    # r=rt+x^2 cancels the turning-point square-root singularity exactly.
    def weight(x):
        r=rt+x*x
        quotient=((r+rt)*r+c2+rt*rt)*r+c1+rt*c2+rt**3
        return 2/math.sqrt(quotient)
    def radial_phi(x):
        r=rt+x*x
        return -(a*(r*r+a*a-a*b)/(r*r-2*r+a*a)-a)*weight(x)
    def both(fun):
        return sum(quad(fun,0,math.sqrt(r-rt),epsabs=1e-11,epsrel=1e-12,limit=300)[0] for r in (rc,outer))
    lam=both(weight)
    phi=both(radial_phi)
    k=q+b*b-a*a
    if a==0:
        up=q/(q+b*b)
        m=0
        omega=math.sqrt(q+b*b)
    else:
        d=math.sqrt(k*k+4*a*a*q)
        up=2*q/(k+d)
        um=(k+d)/(2*a*a)
        m=up/(up+um)
        omega=a*math.sqrt(up+um)
    amp=math.sqrt(up)
    u0=math.cos(theta)
    phase=ellipkinc(math.acos(max(-1,min(1,u0/amp))),m)
    # u' = -sin(theta)*theta'; positive theta' means decreasing u, i.e. positive elliptic phase.
    if ray['polarSign']<0:
        phase=-phase
    K=ellipk(m)
    def polar(l):
        sn,cn,dn,_=ellipj(phase+omega*l,m)
        return amp*cn,-amp*omega*sn*dn
    turns=[(2*j*K-phase)/omega for j in range(-100,101) if 0<(2*j*K-phase)/omega<lam]
    if abs(b)<1e-6:
        phi+=math.pi*len(turns)
    else:
        # Split at each polar extremum so narrow near-axis azimuth peaks cannot be skipped by quadrature.
        phi-=b*quad(lambda l: 1/max(1-polar(l)[0]**2,1e-20),0,lam,points=turns,
                    epsabs=1e-10,epsrel=1e-12,limit=500)[0]
    u,v=polar(lam)
    st=math.sqrt(max(1-u*u,1e-30))
    cp,sp=math.cos(phi),math.sin(phi)
    w=math.sqrt(outer**4+c2*outer*outer+c1*outer+c0)
    phdot=-(a*(outer*outer+a*a-a*b)/(outer*outer-2*outer+a*a)-a+(b/(1-u*u) if abs(b)>1e-6 else 0))
    direction=np.array([st*cp,u,st*sp])*w+np.array([u*cp,-st,u*sp])*(-outer*v/st)+np.array([-sp,0,cp])*(outer*st*phdot)
    direction/=np.linalg.norm(direction)
    ray.update(dirx=float(direction[0]),diry=float(direction[1]),dirz=float(direction[2]),hasDirection=True)
    return ray


if __name__ == "__main__":
    data=json.loads(PATH.read_text())
    with concurrent.futures.ProcessPoolExecutor(max_workers=4) as pool:
        data["rays"]=list(pool.map(quadrature,data["rays"],chunksize=16))
    data["directionReference"]=dict(source="Teo 2003 eqs 5-6; separated radial quadrature and Jacobi polar motion; float64",
                                   scipy=scipy.__version__,quadratureRelativeTolerance=1e-12,
                                   inputs="quantized to native float32 uniforms before independent float64 solve",
                                   boundary="outward r=max(400,8*observer radius), M=1")
    PATH.write_text(json.dumps(data,indent=2)+"\n")
    print(f"Retained {sum(r.get('hasDirection',False) for r in data['rays'])} independent escape directions")
