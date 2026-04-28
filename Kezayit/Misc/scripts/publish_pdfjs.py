"""
Builds the PDF.js viewer from source (Misc/pdf.js-master/) and outputs
directly into vue-frontend/public/pdfjs/.

Run from the workspace root:
    python Misc/scripts/publish_pdfjs.py

This is called automatically as part of the Vue build via the `prebuild` npm script.
"""

import os
import subprocess
import sys
from pathlib import Path

WORKSPACE_ROOT = Path(__file__).resolve().parents[2]
PDFJS_SOURCE = WORKSPACE_ROOT / "Misc" / "pdf.js-master"
DESTINATION = WORKSPACE_ROOT / "vue-frontend" / "public" / "pdfjs"


def main():
    if not PDFJS_SOURCE.exists():
        print(f"ERROR: PDF.js source folder not found: {PDFJS_SOURCE}", file=sys.stderr)
        sys.exit(1)

    env = os.environ.copy()
    env["PDFJS_OUTPUT_DIR"] = str(DESTINATION)

    print(f"Building PDF.js viewer → {DESTINATION}")

    result = subprocess.run(
        ["npx", "gulp", "generic"],
        cwd=str(PDFJS_SOURCE),
        env=env,
    )

    if result.returncode != 0:
        print("ERROR: gulp generic build failed", file=sys.stderr)
        sys.exit(result.returncode)

    print("PDF.js viewer built successfully.")


if __name__ == "__main__":
    main()
