#!/usr/bin/env bash
set -euo pipefail
: "${THEOS:?Set THEOS to an existing, authorized Theos installation with an iOS SDK and arm64 toolchain}"
cd "$(dirname "$0")/../src/iOS"
command -v make >/dev/null
test -d "$THEOS/sdks"
make clean package FINALPACKAGE=1
# Deliberately no automatic install, jailbreak changes, or launchd edits.
