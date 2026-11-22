#!/usr/bin/env python3
"""Convert an ordered list of Markdown files into one PDF.

Standard library only (Python 3.11). The PDF is written object by object
with the base-14 fonts Helvetica, Helvetica-Bold and Courier using
WinAnsiEncoding, so no font files are embedded.

Supported Markdown subset: ATX headings (#, ##, ###, ####), paragraphs,
bullet and numbered lists (nested by indentation), pipe tables, fenced
code blocks, block quotes and horizontal rules. Inline markup (bold,
italic, code, links) is stripped to plain text.

Characters outside cp1252 cannot be drawn with base-14 fonts. A line that
still contains such characters after the ASCII mapping below (for example
Devanagari Hindi) is replaced by a note pointing to the Markdown source.

Usage:
    python3 tools/docs/md2pdf.py --out docs/GangsOfBharat_GDD.pdf docs/design/*.md
"""

from __future__ import annotations

import argparse
import datetime as _dt
import re
import sys
import textwrap
import zlib
from dataclasses import dataclass, field
from pathlib import Path

HINDI_PLACEHOLDER = "[Hindi text: see Markdown source]"

# A4 in points.
PAGE_W = 595.28
PAGE_H = 841.89
MARGIN_L = 56.0
MARGIN_R = 56.0
MARGIN_T = 60.0
MARGIN_B = 60.0
CONTENT_W = PAGE_W - MARGIN_L - MARGIN_R

BODY_SIZE = 10.0
MONO_SIZE = 8.0
FOOTER_SIZE = 8.0
HEADING_SIZES = {1: 18.0, 2: 14.0, 3: 12.0, 4: 10.5}

FONT_REG = "F1"
FONT_BOLD = "F2"
FONT_MONO = "F3"
FONT_NAMES = {FONT_REG: "Helvetica", FONT_BOLD: "Helvetica-Bold", FONT_MONO: "Courier"}

# Character replacements applied before encoding to cp1252.
CHAR_MAP = {
    "\u20b9": "Rs.",  # Indian rupee sign
    "\u2605": "*",  # black star
    "\u2606": "*",  # white star
    "\u2013": "-",  # en dash
    "\u2014": "-",  # em dash
    "\u2212": "-",  # minus sign
    "\u2018": "'",
    "\u2019": "'",
    "\u201a": "'",
    "\u201c": '"',
    "\u201d": '"',
    "\u201e": '"',
    "\u2026": "...",
    "\u2192": "->",
    "\u2190": "<-",
    "\u2194": "<->",
    "\u21d2": "=>",
    "\u2248": "~",
    "\u2264": "<=",
    "\u2265": ">=",
    "\u2260": "!=",
    "\u2713": "v",
    "\u2714": "v",
    "\u2717": "x",
    "\u00a0": " ",
    "\u2009": " ",
    "\u200b": "",
}

# Advance widths (1/1000 em) for ASCII 32..126, from the Adobe AFM files.
_HELV = [
    278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
    556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
    1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
    667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
    333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
    556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
]
_HELV_BOLD = [
    278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
    556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
    975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
    667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
    333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
    611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
]
_WIDTHS = {FONT_REG: _HELV, FONT_BOLD: _HELV_BOLD}


def sanitize(text: str) -> str:
    """Map common Unicode punctuation to ASCII and replace un-encodable lines."""
    out = "".join(CHAR_MAP.get(ch, ch) for ch in text)
    try:
        out.encode("cp1252")
    except UnicodeEncodeError:
        return HINDI_PLACEHOLDER
    return out


_LINK_RE = re.compile(r"\[([^\]]+)\]\(([^)]+)\)")
_BOLD_RE = re.compile(r"(\*\*|__)(.+?)\1")
_ITALIC_RE = re.compile(r"(?<![\w*])([*_])(?!\s)(.+?)(?<!\s)\1(?![\w*])")
_CODE_RE = re.compile(r"`([^`]+)`")


def strip_inline(text: str) -> str:
    """Remove inline Markdown markup, keeping the visible text."""

    def _link(m: re.Match[str]) -> str:
        label, target = m.group(1), m.group(2)
        if target.startswith(("http://", "https://")):
            return f"{label} ({target})"
        return label

    text = _LINK_RE.sub(_link, text)
    text = _CODE_RE.sub(r"\1", text)
    text = _BOLD_RE.sub(r"\2", text)
    text = _ITALIC_RE.sub(r"\2", text)
    text = text.replace("<br>", " ").replace("<br/>", " ").replace("\\|", "|")
    return text


def text_width(text: str, font: str, size: float) -> float:
    if font == FONT_MONO:
        return len(text) * 0.6 * size
    table = _WIDTHS[font]
    total = 0
    for ch in text:
        code = ord(ch)
        total += table[code - 32] if 32 <= code <= 126 else 556
    return total * size / 1000.0


