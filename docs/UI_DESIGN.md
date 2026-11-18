# Native UI 2.0 design and implementation plan

Reference review: 2026-11-18. Starting implementation: 6b986ce; remote branches/tags checked before editing. This document describes intent; acceptance is recorded separately.

## References and original direction

- [Spotify TV redesign, 2023](https://newsroom.spotify.com/2023-11-09/find-your-favorite-audio-more-easily-with-the-redesigned-spotify-on-tv-experience/): adapt artwork hierarchy, quiet metadata and optional visual restraint to a nearby companion display. Do not copy its green branding, assets, spacing or exact layout. Newer [Spotify video controls, April 2026](https://newsroom.spotify.com/2026-04-09/video-control-settings-update/) reinforce explicit user choice between video and audio; they are not evidence of iOS 12 service compatibility.
- [Apple playing video](https://developer.apple.com/design/human-interface-guidelines/playing-video): the picture keeps its space; transient controls and dismissible overlays preserve continuity. AVPlayer survives navigation.
- [Apple gestures](https://developer.apple.com/design/human-interface-guidelines/gestures) and [accessibility](https://developer.apple.com/design/human-interface-guidelines/accessibility): familiar single-finger gestures, a visible/tappable alternative, labeled controls, comfortable targets and reduced-motion support. On Media, paging begins at an edge and never begins on a control or seek region.
- Native compact-remote conventions: visually small previous/play/next marks in 44-point buttons; capability and acknowledgement determine state. Contextual edge drawers keep navigation out of the primary image.

BackThread uses deep charcoal, warm white, restrained aqua and an artwork-derived dark tint. Original vector paths have no icon-font or SF Symbols dependency. At 667×375 points the artwork is a single strong shape, the title has two readable lines, transport icons stay compact and the corner grip retains a 44-point touch target. No live blur, continuous decorative animation or per-second artwork processing.

## Components and sequence

1. Theme/vector icons, reusable tile/row and compact controls; separate media, navigation and event state.
2. Retained lightweight page container (Media, Apps, Actions, Worker, History, Connection/Settings), edge-aware paging and interruptible corner drawer.
3. Native AVPlayer surface for supported HTTPS media, explicit ownership/result state, fit/fill, buffering/error feedback and auto-hidden controls. Windows capabilities remain authoritative for PC playback.
4. Bounded notification coordinator, priority-specific presentation, source/category fullscreen policy, privacy/expiry/deduplication and restoration without replacing the player.
5. Locally pinned Windows catalog, genuine small icons, authenticated ID-only launch with fixed trusted arguments and result feedback. No automatic installed-app upload.
6. Optional temporary TLS connection enrolled through authenticated USB, explicit bind address, pinned certificate, expiry, one session owner and USB preference. Wi-Fi PCM remains off. Manual IP entry is available without mDNS; network isolation remains a possible blocker.
7. Managed/native checks, own-app installation, real native snapshots where available, layout/gesture review, regression, RSS and release manifests. Keep unavailable/compiled-only and physically verified results distinct.

## Resource limits

At most 32 pinned apps, bounded 64-pixel icons, one processed artwork, retained lightweight pages, one AVPlayer and bounded notification/history queues. Reuse collection cells and discard decoded caches under memory pressure. All UIKit entry points are on the main thread; stale requests/animations carry generations. No firmware, system, jailbreak or security-policy changes. Stable checkpoints remain local for this mission.
