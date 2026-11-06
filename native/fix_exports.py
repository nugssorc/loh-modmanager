"""lld 18 은 '전달' 내보내기의 서수(@N)·NONAME 을 무시한다 → 진짜 윈도우 파일과 서수까지 똑같은 내보내기 표로 다시 쓴다.
   쓰는 법: python3 fix_exports.py proxy.dll <시스템 DLL 이름> sys_exports.json"""
import struct,sys,json
p,n,jf=sys.argv[1],sys.argv[2],sys.argv[3]
sysx=json.load(open(jf))[n]
d=bytearray(open(p,'rb').read())
pe=struct.unpack_from('<I',d,0x3c)[0]; opt=pe+24; ddir=opt+112
exp_rva,exp_sz=struct.unpack_from('<II',d,ddir)
nsec=struct.unpack_from('<H',d,pe+6)[0]; soh=struct.unpack_from('<H',d,pe+20)[0]; sec=pe+24+soh
secs=[struct.unpack_from('<8sIIII',d,sec+40*i) for i in range(nsec)]
def off(rva):
    for nm,vs,va,rs,ro in secs:
        if va<=rva<va+max(vs,rs): return rva-va+ro
    raise Exception('rva %x'%rva)
def sec_of(rva):
    for nm,vs,va,rs,ro in secs:
        if va<=rva<va+max(vs,rs): return (va,rs,ro,vs)
e=off(exp_rva)
_,_,_,_,name_rva,base,nfun,nnames,af,an,ao=struct.unpack_from('<IIHHIIIIIII',d,e)
# 우리 함수(LOH_DokbakYuga_Entry) 주소
entry_rva=None
for i in range(nnames):
    nr=struct.unpack_from('<I',d,off(an)+4*i)[0]; o=struct.unpack_from('<H',d,off(ao)+2*i)[0]
    s=d[off(nr):d.index(b'\0',off(nr))].decode()
    if s=='LOH_DokbakYuga_Entry': entry_rva=struct.unpack_from('<I',d,off(af)+4*o)[0]
assert entry_rva
items=[(o,nm,'\\\\.\\GLOBALROOT\\SystemRoot\\System32\\%s.%s'%(n,nm if nm else '#%d'%o)) for o,nm,f in sysx]
mx=max(o for o,_,_ in items); items.append((mx+1,'LOH_DokbakYuga_Entry',None))
b0=min(o for o,_,_ in items); cnt=max(o for o,_,_ in items)-b0+1
named=sorted([x for x in items if x[1]], key=lambda x: x[1].encode())
# 배치: 머리(40) | 함수표 | 이름표 | 서수표 | 문자열들
hdr=40; fa=hdr; na=fa+4*cnt; oa=na+4*len(named); sa=oa+2*len(named)
strs=bytearray(); soff={}
def addstr(s):
    if s in soff: return soff[s]
    soff[s]=sa+len(strs); strs.extend(s.encode()+b'\0'); return soff[s]
dll_off=addstr(n+'.dll')
for o,nm,fw in items:
    if nm: addstr(nm)
    if fw: addstr(fw)
total=sa+len(strs)
va,rs,ro,vs=sec_of(exp_rva)
room=(va+rs)-exp_rva      # 같은 구역 안 남은 자리
assert total<=room, ('자리 부족', total, room)
blob=bytearray(total)
R=lambda x: exp_rva+x
struct.pack_into('<IIHHIIIIIII',blob,0,0,0,0,0,R(dll_off),b0,cnt,len(named),R(fa),R(na),R(oa))
for o,nm,fw in items:
    struct.pack_into('<I',blob,fa+4*(o-b0), R(soff[fw]) if fw else entry_rva)
for i,(o,nm,fw) in enumerate(named):
    struct.pack_into('<I',blob,na+4*i,R(soff[nm])); struct.pack_into('<H',blob,oa+2*i,o-b0)
blob[sa:]=strs
d[e:e+total]=blob
# 남은 옛 내용은 0 으로
old_end=e+exp_sz
if old_end>e+total: d[e+total:old_end]=b'\0'*(old_end-e-total)
struct.pack_into('<II',d,ddir,exp_rva,total)
# 구역 가상 크기가 모자라면 늘림
for i in range(nsec):
    nm,vsz,vadr,rsz,rof=struct.unpack_from('<8sIIII',d,sec+40*i)
    if vadr==va and vsz<(exp_rva-va)+total: struct.pack_into('<I',d,sec+40*i+8,(exp_rva-va)+total)
open(p,'wb').write(d)
print('%s: 내보내기 %d개(서수 %d~%d, 이름 없는 것 %d), 표 %d/%d 바이트'%(n,len(items),b0,b0+cnt-1,len(items)-len(named),total,room))
