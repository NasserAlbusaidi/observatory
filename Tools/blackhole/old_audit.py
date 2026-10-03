# Replicate the current BlackHole.shader ray loop (rs=1) in float32 and measure the shadow edge.
import numpy as np
f=np.float32
def trace_old(rcam, alpha, steps=420):
    # camera on -z axis at distance rcam looking toward +z; ray at angle alpha from the inward direction, in x-z plane
    pos=np.array([0,0,-rcam],f); vel=np.array([np.sin(alpha),0,np.cos(alpha)],f)
    h=np.cross(pos,vel); h2=f(np.dot(h,h))
    for k in range(steps):
        r=f(np.linalg.norm(pos))
        if r<1: return 'cap',k
        if r>60 and np.dot(pos,vel)>0: return 'esc',k
        dt=f(np.clip(0.045*r*(r-0.8),0.012,2.5))
        acc=f(-1.5)*h2*pos/f(r**5)
        nv=vel+acc*dt; np_=pos+nv*dt
        pos,vel=np_,nv
    return 'max',steps
def bcrit_old(rcam,steps=420):
    lo,hi=0.0,0.5
    for _ in range(40):
        m=(lo+hi)/2
        s,_=trace_old(rcam,m,steps)
        if s=='esc': hi=m
        else: lo=m
    a=(lo+hi)/2
    u=1/rcam
    # coordinate angle a measured from inward radial; b from Binet
    b=1/np.sqrt(u*u/np.sin(a)**2-u**3)
    return a,b
for rc in [24,9,7]:
    for st in [420,int(420*0.55)]:
        a,b=bcrit_old(rc,st)
        print(f"rcam={rc} steps={st}: edge angle {np.degrees(a):.4f} deg, b_crit={b:.4f} rs (exact 2.5981), err {100*(b/2.598076-1):+.2f}%")
