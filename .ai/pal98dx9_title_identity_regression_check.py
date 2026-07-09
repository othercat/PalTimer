from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PAL98_DX9_CORES = [
    ROOT / "Pal98Timer" / "仙剑98柔情DX9.cs",
    ROOT / "Pal98Timer" / "仙剑98柔情不欢乐模式.cs",
]


def _core_supports_multilingual_dx9_title(path: Path) -> bool:
    text = path.read_text(encoding="utf-8-sig")
    return (
        'windowTitle.Contains("仙剑奇侠传") || windowTitle.Contains("仙劍奇俠傳")' in text
        and 'normalizedTitle.StartsWith("PAL98DX9", StringComparison.OrdinalIgnoreCase)' in text
        and 'normalizedTitle.Contains("(v")' in text
        and "private static bool IsDx9WindowTitle(string windowTitle)" in text
        and "private static bool IsBaseGameWindowTitle(string windowTitle)" in text
        and text.count("IsDx9WindowTitle(windowTitle)") >= 2
        and text.count("IsBaseGameWindowTitle(windowTitle)") >= 1
        and 'windowTitle.Contains("仙剑") && windowTitle.Contains("DX9")' not in text
    )


def main() -> int:
    failed = [str(path.relative_to(ROOT)) for path in PAL98_DX9_CORES if not _core_supports_multilingual_dx9_title(path)]
    if failed:
        print("FAIL: PAL98DX9 title identity regression check failed:")
        for path in failed:
            print(f"- {path}")
        return 1

    print("PASS: PAL98DX9 timer title matching supports Simplified, Traditional, and English PAL98DX9 identities.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
