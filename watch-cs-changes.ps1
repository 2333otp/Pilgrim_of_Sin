# 監控 Assets 資料夾的 .cs 檔案變更，每次有異動就輸出一行通知
param([string]$Path = "$PSScriptRoot\Assets")

$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $Path
$watcher.Filter = "*.cs"
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true

$action = {
    $details = $Event.SourceEventArgs
    $name = $details.Name
    $changeType = $details.ChangeType
    $timestamp = (Get-Date).ToString("HH:mm:ss")
    Write-Output "CS_CHANGED|$timestamp|$changeType|$name"
}

Register-ObjectEvent $watcher "Created" -Action $action | Out-Null
Register-ObjectEvent $watcher "Changed" -Action $action | Out-Null
Register-ObjectEvent $watcher "Deleted" -Action $action | Out-Null
Register-ObjectEvent $watcher "Renamed" -Action $action | Out-Null

Write-Output "WATCHING|$Path"

while ($true) { Start-Sleep -Seconds 1 }
