"""Limited normalization for Russian voice commands (no LLM)."""

from __future__ import annotations

import re

# Filler words dropped after normalization. Explicit whitelist only.
FILLER_TOKENS = frozenset(
    {
        "пожалуйста",
        "ну",
        "э",
        "а",
        "командир",
    }
)

_PUNCT_RE = re.compile(r"[^\w\s]+", re.UNICODE)
_WS_RE = re.compile(r"\s+", re.UNICODE)


def normalize_text(raw: str) -> str:
    """lowercase, ё->е, strip punctuation, collapse whitespace, trim."""
    if raw is None:
        return ""
    text = str(raw).strip().lower()
    text = text.replace("ё", "е").replace("Ё", "е")
    text = _PUNCT_RE.sub(" ", text)
    text = _WS_RE.sub(" ", text).strip()
    return text


def tokenize(normalized: str) -> list[str]:
    """Split normalized text and drop filler tokens (whitelist)."""
    if not normalized:
        return []
    return [t for t in normalized.split(" ") if t and t not in FILLER_TOKENS]


def normalize_for_match(raw: str) -> str:
    """Normalized phrase with fillers removed, ready for alias comparison."""
    return " ".join(tokenize(normalize_text(raw)))
