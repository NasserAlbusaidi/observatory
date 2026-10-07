"""Assert native render comparisons; numerical images are separate from UI readability review.

Error budgets are in normalized, tonemapped RGB: RMSE <= .02, fraction of pixels
with any channel difference > .1 <= .01. These are image-domain budgets, not
claims of physically correct accretion dynamics or converged retarded emission.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path
import numpy as np
from PIL import Image

parser=argparse.ArgumentParser()
parser.add_argument("directory",type=Path)
args=parser.parse_args()
root=args.directory
capture_log=(root/"bhshots.txt").read_text(encoding="utf-8")
rows=[]
for name in ("bh-edge-on","bh-hotspot","bh-kerr-0.998-guides","bh-polar"):
    table_available=bool(re.search(r"^"+re.escape(name)+r"  .*lens table on",capture_log,re.M))
    for mode in ("table/full","full/reference"):
        left=root/(name+("" if mode=="table/full" else "-full")+".png")
        right=root/(name+("-full" if mode=="table/full" else "-reference")+".png")
        if not left.exists() or not right.exists():
            rows.append(dict(name=name,mode=mode,pass_=False,error="required pair missing"))
            continue
        a=np.asarray(Image.open(left).convert("RGB"),dtype=np.float64)/255
        b=np.asarray(Image.open(right).convert("RGB"),dtype=np.float64)/255
        delta=np.abs(a-b)
        rmse=float(np.sqrt(np.mean(delta**2)))
        pixels=float(np.mean(np.max(delta,axis=2)>.1))
        label="automatic full fallback/full" if mode=="table/full" and not table_available else mode
        rows.append(dict(name=name,mode=label,tableAvailable=table_available,pass_=rmse<=.02 and pixels<=.01,rmse=rmse,
                         largeErrorFraction=pixels,rmseLimit=.02,largeErrorFractionLimit=.01,
                         files=[dict(name=p.name,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in (left,right)]))
        print(f"{name} {label}: RMSE={rmse:.6f} <= .02; >.1 pixels={pixels:.6f} <= .01")
    mask=root/(name+"-reasons.png")
    if mask.exists():
        p=np.asarray(Image.open(mask).convert("RGB"))
        def count(r,g,b):
            return int(np.sum((p[:,:,0]>128 if r else p[:,:,0]<128)&
                             (p[:,:,1]>128 if g else p[:,:,1]<128)&
                             (p[:,:,2]>128 if b else p[:,:,2]<128)))
        rows.append(dict(name=name,mode="reason-counters",pass_=count(1,0,1)==0,
                         capture=count(1,0,0),escape=count(0,1,0),unresolved=count(0,0,1),
                         displayFallback=count(0,1,1),opaque=count(1,1,1),numerical=count(1,0,1),
                         total=p.shape[0]*p.shape[1],note="table geometry reason map" if table_available else "full-trace reason map; automatic fallback because the lens table is unavailable"))
    else:
        rows.append(dict(name=name,mode="reason-counters",pass_=False,error="required reason mask missing"))
result=dict(domain="tonemapped RGB image difference",rows=rows,failed=sum(not r["pass_"] for r in rows))
(root/"image-comparison.json").write_text(json.dumps(result,indent=2)+"\n")
raise SystemExit(1 if result["failed"] else 0)
