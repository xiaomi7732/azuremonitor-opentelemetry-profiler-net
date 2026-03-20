#!/bin/bash
# Tags the ServiceProfiler submodule with otel-profiler-ref-<short-hash> and pushes the tag.
# Usage: ./tag-submodule.sh [repo-root]
# Defaults to current directory if repo-root is not provided.

set -euo pipefail

REPO_ROOT="${1:-.}"
SUBMODULE_DIR="$REPO_ROOT/ServiceProfiler"

if [ ! -d "$SUBMODULE_DIR/.git" ] && [ ! -f "$SUBMODULE_DIR/.git" ]; then
    echo "Error: ServiceProfiler submodule not found at $SUBMODULE_DIR"
    exit 1
fi

# Check submodule is clean
if [ -n "$(git -C "$SUBMODULE_DIR" status --porcelain)" ]; then
    echo "Error: ServiceProfiler has uncommitted changes. Commit or stash them first."
    exit 1
fi

SHORT_HASH=$(git -C "$SUBMODULE_DIR" rev-parse --short HEAD)
TAG_NAME="otel-profiler-ref-$SHORT_HASH"

echo "Submodule HEAD: $(git -C "$SUBMODULE_DIR" log --oneline -1)"
echo "Tag: $TAG_NAME"

# Check if tag already exists
if git -C "$SUBMODULE_DIR" rev-parse "$TAG_NAME" >/dev/null 2>&1; then
    echo "Tag $TAG_NAME already exists. Skipping."
    exit 0
fi

git -C "$SUBMODULE_DIR" tag "$TAG_NAME"
echo "Tag $TAG_NAME created."

git -C "$SUBMODULE_DIR" push origin "$TAG_NAME"
echo "Tag $TAG_NAME pushed to origin."
