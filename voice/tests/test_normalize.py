from voice_control.normalize import normalize_for_match, normalize_text, tokenize


def test_lowercase():
    assert normalize_text("СТОЙ") == "стой"


def test_yo_to_e():
    assert normalize_text("иди за мной ё") == "иди за мной е"
    assert normalize_text("ещё") == "еще"


def test_punctuation_stripped():
    assert normalize_text("стой!") == "стой"
    assert normalize_text("иди, сюда...") == "иди сюда"
    assert normalize_text("статус?") == "статус"


def test_whitespace_collapsed():
    assert normalize_text("  иди   за   мной  ") == "иди за мной"
    assert normalize_text("стой\nтам") == "стой там"


def test_empty():
    assert normalize_text("") == ""
    assert normalize_text(None) == ""
    assert normalize_text("   ") == ""


def test_tokenize_drops_fillers():
    assert tokenize("иди за мной пожалуйста") == ["иди", "за", "мной"]
    assert tokenize("ну стой") == ["стой"]


def test_normalize_for_match():
    assert normalize_for_match("Иди за мной, пожалуйста!") == "иди за мной"
    assert normalize_for_match("  СТОЙ!!! ") == "стой"
