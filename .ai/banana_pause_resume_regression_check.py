from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
KERNELS = [
    ROOT / "Pal98Timer" / "仙剑98柔情.cs",
    ROOT / "Pal98Timer" / "仙剑98柔情DX9.cs",
    ROOT / "Pal98Timer" / "仙剑98柔情不欢乐模式.cs",
]


def _has_pre_start_uncheat_end(path: Path) -> bool:
    text = path.read_text(encoding="utf-8-sig")
    flush_index = text.find("FlushGameObject();")
    start_index = text.find("if (HasStartGame())", flush_index)
    if flush_index == -1 or start_index == -1:
        return False
    pre_start = text[flush_index:start_index]
    return (
        "if (IsInUnCheat)" in pre_start
        and "CheckCheatEnd();" in pre_start
        and "MT.Start();" not in pre_start
    )


def main() -> int:
    failed = [path for path in KERNELS if not _has_pre_start_uncheat_end(path)]
    if failed:
        print("FAIL: missing pre-HasStartGame CheckCheatEnd guard:")
        for path in failed:
            print(f"- {path.relative_to(ROOT)}")
        return 1

    print("PASS: all PAL98 kernels clear existing anti-cheat pause before HasStartGame.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
