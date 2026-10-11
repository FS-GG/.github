import hashlib, json, os, pathlib, re, stat, subprocess, sys, sysconfig, signal, select, time
def require(ok, reason):
    if not ok: raise RuntimeError('custody-core-toolchain-refused: '+reason)
def read(command):
    result=subprocess.run(command, check=True, capture_output=True, timeout=10)
    require(len(result.stdout)<=4194304 and len(result.stderr)<=65536, 'reader-output-bound')
    return result.stdout.decode('utf-8')
compiler=pathlib.Path('/usr/bin/gcc-13')
destination=pathlib.Path(os.environ['RUNNER_TEMP'])/'learn-custody-core-toolchain'
destination.mkdir(mode=0o700)
provider=destination/'provider-original.json'
provider.write_text(json.dumps({key:os.environ[key] for key in ('GITHUB_REPOSITORY','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_JOB','GITHUB_SHA','RUNNER_ENVIRONMENT','RUNNER_OS','RUNNER_ARCH','ImageOS','ImageVersion')},sort_keys=True)+'\n')
provider.chmod(0o600)
required=[compiler, pathlib.Path('/usr/libexec/gcc/x86_64-linux-gnu/13/cc1'),
          pathlib.Path('/usr/libexec/gcc/x86_64-linux-gnu/13/collect2'),
          pathlib.Path('/usr/bin/as'), pathlib.Path('/usr/bin/ld'), pathlib.Path('/usr/bin/readelf'),
          pathlib.Path('/usr/lib/gcc/x86_64-linux-gnu/13/include/stddef.h'),
          pathlib.Path('/usr/lib/gcc/x86_64-linux-gnu/13/include/stdint.h'),
          pathlib.Path('/usr/lib/gcc/x86_64-linux-gnu/13/include/limits.h'),
          pathlib.Path('/usr/include/stdint.h'), pathlib.Path('/usr/include/limits.h'),
          pathlib.Path('/usr/include/stdio.h'), pathlib.Path('/usr/include/string.h'),
          pathlib.Path('/usr/lib/x86_64-linux-gnu/crt1.o'), pathlib.Path('/usr/lib/x86_64-linux-gnu/crti.o'),
          pathlib.Path('/usr/lib/x86_64-linux-gnu/crtn.o')]
missing=[str(p) for p in required if not p.is_file()]
require(not missing, 'required-compiler-linker-header-input:'+','.join(missing))
database=read(['/usr/bin/dpkg-query','-W','-f=${Package}\t${Architecture}\t${Version}\t${db:Status-Status}\t${Depends}\t${Pre-Depends}\n'])
original=destination/'packages-original.tsv'; original.write_text(database); original.chmod(0o600)
installed={}
for line in database.splitlines():
    fields=line.split('\t'); require(len(fields)==6, 'package-fields')
    name,arch,version,status,depends,predepends=fields
    if arch in ('amd64','all') and status=='installed': installed[name]=(version,depends,predepends)
pending=['gcc-13','libc6-dev','binutils']; selected={}
pending += ['sudo','python3','coreutils','libpam-runtime','libnss-systemd']
required += [pathlib.Path('/usr/bin/sudo'),pathlib.Path('/usr/bin/python3'),pathlib.Path('/usr/bin/timeout')]
require(all(p.is_file() for p in required), 'observation-required-input')
while pending:
    name=pending.pop()
    if name in selected: continue
    require(name in installed, 'missing-package-'+name)
    selected[name]=installed[name]
    require(len(selected)<=128, 'package-count')
    for group in ','.join(installed[name][1:]).split(','):
        if not group.strip(): continue
        alternatives=[re.split(r'[ (]',part.strip(),1)[0].split(':')[0] for part in group.split('|')]
        choice=next((item for item in alternatives if item in installed), None)
        require(choice is not None, 'unresolved-package-dependency-'+name)
        pending.append(choice)
require(selected['gcc-13'][0].startswith('13.3.0-'), 'selected-gcc-version')
paths=set(required)
for name in sorted(selected):
    for line in read(['/usr/bin/dpkg-query','-L',name]).splitlines():
        if line.startswith(('/usr/bin/','/usr/lib/','/usr/libexec/','/usr/include/','/lib/','/lib64/')):
            p=pathlib.Path(line)
            if p.is_file(): paths.add(p)
