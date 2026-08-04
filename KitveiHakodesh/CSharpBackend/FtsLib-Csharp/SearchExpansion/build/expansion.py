"""The validated search-expansion channel stack — canonical implementation.

This is the productized form of the guard stack that survived four
adversarial audit rounds in build_search_demo3.py (FINDINGS 26):

  inflections — lexicon surface group, fold-back consistency with a
                containment relaxation; no frequency cap (guard makes
                high-frequency genuine forms safe)
  synonyms    — dictionary link table, minus blocklist, content-shape gate
                (3+ letters, corpus freq <= 500 per matched form) and a
                gloss-overlap sense gate (reject when both glosses are
                substantial yet share zero defining tokens)
  bridge      — Targum mutual-best translation pairs, content-shape gated

Consumers: build_expansion_table.py (FTS artifact). The demo keeps its own
inline copy — regenerate and re-audit it before porting changes there.

All corpus text stays in process / on disk; nothing here prints Hebrew.
"""
import csv
import os
import re
import sqlite3
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
SEFORIM = "C:/ProgramData/otzaria/books/seforim.db"
LEXICAL = "C:/Users/Public/Documents/Dictionary/Backup/lexical.db"
DICT_DB = os.path.expandvars(
    r"%LOCALAPPDATA%\KleiKodesh\KitveiHakodesh\dictionary\KitveiHakodesh_dictionary.db")
BRIDGE_CSV = os.path.join(HERE, "targum_bridge.csv")
BLOCKLIST = os.path.join(HERE, "tools", "syn_blocklist.txt")

TAG = re.compile(r"<[^>]+>")
NIKUD = re.compile(r"[\u0591-\u05BD\u05BF-\u05C7]")
TOK = re.compile(r"[\u05D0-\u05EA]{2,}")
PAREN_REF = re.compile(r"\([^()]{1,6}\)")
MAQAF = "\u05BE"

MATCH_MAX_RATE = 500 / 306_873   # tuned on Tanach; scales as tokens-per-corpus
GLOSS_DF_CAP = 1000
GLOSS_MIN_TOKENS = 5


def clean(t):
    t = TAG.sub("", t)
    t = PAREN_REF.sub(" ", t)
    return NIKUD.sub("", t).replace(MAQAF, " ")


def tokens(t):
    return TOK.findall(clean(t))


def norm(t):
    return "".join(TOK.findall(clean(t)))


