"""OpenSSH ProxyCommand transport: stdin/stdout are raw usbmux tunnel bytes; no TCP listener."""
import argparse
import importlib.util
import os
import pathlib
import socket
import threading

root=pathlib.Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('diag',root/'scripts/diagnose-hardware.py')
diag=importlib.util.module_from_spec(spec)
spec.loader.exec_module(diag)

if __name__=='__main__':
    if os.name=='nt':
        import msvcrt
        msvcrt.setmode(0,os.O_BINARY)
        msvcrt.setmode(1,os.O_BINARY)
    parser=argparse.ArgumentParser()
    parser.add_argument('--device-port',type=int,required=True)
    args=parser.parse_args()
    if not 1<=args.device_port<=65535:parser.error('Invalid device port')
    mux,reply=diag.mux(dict(MessageType='ListDevices'));mux.close()
    devices=[d for d in reply.get('DeviceList',[]) if d['Properties'].get('ConnectionType')=='USB']
    if len(devices)!=1:raise RuntimeError('Exactly one USB device is required')
    tunnel,result=diag.mux(dict(MessageType='Connect',DeviceID=devices[0]['DeviceID'],PortNumber=socket.htons(args.device_port)))
    if result.get('Number')!=0:raise RuntimeError('USB service port unavailable')
    tunnel.settimeout(None)
    def outgoing():
        try:
            while data:=os.read(0,16384):tunnel.sendall(data)
        except OSError:pass
        finally:
            try:tunnel.shutdown(socket.SHUT_WR)
            except OSError:pass
    threading.Thread(target=outgoing,daemon=True).start()
    try:
        while data:=tunnel.recv(16384):
            while data:data=data[os.write(1,data):]
    finally:tunnel.close()