# Loader cache/config participates in actual path selection, while fixed
# -nostdinc removes ambient /usr/local include search from compilation.
for p in [pathlib.Path('/etc/ld.so.cache'),pathlib.Path('/etc/ld.so.conf')]:
    require(p.is_file(), 'loader-search-input'); paths.add(p)
paths.update(p for p in pathlib.Path('/etc/ld.so.conf.d').glob('*') if p.is_file())
# This observation binds the already-running provider-trusted interpreter; it
# cannot retroactively prove immutable startup or the separate SDK/PAM policy.
require(pathlib.Path(sys.executable).resolve(strict=True)==pathlib.Path('/usr/bin/python3.12'), 'python-executable')
require(sys.flags.isolated and sys.flags.no_site and sys.flags.ignore_environment and sys.flags.safe_path, 'python-isolation')
require(sys.prefix==sys.base_prefix=='/usr' and sys.exec_prefix==sys.base_exec_prefix=='/usr', 'python-prefix')
python_observations=[]
def observe_python_path(p,role):
    p=pathlib.Path(p)
    require(p.is_absolute() and '..' not in p.parts and str(p).startswith(('/usr/bin/','/usr/lib/')), 'python-path-scope:'+str(p))
    present=p.exists() or p.is_symlink()
    row={'path':str(p),'role':role,'present':present,'ancestors':[]}
    try:
        if present:
            require(p.lstat().st_uid==0,'python-path-owner:'+str(p))
            resolved=p.resolve(strict=True); info=resolved.stat()
            require(str(resolved).startswith(('/usr/bin/','/usr/lib/')), 'python-resolved-scope:'+str(p))
            require(info.st_uid==0 and not info.st_mode&0o022 and (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)), 'python-path-custody:'+str(p))
            row.update(resolved=str(resolved),uid=info.st_uid,mode=oct(stat.S_IMODE(info.st_mode)),type=stat.S_IFMT(info.st_mode))
            if stat.S_ISREG(info.st_mode): paths.add(p)
        else:
            resolved=p
            row['absence']='exists=false and is_symlink=false at observation'
        for ancestor in sorted(set(p.parents)|set(resolved.parents)):
            if not ancestor.exists():
                require(not ancestor.is_symlink(),'python-dangling-ancestor:'+str(ancestor))
                row['ancestors'].append({'path':str(ancestor),'present':False})
                continue
            info=ancestor.stat()
            require(stat.S_ISDIR(info.st_mode) and info.st_uid==0 and not info.st_mode&0o022, 'python-ancestor-custody:'+str(ancestor))
            row['ancestors'].append({'path':str(ancestor),'present':True,'uid':info.st_uid,'mode':oct(stat.S_IMODE(info.st_mode))})
    finally:
        python_observations.append(row)
        encoded=json.dumps(python_observations).encode()
        require(len(python_observations)<=1024 and len(encoded)<=1048576,'python-observation-bound')
        (destination/'python-runtime-original.json').write_bytes(encoded)
for value in sys.path:
    require(value and str(value).startswith('/usr/lib/'), 'python-search-root:'+str(value))
    observe_python_path(value,'actual-search-root')
config_probes={pathlib.Path('/usr/bin/pyvenv.cfg'),pathlib.Path('/usr/pyvenv.cfg'),
               pathlib.Path('/usr/bin/python3._pth'),pathlib.Path('/usr/bin/python3.12._pth'),
               pathlib.Path('/usr/bin/pybuilddir.txt')}
library_dir=sysconfig.get_config_var('LIBDIR'); library_name=sysconfig.get_config_var('LDLIBRARY')
require(isinstance(library_dir,str) and isinstance(library_name,str) and '/' not in library_name, 'python-library-config')
config_probes.add(pathlib.Path(library_dir)/(library_name+'._pth'))
with pathlib.Path('/proc/self/maps').open('rb') as source: maps=source.read(1048577)
require(len(maps)<=1048576,'python-maps-bound')
for line in maps.decode('utf-8').splitlines():
    fields=line.split(None,5)
    if len(fields)==6 and pathlib.Path(fields[5]).name.startswith('libpython3.12.so'):
        require(fields[5].startswith('/usr/lib/') and ' (deleted)' not in fields[5], 'python-loaded-library')
        observe_python_path(fields[5],'actual-loaded-python-library')
        config_probes.add(pathlib.Path(fields[5]+'._pth'))
