from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TIMER_CORE = ROOT / "Pal98Timer" / "TimerCore.cs"
PLUGIN_MGR = ROOT / "Pal98Timer" / "PluginMgrForm.cs"


def _extract(text: str, start_marker: str, end_marker: str) -> str:
    start = text.find(start_marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    if end < 0:
        return text[start:]
    return text[start:end]


def _load_gate_uses_hidden_bypass(text: str) -> bool:
    body = _extract(text, "private void _loadOnePlugin", "\n        /// <summary>\n        /// 卸载")
    return (
        "if (!ti.Enable || (!ti.IsOK && !TimerPluginPackageInfo.IsPluginAuthorizationDisabled()) || ti.Version!=TimerPlugin.Version.ToString()) return;" in body
        and "System.Reflection.Assembly.Load(ti.Data)" in body
    )


def _hidden_config_is_explicit_opt_in(text: str) -> bool:
    path_body = _extract(text, "public static string GetPluginAuthConfigPath()", "\n        }")
    check_body = _extract(text, "public static bool IsPluginAuthorizationDisabled()", "\n        public string FileName;")
    return (
        '"\\\\plugin_auth"' in path_body
        and "if (!File.Exists(path)) return false;" in check_body
        and 'value == "allow_unsigned_plugins=1"' in check_body
        and "return true;" in check_body
        and check_body.rstrip().endswith("return false;\n        }")
    )


def _plugin_manager_reports_debug_bypass(text: str) -> bool:
    return (
        'TimerPluginPackageInfo.IsPluginAuthorizationDisabled() ? "调试放行" : "否"' in text
        and "TimerPluginPackageInfo.IsPluginAuthorizationDisabled() ? Color.Green : Color.Red" in text
    )


def main() -> int:
    timer_core = TIMER_CORE.read_text(encoding="utf-8-sig")
    plugin_mgr = PLUGIN_MGR.read_text(encoding="utf-8-sig")
    checks = {
        "loader keeps signature as default gate with hidden bypass": _load_gate_uses_hidden_bypass(timer_core),
        "hidden config requires explicit opt-in value": _hidden_config_is_explicit_opt_in(timer_core),
        "plugin manager shows debug bypass status": _plugin_manager_reports_debug_bypass(plugin_mgr),
    }
    failed = [name for name, ok in checks.items() if not ok]
    if failed:
        print("FAIL: plugin authorization bypass regression check failed:")
        for name in failed:
            print(f"- {name}")
        return 1

    print("PASS: plugin authorization bypass remains hidden and explicit.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
