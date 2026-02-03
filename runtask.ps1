$tasks = Get-Content "tasks.txt" | Where-Object { $_.Trim() -ne "" }
  $timeoutSec = 1800  # 30•ª

  foreach ($t in $tasks) {
      Write-Host "=== START $t ==="
      $proc = Start-Process -FilePath "claude" -ArgumentList @(
          "-p", "Execute only this single task ($t) according to ...",
          "--max-turns", "60", "--verbose"
      ) -NoNewWindow -RedirectStandardInput "NUL" `
        -RedirectStandardOutput "claude_task_out.tmp" `
        -RedirectStandardError "claude_task_err.tmp" `
        -PassThru

      if (-not $proc.WaitForExit($timeoutSec * 1000)) {
          Write-Host "=== TIMEOUT $t ==="
          $proc.Kill()
      }

      Get-Content "claude_task_out.tmp", "claude_task_err.tmp" |
          Add-Content "claude_run.log"
      Remove-Item "claude_task_out.tmp", "claude_task_err.tmp" -ErrorAction SilentlyContinue

      Write-Host "=== END $t (exit: $($proc.ExitCode), waiting 180s) ==="
      Start-Sleep -Seconds 180
  }