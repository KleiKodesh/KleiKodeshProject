import os
DATA=os.environ.get('CLEAN_TEXT_DATA',os.path.join(os.path.dirname(os.path.abspath(__file__)),'data'))
import json,re,collections as C
R=json.load(open(os.path.join(DATA,'rows2.json'),encoding='utf-8'))
single=re.compile(r'^\s*(?:<(?!/?(?:b|strong)\b)[^>]+>\s*|\(?[א-ת0-9]{1,4}[).]\s*)*<(b|strong)\b[^>]*>(?:(?!</?(?:b|strong)\b).)*</\1>\s*(?:<[^>]+>\s*)*$',re.S)
for r in R:
  ph=r['pre_html']
  for _ in range(3): ph=re.sub(r'<(\w+)[^>]*>\s*</\1>','',ph)
  r['sb']=bool(single.match(ph)) and len(r['pre_html'])<300
  r['short']=r['wc']<=6 and not r['punct']
H={'H0 current':lambda r:True,
   'H1 single bold prefix':lambda r:r['sb'],
   'H2 bold OR short(<=6w,no punct)':lambda r:r['sb'] or r['short'],
   'H3 bold OR (short AND commentary)':lambda r:r['sb'] or (r['short'] and r['dep']=='commentary'),
   'H4 short only':lambda r:r['short'],
   'H5 bold OR (short AND hyphen/emdash not endash)':lambda r:r['sb'] or (r['short'] and r['ch']!='0x2013'),'H6 bold OR (short<=4 AND commentary)':lambda r:r['sb'] or (r['wc']<=4 and not r['punct'] and r['dep']=='commentary')}
lab=[r for r in R if r['y'] is not None]
print('labeled',len(lab),'pos',sum(r['y'] for r in lab))
for n,f in H.items():
  tp=sum(1 for r in lab if r['y']==1 and f(r));fp=sum(1 for r in lab if r['y']==0 and f(r));fn=sum(1 for r in lab if r['y']==1 and not f(r))
  unl=[r for r in R if r['y'] is None]; fire=sum(f(r) for r in unl)
  print(f'{n:38} TP {tp} FP {fp} FN {fn} prec {tp/max(1,tp+fp):.3f} rec {tp/max(1,tp+fn):.3f} | unlabeled fires {fire}/{len(unl)}')
  bc=C.Counter((r['cat'],r['dep']) for r in lab if r['y']==0 and f(r)); print('   FP by',bc.most_common(5))
  bc=C.Counter((r['cat'],r['book']) for r in lab if r['y']==1 and not f(r)); print('   FN by',bc.most_common(5))
# dh not bold rate among positives per book
print('positive nonbold books',C.Counter(r['book'] for r in lab if r['y']==1 and not r['sb']).most_common(8))
print('wc dist positives',sorted(C.Counter(min(r['wc'],15) for r in lab if r['y']==1).items()))
print('wc dist negatives',sorted(C.Counter(min(r['wc'],15) for r in lab if r['y']==0).items()))
print('endpunct pos/neg',sum(r['endpunct'] for r in lab if r['y']==1),sum(r['endpunct'] for r in lab if r['y']==0))
print('char pos',C.Counter(r['ch'] for r in lab if r['y']==1),'neg',C.Counter(r['ch'] for r in lab if r['y']==0))
