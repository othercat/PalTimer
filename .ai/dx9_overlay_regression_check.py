from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OVERLAY = ROOT / "Pal98Timer" / "Dx9OverlayForm.cs"
DX9_CORE = ROOT / "Pal98Timer" / "仙剑98柔情DX9.cs"
PROJECT = ROOT / "Pal98Timer" / "Pal98Timer.csproj"
GFORM = ROOT / "Pal98Timer" / "GForm.cs"
GFORM_DESIGNER = ROOT / "Pal98Timer" / "GForm.Designer.cs"
OBS_STYLE_SETTINGS = ROOT / "Pal98Timer" / "ObsWindowStyleSettings.cs"
TIMER_CORE = ROOT / "Pal98Timer" / "TimerCore.cs"
ASSEMBLY_INFO = ROOT / "Pal98Timer" / "Properties" / "AssemblyInfo.cs"
OTHER_PAL98_CORES = [
    ROOT / "Pal98Timer" / "仙剑98柔情.cs",
    ROOT / "Pal98Timer" / "仙剑98柔情不欢乐模式.cs",
    ROOT / "Pal98Timer" / "仙剑98官方Steam.cs",
]


def _extract(text: str, start_marker: str, end_marker: str) -> str:
    start = text.find(start_marker)
    if start < 0:
        return ""
    end = text.find(end_marker, start)
    return text[start:] if end < 0 else text[start:end]


