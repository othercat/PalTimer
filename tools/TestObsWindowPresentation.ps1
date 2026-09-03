[CmdletBinding()]
param(
    [string]$Executable = 'Pal98Timer\bin\x64\Release\Pal98Timer.exe'
)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    throw 'This smoke test must run in an STA PowerShell process.'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executablePath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Executable))
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Release executable not found: $executablePath"
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$assembly = [Reflection.Assembly]::LoadFrom($executablePath)
$rendererType = $assembly.GetType('Pal98Timer.GRender', $true)
$boardType = $assembly.GetType('Pal98Timer.GBoard', $true)
$presenterType = $assembly.GetType('Pal98Timer.LayeredWindowPresenter', $true)
$overlayType = $assembly.GetType('Pal98Timer.Dx9OverlayForm', $true)
$overlayLayoutType = $assembly.GetType('Pal98Timer.Dx9OverlayLayoutSettings', $true)
$overlaySnapshotType = $assembly.GetType('Pal98Timer.Dx9OverlaySnapshot', $true)
$overlayTimelineType = $assembly.GetType('Pal98Timer.Dx9OverlayTimelineEntry', $true)
$overlaySettingsType = $assembly.GetType('Pal98Timer.Dx9OverlaySettings', $true)
$obsSettingsType = $assembly.GetType('Pal98Timer.ObsWindowStyleSettings', $true)
$obsStoreType = $assembly.GetType('Pal98Timer.ObsWindowStyleStore', $true)

function Get-VisiblePixelCount([Drawing.Bitmap]$Bitmap) {
    $count = 0
    for ($y = 0; $y -lt $Bitmap.Height; $y += 1) {
        for ($x = 0; $x -lt $Bitmap.Width; $x += 1) {
            if ($Bitmap.GetPixel($x, $y).A -gt 0) {
                $count += 1
            }
        }
    }
    return $count
}

$panel = [Windows.Forms.Panel]::new()
$panel.Size = [Drawing.Size]::new(327, 480)
$board = [Activator]::CreateInstance($boardType)
$renderer = [Activator]::CreateInstance($rendererType, @($panel, $false))
$renderer.SetGBoard($board)
$renderer.SetTitle('Pal98 Timer')
$renderer.SetGameVersion('PAL98DX9')
$renderer.SetVersion('3.37.0')
$renderer.SetMainTimer([TimeSpan]::FromSeconds(3723.45))
$renderer.SetSubTimer('Battle 12.34')
$renderer.SetOutTimer('- 0:01.23')
$renderer.SetMoreInfo('Bee2 Honey1 Fire3')
$null = $renderer.AddBtn('Menu', $null, 0)
$null = $renderer.AddItem('General', [TimeSpan]::FromMinutes(20), -1)

$renderer.SetChromeOpacity(100)
$null = $renderer.Draw($null)
$opaqueFrame = [Drawing.Bitmap]$renderer.GetFrameBitmap().Clone()
$opaqueVisiblePixels = Get-VisiblePixelCount $opaqueFrame

$renderer.SetChromeOpacity(0)
$null = $renderer.Draw($null)
$textFrame = [Drawing.Bitmap]$renderer.GetFrameBitmap().Clone()
$textVisiblePixels = Get-VisiblePixelCount $textFrame
if ($textVisiblePixels -le 0) {
    throw 'Pure-text frame unexpectedly contains no visible pixels.'
}
if ($textVisiblePixels -ge $opaqueVisiblePixels) {
    throw "Chrome opacity did not reduce visible pixels: text=$textVisiblePixels full=$opaqueVisiblePixels"
}

$form = [Windows.Forms.Form]::new()
$form.FormBorderStyle = [Windows.Forms.FormBorderStyle]::None
$form.ShowInTaskbar = $false
$form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
$form.Bounds = [Drawing.Rectangle]::new(-2000, -2000, $textFrame.Width, $textFrame.Height)
try {
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $presenterType.GetMethod('Present').Invoke($null, @($form, $textFrame))
    [Windows.Forms.Application]::DoEvents()
}
finally {
    $form.Close()
    $form.Dispose()
    $opaqueFrame.Dispose()
    $textFrame.Dispose()
    $board.Dispose()
    $panel.Dispose()
}

$timeline = [Activator]::CreateInstance($overlayTimelineType)
$snapshot = [Activator]::CreateInstance(
    $overlaySnapshotType,
    @(
        [IntPtr]::Zero,
        'SimSun',
        '01:02:03.45',
        'Battle 12.34',
        '',
        'Bee2 Honey1 Fire3',
        0,
        $timeline,
        $timeline,
        $timeline,
        '',
        $false,
        $false
    ))
$funcType = [Func``1].MakeGenericType($overlaySnapshotType)
$provider = [Linq.Expressions.Expression]::Lambda(
    $funcType,
    [Linq.Expressions.Expression]::Constant($snapshot, $overlaySnapshotType),
    [Linq.Expressions.ParameterExpression[]]@()).Compile()
