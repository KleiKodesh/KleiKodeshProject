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
  rabbinic forms (OPTIONAL, off unless a Sefaria lexicon DB is passed) —
                Jastrow/Krupnik headword->form groups, added ONLY where
                lexical.db has no group, homograph-guarded, emitted as 'infl'
                rows behind the frequency gate; provenance kept per lemma in
                sefaria_source_of so the artifact can tag the rows

Consumers: build_expansion_table.py (FTS artifact). The demo keeps its own
inline copy — regenerate and re-audit it before porting changes there.

All corpus text stays in process / on disk; nothing here prints Hebrew.
"""
import csv
import json
import os
import re
import sqlite3
from collections import Counter, defaultdict

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

# Sefaria lexicon ids (sefaria-lexicon.db `lexicon` table) -> the exp.source
# tag their rows carry. Listed in priority order: a headword found in several
# lexicons is tagged with the first. BDB/Strong are biblical and deliberately
# absent — lexical.db already covers that register.
SEFARIA_SOURCE_BY_LEXICON = {4: "sefaria-jastrow", 10: "sefaria-krupnik",
                             3: "sefaria-klein"}
SEFARIA_DEFAULT_LEXICONS = (4, 10)              # Klein (3) only on request
SEFARIA_MIN_WORD_LENGTH = 3
HEBREW_LETTERS_ONLY = re.compile(f"[{chr(0x05D0)}-{chr(0x05EA)}]+")   # alef..tav
# headword decorations that are not part of the word: Jastrow's leading '*'
# and trailing homograph indexes (roman numerals / superscript digits, lists)
SEFARIA_HEADWORD_MARK = re.compile(r"^\*+")
SEFARIA_HOMOGRAPH_INDEX = re.compile(
    r"(?:[\s,]*(?:[IVX]+|[²³¹⁰-⁹]+))+[\s,]*$")
# kaf mem nun pe tsadi -> their word-final letterforms
FINAL_LETTER_OF = str.maketrans("".join(map(chr, (0x05DB, 0x05DE, 0x05E0, 0x05E4, 0x05E6))),
                                "".join(map(chr, (0x05DA, 0x05DD, 0x05DF, 0x05E3, 0x05E5))))


def finalize_last_letter(s):
    """word-final kaf/mem/nun/pe/tsadi must take the final letterform —
    stripping a suffix exposes a non-final letter that matches nothing"""
    return s[:-1] + s[-1].translate(FINAL_LETTER_OF) if s else s


def clean(t):
    t = TAG.sub("", t)
    t = PAREN_REF.sub(" ", t)
    return NIKUD.sub("", t).replace(MAQAF, " ")


def tokens(t):
    return TOK.findall(clean(t))


def norm(t):
    return "".join(TOK.findall(clean(t)))


def sefaria_single_word(text):
    """A Sefaria form -> (bare word, None), or (None, rejection reason).
    Pointing/cantillation are stripped and the last letter finalized; a form
    that is several words, too short, or carries any non-letter is rejected."""
    word = clean(text or "").strip()
    if not word:
        return None, "empty"
    if any(c.isspace() for c in word):
        return None, "multi-word"
    if not HEBREW_LETTERS_ONLY.fullmatch(word):
        return None, "non-hebrew-letter"
    if len(word) < SEFARIA_MIN_WORD_LENGTH:
        return None, "under-3-letters"
    return finalize_last_letter(word), None


def sefaria_headword(text):
    """A Sefaria headword minus its decorations, then shaped like a form."""
    text = SEFARIA_HEADWORD_MARK.sub("", (text or "").strip())
    return sefaria_single_word(SEFARIA_HOMOGRAPH_INDEX.sub("", text))


def read_sefaria_headword_form_pairs(con, lexicon_id):
    """Every (raw headword, raw form) one lexicon offers: its word_form
    lookups, each entry's own headword, its alternative headwords, and its
    listed plurals. The headword itself is a member so the homograph guard
    also sees headwords that double as another entry's form."""
    yield from con.execute(
        """select l.headword, f.form from word_form_lookup l
           join word_form f on f.id = l.word_form_id where l.lexicon_id = ?""",
        (lexicon_id,))
    yield from con.execute(
        "select headword, headword from entry where lexicon_id = ?", (lexicon_id,))
    yield from con.execute(
        """select e.headword, a.headword from entry_alt_headword a
           join entry e on e.id = a.entry_id where e.lexicon_id = ?""",
        (lexicon_id,))
    for headword, plurals in con.execute(
            """select headword, plural_form from entry
               where lexicon_id = ? and plural_form is not null""", (lexicon_id,)):
        for plural in sefaria_plural_forms(plurals):
            yield headword, plural


SEFARIA_RTL_SPAN = re.compile(r'<span dir="rtl">([^<]*)</span>')


def sefaria_plural_forms(plural_form):
    """entry.plural_form is a JSON string list in Jastrow but an HTML note
    ('(pl. <span dir="rtl">..</span>, also ..)') in Klein — read both."""
    if plural_form.lstrip().startswith("["):
        return json.loads(plural_form)
    return SEFARIA_RTL_SPAN.findall(plural_form)


