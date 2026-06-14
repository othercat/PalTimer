from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def _program_parses_automation_flags() -> bool:
    text = (ROOT / "Pal98Timer" / "Program.cs").read_text(encoding="utf-8-sig")
    return (
        "AutomationArgs.Current = AutomationArgs.Parse(Environment.GetCommandLineArgs());"
        in text
        and '"--automation-snapshot-export"' in text
        and '"--automation-snapshot-run-id"' in text
        and "public bool Enabled" in text
    )


def _gform_writes_snapshot_only_when_enabled() -> bool:
    text = (ROOT / "Pal98Timer" / "GForm.cs").read_text(encoding="utf-8-sig")
    return (
        "public void WriteAutomationSnapshot(string trigger)" in text
        and "if (!AutomationArgs.Current.Enabled || core == null)" in text
        and "core.BuildAutomationSnapshotJson(trigger, AutomationArgs.Current.SnapshotRunId)"
        in text
        and "new UTF8Encoding(false)" in text
        and 'WriteAutomationSnapshot("checkpoint");' in text
        and 'WriteAutomationSnapshot("core_loaded");' in text
        and "Automation export must not affect normal timer behavior." in text
    )


def _timer_core_builds_autotest_snapshot_envelope() -> bool:
    text = (ROOT / "Pal98Timer" / "TimerCore.cs").read_text(encoding="utf-8-sig")
    return (
        "public string BuildAutomationSnapshotJson(string trigger, string runId)"
        in text
        and 'snapshot["kind"] = "pal98.paltimer.snapshot";' in text
        and 'snapshot["source"] = "paltimer_automation_export";' in text
        and 'snapshot["export_trigger"] = trigger;' in text
        and 'snapshot["autotest_run_id"] = runId;' in text
        and 'snapshot["paltimer_internal"] = new HObj(GetTimerJson());' in text
        and 'form?.WriteAutomationSnapshot("run_end");' in text
        and "public bool IsRunning" in text
    )


def main() -> int:
    checks = {
        "Program.cs automation args": _program_parses_automation_flags(),
        "GForm.cs gated writer": _gform_writes_snapshot_only_when_enabled(),
        "TimerCore.cs AutoTest envelope": _timer_core_builds_autotest_snapshot_envelope(),
    }
    failed = [name for name, ok in checks.items() if not ok]
    if failed:
        print("FAIL: automation snapshot export regression check failed:")
        for name in failed:
            print(f"- {name}")
        return 1

    print("PASS: PalTimer automation snapshot export is gated and emits the AutoTest envelope.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
