#!/usr/bin/env python
"""Claude Code statusline.

Reads the status JSON from stdin and prints ONE line:

    Opus 5 (1M context) | High | 31% ctx | 100.3k/1M | vortexarena

Blocks are ` | ` separated. Model and folder are blue, effort is its own
block in a darker blue, the percentage is green, and only the raw token
block is colourised by context fill.
Stdlib only, no writes to stderr, and never raises: any block whose data is
missing or corrupt is dropped and the rest still prints.
"""

import json
import os
import sys

# ---------------------------------------------------------------- colours ---
GRAY = "\x1b[38;5;245m"      # separators + low-fill tokens
BLUE = "\x1b[38;5;75m"       # model block + workspace folder
NAVY = "\x1b[38;2;0;153;255m"  # effort block — darker than BLUE; truecolor
                               # because 256-colour has no step between 33/39
GREEN = "\x1b[38;5;112m"     # "<n>% ctx" block (fixed, never ramps)
YELLOW = "\x1b[38;5;220m"
ORANGE = "\x1b[38;5;208m"
RED_BOLD = "\x1b[1m\x1b[38;5;196m"
RESET = "\x1b[0m"

DEFAULT_LIMIT = 200000
EFFORT_SHORT = {"low": "Low", "medium": "Med", "high": "High",
                "xhigh": "XHigh", "max": "Max"}

# The four fields that together make up occupied context. Dropping the cache
# ones would under-report by an order of magnitude on a warm session.
USAGE_FIELDS = ("input_tokens", "cache_read_input_tokens",
                "cache_creation_input_tokens", "output_tokens")


def fmt_tokens(n):
    """1000000 -> '1M', 12400 -> '12.4k', 512 -> '512'."""
    n = max(0, int(round(n)))
    if n >= 1000000:
        return _trim(n / 1000000.0) + "M"
    if n >= 1000:
        return _trim(n / 1000.0) + "k"
    return str(n)


def _trim(v):
    s = "%.1f" % v
    return s[:-2] if s.endswith(".0") else s


def _sum_usage(usage):
    if not isinstance(usage, dict):
        return 0
    total = 0
    for key in USAGE_FIELDS:
        val = usage.get(key)
        if isinstance(val, (int, float)):
            total += int(val)
    return total


def tail_lines(path, max_bytes=262144, max_lines=400):
    """Last chunk of a file as lines. Never reads the whole transcript."""
    try:
        with open(path, "rb") as f:
            f.seek(0, os.SEEK_END)
            size = f.tell()
            start = max(0, size - max_bytes)
            f.seek(start)
            blob = f.read()
    except Exception:
        return []
    lines = blob.decode("utf-8", errors="replace").splitlines()
    if start > 0 and lines:
        lines = lines[1:]  # first line may be cut mid-record
    return lines[-max_lines:]


def iter_tail_records(path):
    """Yield parsed JSONL records from the tail, newest first."""
    if not path:
        return
    for line in reversed(tail_lines(path)):
        line = line.strip()
        if not line:
            continue
        try:
            obj = json.loads(line)
        except Exception:
            continue
        if isinstance(obj, dict):
            yield obj


# ------------------------------------------------------------------ model ---
def resolve_limit(data, display_name):
    """Context window size. Never hardcodes 200k as the only answer."""
    cw = data.get("context_window")
    if isinstance(cw, dict):
        size = cw.get("context_window_size")
        if isinstance(size, (int, float)) and size > 0:
            return int(size)

    # "Opus 5 (1M context)" / "... (200k context)"
    name = display_name or ""
    low = name.lower()
    idx = low.find("context")
    if idx > 0:
        head = low[:idx].replace("(", " ").replace(")", " ")
        token = head.split()[-1] if head.split() else ""
        for suffix, mult in (("m", 1000000), ("k", 1000)):
            if token.endswith(suffix):
                try:
                    return int(round(float(token[:-1]) * mult))
                except ValueError:
                    pass

    model_id = ""
    model = data.get("model")
    if isinstance(model, dict):
        model_id = str(model.get("id") or "")
    if "[1m]" in model_id.lower():
        return 1000000

    if data.get("exceeds_200k_tokens") is True:
        return 1000000

    return DEFAULT_LIMIT


