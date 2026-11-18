"""Install only the built BackThread bundle via paired AFC/Installation Proxy.

Explicit --install is required. No signing-policy, activation, jailbreak or firmware operations.
Wire-message references: libimobiledevice's AFC and installation_proxy protocol implementations.
"""
import argparse
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
import uuid
import zipfile
from ios_build_support import macho, verify_adhoc

ROOT = pathlib.Path(__file__).resolve().parents[1]
IDENTIFIERS = {'ui':'app.backthread.companion','probe':'app.backthread.probe'}
spec = importlib.util.spec_from_file_location('audit', ROOT / 'scripts/inspect-ios-device.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)
diag = audit.diagnostic


class PairedDevice:
    def __enter__(self):
        self.lockdown = None
        self.session = None
        self.sockets = []
        try:
            mux, reply = diag.mux(dict(MessageType='ListDevices'))
            mux.close()
            devices = [d for d in reply.get('DeviceList', []) if d['Properties'].get('ConnectionType') == 'USB']
            if len(devices) != 1:
                raise RuntimeError('Exactly one USB device required')
            self.device = devices[0]['DeviceID']
            mux, reply = diag.mux(dict(MessageType='ReadPairRecord', PairRecordID=devices[0]['Properties']['SerialNumber']))
            mux.close()
            self.pair = plistlib.loads(reply['PairRecordData'])
            self.lockdown = self.connect(62078)
            session = audit.plist_request(self.lockdown, dict(Request='StartSession', Label='BackThread',
                                         HostID=self.pair['HostID'], SystemBUID=self.pair['SystemBUID']))
            self.session = session.get('SessionID')
            if not self.session or not session.get('EnableSessionSSL'):
                raise RuntimeError('Paired TLS session required')
            self.lockdown = self.tls(self.lockdown)
            return self
        except Exception:
            self.__exit__(None, None, None)
            raise

    def connect(self, port):
        stream, result = diag.mux(dict(MessageType='Connect', DeviceID=self.device, PortNumber=socket.htons(port)))
        if result.get('Number') != 0:
            stream.close()
            raise RuntimeError('Device service tunnel refused')
        self.sockets.append(stream)
        return stream

    def tls(self, stream):
        private = ROOT / 'artifacts/ios-private'
        private.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='install-tls-', dir=private) as temporary:
            cert, key = pathlib.Path(temporary) / 'host.pem', pathlib.Path(temporary) / 'host.key'
            cert.write_bytes(self.pair['HostCertificate'])
            key.write_bytes(self.pair['HostPrivateKey'])
            context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
            context.check_hostname = False
            context.verify_mode = ssl.CERT_NONE  # Pairing certificate is pinned below, before any app command.
            context.set_ciphers('DEFAULT:@SECLEVEL=0')
            context.load_cert_chain(cert, key)
            secured = context.wrap_socket(stream)
            self.sockets.append(secured)
        expected = ssl.PEM_cert_to_DER_cert(self.pair['DeviceCertificate'].decode())
        if not hmac.compare_digest(hashlib.sha256(expected).digest(), hashlib.sha256(secured.getpeercert(binary_form=True)).digest()):
            raise ssl.SSLError('Paired device certificate mismatch')
        return secured

    def service(self, name):
        if name not in ('com.apple.afc', 'com.apple.mobile.installation_proxy'):
            raise ValueError('Service outside BackThread installation scope')
        result = audit.plist_request(self.lockdown, dict(Request='StartService', Label='BackThread', Service=name))
        if 'Port' not in result:
            raise RuntimeError(f'{name}: {result.get("Error")}')
        stream = self.connect(result['Port'])
        return self.tls(stream) if result.get('EnableServiceSSL') else stream

    def __exit__(self, *unused):
        if self.lockdown and self.session:
            try:
                audit.plist_request(self.lockdown, dict(Request='StopSession', Label='BackThread', SessionID=self.session))
            except (OSError, ValueError, EOFError):
                pass
        for stream in self.sockets:
            stream.close()


class AFC:
    def __init__(self, stream):
        self.stream, self.sequence = stream, 0

    def command(self, operation, arguments=b'', data=b'', allowed=(0,)):
        self.sequence += 1
        self.stream.sendall(struct.pack('<8s4Q', b'CFA6LPAA', 40 + len(arguments) + len(data),
                                        40 + len(arguments), self.sequence, operation) + arguments + data)
        magic, total, header, sequence, response = struct.unpack('<8s4Q', diag.exact(self.stream, 40))
        if magic != b'CFA6LPAA' or not 40 <= header <= total <= 1048576 or sequence != self.sequence:
            raise ValueError('Invalid AFC reply')
        body = diag.exact(self.stream, total - 40)
        if response == 1:
            status = struct.unpack('<Q', body)[0]
            if status not in allowed:
                raise RuntimeError(f'AFC operation {operation} failed: {status}')
        elif response != 14:
            raise ValueError(f'Unexpected AFC operation: {response}')
        return body


def receive_plist(stream):
    length = struct.unpack('>I', diag.exact(stream, 4))[0]
    if length > 1048576:
        raise ValueError('Oversized installer response')
    return plistlib.loads(diag.exact(stream, length))


