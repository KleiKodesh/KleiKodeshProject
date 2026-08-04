"""Masked stdout for corpus scripts — REQUIRED for anything that touches corpus text.

The network proxy scans every byte of the conversation payload, and corpus
vocabulary itself contains blocked terms (a session was killed by an eval
script echoing probe words — see .kiro/steering/agent-behavior.md). Therefore:
raw Hebrew never goes to stdout; it goes to files. Stdout gets stable
placeholders instead.

Usage — first line of every corpus script:

    import sys; sys.path.insert(0, "tools")  # or relative to cwd
    import masked; masked.install()

After install(), every print() is filtered: Hebrew-script runs become
[H:xxxx] (first 4 hex of md5 of the run) and any remaining non-ASCII is
replaced. The hash->word pairs are appended to tools/hashmap.tsv so a human
can decode placeholders locally; that file must never be read into the
conversation.

mask(text) is also available directly for masking single values.
"""
import hashlib
import io
import os
import re
import sys

HEB = re.compile(r"[֐-ׇא-׿יִ-ﭏ]+")
_MAP_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hashmap.tsv")
_known: set[str] = set()


def _load_known() -> None:
    if _known or not os.path.exists(_MAP_PATH):
        return
    try:
        with open(_MAP_PATH, encoding="utf-8") as f:
            for line in f:
                h = line.split("\t", 1)[0].strip()
                if h:
                    _known.add(h)
    except OSError:
        pass


def _record(h: str, word: str) -> None:
    if h in _known:
        return
    _known.add(h)
    try:
        with open(_MAP_PATH, "a", encoding="utf-8") as f:
            f.write(f"{h}\t{word}\n")
    except OSError:
        pass


def mask(text: str) -> str:
    """Replace Hebrew runs with [H:xxxx]; strip all other non-ASCII."""
    if not isinstance(text, str):
        text = str(text)
    _load_known()

    def repl(m: re.Match) -> str:
        word = m.group(0)
        h = hashlib.md5(word.encode("utf-8")).hexdigest()[:4]
        _record(h, word)
        return f"[H:{h}]"

    out = HEB.sub(repl, text)
    return out.encode("ascii", errors="replace").decode("ascii")


class _MaskedWriter(io.TextIOBase):
    def __init__(self, raw):
        self._raw = raw

    def write(self, s: str) -> int:
        self._raw.write(mask(s))
        return len(s)

    def flush(self) -> None:
        self._raw.flush()


def install() -> None:
    """Route stdout AND stderr through the mask — tracebacks leak Hebrew too."""
    sys.stdout = _MaskedWriter(sys.stdout)
    sys.stderr = _MaskedWriter(sys.stderr)
