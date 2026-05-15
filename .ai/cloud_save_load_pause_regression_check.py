from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
KERNELS = [
    ROOT / "Pal98Timer" / "仙剑98柔情.cs",
    ROOT / "Pal98Timer" / "仙剑98柔情DX9.cs",
    ROOT / "Pal98Timer" / "仙剑98柔情不欢乐模式.cs",
]


def _has_auto_resume_after_cloud_or_relay(path: Path) -> bool:
    text = path.read_text(encoding="utf-8-sig")
    return (
        "wasPausedBefore" in text
        or "if (!wasPausedBefore) SetUIPause(false);" in text
    )


def main() -> int:
    failed = [path for path in KERNELS if _has_auto_resume_after_cloud_or_relay(path)]
    if failed:
        print("FAIL: cloud/relay operation can still auto-resume UI pause:")
        for path in failed:
            print(f"- {path.relative_to(ROOT)}")
        return 1

    print("PASS: PAL98 cloud/relay save-load paths leave UI pause enabled.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
