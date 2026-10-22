from pathlib import Path
import json,hashlib,itertools,math,xml.etree.ElementTree as ET
import numpy as np
R=Path(__file__).parent
N=['gi-final-display-orbit-v1','gi-final-display-occluder-step-v2','gi-final-display-occluder-reference-v1']
W,H=320,180
Y,X=np.mgrid[:H,:W]
sha=lambda b:hashlib.sha256(b).hexdigest()
def mat(v):return np.array([[v[f'e{a}{b}'] for b in range(4)] for a in range(4)])
def srgb(a):return np.clip(np.where(a<=.0031308,12.92*a,1.055*np.maximum(a,0)**(1/2.4)-.055),0,1)*255
def world(f,d):
 c=np.stack([(X+.5)/W*2-1,(Y+.5)/H*2-1,d,np.ones_like(d)],-1);q=c@mat(f['inverseJitteredViewProjection']).T;return q[...,:3]/q[...,3:]
def sample(f,a,pts):
 q=np.c_[pts,np.ones(len(pts))]@mat(f['jitteredViewProjection']).T;uv=q[:,:2]/q[:,3:]*.5+.5;px=uv[:,0]*W-.5;py=uv[:,1]*H-.5;ix=np.floor(px).astype(int);iy=np.floor(py).astype(int);wx=px-ix;wy=py-iy
 valid=(ix>=0)&(ix<W-1)&(iy>=0)&(iy<H-1);ix=ix.clip(0,W-2);iy=iy.clip(0,H-2)
 out=(a[iy,ix]*(1-wx[:,None])+a[iy,ix+1]*wx[:,None])*(1-wy[:,None])+(a[iy+1,ix]*(1-wx[:,None])+a[iy+1,ix+1]*wx[:,None])*wy[:,None]
 return out,valid,(ix,iy)
def stats(a):
 return {'mean':np.mean(a,axis=0).tolist(),'p50':np.quantile(a,.5,axis=0).tolist(),'p95':np.quantile(a,.95,axis=0).tolist(),'max':np.max(a,axis=0).tolist()}
data={};receipts={};inputs=[]
for n in N:
 p=R/n/'frames';cb=(p/'capture.json').read_bytes();j=json.loads(cb);run=json.loads((R/n/'run.json').read_text());assert run['status']=='Completed' and run['restored'] and run['finished']
 assert j['status']=='Completed' and j['outstanding']==0 and not j['error'] and not j['persistenceError'];assert len(j['buffers'])==j['retired']
 frames=[]
 for f in j['frames']:
  assert f['width']==W and f['height']==H and f['presentExecuted'] and f['outputPolicy']=='HardwareSRGB'
  arrays={}
  for b in j['buffers']:
   if b['captureIndex']!=f['captureIndex']:continue
   raw=(p/b['file']).read_bytes();fmt=b['format'];dtype='<f4' if '32' in fmt else '<f2';ch=1 if b['semantic']=='Depth' else (2 if fmt=='R16G16_SFloat' or fmt=='R32G32_SFloat' else 4)
   a=np.frombuffer(raw,dtype=dtype).reshape(H,W,ch).astype(float);assert np.isfinite(a).all()
   inputs.append({'path':str((p/b['file']).relative_to(R)),'bytes':len(raw),'sha256':sha(raw)})
   if b['semantic'] in ['Depth','DisplayColor']:arrays[b['semantic']]=a
  frames.append({'metadata':f,'depth':arrays['Depth'][...,0],'display':srgb(arrays['DisplayColor'][...,:3])})
 data[n]=frames;receipts[n]={'captureSha256':sha(cb),'successfulFrames':j['successfulFrames'],'capturedFrames':j['capturedFrames'],'retired':j['retired'],'outstanding':j['outstanding'],'rawFinite':True,'restored':run['restored'],'nativeDisplayPixelReadback':False}
