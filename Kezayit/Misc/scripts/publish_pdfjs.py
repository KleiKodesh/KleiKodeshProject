"""
Copies the PDF.js viewer source from Misc/pdfjs-master/ to vue-frontend/public/pdfjs/.

Run from the workspace root:
    python Misc/scripts/publish_pdfjs.py

This is called automatically as part of the Vue build via the `prebuild` npm script.
"""

import shutil
import sys
from pathlib import Path

WORKSPACE_ROOT = Path(__file__).resolve().parents[2]
SOURCE = WORKSPACE_ROOT / "Misc" / "pdfjs-master"
DESTINATION = WORKSPACE_ROOT / "vue-frontend" / "public" / "pdfjs"


def main():
    if not SOURCE.exists():
        print(f"ERROR: Source folder not found: {SOURCE}", file=sys.stderr)
        sys.exit(1)

    if DESTINATION.exists():
        shutil.rmtree(DESTINATION)

    shutil.copytree(SOURCE, DESTINATION)
    print(f"Published PDF.js viewer: {SOURCE} → {DESTINATION}")


if __name__ == "__main__":
    main()
