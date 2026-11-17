"""Read-only Apple usbmux / lockdown diagnostic. Never pairs, installs, modifies firmware, or authenticates SSH."""
import argparse, json, plistlib, socket, struct

def exact(s, n):
    result = bytearray()
    while len(result) < n:
        part = s.recv(n-len(result))
        if not part: raise EOFError('Connection closed')
        result.extend(part)
    return bytes(result)

def mux(message):
    s=socket.create_connection(('127.0.0.1',27015),3);s.settimeout(3)
    data=plistlib.dumps(dict(ClientVersionString='BackThread/0.1',ProgName='BackThread',kLibUSBMuxVersion=3,**message))
    s.sendall(struct.pack('<IIII',16+len(data),1,8,1)+data)
    header=struct.unpack('<IIII',exact(s,16))
    if not 16<=header[0]<=1024*1024: s.close();raise ValueError('Invalid usbmux reply')
    return s,plistlib.loads(exact(s,header[0]-16))

def diagnose():
    report={'usbServiceAvailable':False,'devices':[]}
    try:
        s,reply=mux({'MessageType':'ListDevices'});s.close();report['usbServiceAvailable']=True
        for item in reply.get('DeviceList',[]):
            props=item.get('Properties',{})
            if props.get('ConnectionType','USB')!='USB':continue
            device={'deviceId':item['DeviceID'],'connection':'USB','trustSessionValidated':False,'hardwareApplicationTests':'not run'}
            serial=props.get('SerialNumber','')
            try:
                s,pair=mux({'MessageType':'ReadPairRecord','PairRecordID':serial});device['localPairRecordPresent']='PairRecordData' in pair;s.close()
                for port in [62078,22,23451]:
                    s,result=mux({'MessageType':'Connect','DeviceID':item['DeviceID'],'PortNumber':socket.htons(port)})
                    device[str(port)]={'usbmuxResult':result.get('Number')}
                    if result.get('Number')==0:
                        if port==22: device['sshBanner']=s.recv(200).decode(errors='replace').strip()
                        if port==62078:
                            for field in ['ProductType','ProductVersion','DeviceClass']:
                                data=plistlib.dumps({'Request':'GetValue','Key':field,'Label':'BackThread-audit'});s.sendall(struct.pack('>I',len(data))+data)
                                size=struct.unpack('>I',exact(s,4))[0]
                                if size>1024*1024:raise ValueError('Invalid lockdown message')
                                value=plistlib.loads(exact(s,size));device[field]=value.get('Value',value.get('Error','unknown'))
                    s.close()
            except Exception as error: device['error']=type(error).__name__+': '+str(error)
            report['devices'].append(device)
    except Exception as error: report['error']=type(error).__name__+': '+str(error)
    return report

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output');args=parser.parse_args();report=diagnose();text=json.dumps(report,indent=2);print(text)
    if args.output:
        from pathlib import Path
        Path(args.output).parent.mkdir(parents=True,exist_ok=True);Path(args.output).write_text(text)
