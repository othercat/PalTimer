param(
    [string]$ExePath = "",
    [string]$PreviewPath = ""
)

$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "ASSERT FAILED: $Message"
    }
}

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    throw "This regression check must run in an STA PowerShell process."
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path (Split-Path -Parent $PSScriptRoot) "Pal98Timer\bin\x64\Release\Pal98Timer.exe"
}
$ExePath = [IO.Path]::GetFullPath($ExePath)
Assert-True (Test-Path -LiteralPath $ExePath -PathType Leaf) "Release x64 Pal98Timer.exe must exist"
$resolvedPreviewPath = ""
if (-not [string]::IsNullOrWhiteSpace($PreviewPath)) {
    $resolvedPreviewPath = [IO.Path]::GetFullPath($PreviewPath)
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class Dx9OverlayReflectionProvider<T>
{
    public static T Value;
    public static T GetValue() { return Value; }
}

public static class Dx9OverlayNativeTest
{
    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$binding = [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::NonPublic
$originalDirectory = [Environment]::CurrentDirectory
$tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ("PalTimer-Dx9Overlay-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($tempDirectory) | Out-Null
$hostForm = $null
$overlayForm = $null

try {
    [Environment]::CurrentDirectory = $tempDirectory
    $assembly = [Reflection.Assembly]::LoadFrom($ExePath)
    $layoutType = $assembly.GetType("Pal98Timer.Dx9OverlayLayoutSettings", $true)
    $settingsType = $assembly.GetType("Pal98Timer.Dx9OverlaySettings", $true)
    $snapshotType = $assembly.GetType("Pal98Timer.Dx9OverlaySnapshot", $true)
    $timelineType = $assembly.GetType("Pal98Timer.Dx9OverlayTimelineEntry", $true)
    $overlayType = $assembly.GetType("Pal98Timer.Dx9OverlayForm", $true)

    $createDefault = $layoutType.GetMethod("CreateDefault", $binding)
    $loadLayout = $settingsType.GetMethod("LoadLayout", $binding)
    $saveLayout = $settingsType.GetMethod("SaveLayout", $binding)
    $layout = $createDefault.Invoke($null, @())
    Assert-True ($layoutType.GetField("PositionX", $binding).GetValue($layout) -eq 1.0) "default X anchor must remain right"
    Assert-True ($layoutType.GetField("PositionY", $binding).GetValue($layout) -eq 1.0) "default Y anchor must remain bottom"
    Assert-True ($layoutType.GetField("Scale", $binding).GetValue($layout) -eq 1.0) "default scale must remain 1.0"
    Assert-True ([string]::IsNullOrEmpty($layoutType.GetField("FontFamily", $binding).GetValue($layout))) "default font must remain automatic"
    Assert-True ($layoutType.GetField("FontColorArgb", $binding).GetValue($layout) -eq 0) "default font color must remain the low-saturation palette"
    Assert-True ($layoutType.GetField("ToggleHotkey", $binding).GetValue($layout) -eq [Windows.Forms.Keys]::None) "default overlay hotkey must remain unset"

    $configuredHotkey = [Windows.Forms.Keys]([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::Shift -bor [int][Windows.Forms.Keys]::O)
    $configuredColor = [Drawing.Color]::FromArgb(255, 126, 178, 204)
    $layoutType.GetField("PositionX", $binding).SetValue($layout, [single]0.25)
    $layoutType.GetField("PositionY", $binding).SetValue($layout, [single]0.50)
    $layoutType.GetField("Scale", $binding).SetValue($layout, [single]1.25)
    $layoutType.GetField("FontFamily", $binding).SetValue($layout, "Arial")
    $layoutType.GetField("FontSize", $binding).SetValue($layout, [single]11.0)
    $layoutType.GetField("FontColorArgb", $binding).SetValue($layout, $configuredColor.ToArgb())
    $layoutType.GetField("ToggleHotkey", $binding).SetValue($layout, $configuredHotkey)
    $saveLayout.Invoke($null, @($layout)) | Out-Null
    $loaded = $loadLayout.Invoke($null, @())
    Assert-True ([Math]::Abs($layoutType.GetField("Scale", $binding).GetValue($loaded) - 1.25) -lt 0.001) "layout scale must round-trip"
    Assert-True ($layoutType.GetField("FontFamily", $binding).GetValue($loaded) -eq "Arial") "font family must round-trip"
    Assert-True ($layoutType.GetField("FontColorArgb", $binding).GetValue($loaded) -eq $configuredColor.ToArgb()) "font color must round-trip"
    Assert-True ($layoutType.GetField("ToggleHotkey", $binding).GetValue($loaded) -eq $configuredHotkey) "overlay hotkey must round-trip"

    $validateHotkey = $settingsType.GetMethod("ValidateToggleHotkey", $binding)
    $f9Hotkey = [Windows.Forms.Keys]([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::F9)
    $ctrlEnterHotkey = [Windows.Forms.Keys]([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::Enter)
    Assert-True (-not [string]::IsNullOrEmpty($validateHotkey.Invoke($null, @($f9Hotkey, [Windows.Forms.Keys]::None)))) "F1-F12 must stay reserved even with modifiers"
    Assert-True (-not [string]::IsNullOrEmpty($validateHotkey.Invoke($null, @($ctrlEnterHotkey, [Windows.Forms.Keys]::None)))) "Ctrl+Enter must stay reserved"
    Assert-True (-not [string]::IsNullOrEmpty($validateHotkey.Invoke($null, @([Windows.Forms.Keys]::O, [Windows.Forms.Keys]::None)))) "unmodified game keys must be rejected"
    Assert-True (-not [string]::IsNullOrEmpty($validateHotkey.Invoke($null, @($configuredHotkey, $configuredHotkey)))) "sound hotkey collisions must be rejected"
    Assert-True ([string]::IsNullOrEmpty($validateHotkey.Invoke($null, @($configuredHotkey, [Windows.Forms.Keys]::None)))) "a distinct modified key must be accepted"

    $defaultLayout = $createDefault.Invoke($null, @())
    $timelineConstructor = $timelineType.GetConstructors($binding) | Where-Object { $_.GetParameters().Count -eq 6 } | Select-Object -First 1
    $entry1 = $timelineConstructor.Invoke(@("Split A", "00:06:05", "00:06:01", [long]-4, $true, $false))
    $entry2 = $timelineConstructor.Invoke(@("Split B", "00:11:13", "", [long]0, $false, $false))
    $entry3 = $timelineConstructor.Invoke(@("Split C", "00:18:37", "", [long]0, $false, $false))
    Assert-True ($timelineType.GetField("Current", $binding).GetValue($entry1) -eq "00:06:01") "timeline third column must carry the current cumulative time"
    Assert-True ($timelineType.GetField("ComparisonSeconds", $binding).GetValue($entry1) -eq -4) "timeline current-time color must preserve the faster/slower comparison"

    $hostForm = New-Object Windows.Forms.Form
    $hostForm.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $hostForm.Location = New-Object Drawing.Point(80, 70)
    $hostForm.ClientSize = New-Object Drawing.Size(800, 600)
    $hostForm.Show()
    [Windows.Forms.Application]::DoEvents()
    [Dx9OverlayNativeTest]::SetForegroundWindow($hostForm.Handle) | Out-Null

    $snapshotConstructor = $snapshotType.GetConstructors($binding) | Where-Object { $_.GetParameters().Count -eq 13 } | Select-Object -First 1
    $snapshot = $snapshotConstructor.Invoke(@(
        $hostForm.Handle, "SimSun", "00:00:18.31", "0.00s", "00:00:14.89", "Bee0 Honey0 Fire0 Blood0",
        3, $entry1, $entry2, $entry3, "", $false, $false
    ))

    $openProviderType = [AppDomain]::CurrentDomain.GetAssemblies() |
        ForEach-Object { $_.GetType("Dx9OverlayReflectionProvider``1", $false) } |
        Where-Object { $_ -ne $null } |
        Select-Object -First 1
    $providerType = $openProviderType.MakeGenericType($snapshotType)
    $providerType.GetField("Value", $binding).SetValue($null, $snapshot)
    $funcType = ([Func[int]]).GetGenericTypeDefinition().MakeGenericType($snapshotType)
    $provider = [Delegate]::CreateDelegate($funcType, $providerType.GetMethod("GetValue", $binding))

    $overlayConstructor = $overlayType.GetConstructors($binding) |
        Where-Object { $_.GetParameters().Count -eq 2 } |
        Select-Object -First 1
    $overlayForm = $overlayConstructor.Invoke(@($provider, $defaultLayout))
    $overlayType.GetMethod("Start", $binding).Invoke($overlayForm, @()) | Out-Null
    [Windows.Forms.Application]::DoEvents()

    Assert-True $overlayForm.Visible "overlay must show over a valid foreground host"
    $hostOrigin = $hostForm.PointToScreen([Drawing.Point]::Empty)
    Assert-True ($overlayForm.Right -eq $hostOrigin.X + $hostForm.ClientSize.Width) "default overlay must remain right-aligned"
    Assert-True ($overlayForm.Bottom -eq $hostOrigin.Y + $hostForm.ClientSize.Height) "default overlay must remain bottom-aligned"
    $normalStyle = [Dx9OverlayNativeTest]::GetWindowLong($overlayForm.Handle, -20)
    Assert-True (($normalStyle -band 0x20) -ne 0) "normal overlay must retain WS_EX_TRANSPARENT"

    $beginEdit = $overlayType.GetMethod("BeginEditMode", $binding).Invoke($overlayForm, @())
    [Windows.Forms.Application]::DoEvents()
    Assert-True $beginEdit "edit mode must start with a valid game window"
    $editStyle = [Dx9OverlayNativeTest]::GetWindowLong($overlayForm.Handle, -20)
    Assert-True (($editStyle -band 0x20) -eq 0) "edit mode must temporarily remove WS_EX_TRANSPARENT"

    $dragStartBoundsField = $overlayType.GetField("DragStartBounds", $binding)
    $beforeMove = $overlayForm.Bounds
    $dragStartBoundsField.SetValue($overlayForm, $beforeMove)
    $overlayType.GetMethod("MoveFromDrag", $binding).Invoke($overlayForm, @(-100, -80)) | Out-Null
    Assert-True ($overlayForm.Left -lt $beforeMove.Left -and $overlayForm.Top -lt $beforeMove.Top) "drag move must change both coordinates"

    $beforeResize = $overlayForm.Bounds
    $dragStartBoundsField.SetValue($overlayForm, $beforeResize)
    $overlayType.GetMethod("ResizeFromDrag", $binding).Invoke($overlayForm, @(-80, -40)) | Out-Null
    Assert-True ($overlayForm.Width -lt $beforeResize.Width) "lower-right resize must adjust the complete overlay scale"

    $overlayType.GetMethod("ApplyFont", $binding).Invoke($overlayForm, @("Arial", [single]11.0, [Drawing.FontStyle]::Italic)) | Out-Null
    $overlayType.GetMethod("ApplyFontColor", $binding).Invoke($overlayForm, @($configuredColor)) | Out-Null
    $overlayType.GetMethod("ApplyToggleHotkey", $binding).Invoke($overlayForm, @($configuredHotkey)) | Out-Null
    $fontLayout = $loadLayout.Invoke($null, @())
    Assert-True ($layoutType.GetField("FontFamily", $binding).GetValue($fontLayout) -eq "Arial") "selected font must persist"
    Assert-True ([Math]::Abs($layoutType.GetField("FontSize", $binding).GetValue($fontLayout) - 11.0) -lt 0.001) "selected font size must persist"
    Assert-True ($layoutType.GetField("FontColorArgb", $binding).GetValue($fontLayout) -eq $configuredColor.ToArgb()) "selected font color must persist"
    Assert-True ($layoutType.GetField("ToggleHotkey", $binding).GetValue($fontLayout) -eq $configuredHotkey) "selected overlay hotkey must persist"

    $bitmap = New-Object Drawing.Bitmap($overlayForm.Width, $overlayForm.Height)
    try {
        $overlayForm.DrawToBitmap($bitmap, $overlayForm.ClientRectangle)
        if (-not [string]::IsNullOrWhiteSpace($resolvedPreviewPath)) {
            $bitmap.Save($resolvedPreviewPath, [Drawing.Imaging.ImageFormat]::Png)
        }
    }
    finally {
        $bitmap.Dispose()
    }

    $overlayType.GetMethod("EndEditMode", $binding).Invoke($overlayForm, @()) | Out-Null
    [Dx9OverlayNativeTest]::SetForegroundWindow($hostForm.Handle) | Out-Null
    $overlayType.GetMethod("RefreshOverlay", $binding).Invoke($overlayForm, @()) | Out-Null
    [Windows.Forms.Application]::DoEvents()
    $restoredStyle = [Dx9OverlayNativeTest]::GetWindowLong($overlayForm.Handle, -20)
    Assert-True (($restoredStyle -band 0x20) -ne 0) "ending edit mode must restore click-through style"

    $overlayType.GetMethod("ResetLayout", $binding).Invoke($overlayForm, @()) | Out-Null
    $reset = $loadLayout.Invoke($null, @())
    Assert-True ($layoutType.GetField("PositionX", $binding).GetValue($reset) -eq 1.0) "reset must restore right anchor"
    Assert-True ($layoutType.GetField("PositionY", $binding).GetValue($reset) -eq 1.0) "reset must restore bottom anchor"
    Assert-True ($layoutType.GetField("Scale", $binding).GetValue($reset) -eq 1.0) "reset must restore 1.0 scale"
    Assert-True ([string]::IsNullOrEmpty($layoutType.GetField("FontFamily", $binding).GetValue($reset))) "reset must restore automatic font"
    Assert-True ($layoutType.GetField("FontColorArgb", $binding).GetValue($reset) -eq 0) "reset must restore the default font color palette"
    Assert-True ($layoutType.GetField("ToggleHotkey", $binding).GetValue($reset) -eq [Windows.Forms.Keys]::None) "reset must clear the overlay hotkey"

    Write-Output "PASS: overlay current-time timeline, layout round-trip, hotkey conflict guards, edit-only movement/resize, render, reset, and click-through restoration all passed."
}
finally {
    if ($overlayForm -ne $null) {
        try { $overlayForm.Dispose() } catch { }
    }
    if ($hostForm -ne $null) {
        try { $hostForm.Dispose() } catch { }
    }
    [Environment]::CurrentDirectory = $originalDirectory
    if ([IO.Directory]::Exists($tempDirectory)) {
        [IO.Directory]::Delete($tempDirectory, $true)
    }
}
