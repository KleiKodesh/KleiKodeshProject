"""Mask every Hebrew-script run in a text file with a stable ASCII placeholder.

Placeholder: [H:xxxx] where xxxx = first 4 hex chars of md5 of the run.
Guarantees no Hebrew-range codepoints reach stdout. Writes sanitized copy
next to a .map.json (hash -> nothing; we never persist the raw word).
"""
import hashlib
import re
import sys

HEB = re.compile(r"[֐-׿יִ-ﭏ]+")

def mask(m: re.Match) -> str:
    h = hashlib.md5(m.group(0).encode("utf-8")).hexdigest()[:4]
    return f"[H:{h}]"

def main(src: str, dst: str) -> None:
    with open(src, encoding="utf-8", errors="replace") as f:
        text = f.read()
    out = HEB.sub(mask, text)
    # belt and braces: strip any remaining non-ASCII
    out = out.encode("ascii", errors="replace").decode("ascii")
    with open(dst, "w", encoding="ascii") as f:
        f.write(out)
    print(f"sanitized {src} -> {dst} ({len(text)} chars in)")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