step=data[N[1]];ref=data[N[2]];rows=[]
for i,a in enumerate(step[1:]):
 f=a['metadata'];matches=[b for b in ref if b['metadata']['currentJitterUV']==f['currentJitterUV'] and b['metadata']['jitteredViewProjection']==f['jitteredViewProjection']];assert len(matches)==1;b=matches[0];assert np.array_equal(a['depth'],b['depth']);w=world(f,a['depth']);roi=(abs(w[...,1])<.005)&(w[...,0]>=-1.1)&(w[...,0]<=-.5)&(w[...,2]>=.7)&(w[...,2]<=1.3)
 # Current visible floor/back wall whose camera ray formerly crossed the old cube.
 pos=f['position'];origin=np.array([pos[k] for k in ['x','y','z']]);direction=w-origin;lo=np.array([-1.1,0,.7]);hi=np.array([-.5,1.2,1.3]);inv=np.divide(1,direction,out=np.full_like(direction,np.inf),where=direction!=0);t0=np.minimum((lo-origin)*inv,(hi-origin)*inv).max(-1);t1=np.maximum((lo-origin)*inv,(hi-origin)*inv).min(-1)
 revealed=(t1>np.maximum(t0,0))&(t0<.98)&(t1<.99)&((abs(w[...,1])<.005)|(abs(w[...,2]-3.9)<.005))
 row={'afterMoveIndex':i,'frame':f['frame'],'referenceFrame':b['metadata']['frame'],'exactJitteredVP':True,'fullDepthExact':True}
 for name,mask in [('oldFloorFootprint',roi),('oldCubeRevealedSurface',revealed)]:
  diff=abs(a['display'][mask]-b['display'][mask]);row[name]={'pixels':int(mask.sum()),'meanAbsCodeRGB':diff.mean(0).tolist(),'meanAbsCodeAll':float(diff.mean()),'p95AbsCodeAll':float(np.quantile(diff,.95)),'maxAbsCodeAll':float(diff.max())}
 rows.append(row)
# Independent noisy steady reference: fixed old-footprint world points, bilinear display sampling,
# retain points whose four sampled depth texels all reconstruct to the same floor.
px,pz=np.meshgrid(np.linspace(-1.08,-.52,15),np.linspace(.72,1.28,15));pts=np.c_[px.ravel(),np.zeros(px.size),pz.ravel()];samples=[];valid=np.ones(len(pts),bool)
for a in ref:
 v,inside,(ix,iy)=sample(a['metadata'],a['display'],pts);w=world(a['metadata'],a['depth']);ok=inside
 for dy in [0,1]:
  for dx in [0,1]:ok &=abs(w[iy+dy,ix+dx,1])<.005
 valid &=ok;samples.append(v)
samples=np.stack(samples)[:,valid];assert valid.sum()>100
pairs=np.array([np.abs(samples[i]-samples[j]).mean() for i,j in itertools.combinations(range(len(samples)),2)])
reference={'worldPoints':int(valid.sum()),'pairwiseMAECodeAll':stats(pairs),'temporalStdCodeRGB':stats(samples.std(0)),'note':'28 pairs of eight noisy reference frames, not a noise-free truth; world aligned bilinear display estimate.'}
# Orbit: fixed floor points; changes combine camera-dependent signal and noise, not an isolated-noise measurement.
px,pz=np.meshgrid(np.linspace(-2.2,-1.2,25),np.linspace(-.5,2.5,50));pts=np.c_[px.ravel(),np.zeros(px.size),pz.ravel()];samples=[];valid=np.ones(len(pts),bool)
for a in data[N[0]]:
 v,inside,(ix,iy)=sample(a['metadata'],a['display'],pts);w=world(a['metadata'],a['depth']);ok=inside
 for dy in [0,1]:
  for dx in [0,1]:ok &=abs(w[iy+dy,ix+dx,1])<.005
 valid &=ok;samples.append(v)