$layout = $overlayLayoutType.GetMethod('CreateDefault').Invoke($null, @())
$layout.GetType().GetField('HasWindowPosition').SetValue($layout, $true)
$layout.GetType().GetField('WindowLeft').SetValue($layout, [Windows.Forms.SystemInformation]::VirtualScreen.Left)
$layout.GetType().GetField('WindowTop').SetValue($layout, [Windows.Forms.SystemInformation]::VirtualScreen.Top)
$overlayConstructor = $overlayType.GetConstructors(
    [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic) |
    Where-Object { $_.GetParameters().Count -eq 2 } |
    Select-Object -First 1
$overlay = $overlayConstructor.Invoke(@($provider, $layout))
try {
    $expectedOverlayTitle = $overlayType.GetField(
        'ObsWindowTitle',
        [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic).GetValue($null)
    if ($overlay.Text -ne $expectedOverlayTitle) {
        throw "Unexpected independent overlay title: $($overlay.Text)"
    }
    if (-not $overlay.ShowInTaskbar) {
        throw 'Independent overlay must be a taskbar-visible top-level window for OBS enumeration.'
    }
    $overlay.Opacity = 0
    $overlay.Start()
    [Windows.Forms.Application]::DoEvents()
    if (-not $overlay.Visible) {
        throw 'Independent overlay did not stay visible without a PAL game window.'
    }
}
finally {
    $overlay.Stop()
    $overlay.Dispose()
}

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('PalTimerObsTest-' + [Guid]::NewGuid().ToString('N'))
$originalCurrentDirectory = [Environment]::CurrentDirectory
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
try {
    [Environment]::CurrentDirectory = $temporaryDirectory
    [IO.File]::WriteAllText(
        (Join-Path $temporaryDirectory 'dx9_overlay_layout'),
        "version=1`r`nposition_x=0.2500`r`nposition_y=0.7500`r`nscale=1.2500`r`n",
        [Text.UTF8Encoding]::new($false))
    $legacyLayout = $overlaySettingsType.GetMethod('LoadLayout').Invoke($null, @())
    if ($legacyLayout.GetType().GetField('HasWindowPosition').GetValue($legacyLayout)) {
        throw 'Legacy overlay layout unexpectedly acquired an absolute window position.'
    }
    $legacyLayout.GetType().GetField('HasWindowPosition').SetValue($legacyLayout, $true)
    $legacyLayout.GetType().GetField('WindowLeft').SetValue($legacyLayout, -321)
    $legacyLayout.GetType().GetField('WindowTop').SetValue($legacyLayout, 123)
    $overlaySettingsType.GetMethod('SaveLayout').Invoke($null, @($legacyLayout))
    $savedLayoutText = [IO.File]::ReadAllText((Join-Path $temporaryDirectory 'dx9_overlay_layout'))
    if (-not $savedLayoutText.Contains('version=2') -or
        -not $savedLayoutText.Contains('window_left=-321') -or
        -not $savedLayoutText.Contains('window_top=123')) {
        throw 'Independent overlay layout was not persisted as version 2.'
    }

    [IO.File]::WriteAllText(
        (Join-Path $temporaryDirectory 'obs_window_style'),
        "version=1`r`nenabled=1`r`nchrome_opacity=37`r`n",
        [Text.UTF8Encoding]::new($false))
    $legacyObsSettings = $obsStoreType.GetMethod('Load').Invoke($null, @())
    if ($legacyObsSettings.GetType().GetField('ToggleHotkey').GetValue($legacyObsSettings) -ne [Windows.Forms.Keys]::None) {
        throw 'Legacy OBS window style settings unexpectedly acquired a toggle hotkey.'
    }

    $obsSettings = $obsSettingsType.GetMethod('CreateDefault').Invoke($null, @())
    $obsSettings.GetType().GetField('Enabled').SetValue($obsSettings, $true)
    $obsSettings.GetType().GetField('ChromeOpacity').SetValue($obsSettings, 37)
    $obsHotkey = [Windows.Forms.Keys]::Control -bor [Windows.Forms.Keys]::O
    $obsSettings.GetType().GetField('ToggleHotkey').SetValue($obsSettings, $obsHotkey)
    $obsStoreType.GetMethod('Save').Invoke($null, @($obsSettings))
    $loadedObsSettings = $obsStoreType.GetMethod('Load').Invoke($null, @())
    if (-not $loadedObsSettings.GetType().GetField('Enabled').GetValue($loadedObsSettings) -or
        $loadedObsSettings.GetType().GetField('ChromeOpacity').GetValue($loadedObsSettings) -ne 37 -or
        $loadedObsSettings.GetType().GetField('ToggleHotkey').GetValue($loadedObsSettings) -ne $obsHotkey) {
        throw 'OBS window style settings did not round-trip.'
    }
    $savedObsText = [IO.File]::ReadAllText((Join-Path $temporaryDirectory 'obs_window_style'))
    if (-not $savedObsText.Contains('version=2') -or
        -not $savedObsText.Contains("toggle_hotkey=$([int]$obsHotkey)")) {
        throw 'OBS window style hotkey was not persisted as version 2.'
    }
}
finally {
    [Environment]::CurrentDirectory = $originalCurrentDirectory
    [IO.Directory]::Delete($temporaryDirectory, $true)
}

[ordered]@{
    executable = $executablePath
    full_ui_visible_pixels = $opaqueVisiblePixels
    pure_text_visible_pixels = $textVisiblePixels
    layered_window_present = 'passed'
    independent_overlay_without_game = 'passed'
    settings_compatibility = 'passed'
    obs_style_hotkey_compatibility = 'passed'
} | ConvertTo-Json
