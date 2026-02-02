@echo off
for /f "usebackq delims=" %%T in ("tasks.txt") do @(
  echo === START %%T ===
  claude -p "Execute only this single task (%%T) according to the instructions in work_procedure.md. Before starting, output the task name (%%T). After completing the task, run UnityTestRunner to verify the result. If any file changes exist, run git add -A and then commit with the message \"task %%T\". Finally, output only OK or FAIL. tasks.txt is a user-managed file and must not be modified. After outputting OK or FAIL, complete the session without waiting for user input." --allowedTools "Read,Write,Edit,Bash(git add:*),Bash(git commit:*),Bash(Unity.exe*),Bash(*-runTests*)"  --max-turns 120 --verbose 2>&1
  echo === END %%T (waiting 180s) ===
  timeout /t 180 /nobreak >nul
) >> claude_run.log 2>&1