def lookup_app(device, identifier):
    if identifier not in IDENTIFIERS.values():
        raise ValueError('Only BackThread applications may be queried or changed')
    service=device.service('com.apple.mobile.installation_proxy')
    try:
        request=plistlib.dumps(dict(Command='Lookup',ClientOptions=dict(BundleIDs=[identifier],ReturnAttributes=['CFBundleIdentifier','CFBundleShortVersionString','CFBundleExecutable','Path','Container'])))
        service.sendall(struct.pack('>I',len(request))+request)
        reply=receive_plist(service)
        if reply.get('Error'):raise RuntimeError(reply['Error'])
        return reply.get('LookupResult',{}).get(identifier,{})
    finally:service.close()


def install_command(identifier, existing):
    if identifier not in IDENTIFIERS.values():raise ValueError('Application outside BackThread scope')
    # Upgrade preserves an existing BackThread data container; Install is only for a new app.
    return 'Upgrade' if existing else 'Install'


def uninstall(component):
    identifier=IDENTIFIERS[component]
    report=dict(bundleIdentifier=identifier,method='paired Installation Proxy Uninstall',statuses=[],uninstalled=False)
    with PairedDevice() as device:
        if not lookup_app(device,identifier):
            report.update(uninstalled=True,alreadyAbsent=True)
        else:
            service=device.service('com.apple.mobile.installation_proxy')
            request=plistlib.dumps(dict(Command='Uninstall',ApplicationIdentifier=identifier))
            service.sendall(struct.pack('>I',len(request))+request)
            service.settimeout(30)
            for _ in range(100):
                status=receive_plist(service);report['statuses'].append(status)
                if status.get('Error') or status.get('Status')=='Complete':
                    report['uninstalled']=status.get('Status')=='Complete' and not status.get('Error');break
    output=ROOT/f'artifacts/ios-{component}-uninstall.json';output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
    return 0 if report['uninstalled'] else 1


def main():
    parser = argparse.ArgumentParser()
    action=parser.add_mutually_exclusive_group()
    action.add_argument('--install', action='store_true', help='Install or upgrade BackThread on the one paired USB device')
    action.add_argument('--uninstall',action='store_true',help='Remove only the selected BackThread app and its container through the normal installer')
    parser.add_argument('--component', choices=('ui','probe'), default='ui')
    args = parser.parse_args()
    if args.uninstall:raise SystemExit(uninstall(args.component))
    app = ROOT / ('artifacts/ios-build/stage/BackThread.app' if args.component=='ui' else 'artifacts/ios-probe/stage/BackThreadProbe.app')
    identifier=IDENTIFIERS[args.component]
    info = plistlib.loads((app / 'Info.plist').read_bytes())
    if info['CFBundleIdentifier'] != identifier:
        raise ValueError('Only BackThread may be installed')
    binary=app/info['CFBundleExecutable']
    macho(binary)
    verify_adhoc(binary, app)
    ipa = app.parents[1] / (app.stem+'.ipa')
    with zipfile.ZipFile(ipa, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(app.rglob('*')):
            if path.is_file():
                item = zipfile.ZipInfo('Payload/'+app.name+'/' + path.relative_to(app).as_posix(), (1980, 1, 1, 0, 0, 0))
                item.external_attr = (0o100755 if path==binary else 0o100644) << 16
                archive.writestr(item, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    if not args.install:
        print(f'Prepared {ipa.name} only; pass --install for an authorized device installation.')
        return
    report = dict(bundleIdentifier=info['CFBundleIdentifier'], ipaSha256=hashlib.sha256(ipa.read_bytes()).hexdigest(),
                  method='paired AFC + Installation Proxy', statuses=[], installed=False, stagingRemoved=False)
    with PairedDevice() as device:
        report['pairedCertificatePinned'] = True
        command=install_command(identifier,lookup_app(device,identifier))
        report['command']=command
        afc = AFC(device.service('com.apple.afc'))
        remote = ('PublicStaging/BackThread-' + uuid.uuid4().hex + '.ipa').encode() + b'\0'
        created = False
        try:
            afc.command(9, b'PublicStaging\0', allowed=(0, 16))
            handle = afc.command(13, struct.pack('<Q', 3) + remote)
            if len(handle) != 8:
                raise ValueError('Invalid AFC file handle')
            created = True
            try:
                with ipa.open('rb') as source:
                    while chunk := source.read(32768):
                        afc.command(16, handle, chunk)
            finally:
                afc.command(20, handle)
            service = device.service('com.apple.mobile.installation_proxy')
            request = plistlib.dumps(dict(Command=command, PackagePath=remote[:-1].decode(),
                                         ClientOptions=dict(CFBundleIdentifier=identifier)))
            service.sendall(struct.pack('>I', len(request)) + request)
            service.settimeout(30)
            deadline = time.monotonic() + 120
            for _ in range(100):
                status = receive_plist(service)
                report['statuses'].append(status)
                print(json.dumps(status), flush=True)
                if status.get('Error') or status.get('Status') == 'Complete':
                    report['installed'] = status.get('Status') == 'Complete' and not status.get('Error')
                    break
                if time.monotonic() > deadline:
                    raise TimeoutError('BackThread installer deadline exceeded')
        except Exception as error:
            report['exception'] = type(error).__name__ + ': ' + str(error)
        finally:
            if created:
                try:
                    afc.command(8, remote, allowed=(0, 8))
                    report['stagingRemoved'] = True
                except (OSError, ValueError, RuntimeError) as error:
                    report['cleanupError'] = str(error)
            output = ROOT / ('artifacts/ios-audit/installation-proxy-result.json' if args.component=='ui' else 'artifacts/ios-probe/installation-result.json')
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(json.dumps(report, indent=2))
    raise SystemExit(0 if report['installed'] else 1)


if __name__ == '__main__':
    main()
