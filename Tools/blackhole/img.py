import numpy as np, kerr
from kerr import *
kerr.PROJ=1e-3
def cam_rays(cam, look, fov, W, H):
    f=(look-cam); f/=np.linalg.norm(f); up=np.array([0,1.0,0]); r=np.cross(up,f); r/=np.linalg.norm(r); u=np.cross(f,r)
    t=np.tan(np.radians(fov)/2); xs=(np.arange(W)+0.5)/W*2-1; ys=(np.arange(H)+0.5)/H*2-1
    X,Y=np.meshgrid(xs*W/H*t, -ys*t)
    d=f[:,None]+r[:,None]*X.ravel()+u[:,None]*Y.ravel(); d/=np.linalg.norm(d,axis=0); return d
for a in [0.0, 0.998]:
  for k in [0.12,0.2,0.3]:
    cam=np.array([0,0,-1000.0]); W=H=101
    d=cam_rays(cam,np.zeros(3),1.2,W,H)
    o=trace(cam,d,a,k=k,nmax=1500,rmax=2000)
    cap=o['cap'].reshape(H,W)
    # horizontal extent of shadow along middle row in impact units
    t=np.tan(np.radians(0.6)); xs=((np.arange(W)+0.5)/W*2-1)*t*1000
    row=cap[H//2]; idx=np.where(row)[0]
    col=cap[:,W//2]; idy=np.where(col)[0]
    print(f"a={a} k={k}: steps mean {o['steps'].mean():.0f} p99 {np.percentile(o['steps'],99):.0f} max {o['steps'].max()} alive {o['alive'].sum()}  x-extent [{xs[idx[0]]:.2f},{xs[idx[-1]]:.2f}] y-extent {xs[idy[-1]]-xs[idy[0]]:.2f} (pixel {xs[1]-xs[0]:.3f})")
# Bardeen critical curve at i=90
for a in [0.0,0.998]:
    rp_=2*(1+np.cos(2/3*np.arccos(-a))); rm_=2*(1+np.cos(2/3*np.arccos(a)))
    rr=np.linspace(rp_,rm_,20001)
    if a>0:
        bb=-(rr**3-3*rr**2+a*a*rr+a*a)/(a*(rr-1)); 
        print(f"Bardeen a={a}: alpha range [{(-bb).min():.3f},{(-bb).max():.3f}] i.e. b in [{bb.min():.3f},{bb.max():.3f}]  photon orbits r={rp_:.3f},{rm_:.3f}")
    else: print("Bardeen a=0: b=±5.196")
