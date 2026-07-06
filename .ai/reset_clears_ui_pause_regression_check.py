from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TIMER_CORE = ROOT / "Pal98Timer" / "TimerCore.cs"


def _extract_method_body(text: str, marker: str, end_marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    if end < 0:
        return text[start:]
    return text[start:end]


def _timer_core_reset_clears_ui_pause(text: str) -> bool:
    body = _extract_method_body(
        text,
        "public virtual void Reset()",
        "\n        public abstract void InitUI();",
    )
    return (
        "IsUIPause = false;" in body
        and "MT.Reset();" in body
        and body.find("IsUIPause = false;") < body.find("MT.Reset();")
    )


def _ptimer_reset_stops_before_restart(text: str) -> bool:
    start = text.find("public class PTimer")
    reset = text.find("public void Reset()", start)
    if start < 0 or reset < 0:
        return False
    body = text[reset : text.find("\n        }", reset) + len("\n        }")]
    return (
        "_Status = 0;" in body
        and "_CurrentTS = new TimeSpan(0);" in body
        and "sw.Reset();" in body
        and body.find("_Status = 0;") < body.find("_CurrentTS = new TimeSpan(0);")
    )


def main() -> int:
    text = TIMER_CORE.read_text(encoding="utf-8-sig")
    checks = {
        "TimerCore.Reset clears UI pause": _timer_core_reset_clears_ui_pause(text),
        "PTimer.Reset leaves stopwatch restartable": _ptimer_reset_stops_before_restart(text),
    }
    failed = [name for name, ok in checks.items() if not ok]
    if failed:
        print("FAIL: reset pause regression check failed:")
        for name in failed:
            print(f"- {name}")
        return 1

    print("PASS: reset clears UI pause and leaves the timer restartable.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
