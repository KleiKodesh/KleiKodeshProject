import os
DATA=os.environ.get('CLEAN_TEXT_DATA',os.path.join(os.path.dirname(os.path.abspath(__file__)),'data'))
import json,re,collections as C
R=json.load(open(os.path.join(DATA,'rows2.json'),encoding='utf-8'));O=json.load(open(os.path.join(DATA,'vout.json'),encoding='utf-8'))
single=re.compile(r'^\s*(?:<(?!/?(?:b|strong)\b)[^>]+>\s*|\(?[א-ת0-9]{1,4}[).]\s*)*<(b|strong)\b[^>]*>(?:(?!</?(?:b|strong)\b).)*</\1>\s*(?:<[^>]+>\s*)*$',re.S)
def sb(r):
  ph=r['pre_html']
  for _ in range(3): ph=re.sub(r'<(\w+)[^>]*>\s*</\1>','',ph)
  return bool(single.match(ph)) and len(r['pre_html'])<300
def tag(s): return re.sub(r'<[^>]*>','',s)
bad=C.Counter();ex={}
for r,o in zip(R,O):
  com=r['dep']=='commentary'
  r['f8']=o['o8']!=o['line']; r['fapp']=(o['o72'] if com else o['o8'])!=o['line']
  r['h1']=sb(r); r['h3']=r['h1'] or (r['wc']<=6 and not r['punct'] and com)
  for k in ('o8','o72'):
    s=o[k]
    if s==o['line']: continue
    t=tag(s);L=tag(o['line'])
    if re.search(r'[.:]\s*\.',t): bad['double '+k]+=1; ex.setdefault('double',(r['book'],s[:0]))
    if len(re.sub(r'\s','',t))!=len(re.sub(r'[\s\u2013\u2014\-]','',L))+ (0 if re.search(r'[.:]\s*$',tag(r['pre_html'])) else 1)-(len(re.findall(r'[\u2013\u2014\-]',L))-len(re.findall(r'[\u2013\u2014\-]',t))-1)*0 and False: pass
    if re.search(r'\s\.|\.\S',t.split('. ',1)[0][-3:]+'. ') : pass
    # lost text: removing first dash char + one '.' should give same non-space chars
    a=re.sub(r'\s','',L); b=re.sub(r'\s','',t)
    ok=any(a.replace(c,'',1)==b.replace('.','',1) or a.replace(c,'',1)==b for c in '\u2013\u2014-')
    if not ok: bad['lost '+k]+=1; ex.setdefault('lost '+k,(r['book'],))
    if '  ' in t or ' .' in t[:len(tag(r['pre_html']))+3]: bad['space '+k]+=1
lab=[r for r in R if r['y'] is not None]
def sc(n,f,rows):
  tp=sum(1 for r in rows if r['y']==1 and f(r));fp=sum(1 for r in rows if r['y']==0 and f(r));fn=sum(1 for r in rows if r['y']==1 and not f(r))
  print(f'{n:30} TP {tp} FP {fp} FN {fn} P {tp/max(1,tp+fp):.3f} R {tp/max(1,tp+fn):.3f}')
sc('impl as app',lambda r:r['fapp'],lab); sc('impl flag8 only',lambda r:r['f8'],lab)
sc('H1',lambda r:r['h1'],lab); sc('H3',lambda r:r['h3'],lab)
cm=[r for r in lab if r['dep']=='commentary']
sc('commentary, pane (72)',lambda r:r['fapp'],cm); sc('commentary, main view (8)',lambda r:r['f8'],cm)
nc=[r for r in lab if r['dep']!='commentary']; sc('non-commentary (8)',lambda r:r['f8'],nc)
print('output checks',dict(bad),ex)
for nm,a,b in (('impl8 vs H1','f8','h1'),('app vs H3','fapp','h3')):
  d=[r for r in lab if r[a]!=r[b]]
  print(nm,'disagree',len(d))
  for r in d[:40]:
    s=re.sub(r'[\u0591-\u05C7]','',r['pre_html'])[-90:]; s=re.sub(r'[\u05D0-\u05EA]+','w',s)
    print(' ',r['book'],r['dep'][:4],'y',r['y'],'impl',r[a],'heur',r[b],'wc',r['wc'],'|',s)
print('---FP app')
def pat(r):
  p=r['pre_html']; c=p.count('<b')-p.count('</b')
  return 'unclosed-bold' if c>0 else ('closed-bold' if '<b' in p else 'plain')
for grp in ('fapp',):
  fp=[r for r in lab if r['y']==0 and r[grp]]
  print(C.Counter((pat(r),r['dep'][:4]) for r in fp)); print(C.Counter((pat(r),r['book']) for r in fp).most_common(15))
  fn=[r for r in lab if r['y']==1 and not r[grp]]
  print('FN',C.Counter((pat(r),r['book']) for r in fn).most_common(12))
