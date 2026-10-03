import numpy as np
from kerr import *
def edge(cam,k,axis=0,a=0.0):
    lo,hi=0.0,0.6
    for _ in range(36):
        m=(lo+hi)/2
        d=np.array([[np.sin(m)],[0],[np.cos(m)]]) if axis==0 else np.array([[0],[np.sin(m)],[np.cos(m)]])
        o=trace(cam,d,a,k=k,nmax=3000)
        if o['esc'][0]: hi=m
        else: lo=m
    return (lo+hi)/2, o['steps'][0]
for rc in [48.0,120.0]:
    ex=np.arcsin(np.sqrt(27)/rc*np.sqrt(1-2/rc))
    for k in [0.12,0.06,0.03,0.015]:
        e,n=edge(np.array([0,0,-rc]),k)
        print(f"rc={rc/2}rs k={k}: err {100*(e/ex-1):+.3f}%  steps near edge {n}")
import kerr
for P in [1e-3,1e-2]:
    kerr.PROJ=P
    for rc in [48.0,120.0]:
        ex=np.arcsin(np.sqrt(27)/rc*np.sqrt(1-2/rc))
        for k in [0.12,0.06,0.03]:
            e,n=edge(np.array([0,0,-rc]),k)
            print(f"PROJ={P} rc={rc/2}rs k={k}: err {100*(e/ex-1):+.4f}%  steps near edge {n}")
