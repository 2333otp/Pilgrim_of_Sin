param([switch]$Test)

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
$ProjectPath = $PSScriptRoot
$LogFile = "$ProjectPath\Build\unity_output.log"
$BuildLog = "$ProjectPath\Build\cli_build.log"
$TestResult = "$ProjectPath\Build\test_results.xml"

New-Item -ItemType Directory -Path "$ProjectPath\Build" -Force | Out-Null

if ($Test) {
    Write-Host "[TEST] 執行 Unity Test Runner..." -ForegroundColor Cyan
    $method = "CliBuildScript.RunEditModeTests"
    $logTarget = $TestResult
} else {
    Write-Host "[BUILD] 開始 Unity 建置..." -ForegroundColor Cyan
    $method = "CliBuildScript.BuildWindows"
    $logTarget = $BuildLog
}

$args = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", "`"$ProjectPath`"",
    "-executeMethod", $method,
    "-logFile", "`"$LogFile`""
)

$proc = Start-Process -FilePath $UnityExe -ArgumentList $args -Wait -PassThru -NoNewWindow
$exitCode = $proc.ExitCode

if ($Test) {
    if (Test-Path $TestResult) {
        Write-Host "[TEST RESULT]" -ForegroundColor Green
        [xml]$xml = Get-Content $TestResult -ErrorAction SilentlyContinue
        if ($xml) {
            $ts = $xml."test-run"
            Write-Host "  通過: $($ts.passed)  失敗: $($ts.failed)  總計: $($ts.total)"
        }
    } else {
        Write-Host "[TEST] 無測試結果（可能沒有 EditMode 測試）" -ForegroundColor Yellow
    }
} else {
    if (Test-Path $BuildLog) {
        $log = Get-Content $BuildLog -Raw
        if ($exitCode -eq 0) {
            Write-Host "[BUILD SUCCESS]" -ForegroundColor Green
        } else {
            Write-Host "[BUILD FAILED] exitCode=$exitCode" -ForegroundColor Red
        }
        Write-Host $log
    }
    # 掃描 Unity log 的錯誤
    if (Test-Path $LogFile) {
        $errors = Select-String -Path $LogFile -Pattern "^error|Exception|FATAL" -CaseSensitive:$false
        if ($errors) {
            Write-Host "`n[ERRORS FOUND]" -ForegroundColor Red
            $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        }
    }
}

exit $exitCode
