# BackThread iOS Build Profiles

BackThread maintains **one shared Objective-C / C11 iOS codebase** under `src/iOS/` and `src/Shared/Protocol/`. Build-time feature flags and `@available(...)` runtime checks select deployment compatibility and optional packaging layouts without duplicating transport or UI code.

## Supported Profiles

| Profile | Min iOS | Compile Flags | Package Layout | Notes |
| --- | --- | --- | --- | --- |
| **Stock iOS 12+ (`ios12-universal`)** | `12.0` | `-miphoneos-version-min=12.0 -DBT_PROFILE_IOS12=1 -DBT_ENABLE_JAILBREAK_EXTENSIONS=0 -Wl,-no_fixup_chains` | `BackThread-iOS12.ipa` | Native UIKit app, zero jailbreak dependencies, pinned Wi-Fi TLS (`Network.framework`), `nw_path_monitor_t`, `AVRoutePickerView`, standard iOS sandbox and background limits. |
| **Enhanced iOS 15+ (`ios15`)** | `15.0` | `-miphoneos-version-min=15.0 -DBT_PROFILE_IOS15=1 -DBT_ENABLE_JAILBREAK_EXTENSIONS=0` | `BackThread-iOS15.ipa` | Enables modern iOS 13+/15+ APIs (Bonjour `nw_browser_t`, Vision/PDF/Speech weak frameworks, Neural Engine offload tier) while preserving the shared codebase. |
| **Jailbreak Rootless** | `15.0` | Shared binary + optional `-DBT_ENABLE_JAILBREAK_EXTENSIONS=1` | `BackThread-iOS15-rootless.deb` (`/var/jb/Applications/BackThread.app`) | Packaged with `ios_build_support.debian_rootless`; elevated/SSH integrations require explicit user installation and authorization and never bundle private SSH keys. |
| **Jailbreak Rootful** | `12.0` | Shared binary + optional `-DBT_ENABLE_JAILBREAK_EXTENSIONS=1` | `BackThread-iOS12-rootful.deb` (`/Applications/BackThread.app`) | Packaged with `ios_build_support.debian`; shares 100% of transport and UI implementation with the stock iOS 12 profile. |

## Verification Rules

- **Separate CI enforcement:** `.github/workflows/ci.yml` (`macos-ios` job) compiles, links, validates Mach-O load commands (`LC_BUILD_VERSION` / legacy dyld info vs chained fixups), and packages **both** `ios12-universal` and `ios15` on every commit with `-Wall -Wextra -Werror -Werror=unguarded-availability-new`.
- **Hardware testing:** CI verifies compilation, linking, Mach-O headers, and archive structure only. Physical device testing on iPhone 6 (iOS 12.5.8) and iOS 15+ hardware must be explicitly recorded separately when run.

## Stock IPA Signing & Distribution Limitations

- CI produces unsigned / ad-hoc-signed `.ipa` archives (`BackThread-iOS12.ipa`, `BackThread-iOS15.ipa`).
- Installing a stock `.ipa` onto a non-jailbroken iPhone requires signing with an Apple Developer certificate or personal free provisioning profile (e.g., Xcode, AltStore, Sideloadly):
  - Free Apple ID profiles expire after 7 days and allow up to 3 active sideloaded apps.
  - Paid Apple Developer profiles allow 1-year signing or TestFlight distribution.
- Background execution on stock iOS is constrained by iOS sandbox lifecycle rules (active audio session or foreground wake lock is required for sustained companion sessions; no background system daemon is installed).
- BackThread never modifies iOS system security configurations and never bundles private SSH credentials.
