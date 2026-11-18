"""Build-time Mach-O and Debian helpers. No device access or fabricated SDK symbols."""
import gzip
import hashlib
import io
import json
import pathlib
import re
import struct
import tarfile


def write_audit_header(image, output):
    """Generate a diagnostic input from real undefined strong imports, never SDK stubs."""
    metadata=macho(image);data=pathlib.Path(image).read_bytes();offset=32
    symbols=count=strings=size=None
    for _ in range(struct.unpack_from('<I',data,16)[0]):
        command,length=struct.unpack_from('<II',data,offset)
        if command==2:symbols,count,strings,size=struct.unpack_from('<4I',data,offset+8)
        offset+=length
    if symbols is None:raise ValueError('Image has no symbol table for loader audit')
    names=[]
    for index in range(count):
        name,kind,section,description,value=struct.unpack_from('<IBBHQ',data,symbols+index*16)
        if kind&14==0 and kind&1 and not description&0x40:
            text=data[strings+name:strings+size].split(b'\0')[0].decode()
            names.append(text[1:] if text.startswith('_') else text)
    def array(name,values):return 'static const char *'+name+'[]={'+','.join(json.dumps(s) for s in values)+'};\n'
    pathlib.Path(output).write_text(array('audit_libraries',metadata['linkedLibraries'])+array('audit_symbols',names))


def macho(path):
    data = pathlib.Path(path).read_bytes()
    if len(data) < 32 or struct.unpack_from('<I', data)[0] != 0xfeedfacf:
        raise ValueError('Expected a thin 64-bit Mach-O')
    _, cpu, subtype, kind, count, size, flags, _ = struct.unpack_from('<8I', data)
    if cpu != 0x100000c or subtype & 0xffffff == 2:
        raise ValueError('Expected baseline ARM64, not ARM64e')
    if 32 + size > len(data):
        raise ValueError('Truncated Mach-O load commands')
    result = dict(architecture='arm64', fileType=kind, flags=flags, linkedLibraries=[], signed=False)
    offset = 32
    for _ in range(count):
        command, length = struct.unpack_from('<II', data, offset)
        if length < 8 or offset + length > 32 + size:
            raise ValueError('Invalid Mach-O command')
        if command in (0x80000033,0x80000034):
            raise ValueError('iOS 12 requires legacy dyld bind/export metadata')
        if command==0x80000022:
            result['linkMetadataFormat']='legacy dyld info'
        if command == 0x32:
            platform, minimum, sdk = struct.unpack_from('<III', data, offset + 8)
            result.update(platform=platform, minimumOS=f'{minimum >> 16}.{minimum >> 8 & 255}.{minimum & 255}', sdkVersion=f'{sdk >> 16}.{sdk >> 8 & 255}.{sdk & 255}')
        if command == 0x25:
            minimum = struct.unpack_from('<I', data, offset + 8)[0]
            result.update(platform=2, minimumOS=f'{minimum >> 16}.{minimum >> 8 & 255}.{minimum & 255}')
        if command in (0xc, 0x80000018, 0x8000001f):
            start = struct.unpack_from('<I', data, offset + 8)[0]
            result['linkedLibraries'].append(data[offset + start:offset + length].split(b'\0')[0].decode())
        if command == 0x1d:
            start, length_in_file = struct.unpack_from('<II', data, offset + 8)
            if start + length_in_file > len(data) or length_in_file < 12:
                raise ValueError('Invalid code signature range')
            result['signed'] = struct.unpack_from('>I', data, start)[0] == 0xfade0cc0
        offset += length
    if kind == 2 and (result.get('platform') != 2 or result.get('minimumOS') != '12.0.0'):
        raise ValueError('Executable must target iOS 12.0')
    if kind==2 and result.get('linkMetadataFormat')!='legacy dyld info':
        raise ValueError('Missing iOS 12 legacy dyld metadata')
    return result


def normalize_tbds(sdk, destination):
    """Split existing YAML documents for Zig 0.13's reader; never add an export."""
    selected = {}
    for path in sorted(sdk.rglob('*.tbd')):
        documents = [d for d in re.split(r'(?m)(?=^--- !tapi-tbd)', path.read_text()) if d.strip()]
        for document in documents:
            found = re.search(r'^install-name:\s*[\'\"]?([^\r\n\'\"]+)', document, re.M)
            if not found:
                raise ValueError(f'TBD lacks install name: {path}')
            name = found.group(1).strip()
            if not name.startswith('/') or '..' in pathlib.PurePosixPath(name).parts:
                raise ValueError('Unsafe TBD install name')
            relative = name[1:]
            relative = relative[:-6] + '.tbd' if relative.endswith('.dylib') else relative + '.tbd'
            # Public umbrella documents precede patched private stubs in sorted SDK paths.
            # Keep the public SDK's declared re-export interface when both describe one library.
            selected.setdefault(relative, (document, path.relative_to(sdk).as_posix()))
        selected.setdefault(path.relative_to(sdk).as_posix(), (documents[0], path.relative_to(sdk).as_posix()))
    manifest = []
    for relative, (document, source) in sorted(selected.items()):
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(document.encode())
        manifest.append(dict(source=source, target=relative, sha256=hashlib.sha256(document.encode()).hexdigest()))
    return manifest


