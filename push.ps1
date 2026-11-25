# Pushes this checkout's commits to GitHub's master - always, without touching anyone's files.
#
# Several sessions work at once: some push from their own copies (KleiKodeshProject-work), so
# GitHub's master moves ahead of this folder, and catching up is refused while another session
# has uncommitted edits in a file those new commits change. This script never needs that:
#   1. fetch;
#   2. when this folder can catch up (git merge refuses nothing), merge and push - the usual way;
#   3. otherwise put only this folder's commits that GitHub lacks (git cherry) on top of GitHub's
#      master in a temporary copy, push from there, and delete the copy.
# It never stashes, never resets, and never changes files in this folder in step 3. A push that
# loses a race with another session's push is retried.
#
# Usage (from anywhere inside the repo):  powershell -File push.ps1
$ErrorActionPreference = 'Stop'
$repo = (git rev-parse --show-toplevel).Trim()
Set-Location $repo

function Invoke-Git([string[]] $GitArgs) {
    $out = & git @GitArgs 2>&1
    return [pscustomobject]@{ Ok = ($LASTEXITCODE -eq 0); Text = ($out | Out-String).Trim() }
}

for ($attempt = 1; $attempt -le 3; $attempt++) {
    git fetch -q origin
    $missing = @(git cherry origin/master HEAD | Where-Object { $_ -like '+ *' } | ForEach-Object { $_.Substring(2) })
    if ($missing.Count -eq 0) { Write-Output 'push: nothing to push - GitHub has every commit of this folder.'; exit 0 }

    # 2. The usual way, when catching up touches nobody's uncommitted file.
    $behind = [int](git rev-list --count HEAD..origin/master)
    $merged = $true
    if ($behind -gt 0) {
        $m = Invoke-Git @('merge', '-q', '--no-edit', 'origin/master')
        if (-not $m.Ok) {
            $merged = $false
            # A merge that started and stopped on a conflict is undone; one refused up front left nothing.
            if (Test-Path (Join-Path (git rev-parse --git-dir) 'MERGE_HEAD')) { git merge --abort }
            Write-Output "push: this folder cannot catch up now (another session's files) - pushing through a temporary copy."
        }
    }
    if ($merged) {
        $p = Invoke-Git @('push', '-q', 'origin', 'HEAD:master')
        if ($p.Ok) { Write-Output "push: pushed $($missing.Count) commit(s)."; exit 0 }
        continue # GitHub moved again meanwhile
    }

    # 3. Only this folder's commits GitHub lacks, on top of GitHub's master, in a temporary copy.
    $work = Join-Path (Split-Path $repo -Parent) ("KleiKodeshProject-work\push-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    git worktree add -q --detach $work origin/master
    try {
        Push-Location $work
        $ok = $true
        foreach ($commit in $missing) {
            $c = Invoke-Git @('cherry-pick', '--allow-empty', $commit)
            if (-not $c.Ok) {
                git cherry-pick --abort 2>$null
                Write-Output "push: commit $commit does not apply on GitHub's master (it conflicts with newer work): $($c.Text)"
                $ok = $false
                break
            }
        }
        if (-not $ok) { exit 1 }
        $p = Invoke-Git @('push', '-q', 'origin', 'HEAD:master')
        if ($p.Ok) { Write-Output "push: pushed $($missing.Count) commit(s) through a temporary copy; this folder catches up on its next pull."; exit 0 }
    }
    finally {
        Pop-Location
        git worktree remove --force $work
    }
}
Write-Output 'push: GitHub kept moving during three tries - run it again.'
exit 1
