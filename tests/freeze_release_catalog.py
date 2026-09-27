"""Freeze a verification baseline from a matched PE/MAP and reviewed package.

Does not edit the approved-DLL table or promote a candidate to an official build.
Existing manifests are inputs only; write to a new, explicit output path.
"""
import argparse
import copy
import hashlib
import json
import re
import struct
import subprocess
import zipfile
from pathlib import Path


def digest(data):
    return hashlib.sha256(data).hexdigest()


def identity(path, data):
    return dict(path=path, size=len(data), sha256=digest(data))


class PE:
    def __init__(self, path):
        self.data = path.read_bytes()
        at = self.u32(0x3c)
        assert self.data[at:at+4] == b'PE\0\0'
        assert self.u16(at+4) == 0x14c, 'Win32 PE required'
        self.timestamp = self.u32(at+8)
        opt = at+24
        assert self.u16(opt) == 0x10b
        self.base = self.u32(opt+28)
        self.size = self.u32(opt+56)
        self.sections = []
        start = opt+self.u16(at+20)
        for i in range(self.u16(at+6)):
            p = start+40*i
            self.sections.append((self.data[p:p+8].rstrip(b'\0').decode(),
                                  self.u32(p+12), self.u32(p+8), self.u32(p+20), self.u32(p+16)))
        rva, size = self.u32(opt+96+5*8), self.u32(opt+100+5*8)
        self.relocations = []
        n = 0
        while n < size:
            page, length = struct.unpack('<II', self.read(rva+n, 8))
            assert 8 <= length <= size-n and length % 2 == 0
            for kind in struct.unpack('<'+'H'*((length-8)//2), self.read(rva+n+8, length-8)):
                if kind >> 12:
                    assert kind >> 12 == 3, 'Unsupported loader relocation'
                    self.relocations.append(page+(kind & 0xfff))
            n += length

    def u16(self, p): return struct.unpack_from('<H', self.data, p)[0]
    def u32(self, p): return struct.unpack_from('<I', self.data, p)[0]

    def read(self, rva, size):
        for name, va, virtual, raw, length in self.sections:
            if va <= rva and rva+size <= va+max(virtual, length):
                offset = rva-va
                return (self.data[raw+offset:raw+min(length, offset+size)] + bytes(size))[:size]
        raise ValueError(f'RVA outside PE: {rva:x}')

    def protected_regions(self):
        regions = []
        for name, va, virtual, _, _ in self.sections:
            if name not in ('.rint', '.rstat', '.prng'): continue
            cursor = va
            while cursor < va+virtual:
                end = min(cursor+4096, va+virtual)
                for reloc in self.relocations:
                    if cursor <= reloc < end < reloc+4: end = reloc
                data = self.read(cursor, end-cursor)
                fixups = []
                for reloc in self.relocations:
                    if cursor <= reloc < end:
                        value = struct.unpack('<I', self.read(reloc, 4))[0]
                        assert self.base <= value < self.base+self.size
                        fixups.append(dict(offset=reloc-cursor, module='PAL.dll', rva=value-self.base, kind='abs32'))
                regions.append(dict(id=f'{name}-0x{cursor:x}', module='PAL.dll', rva=cursor, expected=data.hex(), fixups=fixups))
                cursor = end
        assert set(r['id'].split('-')[0] for r in regions) == {'.rint', '.rstat', '.prng'}
        return regions


def add_profile(manifest, archive):
    sidecar = archive.with_suffix('.json')
    side = json.loads(sidecar.read_text(encoding='utf-8-sig'))
    envelope = side['patches'][0]['game_profile']
    with zipfile.ZipFile(archive) as z:
        raw = z.read(envelope['descriptor_path'])
        h = digest(raw)
        assert h == envelope['descriptor_sha256']
        if any(p['content_sha256'] == h for p in manifest['profiles']): return
        descriptor = json.loads(raw)
        assert descriptor['profile_id'] == envelope['profile_id']
        assert descriptor['profile_version'] == envelope['profile_version']
        routed = {r['kind'].lower() for r in descriptor['resource_set']}
        original = next(p for p in manifest['profiles'] if p['content_id'] == 'pal98.original')
        root_files = [copy.deepcopy(f) for f in original['files'] if '/' not in f['path'] and f['path'].lower() not in routed]
        names = {n.lower(): n for n in z.namelist() if '/' not in n}
        for f in root_files:
            if f['path'].lower() in names:
                f.update(identity(f['path'], z.read(names[f['path'].lower()])))
        prefix = f'palmod/Profiles/p/{h[:28]}/'
        files = root_files+[identity('patches/'+archive.name, archive.read_bytes()),
                            identity('patches/'+sidecar.name, sidecar.read_bytes()),
                            identity(prefix+'manifest/game-profile.json', raw)]
        for r in descriptor['resource_set']:
            data = z.read(r['relative_path'])
            assert len(data) == r['size_bytes'] and digest(data) == r['sha256'], r['relative_path']
            path = r['relative_path']
            if r['kind'].startswith('EXTERNAL.MAGIC.RLE.CHUNK.'): path = 'x/c/'+r['sha256']
            if r['kind'] == 'EXTERNAL.MAGIC.RLE.MANIFEST': path = 'x/m'
            files.append(identity(prefix+path, data))
        manifest['profiles'].append(dict(content_id=descriptor['profile_id'], content_version=descriptor['profile_version'],
            content_sha256=h, files=files, settings=[dict(file='config.ini', section='Patch', key='DefaultPatch',
            ignore_case=True, allowed_values=[archive.stem, archive.name, sidecar.name])]))


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--base', type=Path, required=True)
    p.add_argument('--dll', type=Path, required=True)
    p.add_argument('--map', type=Path, required=True)
    p.add_argument('--helper', type=Path, required=True)
    p.add_argument('--native-repo', type=Path, required=True)
    p.add_argument('--package', type=Path, required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    assert a.base.resolve() != a.output.resolve(), 'Never replace an existing build baseline'
    manifest = json.loads(a.base.read_text(encoding='utf-8-sig'))
    pe = PE(a.dll)
    map_text = a.map.read_text(encoding='utf-8-sig')
    assert int(re.search(r'Timestamp is ([0-9a-fA-F]+)', map_text)[1], 16) == pe.timestamp, 'DLL/MAP mismatch'
    assert int(re.search(r'Preferred load address is ([0-9a-fA-F]+)', map_text)[1], 16) == pe.base
    symbols = [(m[1], int(m[2], 16)-pe.base) for m in re.finditer(r'^\s+[0-9a-fA-F]{4}:[0-9a-fA-F]+\s+(\S+)\s+([0-9a-fA-F]{8})\s', map_text, re.M)]
    def symbol(prefix):
        found = [va for name, va in symbols if name.startswith(prefix)]
        assert len(found) == 1, (prefix, found)
        return found[0]
    records = [r for r in manifest['memory_regions'] if not r['id'].startswith('.')]
    for r in records:
        if r['id'] == 'random-entry': r['fixups'][0]['rva'] = symbol('?MyrtcRandomNext@@')
        if r['id'] == 'random-trampoline': r['pointer_rva'] = symbol('?RealrtcRandomNext@@')
        if r['id'] == 'role-load-dispatch': r['fixups'][0]['rva'] = symbol('?LoadGuard@')
        if r['id'] == 'role-store-dispatch': r['fixups'][0]['rva'] = symbol('?StoreGuard@')
    manifest['memory_regions'] = pe.protected_regions()+records
    for f in manifest['files']:
        candidate = a.dll if f['path'] == 'PAL.dll' else a.helper if f['path'] == 'Pal98ProcessHelper.exe' else None
        if candidate: f.update(identity(f['path'], candidate.read_bytes()))
        elif f.get('normalization') is None:
            # Baseline core changes require deliberate review, not silent blessing.
            path = a.package/f['path']
            assert path.is_file() and digest(path.read_bytes()) == f['sha256'], f['path']
    for archive in sorted((a.package/'patches').glob('*.zip')): add_profile(manifest, archive)
    setting = dict(file='config.ini', section='extra', key='EnableHighCashDisplay', allowed_values=['0','1'], default_value='0')
    manifest['settings'] = [s for s in manifest['settings'] if s['key'] != setting['key']]+[setting]
    manifest['build'] = a.version
    manifest['release_id'] = 'pal98-'+a.version+'-candidate-'+digest(pe.data)[:16]
    manifest['frozen'] = True
    manifest['source_commit'] = subprocess.check_output(['git','rev-parse','HEAD'], cwd=a.native_repo, text=True).strip()
    names = subprocess.check_output(['git','ls-files','-co','--exclude-standard','--','*.cpp','*.h','*.vcxproj','*.def','*.rc'], cwd=a.native_repo, text=True).splitlines()
    sources = [(n,digest((a.native_repo/n).read_bytes())) for n in sorted(set(names)) if (a.native_repo/n).is_file() and not n.startswith('.agents/')]
    manifest['source_state'] = 'working-tree;source_inventory_sha256='+digest(json.dumps(sources,ensure_ascii=True,separators=(',',':')).encode())
    a.output.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n', encoding='utf-8')
    print(json.dumps(dict(build=a.version,dll_sha256=digest(pe.data),map_sha256=digest(a.map.read_bytes()),
        manifest_sha256=digest(a.output.read_bytes()),core_files=len(manifest['files']),profiles=len(manifest['profiles']),
        code_regions=len(manifest['memory_regions']),approval_changed=False),indent=2))


if __name__ == '__main__': main()
