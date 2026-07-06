from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PLUGIN = ROOT / "PAL98.FujiaCaishen" / "Main.cs"


def _extract(text: str, start_marker: str, end_marker: str) -> str:
    start = text.find(start_marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    if end < 0:
        return text[start:]
    return text[start:end]


def _output_prefixes_fujia_result(text: str) -> bool:
    body = _extract(text, "public override string GetResult()", "\n        }")
    return (
        '"神器:"' in body
        and '" 钱" + money' in body
        and '" 道具" + ic' in body
        and body.find('"神器:"') < body.find('" 钱" + money')
    )


def _tracks_expected_items(text: str) -> bool:
    required = [
        "private const short ZijinDan = 0x111;",
        "private const short EarthBall = 0x10B;",
        "private const short LiushenDan = 0x11E;",
        "private const short ClothBag = 0x10F;",
        "private short[] YuhangArtifactItems = new short[] { ZijinDan, EarthBall, LiushenDan, ClothBag };",
    ]
    return all(item in text for item in required)


def _freezes_at_before_boat_point(text: str) -> bool:
    body = _extract(text, "private bool IsBeforeBoatPoint", "\n        }")
    flush = _extract(text, "public override void Flush", "\n        private void ResetYuhangArtifacts")
    update = _extract(text, "private void UpdateYuhangArtifacts", "\n        private void ResetYuhangArtifacts")
    return (
        "area == 6" in body
        and "1072 - 16 * 2" in body
        and "1080 - 8 * 2" in body
        and "if (!isYuhangArtifactsFrozen)" in flush
        and "isYuhangArtifactsFrozen = true;" in flush
        and "UpdateYuhangArtifacts(Items, TeamMembers);" in flush
        and "items.ContainsKey(id)" in update
        and "mem.Equip_Ball == id" in update
        and flush.find("UpdateYuhangArtifacts(Items, TeamMembers);") < flush.find("isYuhangArtifactsFrozen = true;")
    )


def _resets_for_new_run(text: str) -> bool:
    on_load = _extract(text, "public override void OnLoad()", "\n        }")
    on_event = _extract(text, "public override void OnEvent", "\n        }")
    reset = _extract(text, "private void ResetYuhangArtifacts()", "\n        }")
    return (
        "ResetYuhangArtifacts();" in on_load
        and 'name == "Start"' in on_event
        and 'name == "InitCheckPoints"' in on_event
        and "hasYuhangArtifacts = false;" in reset
        and "isYuhangArtifactsFrozen = false;" in reset
    )


def main() -> int:
    text = PLUGIN.read_text(encoding="utf-8-sig")
    checks = {
        "output prefixes the money/item display": _output_prefixes_fujia_result(text),
        "tracks the four requested item ids": _tracks_expected_items(text),
        "freezes at the before-boat split point": _freezes_at_before_boat_point(text),
        "resets collection state for a new run": _resets_for_new_run(text),
    }
    failed = [name for name, ok in checks.items() if not ok]
    if failed:
        print("FAIL: Fujia Yuhang artifact regression check failed:")
        for name in failed:
            print(f"- {name}")
        return 1

    print("PASS: Fujia plugin tracks and freezes Yuhang artifact collection.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
