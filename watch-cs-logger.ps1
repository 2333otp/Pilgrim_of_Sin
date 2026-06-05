# 背景啟動的 .cs 監控器，寫入 log 檔，使用 PID 檔避免重複執行
$PidFile = "$PSScriptRoot\Build\cs-watcher.pid"
$LogFile = "$PSScriptRoot\Build\cs-changes.log"

New-Item -ItemType Directory -Path "$PSScriptRoot\Build" -Force | Out-Null

# 檢查是否已有另一個實例在跑
if (Test-Path $PidFile) {
    $oldPid = Get-Content $PidFile -ErrorAction SilentlyContinue
    if ($oldPid -and (Get-Process -Id $oldPid -ErrorAction SilentlyContinue)) {
        exit 0
    }
}

$PID | Out-File $PidFile -Force

try {
    $watcher = New-Object System.IO.FileSystemWatcher
    $watcher.Path = "$PSScriptRoot\Assets"
    $watcher.Filter = "*.cs"
    $watcher.IncludeSubdirectories = $true
    $watcher.EnableRaisingEvents = $true

    "[START] $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') 監控啟動 PID=$PID" | Add-Content $LogFile

    while ($true) {
        $result = $watcher.WaitForChanged([System.IO.WatcherChangeTypes]::All, 2000)
        if (-not $result.TimedOut) {
            $ts = (Get-Date).ToString("HH:mm:ss")
            "[CHANGED] $ts $($result.ChangeType) $($result.Name)" | Add-Content $LogFile
        }
    }
} finally {
    Remove-Item $PidFile -ErrorAction SilentlyContinue
}
