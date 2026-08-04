"""
Compare scoring configurations side by side.

Builds several indexes into index-<name>/ and prints the same probe words under
each, so the effect of the BM25-style normalizations is visible rather than
guessed at.

Usage:  python compare.py            build + compare the standard configs
        python compare.py --no-build reuse existing index-* dirs
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).parent

# name -> extra build flags.
# NOTE: --length-norm-b now defaults to 0.75, so "baseline" (pure PPMI) has to
# turn it OFF explicitly to remain a real baseline.
CONFIGS: dict[str, list[str]] = {
    "baseline": ["--length-norm-b", "0"],
    "idf-df": ["--length-norm-b", "0", "--idf-weight", "0.6", "--idf-basis", "df"],
    "idf-degree": ["--length-norm-b", "0", "--idf-weight", "0.6",
                   "--idf-basis", "degree"],
    "saturate": ["--length-norm-b", "0", "--saturate-k", "1.2"],
    "lennorm": ["--length-norm-b", "0.75"],                    # the shipped default
    "bm25": ["--length-norm-b", "0.75", "--idf-weight", "0.6",
             "--saturate-k", "1.2", "--min-ctx-df", "3"],
    "bm25-strict": ["--length-norm-b", "0.75", "--idf-weight", "0.85",
                    "--saturate-k", "0.8", "--min-ctx-df", "5"],
}

# The Numbers-7 princes: the burstiness artifact these knobs should kill.
NUM7_NAMES = {"אחירע", "שלמיאל", "פגעיאל", "אבידנ", "גמליאל", "אליצור",
              "אחיעזר", "אליספ", "עכרנ", "שדיאור", "פדהצור", "דעואל",
              "גדעני", "צוער", "עמישדי", "נחשונ", "אליאב", "נתנאל"}

PROBES = ["מזבח", "זהב", "מלחמה", "מלכ", "לחמ", "שבת"]


def build(name: str, flags: list[str]) -> Path:
    out = HERE / f"index-{name}"
    print(f"\n=== building '{name}'  {' '.join(flags) or '(defaults)'} ===")
    r = subprocess.run(
        [sys.executable, str(HERE / "build_index.py"), "--out", str(out), *flags],
        capture_output=True, text=True, encoding="utf-8",
    )
    if r.returncode != 0:
        print(r.stdout, r.stderr)
        raise SystemExit(f"build '{name}' failed")
    for line in r.stdout.splitlines():
        if any(k in line for k in ("edges kept", "words (min-count", "done in")):
            print("   ", line.strip())
    return out


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-build", action="store_true")
    args = ap.parse_args()

    from query import AssocIndex

    dirs = {}
    for name, flags in CONFIGS.items():
        d = HERE / f"index-{name}"
        if not args.no_build:
            d = build(name, flags)
        if not (d / "meta.json").exists():
            print(f"  (skipping '{name}' — not built)")
            continue
        dirs[name] = d

    idx = {n: AssocIndex(d) for n, d in dirs.items()}

    print("\n\n" + "=" * 78)
    print("  TOP-8 ASSOCIATIONS BY CONFIG")
    print("=" * 78)
    for probe in PROBES:
        print(f"\n### {probe}")
        for name, ix in idx.items():
            res = ix.neighbors(probe, 8)
            words = ", ".join(w for w, _ in res) or "(none)"
            print(f"  {name:<12s} {words}")

    print("\n\n" + "=" * 78)
    print("  BURSTINESS PROBE — Numbers 7 princes leaking into 'קרבנ'")
    print("=" * 78)
    for name, ix in idx.items():
        res = ix.neighbors("קרבנ", 20)
        bad = [w for w, _ in res if w in NUM7_NAMES]
        print(f"  {name:<12s} {len(bad):2d}/20 princes in top-20   {bad[:6]}")

    print("\n\n" + "=" * 78)
    print("  EXPANSION: 'מזבח קרבן'")
    print("=" * 78)
    for name, ix in idx.items():
        _, exp = ix.expand("מזבח קרבן", per_term=5, mode="assoc")
        print(f"  {name:<12s} {', '.join(w for w, _, _ in exp[:8])}")

    print("\n\n" + "=" * 78)
    print("  INDEX SHAPE")
    print("=" * 78)
    print(f"  {'config':<12s} {'vocab':>8s} {'edges':>10s} {'median deg':>11s} {'MB':>7s}")
    for name, ix in idx.items():
        degs = sorted(ix._slice(i)[1] - ix._slice(i)[0] for i in range(len(ix.words)))
        mb = (ix.meta["offsets_bytes"] + ix.meta["edges_bytes"]) / 1e6
        print(f"  {name:<12s} {ix.meta['vocab_size']:8,d} {ix.meta['edge_count']:10,d} "
              f"{degs[len(degs) // 2]:11d} {mb:7.2f}")

    for ix in idx.values():
        ix.close()


if __name__ == "__main__":
    main()