for p in sorted(config_probes):
    # pyvenv.cfg alongside /usr is outside module roots but still has qualified
    # existing ancestors; present content is refused rather than interpreted.
    if p==pathlib.Path('/usr/pyvenv.cfg'):
        require(not p.exists() and not p.is_symlink(),'python-parent-venv-config')
        ancestors=[]
        for ancestor in p.parents:
            info=ancestor.stat(); require(stat.S_ISDIR(info.st_mode) and info.st_uid==0 and not info.st_mode&0o022,'python-parent-config-ancestor')
            ancestors.append({'path':str(ancestor),'present':True,'uid':info.st_uid,'mode':oct(stat.S_IMODE(info.st_mode))})
        python_observations.append({'path':str(p),'role':'path-configuration','present':False,'absence':'exists=false and is_symlink=false at observation','ancestors':ancestors})
    else: observe_python_path(p,'path-configuration')
    require(not p.exists() and not p.is_symlink(),'python-present-path-configuration:'+str(p))
for name,module in sorted(tuple(sys.modules.items())):
    for value in getattr(module,'__path__',()): observe_python_path(value,'loaded-package-search:'+name)
    origin=getattr(getattr(module,'__spec__',None),'origin',None)
    if origin in (None,'built-in','frozen'): continue
    require(isinstance(origin,str),'python-module-origin:'+name)
    observe_python_path(origin,'loaded-module:'+name)
require(len(paths)<=16384, 'file-count')
# Direct argv/Path/JSON/sha256sum records require no shell escaping.
def path_record_ok(p):
    value=str(p)
    return (p.is_absolute() and len(value.encode('utf-8'))<=4096
        and all(33<=ord(c)<=126 for c in value)
        and all(c not in ('.','..') for c in value.split('/')))
path_records=sorted(str(p) for p in paths)
require(sum(len(p.encode('utf-8')) for p in path_records)<=1048576, 'path-record-byte-bound')
encoded_paths=json.dumps(path_records).encode()
require(len(encoded_paths)<=1048576, 'path-evidence-byte-bound')
(destination/'package-paths-original.json').write_bytes(encoded_paths)
invalid_paths=[str(p) for p in sorted(paths) if not path_record_ok(p)]
(destination/'invalid-paths-original.json').write_text(json.dumps(invalid_paths))
require(not invalid_paths, 'path-encoding:'+json.dumps(invalid_paths[0][:4096]) if invalid_paths else 'path-encoding')
# Complete read-only custody diagnosis precedes all hashing/readelf work.
# Keep each completed row if a later read, unknown race or budget stops us.
custody_bytes=0; custody_first=None; inventory_only=set()
custody_output=destination/'path-custody-original.jsonl'
custody_output.touch(mode=0o600)
def retain_custody(row):
    global custody_bytes
    encoded=(json.dumps(row,separators=(',',':'))+'\n').encode()
    require(len(encoded)<=16384, (custody_first or 'custody-record')+'; additional:row-byte-bound')
    require(custody_bytes+len(encoded)<=4194304, (custody_first or 'custody-record')+'; additional:custody-evidence-bound')
    with custody_output.open('ab') as output: output.write(encoded)
    custody_bytes+=len(encoded)
def mark_custody(row,reason):
    global custody_first
    if reason not in row['defects']: row['defects'].append(reason)
    if custody_first is None and row['path']!='/usr/bin/py3versions': custody_first=reason+':'+row['path']
