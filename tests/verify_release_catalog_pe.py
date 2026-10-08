"""Check frozen native bytes and MAP symbols against a Timer memory baseline.

This reads the actual delivered PE, not a memory fixture synthesized from the
manifest itself. It does not start PAL or replace physical-game acceptance.
"""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path
from freeze_release_catalog import PE


def main():
    parser = argparse.ArgumentParser()
    for name in ('manifest', 'dll', 'map', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
    pe = PE(args.dll)
    rows = []
    def check(name, passed):
        rows.append({'name': name, 'passed': bool(passed)})
    identity = next(f for f in manifest['files'] if f['path'] == 'PAL.dll')
    check('dll-exact-identity', identity['size'] == len(pe.data) and
          identity['sha256'] == hashlib.sha256(pe.data).hexdigest())
    text = args.map.read_text(encoding='utf-8-sig')
    check('dll-map-timestamp', int(re.search(r'Timestamp is ([0-9a-fA-F]+)', text)[1], 16) == pe.timestamp)
    check('dll-map-base', int(re.search(r'Preferred load address is ([0-9a-fA-F]+)', text)[1], 16) == pe.base)
    symbols = {m[1]: int(m[2], 16)-pe.base for m in re.finditer(
        r'^\s+[0-9a-fA-F]{4}:[0-9a-fA-F]+\s+(\S+)\s+([0-9a-fA-F]{8})\s', text, re.M)}
    def symbol(prefix):
        found = [value for name, value in symbols.items() if name.startswith(prefix)]
        assert len(found) == 1, prefix
        return found[0]
    bindings = {'random-entry': '?MyrtcRandomNext@@', 'random-trampoline': '?RealrtcRandomNext@@',
                'role-load-dispatch': '?LoadGuard@', 'role-store-dispatch': '?StoreGuard@'}
    for region in manifest['memory_regions']:
        if region['module'] == 'PAL.dll' and 'rva' in region:
            expected = bytes.fromhex(region['expected'])
            check(region['id']+'-file-bytes', pe.read(region['rva'], len(expected)) == expected)
            for fixup in region.get('fixups', []):
                if fixup['module'] != 'PAL.dll' or fixup['kind'] != 'abs32': continue
                actual, = struct.unpack('<I', pe.read(region['rva']+fixup['offset'], 4))
                check(region['id']+'-reloc-'+str(fixup['offset']), actual == pe.base+fixup['rva'])
        if region['id'] in bindings:
            actual = region['pointer_rva'] if region['id'] == 'random-trampoline' else region['fixups'][0]['rva']
            check(region['id']+'-map-symbol', actual == symbol(bindings[region['id']]))
    result = {'schema': 'PalTimer.NativeBaselinePECheck.v1', 'dll_sha256': hashlib.sha256(pe.data).hexdigest(),
              'manifest_sha256': hashlib.sha256(args.manifest.read_bytes()).hexdigest(), 'checks': rows,
              'passed': sum(r['passed'] for r in rows), 'failed': sum(not r['passed'] for r in rows),
              'real_game_used': False}
    args.output.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(f"Native PE baseline: {result['passed']} passed, {result['failed']} failed")
    raise SystemExit(1 if result['failed'] else 0)


if __name__ == '__main__':
    main()
