$ErrorActionPreference = "Continue"
$tasks = Get-Content "tasks.txt" | Where-Object { $_.Trim() -ne "" }
$timeoutSec = 1800  # 30分
$logFile = "claude_run.log"

# 空の stdin 用ファイルを作成
$emptyInput = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($emptyInput, "")

$prompt = @'
Execute only this single task ({0}) according to the instructions in work_procedure.md. Before starting, output the task name ({0}). After completing the task, run UnityTestRunner to verify the result. If any file changes exist, run git add -A and then commit with the message "task {0}". Finally, output only OK or FAIL. tasks.txt is a user-managed file and must not be modified. After outputting OK or FAIL, complete the session without waiting for user input.
'@

foreach ($t in $tasks) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "=== START $t ($timestamp) ==="
    "=== START $t ($timestamp) ===" | Add-Content $logFile

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "claude"
    $psi.Arguments = "-p `"$($prompt -f $t)`" --max-turns 60 --verbose"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi

    # 非同期で stdout/stderr を収集（デッドロック防止）
    $outBuilder = New-Object System.Text.StringBuilder
    $errBuilder = New-Object System.Text.StringBuilder

    $outEvent = Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived -Action {
        if ($null -ne $EventArgs.Data) {
            $Event.MessageData.AppendLine($EventArgs.Data) | Out-Null
        }
    } -MessageData $outBuilder

    $errEvent = Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived -Action {
        if ($null -ne $EventArgs.Data) {
            $Event.MessageData.AppendLine($EventArgs.Data) | Out-Null
        }
    } -MessageData $errBuilder

    try {
        $proc.Start() | Out-Null
        $proc.StandardInput.Close()  # stdin を即閉じ（<nul と同等）
        $proc.BeginOutputReadLine()
        $proc.BeginErrorReadLine()

        $exited = $proc.WaitForExit($timeoutSec * 1000)

        if (-not $exited) {
            $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            Write-Host "=== TIMEOUT $t ($timestamp) ==="
            "=== TIMEOUT $t ($timestamp) ===" | Add-Content $logFile
            try { $proc.Kill() } catch {}
            $proc.WaitForExit(10000)  # Kill 後の後処理待ち
        }
    }
    finally {
        Unregister-Event -SourceIdentifier $outEvent.Name -ErrorAction SilentlyContinue
        Unregister-Event -SourceIdentifier $errEvent.Name -ErrorAction SilentlyContinue
        Remove-Job -Name $outEvent.Name -Force -ErrorAction SilentlyContinue
        Remove-Job -Name $errEvent.Name -Force -ErrorAction SilentlyContinue
    }

    # ログ書き出し
    $outBuilder.ToString() | Add-Content $logFile
    if ($errBuilder.Length -gt 0) {
        "[STDERR]" | Add-Content $logFile
        $errBuilder.ToString() | Add-Content $logFile
    }

    $exitCode = $proc.ExitCode
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "=== END $t (exit: $exitCode, $timestamp) ==="
    "=== END $t (exit: $exitCode, $timestamp) ===" | Add-Content $logFile

    $proc.Dispose()

    Write-Host "Waiting 180s before next task..."
    Start-Sleep -Seconds 180
}

Remove-Item $emptyInput -ErrorAction SilentlyContinue
Write-Host "All tasks completed."
