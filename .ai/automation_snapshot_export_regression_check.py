from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def _program_parses_automation_flags() -> bool:
    text = (ROOT / "Pal98Timer" / "Program.cs").read_text(encoding="utf-8-sig")
    return (
        "AutomationArgs.Current = AutomationArgs.Parse(Environment.GetCommandLineArgs());"
        in text
        and '"--automation-snapshot-export"' in text
        and '"--automation-snapshot-run-id"' in text
        and '"--automation-non-sequential-splits"' in text
        and '"--automation-accept-pal98-base-title"' in text
        and "public bool Enabled" in text
        and "public bool EnableNonSequentialSplits" in text
        and "public bool EnablePal98BaseTitleFallback" in text
        and "get { return Enabled && AcceptPal98BaseTitle; }" in text
    )


def _extract_write_automation_snapshot_body(text: str) -> str:
    marker = "public void WriteAutomationSnapshot(string trigger)"
    start = text.find(marker)
    if start < 0:
        return ""

    end_marker = "\n        public void _ResetAll()"
    end = text.find(end_marker, start)
    if end < 0:
        return text[start:]
    return text[start:end]


def _gform_writes_snapshot_only_when_enabled() -> bool:
    text = (ROOT / "Pal98Timer" / "GForm.cs").read_text(encoding="utf-8-sig")
    body = _extract_write_automation_snapshot_body(text)
    return (
        "public void WriteAutomationSnapshot(string trigger)" in text
        and "if (!AutomationArgs.Current.Enabled || core == null)" in body
        and "core.BuildAutomationSnapshotJson(trigger, AutomationArgs.Current.SnapshotRunId)"
        in body
        and "new UTF8Encoding(false)" in body
        and "StreamWriter(fileStream, Encoding.UTF8)" not in body
        and 'WriteAutomationSnapshot("checkpoint");' in text
        and 'WriteAutomationSnapshot("core_loaded");' in text
        and "ApplyAutomationOptions();" in text
        and "AutomationArgs.Current.EnableNonSequentialSplits" in text
        and "Automation export must not affect normal timer behavior." in body
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
        and 'snapshot["non_sequential_check_enabled"] = form != null && form.IsNonSequentialCheck;'
        in text
        and 'snapshot["automation_non_sequential_splits"] = AutomationArgs.Current.EnableNonSequentialSplits;'
        in text
        and 'snapshot["automation_pal98_base_title_fallback"] = AutomationArgs.Current.EnablePal98BaseTitleFallback;'
        in text
        and 'snapshot["automation_tick_snapshot_interval_ms"] = AutomationTickSnapshotIntervalMilliseconds;'
        in text
        and "FillAutomationSnapshotDiagnostics(snapshot);" in text
        and "protected virtual void FillAutomationSnapshotDiagnostics(HObj snapshot)" in text
        and 'snapshot["paltimer_internal"] = new HObj(GetTimerJson());' in text
        and 'form?.WriteAutomationSnapshot("run_end");' in text
        and "public bool IsRunning" in text
    )


def _timer_core_writes_tick_snapshot_only_for_automation() -> bool:
    text = (ROOT / "Pal98Timer" / "TimerCore.cs").read_text(encoding="utf-8-sig")
    return (
        "private const int AutomationTickSnapshotIntervalMilliseconds = 500;" in text
        and "private DateTime LastAutomationTickSnapshotTime = DateTime.MinValue;" in text
        and "private void WriteAutomationTickSnapshotIfDue()" in text
        and "if (!AutomationArgs.Current.Enabled || form == null)" in text
        and "LastAutomationTickSnapshotTime = now;" in text
        and 'form.WriteAutomationSnapshot("automation_tick");' in text
        and "OnTick();" in text
        and "WriteAutomationTickSnapshotIfDue();" in text
    )


def _pal98_title_fallback_is_automation_only() -> bool:
    filenames = ["仙剑98柔情DX9.cs", "仙剑98柔情不欢乐模式.cs"]
    for filename in filenames:
        text = (ROOT / "Pal98Timer" / filename).read_text(encoding="utf-8-sig")
        required = (
            "ShouldAcceptAutomationBaseTitle(isBaseGameTitle)" in text
            and "AutomationArgs.Current.EnablePal98BaseTitleFallback" in text
            and '"connected_by_automation_base_title"' in text
            and 'DX9Version = "automation-base-title";' in text
            and "protected override void FillAutomationSnapshotDiagnostics(HObj snapshot)"
            in text
            and 'attach["automation_accept_pal98_base_title"] = AutomationArgs.Current.EnablePal98BaseTitleFallback;'
            in text
            and 'snapshot["pal_process_attach"] = attach;' in text
        )
        if not required:
            return False
    return True


def main() -> int:
    checks = {
        "Program.cs automation args": _program_parses_automation_flags(),
        "GForm.cs gated writer": _gform_writes_snapshot_only_when_enabled(),
        "TimerCore.cs AutoTest envelope": _timer_core_builds_autotest_snapshot_envelope(),
        "TimerCore.cs gated tick snapshot": _timer_core_writes_tick_snapshot_only_for_automation(),
        "PAL98 title fallback": _pal98_title_fallback_is_automation_only(),
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
