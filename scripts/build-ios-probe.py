"""Build the small BackThread-owned UIKit execution probe using the existing cross-toolchain."""
import argparse
import hashlib
import importlib.util
import json
import os
import pathlib
import plistlib
import shutil
from ios_build_support import macho, stable_uuid, verify_adhoc, write_debian, write_audit_header

ROOT=pathlib.Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('builder',ROOT/'scripts/build-ios.py')
builder=importlib.util.module_from_spec(spec);spec.loader.exec_module(builder)

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--c-entry',action='store_true',help='Build a libc-only entry diagnostic instead of the UIKit probe')
    parser.add_argument('--linker',required=True,type=pathlib.Path,help='Existing LLVM LLD 23.1.5, the validated device execution path')
    parser.add_argument('--audit-image',type=pathlib.Path,help='Audit imports of a built BackThread image using a small native loader probe')
    parser.add_argument('--launch-ui',action='store_true',help='Use the prepared normal app-launch service for only app.backthread.companion')
    for name in ('zig','sdk','codesign'):parser.add_argument('--'+name,required=True,type=pathlib.Path)
    args=parser.parse_args();zig,sdk,signer=args.zig.resolve(),args.sdk.resolve(),args.codesign.resolve()
    out=ROOT/'artifacts/ios-probe';app=out/'stage/BackThreadProbe.app';logs=out/'logs'
    app.mkdir(parents=True,exist_ok=True);logs.mkdir(exist_ok=True)
    env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(out/'zig-cache'))
    if builder.run([zig,'version'],logs/'compiler.txt',env)!='0.13.0':raise ValueError('Zig 0.13.0 required')
    linker=args.linker.resolve()
    linker_version=builder.run([linker,'-flavor','darwin','--version'],logs/'linker.txt',env)
    if not (linker_version.startswith('LLD 23.1.') and len(linker_version)>9 and linker_version[9].isdigit()):raise ValueError('LLD 23.1.x required')
    obj=out/'Probe.o'
    c_mode=args.c_entry or args.audit_image is not None
    source='src/iOS/Probe/loader.c' if args.audit_image else 'src/iOS/Probe/entry.c' if args.c_entry else 'src/iOS/Probe/main.m'
    if args.audit_image:
        image=args.audit_image.resolve()
        if not image.is_relative_to(ROOT/'artifacts'):raise ValueError('Audit only a repository build artifact')
        write_audit_header(image,out/'audit-symbols.h')
    objc=[] if c_mode else ['-fobjc-arc','-fblocks']
    if args.launch_ui:objc+=['-DBT_PROBE_LAUNCH_UI=1']
    builder.run([zig,'cc','-target','aarch64-ios.12.0','-isysroot',sdk,'-iframework',sdk/'System/Library/Frameworks','-isystem',sdk/'usr/include','-I',out,*objc,'-O2','-g0','-fno-sanitize=all','-Wall','-Wextra','-Werror','-Werror=unguarded-availability-new','-c',source,'-o',obj],logs/'compile.txt',env)
    binary=app/'BackThreadProbe'
    frameworks=[] if c_mode else ['-lobjc','-framework','UIKit','-framework','Foundation']
    builder.run([linker,'-flavor','darwin','-arch','arm64','-platform_version','ios','12.0','12.4','-syslibroot',sdk,'-L',sdk/'usr/lib','-F',sdk/'System/Library/Frameworks','-F',sdk/'System/Library/PrivateFrameworks','-no_fixup_chains','-headerpad','0x1000',obj,'-lSystem',*frameworks,'-o',binary],logs/'link.txt',env)
    stable_uuid(binary)
    info=plistlib.loads((ROOT/'src/iOS/Resources/Info.plist').read_bytes())
    info.update(CFBundleIdentifier='app.backthread.probe',CFBundleExecutable='BackThreadProbe',CFBundleName='BackThread Probe')
    info['CFBundleURLTypes']=[dict(CFBundleURLName='app.backthread.probe',CFBundleURLSchemes=['backthread-probe'])]
    info.pop('UIBackgroundModes',None)
    (app/'Info.plist').write_bytes(plistlib.dumps(info))
    shutil.copyfile(ROOT/'src/iOS/Resources/LaunchLandscape@2x.png',app/'LaunchLandscape@2x.png')
    config=out/'empty-codesign.toml';config.write_text('')
    builder.run([signer,'sign','--config-file',config,'--timestamp-url','none',app],logs/'sign.txt',env)
    result=dict(probeKind='loader audit' if args.audit_image else 'C entry only' if args.c_entry else 'UIKit',launchOwnUI=args.launch_ui,linker=linker_version,image=macho(binary),signature=verify_adhoc(binary,app),sha256=hashlib.sha256(binary.read_bytes()).hexdigest())
    version=info['CFBundleShortVersionString']
    control=f'Package: app.backthread.probe\nName: BackThread Probe\nVersion: {version}\nArchitecture: iphoneos-arm\nDescription: Temporary BackThread-owned native entry diagnostic.\nMaintainer: BackThread contributors\nSection: Utilities\nDepends: firmware (>= 12.0)\n'.encode()
    post=b'#!/bin/sh\nset -eu\nuicache -p /Applications/BackThreadProbe.app\n'
    pre=b'#!/bin/sh\nuicache -u /Applications/BackThreadProbe.app || true\n'
    files=[('./Applications/BackThreadProbe.app/'+p.relative_to(app).as_posix(),p.read_bytes(),0o755 if p==binary else 0o644) for p in app.rglob('*') if p.is_file()]
    directories=[('./Applications/BackThreadProbe.app',0o755,0,0)]+[('./Applications/BackThreadProbe.app/'+p.relative_to(app).as_posix(),0o755,0,0) for p in app.rglob('*') if p.is_dir()]
    package=out/'BackThread.Probe.deb'
    write_debian(package,[('./control',control,0o644),('./postinst',post,0o755),('./prerm',pre,0o755)],files,data_directories=directories)
    result['packageSha256']=hashlib.sha256(package.read_bytes()).hexdigest()
    (out/'build-manifest.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))

if __name__=='__main__':main()
