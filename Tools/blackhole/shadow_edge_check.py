# Analytic shadow edge rho_c(psi) in the camera's ZAMO frame from the photon-orbit family, against bisection with the tracer.
import numpy as np
from lens_proto import camera, shadow_edge
def analytic_edge(c, nbins):
    a=c['a']; al=c['lapse']; om=c['omega']; vp=c['varpi']; rho=c['rho']; mu=c['mu']; st=c['st']
    if a < 1e-3:
        chi=np.linspace(0,2*np.pi,8192,endpoint=False); b=np.sqrt(27)*np.sin(chi); q=27-b*b
    else:
        rp=2*(1+np.cos(2/3*np.arccos(-a))); rm=2*(1+np.cos(2/3*np.arccos(a)))
        r=np.linspace(rp,rm,8192); b=-(r**3-3*r**2+a*a*r+a*a)/(a*(r-1)); q=r**3*(4*a*a-r*(r-3)**2)/(a*a*(r-1)**2)
    pts=[]
    for sg in (1,-1):
        nph=b*al/(vp*(1-b*om)); EF=1/(al+om*vp*nph)
        pth2=q-mu*mu*(b*b/st**2-a*a); ok=pth2>=0
        nth=sg*np.sqrt(np.maximum(pth2,0))/(EF*rho); nr2=1-nph**2-nth**2; ok&=nr2>=0
        nr=np.sqrt(np.maximum(nr2,0)); d=-np.stack([nr,nth,nph],1)
        psi=np.arctan2(d[:,2],d[:,1])%(2*np.pi); rr=np.arccos(np.clip(-d[:,0],-1,1))
        pts.append(np.stack([psi[ok],rr[ok]],1))
    P=np.concatenate(pts); P=P[np.argsort(P[:,0])]
    centers=(np.arange(nbins)+0.5)/nbins*2*np.pi
    ext=np.concatenate([P-[2*np.pi,0],P,P+[2*np.pi,0]])
    return np.interp(centers, ext[:,0], ext[:,1])
for name,pos,a in [('default',(0,0.9,24),0.0),('kerr998',(0,0.9,24),0.998),('close',(0,2.2,6),0.9)]:
    c=camera(pos,a); N=256
    psi,rc=shadow_edge(c,N); an=analytic_edge(c,N)
    px=2*np.tan(np.radians(21))/1080
    print(name,'max |analytic - bisection| = %.2e rad = %.3f px'%(np.abs(an-rc).max(), np.abs(an-rc).max()/px))
