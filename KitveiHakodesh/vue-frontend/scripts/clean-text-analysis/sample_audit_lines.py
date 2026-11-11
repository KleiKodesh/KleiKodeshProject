import os,sys
DB=sys.argv[1] if len(sys.argv)>1 else os.path.join(os.environ['APPDATA'],'otzaria','books','seforim.db')
DATA=os.environ.get('CLEAN_TEXT_DATA',os.path.join(os.path.dirname(os.path.abspath(__file__)),'data'))
os.makedirs(DATA,exist_ok=True)
import sqlite3,random,json
from compression import zstd
random.seed(7)
c=sqlite3.connect('file:'+DB.replace(os.sep,'/')+'?mode=ro',uri=True)
zd=zstd.ZstdDict(c.execute("select dict from zstd_dict").fetchone()[0])
def dec(b): return zstd.decompress(b,zstd_dict=zd).decode('utf-8') if isinstance(b,bytes) else b
cats={1:'Tanakh',5:'Mishna',12:'Bavli',19:'Yerushalmi',33:'Midrash',43:'Halacha',62:'Chasidut',546:'Machshava',1150:'Shut'}
out=[]
for cid,cn in cats.items():
  books=c.execute("select b.id from book b join category_closure cc on cc.descendantId=b.categoryId where cc.ancestorId=? and b.totalLines>20",(cid,)).fetchall()
  random.shuffle(books)
  for (bid,) in books[:40]:
    for lid,cont in c.execute("select l.id,lc.content from line l join line_content lc on lc.id=l.id where l.bookId=? order by random() limit 60",(bid,)):
      h=dec(cont)
      if h: out.append(dict(cat=cn,book=bid,line=h))
json.dump(out,open(os.path.join(DATA,'au.json'),'w',encoding='utf-8'),ensure_ascii=False)
print(len(out))