samples=np.stack(samples)[:,valid];assert valid.sum()>500
orbit={'worldPoints':int(valid.sum()),'temporalStdCodeRGB':stats(samples.std(0)),'adjacentEveryFourFramesAbsCodeRGB':stats(abs(np.diff(samples,axis=0)).reshape(-1,3)),'note':'Moving-camera, every-four-frame samples; changing visible illumination and TAA reprojection are included, so this is not pure stochastic variance.'}
# CPU verification of the analytical no-clipping premise in the new mature test.
pattern=[1,-1,1,1,-1,-1,1,-1,1,1,-1,-1,-1,1,-1,1];pat=lambda x,y,t:pattern[((y+t//4)%4)*4+(x+t)%4];maxsign=0;ratio=0
for channel in range(4):
 amp=.075 if channel==3 else .1;margin=(1.25*math.sqrt(1-25/81)-5/9)*amp
 for y in range(2,10):
  for x in range(2,10):
   e=.25
   for t in range(16):
    signs=sum(pat(x+dx,y+dy,t+channel) for dy in [-1,0,1] for dx in [-1,0,1]);maxsign=max(maxsign,abs(signs));ratio=max(ratio,abs(e-.25)/margin);assert abs(signs)<=5 and abs(e-.25)<margin;e=.93*e+.07*(.25+amp*pat(x,y,t+channel))
report={'scope':'Read-only independent final DisplayColor and corrected contract audit; no Editor operations. Original runtime shader retained.','displayModel':'DisplayColor linear RGB transformed by IEC sRGB OETF and expressed in 8-bit code units. HardwareSRGB and Present were recorded. This is a prediction, not native framebuffer pixel readback.','captures':receipts,'step':rows,'reference':reference,'orbit':orbit,'matureOracle':{'actualMaxNeighborhoodSignSum':maxsign,'allowedMax':5,'maxHistoryOffsetOverConservativeMargin':ratio,'noClippingPremiseVerified':True},'inputs':inputs,'limits':['Native screenshots are discrete observations, not video.','Reference noise is not a ground truth.','No universal no-noise or zero-ghosting claim.','No new production filtering accepted.']}
out=R/'independent-final-display-review-v1.json';out.write_text(json.dumps(report,indent=2));print(json.dumps({k:report[k] for k in ['reference','orbit','matureOracle']},indent=2));print('STEP',[(v['afterMoveIndex'],v['oldFloorFootprint']['meanAbsCodeAll'],v['oldCubeRevealedSurface']['pixels'],v['oldCubeRevealedSurface']['meanAbsCodeAll']) for v in rows]);print('output',out.stat().st_size,sha(out.read_bytes()))
# Independent full-suite reconciliation and immutable test/source binding.
import gzip,collections,subprocess
repo=Path('/Volumes/DataDisk/Projects/Unity/InfinityExample/Packages/com.infinity.render-pipeline')
focused=R/'main-temporal-contract-final-v1';full=R/'main-full-final-v1'
for group in [focused,full]:
 run=json.loads((group/'run.json').read_text());assert run['status']=='Completed' and run['callbacksComplete']
 for b in json.loads((R/N[0]/'frames/capture.json').read_text())['buffers']:assert b['submitted'] and b['readbackTerminal'] and b['retired'] and not b['error']
correction=json.loads((R/'experimental-test-contract-correction-v1.json').read_text());source=repo/'Tests/Editor/SSGITemporalResponseGpuTests.cs';assert sha(source.read_bytes())==correction['correctedSourceSHA256']
fx=ET.parse(focused/'callback-results.xml').findall('.//test-case');names={x.get('name') for x in fx};assert len(fx)==20 and all(x.get('result')=='Passed' for x in fx);assert all(v in names for v in correction['renames'].values()) and all(k not in names for k in correction['renames'])
run=json.loads((full/'run.json').read_text());ck=json.loads((full/'reload-checkpoint.json').read_text());cb=ET.parse(full/'callback-results.xml');raw=ET.parse(full/'results.xml');priorpath=repo/'Documentation~/Evidence/Verification-0.5-2026-10-19/S13-closure/test-results.json.gz';priorbytes=priorpath.read_bytes() if priorpath.exists() else subprocess.check_output(['git','show','64456f1:Documentation~/Evidence/Verification-0.5-2026-10-19/S13-closure/test-results.json.gz'],cwd=repo);assert sha(priorbytes)=='7da331ae40452572fd9e9b7c568cbbc6e0733a65296e328ed42a8dbab10c27b7';old=json.loads(gzip.decompress(priorbytes));key=next(k for k in old if k.endswith('main-s13-full-v2/callback-results.xml'))
def indexed(tree):
 d={}
 for x in tree.findall('.//test-case'):
  props=x.findall('./properties/property[@name="InfinityRP.UniqueName"]');assert len(props)==1;u=props[0].get('value');assert u not in d;d[u]=x
 return d
ver=lambda x:tuple(x.get(k,'') for k in ['fullname','result','label','site'])
new=indexed(cb);prior=indexed(ET.fromstring(old[key]));assert not(prior.keys()-new.keys());assert all(ver(x)==ver(new[u]) for u,x in prior.items());added=new.keys()-prior.keys();assert len(added)==20 and all(new[u].get('result')=='Passed' for u in added)
missing=run['frameworkMissingSkipped'];assert len(missing)==len(set(missing))==33;leaves={x['uniqueName']:x for x in ck['leaves']}
for u in missing:
 l=leaves[u];assert l['started'] and l['finished'] and l['checkpointed'];x=ET.fromstring(l['xml']);assert x.get('result')=='Skipped' and ver(x)==ver(new[u])
assert collections.Counter(ver(x) for x in raw.findall('.//test-case'))==collections.Counter(ver(x) for u,x in new.items() if u not in missing)
assert {u for u,x in prior.items() if x.get('result')=='Skipped'}=={u for u,x in new.items() if x.get('result')=='Skipped'}
report['tests']={'sourceSha256':sha(source.read_bytes()),'runtimeShaderSha256':sha((repo/'Shaders/RenderingFeature/ScreenSpaceIndirectDiffuse/Compute_ScreenSpaceIndirectDiffuse.compute').read_bytes()),'sevenRenamesExact':True,'focused':{'passed':20,'failed':0,'skipped':0,'frameworkComplete':True,'callbacksComplete':True},'mainFull':{'total':len(new),'passed':run['passed'],'failed':run['failed'],'skipped':run['skipped'],'nativeRawLeaves':len(raw.findall('.//test-case')),'actualRecoveredSkipped':33,'originalUIDVerdictsUnchanged':len(prior),'addedPassed':20,'skipUIDSetUnchanged':True,'callbacksComplete':True,'frameworkComplete':False,'reload':run['reloadCount'],'resume':run['resumedCount'],'failures':[x.get('fullname') for x in new.values() if x.get('result')=='Failed']},'sourceInputs':{str(p):sha(priorbytes if p==priorpath else p.read_bytes()) for p in [focused/'run.json',focused/'results.xml',focused/'callback-results.xml',full/'run.json',full/'results.xml',full/'callback-results.xml',full/'reload-checkpoint.json',priorpath]}}
report['native']={}
for mode in ['orbit','step']:
 p=R/('native-normal-'+mode+'-v2.json');n=json.loads(p.read_text());assert n['finished'] and n['view']=='None' and n['seconds']>=30
 report['native'][mode]={'receipt':n,'sha256':sha(p.read_bytes()),'screenshots':{s:sha((R/s).read_bytes()) for s in ['native-normal-'+mode+'-'+a+'-v2.png' for a in ['a','b']]}}
report['visualAssessment']={'normalScreenshots':'Four actual original-Example Game screenshots inspected. No obvious debug-like granular pattern or persistent old-object silhouette; slight low-frequency wall variation remains. Discrete screenshots do not prove continuous flicker absence.','displayDerivedContact':'Original-runtime DisplayColor orbit and step samples inspected after recorded Y flip and sRGB OETF. No conspicuous long trail in the reviewed samples.','boundedVerdict':'No demonstrated final-display defect requiring either withdrawn filter experiment. Existing normal-display behavior is acceptable for this fixture within sampled-sequence/native-screenshot limits; no universal zero-noise/zero-ghosting claim. Overall S14 remains pending historical and delivery closure.'}
for n in N:
 j=json.loads((R/n/'frames/capture.json').read_text())
 assert all(b['submitted'] and b['readbackTerminal'] and b['retired'] and not b['error'] and b['producerFrame']==next(f['frame'] for f in j['frames'] if f['captureIndex']==b['captureIndex']) for b in j['buffers'])
report['retirement']={'consumedFinalRawCount':len(inputs),'consumedFinalRawBytes':sum(i['bytes'] for i in inputs),'rawPaths': 'The exact paths and hashes are in inputs; eligible for retirement after parent retains this compact report, reproduction source, capture metadata, native images and restoration receipts. No blanket authorization for other unreviewed raw.'}
out.write_text(json.dumps(report,indent=2));print('FINAL',out.stat().st_size,sha(out.read_bytes()),'scriptSHA',sha(Path(__file__).read_bytes()))