def stable_uuid(path):
    """Derive the optional image UUID from linked contents before signing."""
    data = bytearray(path.read_bytes())
    count = struct.unpack_from('<I', data, 16)[0]
    offset = 32
    for _ in range(count):
        command, length = struct.unpack_from('<II', data, offset)
        if command == 0x1b:
            data[offset + 8:offset + 24] = bytes(16)
            identity = bytearray(hashlib.sha256(data).digest()[:16])
            identity[6] = identity[6] & 15 | 0x50
            identity[8] = identity[8] & 63 | 0x80
            data[offset + 8:offset + 24] = identity
            path.write_bytes(data)
            return
        offset += length


def verify_adhoc(path, bundle=None):
    """Verify Apple CodeDirectory SHA-256 page hashes and optional bundle special slots."""
    data=path.read_bytes();offset=32;signature=None
    for _ in range(struct.unpack_from('<I',data,16)[0]):
        command,length=struct.unpack_from('<II',data,offset)
        if command==0x1d:
            start,size=struct.unpack_from('<II',data,offset+8);signature=data[start:start+size]
        offset+=length
    if signature is None or struct.unpack_from('>I',signature)[0]!=0xfade0cc0:raise ValueError('Missing signature superblob')
    count=struct.unpack_from('>I',signature,8)[0];directory=None
    for i in range(count):
        slot,start=struct.unpack_from('>II',signature,12+i*8)
        if slot==0:directory=signature[start:]
    if directory is None:raise ValueError('Missing CodeDirectory')
    magic,length,version,flags,hash_offset,identifier,special,slots,limit=struct.unpack_from('>9I',directory)
    hash_size,hash_type,platform,page_bits=struct.unpack_from('4B',directory,36)
    if magic!=0xfade0c02 or not flags&2 or hash_type!=2 or hash_size!=32:raise ValueError('Expected SHA-256 ad-hoc CodeDirectory')
    page=1<<page_bits
    if slots!=(limit+page-1)//page:raise ValueError('Code slot count mismatch')
    for i in range(slots):
        expected=directory[hash_offset+i*32:hash_offset+(i+1)*32]
        if hashlib.sha256(data[i*page:min((i+1)*page,limit)]).digest()!=expected:raise ValueError('Mach-O code page hash mismatch')
    if bundle:
        for slot,relative in ((1,'Info.plist'),(3,'_CodeSignature/CodeResources')):
            if special<slot or hashlib.sha256((bundle/relative).read_bytes()).digest()!=directory[hash_offset-slot*32:hash_offset-(slot-1)*32]:raise ValueError('Bundle special-slot hash mismatch')
        import plistlib
        resources=plistlib.loads((bundle/'_CodeSignature/CodeResources').read_bytes())
        for relative,entry in resources.get('files2',{}).items():
            if isinstance(entry,dict) and 'hash2' in entry and hashlib.sha256((bundle/relative).read_bytes()).digest()!=entry['hash2']:raise ValueError('Bundle resource hash mismatch')
    return dict(codePages=slots,pageSize=page,sha256=hashlib.sha256(directory[:length]).hexdigest())


def tar_gzip(entries, epoch=0, directories=()):
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode='w', format=tarfile.USTAR_FORMAT) as archive:
        for name, mode, uid, gid in sorted(directories):
            if name.startswith('/') or '..' in pathlib.PurePosixPath(name).parts:
                raise ValueError('Unsafe package directory')
            item=tarfile.TarInfo(name);item.type=tarfile.DIRTYPE
            item.uid,item.gid,item.mode,item.mtime=uid,gid,mode,epoch
            archive.addfile(item)
        for name, content, mode in sorted(entries):
            if name.startswith('/') or '..' in pathlib.PurePosixPath(name).parts:
                raise ValueError('Unsafe package member')
            item = tarfile.TarInfo(name)
            item.uid = item.gid = 0
            item.mode, item.mtime, item.size = mode, epoch, len(content)
            archive.addfile(item, io.BytesIO(content))
    return gzip.compress(buffer.getvalue(), mtime=epoch)


def debian(path, control, app, epoch=0):
    postinst = b'#!/bin/sh\nset -eu\nif [ "${1:-}" = configure ] && command -v uicache >/dev/null 2>&1; then\n uicache -p /Applications/BackThread.app || echo "Register BackThread with your existing app installer." >&2\nfi\n'
    pre_remove = b'#!/bin/sh\nset -eu\nif [ "${1:-}" = remove ] && command -v uicache >/dev/null 2>&1; then\n uicache -u /Applications/BackThread.app || true\nfi\n'
    data = [(f'./Applications/BackThread.app/{p.relative_to(app).as_posix()}', p.read_bytes(), 0o755 if p.name == 'BackThread' else 0o644) for p in app.rglob('*') if p.is_file()]
    directories=[('./Applications/BackThread.app',0o755,0,0)]+[(f'./Applications/BackThread.app/{p.relative_to(app).as_posix()}',0o755,0,0) for p in app.rglob('*') if p.is_dir()]
    write_debian(path,[('./control', control, 0o644), ('./postinst', postinst, 0o755), ('./prerm', pre_remove, 0o755)],data,epoch,data_directories=directories)


def write_debian(path,control_entries,data_entries,epoch=0,data_directories=()):
    members = [('debian-binary', b'2.0\n'), ('control.tar.gz', tar_gzip(control_entries, epoch)), ('data.tar.gz', tar_gzip(data_entries, epoch,data_directories))]
    with path.open('wb') as output:
        output.write(b'!<arch>\n')
        for name, content in members:
            header = f'{name + "/":<16}{epoch:<12}{0:<6}{0:<6}{"100644":<8}{len(content):<10}`\n'.encode()
            if len(header) != 60:
                raise ValueError('Invalid ar member header')
            output.write(header + content)
            if len(content) % 2:
                output.write(b'\n')
