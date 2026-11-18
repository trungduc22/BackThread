# YouTube Next helper

Thorium reported CanNext=false; Windows accepted TrySkipNextAsync but the video did not change in the four-second probe. This optional extension registers the standard nexttrack Media Session action and invokes YouTube's visible Next button, or the first visible ordinary recommended-video link when the player omits that button. All fallback URLs are restricted to a different HTTPS youtube.com/watch video; promoted/ad entries are excluded. It does not extract streams, skip ads, contact servers, use native messaging or read other sites. It leaves play/pause/seek unchanged. The content script is limited to https://www.youtube.com and needs explicit user approval before loading.

After approval, load this directory unpacked on the Chromium extensions page and reload the YouTube watch tab. No Windows service or certificate is needed for this helper. It requires Chromium111+ and a visible/enabled YouTube Next button or a normal recommendation. During advertisements or outside watch pages it removes its handler. The browser/YouTube can change behavior; end-to-end Next acceptance requires a real loaded-extension test.

Tests: node tests/youtube-next.test.cjs. Node fixtures passed. User approved/loaded the extension in Thorium; CUA observed it enabled, Windows Next changed the video, and the user confirmed Next from the physical iPhone. See docs/reports/build9-youtube-next-physical.json.

References: https://developer.chrome.com/blog/media-session and https://developer.chrome.com/docs/extensions/reference/manifest/content-scripts
