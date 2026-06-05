$ProjectRoot = $PSScriptRoot
$PidFile = "$ProjectRoot\Build\cs-watcher.pid"
$LogFile = "$ProjectRoot\Build\cs-changes.log"

New-Item -ItemType Directory -Path "$ProjectRoot\Build" -Force | Out-Null

# 啟動監控器（若尚未執行）
$needStart = $true
if (Test-Path $PidFile) {
    $oldPid = Get-Content $PidFile -ErrorAction SilentlyContinue
    if ($oldPid -and (Get-Process -Id ([int]$oldPid) -ErrorAction SilentlyContinue)) {
        $needStart = $false
    }
}
if ($needStart) {
    Start-Process -FilePath 'powershell.exe' `
        -ArgumentList "-NoProfile -WindowStyle Hidden -File `"$ProjectRoot\watch-cs-logger.ps1`"" `
        -PassThru | Out-Null
}

# 讀取上次 session 後的 .cs 變更並注入 context
$context = ""
if (Test-Path $LogFile) {
    $lines = Get-Content $LogFile -Tail 50
    $changes = $lines | Where-Object { $_ -match '^\[CHANGED\]' }
    if ($changes.Count -gt 0) {
        $context = "【.cs 變更記錄】上次 session 後有 $($changes.Count) 個 .cs 檔案被修改：`n" + ($changes -join "`n")
    }
}

if ($context) {
    [Console]::Out.WriteLine((@{
        hookSpecificOutput = @{
            hookEventName = "SessionStart"
            additionalContext = $context
        }
    } | ConvertTo-Json -Compress))
}
