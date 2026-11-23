"""Shared helpers for the content pipeline scripts (standard library only).

Keys are read from environment variables and never printed, logged or written to disk.
Every request goes through PlannedRequest so --dry-run can show exactly what would be
sent, with secret headers replaced by a placeholder.
"""

from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

CONTENT_DIR = Path(__file__).resolve().parent
DEFAULT_OUT_DIR = CONTENT_DIR / "out"
REDACTED = "<redacted>"
TIMEOUT_SECONDS = 60


class MissingKeyError(RuntimeError):
    """Raised when the API key environment variable is not set."""


class ApiError(RuntimeError):
    """Raised for HTTP or response format errors. Messages never contain the key."""


def read_key(env_var: str) -> str | None:
    value = os.environ.get(env_var, "").strip()
    return value or None


def require_key(env_var: str) -> str:
    key = read_key(env_var)
    if key is None:
        raise MissingKeyError(
            f"{env_var} is not set. Export it in your shell or put it in a local .env file "
            f"(gitignored, see .env.example). Use --dry-run to see the planned requests "
            f"without a key."
        )
    return key


@dataclass
class PlannedRequest:
    """One HTTP request. secret_headers names the headers that carry the key."""

    method: str
    url: str
    headers: dict[str, str]
    json_body: dict[str, Any] | None = None
    raw_body: bytes | None = None
    raw_body_summary: str | None = None
    secret_headers: tuple[str, ...] = field(default_factory=tuple)
    purpose: str = ""

    def describe(self, key_present: bool) -> dict[str, Any]:
        """Printable form with secrets replaced. Safe to log."""
        headers = {}
        for name, value in self.headers.items():
            if name in self.secret_headers:
                headers[name] = REDACTED if key_present else "<key env var not set>"
            else:
                headers[name] = value
        out: dict[str, Any] = {
            "purpose": self.purpose,
            "method": self.method,
            "url": self.url,
            "headers": headers,
        }
        if self.json_body is not None:
            out["json"] = self.json_body
        if self.raw_body_summary is not None:
            out["body"] = self.raw_body_summary
        return out


def print_dry_run(requests: list[PlannedRequest], key_env_var: str, stream=None) -> None:
    stream = stream or sys.stdout
    key_present = read_key(key_env_var) is not None
    if not key_present:
        print(
            f"[dry-run] {key_env_var} is not set; showing planned requests only.",
            file=stream,
        )
    print(f"[dry-run] {len(requests)} planned request(s), nothing sent:", file=stream)
    for req in requests:
        print(json.dumps(req.describe(key_present), ensure_ascii=False, indent=2), file=stream)


def send(req: PlannedRequest, key: str) -> dict[str, Any]:
    """Sends a request with the key filled into its secret headers and returns parsed JSON."""
    headers = dict(req.headers)
    for name in req.secret_headers:
        headers[name] = key
    if req.json_body is not None:
        data = json.dumps(req.json_body, ensure_ascii=False).encode("utf-8")
    else:
        data = req.raw_body
    http_req = urllib.request.Request(req.url, data=data, headers=headers, method=req.method)
    try:
        with urllib.request.urlopen(http_req, timeout=TIMEOUT_SECONDS) as resp:
            payload = resp.read()
    except urllib.error.HTTPError as exc:
        detail = exc.read()[:500].decode("utf-8", errors="replace")
        raise ApiError(f"{req.purpose}: HTTP {exc.code} from {req.url}: {scrub(detail, key)}") from None
    except urllib.error.URLError as exc:
        raise ApiError(f"{req.purpose}: cannot reach {req.url}: {scrub(str(exc.reason), key)}") from None
    try:
        return json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        raise ApiError(f"{req.purpose}: response from {req.url} is not JSON") from None


def scrub(text: str, key: str | None) -> str:
    """Removes the key from text in case a server echoes it back."""
    if key and key in text:
        return text.replace(key, REDACTED)
    return text


def slugify(text: str, max_len: int = 40) -> str:
    out = []
    for ch in text.lower():
        if ch.isascii() and ch.isalnum():
            out.append(ch)
        elif out and out[-1] != "_":
            out.append("_")
    return "".join(out).strip("_")[:max_len] or "item"
