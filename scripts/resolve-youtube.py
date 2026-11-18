"""Resolve metadata only; never download, proxy, merge or transcode video.

Optional yt-dlp/EJS installation and a supported local JS runtime are required.
This resolver runs on the machine executing this script. A Windows run is NOT
an autonomous phone resolver. No browser cookies, accounts, DRM or PO-token
plugins are imported. Only public, single H.264/AAC streams are selected.
"""
import argparse
import json
import pathlib
import re
import sys
import urllib.parse

ROOT = pathlib.Path(__file__).resolve().parents[1]


def video_url(text):
    p = urllib.parse.urlsplit(text)
    if p.scheme != 'https' or p.username is not None or p.password is not None or p.port not in (None, 443):
        raise ValueError('A public HTTPS YouTube video link is required')
    host = (p.hostname or '').lower()
    identifier = None
    if host == 'youtu.be' and p.path.count('/') == 1:
        identifier = p.path[1:]
    elif host in ('youtube.com', 'www.youtube.com', 'm.youtube.com'):
        if p.path == '/watch':
            values = urllib.parse.parse_qs(p.query).get('v', [])
            if len(values) == 1:
                identifier = values[0]
        elif re.fullmatch(r'/(?:shorts|live|embed)/[A-Za-z0-9_-]{11}', p.path):
            identifier = p.path.rsplit('/', 1)[1]
    if not identifier or not re.fullmatch(r'[A-Za-z0-9_-]{11}', identifier):
        raise ValueError('A single YouTube video is required')
    return 'https://www.youtube.com/watch?v='+identifier


def select_stream(info, maximum_height=480):
    if info.get('has_drm') or info.get('_type') in ('playlist', 'multi_video'):
        raise ValueError('This media is not supported')
    candidates = []
    for f in info.get('formats', []):
        p = urllib.parse.urlsplit(f.get('url', ''))
        host = (p.hostname or '').lower()
        if (p.scheme != 'https' or p.username is not None or p.password is not None or p.port not in (None, 443)
                or not host.endswith('.googlevideo.com') or len(host) <= len('.googlevideo.com')
                or len(f.get('url', '')) > 12000 or f.get('has_drm')
                or not (p.path == '/videoplayback' or p.path.endswith('.m3u8'))):
            continue
        video = (f.get('vcodec') or '').lower()
        audio = (f.get('acodec') or '').lower()
        height, fps = f.get('height') or 0, f.get('fps') or 0
        if not video.startswith(('avc1', 'h264')) or not audio.startswith(('mp4a', 'aac')):
            continue  # Separate audio/video cannot be silently proxied or merged.
        if not 0 < height <= maximum_height or fps > 30 or (f.get('tbr') or 0) > 2500:
            continue
        candidates.append(f)
    if not candidates:
        raise ValueError('No single H.264/AAC stream at this quality; no PCM/proxy fallback was started')
    selected = max(candidates, key=lambda f: (f.get('height') or 0, f.get('tbr') or 0))
    return dict(url=selected['url'], codec=selected['vcodec'], audioCodec=selected['acodec'],
                height=selected['height'], fps=selected.get('fps'), isLive=bool(info.get('is_live')),
                resolverLocation=sys.platform, videoRelay=False, transcoding=False,
                resolver='yt-dlp', requiresPhysicalValidation=True)


class QuietLogger:
    def debug(self, message): pass
    def warning(self, message): pass
    def error(self, message): pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--url', required=True)
    parser.add_argument('--node', type=pathlib.Path, required=True)
    parser.add_argument('--packages', type=pathlib.Path, help='Isolated optional dependency directory')
    parser.add_argument('--height', type=int, choices=(360, 480, 720), default=480)
    parser.add_argument('--output', required=True, type=pathlib.Path, help='Private signed-URL JSON under artifacts/')
    args = parser.parse_args()
    url = video_url(args.url)
    if not args.node.is_file():
        raise ValueError('An existing supported JS runtime is required')
    output = args.output.resolve()
    if not output.is_relative_to(ROOT/'artifacts') or output == ROOT/'artifacts':
        raise ValueError('Private resolver output must stay under artifacts/')
    if args.packages:
        sys.path.insert(0, str(args.packages.resolve()))
    import yt_dlp
    # API construction does not load the user's CLI configuration or browser data.
    options = dict(skip_download=True, noplaylist=True, quiet=True, no_warnings=True,
                   logger=QuietLogger(), cachedir=False, usenetrc=False, cookiefile=None,
                   socket_timeout=10, retries=1, extractor_retries=1,
                   js_runtimes={'node': {'path': str(args.node.resolve())}})
    with yt_dlp.YoutubeDL(options) as ydl:
        info = ydl.extract_info(url, download=False, process=False)
    result = select_stream(info, args.height)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result), encoding='utf-8')
    output.chmod(0o600)
    print(json.dumps({k:v for k,v in result.items() if k != 'url'}))
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except Exception:
        # Extractor errors may carry signed URLs or account-related responses.
        print(json.dumps(dict(success=False, error='Public YouTube metadata resolution failed or no compatible stream is available. No playback, account import, relay or system change was attempted.')))
        raise SystemExit(1)
