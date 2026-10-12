$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Path $bin -Force | Out-Null

$pfimSource = Join-Path $root 'third_party_bin\Pfim.0.9.1\lib\netstandard2.0\Pfim.dll'
if(-not (Test-Path $pfimSource)) { throw "Pfim runtime not found: $pfimSource" }
Copy-Item $pfimSource (Join-Path $bin 'Pfim.dll') -Force

$deps = @(
  'W3Edit.exe',
  'W3Edit.CR2W.dll',
  'W3Edit.Bundles.dll',
  'W3Edit.W3Strings.dll',
  'WeifenLuo.WinFormsUI.Docking.dll',
  'ObjectListView.dll',
  'LZ4.dll',
  'Doboz.dll',
  'Ionic.Zlib.dll',
  'Pfim.dll'
)

$referenceRuntime = $env:W3FORGE_W3EDIT_REFERENCE
if([string]::IsNullOrWhiteSpace($referenceRuntime)) {
  $candidate = Join-Path $root 'ReferenceRuntime'
  if(Test-Path $candidate) { $referenceRuntime = $candidate }
}
if([string]::IsNullOrWhiteSpace($referenceRuntime)) {
  $missing = $deps | Where-Object { -not (Test-Path (Join-Path $bin $_)) }
  if($missing.Count -eq 0) { $referenceRuntime = $bin }
}
if([string]::IsNullOrWhiteSpace($referenceRuntime) -or -not (Test-Path $referenceRuntime)) {
  $message = 'W3Edit reference runtime not found.' + [Environment]::NewLine + [Environment]::NewLine +
             'Set W3FORGE_W3EDIT_REFERENCE to a directory containing the required W3Edit compatibility assemblies,' + [Environment]::NewLine +
             'or place them in .\ReferenceRuntime.' + [Environment]::NewLine + [Environment]::NewLine +
             'Required:' + [Environment]::NewLine + ($deps -join [Environment]::NewLine)
  throw $message
}

foreach($d in $deps) {
  $from = Join-Path $referenceRuntime $d
  if(-not (Test-Path $from)) { throw "Missing reference dependency: $from" }
  $to = Join-Path $bin $d
  if([IO.Path]::GetFullPath($from) -ne [IO.Path]::GetFullPath($to)) {
    Copy-Item $from $to -Force
  }
}

$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if(-not (Test-Path $fw)) { throw '.NET Framework 4.x compiler/reference directory not found.' }

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$sdkLine = (& $dotnet --list-sdks | Select-Object -Last 1)
if(-not $sdkLine -or $sdkLine -notmatch '^([0-9A-Za-z\.\-]+)\s+\[(.+)\]$') {
  throw 'No .NET SDK found. Install a current .NET SDK to build W3 Forge Studio.'
}
$sdkVersion = $matches[1]
$sdkBase = $matches[2]
$csc = Join-Path (Join-Path $sdkBase $sdkVersion) 'Roslyn\bincore\csc.dll'
if(-not (Test-Path $csc)) { throw 'Roslyn csc.dll was not found in the installed .NET SDK.' }

$refs = @(
  "$fw\mscorlib.dll",
  "$fw\System.dll",
  "$fw\System.Core.dll",
  "$fw\System.Xml.dll",
  "$fw\System.Xml.Linq.dll",
  "$fw\System.Drawing.dll",
  "$fw\System.Data.dll",
  "$fw\System.Web.Extensions.dll",
  "$fw\System.IO.Compression.dll",
  "$fw\System.Windows.Forms.dll",
  "$fw\System.Xaml.dll",
  "$fw\WPF\WindowsBase.dll",
  "$fw\WPF\PresentationCore.dll",
  "$fw\WPF\PresentationFramework.dll",
  "$fw\WPF\WindowsFormsIntegration.dll",
  "$fw\netstandard.dll",
  "$bin\Pfim.dll",
  "$bin\W3Edit.exe",
  "$bin\W3Edit.Bundles.dll",
  "$bin\W3Edit.CR2W.dll",
  "$bin\WeifenLuo.WinFormsUI.Docking.dll",
  "$bin\ObjectListView.dll",
  "$bin\LZ4.dll",
  "$bin\Doboz.dll",
  "$bin\Ionic.Zlib.dll"
)

$out = Join-Path $bin 'W3ForgeStudio.exe'
$icon = Join-Path $assets 'W3ForgeStudio.ico'
if(-not (Test-Path $icon)) { throw "Application icon not found: $icon" }

$args = @(
  '/nologo',
  '/target:winexe',
  '/platform:anycpu',
  '/optimize+',
  "/out:$out",
  "/win32icon:$icon"
)
$args += $refs | ForEach-Object { "/reference:$_" }
$args += Get-ChildItem $src -Filter '*.cs' -File | ForEach-Object { $_.FullName }

& $dotnet $csc @args
if($LASTEXITCODE -ne 0) { throw "Compiler returned code $LASTEXITCODE" }

Copy-Item (Join-Path $src 'Theme.xaml') (Join-Path $bin 'Theme.xaml') -Force
Copy-Item (Join-Path $root 'docs\W3ForgeStudio.exe.config') (Join-Path $bin 'W3ForgeStudio.exe.config') -Force

# Keep local development runs consistent with the publicly shipped Runtime layout.
$runtime = Join-Path $bin 'Runtime'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
foreach($d in $deps){ Copy-Item (Join-Path $bin $d) (Join-Path $runtime $d) -Force }
$layout = Join-Path $bin 'cr2wdocument_layout.xml'
if(Test-Path $layout){ Copy-Item $layout (Join-Path $runtime 'cr2wdocument_layout.xml') -Force }

Write-Host 'COMPILE_OK'
(Get-Item $out).VersionInfo | Select-Object ProductName,FileVersion,ProductVersion
Write-Host ('EXE_SHA256=' + (Get-FileHash $out -Algorithm SHA256).Hash)