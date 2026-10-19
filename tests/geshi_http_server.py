"""Isolated real HTTP integration: new SQLite, test publisher, loopback only.

No production URL, admin account, credentials or database is accepted. The C#
host uses a test-only observation, not a played game. Only sanitized results
are printed; private fixture credentials remain in the ignored output folder.
"""
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
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = args.output.resolve()
    artifacts = Path(__file__).resolve().parents[1] / 'artifacts'
    if not root.is_relative_to(artifacts) or root == artifacts:
        raise ValueError('Only a new child of this test checkout artifacts is allowed')
    database = root / 'fixture.sqlite3'
    if any(Path(str(database) + suffix).exists() for suffix in ('', '-wal', '-shm')):
        raise FileExistsError('Refusing existing fixture database')
    evidence = root / 'evidence'
    evidence.mkdir()
    sys.path.insert(0, str(args.server_root.resolve() / 'src'))
    import uvicorn
    from palserver.competition_service import create_competition_app
    from palserver.timer_build_candidates import PublisherRegistration
    from palserver.timer_online_auth import BuildRegistration
    from palserver.timer_online_protocol import ConfigurationPublication
    from palserver.timer_protocol import BindingIn

    app = create_competition_app(args.server_root.resolve(), database, sites=[])
    store = app.state.timer_store
    registration = json.loads((root / 'registration.public.json').read_text(encoding='utf-8-sig'))
    publisher = json.loads((root / 'publisher.public.json').read_text(encoding='utf-8-sig'))
    now = lambda: datetime.now(timezone.utc).isoformat()
    store.register_publisher(PublisherRegistration.model_validate({**publisher, 'reason':'LOCAL HTTP FIXTURE ONLY'}), now())
    store.register_build(BuildRegistration.model_validate(registration), now())
    checks = []

    def check(value, label):
        if not value:
            raise AssertionError(label)
        checks.append(label)

    def wait(predicate, label, seconds=35):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            if predicate():
                check(True, label)
                return
            if process is not None and process.poll() is not None:
                raise RuntimeError(f'Fixture client ended early at: {label}; see client.log')
            time.sleep(.08)
        raise TimeoutError(label)

    # Keep the bound socket through uvicorn startup; no free-port race or public
    # interface fallback. Disable proxy handling for these public fixture GETs.
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.bind(('127.0.0.1', 0))
    listener.listen(128)
    origin = f'http://127.0.0.1:{listener.getsockname()[1]}'
    http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def get(path):
        with http.open(origin + '/api/v1/timer/' + path, timeout=5) as response:
            check(response.status == 200, 'public HTTP 200: ' + path.split('?')[0])
            return json.load(response)

    service = uvicorn.Server(uvicorn.Config(app, host='127.0.0.1', port=listener.getsockname()[1],
                                           access_log=False, log_level='warning'))
    thread = threading.Thread(target=service.run, kwargs={'sockets':[listener]}, daemon=True)
    process = None
    transcript = (root / 'client.log').open('w', encoding='utf-8')
    try:
        thread.start()
        wait(lambda: service.started, 'production service started on loopback')
        process = subprocess.Popen([str(root / 'GeshiHttpProbe.exe'), str(evidence), origin], cwd=root,
                                   stdout=transcript, stderr=subprocess.STDOUT,
                                   creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        wait(lambda: (evidence / 'pending.fixture').exists(), 'native sealed result waiting for approval')
        with store.lock:
            check(store.db.execute('SELECT COUNT(*) FROM timer_runs').fetchone()[0] == 0,
                  'unapproved host sent no score')
        devices = store.admin_devices()['items']
        check(len(devices) == 1, 'real client registered one isolated device')
        store.bind(devices[0]['id'], BindingIn(display_name='LOCAL SDL FIXTURE'), now())
        store.approve_build(registration['key_id'], registration['exe_sha256'], True,
                            'LOCAL HTTP FIXTURE ONLY - never a release approval', now())
        (evidence / 'approved.fixture').write_text('LOCAL FIXTURE ONLY', encoding='utf-8')
        wait(lambda: (evidence / 'receipt.fixture').exists(), 'native request proof upload and retry completed')
        with store.lock:
            rows = store.db.execute('SELECT id,auth_verified,payload FROM timer_runs ORDER BY id').fetchall()
        check(len(rows) == 1 and rows[0]['auth_verified'], 'retry kept exactly one authenticated database row')
        run = json.loads(rows[0]['payload'])
        config = run['ranking']['configuration_id']
        decision = store.online_review(rows[0]['id'])
        check(run['protocol'] == 'PAL98.TimerOnline.v2' and run['gameplay']['family'] == 'sdlpal-geshi-easy',
              'actual HTTP payload preserved SDL family and v2 protocol')
        check(run['gameplay']['walk_frame_ms'] == 90 and run['gameplay']['fade_ms'] == 800,
              'actual HTTP payload preserved millisecond timing')
        check(decision['board_eligible'] and decision['status'] == 'automatic' and not decision['issues'],
              'complete synthetic test observation automatically eligible in fixture only')
        check(get('configurations')['items'] == [], 'unpublished configuration absent from public list')
        unpublished = get('standings?configuration_id=' + config)
        check(not unpublished['published'] and unpublished['entries'] == [], 'unpublished standings contain no score')
        store.publish(config, ConfigurationPublication(title='隔世追凶-简单 LOCAL FIXTURE', published=True,
                      reason='LOCAL HTTP FIXTURE ONLY'), now())
        published = get('configurations')['items']
        check(len(published) == 1 and published[0]['rules']['family'] == 'sdlpal-geshi-easy', 'published SDL category visible')
        boards = get('daily-boards')['items']
        check(len(boards) == 1, 'published daily board visible')
        before = get('standings?configuration_id=' + config)
        check(before['published'] and len(before['entries']) == 1, 'public daily standings contain accepted fixture')
        (evidence / 'published.fixture').write_text('LOCAL FIXTURE ONLY', encoding='utf-8')
        process.wait(timeout=45)
        check(process.returncode == 0, 'native client completed all HTTP scenarios')
        wire = json.loads((evidence / 'wire.json').read_text(encoding='utf-8-sig'))
        uploads = [call for call in wire if call['method'] == 'POST' and call['path'].endswith('/runs')]
        check([call['status'] for call in uploads] == [201, 200, 201], 'real HTTP statuses: create, idempotent retry, assisted create')
        check(uploads[0]['acknowledgementDropped'] and uploads[1]['receipt']['replayed'], 'lost acknowledgement recovered with replay receipt')
        check(uploads[0]['receipt']['run_id'] == uploads[1]['receipt']['run_id'], 'retry preserved run identity')
        assisted = uploads[2]['receipt']
        check(assisted['auth_verified'] and not assisted['board_eligible'] and assisted['daily_status'] == 'pending_review',
              'authenticated assisted run retained for review, not ranked')
        check('late_start' in assisted['review_issues'] and 'skipped_nodes' in assisted['review_issues'],
              'assisted review explains late start and skipped node')
        check(any(call['path'].endswith('/live/sessions') and call['status'] == 200 for call in wire),
              'native live challenge proof created real HTTP session')
        with store.lock:
            check(store.db.execute('SELECT COUNT(*) FROM timer_runs').fetchone()[0] == 3,
                  'two fixture results and one initialization persisted, despite retries')
        seed_receipt = json.loads((evidence / 'initialization' / 'initialization.receipt.json').read_text(encoding='utf-8-sig'))
        seed_config = seed_receipt['configuration_id']
        seed_review = store.online_review(seed_receipt['receipt_id'])
        check(seed_receipt['http_status'] == 200 and seed_review['status'] == 'pending' and not seed_review['board_eligible'],
              'initialization retry receives 200 and remains pending, never ranked')
        check({'zero_total', 'late_start', 'unfinished_observation', 'client_reported_issues'}.issubset(seed_review['issues']),
              'server independently excludes synthetic configuration initialization')
        store.publish(seed_config, ConfigurationPublication(title='隔世追凶-简单 CONFIG INIT FIXTURE', published=True,
                      reason='LOCAL HTTP FIXTURE ONLY'), now())
        seed_board = get('standings?configuration_id=' + seed_config)
        check(seed_board['published'] and seed_board['entries'] == [], 'publishing initialized configuration never publishes its seed score')
        after = get('standings?configuration_id=' + config)
        check(after['entries'] == before['entries'], 'assisted run excluded from public standings')
        summary = dict(schema='PalTimer.GeshiHttpAcceptance.v1', fixtureOnly=True, origin=origin,
                       frozenReleaseTested=False, productionServiceTested=False, checks=checks,
                       client=json.loads((evidence / 'client-result.json').read_text(encoding='utf-8-sig')),
                       uploadStatuses=[call['status'] for call in uploads], acceptedRows=1, reviewRows=2,
                       diagnosticGate='Not exercised by this observation host; covered by real-core SDL tests.',
                       hostSha256=hashlib.sha256((root / 'GeshiHttpProbe.exe').read_bytes()).hexdigest())
        (root / 'result.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(f'PASS {len(checks)} service checks; one ranked, one assisted and one initialization held for review. Local fixture only.')
    finally:
        if process is not None and process.poll() is None:
            # Only this newly created bounded test host, never a player's timer.
            process.terminate()
            process.wait(timeout=5)
        service.should_exit = True
        thread.join(timeout=10)
        transcript.close()
        listener.close()
        store.db.close()
        app.state.store._db.close()


if __name__ == '__main__':
    main()
