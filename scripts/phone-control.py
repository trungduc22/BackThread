"""Control foreground BackThread through existing, strictly verified SSH.

Only writes the app's own inbox. No daemon, system change, audio/video relay,
password argument, or unverified host-key enrollment. Run from your terminal
for password authentication, or supply an existing private-key path.
"""
import argparse
import json
import math
import pathlib
import re
import subprocess
import uuid
import urllib.parse


def validate_request(command, url=None, seconds=None):
    if command not in ('open', 'status', 'play', 'pause', 'seek', 'return-pc'):
        raise ValueError('Unknown phone command')
    request = dict(version=1, id=str(uuid.uuid4()), command=command)
    if command == 'open':
        if not isinstance(url, str) or not 0 < len(url) <= 12000:
            raise ValueError('A bounded HTTPS stream URL is required')
        parts = urllib.parse.urlsplit(url)
        if parts.scheme != 'https' or not parts.hostname or parts.username is not None or parts.password is not None or parts.port not in (None, 443):
            raise ValueError('Only credential-free HTTPS streams on port 443 are supported')
        request['url'] = url
    elif url is not None:
        raise ValueError('A URL is only valid for open')
    if command == 'seek':
        if isinstance(seconds, bool) or not isinstance(seconds, (int, float)) or not math.isfinite(seconds) or not 0 <= seconds <= 1e7:
            raise ValueError('A finite, nonnegative seek position is required')
        request['seconds'] = seconds
    elif seconds is not None:
        raise ValueError('Seconds are only valid for seek')
    return request


def remote_command(container, user, identifier):
    if not re.fullmatch(r'/var/mobile/Containers/Data/Application/[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}', container):
        raise ValueError('Use the exact current BackThread data-container UUID path')
    if user not in ('root', 'mobile') or str(uuid.UUID(identifier)) != identifier:
        raise ValueError('Invalid SSH identity or request identifier')
    directory = container+'/Library/BackThread/Control'
    temporary = '.request-'+identifier+'.tmp'
    # Every interpolated component above is restricted to fixed ASCII syntax.
    # Media URL/content travel on stdin, never through the remote shell/argv.
    ownership = 'chown 501:501 '+temporary+' && ' if user == 'root' else ''
    return ("umask 077; cd '"+directory+"' || exit 20; "
            "test \"$(pwd -P)\" = '"+directory+"' || test \"$(pwd -P)\" = '/private"+directory+"' || exit 20; "
            "mkdir .ssh-control-lock || exit 21; "
            "trap 'rm -f "+temporary+"; rmdir .ssh-control-lock' EXIT; "
            'test ! -e request.json || exit 22; '
            'set -C; cat > '+temporary+' && '+ownership+'chmod 600 '+temporary+' && '
            'mv '+temporary+' request.json || exit 23; '
            'n=0; while test "$n" -lt 12; do '
            "if test -f response.json && grep -Fq '"+identifier+"' response.json; then "
            'head -c 16385 response.json; exit 0; fi; n=$((n+1)); sleep 1; done; exit 24')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--container', required=True)
    parser.add_argument('--port', type=int, default=23444, help='Existing loopback USB-to-SSH forward')
    parser.add_argument('--user', choices=('root', 'mobile'), default='root')
    parser.add_argument('--known-hosts', required=True, type=pathlib.Path)
    parser.add_argument('--host-key-alias', default='backthread-device')
    parser.add_argument('--identity', type=pathlib.Path)
    parser.add_argument('command', choices=('open', 'status', 'play', 'pause', 'seek', 'return-pc'))
    parser.add_argument('--request-file', type=pathlib.Path, help='Private resolver JSON containing url; signed URLs stay out of argv')
    parser.add_argument('--url', help='For nonsensitive direct media URLs; use --request-file for signed URLs')
    parser.add_argument('--seconds', type=float)
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535 or not re.fullmatch(r'[A-Za-z0-9._-]{1,128}', args.host_key_alias):
        parser.error('Invalid loopback SSH endpoint')
    if not args.known_hosts.is_file() or args.identity and not args.identity.is_file():
        parser.error('Existing host record/key file is required; host keys are never auto-enrolled')
    if args.request_file:
        if args.url or args.request_file.stat().st_size > 32768:
            parser.error('Choose one bounded input file or URL')
        args.url = json.loads(args.request_file.read_text(encoding='utf-8'))['url']
    request = validate_request(args.command, args.url, args.seconds)
    command = remote_command(args.container, args.user, request['id'])
    options = ['ssh', '-T', '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=5',
               '-o', 'UserKnownHostsFile='+str(args.known_hosts.resolve()),
               '-o', 'HostKeyAlias='+args.host_key_alias, '-p', str(args.port)]
    if args.identity:
        options += ['-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-i', str(args.identity.resolve())]
    options += [args.user+'@127.0.0.1', command]
    result = subprocess.run(options, input=json.dumps(request).encode(), stdout=subprocess.PIPE, timeout=90, check=False)
    if result.returncode:
        raise RuntimeError('SSH/inbox failed (code '+str(result.returncode)+'); check the verified SSH endpoint and foreground build10. No system repair was attempted.')
    if len(result.stdout) > 16384:
        raise ValueError('Oversized phone response')
    response = json.loads(result.stdout)
    if response.get('id') != request['id'] or response.get('version') != 1:
        raise ValueError('Phone response did not match this request')
    print(json.dumps(response, ensure_ascii=False))
    return 0 if response.get('success') else 1


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, OSError, RuntimeError, subprocess.TimeoutExpired):
        # Do not echo exceptions that could include a signed media URL.
        print(json.dumps(dict(success=False, error='Phone control unavailable. Check arguments, existing SSH trust, current BackThread container, and foreground build10.')))
        raise SystemExit(1)
