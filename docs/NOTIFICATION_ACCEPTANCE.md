# Notification Routing & Overlay Acceptance Specification

![Notification Overlays Design Mockup](images/ui-notifications-p2-p3.png)

*Note: Image above is a rendered UI design mockup from `BTTheme` & `BTNotificationOverlayView` Auto Layout specs, not a live physical iPhone capture.*

BackThread delivers rich Windows notifications to the iPhone while guaranteeing that alerts never corrupt or interrupt an active External Picture-in-Picture video stream.

## Priority Tiers & Filtering (`EventRouter`)

Implemented in [`EventRouter`](../src/Windows/Events/EventRouter.cs) and [`CompanionEvent`](../src/Windows/Events/EventRouter.cs):

| Priority | Presentation on iOS | Windows Routing Behavior | Auto-Dismiss Window |
|---|---|---|---|
| **0 (Low / Quiet)** | Stored in `BTHistoryPanelView` only (`_events`); no visual popup | Captured during normal or quiet hours | N/A |
| **1 (Notice)** | Compact top-center blue toast pill (`NOTICE`) without body/icon | Rate-limited (max 10/s), deduplicated by key within window | `min(3s, remainingTTL)` |
| **2 (High / Banner)** | Amber top banner card with optional `<=24 KiB` Base64 app icon, title, body, swipe-up / Dismiss button | Promoted automatically if `source` matches `HighNotificationSources` | `min(8s, remainingTTL)` |
| **3 (Critical / Fullscreen)** | Red-bordered fullscreen centered modal card over translucent dark backdrop (`0.90` alpha) + single-tap `"Dismiss & Return to Media"` | Promoted automatically if `source` matches `CriticalNotificationSources`; optional `PauseMediaOnCriticalNotification` | Manual explicit dismissal (fullscreen locked) |

### Privacy, Suppression & Bounded Memory
- **Privacy Masking (`hideSensitive: true`)**: Replaces `title` with `"Private notification"`, clears `body`, clears `iconBase64`, and clears action buttons before sending over USB Channel 4 ([`EventRouter.Evaluate`](../src/Windows/Events/EventRouter.cs)).
- **Blocked Sources (`blockedSources`)**: Case-insensitive suppression applied before history retention.
- **Quiet Hours & Focus Suppression**: Quiet-hours/fullscreen Windows modes suppress Priority 1-2 interruptions (logged/retained at quiet Priority `0` or dropped safely) while honoring Priority 3 critical alerts unless explicitly disabled.
- **Bounded History**: Both `EventRouter` (Windows) and `BTViewController._events` (iOS) retain at most 64 unexpired notifications, pruning expired entries automatically.

## Non-Destructive External PiP Guarantee (`BTNotificationOverlayView`)

Verified in [`BTViewController.m`](../src/iOS/UI/BTViewController.m) and [`BTNotificationOverlayView.m`](../src/iOS/UI/BTNotificationOverlayView.m):
1. `BTNotificationOverlayView` is added on top of both `BTVideoSurfaceView` (PiP canvas) and `_embedHostView` (`[self.view bringSubviewToFront:_notificationOverlay]`).
2. Priority 2 and Priority 3 alerts NEVER pause, stop, swap out, or detach `AVPlayer` / `_directPlayer.playerLayer` on iOS.
3. Priority <=2 overrides `-pointInside:withEvent:` so touches outside the banner pass seamlessly through to `BTVideoSurfaceView` video controls beneath.
4. Priority 3 engages `_fullscreenActive = YES`, un-hiding status bar gently for situational awareness and requiring a single tap (*Dismiss & Return to Media* or swipe-up) which restores `BTVideoSurfaceView` instantaneously at exact live timestamp.
5. `BTHistoryPanelView` lets the user inspect any active notification at fullscreen (`Inspect Fullscreen`) at any time.

## Windows Ingestion Boundary & Acceptance Ledger

- **Authenticated Loopback Event API (`POST /v1/events`) + SSE / History (`GET /v1/events`)**: **Verified** in [`HybridMediaTests`](../tests/BackThread.Tests/HybridMediaTests.cs) (`events.rich-priorities-and-privacy-masking`) and portable smoke tests.
- **Official Windows Toast Listener (`WindowsNotificationCollector` + `packaging/msix/Package.appxmanifest`)**: **Compiled & Manifest-Staged Only**. Windows `UserNotificationListener` requires MSIX/Sparse package identity (`userNotificationListener` capability) and interactive Windows 10 consent UI (`POST /v1/notifications/request-access`); unpackaged builds report `"Unavailable"` cleanly without polling errors. Physical Windows 10 toast capture remains unexecuted on Linux.
