param([Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$RuntimeDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a fresh Issue artifact directory.'}
[void][IO.Directory]::CreateDirectory($out)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc=& $vswhere -latest -products '*' -version '[18.0,19.0)' -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if(!$csc){throw 'VS 2026 Roslyn is required.'}
Get-ChildItem -LiteralPath $RuntimeDirectory -File | Where-Object {$_.Extension -in @('.dll','.exe','.config')} | Copy-Item -Destination $out
$hostExe=Join-Path $out 'UnifiedProductBehavior.exe'
Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'Pal98Timer.exe.config') -Destination ($hostExe+'.config')
& $csc /nologo /target:exe /platform:x64 "/out:$hostExe" /reference:System.dll /reference:System.Core.dll (Join-Path $PSScriptRoot 'unified_product_behavior.cs')
if($LASTEXITCODE){throw 'Product probe compilation failed.'}
& $hostExe (Join-Path $out 'Pal98Timer.exe') (Join-Path $out 'PalTimerOnline.dll') 2>&1 | Tee-Object -FilePath (Join-Path $out 'result.txt')
if($LASTEXITCODE){throw 'Product probe failed.'}
