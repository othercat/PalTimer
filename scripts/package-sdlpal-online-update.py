"""Refs #60 / othercat/sdlpal-lostedit#250: Online-only r108 over sealed r107."""
from datetime import datetime, timezone
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

BASE = 'sdlpal-lostedit-release-r107-20261104'
NAME = 'sdlpal-lostedit-release-r108-20261105'
BASE_SHA = '705c16e5657517b527111066e7c23276e3badf33014459136d1263f91e38a737'
RULES_SHA = 'f139bccba8300f75645114e7437f84ddc45a6f75bbea181ef83d02acfea22d47'
UPDATE = 'PalTimer-3.38.6.9-Online1.0.2.9-update'
SKIP_SOURCE_DIRS = {'.git','.vs','.vscode','.idea','.ai','.claude','.codex','.agents','bin','obj','__pycache__',
                    'node_modules','.venv','packages','artifacts','dist'}
SKIP_SOURCE_SUFFIXES = {'.pfx','.p12','.key','.pem','.snk','.sqlite','.sqlite3','.db','.pyc','.pdb','.log'}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def load(path):
    return json.loads(path.read_text('utf-8-sig'))


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def inventory(root):
    return {p.relative_to(root).as_posix():digest(p.read_bytes()) for p in sorted(root.rglob('*')) if p.is_file()}


