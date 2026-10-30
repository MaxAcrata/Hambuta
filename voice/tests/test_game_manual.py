"""Manual in-game tests — NEVER run in CI. Marked `game`.

Run explicitly (not implemented here, documentation only):
    pytest -m game --collect-only

Real validation is the checklist in voice/README_VOICE.md on Windows + Valheim.
"""

import pytest

pytestmark = pytest.mark.game


@pytest.mark.skip(reason="manual in-game: requires Valheim + mic; see README_VOICE.md checklist")
def test_voice_follow_me_in_game():
    ...


@pytest.mark.skip(reason="manual in-game: requires Valheim + mic")
def test_voice_come_here_in_game():
    ...


@pytest.mark.skip(reason="manual in-game: requires Valheim + mic")
def test_voice_stop_in_game():
    ...


@pytest.mark.skip(reason="manual in-game: requires Valheim + mic")
def test_voice_status_in_game():
    ...


@pytest.mark.skip(reason="manual in-game: unknown phrase must not move the companion")
def test_voice_unknown_phrase_no_action():
    ...
