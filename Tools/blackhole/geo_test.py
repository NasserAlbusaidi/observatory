# Python twin of KerrGeo.cs for checking: circular orbits stay circular, precession matches GR, photon orbit is unstable.
import math
class Geo:
    def __init__(s,**k):
        s.r=s.w=s.phi=s.t=s.tau=0.0;s.E=s.L=0.0;s.photon=False;s.__dict__.update(k)
    def mu2(s): return 0 if s.photon else 1
    def R(s,r,a):
        P=s.E*(r*r+a*a)-a*s.L;K=s.L-a*s.E;return P*P-(r*r-2*r+a*a)*(s.mu2()*r*r+K*K)
    def D(s,r,w,a):
        P=s.E*(r*r+a*a)-a*s.L;K=s.L-a*s.E;Dl=r*r-2*r+a*a
        return w, 2*r*s.E*P-(r-1)*(s.mu2()*r*r+K*K)-Dl*s.mu2()*r, a*P/Dl+K, (r*r+a*a)*P/Dl-a*(a*s.E-s.L), r*r
    def step(s,a,h):
        k1=s.D(s.r,s.w,a);k2=s.D(s.r+.5*h*k1[0],s.w+.5*h*k1[1],a);k3=s.D(s.r+.5*h*k2[0],s.w+.5*h*k2[1],a);k4=s.D(s.r+h*k3[0],s.w+h*k3[1],a)
        d=[(k1[i]+2*k2[i]+2*k3[i]+k4[i])/6*h for i in range(5)]
        s.r+=d[0];s.w+=d[1];s.phi+=d[2];s.t+=d[3];s.tau+=d[4]
        Rn=s.R(s.r,a)
        if Rn>1e-6*s.r**4: s.w=math.copysign(math.sqrt(Rn),s.w)
    def hstep(s,a,k=0.08):
        d=s.D(s.r,s.w,a);return k/(abs(s.w)/s.r+math.sqrt(abs(d[1])/s.r)+abs(d[2])+1e-9)
def circ(r,a):
    sr=math.sqrt(r);den=r**0.75*math.sqrt(r*sr-3*sr+2*a)
    return Geo(r=r,E=(r*sr-2*sr+a)/den,L=(r*r-2*a*sr+a*a)/den)
def bound(rp,ra,a):
    p=2*rp*ra/(rp+ra);e=(ra-rp)/(ra+rp);g=Geo(E=math.sqrt(1-(1-e*e)/p),L=math.sqrt(p))
    for it in range(40):
        f1=g.R(rp,a);f2=g.R(ra,a);eps=1e-7
        gE=Geo(E=g.E+eps,L=g.L);gL=Geo(E=g.E,L=g.L+eps)
        a11=(gE.R(rp,a)-f1)/eps;a12=(gL.R(rp,a)-f1)/eps;a21=(gE.R(ra,a)-f2)/eps;a22=(gL.R(ra,a)-f2)/eps
        det=a11*a22-a12*a21;dE=(f1*a22-f2*a12)/det;dL=(a11*f2-a21*f1)/det;g.E-=dE;g.L-=dL
    g.r=ra;g.w=0;return g
for a in [0,0.9]:
    g=circ(12,a);r0=g.r;T=0
    while g.t<2000: g.step(a,g.hstep(a))
    print(f"a={a} circular r=12: drift {abs(g.r-r0):.2e}, Omega num {g.phi/g.t:.6f} vs {1/(12**1.5+a):.6f}")
# precession for rp=20, ra=80 (a=0): integrate apocentre to apocentre
a=0;g=bound(20,80,a);last=None;phis=[]
prev=g.w
while len(phis)<4:
    g.step(a,g.hstep(a)*0.5)
    if prev<0 and g.w>=0: phis.append(g.phi)   # pericentre: w crosses 0 upward
    prev=g.w
dphi=phis[1]-phis[0]-2*math.pi
A=(20+80)/2;e=0.6
print(f"precession per orbit numeric {math.degrees(dphi):.3f} deg; weak-field 6pi M/(a(1-e^2)) {math.degrees(6*math.pi/(A*(1-e*e))):.3f} deg; exact Schwarzschild (elliptic integral) below")
# exact Schwarzschild: Delta phi = 2*int_{up}^{ua} du/sqrt(2u^3 - u^2 + (2u... use quadrature
import numpy as np
up=1/20;ua=1/80;L2=g.L**2;E2=g.E**2
f=lambda u: (E2-(1-2*u)*(1+L2*u*u))/L2
th=np.linspace(0,np.pi,200001);u=ua+(up-ua)*(1-np.cos(th))/2;du=(up-ua)/2*np.sin(th)
val=du/np.sqrt(np.maximum(f(u),1e-30));val[0]=val[-1]=0
# endpoints singular-ish: use substitution which cancels sqrt zeros
I=np.trapezoid(val,th)*2
print(f"quadrature {math.degrees(I-2*math.pi):.3f} deg")
# zoom-whirl search: a=0, ra=60, scan rp, count whirls (phi swept near pericentre)
def run(rp,ra,a=0,turns=1):
    g=bound(rp,ra,a);prev=g.w;phis=[];n=0
    while len(phis)<2 and n<200000:
        g.step(a,g.hstep(a)*0.5);n+=1
        if prev<0 and g.w>=0: phis.append(g.phi)
        prev=g.w
    return (phis[1]-phis[0])/(2*math.pi) if len(phis)==2 else None
for rp in [9,8.5,8.2,8.1,8.05,8.02]:
    print("rp",rp,"turns per radial period",run(rp,60))