def zip_files(path, files, prefix=''):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(prefix + name, (2026, 11, 5, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data, compresslevel=6)
    with zipfile.ZipFile(path) as archive:
        assert set(archive.namelist()) == {prefix+n for n in files}
        assert all(archive.read(prefix+n) == data for n,data in files.items())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=['prepare','assemble','seal'])
    parser.add_argument('--palwork', type=Path, required=True)
    parser.add_argument('--timer-bin', type=Path, required=True)
    parser.add_argument('--validation', type=Path)
    parser.add_argument('--approval', type=Path)
    parser.add_argument('--replace-candidate', action='store_true', help='Correct only this task\'s locally sealed r108 candidate; preserve its prior identity in evidence.')
    args = parser.parse_args()
    source = Path(__file__).resolve().parents[1]
    work = args.palwork.resolve()
    release = work/'releases/so-lostedit'
    evidence = work/'artifacts/so-lostedit-issue-250/r108/package'
    out, archive = release/NAME, release/(NAME+'.zip')
    baseline = release/(BASE+'.zip')
    assert digest(baseline.read_bytes()) == BASE_SHA
    replacement = None
    if args.replace_candidate:
        replacement = load(evidence/'completion.json')
        assert replacement['release']==NAME and digest(archive.read_bytes())==replacement['zipSha256']
        index = load(release/'release-index.json')
        assert index['highestRevision']==108 and index['recommendedDirectory']==NAME
        assert next(row for row in index['releases'] if row['revision']==108)['zipSha256']==replacement['zipSha256']
    else:
        assert not archive.exists()
    if args.phase == 'prepare':
        assert not out.exists() and not evidence.exists()
        assert load(release/'release-index.json')['highestRevision'] == 107
        out.mkdir(); evidence.mkdir(parents=True)
        before = {}
        with zipfile.ZipFile(baseline) as z:
            assert all(n.startswith(BASE+'/') for n in z.namelist())
            for info in z.infolist():
                if info.is_dir(): continue
                name = info.filename[len(BASE)+1:]
                assert name and '..' not in Path(name).parts
                data = z.read(info); target=out/name; target.parent.mkdir(parents=True,exist_ok=True); target.write_bytes(data)
                before[name] = digest(data)
        assert len(before) == 306 and inventory(out) == before
        save(evidence/'baseline.json', {'zipSha256':BASE_SHA,'files':before})
        for name in ['release-index.json','README.txt','SHA256SUMS.txt']:
            (evidence/('before-'+name)).write_bytes((release/name).read_bytes())
        print('Prepared sealed r107 baseline:', len(before), 'files')
        return
    assert out.is_dir()
    before = load(evidence/'baseline.json')['files']
    allowed = set()

    def deliver(name, data):
        target=out/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data);allowed.add(name)

    manifest = load(out/'package-manifest.json')
    registration = load(args.timer_bin/'PalCompetitionRegistration.public.json')
    build = registration['build']
    assert build['timer_version']=='3.38.6.9' and build['online_component_version']=='1.0.2.9'
    assert digest((out/'Tools/Timer/Pal98Timer.exe').read_bytes()) == build['exe_sha256'] == before['Tools/Timer/Pal98Timer.exe']
    for file, field in [('PalTimerOnline.dll','online_component_sha256'),('PalCompetitionAuth.dll','component_sha256')]:
        data=(args.timer_bin/file).read_bytes();assert digest(data)==build[field];deliver('Tools/Timer/'+file,data)
    deliver('Tools/Timer/PalCompetitionRegistration.public.json',(args.timer_bin/'PalCompetitionRegistration.public.json').read_bytes())

    names=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=source).decode('utf-8').split('\0')
    excluded = [n for n in names if n and (any(p.casefold() in SKIP_SOURCE_DIRS or 'private' in p.casefold() for p in Path(n).parts[:-1]) or
        Path(n).suffix.casefold() in SKIP_SOURCE_SUFFIXES or Path(n).name.casefold().startswith(('.env','credentials','secrets')) or n.endswith('.device'))]
    files={n:(source/n).read_bytes() for n in names if n and n!='SOURCE-STATE.json' and n not in excluded and (source/n).is_file()}
    assert not any(set(Path(n).parts)&SKIP_SOURCE_DIRS or n.endswith('.device') for n in files)
    with zipfile.ZipFile(release/BASE/'Sources/PalTimer-source.zip') as z:
        prior={n.removeprefix('PalTimer/'):z.read(n) for n in z.namelist() if not n.endswith('/') and not n.endswith('/SOURCE-STATE.json')}
    state={'schema':'PAL.MaintenanceSource.v1','repository':'othercat/PalTimer','issues':[60],
        'branch':subprocess.check_output(['git','branch','--show-current'],cwd=source,text=True).strip(),
        'checkoutHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=source,text=True).strip(),
        'targetBranch':'master','baseRelease':BASE,'sourceCommitted':False,'implementationCommit':None,'mergeCommit':None,
        'files':{n:digest(data) for n,data in files.items()},'excludedFiles':excluded,'sourceDeltaFromR107':{
            n:{'before':digest(prior[n]) if n in prior else None,'after':digest(files[n]) if n in files else None}
            for n in sorted(prior.keys()|files.keys()) if prior.get(n)!=files.get(n)}}
    save(evidence/'PalTimer-source-state.json',state)
    source_archive=evidence/'PalTimer-source.zip'
    zip_files(source_archive,dict(files,**{'SOURCE-STATE.json':(json.dumps(state,ensure_ascii=False,indent=2)+'\n').encode('utf-8')}),'PalTimer/')
    deliver('Sources/PalTimer-source.zip',source_archive.read_bytes())
    source_info={'archive':'PalTimer-source.zip','sha256':digest(source_archive.read_bytes()),'fileCount':len(files),
        'branch':state['branch'],'checkoutHead':state['checkoutHead'],'targetBranch':'master','sourceCommitted':False}
    manifest['sources']['PalTimer']=source_info
    for name in ['timer','timerOnline']:
        item=manifest['components'][name]
        item.update(sourceArchiveSha256=source_info['sha256'],sourceCommitted=False,implementationCommit=None,mergeCommit=None)
        if name=='timerOnline':item.update(sha256=build['online_component_sha256'],version='1.0.2.9',releaseRevision='r108',baseRelease=BASE,commit=None)
    for key in ['timerAuth','timerRegistration']:
        item=manifest['components'][key]
        item.update(sha256=digest((out/item['path']).read_bytes()),releaseRevision='r108',baseRelease=BASE,commit=None,
                    buildInputs={'timerExeSha256':build['exe_sha256'],'onlineSha256':build['online_component_sha256']},publisherPreserved=True)
    approved=False
    if args.approval:
        status=load(args.approval)
        assert status['keyId']==build['key_id'] and status['exeSha256']==build['exe_sha256'] and status['originMatches'] and status['identityMatches']
        approved=status['approved'] is True
    manifest.update(release=NAME,revision=108,created=datetime.now(timezone.utc).isoformat(),baseRelease=BASE,baseZipSha256=BASE_SHA,
        issue=250,maintenanceIssue=250,parentIssue=115,maintenanceIssues=[250],acceptanceStatus='Client fix verified; real connection/player acceptance pending',
        gameplayExecuted=False,onlineApproval='Exact Online1.0.2.9 composition approved' if approved else 'Exact new composition pending approval',
        serverDeployment='No server update required for r108 client compatibility; deployed service untouched')
    manifest['includedFixIssues']=sorted(set(manifest['includedFixIssues'])|{250})
    if 'othercat/PalTimer#60' not in manifest['relatedComponentIssues']:manifest['relatedComponentIssues'].append('othercat/PalTimer#60')
    manifest['components']['timerOnline']['approved']=approved
    for key in ['timerAuth','timerRegistration']:
        manifest['components'][key].update(approved=approved,keyId=build['key_id'],
            approvalStatus='Exact composition approved by user; status read back' if approved else 'Formally signed; exact composition pending approval')
    manifest['integration'].update(issue=250,parentIssue=115,branch=state['branch'],targetBranch='master',baseRelease=BASE,
        sourceCommitted=False,implementationCommit=None,mergeCommit=None,productionDeployed=False)
    manifest['r108']={'scope':'Online-only client compatibility','engineReusedFrom':'r107','timerExeReusedFrom':'r107',
        'onlineVersion':'1.0.2.9','keyId':build['key_id'],'newFactsPreserved':True,'completeConfigurationPreserved':True,
        'serverModified':False,'historicalOutboxRewritten':False,'localBestIdentityChanged':False,'gameplayAcceptance':'pending'}
    guide=('r108 计时器连接修复（完整继承r107）\n\n'
        '修复速通v6开启甄吴强后先连接、随后连接失败；同样覆盖DOS/其他内容的状态残留、甄吴强及新规则组合。\n'
        '这三项信息完整保留在现有可扩展配置字段中，规则摘要可无损还原。所有41规则、优先级和来源继续上传并参与配置ID。\n'
        '心跳、比较和新成绩签封统一使用完整身份，不退回空配置连接。游戏和计时器EXE原字节、本地最佳线、历史签封和存档保持。\n'
        'Online为1.0.2.9，Auth/公开登记配套；计时器EXE仍3.38.6.9，忽爷云激活MD5不变。\n'
        '无需修改或部署PalServer。自然游玩和比赛验收由玩家完成。\n'
        '服务器可上传的最新规则仍为Sources/challenge-rules.v2.json：revision12，41规则，21内容。\n')
    guide += '本次正式组件组合已由用户批准，状态接口已读回确认。\n' if approved else '本次新组件组合仍待管理员批准；批准前本地计时正常。\n'
    deliver('docs/r108-update.txt',guide.encode('utf-8'))
    deliver('docs/SDLPAL_ONLINE_CONFIGURATION.md',(source/'docs/SDLPAL_ONLINE_CONFIGURATION.md').read_bytes())
    deliver('使用说明.txt',(guide+'\n\n').encode('utf-8')+(release/BASE/'使用说明.txt').read_bytes())
    deliver('docs/README.md',('# 使用说明\n\n当前说明：[r108计时器连接修复](r108-update.txt)。\n\n').encode('utf-8')+(release/BASE/'docs/README.md').read_bytes())
    deliver('Sources/README.txt',('r108仅更新计时器Online1.0.2.9及匹配Auth/公开登记，Timer3.38.6.9 EXE和引擎r107原字节继承。\n'
        'PalTimer精确源码、SOURCE-STATE及配置协议说明随包；其余源码及最新可上传规则继承r107。无服务器代码变更、提交、推送或部署。\n\n').encode('utf-8')+(release/BASE/'Sources/README.txt').read_bytes())
    deliver('Sources/PalTimer-README.txt',('EXE3.38.6.9（r107原字节）/Online1.0.2.9；成套Auth/公开登记。\n'
        '修复SDLPAL新运行字段的固定顶层合同冲突，完整保留模式、规则摘要及41槽和优先级；无需服务端修改。\n'
        '本地/PB、历史签封保持，源码未提交。'+('本次组合已由用户批准并读回确认。\n' if approved else '本次新组合待管理员独立批准。\n')).encode('utf-8'))
    deliver('release-version.txt',b'r108\n')
    assert digest((out/'Sources/challenge-rules.v2.json').read_bytes()) == RULES_SHA
    if args.phase=='seal':
        validation=load(args.validation)
        assert validation['passed'] and validation['contents']==21 and not validation['gameplayExecuted']
        assert validation['componentHashes']=={n:digest((out/n).read_bytes()) for n in validation['componentHashes']}
        assert validation['sourceArchiveSha256']==source_info['sha256']
        assert approved and validation['actualConnectionVerified']
        manifest['acceptanceStatus']='Client and actual connection verified; player gameplay acceptance pending'
        manifest['validationEvidence']=[{'path':str(args.validation.relative_to(work/'artifacts')).replace('\\','/'),'sha256':digest(args.validation.read_bytes())}]
    manifest['files']=[{'path':n,'bytes':(out/n).stat().st_size,'sha256':v} for n,v in inventory(out).items() if n not in ('package-manifest.json','SHA256SUMS.txt')]
    save(out/'package-manifest.json',manifest);allowed.add('package-manifest.json')
    deliver('SHA256SUMS.txt',''.join(h+'  '+n+'\n' for n,h in inventory(out).items() if n!='SHA256SUMS.txt').encode('utf-8'))
    final=inventory(out)
    changed={n for n in before if final.get(n)!=before[n]}
    assert before.keys() <= final.keys() and changed <= allowed
    assert all(final[n]==v for n,v in before.items() if n not in allowed)
    for item in manifest['components'].values():
        if item.get('path') and item.get('sha256'):assert final[item['path']]==item['sha256']
    save(evidence/'prepared.json',{'files':len(final),'changed':sorted(changed),'added':sorted(final.keys()-before.keys()),'sourceDelta':state['sourceDeltaFromR107']})
    update_files={n:(out/n).read_bytes() for n in ['Tools/Timer/PalTimerOnline.dll','Tools/Timer/PalCompetitionAuth.dll','Tools/Timer/PalCompetitionRegistration.public.json']}
    update_files['r107-online-update.txt']=(guide+'\n安装：关闭计时器后，在r107完整包根目录解压覆盖Tools中的三份配套文件。\n'
        '请勿覆盖个人配置、存档、时间线或设备凭据。安装前备份旧三件套，回退时一起恢复。\n'
        '必须配套r107的Pal98Timer.exe，SHA256 '+build['exe_sha256']+'。\n').encode('utf-8')
    update_files['update-manifest.json']=(json.dumps({'baseExeSha256':build['exe_sha256'],'keyId':build['key_id'],
        'files':{n:digest(data) for n,data in update_files.items()},'serverChanged':False,'exeChanged':False},ensure_ascii=False,indent=2)+'\n').encode('utf-8')
    update_path=evidence.parent/(UPDATE+'.zip')
    zip_files(update_path,update_files)
    (evidence.parent/(UPDATE+'.sha256')).write_text(digest(update_path.read_bytes())+'  '+update_path.name+'\n','ascii')
    if args.phase!='seal':
        print('Assembled r108:',len(final),'files; standalone update verified:',len(update_files),'files')
        return
    if not replacement:
        for name in ['release-index.json','README.txt','SHA256SUMS.txt']:
            assert (release/name).read_bytes()==(evidence/('before-'+name)).read_bytes(),'Concurrent release index update'
    zip_files(archive,{n:(out/n).read_bytes() for n in final},NAME+'/')
    zipped=digest(archive.read_bytes())
    (release/(NAME+'.sha256')).write_text(zipped+'  '+archive.name+'\n','ascii')
    index=load(release/'release-index.json');assert index['highestRevision']==(108 if replacement else 107)
    for row in index['releases']:row['recommended']=False
    row={'revision':108,'date':'20261105','directory':NAME,'label':'计时器完整配置心跳兼容修复',
        'status':'connection-verified-player-gameplay-acceptance-pending','recommended':True,'files':len(final),'zipBytes':archive.stat().st_size,'zipSha256':zipped,'baseRelease':BASE}
    if replacement:
        index['releases']=[row if item['revision']==108 else item for item in index['releases']]
        save(evidence/'candidate-source-filter-correction.json',{'previousZipSha256':replacement['zipSha256'],
            'previousZipBytes':replacement['zipBytes'],'finalZipSha256':zipped,'reason':'Exclude development/tool configuration directories from source archive; binaries unchanged'})
    else:index['releases'].append(row)
    index.update(highestRevision=108,nextRevision=109,recommendedDirectory=NAME,recommendedEntry=str(out/'pal.exe'),recommendedZip=str(archive))
    save(release/'release-index.json',index)
    (release/'README.txt').write_text('当前推荐r108，计时器连接修复；游戏内容完整继承r107。\n'+str(out)+'\n'+str(archive)+'\nSHA256 '+zipped+'\n\n'+(evidence/'before-README.txt').read_text('utf-8-sig'),'utf-8')
    (release/'SHA256SUMS.txt').write_text(''.join(digest(p.read_bytes())+'  '+p.name+'\n' for p in sorted(release.glob('*.zip'))),'ascii')
    assert digest(baseline.read_bytes())==BASE_SHA
    save(evidence/'completion.json',{'passed':True,'release':NAME,'files':len(final),'zipBytes':archive.stat().st_size,'zipSha256':zipped,
        'baseZipSha256':BASE_SHA,'changed':sorted(changed),'added':sorted(final.keys()-before.keys()),'inheritedUnchangedFiles':len(before)-len(changed),
        'updateZipSha256':digest(update_path.read_bytes()),'gameplayExecuted':False,'serverModified':False,'sourceCommitted':False})
    print('Sealed r108:',len(final),'files; SHA256',zipped)


if __name__=='__main__':
    main()
