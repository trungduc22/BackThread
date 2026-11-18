#!/usr/bin/env bash
set -euo pipefail
: "${THEOS:?Set THEOS to an existing, authorized Theos installation with an iOS SDK and arm64 toolchain}"
root="$(cd "$(dirname "$0")/.." && pwd)"
command -v make >/dev/null
test -d "$THEOS/sdks"
if [[ -n "${THEOS_PACKAGE_SCHEME:-}" ]]; then
    echo 'This iOS 12 build requires the existing rootful package scheme (unset THEOS_PACKAGE_SCHEME).' >&2
    exit 1
fi
make -C "$root/src/iOS" clean package FINALPACKAGE=1
make -C "$root/src/iOS/Agent" clean package FINALPACKAGE=1
# Deliberately no automatic install, jailbreak changes, or launchd edits.
