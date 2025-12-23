# KleiKodesh Update Script
param([string]$CurrentVersion = "")

$ErrorActionPreference = "SilentlyContinue"
$GitHubApiUrl = "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest"

if (-not $CurrentVersion) {
    # Try to read from registry if not provided
    try {
        $regPath = "HKCU:\Software\Microsoft\Office\Word\Addins\כלי קודש"
        $regValue = Get-ItemProperty -Path $regPath -Name "Version" -ErrorAction SilentlyContinue
        $CurrentVersion = $regValue.Version
    } catch { }
    
    if (-not $CurrentVersion) {
        exit 1
    }
}

# User-Agents for NetFree bypass
$userAgents = @(
    "Microsoft Office/16.0 (Windows NT 10.0; Microsoft Word 16.0.0)",
    "Windows Update Agent", 
    "curl/7.68.0",
    "PowerShell/7.0",
    "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 10.0)",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
)

$remoteVersion = $null
$downloadUrl = $null

foreach ($userAgent in $userAgents) {
    try {
        $webClient = New-Object System.Net.WebClient
        $webClient.Headers.Add("User-Agent", $userAgent)
        $webClient.Encoding = [System.Text.Encoding]::UTF8
        
        $jsonResponse = $webClient.DownloadString($GitHubApiUrl)
        
        if ($jsonResponse -match '"tag_name":\s*"([^"]+)"') {
            $remoteVersion = $matches[1] -replace '^v', ''
        }
        
        if ($jsonResponse -match '"browser_download_url":\s*"([^"]*Setup\.exe[^"]*)"') {
            $downloadUrl = $matches[1]
        }
        
        $webClient.Dispose()
        
        if ($remoteVersion -and $downloadUrl) {
            break
        }
    } catch {
        if ($webClient) { $webClient.Dispose() }
        continue
    }
}

if (-not $remoteVersion) {
    exit 1
}

# Compare versions
try {
    $current = [Version]$CurrentVersion
    $remote = [Version]$remoteVersion
    
    if ($remote -le $current) {
        exit 0  # No update needed
    }
} catch {
    exit 1
}

# Show update notification window
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object System.Windows.Forms.Form
$form.Text = "כלי קודש - עדכון זמין"
$form.Size = New-Object System.Drawing.Size(400, 200)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.RightToLeft = "Yes"
$form.RightToLeftLayout = $true

$label = New-Object System.Windows.Forms.Label
$label.Location = New-Object System.Drawing.Point(20, 20)
$label.Size = New-Object System.Drawing.Size(350, 80)
$label.Text = "נמצא עדכון חדש לכלי קודש`n`nגרסה נוכחית: $CurrentVersion`nגרסה חדשה: $remoteVersion`n`nהאם להוריד ולהתקין את העדכון?"
$label.TextAlign = "MiddleCenter"
$form.Controls.Add($label)

$buttonYes = New-Object System.Windows.Forms.Button
$buttonYes.Location = New-Object System.Drawing.Point(220, 120)
$buttonYes.Size = New-Object System.Drawing.Size(75, 30)
$buttonYes.Text = "כן"
$buttonYes.DialogResult = [System.Windows.Forms.DialogResult]::Yes
$form.Controls.Add($buttonYes)

$buttonNo = New-Object System.Windows.Forms.Button
$buttonNo.Location = New-Object System.Drawing.Point(100, 120)
$buttonNo.Size = New-Object System.Drawing.Size(75, 30)
$buttonNo.Text = "לא"
$buttonNo.DialogResult = [System.Windows.Forms.DialogResult]::No
$form.Controls.Add($buttonNo)

$form.AcceptButton = $buttonYes
$form.CancelButton = $buttonNo

$result = $form.ShowDialog()

if ($result -ne [System.Windows.Forms.DialogResult]::Yes) {
    exit 0
}

if (-not $downloadUrl) {
    [System.Windows.Forms.MessageBox]::Show("שגיאה: לא ניתן למצוא קישור להורדה", "שגיאה", "OK", "Error")
    exit 1
}

$tempFile = Join-Path $env:TEMP "KleiKodesh-Setup-$remoteVersion.exe"

try {
    # Show progress form
    $progressForm = New-Object System.Windows.Forms.Form
    $progressForm.Text = "מוריד עדכון..."
    $progressForm.Size = New-Object System.Drawing.Size(300, 100)
    $progressForm.StartPosition = "CenterScreen"
    $progressForm.FormBorderStyle = "FixedDialog"
    $progressForm.MaximizeBox = $false
    $progressForm.MinimizeBox = $false
    
    $progressLabel = New-Object System.Windows.Forms.Label
    $progressLabel.Location = New-Object System.Drawing.Point(20, 20)
    $progressLabel.Size = New-Object System.Drawing.Size(250, 30)
    $progressLabel.Text = "מוריד עדכון..."
    $progressLabel.TextAlign = "MiddleCenter"
    $progressForm.Controls.Add($progressLabel)
    
    $progressForm.Show()
    $progressForm.Refresh()
    
    $webClient = New-Object System.Net.WebClient
    $webClient.Headers.Add("User-Agent", "Microsoft Office/16.0 (Windows NT 10.0; Microsoft Word 16.0.0)")
    $webClient.DownloadFile($downloadUrl, $tempFile)
    $webClient.Dispose()
    
    $progressForm.Close()
} catch {
    if ($webClient) { $webClient.Dispose() }
    if ($progressForm) { $progressForm.Close() }
    [System.Windows.Forms.MessageBox]::Show("שגיאה בהורדה: $($_.Exception.Message)", "שגיאה", "OK", "Error")
    exit 1
}

# Check if Word is running
$wordProcesses = Get-Process -Name "WINWORD" -ErrorAction SilentlyContinue
if ($wordProcesses) {
    $wordWarning = [System.Windows.Forms.MessageBox]::Show("Microsoft Word פועל כעת!`n`nאנא סגור את Word לפני המשך ההתקנה.", "אזהרה", "OKCancel", "Warning")
    if ($wordWarning -eq "Cancel") {
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        exit 0
    }
}

# Run installer
try {
    $process = Start-Process -FilePath $tempFile -Wait -PassThru
    
    if ($process.ExitCode -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("ההתקנה הושלמה בהצלחה!", "הושלם", "OK", "Information")
    } else {
        [System.Windows.Forms.MessageBox]::Show("ההתקנה עלולה להיכשל", "אזהרה", "OK", "Warning")
    }
} catch {
    [System.Windows.Forms.MessageBox]::Show("שגיאה בהפעלת המתקין: $($_.Exception.Message)", "שגיאה", "OK", "Error")
}

# Clean up
Start-Sleep -Seconds 2
Remove-Item $tempFile -Force -ErrorAction SilentlyContinue