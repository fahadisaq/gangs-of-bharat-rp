"""Tests for tools/docs/md2pdf.py. Run: python3 -m pytest tools/docs -q"""

from __future__ import annotations

import re
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import md2pdf

SAMPLE = """# Sample Chapter

Intro paragraph with **bold** text and a [link](other.md).

## Prices

- Cycle costs ₹5k
- Nested list:
  - Ranga Rover ₹1.5Cr – premium ★

| Item | Price |
| --- | --- |
| Bike | ₹60k |
| Flat | ₹20L |

नमस्ते, मायानगरी में आपका स्वागत है।

```
code line
```
"""


def _convert(tmp_path: Path, compress: bool = False) -> tuple[bytes, int]:
    src = tmp_path / "sample.md"
    src.write_text(SAMPLE, encoding="utf-8")
    out = tmp_path / "out.pdf"
    pages = md2pdf.convert([src], out, compress=compress)
    return out.read_bytes(), pages


def _streams(data: bytes) -> bytes:
    chunks = []
    for m in re.finditer(rb"<< /Length (\d+)( /Filter /FlateDecode)? >>\nstream\n", data):
        start = m.end()
        body = data[start:start + int(m.group(1))]
        chunks.append(zlib.decompress(body) if m.group(2) else body)
    return b"\n".join(chunks)


def test_pdf_structure(tmp_path: Path) -> None:
    data, pages = _convert(tmp_path, compress=True)
    assert data.startswith(b"%PDF-")
    assert data.rstrip().endswith(b"%%EOF")

    m = re.search(rb"startxref\n(\d+)\n%%EOF\s*$", data)
    assert m, "startxref missing"
    offset = int(m.group(1))
    assert data[offset:offset + 4] == b"xref"

    # Every xref entry must point at the matching "N 0 obj" header.
    xref = data[offset:].split(b"trailer")[0].splitlines()
    count = int(xref[1].split()[1])
    entries = xref[3:3 + count - 1]
    for num, entry in enumerate(entries, start=1):
        pos = int(entry[:10])
        assert data[pos:].startswith(b"%d 0 obj" % num)

    # Title page plus one content page.
    assert pages == 2
    assert b"/Count %d" % pages in data
    assert len(re.findall(rb"/Type /Page ", data)) == pages


def test_text_mapping(tmp_path: Path) -> None:
    data, _ = _convert(tmp_path, compress=False)
    text = _streams(data)
    assert b"Rs.5k" in text
    assert b"Rs.1.5Cr - premium *" in text
    assert b"Sample Chapter" in text
    assert b"Bike" in text and b"Rs.60k" in text
    assert md2pdf.HINDI_PLACEHOLDER.encode() in text
    assert b"**" not in text
    assert b"Page 2 of 2" in text


def test_sanitize_and_wrap() -> None:
    assert md2pdf.sanitize("₹500") == "Rs.500"
    assert md2pdf.sanitize("“hi” — ok") == '"hi" - ok'
    assert md2pdf.sanitize("नमस्ते") == md2pdf.HINDI_PLACEHOLDER
    lines = md2pdf.wrap_text("word " * 200, md2pdf.FONT_REG, 10, 200)
    assert len(lines) > 1
    assert all(md2pdf.text_width(ln, md2pdf.FONT_REG, 10) <= 200 for ln in lines)


def test_cli(tmp_path: Path) -> None:
    src = tmp_path / "a.md"
    src.write_text("# A\n\ntext\n", encoding="utf-8")
    out = tmp_path / "cli.pdf"
    assert md2pdf.main(["--out", str(out), str(src)]) == 0
    assert out.read_bytes().startswith(b"%PDF-")
