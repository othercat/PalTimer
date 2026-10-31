"""Refs #52: vary rules against ONE final Online DLL over disposable loopback HTTP."""
from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit, parse_qs
import argparse, copy, hashlib, json, shutil, subprocess, threading

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    p=argparse.ArgumentParser(description=__doc__)
    for name in ('runtime','csc','output'): p.add_argument('--'+name,required=True,type=Path)
    a=p.parse_args(); out=a.output.resolve(); runtime=a.runtime.resolve()
    assert any('/PALWork/artifacts/' + project + '-issue-' in out.as_posix() for project in ('PalTimer', 'so-lostedit'))
    out.mkdir(parents=True,exist_ok=False)
    product={n:digest(runtime/n) for n in ('Pal98Timer.exe','PalTimerOnline.dll')}
    host=out/'ChallengeRelayProduct.exe'
    with (out/'build.log').open('wb') as log:
        subprocess.run([str(a.csc),'/nologo','/target:exe','/platform:x64','/out:'+str(host),
            '/reference:System.dll','/reference:System.Core.dll',str(Path(__file__).with_name('challenge_relay_product.cs'))],stdout=log,stderr=subprocess.STDOUT,check=True)
    shutil.copy2(runtime/'Pal98Timer.exe.config',str(host)+'.config')
    identity=dict(bound=True,id='33333333-3333-4333-8333-333333333333',display_name='规则传输测试玩家')
    device='44444444-4444-4444-8444-444444444444'
    challenge='22222222-2222-4222-8222-222222222222'
    context={'mode':'14','calls':[]}
    package_text=' {"schema":"PAL.RulePackage.v2","integer":90071992547409931234567,"parameters":{"steps":[1,15,30]}}\n'
    package_sha=hashlib.sha256(package_text.encode()).hexdigest()

    def docs():
        mode=context['mode']; count=int(mode) if mode.isdecimal() else 15
        definitions=[dict(key=f'web_rule_{i}',name=f'网页规则{i}',description='由配置上传定义的规则',
                          default_value=False,parameter_schema={'type':'boolean'}) for i in range(count)]
        values={d['key']:True for d in definitions}
        rules={'schema_version':1,'values':values}
        if mode in ('structured','future','grouped','array_rules'):
            rules={'schema_version':2 if mode=='structured' else 99,'values':{
                'web_rule_0':{'enabled':True,'quantities':[1,15,30],'labels':['最大数量','最大金额'],
                              'conditions':[{'kind':'inventory','range':{'min':0,'max':99}}],
                              'large_integer':9007199254740993},'web_rule_1':None},
                   'future_metadata':{'original_json':package_text,'sha256':package_sha}}
            definitions[0].update(parameter_schema={'type':'object','properties':{'enabled':{'type':'boolean'},'quantities':{'type':'array','items':{'type':'integer'}}}},default_value={'enabled':False,'quantities':[1]})
        if mode=='grouped': definitions={'schema':'PAL.RuleDirectory.v2','groups':[{'id':'shop','children':definitions}]}
        if mode=='array_rules': rules=[{'schema_version':3,'parameters':rules}]
        item=dict(id=challenge,author_id='11111111-1111-4111-8111-111111111111',author_name='规则作者',
            title='动态规则挑战',revision=1,latest_revision=1,rules_revision=1,approval_status='approved',
            contents=['pal98-classic-v6'],rules=rules,rule_package_json=package_text,rule_package_sha256=package_sha)
        return item,definitions

    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args): pass
        def do_GET(self): self.respond()
        def do_POST(self): self.respond()
        def respond(self):
            length=int(self.headers.get('Content-Length','0')); body=json.loads(self.rfile.read(length)) if length else None
            path=urlsplit(self.path); mode=context['mode']; context['calls'].append({'method':self.command,'path':path.path})
            item,definitions=docs(); value={'protocol':'PAL.Challenges.v1','as_of':'2026-10-30T00:00:00Z'}
            if path.path.endswith('/devices'):
                value.update(protocol='PAL98.TimerOnline.v1',device_id=device,player=identity)
            elif '/catalog' in path.path:
                value.update(items=[item],total=1,next_offset=None,rules=definitions,rules_revision=1,
                    contents={'pal98-classic-v6':'98柔情经典速通v6'},heroes={'li-xiaoyao':'李逍遥'},
                    rule_package={'schema':'PAL.RulePackage.v2','body':package_text,'sha256':package_sha})
                if mode=='bad_rules': value['items'][0]['rules']=42
                if mode=='bad_directory': value['rules']='not-a-container'
                if mode=='bad_protocol': value['protocol']='unrelated.protocol'
                if mode=='page_change':
                    offset=int(parse_qs(path.query).get('offset',['0'])[0]); value.update(total=2,next_offset=1 if offset==0 else None,rules_revision=1+offset)
                    if offset: value['items'][0]['id']='55555555-5555-4555-8555-555555555555'
            elif path.path.endswith('/identity'): value.update(status='bound',player=identity,requested_name=None,refresh_interval_seconds=60)
            elif path.path.endswith('/enrollments'): value['items']=[item]
            elif path.path.endswith('/enroll'):
                assert 'content_id' not in body and 'cover_version' not in body
                value.update(player=identity,challenge=item)
            elif path.path.endswith('/activate'):
                assert body['revision']==1 and body['player_id']==identity['id']
                assert body['capabilities']=={'rule_formats':[1,2,99],'engine':{'advanced_trade':True}}
                assert body['cover_version']==(2 if mode in ('structured','future','grouped','array_rules') else 0)
                assert body['future_option']=={'new_parameter':['unchanged',7]} and 'action' not in body and 'challenge_id' not in body
                value.update(player=copy.deepcopy(identity),challenge=item,device_id=device,content_id='pal98-classic-v6',locked=True,
                             ini_filename='challenge-rules.ini',ini='[Challenge]\nSchemaVersion=1\n',
                             output_package={'schema':'PAL.GameRuleFiles.v2','document':package_text},received_options=body)
                if mode=='wrong_device': value['device_id']='55555555-5555-4555-8555-555555555555'
                if mode=='wrong_player': value['player']['id']='55555555-5555-4555-8555-555555555555'
                if mode=='wrong_name': value['player']['display_name']='其他玩家'
                if mode=='wrong_challenge': value['challenge']['id']='55555555-5555-4555-8555-555555555555'
                if mode=='stale': value['challenge']['latest_revision']=2
                if mode=='not_approved': value['challenge']['approval_status']='pending'
                if mode=='wrong_content': value['content_id']='pal-dos'
                if mode=='unlocked': value['locked']=False
                if mode=='string_bound': value['player']['bound']='true'
            else: raise AssertionError(path.path)
            data=json.dumps(value,ensure_ascii=False).encode('utf-8')
            if '/catalog' in path.path:
                if mode=='duplicate_json': data=b'{"protocol":"PAL.Challenges.v1","protocol":"PAL.Challenges.v1"}'
                if mode=='trailing_json': data+=b' trailing'
                if mode=='invalid_utf8': data=b'\xff'
                if mode=='oversized': data=b' '*(1048576+1)
            self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(data))); self.end_headers()
            try: self.wfile.write(data)
            except (BrokenPipeError,ConnectionResetError): pass

    server=ThreadingHTTPServer(('127.0.0.1',0),Handler); thread=threading.Thread(target=server.serve_forever,daemon=True); thread.start()
    origin=f'http://127.0.0.1:{server.server_port}'
    checks=[]
    def check(ok,label):
        assert ok,label
        checks.append(label)
    def setup(name):
        root=out/name; (root/'patches').mkdir(parents=True); (root/'palmod').mkdir()
        (root/'config.ini').write_text('[Patch]\nDefaultPatch=pal98-classic-v6\n',encoding='ascii')
        (root/'palmod/common-tools.v1.json').write_text(json.dumps({'schema':'PAL98.ToolLaunchSettings.v1','competition_upload':{
            'schema':'PAL98.TimerOnlineSettings.v1','xiaorou':{'enabled':False,'server':origin}}}),encoding='utf-8')
        return root
    def call(root,action):
        request={'action':action}
        if action in ('activate','enroll'): request.update(challenge_id=challenge,revision=1,player_id=identity['id'],display_name=identity['display_name'])
        if action=='enroll': request.update(content_id='pal98-classic-v6',cover_version=2)
        if action=='activate': request.update(content_id='pal98-classic-v6',
            cover_version=2 if context['mode'] in ('structured','future','grouped','array_rules') else 0,
            capabilities={'rule_formats':[1,2,99],'engine':{'advanced_trade':True}},future_option={'new_parameter':['unchanged',7]})
        req=root/'request.json'; resp=root/'response.json'; req.write_text(json.dumps(request),encoding='utf-8')
        with (root/(action+'-'+context['mode']+'.log')).open('wb') as log:
            subprocess.run([str(host),str(runtime/'PalTimerOnline.dll'),str(root),str(req),str(resp)],stdout=log,stderr=subprocess.STDOUT,timeout=40,check=True)
        reply=json.loads(resp.read_text(encoding='utf-8'))
        check(reply.get('protocol')=='PAL.ChallengeBridge.v2',context['mode']+' '+action+' explicitly carries bridge protocol')
        return reply
    try:
        for mode in ('14','15','64','256','structured','future','grouped','array_rules'):
            context['mode']=mode; root=setup('rules-'+mode); expected,definitions=docs()
            reply=call(root,'refresh'); check(reply['ok'],mode+' actual product refresh succeeds')
            cache=json.loads((root/'palmod/challenges.v1.json').read_text(encoding='utf-8'))
            check(cache['items'][0]==expected and cache['rules']==definitions,mode+' rule fields, order and nested values survive actual cache')
            check(cache['catalog_metadata']['rule_package']['body']==package_text,mode+' original package JSON text survives byte-for-byte')
            check(call(root,'enroll')['ok'],mode+' enrollment succeeds')
            reply=call(root,'activate'); check(reply['ok'] and reply['activation']['challenge']==expected,mode+' activation retains exact rule document')
            check(reply['activation']['received_options']['future_option']=={'new_parameter':['unchanged',7]},mode+' interpreter-owned capabilities and version options forwarded')
            check(reply['activation']['output_package']['document']==package_text,mode+' structured output is relayed without interpreting or applying it')
            check(not (root/'challenge-rules.ini').exists(),mode+' timer does not apply game configuration')
        context['mode']='14'; root=setup('envelope-rejections'); check(call(root,'refresh')['ok'],'baseline cache created')
        before=digest(root/'palmod/challenges.v1.json')
        for mode in ('wrong_device','wrong_player','wrong_name','wrong_challenge','stale','not_approved','wrong_content','unlocked','string_bound'):
            context['mode']=mode; reply=call(root,'activate')
            check(not reply['ok'] and reply['activation'] is None and digest(root/'palmod/challenges.v1.json')==before,mode+' rejected without replacing baseline cache')
        for mode in ('bad_rules','bad_directory','bad_protocol','page_change','duplicate_json','trailing_json','invalid_utf8','oversized'):
            context['mode']=mode; reply=call(root,'refresh')
            check(not reply['ok'] and reply['code']!='network_unavailable' and digest(root/'palmod/challenges.v1.json')==before,mode+' malformed envelope rejected with baseline cache intact')
        check(all(digest(runtime/n)==v for n,v in product.items()),'same final EXE and Online DLL used for all rule counts and structures')
    finally:
        server.shutdown(); server.server_close(); thread.join(5)
    check(not thread.is_alive(),'loopback fixture stopped')
    result=dict(passed=True,checks=len(checks),assertions=checks,productSha256=product,
        actualProductDll=True,perRuleDtoAbsentInProduct=True,ruleCounts=[14,15,64,256],nestedRulesVersions=[2,99],
        groupedDirectoryPreserved=True,arrayRuleDocumentPreserved=True,interpreterOptionsForwarded=True,explicitBridgeProtocol=True,
        originalRulePackageStringPreserved=True,gameConfigurationApplied=False,
        loopbackServerStopped=True,productionDataUsed=False,productionNetworkUsed=False)
    (out/'summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('PASS',len(checks),'actual product DLL checks; same binaries, loopback stopped')

if __name__=='__main__': main()
