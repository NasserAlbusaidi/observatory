import numpy as np, kerr
from kerr import *
from img import cam_rays
from PIL import Image
kerr.PROJ=1e-3
def isco(a):
    z1=1+(1-a*a)**(1/3)*((1+a)**(1/3)+(1-a)**(1/3)); z2=np.sqrt(3*a*a+z1*z1)
    return 3+z2-np.sqrt((3-z1)*(3+z1+2*z2))
def flux(r,a):
    # Page-Thorne, M=1, prograde
    x=np.sqrt(r); x0=np.sqrt(isco(a)); 
    ac=np.arccos(a)
    x1=2*np.cos((ac-np.pi)/3); x2=2*np.cos((ac+np.pi)/3); x3=-2*np.cos(ac/3)
    br = x-x0-1.5*a*np.log(x/x0) \
        - 3*(x1-a)**2/(x1*(x1-x2)*(x1-x3))*np.log((x-x1)/(x0-x1)) \
        - 3*(x2-a)**2/(x2*(x2-x1)*(x2-x3))*np.log((x-x2)/(x0-x2)) \
        - 3*(x3-a)**2/(x3*(x3-x1)*(x3-x2))*np.log((x-x3)/(x0-x3))
    return np.where(x>x0, br/(x**4*(x**3-3*x+2*a)), 0)
def luminet(r):
    x=np.sqrt(r/1.0); 
    return np.where(r>6, 1/((r-3)*r**2.5)*(np.sqrt(r)-np.sqrt(6)+np.sqrt(3)/2*np.log((np.sqrt(r)+np.sqrt(3))*(np.sqrt(6)-np.sqrt(3))/((np.sqrt(r)-np.sqrt(3))*(np.sqrt(6)+np.sqrt(3))))),0)
rr=np.linspace(6.001,60,20000)
fa=flux(rr,1e-5); fl=luminet(rr)
print("Page-Thorne a=0 vs Luminet max rel diff", np.max(np.abs(fa/fa.max()-fl/fl.max())), " peak r =", rr[fa.argmax()], "M =",rr[fa.argmax()]/2,"rs")
newt=(1/rr**3)*(1-np.sqrt(6/rr)); print("Newtonian SS peak", rr[newt.argmax()]/2, "rs")
for a in [0.5,0.9,0.998]:
    r2=np.linspace(isco(a)*1.0001,60,50000); f=flux(r2,a); print(f"a={a}: isco {isco(a):.4f} M, flux peak at {r2[f.argmax()]:.3f} M")
