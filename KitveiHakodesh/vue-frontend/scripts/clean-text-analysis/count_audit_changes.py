import os
DATA=os.environ.get('CLEAN_TEXT_DATA',os.path.join(os.path.dirname(os.path.abspath(__file__)),'data'))
import json,re,collections as C
R=json.load(open(os.path.join(DATA,'auout.json'),encoding='utf-8'))
def cl(c):
  if not c: return 'E'
  k=ord(c)
  if 0x5d0<=k<=0x5ea: return 'L'
  if 0x591<=k<=0x5c7: return 'M'
  if c.isspace(): return 'S'
  if c.isdigit(): return 'D'
  return c
def sk(s): return ''.join('L' if 0x5d0<=ord(c)<=0x5ea else 'm' if 0x591<=ord(c)<=0x5c7 else '{%04X}'%ord(c) if ord(c)>127 else c for c in s)
for key in ['o16','o18','o32']:
  print('=====',key)
  for ch in [':','"','״','׳',"'",'.',';','!','?','׃','&quot;','&nbsp;','–','־','-']:
    tin=tout=0; ctx=C.Counter(); ex={}
    for r in R:
      a=r['line']; b=r[key]
      n1=a.count(ch); n2=b.count(ch); tin+=n1; tout+=n2
      if n2<n1:
        for m in re.finditer(re.escape(ch),a):
          k=cl(a[m.start()-1:m.start()])+'_'+cl(a[m.end():m.end()+1])+('_end' if re.match(r'\s*(<|$)',a[m.end():]) else '')
          ctx[k]+=1; ex.setdefault(k,(r['book'],r['cat'],sk(a[max(0,m.start()-10):m.end()+10])))
    if tin: print('%04X'%ord(ch[0]) if len(ch)==1 else ch,'in',tin,'out',tout,'|',[(k,n,ex[k]) for k,n in ctx.most_common(6)])