class Expander:
    def __init__(self, corpus_freq, sefaria_lexicon_path=None,
                 sefaria_lexicon_ids=SEFARIA_DEFAULT_LEXICONS):
        """corpus_freq: {surface: count} for the register being served.
        sefaria_lexicon_path: optional sefaria-lexicon.db; None keeps the
        expansion exactly as it was before the rabbinic-forms channel."""
        self.freq = corpus_freq
        total = sum(corpus_freq.values())
        self.match_max = max(500, int(MATCH_MAX_RATE * total))
        self.sefaria_forms_of = {}         # headword -> its admitted forms
        self.sefaria_headword_of = {}      # admitted form -> its headword
        self.sefaria_source_of = {}        # headword -> exp.source tag
        self.sefaria_drop_counts = Counter()
        self._load_lexicon()
        self._load_synonyms()
        self._load_bridge()
        if sefaria_lexicon_path:
            self._load_sefaria_rabbinic_forms(sefaria_lexicon_path,
                                              sefaria_lexicon_ids)
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

    def _load_sefaria_rabbinic_forms(self, path, lexicon_ids):
        """Fourth inflection source: Sefaria's rabbinic lexicons. Additive
        only — a group is admitted where lexical.db knows neither the headword
        nor the form, so every existing fold and group stays as it was."""
        con = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
        headwords_of_form, source_of_headword = \
            self._read_sefaria_groups(con, lexicon_ids)
        con.close()
        forms_of_headword = self._drop_sefaria_homographs(headwords_of_form)
        self._admit_sefaria_groups(forms_of_headword, source_of_headword)

    def _read_sefaria_groups(self, con, lexicon_ids):
        headwords_of_form = defaultdict(set)
        source_of_headword = {}
        drops = self.sefaria_drop_counts
        for lexicon_id in lexicon_ids:
            tag = SEFARIA_SOURCE_BY_LEXICON[lexicon_id]
            for raw_headword, raw_form in read_sefaria_headword_form_pairs(
                    con, lexicon_id):
                headword, reason = sefaria_headword(raw_headword)
                if reason:
                    drops[f"read: headword {reason}"] += 1
                    continue
                form, reason = sefaria_single_word(raw_form)
                if reason:
                    drops[f"read: form {reason}"] += 1
                    continue
                headwords_of_form[form].add(headword)
                source_of_headword.setdefault(headword, tag)
        return headwords_of_form, source_of_headword

    def _drop_sefaria_homographs(self, headwords_of_form):
        """A form listed under 2+ distinct headwords cannot be folded to one
        of them safely — it leaves every group."""
        forms_of_headword = defaultdict(set)
        for form, headwords in headwords_of_form.items():
            if len(headwords) > 1:
                self.sefaria_drop_counts["homograph: form under 2+ headwords"] += 1
                continue
            (headword,) = headwords
            if form != headword:
                forms_of_headword[headword].add(form)
        return forms_of_headword

    def _admit_sefaria_groups(self, forms_of_headword, source_of_headword):
        """Never override: the headword and each form must fold to themselves
        today (no lexical.db group), and a form with its own synonym/bridge
        rows stays a lemma so those rows are not lost to the fold."""
        in_lexical_group = self.bases | self.fold_cands.keys()
        drops = self.sefaria_drop_counts
        for headword, forms in forms_of_headword.items():
            if headword in in_lexical_group:
                drops["existing group: headword in lexical.db (group)"] += 1
                continue
            admitted = set()
            for form in forms:
                if form in in_lexical_group:
                    drops["existing group: form in lexical.db"] += 1
                elif form in self.syn or form in self.bridge:
                    drops["existing rows: form has own syn/bridge"] += 1
                else:
                    admitted.add(form)
            if not admitted:
                continue
            self.sefaria_forms_of[headword] = admitted
            self.sefaria_source_of[headword] = source_of_headword[headword]
            for form in admitted:
                self.sefaria_headword_of[form] = headword

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
                # no lexical group: only an admitted Sefaria form folds away
                out = self.sefaria_headword_of.get(t, t)
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

    def sefaria_in_corpus_forms(self, base, exclude=None):
        """The admitted Sefaria forms of base that pass the same gates as the
        lexical channel, plus the frequency cap (matchable): a dictionary
        form list has no fold-back consistency to vouch for a very frequent
        form, so it is held to the synonym-strength shape gate."""
        kept = set()
        for f in self.sefaria_forms_of.get(base, ()):
            if f == exclude:
                continue
            if not self.freq.get(f):
                self.sefaria_drop_counts["gate: not in corpus"] += 1
            elif not self.same_lexeme(f, base):
                self.sefaria_drop_counts["gate: same_lexeme"] += 1
            elif not self.matchable(f):
                self.sefaria_drop_counts["gate: frequency cap (matchable)"] += 1
            else:
                kept.add(f)
        return kept

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
        for f in self.sefaria_in_corpus_forms(base, term):
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
        self.sefaria_drop_counts["gate: non-final last letter"] += sum(
            1 for f in out if f in self.sefaria_headword_of and f.endswith(nonfinal))
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