def resolve_used(data, transcript_path):
    """Occupied context. Returns None when it cannot be determined."""
    # Preferred: the harness already computed it for us — no disk I/O.
    cw = data.get("context_window")
    if isinstance(cw, dict):
        total = _sum_usage(cw.get("current_usage"))
        if total > 0:
            return total

    # Fallback: last assistant usage in the transcript tail.
    for obj in iter_tail_records(transcript_path):
        message = obj.get("message")
        if isinstance(message, dict):
            total = _sum_usage(message.get("usage"))
            if total > 0:
                return total
    return None


# ----------------------------------------------------------------- effort ---
def _normalize_effort(value):
    """Accept both {'level': 'high'} and a bare 'high'."""
    if isinstance(value, dict):
        value = value.get("level")
    if not isinstance(value, str):
        return None
    return EFFORT_SHORT.get(value.strip().lower())


def resolve_effort(data, transcript_path):
    """First hit wins: stdin -> user settings -> transcript. None = hide."""
    hit = _normalize_effort(data.get("effort"))
    if hit:
        return hit

    try:
        path = os.path.join(os.path.expanduser("~"), ".claude", "settings.json")
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            settings = json.load(f)
        if isinstance(settings, dict):
            hit = _normalize_effort(settings.get("effortLevel"))
            if hit:
                return hit
    except Exception:
        pass

    for obj in iter_tail_records(transcript_path):
        hit = _normalize_effort(obj.get("effort"))
        if hit:
            return hit
    return None


# ------------------------------------------------------------------ paint ---
def token_colour(ratio):
    if ratio > 0.90:
        return RED_BOLD
    if ratio >= 0.75:
        return ORANGE
    if ratio >= 0.50:
        return YELLOW
    return GRAY


def basename(path):
    if not path:
        return ""
    trimmed = str(path).rstrip("\\/")
    for sep in ("\\", "/"):
        trimmed = trimmed.replace(sep, "\x00")
    parts = [p for p in trimmed.split("\x00") if p]
    return parts[-1] if parts else ""


def build_line(data):
    blocks = []

    # 1) model, 2) effort — separate blocks
    model = data.get("model")
    display_name = ""
    if isinstance(model, dict):
        display_name = str(model.get("display_name") or "")
    limit = resolve_limit(data, display_name)

    if display_name:
        # Only synthesize a context label when the name lacks one AND the
        # window is non-default, so 200k sessions match the built-in exactly.
        if "context" not in display_name.lower() and limit != DEFAULT_LIMIT:
            display_name = "%s (%s context)" % (display_name, fmt_tokens(limit))
        blocks.append(BLUE + display_name + RESET)

    effort = resolve_effort(data, data.get("transcript_path"))
    if effort:
        blocks.append(NAVY + effort + RESET)

    used = resolve_used(data, data.get("transcript_path"))
    if used is not None and limit > 0:
        ratio = float(used) / float(limit)
        # 3) percentage — always green, independent of how full the window is
        blocks.append("%s%d%% ctx%s" % (GREEN, int(round(ratio * 100)), RESET))
        # 4) raw tokens — the only block that ramps with fill
        blocks.append("%s%s/%s%s" % (token_colour(ratio), fmt_tokens(used),
                                     fmt_tokens(limit), RESET))

    # 5) workspace folder
    workspace = data.get("workspace")
    folder = ""
    if isinstance(workspace, dict):
        folder = basename(workspace.get("project_dir")
                          or workspace.get("current_dir"))
    if not folder:
        folder = basename(data.get("cwd"))
    if folder:
        blocks.append(BLUE + folder + RESET)

    return (GRAY + " | " + RESET).join(blocks)


def main():
    try:
        raw = sys.stdin.read()
    except Exception:
        raw = ""
    try:
        data = json.loads(raw) if raw.strip() else {}
    except Exception:
        data = {}
    if not isinstance(data, dict):
        data = {}
    try:
        line = build_line(data)
    except Exception:
        line = ""
    sys.stdout.write(line)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        pass  # a traceback on stdout/stderr would corrupt the status line
