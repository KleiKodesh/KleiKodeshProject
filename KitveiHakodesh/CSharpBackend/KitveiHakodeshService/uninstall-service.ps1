<#
.SYNOPSIS
    Stops the KitveiHakodesh service, uninstalls it, and deletes its FTS index.

.DESCRIPTION
    Handles both deployment shapes:
      * Installed Windows service (name: KitveiHakodeshSvc) - stopped and deleted
        via the SCM (self-elevates, since 'sc delete' needs admin).
      * Dev mode - the service runs as a process spawned by the Vite dev plugin
        (dotnet run / KitveiHakodeshService.exe); those processes are killed.

    Then deletes the FTS index. The service builds its index into a "FtsIndex"
    folder next to its binary (AppContext.BaseDirectory), so it lives under this
    script's folder - in the installed layout (.\FtsIndex) and in the dev build
    outputs (.\bin\<Config>\net10.0\FtsIndex). Every "FtsIndex" folder found under
    this script's directory is removed, so the service and its index go together
    "in one fell swoop." Pass -KeepIndex to leave the index in place.

    Safe to run repeatedly.

.PARAMETER KeepIndex
    Stop/uninstall the service but do NOT delete the FTS index.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall-service.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall-service.ps1 -KeepIndex
#>
[CmdletBinding()]
param(
    [switch]$KeepIndex
)

$ErrorActionPreference = 'Stop'
$SvcName = 'KitveiHakodeshSvc'

function Test-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Delete a directory tree, retrying while the OS releases file handles just freed
# by the killed service (segment .db files, write.lock).
function Remove-TreeWithRetry {
    param([string]$Path)
    for ($i = 0; $i -lt 6; $i++) {
        if (-not (Test-Path -LiteralPath $Path)) { return $true }
        try { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop; return $true }
        catch { Start-Sleep -Milliseconds 400 }
    }
    return (-not (Test-Path -LiteralPath $Path))
}

Write-Host '== KitveiHakodesh service: stop + uninstall ==' -ForegroundColor Cyan

# 1. Windows service (present only if it was installed as a service).
$svc = Get-Service -Name $SvcName -ErrorAction SilentlyContinue
if ($svc) {
    if (-not (Test-Admin)) {
        Write-Host "Service '$SvcName' is registered; deleting it needs elevation - relaunching as admin..." -ForegroundColor Yellow
        $relaunch = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
        if ($KeepIndex) { $relaunch += '-KeepIndex' }
        Start-Process -FilePath 'powershell.exe' -ArgumentList $relaunch -Verb RunAs -Wait
        return
    }

    if ($svc.Status -ne 'Stopped') {
        Write-Host "Stopping service '$SvcName'..."
        try { Stop-Service -Name $SvcName -Force -ErrorAction Stop } catch { Write-Warning $_.Exception.Message }
    }
    Write-Host "Deleting service '$SvcName'..."
    & sc.exe delete $SvcName | Out-Null
    Write-Host "Service '$SvcName' uninstalled." -ForegroundColor Green
}
else {
    Write-Host "No Windows service named '$SvcName' is registered (dev runs it as a spawned process)."
}

# 2. Kill any running service processes - the dev-spawned 'dotnet run' host and the
#    apphost exe. Covers both a standalone run and the Vite-spawned child.
$procs = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'KitveiHakodeshService.exe' -or
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -like '*KitveiHakodeshService*')
}
if ($procs) {
    foreach ($proc in $procs) {
        Write-Host ("Stopping process {0} (PID {1})..." -f $proc.Name, $proc.ProcessId)
        try { Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop } catch { Write-Warning $_.Exception.Message }
    }
    Write-Host 'Service processes stopped.' -ForegroundColor Green
    Start-Sleep -Milliseconds 500   # let the OS release the index file handles
}
else {
    Write-Host 'No running KitveiHakodesh service processes found.'
}

# 3. Delete the FTS index (unless -KeepIndex). It sits next to the service binary,
#    so it's under this script's folder: the installed layout (.\FtsIndex) and the
#    dev build outputs (.\bin\<Config>\net10.0\FtsIndex).
if ($KeepIndex) {
    Write-Host 'Keeping the FTS index (-KeepIndex).' -ForegroundColor Yellow
}
else {
    $indexDirs = @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -Directory -Filter 'FtsIndex' -ErrorAction SilentlyContinue)
    if ($indexDirs.Count -gt 0) {
        foreach ($d in $indexDirs) {
            Write-Host "Deleting FTS index: $($d.FullName)"
            if (-not (Remove-TreeWithRetry -Path $d.FullName)) {
                Write-Warning "Could not fully delete '$($d.FullName)' - a process may still be holding it."
            }
        }
        Write-Host 'FTS index removed.' -ForegroundColor Green
    }
    else {
        Write-Host 'No FTS index folder found.'
    }
}

Write-Host 'Done.' -ForegroundColor Cyan