def wrap_text(text: str, font: str, size: float, width: float) -> list[str]:
    """Greedy word wrap using font metrics. Long words are split."""
    words = text.split()
    if not words:
        return [""]
    lines: list[str] = []
    current = ""
    for word in words:
        while text_width(word, font, size) > width:
            # Split an over-long word at the widest prefix that fits.
            cut = len(word)
            while cut > 1 and text_width(word[:cut], font, size) > width:
                cut -= 1
            if current:
                lines.append(current)
                current = ""
            lines.append(word[:cut])
            word = word[cut:]
        candidate = f"{current} {word}" if current else word
        if text_width(candidate, font, size) <= width:
            current = candidate
        else:
            lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines


@dataclass
class Page:
    ops: list[tuple[str, float, float, float, str]] = field(default_factory=list)
    rules: list[tuple[float, float, float]] = field(default_factory=list)


class Layout:
    """Places lines of text on A4 pages top to bottom."""

    def __init__(self) -> None:
        self.pages: list[Page] = []
        self.y = 0.0
        self.new_page()

    def new_page(self) -> None:
        self.pages.append(Page())
        self.y = PAGE_H - MARGIN_T

    @property
    def page_empty(self) -> bool:
        page = self.pages[-1]
        return not page.ops and not page.rules

    def ensure(self, height: float) -> None:
        if self.y - height < MARGIN_B and not self.page_empty:
            self.new_page()

    def space(self, amount: float) -> None:
        if not self.page_empty:
            self.y -= amount

    def line(self, text: str, font: str, size: float, x: float, leading: float) -> None:
        self.ensure(leading)
        self.y -= leading
        if text:
            self.pages[-1].ops.append((font, size, x, self.y, text))

    def rule(self) -> None:
        self.ensure(8)
        self.y -= 4
        self.pages[-1].rules.append((MARGIN_L, PAGE_W - MARGIN_R, self.y))
        self.y -= 4

    def paragraph(self, text: str, font: str = FONT_REG, size: float = BODY_SIZE,
                  indent: float = 0.0, first_prefix: str = "") -> None:
        leading = size * 1.35
        x = MARGIN_L + indent
        width = CONTENT_W - indent
        if first_prefix:
            prefix_w = text_width(first_prefix + " ", font, size)
            lines = wrap_text(text, font, size, width - prefix_w)
            for i, ln in enumerate(lines):
                if i == 0:
                    self.line(first_prefix, font, size, x, leading)
                    self.pages[-1].ops.append((font, size, x + prefix_w, self.y, ln))
                else:
                    self.line(ln, font, size, x + prefix_w, leading)
        else:
            for ln in wrap_text(text, font, size, width):
                self.line(ln, font, size, x, leading)


def _split_row(line: str) -> list[str]:
    row = line.strip()
    row = row.removeprefix("|")
    if row.endswith("|") and not row.endswith("\\|"):
        row = row[:-1]
    cells = re.split(r"(?<!\\)\|", row)
    return [c.strip() for c in cells]


_SEP_RE = re.compile(r"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$")


def render_table(layout: Layout, rows: list[list[str]]) -> None:
    """Render a pipe table in Courier with wrapped cells."""
    ncols = max(len(r) for r in rows)
    rows = [r + [""] * (ncols - len(r)) for r in rows]
    cells = [[sanitize(strip_inline(c)) for c in r] for r in rows]
    max_chars = int(CONTENT_W / (0.6 * MONO_SIZE))
    sep = " | "
    budget = max_chars - len(sep) * (ncols - 1)
    widths = [max(3, max(len(r[i]) for r in cells)) for i in range(ncols)]
    min_w = 6
    while sum(widths) > budget:
        widest = max(range(ncols), key=lambda i: widths[i])
        if widths[widest] <= min_w:
            break
        widths[widest] -= 1
    leading = MONO_SIZE * 1.3
    layout.space(3)
    for ri, row in enumerate(cells):
        wrapped = [textwrap.wrap(c, widths[i], break_long_words=True) or [""]
                   for i, c in enumerate(row)]
        height = max(len(w) for w in wrapped)
        layout.ensure(leading * height)
        font = FONT_MONO
        for k in range(height):
            parts = [(wrapped[i][k] if k < len(wrapped[i]) else "").ljust(widths[i])
                     for i in range(ncols)]
            layout.line(sep.join(parts).rstrip(), font, MONO_SIZE, MARGIN_L, leading)
        if ri == 0:
            dash = "-+-".join("-" * w for w in widths)
            layout.line(dash, font, MONO_SIZE, MARGIN_L, leading)
    layout.space(4)


