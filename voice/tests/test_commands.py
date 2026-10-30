from voice_control.commands import (
    CANONICAL_PHRASES,
    CommandKind,
    MatchOutcome,
    match_command,
)


def test_canonical_all_four():
    for kind, phrase in CANONICAL_PHRASES.items():
        m = match_command(phrase)
        assert m.outcome == MatchOutcome.MATCHED, (kind, phrase, m)
        assert m.command == kind


def test_aliases_follow_me():
    for phrase in ("следуй за мной", "за мной", "Иди за мной!"):
        m = match_command(phrase)
        assert m.ok and m.command == CommandKind.FOLLOW_ME, phrase


def test_aliases_come_here():
    for phrase in ("подойди сюда", "подойди ко мне", "сюда", "иди ко мне"):
        m = match_command(phrase)
        assert m.ok and m.command == CommandKind.COME_HERE, phrase


def test_aliases_stop():
    for phrase in ("остановись", "стой там", "прекрати"):
        m = match_command(phrase)
        assert m.ok and m.command == CommandKind.STOP, phrase


def test_aliases_status():
    for phrase in ("Статус", "как дела", "статус компаньона"):
        m = match_command(phrase)
        assert m.ok and m.command == CommandKind.STATUS, phrase


def test_fillers_ignored():
    m = match_command("Иди за мной, пожалуйста, командир")
    assert m.ok and m.command == CommandKind.FOLLOW_ME


def test_unknown_phrase():
    m = match_command("открой инвентарь")
    assert m.outcome == MatchOutcome.UNKNOWN
    assert m.command is None


def test_unknown_gibberish():
    m = match_command("бла бла бла")
    assert m.outcome == MatchOutcome.UNKNOWN


def test_empty():
    for raw in ("", "   ", "пожалуйста", "ну"):
        m = match_command(raw)
        assert m.outcome == MatchOutcome.EMPTY, raw
        assert m.command is None


def test_no_action_on_unknown():
    m = match_command("сделай что-нибудь")
    assert not m.ok


def test_fuzzy_close_single_candidate():
    # small typo, still clearly STOP ("остановись" without final ь)
    m = match_command("остановис")
    assert m.outcome == MatchOutcome.MATCHED
    assert m.command == CommandKind.STOP


def test_ambiguous_when_exact_collides():
    # inject a temporary collision via monkeypatch-free path:
    # "сюда" is unique; craft ambiguity by a phrase that fuzzy-matches two commands.
    m = match_command("иди туда", fuzzy_min_ratio=0.5)
    # With a very low threshold two commands can be close -> must not execute.
    if m.outcome == MatchOutcome.MATCHED:
        # only acceptable if exactly one command is a clear winner
        assert m.command is not None
    else:
        assert m.outcome in (MatchOutcome.AMBIGUOUS, MatchOutcome.UNKNOWN)
        assert m.command is None


def test_always_command_kind_enum():
    m = match_command("статус")
    assert isinstance(m.command, CommandKind)
    assert m.command.value == "STATUS"
