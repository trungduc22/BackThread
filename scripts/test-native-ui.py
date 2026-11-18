"""Authenticated inspection of foreground BackThread; never a simulator visual claim.

Requires a running Host configured for this phone. Changes only BackThread UI and
fixture notifications. No audio playback, Wi-Fi enrollment or application launch.
"""
import argparse
import datetime
import hashlib
import json
import pathlib
import time
import urllib.request
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True, type=pathlib.Path)
    parser.add_argument('--output', type=pathlib.Path, default=ROOT/'artifacts/native-ui')
    parser.add_argument('--wait-seconds', type=int, default=0, choices=range(0, 121))
    args = parser.parse_args()
    config = json.loads(args.config.read_text())
    key_path = pathlib.Path(config['keyFile'])
    if not key_path.is_absolute():
        key_path = ROOT/key_path
    headers = {'Authorization': 'Bearer '+key_path.read_text().strip().lower(),
               'Content-Type': 'application/json'}
    origin = 'http://127.0.0.1:'+str(config['apiPort'])+'/v1/'
    args.output.mkdir(parents=True, exist_ok=True)
    report = dict(timestampUtc=datetime.datetime.now(datetime.UTC).isoformat(),
                  physicalIPhone=False, screenshots=[], checks=[], passed=False,
                  gestureTouchTested=False, audiblePlaybackTested=False)
    previous = None

    def request(path, body=None):
        data = None if body is None else json.dumps(body).encode()
        with urllib.request.urlopen(urllib.request.Request(origin+path, data=data, headers=headers), timeout=20) as response:
            raw = response.read()
            return json.loads(raw) if response.headers.get_content_type() == 'application/json' else raw

    def inspect(body):
        state = request('ui', body)
        if 'error' in state:
            raise RuntimeError(state['error'])
        return state

    def check(name, condition):
        report['checks'].append(dict(name=name, passed=bool(condition)))
        if not condition:
            raise AssertionError(name)

    def capture(name, body):
        state = inspect(dict(body, capture=True))
        snapshot = state.get('snapshot', {})
        size = snapshot.get('bytes', 0)
        check(name+' snapshot bounded', 0 < size <= 2*1024*1024)
        data = bytearray()
        while len(data) < size:
            chunk = request('artifacts/ui-snapshot.png?offset='+str(len(data))+'&count='+str(min(32768, size-len(data))))
            if not chunk:
                raise RuntimeError('Incomplete native snapshot')
            data.extend(chunk)
        check(name+' snapshot SHA-256', hashlib.sha256(data).hexdigest() == snapshot['sha256'])
        path = args.output/(name+'.png')
        path.write_bytes(data)
        report['screenshots'].append(dict(name=name, file=path.name, state=state))
        return state

    try:
        deadline = time.monotonic()+args.wait_seconds
        while True:
            status = request('status')
            if status['state'] == 'connected' and not status['simulated'] and status['transport'] == 'usb':
                try:
                    previous = inspect({})
                    break
                except RuntimeError:
                    if time.monotonic() >= deadline:
                        raise
            if time.monotonic() >= deadline:
                break
            time.sleep(1)
        check('physical USB Host connected', status['state'] == 'connected' and not status['simulated'] and status['transport'] == 'usb')
        report['physicalIPhone'] = True
        if previous is None:
            previous = inspect({})
        check('new native inspection capability', previous['media'].get('playerInstancesCreated') == 1)
        check('landscape iPhone 6 logical bounds', previous['width'] == 667 and previous['height'] == 375)
        capture('01-current-media', {'page': 0, 'drawer': False})
        capture('02-corner-drawer', {'drawer': True})
        inspect({'drawer': False})
        for page, name in [(1, '03-windows-apps'), (2, '04-actions'), (3, '05-worker'), (4, '06-history'), (5, '07-connection')]:
            state = capture(name, {'page': page})
            check(name+' selected', state['page'] == page)
            check(name+' retained player', state['media']['playerInstancesCreated'] == 1)
        inspect({'page': 0})
        for priority, name in [(1, '08-compact-notification'), (3, '09-critical-notification')]:
            event = dict(id=uuid.uuid4().hex, source='BackThread QA', category='ui.fixture', title='Native UI inspection', body='Foreground BackThread fixture. This is not a Windows permission test.', priority=priority, sensitive=False, expiresUtc=(datetime.datetime.now(datetime.UTC)+datetime.timedelta(seconds=30)).isoformat())
            inspect({'notification': event})
            time.sleep(.3)
            state = capture(name, {})
            check(name+' visible', state['notification']['visible'])
            check(name+' presentation', state['notification']['fullscreen'] == (priority == 3))
            before = state['notification']['history']
            duplicate = inspect({'notification': event})
            check(name+' duplicate ignored', duplicate['notification']['history'] == before)
            inspect({'dismissNotification': True})
            time.sleep(.3)
        restored = capture('10-media-restored', {})
        check('same media owner after notifications', restored['media']['owner'] == previous['media']['owner'])
        report['passed'] = True
    except Exception as error:
        report['error'] = type(error).__name__+': '+str(error)
    finally:
        if previous is not None:
            try:
                inspect({'dismissNotification': True, 'page': previous['page'], 'drawer': previous['drawerOpen']})
            except Exception as error:
                report['restoreError'] = type(error).__name__+': '+str(error)
        (args.output/'results.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(dict(passed=report['passed'], physicalIPhone=report['physicalIPhone'], checks=len(report['checks']), screenshots=len(report['screenshots']), error=report.get('error'))))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
