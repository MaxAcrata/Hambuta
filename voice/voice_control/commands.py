"""Fixed 4-command whitelist and explicit matching rules (no LLM)."""

from __future__ import annotations

import difflib
from dataclasses import dataclass
from enum import Enum

from .normalize import normalize_for_match, normalize_text


def _ratio(a: str, b: str) -> float:
    return difflib.SequenceMatcher(None, a, b).ratio()


class CommandKind(str, Enum):
    FOLLOW_ME = "FOLLOW_ME"
    COME_HERE = "COME_HERE"
    STOP = "STOP"
    STATUS = "STATUS"


class MatchOutcome(str, Enum):
    MATCHED = "matched"
    AMBIGUOUS = "ambiguous"
    UNKNOWN = "unknown"
    EMPTY = "empty"


@dataclass(frozen=True)
class MatchResult:
    outcome: MatchOutcome
    command: CommandKind | None
    normalized: str
    detail: str = ""

    @property
    def ok(self) -> bool:
        return self.outcome == MatchOutcome.MATCHED and self.command is not None


# Explicit alias table. Aliases are already normalized (lowercase, ё->е, no punctuation).
# Keep this list small and literal — do not turn it into free-form interpretation.
COMMAND_ALIASES: dict[CommandKind, frozenset[str]] = {
    CommandKind.FOLLOW_ME: frozenset(
        {
            "иди за мной",
            "следуй за мной",
            "за мной",
            "иди за мной пожалуйста",
        }
    ),
    CommandKind.COME_HERE: frozenset(
        {
            "иди сюда",
            "подойди сюда",
            "подойди ко мне",
            "сюда",
            "иди ко мне",
        }
    ),
    CommandKind.STOP: frozenset(
        {
            "стой",
            "остановись",
            "стой там",
            "прекрати",
            "стой здесь",
        }
    ),
    CommandKind.STATUS: frozenset(
        {
            "статус",
            "как дела",
            "статус компаньона",
        }
    ),
}

# Canonical phrases shown to the user / used in docs and tests.
CANONICAL_PHRASES: dict[CommandKind, str] = {
    CommandKind.FOLLOW_ME: "иди за мной",
    CommandKind.COME_HERE: "иди сюда",
    CommandKind.STOP: "стой",
    CommandKind.STATUS: "статус",
}


def _lookup_exact(norm_match: str) -> list[CommandKind]:
    hits: list[CommandKind] = []
    for kind, aliases in COMMAND_ALIASES.items():
        if norm_match in aliases:
            hits.append(kind)
    return hits


def match_command(raw_text: str, fuzzy_min_ratio: float = 0.90) -> MatchResult:
    """Map recognized text to exactly one of the four commands, or refuse.

    Rules (explicit, in order):
    1. Normalize (case, ё->е, punctuation, whitespace) and drop filler tokens.
    2. Exact alias match. More than one command hit -> AMBIGUOUS.
    3. Optional fuzzy: single best candidate with difflib ratio >= threshold.
       Two or more close candidates -> AMBIGUOUS.
    4. Otherwise UNKNOWN. Never invent a command.
    """
    normalized = normalize_for_match(raw_text)
    if not normalized:
        return MatchResult(MatchOutcome.EMPTY, None, "", "empty after normalization")

    exact = _lookup_exact(normalized)
    if len(exact) == 1:
        return MatchResult(MatchOutcome.MATCHED, exact[0], normalized, "exact alias")
    if len(exact) > 1:
        names = ", ".join(c.value for c in exact)
        return MatchResult(
            MatchOutcome.AMBIGUOUS, None, normalized, f"matches multiple commands: {names}"
        )

    # Fuzzy fallback: best ratio per command (max over its aliases).
    scored: list[tuple[float, CommandKind]] = []
    for kind, aliases in COMMAND_ALIASES.items():
        best = max(_ratio(normalized, a) for a in aliases)
        scored.append((best, kind))
    scored.sort(key=lambda t: t[0], reverse=True)

    best_ratio, best_kind = scored[0]
    if best_ratio < fuzzy_min_ratio:
        return MatchResult(
            MatchOutcome.UNKNOWN,
            None,
            normalized,
            f"no alias above ratio {fuzzy_min_ratio:.2f} (best={best_ratio:.2f})",
        )

    # Ambiguous if another command is essentially as close.
    close = [k for r, k in scored if r >= fuzzy_min_ratio and k != best_kind]
    # Also treat near-ties (within 0.02) as ambiguous even if only one crosses the bar.
    near_tie = [k for r, k in scored[1:] if best_ratio - r <= 0.02 and r >= fuzzy_min_ratio - 0.05]
    rivals = set(close) | set(near_tie)
    if rivals:
        names = ", ".join(sorted({best_kind.value} | {k.value for k in rivals}))
        return MatchResult(
            MatchOutcome.AMBIGUOUS,
            None,
            normalized,
            f"close to several commands: {names} (best={best_ratio:.2f})",
        )

    return MatchResult(
        MatchOutcome.MATCHED, best_kind, normalized, f"fuzzy alias ratio={best_ratio:.2f}"
    )


def is_known_command_phrase(text: str) -> bool:
    """True if normalized text is exactly one of the canonical phrases."""
    m = match_command(text)
    return m.ok and m.command is not None and normalize_text(text) == normalize_for_match(
        CANONICAL_PHRASES[m.command]
    )
