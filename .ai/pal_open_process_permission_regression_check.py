from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
KERNELS = [
    ROOT / "Pal98Timer" / "仙剑98柔情.cs",
    ROOT / "Pal98Timer" / "仙剑98柔情DX9.cs",
    ROOT / "Pal98Timer" / "仙剑98柔情不欢乐模式.cs",
]


def _kernel_has_permission_prompt(path: Path) -> bool:
    text = path.read_text(encoding="utf-8-sig")
    open_call = "if (!TryOpenPalProcess(res[0]))"
    pid_assign = "PID = PalProcess.Id;"

    return (
        "private bool HasAlertPalOpenProcessError = false;" in text
        and "private bool TryOpenPalProcess(Process process)" in text
        and "private bool CanOpenPalProcess(Process process)" in text
        and "private string BuildOpenPalProcessError(int errorCode)" in text
        and "Kernel32.ERROR_ACCESS_DENIED" in text
        and "PAL.exe 可能以管理员身份运行" in text
        and "PalHandle = new IntPtr(handle);" in text
        and "Kernel32.CloseHandle(handle);" in text
        and "PalHandle = new IntPtr(Kernel32.OpenProcess" not in text
        and "catch { return true; }" in text
        and "if (!CanOpenPalProcess(res[0]))" in text
        and text.find(open_call) != -1
        and text.find(pid_assign, text.find(open_call)) != -1
    )


def _kernel32_captures_open_process_error() -> bool:
    text = (ROOT / "Pal98Timer" / "Kernel32.cs").read_text(encoding="utf-8-sig")
    return (
        "public const int ERROR_ACCESS_DENIED = 5;" in text
        and '[DllImport("kernel32.dll", SetLastError = true)]' in text
        and "public static extern int OpenProcess" in text
        and "public static int GetLastWin32Error()" in text
        and "Marshal.GetLastWin32Error()" in text
    )


def _gform_warns_when_elevated() -> bool:
    text = (ROOT / "Pal98Timer" / "GForm.cs").read_text(encoding="utf-8-sig")
    return (
        "using System.Security.Principal;" in text
        and "private bool HasAlertElevatedProcess = false;" in text
        and "ShowElevationGuidanceIfNeeded();" in text
        and "private void ShowElevationGuidanceIfNeeded()" in text
        and "private bool IsCurrentProcessElevated()" in text
        and "WindowsBuiltInRole.Administrator" in text
        and "普通速通和 pal98autotest 自动化测试建议关闭管理员权限" in text
        and "只有当 PAL.exe 因其他补丁必须管理员运行时" in text
    )


def main() -> int:
    failed = [path for path in KERNELS if not _kernel_has_permission_prompt(path)]
    if not _kernel32_captures_open_process_error():
        print("FAIL: Kernel32.OpenProcess does not capture last-error state.")
        return 1

    if not _gform_warns_when_elevated():
        print("FAIL: GForm does not warn when PalTimer itself is running elevated.")
        return 1

    if failed:
        print("FAIL: missing OpenProcess permission prompt guard:")
        for path in failed:
            print(f"- {path.relative_to(ROOT)}")
        return 1

    print("PASS: PAL98 kernels and PalTimer startup explain elevation mismatch constraints.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
