"""Disposable PalServer + actual compiled PalTimer HTTP contract host, loopback only."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import socket
import subprocess
import sys
import threading
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = args.output.resolve()
    allowed = [Path(r'C:\Workspace\PALWork\artifacts\PalTimer-issue-51\r84').resolve(),
               Path(r'C:\Workspace\PALWork\artifacts\PalTimer-issue-52').resolve(),
               Path(r'C:\Workspace\PALWork\artifacts\so-lostedit-issue-189').resolve()]
    if not any(root.is_relative_to(p) and root != p for p in allowed):
        raise ValueError('Use a new run directory under the authorized Issue 51/52 evidence root.')
    database = root / 'fixture.sqlite3'
    if any(Path(str(database) + tail).exists() for tail in ('', '-wal', '-shm')):
        raise FileExistsError('Refusing an existing fixture database.')
    client = root / 'client/ChallengeBehavior.exe'
    if not client.is_file():
        raise FileNotFoundError('Build the test host using -BuildOnly first.')
    data = root / 'client/data'
    if data.exists():
        raise FileExistsError('Refusing pre-existing client fixture data.')
    source = args.server_root.resolve() / 'src'
    sys.path.insert(0, str(source))
    import uvicorn
    from starlette.responses import Response
    from palserver.competition_service import create_competition_app
    from palserver.shougansai_admin import hash_password
    local_repo = root / 'service-root'
    local_repo.mkdir()
    app = create_competition_app(local_repo, database, sites=[], enable_docs=False,
                                 admin_password_hash=hash_password('fixture-only-password'))
    wire = []

    class FixtureHost:
        async def __call__(self, scope, receive, send):
            if scope['type'] != 'http':
                return await app(scope, receive, send)
            mode_file = data / 'http-fault-mode.fixture'
            mode = mode_file.read_text('utf-8') if mode_file.exists() else ''
            row = {'method': scope['method'], 'path': scope['path']}
            captured = bytearray()

            async def capture(message):
                if message['type'] == 'http.response.start':
                    row['status'] = message['status']
                elif message['type'] == 'http.response.body' and len(captured) < 65536:
                    captured.extend(message.get('body', b'')[:65536-len(captured)])
                await send(message)

            try:
                if mode and scope['path'].startswith('/api/v1/timer/'):
                    row['fixtureFault'] = mode
                    if mode.isdigit():
                        response = Response(b'{"detail":"LOCAL FIXTURE RESPONSE"}', status_code=int(mode), media_type='application/json')
                    elif mode == 'oversized':
                        response = Response(b'x' * 1048577, media_type='application/json')
                    elif mode == 'invalid-utf8':
                        response = Response(b'\xff\xfe', media_type='application/json')
                    elif mode == 'invalid-json':
                        response = Response(b'{not json}', media_type='application/json')
                    else:
                        raise ValueError('Unknown local fixture mode')
                    await response(scope, receive, capture)
                else:
                    await app(scope, receive, capture)
            finally:
                try:
                    value = json.loads(captured)
                    for key in ('code', 'protocol', 'rules_revision', 'updated_challenges', 'approval_status'):
                        if key in value:
                            row[key] = value[key]
                    if isinstance(value.get('challenge'), dict):
                        row['challengeRevision'] = value['challenge'].get('revision')
                except (ValueError, UnicodeError):
                    pass
                wire.append(row)

    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.bind(('127.0.0.1', 0))
    listener.listen(128)
    port = listener.getsockname()[1]
    origin = f'http://127.0.0.1:{port}'
    service = uvicorn.Server(uvicorn.Config(FixtureHost(), host='127.0.0.1', port=port,
                            access_log=False, log_level='warning', proxy_headers=False))
    thread = threading.Thread(target=service.run, kwargs={'sockets': [listener]}, daemon=True)
    process = None
    checks = []

    def check(value, label):
        if not value:
            raise AssertionError(label)
        checks.append(label)

    try:
        thread.start()
        deadline = time.monotonic() + 10
        while not service.started:
            if not thread.is_alive() or time.monotonic() > deadline:
                raise TimeoutError('Local fixture server startup failed.')
            time.sleep(.025)
        check(True, 'production application listening only on loopback with an independent database')
        with (root / 'client.log').open('wb') as log:
            process = subprocess.Popen([str(client), str(data), origin], cwd=client.parent,
                       stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
            deadline = time.monotonic() + 90
            stopped = False
            while process.poll() is None:
                if (data / 'stop-http.fixture').exists() and not stopped:
                    service.should_exit = True
                    thread.join(10)
                    check(not thread.is_alive(), 'uvicorn service thread stopped before no-response test')
                    listener.close()
                    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
                        probe.settimeout(.2)
                        check(probe.connect_ex(('127.0.0.1', port)) != 0, 'loopback port is closed for genuine transport failure')
                    (data / 'http-stopped.fixture').write_text('local service and port stopped', 'utf-8')
                    stopped = True
                if time.monotonic() > deadline:
                    raise TimeoutError('Challenge HTTP client did not complete.')
                time.sleep(.025)
            check(process.returncode == 0, 'actual C# client contract checks passed; see client.log')
            check(stopped, 'client reached genuine offline no-response scenario')
        errors = [row.get('code') for row in wire if row.get('status') == 409]
        check('approval_pending' in errors, 'real HTTP reports challenge approval_pending')
        check(errors.count('revision_outdated') >= 4, 'real HTTP refuses stale activation after edit and rule upload')
        for status in (401, 403, 404, 408, 409, 422, 429, 500, 503):
            check(any(row.get('fixtureFault') == str(status) and row.get('status') == status for row in wire), f'actual HTTP response {status} exercised')
        for mode in ('oversized', 'invalid-utf8', 'invalid-json'):
            check(any(row.get('fixtureFault') == mode and row.get('status') == 200 for row in wire), 'received malformed HTTP body exercised: ' + mode)
        db = app.state.timer_store.db
        with app.state.timer_store.lock:
            facts = {'devices': db.execute('SELECT COUNT(*) FROM timer_devices').fetchone()[0],
                     'players': db.execute('SELECT COUNT(*) FROM timer_players').fetchone()[0],
                     'challenges': db.execute('SELECT COUNT(*) FROM pal_challenges').fetchone()[0],
                     'revisions': db.execute('SELECT COUNT(*) FROM pal_challenge_revisions').fetchone()[0],
                     'approvedApplications': db.execute("SELECT COUNT(*) FROM pal_challenge_approvals WHERE status='approved'").fetchone()[0]}
        check(facts == {'devices': 1, 'players': 1, 'challenges': 2, 'revisions': 5, 'approvedApplications': 1}, 'isolated database reflects expected approval and all revision transitions')
        summary = {'passed': True, 'at': datetime.now(timezone.utc).isoformat(), 'origin': origin,
                   'database': str(database), 'clientPid': process.pid, 'clientExited': True,
                   'serviceThreadExited': not thread.is_alive(), 'portClosed': True,
                   'fixtureDatabaseFacts': facts, 'driverChecks': checks,
                   'clientSha256': hashlib.sha256(client.read_bytes()).hexdigest(),
                   'productionDataUsed': False, 'productionNetworkUsed': False}
        (root / 'result.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), 'utf-8')
        print('PASS', len(checks), 'local HTTP driver checks; C# assertions in client.log; service and client stopped.')
        return 0
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            process.wait(timeout=5)
        service.should_exit = True
        thread.join(10)
        listener.close()
        (root / 'wire.json').write_text(json.dumps(wire, ensure_ascii=False, indent=2), 'utf-8')
        app.state.timer_store.db.close()
        app.state.store._db.close()


if __name__ == '__main__':
    raise SystemExit(main())
