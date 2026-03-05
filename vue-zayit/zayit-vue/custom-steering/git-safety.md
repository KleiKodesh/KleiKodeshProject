# Git Safety Rules

## CRITICAL: Never Revert User Work

**NEVER use these commands:**
- `git checkout <file>` - Reverts file to last commit, losing all changes
- `git reset` - Resets repository state
- `git restore` - Restores files from commits
- `git revert` - Creates revert commits

These commands will **DELETE USER WORK** and are **STRICTLY FORBIDDEN**.

## Allowed Git Commands

Only use git for:
- `git status` - Check repository status
- `git diff` - View changes
- `git log` - View history
- `git show` - View specific commits

**If a file has issues, fix it with file editing tools, never revert it.**