_HEADING_RE = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")
_BULLET_RE = re.compile(r"^(\s*)([-*+])\s+(.*)$")
_NUMBER_RE = re.compile(r"^(\s*)(\d+[.)])\s+(.*)$")
_HR_RE = re.compile(r"^\s*([-*_])(\s*\1){2,}\s*$")


def render_markdown(layout: Layout, source: str) -> None:
    lines = source.splitlines()
    i = 0
    para: list[str] = []
    first_heading = True

    def flush_para() -> None:
        if para:
            text = " ".join(p.strip() for p in para)
            layout.paragraph(sanitize(strip_inline(text)))
            layout.space(4)
            para.clear()

    while i < len(lines):
        raw = lines[i]
        stripped = raw.strip()

        if stripped.startswith("```"):
            flush_para()
            i += 1
            layout.space(2)
            while i < len(lines) and not lines[i].strip().startswith("```"):
                code = sanitize(lines[i].rstrip().replace("\t", "    "))
                max_chars = int(CONTENT_W / (0.6 * MONO_SIZE))
                for chunk in textwrap.wrap(code, max_chars, drop_whitespace=False,
                                           replace_whitespace=False) or [""]:
                    layout.line(chunk, FONT_MONO, MONO_SIZE, MARGIN_L + 8, MONO_SIZE * 1.3)
                i += 1
            i += 1
            layout.space(4)
            continue

        if not stripped:
            flush_para()
            i += 1
            continue

        m = _HEADING_RE.match(stripped)
        if m:
            flush_para()
            level = min(len(m.group(1)), 4)
            text = sanitize(strip_inline(m.group(2)))
            size = HEADING_SIZES[level]
            if level == 1 and first_heading and not layout.page_empty:
                layout.new_page()
            first_heading = False
            layout.space(size * 0.8)
            layout.ensure(size * 1.3 + BODY_SIZE * 3)
            layout.paragraph(text, FONT_BOLD, size)
            if level == 1:
                layout.rule()
            layout.space(3)
            i += 1
            continue

        if _HR_RE.match(stripped):
            flush_para()
            layout.rule()
            i += 1
            continue

        if stripped.startswith("|"):
            flush_para()
            block: list[list[str]] = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                if not _SEP_RE.match(lines[i]):
                    block.append(_split_row(lines[i]))
                i += 1
            if block:
                render_table(layout, block)
            continue

        mb = _BULLET_RE.match(raw) or _NUMBER_RE.match(raw)
        if mb:
            flush_para()
            depth = len(mb.group(1).replace("\t", "    ")) // 2
            marker = mb.group(2)
            prefix = "\u2022" if marker in "-*+" else marker
            text = mb.group(3)
            i += 1
            # Lazy continuation lines of the same item.
            while i < len(lines):
                nxt = lines[i]
                if (not nxt.strip() or _BULLET_RE.match(nxt) or _NUMBER_RE.match(nxt)
                        or _HEADING_RE.match(nxt.strip()) or nxt.strip().startswith(("|", "```"))):
                    break
                text += " " + nxt.strip()
                i += 1
            layout.paragraph(sanitize(strip_inline(text)), indent=12 + depth * 14,
                             first_prefix=prefix)
            layout.space(1.5)
            continue

        if stripped.startswith(">"):
            flush_para()
            text = stripped.lstrip(">").strip()
            layout.paragraph(sanitize(strip_inline(text)), indent=16)
            layout.space(3)
            i += 1
            continue

        para.append(stripped)
        i += 1

    flush_para()


def render_title(layout: Layout, title: str, subtitle: str, files: list[Path]) -> None:
    layout.y = PAGE_H - 250
    layout.paragraph(sanitize(title), FONT_BOLD, 30)
    layout.space(10)
    layout.paragraph(sanitize(subtitle), FONT_REG, 16)
    layout.space(24)
    layout.paragraph(f"Generated {_dt.date.today().isoformat()} from the Markdown sources:",
                     FONT_REG, BODY_SIZE)
    layout.space(4)
    for f in files:
        layout.paragraph(sanitize(f.name), FONT_MONO, MONO_SIZE + 1, indent=12)
    layout.space(16)
    layout.paragraph(
        "Hindi (Devanagari) lines cannot be drawn with the built-in PDF fonts and are "
        "shown as '" + HINDI_PLACEHOLDER + "'. The Markdown files are the source of truth.",
        FONT_REG, 9)
    layout.new_page()


def _pdf_str(text: str) -> bytes:
    data = text.encode("cp1252", errors="replace")
    data = data.replace(b"\\", b"\\\\").replace(b"(", b"\\(").replace(b")", b"\\)")
    return b"(" + data + b")"