class Expander:
    def __init__(self, corpus_freq):
        """corpus_freq: {surface: count} for the register being served."""
        self.freq = corpus_freq
        total = sum(corpus_freq.values())
        self.match_max = max(500, int(MATCH_MAX_RATE * total))
        self._load_lexicon()
        self._load_synonyms()
        self._load_bridge()
        self._load_gloss()
        self._lemma_cache = {}

    # ── data loading ────────────────────────────────────────────────────
    def _load_lexicon(self):
        con = sqlite3.connect(f"file:{LEXICAL}?mode=ro", uri=True)
        self.bases = {r[0] for r in con.execute("select value from base")}
        self.fold_cands = defaultdict(set)
        self.surfaces_of = defaultdict(set)
        q = """select s.value, b.value from surface s join base b on b.id = s.base_id
               union
               select v.value, b.value
               from surface_variant sv
               join variant v on v.id = sv.variant_id
               join surface s on s.id = sv.surface_id
               join base b on b.id = s.base_id"""
        for surf, base in con.execute(q):
            self.surfaces_of[base].add(surf)
            if surf not in self.bases:
                self.fold_cands[surf].add(base)
        con.close()

    def _load_synonyms(self):
        blocked = set()
        if os.path.exists(BLOCKLIST):
            with open(BLOCKLIST, encoding="utf-8") as f:
                blocked = {norm(l.strip()) for l in f if l.strip()}
        con = sqlite3.connect(f"file:{DICT_DB}?mode=ro", uri=True)
        self.syn = defaultdict(set)
        for a, b in con.execute(
                """select w1.headword, w2.headword from link l
                   join word w1 on w1.id = l.word_id
                   join word w2 on w2.id = l.target_id where l.kind_id = 1"""):
            a, b = norm(a), norm(b)
            if a and b and a != b and a not in blocked and b not in blocked:
                self.syn[a].add(b)
                self.syn[b].add(a)
        con.close()

    def _load_bridge(self):
        self.bridge = defaultdict(set)
        with open(BRIDGE_CSV, encoding="utf-8") as f:
            for row in csv.DictReader(f):
                ak = [k for k in row if "aram" in k.lower()][0]
                hk = [k for k in row if "heb" in k.lower()][0]
                a, h = norm(row[ak]), norm(row[hk])
                if a and h:
                    self.bridge[h].add(a)
                    self.bridge[a].add(h)

    def _load_gloss(self):
        con = sqlite3.connect(f"file:{DICT_DB}?mode=ro", uri=True)
        texts = defaultdict(list)
        for hw, txt in con.execute(
                "select w.headword, s.text from sense s "
                "join word w on w.id = s.word_id where s.text is not null"):
            texts[norm(hw)].append(txt)
        con.close()
        prof = {}
        df = defaultdict(int)
        for hw, ts in texts.items():
            toks = set()
            for t in ts:
                toks.update(TOK.findall(clean(t)))
            toks.discard(hw)
            if len(toks) >= 3:
                prof[hw] = toks
                for tk in toks:
                    df[tk] += 1
        self.gloss = {hw: {t for t in p if df[t] <= GLOSS_DF_CAP}
                      for hw, p in prof.items()}

    # ── the guard stack ─────────────────────────────────────────────────
    def lemma_of(self, t):
        got = self._lemma_cache.get(t)
        if got is not None:
            return got
        if t in self.bases:
            out = t
        else:
            cands = self.fold_cands.get(t)
            if not cands:
                out = t
            else:
                biblical = [b for b in cands if self.freq.get(b)]
                out = max(biblical or list(cands), key=len)
        self._lemma_cache[t] = out
        return out

    def same_lexeme(self, f, base):
        if self.lemma_of(f) == base:
            return True
        return len(base) >= 3 and base in f

    def in_corpus_forms(self, base, exclude=None):
        return {s for s in self.surfaces_of.get(base, ())
                if self.freq.get(s) and self.same_lexeme(s, base)} - {exclude}

    def matchable(self, f):
        return len(f) >= 3 and 0 < self.freq.get(f, 0) <= self.match_max

    def sense_compatible(self, a, b):
        pa, pb = self.gloss.get(a), self.gloss.get(b)
        if pa and pb and len(pa) >= GLOSS_MIN_TOKENS and len(pb) >= GLOSS_MIN_TOKENS:
            return bool(pa & pb)
        return True                          # absence of data is not evidence

    def expand(self, term):
        """term (surface, normalized) -> list of (form, channel, rank).

        Channels: 'infl' | 'syn' | 'bridge'. Rank orders within channel by
        corpus frequency (descending) — most attested form first.
        """
        base = self.lemma_of(term)
        out = {}
        for f in self.in_corpus_forms(base, term):
            out.setdefault(f, "infl")
        for s in self.syn.get(term, set()) | self.syn.get(base, set()):
            if len(s) < 3 or not self.sense_compatible(base, s):
                continue
            for f in {s} | self.in_corpus_forms(self.lemma_of(s), term):
                if self.matchable(f):
                    out.setdefault(f, "syn")
        for f in (self.bridge.get(term, set()) | self.bridge.get(base, set())) \
                - {term, base}:
            if self.matchable(f):
                out.setdefault(f, "bridge")
        # word-final kaf/mem/nun/pe/tsadi must use final letterforms; a token
        # ending in the non-final form is a normalization artifact
        nonfinal = tuple("כמנפצ")
        out = {f: ch for f, ch in out.items() if not f.endswith(nonfinal)}
        ordered = sorted(out.items(),
                         key=lambda kv: (kv[1] != "infl", kv[1] != "syn",
                                         -self.freq.get(kv[0], 0)))
        ranked = []
        seen_rank = defaultdict(int)
        for f, ch in ordered:
            ranked.append((f, ch, seen_rank[ch]))
            seen_rank[ch] += 1
        return ranked


def corpus_freq(corpus="tanach"):
    con = sqlite3.connect(f"file:{SEFORIM}?mode=ro", uri=True)
    where = "where bookId between 1 and 39" if corpus == "tanach" else ""
    freq = defaultdict(int)
    for (c,) in con.execute(f"select content from line {where}"):
        for t in tokens(c):
            freq[t] += 1
    con.close()
    return dict(freq)


def tanach_freq():
    return corpus_freq("tanach")
