"""Physical direct-playback check. Restores PC ownership in finally.

Requires foreground native build10 and the existing authenticated USB Host.
No system volume, network, signing, jailbreak or firmware change. The private
request file contains a direct HTTPS stream, never a YouTube watch page.
"""
import argparse
import datetime
import json
import pathlib
import time
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=pathlib.Path, required=True)
    parser.add_argument('--request-file', type=pathlib.Path, required=True)
    parser.add_argument('--seconds', type=int, choices=range(10, 61), default=15)
    parser.add_argument('--output', type=pathlib.Path, default=ROOT/'artifacts/native-direct.json')
    args = parser.parse_args()
    config = json.loads(args.config.read_text())
    key = pathlib.Path(config['keyFile'])
    if not key.is_absolute(): key = ROOT/key
    headers = {'Authorization': 'Bearer '+key.read_text().strip().lower(), 'Content-Type': 'application/json'}
    origin = 'http://127.0.0.1:'+str(config['apiPort'])+'/v1/'
    report = dict(timestampUtc=datetime.datetime.now(datetime.UTC).isoformat(), passed=False,
                  physical=False, acousticVerified=False, hardwareDecoderVerified=False,
                  sourceUrlLogged=False, samples=[])

    def request(path, body=None):
        data = None if body is None else json.dumps(body).encode()
        with urllib.request.urlopen(urllib.request.Request(origin+path, data=data, headers=headers), timeout=8) as response:
            result = json.load(response)
        if result.get('error'): raise RuntimeError('Native operation failed')
        return result

    restore = False
    stage = 'connect'
    try:
        status = request('status')
        if status.get('state') != 'connected' or status.get('simulated') or status.get('transport') != 'usb':
            raise RuntimeError('A physical USB session is required')
        report['physical'] = True
        stage = 'check-client'
        media = request('ui', {}).get('media', {})
        report['clientSupportsDirectCheck'] = 'timeAdvanced' in media
        if 'timeAdvanced' not in media or media.get('owner') != 'pc':
            raise RuntimeError('Requires idle phone playback and matching native build10')
        if args.request_file.stat().st_size > 32768: raise ValueError('Oversized input')
        url = json.loads(args.request_file.read_text())['url']
        # Set before sending: a lost response may still have changed ownership.
        restore = True
        stage = 'open'
        request('ui', dict(phoneControl=dict(command='open', url=url)))
        stage = 'warmup'
        deadline = time.monotonic()+20
        while True:
            media = request('ui', {}).get('media', {})
            if media.get('state') == 'failed': raise RuntimeError('Native playback failed')
            if media.get('state') == 'playing' and media.get('timeAdvanced'): break
            if time.monotonic() >= deadline: raise TimeoutError('Playback did not advance')
            time.sleep(.5)
        time.sleep(2)  # Allow the already-queued PC audio stop to complete.
        frames = request('status')['audioFrames']
        stage = 'sample'
        for _ in range(args.seconds):
            media = request('ui', {}).get('media', {})
            report['samples'].append(media)
            if media.get('owner') != 'phone' or media.get('state') in ('failed', 'ended'):
                raise RuntimeError('Native stream stopped')
            if request('status')['audioFrames'] != frames:
                raise RuntimeError('PC PCM is still being transmitted')
            time.sleep(1)
        first, last = report['samples'][0], report['samples'][-1]
        if last['position']-first['position'] < args.seconds*.6: raise RuntimeError('Playback stalled')
        if not last.get('videoReadyForDisplay') or last.get('videoWidth', 0) <= 0 or last.get('networkBytes', 0) <= 0:
            raise RuntimeError('Missing native video/network evidence')
        if last.get('videoCodec') not in ('avc1', 'avc3'): raise RuntimeError('Not the requested H.264 video path')
        report['pcPcmFramesStayedConstant'] = True
        report['passed'] = True
    except Exception as error:
        report['errorType'] = type(error).__name__  # Never include a signed source URL.
        report['failureStage'] = stage
    finally:
        if restore:
            try:
                state = request('ui', dict(phoneControl=dict(command='return-pc')))
                report['pcOwnershipRestored'] = state.get('media', {}).get('owner') == 'pc'
                if not report['pcOwnershipRestored']: report['passed'] = False
            except Exception:
                report['pcOwnershipRestored'] = False
                report['passed'] = False
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2))
    print(json.dumps({k:v for k,v in report.items() if k != 'samples'}))
    return 0 if report['passed'] else 1


if __name__ == '__main__': raise SystemExit(main())
