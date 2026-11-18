# Build10 — direct media / scoped SSH control / companion direction

Build10 continues local commit 5d36a98. Native version 0.2.0(10), ARM64, minimum iOS12.0, SDK12.4. The installed Windows package remains Host9; this checkpoint does not change certificates, Windows trust, source apps or iPhone system components.

## Implemented source

- Monochrome blurred battery badge, white translucent level/outline and shadow/text contrast. Reduce Transparency uses an opaque dark backing. Clock/location/route HUD otherwise retained.
- Ready native indefinite-duration streams show LIVE. Explicit PC isLive is supported; Windows' current media API does not provide a reliable universal live flag, so unknown PC duration displays —:—. It must not be presented as verified live detection for every desktop source.
- Left/right swipe on PC artwork invokes next/previous when that action is enabled. Page navigation excludes artwork so it cannot steal that gesture. Native single-video playback has no invented next queue.
- Extensionless signed HTTPS googlevideo subdomain /videoplayback URLs are accepted alongside direct media files/HLS. Credential-bearing URLs/nondefault ports/oversized URLs are rejected. No YouTube watch-page parser was added to AVPlayer.
- Native playing acknowledgement waits for time advancement; snapshot adds video display readiness, codec, dimensions, access-log bytes, bitrate, stalls/dropped frames, app resident memory, and OS thermal-state enum. These are diagnostics, not acoustic or hardware-decoder proof. AVPlayer adaptive preferences: 1.5Mbps and up to 854×480; these are hints, not guarantees for fixed progressive files.
- Existing PCM ownership handover retained; entering native mode also clears acceptingPCM. Native failure text omits the signed URL. Returning to PC clears stale native state.
- App-private Library/BackThread/Control inbox accepts open/status/play/pause/seek/return-pc only, foreground only. No new listener or daemon. Requests are bounded regular non-symlink files, consumed before dispatch; responses contain no media URL. SSH utility writes only this app-owned directory with mobile ownership/private permissions and preserves existing strict host-key trust. A single outstanding request is allowed.

## Optional YouTube metadata resolver

scripts/resolve-youtube.py uses separately installed, pinned yt-dlp/EJS and an existing Node22+ runtime. It processes metadata only; it does not download/proxy/merge/transcode video. No account/cookie import or PO-token plugin is configured. It selects a single public H.264/AAC stream, default cap480p/30fps/2.5Mbps, rather than silently falling back to separate video/audio or PCM.

A real metadata-only run for Google's public IFrame sample resolved H.264 avc1.42001E + AAC mp4a.40.2, 360p30. **That run was on Windows.** The signed URL is held only in ignored private artifacts. Native YouTube playback and a phone-local resolver remain unverified/unimplemented respectively. Google can expire/bind/reject these URLs; different PC/phone public IPs, headers, rate limits and source restrictions can prevent playback. An extractor result is not device acceptance.

Native ownership stops Host PCM; it does not automatically pause or close an independently playing PC browser video. CPU offload must be measured with the original PC source no longer decoding. A future phone-only handoff needs an acknowledged native start followed by a scoped PC pause; dual playback must remain a separate user choice. No CPU/energy reduction is claimed here.

Install optional dependencies to an isolated directory with tools/youtube-resolver-requirements.txt (hashes pinned). Do not install a Python/JS runtime into the fragile phone system to make this work. No provider credentials belong in chat, argv, Git or reports.

## Use after installing the matching IPA

The user previously chose to install IPA using their own method. Normal Installation Proxy remains a recorded blocker; build10 has not been installed automatically. Keep BackThread foreground.

1. Resolve a public video using scripts/resolve-youtube.py, supplying an existing Node executable, isolated packages and an output file under artifacts/ios-private. stdout contains metadata only.
2. For the existing authenticated USB Host, scripts/test-native-direct.py --config artifacts/ios-private/ui2-control-host.json --request-file artifacts/ios-private/youtube-stream.json tests15seconds of real native video by default. It refuses an old client or active native session, checks advancement/display/H.264/network activity and constant PC PCM frame count, then restores PC ownership in finally. The test does not prove audible output or hardware decoding. A PC ownership restoration does not itself prove that acoustic PC audio resumed; confirm status and listening separately.
3. SSH control is independent of the Host API. Start only the existing bounded USB forward to device SSH44 using scripts/usb-port-forward.py. Use scripts/phone-control.py with the exact current BackThread data-container UUID, a previously verified known-host record, its matching alias, and optional existing key. Keyless use prompts in the user's terminal; no password argument exists. For signed URLs use open --request-file, not --url. No automatic trust enrollment or app-container guessing occurs.
4. An open response says accepted, not playing. Poll status and check position/timeAdvanced/videoReadyForDisplay/networkBytes. return-pc releases native ownership. If the app is suspended the foreground inbox will not respond; do not repair system services or repeatedly submit opens.

## Verification and remaining work

See docs/reports/build10-* for the completed checks. Build/analysis/fixture results are separate from device results. During this turn the existing physical client was connected, and a read-only USB44 banner check found Dropbear. SSH authentication/inbox execution and build10 visual/native playback are not yet verified. The working installed Host9 was not replaced or stopped. Existing simulator audio-soak failure remains open.

See COMPANION_WORKSPACE.md for the two-region Media/Build/SSH/Mail design and concrete next acceptance gates. Build log/terminal UI, direct mail/OTP collection and personal Messenger/Zalo collection are not implemented in this build.
