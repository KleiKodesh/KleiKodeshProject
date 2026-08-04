"""Extract a sanitized ASCII digest of a Claude Code session transcript.

- Hebrew runs -> [H:xxxx] (md5/4 stable hash)
- All other non-ASCII stripped
- Tool inputs/results truncated hard; assistant/user text kept generously
"""
import hashlib
import json
import re
import sys

HEB = re.compile(r"[֐-׿יִ-ﭏ]+")

def mask(text: str) -> str:
    if not isinstance(text, str):
        text = str(text)
    out = HEB.sub(lambda m: f"[H:{hashlib.md5(m.group(0).encode()).hexdigest()[:4]}]", text)
    return out.encode("ascii", errors="replace").decode("ascii")

def clip(s: str, n: int) -> str:
    s = mask(s)
    return s if len(s) <= n else s[:n] + f" ...[+{len(s)-n} chars]"

def main(src: str, dst: str) -> None:
    lines_out = []
    with open(src, encoding="utf-8", errors="replace") as f:
        for raw in f:
            try:
                rec = json.loads(raw)
            except json.JSONDecodeError:
                continue
            t = rec.get("type")
            if t == "summary":
                lines_out.append(f"== SUMMARY: {clip(rec.get('summary',''), 500)}")
                continue
            msg = rec.get("message") or {}
            role = msg.get("role")
            content = msg.get("content")
            if content is None:
                continue
            if isinstance(content, str):
                if role == "user":
                    lines_out.append(f"\n### USER:\n{clip(content, 3000)}")
                continue
            for block in content:
                bt = block.get("type")
                if bt == "text":
                    tag = "ASSISTANT" if role == "assistant" else role.upper()
                    lines_out.append(f"\n### {tag}:\n{clip(block.get('text',''), 4000)}")
                elif bt == "tool_use":
                    name = block.get("name", "?")
                    inp = block.get("input", {})
                    brief = {k: clip(str(v), 250) for k, v in list(inp.items())[:6]}
                    lines_out.append(f"[tool_use {name}] {json.dumps(brief)[:600]}")
                elif bt == "tool_result":
                    c = block.get("content")
                    if isinstance(c, list):
                        c = " ".join(x.get("text", "") for x in c if isinstance(x, dict))
                    lines_out.append(f"[tool_result] {clip(str(c), 400)}")
    with open(dst, "w", encoding="ascii") as f:
        f.write("\n".join(lines_out))
    print(f"wrote {dst}: {len(lines_out)} blocks")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