for p in sorted(paths):
    row={'path':str(p),'resolved':None,'uid':None,'mode':None,'type':None,'defects':[]}
    try:
        link=p.lstat(); row.update(pathUID=link.st_uid,pathMode=oct(stat.S_IMODE(link.st_mode)),pathType=stat.S_IFMT(link.st_mode))
        resolved=p.resolve(strict=True); info=resolved.stat()
        row.update(resolved=str(resolved),uid=info.st_uid,mode=oct(stat.S_IMODE(info.st_mode)),type=stat.S_IFMT(info.st_mode))
        if link.st_uid!=0: mark_custody(row,'path-owner')
        if not path_record_ok(resolved): mark_custody(row,'resolved-path-encoding')
        if not (stat.S_ISREG(info.st_mode) and info.st_uid==0 and not info.st_mode&0o022): mark_custody(row,'file-custody')
        ancestors=set(p.parents)|set(resolved.parents)
        require(len(ancestors)<=128,'ancestor-count-bound:'+str(p))
        row['failedAncestors']=[]
        for ancestor in sorted(ancestors):
            a=ancestor.stat()
            if a.st_uid!=0 or a.st_mode&0o022:
                mark_custody(row,'ancestor-custody')
                row['failedAncestors'].append(dict(path=str(ancestor),uid=a.st_uid,mode=oct(stat.S_IMODE(a.st_mode)),type=stat.S_IFMT(a.st_mode)))
    except Exception as error:
        row['unknown']=type(error).__name__+':'+str(error)
        retain_custody(row)
        raise RuntimeError((custody_first or 'custody-unknown:'+str(p))+'; additional:'+row['unknown']) from error
    if str(p)=='/usr/bin/py3versions':
        try:
            require(row['resolved']=='/usr/share/python3/py3versions.py' and row['pathUID']==0 and row['uid']==0
                    and row['type']==stat.S_IFREG and row['pathType']==stat.S_IFLNK,'inventory-only-exact-pair')
            require(set(row['defects'])<= {'file-custody','ancestor-custody'} and
                    all(a['path'] in ('/usr/share','/usr/share/python3') and a['uid']==0 and a['type']==stat.S_IFDIR
                        for a in row['failedAncestors']), 'inventory-only-unexpected-defect')
            row['scope']='inventory-only; no fixed execution/import/config edge; runtime search prerequisite separately observed'
            with pathlib.Path(row['resolved']).open('rb') as source: helper_bytes=source.read(65537)
            require(len(helper_bytes)<=65536,'inventory-only-snapshot-bound')
            (destination/'inventory-only-py3versions-original.bin').write_bytes(helper_bytes)
            row['snapshotBytes']=len(helper_bytes); row['snapshotSha256']=hashlib.sha256(helper_bytes).hexdigest()
            row['snapshotCustodyQualified']=False
            inventory_only.add(p)
        finally:
            retain_custody(row)
    else:
        retain_custody(row)
require(custody_first is None,custody_first or 'path-custody')
rows=[]; aliases={}; total=0; cached={}
for p in sorted(paths):
    if p in inventory_only: continue
    require(p.lstat().st_uid==0, 'path-owner:'+str(p))
    resolved=p.resolve(strict=True); info=resolved.stat()
    require(stat.S_ISREG(info.st_mode) and info.st_uid==0 and not info.st_mode&0o022, 'file-custody:'+str(p))
    for ancestor in set(p.parents)|set(resolved.parents):
        a=ancestor.stat(); require(a.st_uid==0 and not a.st_mode&0o022, 'ancestor-custody:'+str(p)+':'+str(ancestor))
    if resolved not in cached:
        total+=info.st_size; require(info.st_size<=134217728 and total<=1073741824, 'read-byte-bound')
        digest=hashlib.sha256()
        with resolved.open('rb') as source:
            for block in iter(lambda:source.read(1048576),b''): digest.update(block)
        cached[resolved]=digest.hexdigest()
    aliases.setdefault(p.name,set()).add(str(resolved))
    rows.append(dict(path=str(p),resolved=str(resolved),bytes=info.st_size,sha256=cached[resolved],
                     link=os.readlink(p) if p.is_symlink() else None))