def build_content(page: Page, number: int, total: int, footer: str) -> bytes:
    out: list[bytes] = []
    for font, size, x, y, text in page.ops:
        out.append(b"BT /%s %.2f Tf %.2f %.2f Td %s Tj ET" % (
            font.encode(), size, x, y, _pdf_str(text)))
    for x1, x2, y in page.rules:
        out.append(b"0.5 w %.2f %.2f m %.2f %.2f l S" % (x1, y, x2, y))
    if number > 1:
        label = f"{footer}   Page {number} of {total}"
        w = text_width(label, FONT_REG, FOOTER_SIZE)
        out.append(b"BT /%s %.2f Tf %.2f %.2f Td %s Tj ET" % (
            FONT_REG.encode(), FOOTER_SIZE, (PAGE_W - w) / 2, 30.0, _pdf_str(label)))
    return b"\n".join(out) + b"\n"


def write_pdf(pages: list[Page], path: Path, footer: str, title: str,
              compress: bool = True) -> None:
    objects: list[bytes] = []  # index 0 -> object 1

    def add(obj: bytes) -> int:
        objects.append(obj)
        return len(objects)

    catalog_id = add(b"")  # placeholder
    pages_id = add(b"")  # placeholder
    font_ids = {}
    for key, base in FONT_NAMES.items():
        font_ids[key] = add(
            b"<< /Type /Font /Subtype /Type1 /BaseFont /%s /Encoding /WinAnsiEncoding >>"
            % base.encode())
    resources = b"<< /Font << " + b" ".join(
        b"/%s %d 0 R" % (k.encode(), v) for k, v in font_ids.items()) + b" >> >>"
    info_id = add(b"<< /Title %s /Producer (tools/docs/md2pdf.py) >>" % _pdf_str(title))

    page_ids = []
    total = len(pages)
    for n, page in enumerate(pages, start=1):
        content = build_content(page, n, total, footer)
        if compress:
            body = zlib.compress(content, 9)
            stream = (b"<< /Length %d /Filter /FlateDecode >>\nstream\n" % len(body)
                      + body + b"\nendstream")
        else:
            stream = b"<< /Length %d >>\nstream\n" % len(content) + content + b"\nendstream"
        content_id = add(stream)
        page_ids.append(add(
            b"<< /Type /Page /Parent %d 0 R /MediaBox [0 0 %.2f %.2f] "
            b"/Resources %s /Contents %d 0 R >>"
            % (pages_id, PAGE_W, PAGE_H, resources, content_id)))

    objects[catalog_id - 1] = b"<< /Type /Catalog /Pages %d 0 R >>" % pages_id
    kids = b" ".join(b"%d 0 R" % pid for pid in page_ids)
    objects[pages_id - 1] = b"<< /Type /Pages /Kids [%s] /Count %d >>" % (kids, total)

    buf = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = []
    for num, obj in enumerate(objects, start=1):
        offsets.append(len(buf))
        buf += b"%d 0 obj\n" % num + obj + b"\nendobj\n"
    xref_at = len(buf)
    buf += b"xref\n0 %d\n" % (len(objects) + 1)
    buf += b"0000000000 65535 f \n"
    for off in offsets:
        buf += b"%010d 00000 n \n" % off
    buf += (b"trailer\n<< /Size %d /Root %d 0 R /Info %d 0 R >>\nstartxref\n%d\n%%%%EOF\n"
            % (len(objects) + 1, catalog_id, info_id, xref_at))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(bytes(buf))


def convert(files: list[Path], out: Path, title: str = "GANGS OF BHARAT",
            subtitle: str = "Game Design Document", compress: bool = True) -> int:
    """Convert files to a PDF at out. Returns the page count."""
    layout = Layout()
    render_title(layout, title, subtitle, files)
    for f in files:
        render_markdown(layout, f.read_text(encoding="utf-8"))
    pages = layout.pages
    if len(pages) > 1 and not pages[-1].ops and not pages[-1].rules:
        pages = pages[:-1]
    write_pdf(pages, out, footer=f"{title} - {subtitle}", title=f"{title} - {subtitle}",
              compress=compress)
    return len(pages)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Convert Markdown files into one PDF.")
    parser.add_argument("--out", required=True, type=Path, help="output PDF path")
    parser.add_argument("--title", default="GANGS OF BHARAT")
    parser.add_argument("--subtitle", default="Game Design Document")
    parser.add_argument("--no-compress", action="store_true",
                        help="write uncompressed content streams")
    parser.add_argument("files", nargs="+", type=Path, help="Markdown files in order")
    args = parser.parse_args(argv)
    missing = [f for f in args.files if not f.is_file()]
    if missing:
        parser.error("missing input: " + ", ".join(str(m) for m in missing))
    count = convert(args.files, args.out, args.title, args.subtitle,
                    compress=not args.no_compress)
    print(f"wrote {args.out} ({count} pages)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
