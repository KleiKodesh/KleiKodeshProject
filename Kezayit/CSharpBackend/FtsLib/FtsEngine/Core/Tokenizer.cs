using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FtsEngine.Core
{
    /// <summary>
    /// Extracts terms from HTML/Hebrew/English text.
    /// Identical behaviour to FtsLib.Tokenizer — reused here to keep FtsEngine self-contained.
    /// Returns a reused HashSet (cleared on each call) — not thread-safe.
    /// </summary>
    internal sealed class Tokenizer
    {
        private readonly HashSet<string> _terms  = new HashSet<string>();
        private readonly StringBuilder   _buffer = new StringBuilder(64);
        private readonly char[]          _tagName = new char[16];
        private int  _tagLen;
        private bool _inTag;

        public HashSet<string> Extract(string text)
        {
            _terms.Clear();
            if (string.IsNullOrEmpty(text)) return _terms;

            _buffer.Clear();
            _tagLen = 0;
            _inTag  = false;
            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                if (_inTag)
                {
                    if (c == '>')
                    {
                        if (IsBlockTag(_tagName, _tagLen)) Flush();
                        _inTag = false; _tagLen = 0;
                    }
                    else if (_tagLen < 16 && c != ' ' && c != '\t' && c != '/')
                        _tagName[_tagLen++] = c;
                    continue;
                }

                if (c == '<') { _inTag = true; _tagLen = 0; continue; }

                if (c == '&') { HandleEntity(text, len, ref i); continue; }

                if (c >= '\u05B0' && c <= '\u05C7') continue;
                if (c > 127 && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

                if (IsLetter(c))
                {
                    if (c >= 'A' && c <= 'Z') c = (char)(c | 32);
                    _buffer.Append(c);
                }
                else Flush();
            }
            Flush();
            return _terms;
        }

        private static bool IsBlockTag(char[] name, int len)
        {
            if (len == 0) return false;
            int start = (name[0] == '/' || name[0] == '!') ? 1 : 0;
            int tlen  = len - start;
            if (tlen == 0) return false;
            char c0 = name[start]; if (c0 >= 'A' && c0 <= 'Z') c0 = (char)(c0 | 32);
            switch (tlen)
            {
                case 1: return c0 == 'p';
                case 2:
                {
                    char c1 = name[start+1]; if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1|32);
                    return (c0=='b'&&c1=='r')||(c0=='h'&&c1=='r')||(c0=='l'&&c1=='i')||
                           (c0=='u'&&c1=='l')||(c0=='o'&&c1=='l')||(c0=='t'&&c1=='r')||
                           (c0=='t'&&c1=='d')||(c0=='t'&&c1=='h')||(c0=='d'&&c1=='d')||
                           (c0=='d'&&c1=='t')||(c0=='h'&&c1>='1'&&c1<='6');
                }
                case 3:
                {
                    char c1=name[start+1]; if(c1>='A'&&c1<='Z')c1=(char)(c1|32);
                    char c2=name[start+2]; if(c2>='A'&&c2<='Z')c2=(char)(c2|32);
                    return (c0=='d'&&c1=='i'&&c2=='v')||(c0=='p'&&c1=='r'&&c2=='e')||
                           (c0=='n'&&c1=='a'&&c2=='v');
                }
                default:
                {
                    var sb = new StringBuilder(tlen);
                    for (int i = start; i < start+tlen; i++) { char ch=name[i]; if(ch>='A'&&ch<='Z')ch=(char)(ch|32); sb.Append(ch); }
                    string tag = sb.ToString();
                    return tag=="div"||tag=="main"||tag=="table"||tag=="aside"||
                           tag=="header"||tag=="footer"||tag=="figure"||tag=="section"||
                           tag=="article"||tag=="caption"||tag=="figcaption"||tag=="blockquote";
                }
            }
        }

        private void HandleEntity(string text, int len, ref int i)
        {
            int start = i+1, end = start;
            while (end < len && end-start < 10 && text[end] != ';') end++;
            if (end >= len || text[end] != ';') return;
            i = end;
            int elen = end-start; if (elen == 0) return;
            char e0 = text[start];
            if (e0=='n'&&elen==4&&text[start+1]=='b'&&text[start+2]=='s'&&text[start+3]=='p'){Flush();return;}
            if (e0=='e'&&elen==4&&text[start+1]=='n'&&text[start+2]=='s'&&text[start+3]=='p'){Flush();return;}
            if (e0=='e'&&elen==4&&text[start+1]=='m'&&text[start+2]=='s'&&text[start+3]=='p'){Flush();return;}
            if (e0=='#'&&elen>1){int v=0;bool ok=true;for(int k=start+1;k<end;k++){char d=text[k];if(d<'0'||d>'9'){ok=false;break;}v=v*10+(d-'0');}if(ok&&(v==160||v==8194||v==8195||v==8201))Flush();}
        }

        private void Flush()
        {
            if (_buffer.Length == 0) return;
            _terms.Add(_buffer.ToString());
            _buffer.Clear();
        }

        private static bool IsLetter(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '\u05D0' && c <= '\u05EA');
    }
}
