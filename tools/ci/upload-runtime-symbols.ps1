<#
.SYNOPSIS
    Uploads Microsoft's debug files for the .NET runtime libraries inside an Android bundle to Sentry.

.DESCRIPTION
    The runtime libraries an Android build ships - libmonosgen-2.0.so and the System.*.Native
    libraries - come stripped from the runtime pack, and the symbol upload the Sentry package does
    after a build sends those same stripped files. Their exported functions keep their names, but a
    crash inside the GC itself lands in static functions such as the bridge's push_object, and those
    read as bare addresses in libmonosgen. Microsoft publishes the debug information for each of those
    builds on its symbol server, keyed by the library's ELF build id:

        https://msdl.microsoft.com/download/symbols/_.debug/elf-buildid-sym-<build id>/_.debug

    This takes each native library out of the bundle that is actually being shipped, reads its build
    id with sentry-cli, fetches Microsoft's debug file for it, checks the file really is that build's
    debug information, and uploads what it found. A library Microsoft did not build - the app's own,
    Sentry's, SkiaSharp's - is simply not on the server and is skipped. The AOT-compiled assemblies
    carry no build id and are left out.

    A library that cannot be read or fetched is skipped with a warning rather than ending the run, so
    one bad file or a slow server does not cost the rest. Without SENTRY_AUTH_TOKEN it downloads
    nothing and exits cleanly, like the Sentry package's own upload, so a build without the secret is
    unaffected. -NoUpload does everything but the upload.

.PARAMETER Bundle
    The .aab or .apk to read the native libraries from.

.PARAMETER SentryCli
    Path to sentry-cli. Defaults to the one inside the Sentry NuGet package this repo pins in
    Directory.Packages.props, which is the same binary the package's own upload runs.

.PARAMETER WorkDir
    Scratch directory for the extracted libraries and downloaded debug files. Emptied first.

.PARAMETER NoUpload
    Resolve, download and check, but upload nothing. For checking the script locally.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Bundle,
    [string] $SentryCli,
    [string] $WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'redmist-runtime-symbols'),
    [switch] $NoUpload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $NoUpload -and [string]::IsNullOrWhiteSpace($env:SENTRY_AUTH_TOKEN)) {
    Write-Host 'SENTRY_AUTH_TOKEN is not set; skipping the runtime symbol upload.'
    exit 0
}

if (-not $SentryCli) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
    [xml] $packages = Get-Content (Join-Path $repoRoot 'Directory.Packages.props')
    $sentryVersion = ($packages.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'Sentry' }).Version
    $globalPackages = ((dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
    $binary = if ($IsWindows) { 'sentry-cli-Windows-x86_64.exe' } elseif ($IsMacOS) { 'sentry-cli-Darwin-arm64' } else { 'sentry-cli-Linux-x86_64' }
    $SentryCli = Join-Path $globalPackages "sentry/$sentryVersion/tools/$binary"
}
if (-not (Test-Path $SentryCli)) {
    throw "sentry-cli not found at $SentryCli. Restore the solution first, or pass -SentryCli."
}

# What sentry-cli knows about one file, or $null when it could not read it. It exits non-zero with
# nothing on stdout for a file it does not understand.
function Get-DebugFileInfo([string] $Path) {
    $output = & $SentryCli debug-files check --json $Path 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $output) { return $null }
    return ($output | Out-String | ConvertFrom-Json)
}

$libraries = Join-Path $WorkDir 'libraries'
$symbols = Join-Path $WorkDir 'symbols'
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $libraries, $symbols | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Bundle))
try {
    # An .aab keeps them under base/lib/<abi>/, an .apk under lib/<abi>/.
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -notmatch '(^|/)lib/(?<abi>[^/]+)/(?<name>[^/]+\.so)$' -or $Matches.name -like 'libaot-*') {
            continue
        }
        $target = Join-Path $libraries "$($Matches.abi)/$($Matches.name)"
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
}
finally {
    $zip.Dispose()
}

$found = 0
foreach ($library in Get-ChildItem $libraries -Recurse -Filter '*.so') {
    $label = "$($library.Directory.Name)/$($library.Name)"
    $info = Get-DebugFileInfo $library.FullName
    if (-not $info) {
        Write-Warning "${label}: sentry-cli could not read it; skipped"
        continue
    }
    $codeId = @($info.variants | ForEach-Object { $_.code_id } | Where-Object { $_ }) | Select-Object -First 1
    if (-not $codeId) {
        Write-Host "  ${label}: no build id, skipped"
        continue
    }

    $url = "https://msdl.microsoft.com/download/symbols/_.debug/elf-buildid-sym-$codeId/_.debug"
    $destination = Join-Path $symbols "$($library.Directory.Name)-$($library.BaseName).debug"
    try {
        Invoke-WebRequest -Uri $url -OutFile $destination -TimeoutSec 120
    }
    catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        $status = $_.Exception.Response.StatusCode
        if ($status -eq [Net.HttpStatusCode]::NotFound) {
            Write-Host "  ${label}: $codeId, not on Microsoft's symbol server"
        }
        else {
            Write-Warning "${label}: Microsoft's symbol server answered $([int] $status) for $codeId; skipped"
        }
        Remove-Item $destination -ErrorAction SilentlyContinue
        continue
    }
    catch {
        Write-Warning "${label}: could not fetch $codeId from Microsoft's symbol server ($($_.Exception.Message)); skipped"
        Remove-Item $destination -ErrorAction SilentlyContinue
        continue
    }

    # Checked rather than trusted, and for debug information specifically: the stripped library itself
    # reports a symbol table too, so a symbol table alone proves nothing, and a debug file for some
    # other build would upload without complaint and simply never match.
    $debug = Get-DebugFileInfo $destination
    $debugCodeIds = if ($debug) { @($debug.variants | ForEach-Object { $_.code_id }) } else { @() }
    $features = if ($debug) { "$($debug.features)" } else { 'unreadable' }
    if ($debugCodeIds -notcontains $codeId -or $features -notmatch '\bdebug\b') {
        Remove-Item $destination
        Write-Warning "${label}: the file served for $codeId is not its debug information ($features); skipped"
        continue
    }
    Write-Host "  ${label}: $codeId, debug file found ($features)"
    $found++
}

if ($found -eq 0) {
    Write-Host 'No debug files found on Microsoft''s symbol server; nothing to upload.'
    exit 0
}

if ($NoUpload) {
    Write-Host "Found $found debug file(s) in $symbols; not uploading (-NoUpload)."
    exit 0
}

& $SentryCli debug-files upload --type elf $symbols
if ($LASTEXITCODE -ne 0) {
    throw "sentry-cli debug-files upload exited with $LASTEXITCODE"
}
Write-Host "Uploaded $found debug file(s) from Microsoft's symbol server."