def main() -> int:
    overlay = OVERLAY.read_text(encoding="utf-8-sig")
    dx9 = DX9_CORE.read_text(encoding="utf-8-sig")
    project = PROJECT.read_text(encoding="utf-8-sig")
    gform = GFORM.read_text(encoding="utf-8-sig")
    gform_designer = GFORM_DESIGNER.read_text(encoding="utf-8-sig")
    obs_style_settings = OBS_STYLE_SETTINGS.read_text(encoding="utf-8-sig")
    timer_core = TIMER_CORE.read_text(encoding="utf-8-sig")
    assembly = ASSEMBLY_INFO.read_text(encoding="utf-8-sig")

    enable_method = _extract(
        dx9,
        "private void SetDx9OverlayEnabled(bool enabled)",
        "private void CloseDx9Overlay()",
    )
    snapshot_method = _extract(
        dx9,
        "private Dx9OverlaySnapshot CreateDx9OverlaySnapshot()",
        "protected override void OnTick()",
    )
    custom_hotkey_method = _extract(
        dx9,
        "public override bool TryHandleCustomHotkey(Keys hotkey)",
        "private Dx9OverlaySnapshot CreateDx9OverlaySnapshot()",
    )
    forbidden_overlay_paths = [
        "BitBlt",
        "GetWindowDC",
        "Graphics.FromImage",
        "new Bitmap",
        "ReadProcessMemory",
        "WriteProcessMemory",
        "OpenProcess",
        "Socket",
        "HttpClient",
        "WebClient",
        "OpenFileMapping",
        "PAL98_IPC_v1",
        "System.Threading",
    ]

    checks = {
        "project compiles the dedicated overlay form": '<Compile Include="Dx9OverlayForm.cs">' in project,
        "missing config is default-off": (
            'internal const string ConfigFileName = "dx9_overlay";' in overlay
            and "if (!File.Exists(ConfigFileName))" in overlay
            and "return false;" in overlay
        ),
        "disabled path returns before form creation": (
            "if (!enabled)" in enable_method
            and "return;" in enable_method
            and "new Dx9OverlayForm" in enable_method
            and enable_method.find("return;") < enable_method.find("new Dx9OverlayForm")
        ),
        "enabled path refreshes at exactly 10 Hz": (
            "private const int RefreshIntervalMilliseconds = 100;" in overlay
            and "RefreshTimer.Interval = RefreshIntervalMilliseconds;" in overlay
        ),
        "overlay defaults to the compact bottom-right panel and supports virtual-screen movement": (
            "private const float OverlayWidthLogicalPixels = 340.0F;" in overlay
            and "private const float OverlayHeightLogicalPixels = 148.0F;" in overlay
            and "PositionX = 1.0F" in overlay
            and "PositionY = 1.0F" in overlay
            and "Rectangle movementBounds = SystemInformation.VirtualScreen;" in overlay
            and "LayoutSettings.HasWindowPosition = true;" in overlay
            and "LayoutSettings.WindowLeft = overlayLeft;" in overlay
            and "LayoutSettings.WindowTop = overlayTop;" in overlay
            and "availableWidth * LayoutSettings.PositionX" in overlay
            and "availableHeight * LayoutSettings.PositionY" in overlay
            and "SetBounds(overlayLeft, overlayTop, overlayWidth, overlayHeight);" in overlay
        ),
        "layout config is bounded backward-compatible and only saved on explicit interaction": (
            'internal const string LayoutConfigFileName = "dx9_overlay_layout";' in overlay
            and "if (!File.Exists(LayoutConfigFileName))" in overlay
            and "MinimumScale = 0.50F" in overlay
            and "MaximumScale = 2.00F" in overlay
            and "MinimumFontSize = 6.00F" in overlay
            and "MaximumFontSize = 18.00F" in overlay
            and 'case "font_color":' in overlay
            and 'case "toggle_hotkey":' in overlay
            and 'case "window_left":' in overlay
            and 'case "window_top":' in overlay
            and 'text.AppendLine("font_color="' in overlay
            and 'text.AppendLine("toggle_hotkey="' in overlay
            and 'text.AppendLine("window_left="' in overlay
            and 'text.AppendLine("window_top="' in overlay
            and "Dx9OverlaySettings.SaveLayout(LayoutSettings);" in overlay
            and "SaveLayoutAfterInteraction();" in overlay
            and 'btnDx9OverlayAdjust.Text = "拖动并调整遮罩比例";' in dx9
            and 'btnDx9OverlayFont.Text = "调整遮罩字体和字号...";' in dx9
            and 'btnDx9OverlayFontColor.Text = "调整遮罩字体颜色...";' in dx9
            and 'btnDx9OverlayReset.Text = "恢复遮罩默认设置";' in dx9
        ),
        "overlay controls are consolidated under one top-level settings menu": (
            'btnDx9Overlay.Text = "OBS 独立遮罩窗口设置";' in dx9
            and 'btnDx9OverlayEnabled.Text = "启用 OBS 独立遮罩窗口";' in dx9
            and "btnDx9Overlay.DropDownItems.AddRange(new ToolStripItem[]" in dx9
            and "btnDx9Overlay = form.NewMenuItem();" in dx9
            and "btnDx9OverlayEnabled = new ToolStripMenuItem();" in dx9
            and "btnDx9OverlayHotkey = new ToolStripMenuItem();" in dx9
            and "btnDx9OverlayAdjust = new ToolStripMenuItem();" in dx9
            and "btnDx9OverlayFont = new ToolStripMenuItem();" in dx9
            and "btnDx9OverlayFontColor = new ToolStripMenuItem();" in dx9
            and "btnDx9OverlayReset = new ToolStripMenuItem();" in dx9
            and "btnDx9OverlayAdjust = form.NewMenuItem();" not in dx9
            and "btnDx9OverlayFont = form.NewMenuItem();" not in dx9
            and "btnDx9OverlayHotkey = form.NewMenuItem();" not in dx9
            and "btnDx9OverlayFontColor = form.NewMenuItem();" not in dx9
            and "btnDx9OverlayReset = form.NewMenuItem();" not in dx9
        ),
        "overlay hotkey reuses the existing hook and rejects timer conflicts": (
            "public virtual bool TryHandleCustomHotkey(Keys hotkey)" in timer_core
            and gform.count("_keyboardHook.InstallHook(this.OnKeyPress);") == 1
            and "ActiveCustomHotkey" in gform
            and "core.TryHandleCustomHotkey(pressed)" in gform
            and "new KeyboardLib" not in overlay
            and "new KeyboardLib" not in dx9
            and "keyCode >= Keys.F1 && keyCode <= Keys.F12" in overlay
            and "keyCode == Keys.Enter && (modifiers & Keys.Control) == Keys.Control" in overlay
            and "modifiers == Keys.None" in overlay
            and "hotkey == soundToggleHotkey" in overlay
            and "Dx9OverlayToggleHotkey == Keys.None" in custom_hotkey_method
            and "ToggleDx9OverlayEnabled();" in custom_hotkey_method
            and "System.Threading" not in custom_hotkey_method
            and "ReadProcessMemory" not in custom_hotkey_method
        ),
        "OBS window style has a recoverable global toggle hotkey": (
            "public Keys ToggleHotkey;" in obs_style_settings
            and 'case "toggle_hotkey":' in obs_style_settings
            and 'text.AppendLine("toggle_hotkey="' in obs_style_settings
            and 'text.AppendLine("version=2")' in obs_style_settings
            and "internal Keys ObsWindowStyleToggleHotkey" in gform
            and "ToggleObsWindowStyleEnabled();" in gform
            and "ActiveCustomHotkey = pressed;" in gform
            and "GetCustomToggleHotkey()" in gform
            and "public virtual Keys GetCustomToggleHotkey()" in timer_core
            and "public override Keys GetCustomToggleHotkey()" in dx9
            and "hotkey == form.ObsWindowStyleToggleHotkey" in dx9
            and "btnObsWindowStyleHotkey" in gform_designer
            and 'this.btnObsWindowStyleHotkey.Text = "配置样式开关快捷键...（未设置）";' in gform_designer
            and gform.count("_keyboardHook.InstallHook(this.OnKeyPress);") == 1
        ),
        "normal mode stays click-through and edit mode is explicit and handle-based": (
            "if (!EditMode)" in overlay
            and "cp.ExStyle |= WS_EX_TRANSPARENT;" in overlay
            and "if (!EditMode && m.Msg == WM_NCHITTEST)" in overlay
            and "public bool BeginEditMode()" in overlay
            and "public void EndEditMode()" in overlay
            and "GetResizeHandleRectangle().Contains(e.Location)" in overlay
            and "MoveFromDrag(deltaX, deltaY);" in overlay
            and "ResizeFromDrag(deltaX, deltaY);" in overlay
            and '"拖动移动  右下角缩放  右键完成"' in overlay
            and "Dx9Overlay.BeginEditMode()" in dx9
            and "Dx9Overlay.EndEditMode()" in dx9
        ),
        "timeline is the historical three-row previous-current-next window": (
            "private const int Dx9OverlayTimelineEntryCount = 3;" in dx9
            and "CreateDx9OverlayTimeline(out timelineFirst, out timelineSecond, out timelineThird);" in snapshot_method
            and "int start = Math.Max(0, displayStep - 1);" in snapshot_method
            and "start = count - visibleCount;" in snapshot_method
            and "bool isCurrent = index == displayStep;" in snapshot_method
            and "point.GetNickName()" in snapshot_method
            and "TItem.TimeSpanToStringLite(point.Best)" in snapshot_method
            and "TItem.TimeSpanToStringLite(point.Current)" in snapshot_method
            and "comparisonSeconds = point.GetCHA();" in snapshot_method
            and "FormatDx9OverlayDifference" not in snapshot_method
            and "public readonly string Current;" in overlay
            and "DrawOutlinedText(graphics, entry.Current" in overlay
            and "TimelineFirst" in overlay
            and "TimelineSecond" in overlay
            and "TimelineThird" in overlay
        ),
        "overlay omits patch and estimate text and uses neutral color-key-safe text": (
            "GameVersion" not in overlay
            and "Estimate" not in overlay
            and "GetGameVersion()" not in snapshot_method
            and "GetPointEnd()" not in snapshot_method
            and "TextRenderingHint.SingleBitPerPixelGridFit" in overlay
            and "Color.Lime" not in overlay
            and "Color.Yellow" not in overlay
            and "Color.Red" not in overlay
            and "string.IsNullOrEmpty(LayoutSettings.FontFamily) ? snapshot.FontFamily : LayoutSettings.FontFamily" in overlay
            and "new Font(fontFamily, LayoutSettings.FontSize * scale, LayoutSettings.FontStyle" in overlay
            and "new Font(fontFamily, GetTimerFontLogicalPoints() * scale, LayoutSettings.FontStyle" in overlay
            and 'return "SimSun";' in snapshot_method
            and 'return "MingLiU";' in snapshot_method
            and "Microsoft YaHei" not in overlay
            and "Consolas" not in overlay
            and 'string name = entry.IsCurrent ? "> " + entry.Name : entry.Name;' in overlay
            and "DrawOutlinedText(graphics, name, font, nameBrush, shadowBrush, nameRect, rightFormat, scale);" in overlay
            and "ConfiguredFontColorArgb" in overlay
            and "ApplyFontColor(Color color)" in overlay
            and "洋红色用于叠加窗口透明色键" in overlay
        ),
        "overlay has no capture network memory or IPC path": not any(
            token in overlay for token in forbidden_overlay_paths
        ),
        "snapshot reuses current core state and not legacy SI": (
            "GameWindowHandle" in snapshot_method
            and "MT.ToString()" in snapshot_method
            and "GetMoreInfo()" in snapshot_method
            and "form.ManualPauseCount" in snapshot_method
            and "SI.ins" not in snapshot_method
            and "CurrentStep =" not in snapshot_method
            and ".Current =" not in snapshot_method
        ),
        "manual pause count is display-only and precedes battle timing": (
            "public int ManualPauseCount" in gform
            and "get { return HandPauseCount; }" in gform
            and "public readonly int ManualPauseCount;" in overlay
            and 'string timing = "暂停" + snapshot.ManualPauseCount.ToString() + "  战斗 " + snapshot.BattleTimer;' in overlay
            and "ManualPauseCount =" not in snapshot_method
            and "HandPauseCount++" not in snapshot_method
            and "UIPause()" not in snapshot_method
        ),
        "overlay is closed when DX9 UI unloads": (
            "public override void UnloadUI()" in dx9
            and "CloseDx9Overlay();" in dx9
            and "base.UnloadUI();" in dx9
        ),
        "other PAL98 cores do not reference the overlay": all(
            "Dx9Overlay" not in path.read_text(encoding="utf-8-sig")
            for path in OTHER_PAL98_CORES
        ),
        "version is 3.37.0 everywhere": (
            'public const string CurrentVersion = "3.37.0";' in gform
            and '[assembly: AssemblyVersion("3.37.0")]' in assembly
            and '[assembly: AssemblyFileVersion("3.37.0")]' in assembly
        ),
    }

    failed = [name for name, ok in checks.items() if not ok]
    if failed:
        print("FAIL: PAL98DX9 overlay regression contract failed:")
        for name in failed:
            print(f"- {name}")
        return 1

    print("PASS: PAL98DX9 overlay is default-off, 10 Hz, current-time timeline enabled, edit-gated, conflict-guarded, capture-free, network-free, and isolated from other cores.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