manifest=destination/'toolchain.sha256'
manifest.write_text(''.join(('\\' if '\\' in row['path'] else '')+row['sha256']+'  '+row['path'].replace('\\','\\\\')+'\n' for row in rows)); manifest.chmod(0o600)
require(manifest.stat().st_size<=4194304, 'manifest-size')
elf=[]; elf_defects=[]
def elf_check(ok,reason):
    if not ok:
        first=elf_defects[0] if elf_defects else reason
        require(len(elf_defects)<1024, first+'; additional:ELF-defect-count-bound')
        encoded=json.dumps(elf_defects+[reason]).encode()
        require(len(encoded)<=131072, first+'; additional:ELF-defect-byte-bound')
        elf_defects.append(reason)
        try:
            pending=destination/'elf-guard-defects.pending.json'
            pending.write_bytes(encoded)
            pending.replace(destination/'elf-guard-defects-original.json')
        except Exception as error:
            raise RuntimeError(first+'; additional:defect-retention:'+str(error)) from error
elf_original=destination/'elf-original.txt'; elf_original.touch(mode=0o600); elf_bytes=0
for resolved in sorted(cached):
    with resolved.open('rb') as source: magic=source.read(4)
    if magic!=b'\x7fELF': continue
    detail=read(['/usr/bin/readelf','--wide','--file-header','--dynamic','--program-headers',str(resolved)])
    retained=str(resolved)+'\n'+detail+'\n'; elf_bytes+=len(retained.encode())
    require(elf_bytes<=4194304, 'ELF-evidence-bound')
    with elf_original.open('a') as output: output.write(retained)
    elf_check(re.search(r'Class:\s+ELF64',detail) and re.search(r'Machine:.*X86-64',detail), 'ELF-target:'+str(resolved))
    needed=re.findall(r'\(NEEDED\).*?\[([^\]]+)\]',detail)
    elf_check(all(len(aliases.get(name,set()))==1 for name in needed), 'unclosed-or-ambiguous-ELF-needed:'+str(resolved)+':'+','.join(needed))
    interpreters=re.findall(r'Requesting program interpreter: ([^\]]+)',detail)
    elf_check(all(pathlib.Path(path).resolve(strict=True) in cached for path in interpreters), 'unclosed-ELF-interpreter:'+str(resolved))
    search=re.findall(r'\((?:RPATH|RUNPATH)\).*?\[([^\]]*)\]',detail)
    for value in search:
        elf_check(value in ('$ORIGIN','${ORIGIN}'), 'unexpected-ELF-search-override:'+str(resolved)+':'+value)
        if value not in ('$ORIGIN','${ORIGIN}'): continue
        for name in needed:
            sibling=resolved.parent/name
            if sibling.exists() and len(aliases.get(name,set()))==1:
                elf_check(str(sibling.resolve(strict=True))==next(iter(aliases[name])), 'unclosed-ELF-origin-hit')
    elf.append(dict(path=str(resolved),needed={name:(next(iter(aliases[name])) if len(aliases.get(name,set()))==1 else None) for name in needed},interpreters=interpreters,search=search))
require(not elf_defects, elf_defects[0] if elf_defects else 'ELF-guards')
evidence=destination/'toolchain-evidence.json'
evidence.write_text(json.dumps(dict(provider='github-hosted',image=os.environ['ImageVersion'],
    imageSource=os.environ['FSGG_LEARN_CUSTODY_CORE_IMAGE_SOURCE'],compiler=str(compiler),
    packages={name:values[0] for name,values in selected.items()},files=rows,elf=elf,
    trust='Root-selected immutable GitHub-hosted provisioning; census hashes bind bytes, not provenance.'),sort_keys=True)+'\n')
evidence.chmod(0o600); require(evidence.stat().st_size<=8388608, 'evidence-size')
compiler_sha=next(row['sha256'] for row in rows if row['path']==str(compiler))
manifest_sha=hashlib.sha256(manifest.read_bytes()).hexdigest()
with open(os.environ['GITHUB_ENV'],'a') as output:
    output.write('FSGG_LEARN_CUSTODY_CORE_CC='+str(compiler)+'\nFSGG_LEARN_CUSTODY_CORE_CC_SHA256='+compiler_sha+
        '\nFSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256_FILE='+str(manifest)+'\nFSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256='+manifest_sha+'\n')
print('custody-core trusted-image='+os.environ['ImageVersion']+' image-source='+os.environ['FSGG_LEARN_CUSTODY_CORE_IMAGE_SOURCE']+
      ' packages='+str(len(selected))+' files='+str(len(rows))+' bytes='+str(total)+' compiler='+compiler_sha+' closure='+manifest_sha)
