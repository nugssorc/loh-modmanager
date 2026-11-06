/* Personal mod bootstrap. No IL2CPP calls and no runtime DLL unloading. */
#include "win.h"
W HANDLE API LoadLibraryW(const wch *);
W u32 API GetPrivateProfileStringW(const wch *,const wch *,const wch *,wch *,u32,const wch *);
W u32 API GetPrivateProfileIntW(const wch *,const wch *,int,const wch *);
W int API WritePrivateProfileStringW(const wch *,const wch *,const wch *,const wch *);
W int API GetProcessTimes(HANDLE,u64 *,u64 *,u64 *,u64 *);
static wch gameRoot[1000],config[1200],runtimeFile[1200],entryName[32];
typedef int (*Query)(void);
typedef struct {wch id[64],path[1000];HANDLE module;int state,error,detail;} Mod;
static Mod mods[32];static int count;
void *memset(void *d,int c,uptr n){u8 *p=d;while(n--)*p++=(u8)c;return d;}
void *memcpy(void *d,const void *s,uptr n){u8 *p=d;const u8 *q=s;while(n--)*p++=*q++;return d;}
static void copy(wch *d,const wch *s){while((*d++=*s++));}
static void append(wch *d,const wch *s){while(*d)d++;copy(d,s);}
static void number(wch *out,u32 value){wch r[16];int i=0,j=0;do{r[i++]=(wch)('0'+value%10);value/=10;}while(value);while(i)out[j++]=r[--i];out[j]=0;}
static void put(const wch *section,const wch *key,u32 value){wch text[16];number(text,value);WritePrivateProfileStringW(section,key,text,runtimeFile);}
static int valid_id(const wch *s){int n=0;while(*s){if(!((*s>='a'&&*s<='z')||(*s>='A'&&*s<='Z')||(*s>='0'&&*s<='9')||*s=='_'))return 0;n++;s++;}return n>0&&n<64;}
static const wch *basename(const wch *s){const wch *base=s;while(*s){if(*s=='\\'||*s=='/')base=s+1;s++;}return base;}
static int valid_path(const wch *s){int n=0;while(s[n])n++;return n>6&&s[1]==':'&&(s[2]=='\\'||s[2]=='/')&&lstrcmpiW(s+n-4,L".dll")==0;}
static void report(Mod *m){put(m->id,L"State",(u32)m->state);put(m->id,L"Error",(u32)m->error);put(m->id,L"Detail",(u32)m->detail);}
__declspec(dllexport) const char *LOH_ModManager_Entry(void){return "LOH_ModManager entry 1";}
static u32 API boot(void *unused){wch section[40],digits[20],mutex[100],revision[80],defaultEntry[32];u64 created=0,exitTime,kernel,user;u32 pid=GetCurrentProcessId(),written;HANDLE file;(void)unused;
    copy(mutex,L"LOH_ModManager_");number(digits,pid);append(mutex,digits);append(mutex,L"_");append(mutex,entryName);CreateMutexW(0,0,mutex);if(GetLastError()==183)return 0;
    copy(config,gameRoot);append(config,L"LOH_ModManager\\Modules.ini");
    copy(runtimeFile,gameRoot);append(runtimeFile,L"LOH_ModManager\\Runtime-");append(runtimeFile,digits);append(runtimeFile,L"-");append(runtimeFile,entryName);append(runtimeFile,L".ini");
    file=CreateFileW(runtimeFile,0x40000000,3,0,2,0x80,0);
    if(file!=INVALID_HANDLE){static const u8 bom[2]={0xff,0xfe};WriteFile(file,bom,2,&written,0);CloseHandle(file);}
    GetProcessTimes(GetCurrentProcess(),&created,&exitTime,&kernel,&user);
    put(L"Manager",L"Pid",pid);put(L"Manager",L"CreationLow",(u32)created);put(L"Manager",L"CreationHigh",(u32)(created>>32));
    WritePrivateProfileStringW(L"Manager",L"Entry",entryName,runtimeFile);
    GetPrivateProfileStringW(L"Manager",L"DefaultEntry",entryName,defaultEntry,32,config);
    GetPrivateProfileStringW(L"Manager",L"Revision",L"",revision,80,config);WritePrivateProfileStringW(L"Manager",L"Revision",revision,runtimeFile);
    count=(int)GetPrivateProfileIntW(L"Manager",L"Count",0,config);if(count<0||count>32){put(L"Manager",L"Error",1);return 1;}
    put(L"Manager",L"Count",(u32)count);
    for(int i=0;i<count;i++){
        Mod *m=mods+i;wch loadedPath[1000],assignedEntry[32];copy(section,L"Module");number(digits,(u32)i);append(section,digits);
        GetPrivateProfileStringW(section,L"Route",defaultEntry,assignedEntry,32,config);
        if(lstrcmpiW(assignedEntry,entryName)!=0)continue;
        GetPrivateProfileStringW(section,L"Id",L"",m->id,64,config);
        if(!valid_id(m->id)){put(L"Manager",L"Error",2);continue;}
        {int duplicate=0;for(int j=0;j<i;j++)if(lstrcmpiW(m->id,mods[j].id)==0)duplicate=1;if(duplicate){m->state=7;report(m);continue;}}
        GetPrivateProfileStringW(section,L"Path",L"",m->path,1000,config);
        if(!GetPrivateProfileIntW(section,L"Enabled",0,config)){m->state=1;report(m);continue;}
        if(!valid_path(m->path)){m->state=7;report(m);continue;}
        if(GetFileAttributesW(m->path)==0xffffffff){m->state=2;m->error=(int)GetLastError();report(m);continue;}
        m->module=GetModuleHandleW(basename(m->path));
        if(m->module){
            GetModuleFileNameW(m->module,loadedPath,1000);
            if(lstrcmpiW(loadedPath,m->path)!=0){m->state=5;report(m);continue;}
            m->state=4;
        }else{
            m->module=LoadLibraryW(m->path);m->error=m->module?0:(int)GetLastError();m->state=m->module?3:6;
        }report(m);
    }
    /* Detail is written only when it changes. The heartbeat used to be written every second;
       one WritePrivateProfileStringW costs 2-5 ms of disk work (measured 2026-10-30), so it is
       now written every 30 s. The 0.5 manager judges liveness from the game process itself. */
    for(u32 tick=0;;tick++){
        for(int i=0;i<count;i++)if(mods[i].module&&(mods[i].state==3||mods[i].state==4)){
            Query q=(Query)GetProcAddress(mods[i].module,"LOH_ModManagerState");
            int detail=q?q():0;if(detail!=mods[i].detail){mods[i].detail=detail;report(mods+i);}
        }
        if(tick%30==0)put(L"Manager",L"Heartbeat",GetTickCount());Sleep(1000);
    }
}
int API DllMain(HANDLE module,u32 reason,void *reserved){(void)reserved;if(reason==1){wch exe[1000];u32 n;DisableThreadLibraryCalls(module);
    n=GetModuleFileNameW(0,exe,1000);if(n&&n<1000&&lstrcmpiW(basename(exe),L"ThreeKingdom.exe")==0){
        n=GetModuleFileNameW(module,gameRoot,999);if(!n||n>=999)return 1;
        if(lstrlenW(basename(gameRoot))>=32)return 1;copy(entryName,basename(gameRoot));
        while(n&&gameRoot[n-1]!='\\')n--;gameRoot[n]=0;
        if(n){HANDLE th=CreateThread(0,0,boot,0,0,0);if(th)CloseHandle(th);}
    }}return 1;
}
