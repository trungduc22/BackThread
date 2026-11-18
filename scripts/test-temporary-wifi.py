"""Exercise a real enrolled phone TLS handover; always revoke the temporary session.

Requires an explicit local private IPv4 address and an authenticated USB Host.
Does not change firewall, trust stores, network settings or system volume.
"""
import argparse
import datetime
import json
import pathlib
import time
import urllib.request
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True, type=pathlib.Path)
    parser.add_argument('--address', required=True)
    parser.add_argument('--output', type=pathlib.Path, default=ROOT/'artifacts/wifi-physical.json')
    args = parser.parse_args()
    config = json.loads(args.config.read_text())
    key_path = pathlib.Path(config['keyFile'])
    if not key_path.is_absolute():
        key_path = ROOT/key_path
    headers = {'Authorization': 'Bearer '+key_path.read_text().strip().lower(), 'Content-Type': 'application/json'}
    report = dict(timestampUtc=datetime.datetime.now(datetime.UTC).isoformat(), physicalIPhone=False,
                  passed=False, wifiPcmEnabled=False, checks=[], privateEnrollmentLogged=False)

    def request(path, body=None):
        data = None if body is None else json.dumps(body).encode()
        with urllib.request.urlopen(urllib.request.Request('http://127.0.0.1:'+str(config['apiPort'])+'/v1/'+path, data=data, headers=headers), timeout=20) as response:
            return json.loads(response.read())

    def wait(predicate, seconds=25):
        deadline = time.monotonic()+seconds
        while True:
            result = predicate()
            if result:
                return result
            if time.monotonic() >= deadline:
                raise TimeoutError('Requested physical transport state was not reached')
            time.sleep(.5)

    def check(name, result):
        report['checks'].append(dict(name=name, passed=bool(result)))
        if not result:
            raise AssertionError(name)

    def connected(transport):
        status = request('status')
        return status if status['state'] == 'connected' and status['transport'] == transport else None

    enabled = False
    try:
        status = wait(lambda: connected('usb'), 8)
        check('physical USB enrollment', not status['simulated'])
        report['physicalIPhone'] = True
        enrolled = request('wifi/enable', dict(address=args.address, minutes=15, fallback=False))
        enabled = True
        check('phone acknowledged USB enrollment', enrolled.get('authorized'))
        status = wait(lambda: connected('wifi'))
        check('native pinned TLS and HMAC transport owns session', status['wifi']['enabled'])
        check('Wi-Fi PCM stays off', status['audioFrames'] == 0)
        state = request('ui', {})
        check('same native player after TLS handover', state.get('media', {}).get('playerInstancesCreated') == 1)
        jid = uuid.uuid4().hex
        request('jobs', dict(jobId=jid, kind='diagnostic', name='memory'))
        def completed():
            job = request('jobs/'+jid)
            if job.get('state') in ('failed', 'cancelled'):
                raise RuntimeError('Native diagnostic failed over TLS')
            return job if job.get('state') == 'completed' else None
        job = wait(completed, 10)
        check('real native worker completed over TLS', job['state'] == 'completed')
        report['memory'] = job.get('result')
        request('wifi/disable', {})
        enabled = False
        status = wait(lambda: connected('usb'))
        check('USB restored after revocation', not status['wifi']['enabled'])
        report['passed'] = True
    except Exception as error:
        report['error'] = type(error).__name__+': '+str(error)
    finally:
        if enabled:
            try:
                request('wifi/disable', {})
                report['revokedAfterFailure'] = True
            except Exception as error:
                report['cleanupError'] = type(error).__name__+': '+str(error)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
