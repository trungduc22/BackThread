# Current policy: top bars only

Build7 removes fullscreen presentation for all priorities, including stored source/category overrides. The compact card shows source/title/body and a44pt dismiss target. It does not pause media or make navigation modal. Shared decision tests and updated physical-inspection assertions cover the new policy; hardware rendering has not been rerun. OS Windows notification collection remains a separate unresolved identity/permission requirement.

## Historical build5 notes

# Notification acceptance at the handoff

User now requests top bars only, including critical events. Build5 still has compact and critical fullscreen presentations; redesign is pending. Real native fixtures verified both appearances, dedup and retained-player restoration. Queue8/history64, expiry/privacy/source policy are implemented, with broader burst/touch acceptance pending. Portable Windows reports missing package identity/userNotificationListener capability: actual Windows toast copying has not passed. The authenticated events fixture is separate from OS notification permission and collection. No system-wide iOS overlay is promised.
