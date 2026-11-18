"""Temporary, loopback-only usbmux forwarding for an explicitly selected iPhone service."""
import argparse
import asyncio
import importlib.util
import json
import pathlib
import socket

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('diag', ROOT / 'scripts/diagnose-hardware.py')
diag = importlib.util.module_from_spec(spec)
spec.loader.exec_module(diag)


async def main(args):
    mux, reply = diag.mux(dict(MessageType='ListDevices'))
    mux.close()
    devices = [d for d in reply.get('DeviceList', []) if d['Properties'].get('ConnectionType') == 'USB']
    if len(devices) != 1:
        raise RuntimeError('Exactly one USB device required')
    device_id = devices[0]['DeviceID']
    slots = asyncio.Semaphore(4)

    async def accept(reader, writer):
        remote = None
        if slots.locked():
            writer.close()
            return
        async with slots:
            try:
                stream, result = await asyncio.to_thread(diag.mux, dict(MessageType='Connect', DeviceID=device_id, PortNumber=socket.htons(args.device_port)))
                if result.get('Number') != 0:
                    stream.close()
                    return
                remote_reader, remote = await asyncio.open_connection(sock=stream)

                async def copy(source, target):
                    while data := await source.read(16384):
                        target.write(data)
                        await target.drain()
                jobs = [asyncio.create_task(copy(reader, remote)), asyncio.create_task(copy(remote_reader, writer))]
                await asyncio.wait(jobs, return_when=asyncio.FIRST_COMPLETED)
                for job in jobs:
                    job.cancel()
                await asyncio.gather(*jobs, return_exceptions=True)
            except (OSError, ConnectionError, EOFError):
                pass
            finally:
                writer.close()
                if remote:
                    remote.close()

    server = await asyncio.start_server(accept, '127.0.0.1', args.local_port, limit=32768)
    details = dict(localAddress='127.0.0.1', localPort=server.sockets[0].getsockname()[1], devicePort=args.device_port, deviceId=device_id)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(details, indent=2))
    print(json.dumps(details), flush=True)
    async with server:
        await asyncio.sleep(args.seconds)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--device-port', type=int, required=True)
    parser.add_argument('--local-port', type=int, default=0)
    parser.add_argument('--seconds', type=int, default=1800)
    parser.add_argument('--output', type=pathlib.Path)
    options = parser.parse_args()
    if not 1 <= options.device_port <= 65535 or not 0 <= options.local_port <= 65535 or not 1 <= options.seconds <= 3600:
        parser.error('Invalid port or bounded duration')
    try:
        asyncio.run(main(options))
    except KeyboardInterrupt:
        pass
