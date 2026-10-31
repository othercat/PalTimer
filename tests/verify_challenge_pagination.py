"""Actual Online DLL: bounded structural catalogs/enrollments and failure rollback. Refs sdlpal-lostedit#189."""
from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit, parse_qs
import argparse, copy, hashlib, json, os, subprocess, threading


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ('runtime', 'host', 'snapshot', 'output'): p.add_argument('--' + name, required=True, type=Path)
    a = p.parse_args(); out = a.output.resolve()
    assert '/PALWork/artifacts/so-lostedit-issue-189/' in out.as_posix()
    out.mkdir(parents=True, exist_ok=False)
    frozen = json.loads(a.snapshot.read_text('utf-8')); context = dict(mode='normal', calls=[])
    player = dict(bound=True, id='33333333-3333-4333-8333-333333333333', display_name='分页测试玩家')
    device = '44444444-4444-4444-8444-444444444444'

    def item(index):
        return dict(id=f'22222222-2222-4222-8222-{index:012d}', author_id='11111111-1111-4111-8111-111111111111', author_name='分页作者',
                    title=f'结构化挑战{index}', revision=1, latest_revision=1, rules_revision=1, approval_status='approved', contents=['pal98-classic-v6'], rules=frozen)

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args): pass
        def do_GET(self): self.reply()
        def do_POST(self): self.reply()
        def reply(self):
            length = int(self.headers.get('Content-Length', '0'))
            if length: self.rfile.read(length)
            path = urlsplit(self.path); query = parse_qs(path.query); mode = context['mode']
            context['calls'].append(dict(path=path.path, offset=int(query.get('offset', ['0'])[0])))
            value = dict(protocol='PAL.Challenges.v1', as_of='2026-10-31T00:00:00Z')
            if path.path.endswith('/devices'): value.update(protocol='PAL98.TimerOnline.v1', device_id=device, player=player)
            elif path.path.endswith('/identity'): value.update(player=player, status='bound')
            elif path.path.endswith('/catalog') or path.path.endswith('/enrollments'):
                offset = int(query.get('offset', ['0'])[0]); total = 64 if mode == 'capacity' else 1 if mode == 'legacy' else 9
                stop = min(total, offset + 2); page = [item(i + 1) for i in range(offset, stop)]
                enrolled = path.path.endswith('/enrollments')
                value.update(items=page, total=total, next_offset=stop if stop < total else None)
                if enrolled:
                    value['enrollments_snapshot'] = 'changed' if mode == 'changed' and offset else 'frozen'
                    if mode == 'stalled': value['next_offset'] = offset
                    if mode == 'legacy': value = dict(protocol='PAL.Challenges.v1', as_of=value['as_of'], items=page)
                else:
                    value.update(rules=[], rule_package=frozen['package'], rules_revision=1, catalog_snapshot='frozen',
                                 contents={'pal98-classic-v6': '98柔情经典速通v6'}, heroes={'li-xiaoyao': '李逍遥'})
            else: raise AssertionError(path.path)
            data = json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
            self.send_response(200); self.send_header('Content-Type', 'application/json'); self.send_header('Content-Length', str(len(data))); self.end_headers()
            try: self.wfile.write(data)
            except (BrokenPipeError, ConnectionResetError): pass

    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler); thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
    root = out / 'game'; (root/'palmod').mkdir(parents=True); (root/'patches').mkdir()
    (root/'config.ini').write_text('[Patch]\nDefaultPatch=pal98-classic-v6\n', encoding='ascii')
    (root/'palmod/common-tools.v1.json').write_text(json.dumps({'schema':'PAL98.ToolLaunchSettings.v1', 'competition_upload':{
        'schema':'PAL98.TimerOnlineSettings.v1', 'xiaorou':{'enabled':False,'server':f'http://127.0.0.1:{server.server_port}'}}}), encoding='utf-8')
    checks=[]
    def check(value, label):
        assert value, label
        checks.append(label)
    def call(mode):
        context['mode'] = mode; context['calls'] = []
        request = out/(mode+'-request.json'); response = out/(mode+'-response.json')
        request.write_text('{"action":"refresh"}', encoding='ascii')
        proc=subprocess.run([str(a.host), str(a.runtime/'PalTimerOnline.dll'), str(root), str(request), str(response)],
                            creationflags=subprocess.CREATE_NO_WINDOW, capture_output=True, timeout=40)
        (out/(mode+'-host.log')).write_bytes(proc.stdout+proc.stderr)
        check(proc.returncode == 0 and response.is_file(), mode+' actual DLL returns a bounded explicit response')
        return json.loads(response.read_text('utf-8'))
    try:
        reply = call('normal')
        check(reply['ok'] and len(reply['state']['items']) == 9 and len(reply['state']['enrollments']) == 9, 'all catalog and enrolled pages are preserved')
        check(len([c for c in context['calls'] if c['path'].endswith('/enrollments')]) == 5, 'new enrollment pagination advances through the final page')
        check(reply['state']['items'][0]['rules'] == frozen and reply['state']['enrollments'][-1]['rules'] == frozen, 'complete 256-rule snapshots survive both paged lists')
        check(reply['state']['catalog_metadata']['rule_package'] == frozen['package'], 'rule-package metadata remains generic and intact')
        cache=root/'palmod/challenges.v1.json'; before=cache.read_bytes()
        for mode in ('changed','stalled','capacity'):
            reply=call(mode)
            check(not reply['ok'] and cache.read_bytes() == before, mode+' does not replace the last good downloaded cache')
            check(len(reply['state']['items']) == 9 and len(reply['state']['enrollments']) == 9, mode+' does not expose a partial catalog in its failure response')
            if mode == 'capacity': check(reply['code'] == 'challenge_capacity', 'total-cache capacity failure is explicit')
        reply=call('legacy')
        check(reply['ok'] and len(reply['state']['enrollments']) == 1, 'explicit old unpaged enrollment response remains compatible')
        check(not (root/'challenge-rules.ini').exists(), 'timer never applies gameplay configuration')
    finally:
        server.shutdown(); thread.join(5); server.server_close()
    (out/'summary.json').write_text(json.dumps(dict(passed=True,checks=len(checks),assertions=checks,
        productSha256=hashlib.sha256((a.runtime/'PalTimerOnline.dll').read_bytes()).hexdigest(), sameProductAllCases=True,
        rulesPerChallenge=256, pagedChallenges=9, productionNetworkUsed=False),ensure_ascii=False,indent=2),encoding='utf-8')
    print('PASS',len(checks),'actual DLL paged structural challenge checks')


if __name__ == '__main__': main()
