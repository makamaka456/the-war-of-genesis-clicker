# 헤드리스 시험기를 csc 로 빌드한다 — APK 와 같은 소스 목록(WarOfGenesis.Android.csproj 의 Compile)을 쓰되,
# 안드로이드 API 를 쓰는 곳(MainActivity.cs · Droid/)은 빼고 시험기 자리(SimMain.cs)를 넣는다. 정의 기호도 ANDROID 로 같다.
# 쓰기:  TOOLS=~/twg-tools python3 WarOfGenesis.Android/devtools/sim/build.py   → 출력 폴더를 찍는다(번갈아 a · b — 돌고 있는 시험기를 덮지 않게).
import glob, os, re, shutil, subprocess, sys

T = os.environ.get('TOOLS') or os.path.expanduser('~/twg-tools')
here = os.path.dirname(os.path.abspath(__file__))
pdir = os.path.normpath(os.path.join(here, '..', '..'))
repo = os.path.dirname(pdir)
xml = open(os.path.join(pdir, 'WarOfGenesis.Android.csproj'), encoding='utf-8-sig').read()

files, excl = [], set()
pairs = re.findall(r'<Compile Include="([^"]+)"(?:\s+Exclude="([^"]+)")?', xml)
for inc, ex in pairs:
    for e in (ex or '').split(';'):
        if e:
            excl.update(os.path.normpath(x) for x in glob.glob(os.path.normpath(os.path.join(pdir, e.replace('\\', '/'))), recursive=True))
for inc, _ in pairs:
    full = os.path.normpath(os.path.join(pdir, inc.replace('\\', '/')))
    files.extend(sorted(glob.glob(full, recursive=True)) if '*' in full else [full])
files += sorted(glob.glob(os.path.join(pdir, 'Platform', '*.cs'))) + [os.path.join(pdir, 'Game', 'GameSetup.cs'), os.path.join(here, 'SimMain.cs')]
files = [f for f in files if os.path.normpath(f) not in excl]

usings = ['System', 'System.Collections.Generic', 'System.IO', 'System.Linq', 'System.Net.Http', 'System.Threading', 'System.Threading.Tasks'] + re.findall(r'<Using Include="([^"]+)"', xml)
gu = os.path.join(T, 'SimUsings.g.cs')
open(gu, 'w').write('\n'.join('global using global::%s;' % n for n in usings) + '\n')

slot = 'a'
mark = os.path.join(T, 'sim', 'last')
if os.path.exists(mark) and open(mark).read().strip() == 'a':
    slot = 'b'
out = os.path.join(T, 'sim', slot)
shutil.rmtree(out, ignore_errors=True)
os.makedirs(out)
refs = glob.glob(T + '/pkgs/microsoft.netcore.app.ref/ref/net10.0/*.dll')
args = [T + '/dn/dotnet', T + '/pkgs/microsoft.net.compilers.toolset/tasks/netcore/bincore/csc.dll', '/nologo', '/noconfig', '/nostdlib+', '/t:exe',
        '/unsafe+', '/nullable:enable', '/langversion:latest', '/optimize+', '/out:' + os.path.join(out, 'sim.dll'), '/define:ANDROID;SIM',
        '/nowarn:CS8618,CS8600,CS8602,CS8603,CS8604,CS8625,CS8765,CS8767,CS8764,CS8610,CS8619,CS8601,CS8620,CS1998,CS0162,CS0169,CS0414,CS0649,CS0219,CS0168'] \
       + ['/r:' + r for r in refs] + [gu] + files
r = subprocess.run(args, capture_output=True, text=True)
errs = [l for l in r.stdout.splitlines() if 'error' in l]
if errs:
    print('\n'.join(errs[:60]))
    sys.exit('빌드 실패: 오류 %d' % len(errs))
open(os.path.join(out, 'sim.runtimeconfig.json'), 'w').write('{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}')
shutil.copy(os.path.join(here, 'pilserver.py'), out)
# 파트1 엔딩 영상 해독기(native/wogmpeg.c · pl_mpeg) — 리눅스용으로 굽는다
nr = subprocess.run(['gcc', '-O2', '-shared', '-fPIC', '-o', os.path.join(out, 'libwogmpeg.so'), os.path.join(pdir, 'native', 'wogmpeg.c'), '-lm'], capture_output=True, text=True)
if nr.returncode != 0: sys.exit('해독기 빌드 실패: ' + nr.stderr[:2000])
# PC 판이 exe 옆에 싣는 우리 파일 — 저장소 assets(JSON · 꺼낼 목록 · 아이콘 · 그림 예제)와 릴리즈 노트
os.symlink(os.path.join(repo, 'assets'), os.path.join(out, 'assets'))
shutil.copy(os.path.join(repo, 'contributor.md'), out)
# 릴리즈 노트는 새 판부터 몇 대목만 — Platform/ReleaseNotesTrim.cs 와 같은 규칙
notes = open(os.path.join(repo, '릴리즈노트.md'), encoding='utf-8').read()
at, found = 0, 0
while True:
    at = notes.find('\n## ', at)
    if at < 0: break
    found += 1
    if found > 6 or at > 1800:
        notes = notes[:at].rstrip() + '\n' + '\n(이전 판 내역은 PC 판 릴리즈 노트에 있습니다)\n'
        break
    at += 4
open(os.path.join(out, '릴리즈노트.md'), 'w', encoding='utf-8').write(notes)
open(mark, 'w').write(slot)
print(out)
