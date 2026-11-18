/* Local UI control only: no network requests, credentials, storage or stream access. */
(function () {
    'use strict';
    if (!navigator.mediaSession || location.hostname !== 'www.youtube.com') return;
    var installed = false;
    function button() {
        if (location.pathname !== '/watch' || document.querySelector('.html5-video-player.ad-showing')) return null;
        var next = document.querySelector('.ytp-next-button');
        if (next && !next.disabled && next.getAttribute('aria-disabled') !== 'true' && next.getClientRects().length) return next;
        var current = new URL(location.href).searchParams.get('v');
        var suggestions = document.querySelectorAll('#related a[href]');
        for (var i = 0; i < Math.min(suggestions.length, 80); i++) {
            var link = suggestions[i];
            if (link.closest('ytd-ad-slot-renderer,ytd-promoted-video-renderer') || !link.getClientRects().length) continue;
            var url;
            try { url = new URL(link.getAttribute('href'), location.href); } catch (_) { continue; }
            if (url.protocol === 'https:' && url.hostname === 'www.youtube.com' && url.pathname === '/watch' && /^[A-Za-z0-9_-]{11}$/.test(url.searchParams.get('v') || '') && url.searchParams.get('v') !== current) return link;
        }
        return null;
    }
    function refresh() {
        try {
            if (button()) {
                navigator.mediaSession.setActionHandler('nexttrack', function () {
                    var next = button();
                    if (next) next.click();
                });
                installed = true;
            } else if (installed) {
                navigator.mediaSession.setActionHandler('nexttrack', null);
                installed = false;
            }
        } catch (_) { /* An unsupported browser keeps its own media behavior. */ }
    }
    document.addEventListener('yt-navigate-finish', refresh);
    document.addEventListener('playing', refresh, true);
    var timer = setInterval(refresh, 3000);
    window.addEventListener('pagehide', function () {
        clearInterval(timer);
        if (installed) navigator.mediaSession.setActionHandler('nexttrack', null);
    });
    refresh();
}());
