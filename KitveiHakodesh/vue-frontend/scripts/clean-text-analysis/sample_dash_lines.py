import os,sys
DB=sys.argv[1] if len(sys.argv)>1 else os.path.join(os.environ['APPDATA'],'otzaria','books','seforim.db')
DATA=os.environ.get('CLEAN_TEXT_DATA',os.path.join(os.path.dirname(os.path.abspath(__file__)),'data'))
os.makedirs(DATA,exist_ok=True)
import sqlite3,random,re,json,collections as C
from compression import zstd
random.seed(1)
c=sqlite3.connect('file:'+DB.replace(os.sep,'/')+'?mode=ro',uri=True)
zd=zstd.ZstdDict(c.execute("select dict from zstd_dict").fetchone()[0])
def dec(b): return zstd.decompress(b,zstd_dict=zd).decode('utf-8') if isinstance(b,bytes) else b
cats={1:'Tanakh',5:'Mishna',12:'Bavli',19:'Yerushalmi',25:'Tosefta',33:'Midrash',43:'Halacha',62:'Chasidut',522:'Kabbalah',546:'Machshava',663:'Musar',1150:'Shut',1158:'Reference'}
DASH='-\u2012\u2013\u2014\u2015\u2212'
dre=re.compile('(?<=\S)?\s*['+re.escape(DASH)+'](?=\s)')
tag=re.compile('<[^>]+>')
out=[]
for cid,cn in cats.items():
  books=c.execute("select b.id,b.title,coalesce(b.dependenceType,'base'),(select count(*) from line_dh d where d.bookId=b.id limit 1) from book b join category_closure cc on cc.descendantId=b.categoryId where cc.ancestorId=? and b.totalLines>20",(cid,)).fetchall()
  random.shuffle(books)
  for bid,title,dep,hasdh in books[:70]:
    ids=c.execute("select l.id,l.lineIndex,lc.content from line l join line_content lc on lc.id=l.id where l.bookId=? order by random() limit 250",(bid,)).fetchall()
    for lid,li,cont in ids:
      h=dec(cont)
      if not h or h.lstrip().startswith('<h'): continue
      # first dash followed by whitespace in html, outside tags
      txt=h
      m=None
      for mm in re.finditer('['+re.escape(DASH)+'](?=[\s<]|&nbsp;)',txt):
        pre=txt[:mm.start()]
        if pre.count('<')>pre.count('>'): continue
        m=mm;break
      if not m: continue
      pre=txt[:m.start()]; pt=tag.sub('',pre).strip()
      if not pt: continue
      dh=c.execute("select dhDisplay from line_dh where bookId=? and lineIndex=?",(bid,li)).fetchone() if hasdh else None
      out.append(dict(cat=cn,book=bid,title=title,dep=dep,hasdh=bool(hasdh),dh=dh[0] if dh else None,ch=hex(ord(m.group(0))),pre_html=pre[-300:] if len(pre)>300 else pre,pre=pt,post=tag.sub('',txt[m.end():])[:80],start=h[:12],sp_before=pre[-1:].isspace() or pre.endswith('</b>') ))
json.dump(out,open(os.path.join(DATA,'rows.json'),'w',encoding='utf-8'),ensure_ascii=False)
print(len(out))
