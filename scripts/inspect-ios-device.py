"""Read-only trusted lockdown/service/port audit. Never pairs, installs, activates or changes firmware."""
import argparse
import datetime
import hashlib
import hmac
import importlib.util
import json
import pathlib
import plistlib
import socket
import ssl
import struct
import tempfile
import time
from ios_log_support import BackThreadLogs

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('backthread_diagnostic', ROOT / 'scripts/diagnose-hardware.py')
diagnostic = importlib.util.module_from_spec(spec)
spec.loader.exec_module(diagnostic)


def plist_request(stream, body):
    data = plistlib.dumps(body)
    stream.sendall(struct.pack('>I', len(data)) + data)
    size = struct.unpack('>I', diagnostic.exact(stream, 4))[0]
    if size > 1048576:
        raise ValueError('Oversized lockdown reply')
    return plistlib.loads(diagnostic.exact(stream, size))


def inspect(log_seconds=2):
    report = dict(timestampUtc=datetime.datetime.now(datetime.UTC).isoformat(), persistentDeviceChanges=False, pairingAttempted=False, applicationTestsRun=False, trustSessionValidated=False, jailbroken='not established')
    stream = None
    session_id = None
    try:
        mux, devices = diagnostic.mux(dict(MessageType='ListDevices'))
        mux.close()
        usb = [d for d in devices.get('DeviceList', []) if d['Properties'].get('ConnectionType') == 'USB']
        if len(usb) != 1:
            raise RuntimeError('Exactly one USB device is required for this targeted audit')
        device = usb[0]
        device_id = device['DeviceID']
        mux, record = diagnostic.mux(dict(MessageType='ReadPairRecord', PairRecordID=device['Properties']['SerialNumber']))
        mux.close()
        pair = plistlib.loads(record['PairRecordData'])
        stream, result = diagnostic.mux(dict(MessageType='Connect', DeviceID=device_id, PortNumber=socket.htons(62078)))
        if result.get('Number') != 0:
            raise RuntimeError('Lockdown USB tunnel refused')
        session = plist_request(stream, dict(Label='BackThread', Request='StartSession', HostID=pair['HostID'], SystemBUID=pair['SystemBUID']))
        session_id = session.get('SessionID')
        if not session_id or not session.get('EnableSessionSSL'):
            raise RuntimeError('Trusted TLS session unavailable: ' + str(session.get('Error', 'SSL not requested')))
        private_root = ROOT / 'artifacts/ios-private'
        private_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='lockdown-', dir=private_root) as temporary:
            private = pathlib.Path(temporary).resolve()
            if not private.is_relative_to(private_root.resolve()):
                raise ValueError('Unsafe credential scratch directory')
            cert, key = private / 'host.pem', private / 'host.key'
            cert.write_bytes(pair['HostCertificate'])
            key.write_bytes(pair['HostPrivateKey'])
            context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
            context.check_hostname = False
            # Lockdown uses pairing certificates, not a DNS/Internet CA. Pin the exact paired device certificate below.
            context.verify_mode = ssl.CERT_NONE
            context.set_ciphers('DEFAULT:@SECLEVEL=0')
            context.load_cert_chain(cert, key)
            stream = context.wrap_socket(stream, server_hostname=None)
        paired = ssl.PEM_cert_to_DER_cert(pair['DeviceCertificate'].decode())
        if not hmac.compare_digest(hashlib.sha256(paired).digest(), hashlib.sha256(stream.getpeercert(binary_form=True)).digest()):
            raise ssl.SSLError('Paired device certificate pin mismatch')
        report['trustSessionValidated'] = report['pairedCertificatePinned'] = True
        for field in ('ProductType', 'ProductVersion', 'ActivationState'):
            value = plist_request(stream, dict(Label='BackThread', Request='GetValue', Key=field))
            report[field] = value.get('Value', value.get('Error', 'unknown'))
        report['ports'] = {}
        for port in (22, 44, 2222, 23451):
            tunnel, result = diagnostic.mux(dict(MessageType='Connect', DeviceID=device_id, PortNumber=socket.htons(port)))
            try:
                entry = dict(usbmuxResult=result.get('Number'), reachable=result.get('Number') == 0)
                if entry['reachable'] and port != 23451:
                    entry['sshBanner'] = tunnel.recv(200).split(b'\n',1)[0].decode(errors='replace').strip()
                report['ports'][str(port)] = entry
            finally:
                tunnel.close()
        report['services'] = {}
        for service in ('com.apple.afc', 'com.apple.afc2', 'com.apple.mobile.installation_proxy', 'com.apple.syslog_relay'):
            value = plist_request(stream, dict(Label='BackThread', Request='StartService', Service=service))
            report['services'][service] = dict(available='Port' in value, error=value.get('Error'))
            if service == 'com.apple.syslog_relay' and 'Port' in value and not value.get('EnableServiceSSL') and log_seconds:
                tunnel, status = diagnostic.mux(dict(MessageType='Connect', DeviceID=device_id, PortNumber=socket.htons(value['Port'])))
                total, collector = 0, BackThreadLogs(nul_framed=True)
                try:
                    if status.get('Number') == 0:
                        tunnel.settimeout(.3)
                        deadline = time.monotonic() + min(10, max(0, log_seconds))
                        while time.monotonic() < deadline and total < 1048576:
                            try:
                                data = tunnel.recv(8192)
                            except socket.timeout:
                                continue
                            if not data:
                                break
                            total += len(data)
                            collector.feed(data)
                finally:
                    tunnel.close()
                collector.finish()
                report['syslog'] = dict(bytesObserved=total, **collector.report())
        report['standardInstallerServiceAvailable'] = report['ActivationState'] == 'Activated' and report['services']['com.apple.mobile.installation_proxy']['available']
        report['deploymentValidated'] = False  # A service being reachable does not establish signing/install authorization.
    except Exception as error:
        report['error'] = type(error).__name__ + ': ' + str(error)
    finally:
        if stream is not None:
            if session_id:
                try:
                    plist_request(stream, dict(Label='BackThread', Request='StopSession', SessionID=session_id))
                except (OSError, ValueError, EOFError):
                    pass
            stream.close()
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=pathlib.Path, default=ROOT / 'artifacts/ios-device-audit.json')
    parser.add_argument('--log-seconds', type=int, default=2)
    args = parser.parse_args()
    result = inspect(args.log_seconds)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))
    raise SystemExit(1 if 'error' in result else 0)
