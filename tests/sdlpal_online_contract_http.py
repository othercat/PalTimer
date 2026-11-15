"""Actual API-1 output against an unchanged service over an ephemeral TCP port.

All identities, RSA keys, devices and the SQLite database here are local fixtures.
No production requests or gameplay, and no edits/import caches in server source.
"""
from __future__ import annotations

import argparse
import base64
import copy
from contextlib import closing
import hashlib
import http.client
import json
from pathlib import Path
import socket
import sys
import threading
import time
from types import SimpleNamespace
from uuid import uuid4


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server-root', type=Path, required=True)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    root = args.server_root.resolve()
    source_hashes = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
                     for p in (root / 'src').rglob('*.py')}
    sys.path[:0] = [str(root / 'src'), str(root / 'tests')]
    import uvicorn
    from palserver.competition_service import create_competition_app
    from palserver.shougansai_admin import hash_password
    from palserver.timer_sdlpal import sdl_rules_issues
    from test_timer_online import NOW, provision
    from test_timer_live import proof
    from timer_auth_fixtures import encoded, raw_json

    before = json.loads(args.before.read_text('utf-8-sig'))
    after = json.loads(args.after.read_text('utf-8-sig'))
    assert [x['label'] for x in before] == [x['label'] for x in after]
    app = create_competition_app(args.output, args.output / 'fixture.sqlite3', sites=[], clock=lambda: NOW,
                                 admin_password_hash=hash_password('local-fixture-only'))
    identity = provision(SimpleNamespace(app=app))
    listener = socket.socket()
    listener.bind(('127.0.0.1', 0))
    port = listener.getsockname()[1]
    listener.listen()
    server = uvicorn.Server(uvicorn.Config(app, log_level='critical', access_log=False))
    thread = threading.Thread(target=server.run, kwargs={'sockets': [listener]}, daemon=True)
    thread.start()
    deadline = time.monotonic() + 10
    while not server.started and time.monotonic() < deadline:
        time.sleep(.02)
    assert server.started
    checks = 0
    rows = []
    hwid = 'r108-local-contract-fixture'

    def request(path, body):
        # The only network destination in this test is the bound loopback socket.
        with closing(http.client.HTTPConnection('127.0.0.1', port, timeout=10)) as connection:
            connection.request('POST', '/api/v1/timer' + path, raw_json(body),
                               {'Authorization': 'Bearer ' + 'a' * 64, 'X-PAL-HWID': hwid,
                                'Content-Type': 'application/json'})
            response = connection.getresponse()
            status, data = response.status, json.loads(response.read())
            return status, data

    def check(condition, why):
        nonlocal checks
        assert condition, why
        checks += 1

    try:
        status, _ = request('/devices', {'protocol': 'PAL98.TimerOnline.v1', 'hwid': hwid})
        check(status == 200, 'Real HTTP device registration')
        status, challenge = request('/live/challenges', {'protocol': 'PAL98.TimerLive.v1', 'hwid': hwid,
             'instance_id': str(uuid4()), 'key_id': identity['key_id'], 'timer_exe_sha256': identity['exe_sha256']})
        check(status == 200, 'Real HTTP challenge accepted before faulty configuration is sent')
        signed = proof(challenge)
        status, session = request('/live/sessions', {'protocol': 'PAL98.TimerLive.v1', 'proof_base64': encoded(raw_json(signed))})
        check(status == 200, 'Ephemeral RSA proof establishes a real session')
        sequence = 0

        def update(case, mutate=None):
            nonlocal sequence
            sequence += 1
            ranking = case['ranking']
            observation = {'run_id': None, 'run_generation': sequence, 'state': 'waiting',
                'configuration_id': ranking['configuration_id'], 'custom_competition_id': None,
                'route_sha256': case['route'], 'game_title': 'LOCAL HTTP CONTRACT FIXTURE', 'release_revision': 'r107',
                'maintenance_ranking': copy.deepcopy(ranking), 'elapsed_ms': 0, 'restart_ms': 0,
                'current_checkpoint': '', 'hardcore': False, 'splits': [
                    {'checkpoint_id': 'offline-contract-node', 'elapsed_ms': None, 'status': 'in_progress'}]}
            compare = {'configuration_id': ranking['configuration_id'], 'custom_competition_id': None,
                'route_sha256': case['route'], 'checkpoint_id': None, 'elapsed_ms': None, 'run_id': None, 'board': 'overall'}
            body = {'protocol': 'PAL98.TimerLive.v1', 'hwid': hwid, 'session_id': session['session_id'],
                    'session_token': session['session_token'], 'sequence': sequence, 'observation': observation, 'compare': compare}
            if mutate:
                mutate(body)
            return request('/live/updates', body)

        for original, fixed in zip(before, after):
            raw = original['ranking']['rules']
            unknown = {'wuqiang_plus', 'prebattle_slot_residue', 'challenge_config_sha256'} & raw.keys()
            old_status, old_reply = update(original)
            check(old_status == (422 if unknown else 200), 'Old actual contract reproduces exactly: ' + original['label'])
            new_status, ack = update(fixed)
            check(new_status == 200 and ack['sequence'] == sequence, 'Fixed full identity receives matching ACK: ' + fixed['label'])
            stored = json.loads(app.state.timer_store.db.execute('SELECT observation FROM timer_live_sessions WHERE public_id=?',
                                                               (int(session['public_id']),)).fetchone()[0])
            check(stored['maintenance_ranking'] == fixed['ranking'] and stored['configuration_id'] == fixed['ranking']['configuration_id'],
                  'Database retains the full exact identity and all configuration slots')
            check(not sdl_rules_issues(fixed['ranking']['rules']), 'Unchanged service accepts complete SDL fields')
            fields = fixed['ranking']['rules']
            if 'challenge_config_sha256' in raw:
                encoded_digest = fields['config.slot.RuntimeChallengeDigest']
                check(encoded_digest.startswith('sha256-') and len(encoded_digest) == 59, 'Digest fits the existing scalar grammar')
                decoded = base64.b32decode(encoded_digest[7:].upper() + '====').hex()
                check(decoded == raw['challenge_config_sha256'], 'All 256 original digest bits survive round trip')
            for old, new in [('wuqiang_plus', 'config.slot.RuntimeWuQiangPlus'),
                             ('prebattle_slot_residue', 'config.slot.RuntimePrebattleSlotResidue')]:
                check((fields.get(new) == raw[old]) if old in raw else new not in fields, 'Actual mode value survives')
            rows.append({'label':original['label'], 'before':old_status, 'after':new_status,
                'configuration_id':fixed['ranking']['configuration_id'], 'issues_before':sdl_rules_issues(raw),
                'full_configuration_persisted':True})

        for content in sorted({c['content'] for c in after}):
            ids = [c['ranking']['configuration_id'] for c in after if c['content'] == content and c['mode'] >= 0]
            check(len(ids) == len(set(ids)) == 8, 'Every mode combination retains a distinct configuration ID: ' + content)
        target = after[-1]
        status, _ = update(target, lambda body: body['compare'].update(configuration_id='0' * 64))
        check(status == 422, 'Comparison cannot switch to another configuration ID')
        status, _ = update(target, lambda body: body['observation']['maintenance_ranking'].update(configuration_id='0' * 64))
        check(status == 422, 'Tampered identity remains rejected')
        check(app.state.timer_store.db.execute('SELECT COUNT(*) FROM timer_runs').fetchone()[0] == 0, 'Heartbeat tests create zero score records')
        check(source_hashes == {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest()
                               for p in (root / 'src').rglob('*.py')}, 'Server source is unchanged')
        result = {'passed':True, 'checks':checks, 'cases':len(rows), 'contents':21,
                  'beforeRejected':sum(x['before'] == 422 for x in rows), 'afterAccepted':len(rows),
                  'serverSourceUnchanged':True, 'productionRequests':False, 'scorePosts':0, 'gameplay':False, 'rows':rows}
        (args.output / 'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),'utf-8')
        print(f'PASS {checks} real HTTP checks / {len(rows)} cases; old 422={result["beforeRejected"]}, fixed 200={len(rows)}; no score posts')
    finally:
        server.should_exit = True
        thread.join(10)
        listener.close()
        app.state.timer_store.db.close()
        app.state.store._db.close()


if __name__ == '__main__':
    main()
