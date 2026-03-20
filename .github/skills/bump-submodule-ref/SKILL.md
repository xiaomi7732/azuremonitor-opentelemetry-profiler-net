---
name: bump-submodule-ref
description: >
  Bump the ServiceProfiler submodule reference in the main repo.
  Use this skill when asked to bump, update, or sync the ServiceProfiler
  submodule reference. Handles validation, commit, tagging, and push.
---

# Bump ServiceProfiler Submodule Reference

## Steps

1. **Show current state and confirm with user before proceeding:**
   - Run `git -C ServiceProfiler log --oneline -1` to show the current submodule HEAD.
   - Run `git diff --submodule ServiceProfiler` to show what changed.
   - Ask the user to confirm before proceeding.

2. **Verify ServiceProfiler repo is clean:**
   - Run `git -C ServiceProfiler status --porcelain` in the repo root.
   - If there are uncommitted changes, stop and tell the user to commit or stash them first.

3. **Commit the submodule reference update in the main repo:**
   - Run `git add ServiceProfiler`
   - Run `git commit -m "Bump ServiceProfiler submodule reference"`

4. **Tag the ServiceProfiler repo:**
   - Get the short commit hash: `git -C ServiceProfiler rev-parse --short HEAD`
   - Create the tag: `git -C ServiceProfiler tag otel-profiler-ref-<short-hash>`
   - Example: if commit is `d55da67`, tag is `otel-profiler-ref-d55da67`

5. **Push the tag:**
   - Run `git -C ServiceProfiler push origin otel-profiler-ref-<short-hash>`

## Notes

- Always confirm with the user before committing or pushing.
- The tag format is `otel-profiler-ref-<short-hash>` (not `olte-`).
- The repo root is `C:\AIR\fork-otel-profiler`.
