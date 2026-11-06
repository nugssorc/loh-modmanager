// 관리창의 화면 없는 부분: 상태 읽기, DLL 버전, 백엔드 호출, 프로필, 예약 저장.
// WPF 에 기대지 않으므로 tests/test_state.cs 가 그대로 불러 검사한다. (csc 4.x = C# 5 문법)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace LohModManager {
    static class J {
        public static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer {MaxJsonLength=int.MaxValue,RecursionLimit=64};
        public static Dictionary<string,object> Obj(object v) { return v as Dictionary<string,object>; }
        public static object Get(Dictionary<string,object> o,string k) { object v; return o!=null && o.TryGetValue(k,out v) ? v : null; }
        public static string S(Dictionary<string,object> o,string k) { object v=Get(o,k); return v==null ? "" : Convert.ToString(v,CultureInfo.InvariantCulture); }
        public static bool B(Dictionary<string,object> o,string k) {
            object v=Get(o,k); if(v==null) return false; if(v is bool) return (bool)v;
            string s=Convert.ToString(v,CultureInfo.InvariantCulture).Trim(); return s=="1" || s.Equals("true",StringComparison.OrdinalIgnoreCase);
        }
        // ConvertTo-Json writes a one-element array as a bare object in some places; accept both.
        public static IEnumerable<Dictionary<string,object>> Items(object v) {
            var d=v as Dictionary<string,object>; if(d!=null) { yield return d; yield break; }
            var list=v as IEnumerable; if(list==null || v is string) yield break;
            foreach(object item in list) { var x=item as Dictionary<string,object>; if(x!=null) yield return x; }
        }
        public static List<string> Strings(object v) {
            var r=new List<string>(); if(v==null) return r; if(v is string) { r.Add((string)v); return r; }
            var list=v as IEnumerable; if(list!=null) foreach(object item in list) if(item!=null) r.Add(Convert.ToString(item,CultureInfo.InvariantCulture));
            return r;
        }
        public static Dictionary<string,object> ReadFile(string path) {
            string text=Text.Read(path); if(text==null) return null;
            try { return Serializer.Deserialize<Dictionary<string,object>>(text.Trim()); } catch { return null; }
        }
        public static void WriteFile(string path,object value) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp=path+".tmp";File.WriteAllText(temp,Serializer.Serialize(value),new UTF8Encoding(true));
            if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
    }

    static class Text {
        // Reads while the game or PowerShell may be writing the same file.
        public static string Read(string path) {
            try {
                byte[] bytes;
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    if(stream.Length>16<<20) return null; bytes=new byte[stream.Length]; int done=0;
                    while(done<bytes.Length) { int n=stream.Read(bytes,done,bytes.Length-done); if(n<=0) break; done+=n; }
                    if(done<bytes.Length) Array.Resize(ref bytes,done);
                }
                return Decode(bytes);
            } catch { return null; }
        }
        public static string Decode(byte[] b) {
            if(b.Length>=2 && b[0]==0xFF && b[1]==0xFE) return Encoding.Unicode.GetString(b,2,b.Length-2);
            if(b.Length>=3 && b[0]==0xEF && b[1]==0xBB && b[2]==0xBF) return Encoding.UTF8.GetString(b,3,b.Length-3);
            if(b.Length>=4 && b[0]!=0 && b[1]==0 && b[3]==0) return Encoding.Unicode.GetString(b);
            return Encoding.UTF8.GetString(b);
        }
    }

    sealed class IniFile {
        readonly Dictionary<string,Dictionary<string,string>> sections=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Order=new List<string>();
        public static IniFile Read(string path) {
            string text=Text.Read(path); if(text==null) return null;
            var ini=new IniFile();Dictionary<string,string> current=null;
            foreach(string raw in text.Split('\n')) {
                string line=raw.TrimEnd('\r').Trim(); if(line.Length==0 || line[0]==';') continue;
                if(line[0]=='[' && line[line.Length-1]==']') {
                    string name=line.Substring(1,line.Length-2);
                    if(!ini.sections.TryGetValue(name,out current)) { current=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);ini.sections[name]=current;ini.Order.Add(name); }
                    continue;
                }
                int eq=line.IndexOf('='); if(eq>0 && current!=null) current[line.Substring(0,eq).Trim()]=line.Substring(eq+1);
            }
            return ini;
        }
        public Dictionary<string,string> Section(string name) { Dictionary<string,string> s; return sections.TryGetValue(name,out s) ? s : null; }
        public string Get(string section,string key) { var s=Section(section); string v; return s!=null && s.TryGetValue(key,out v) ? v : ""; }
        public long Long(string section,string key) { long v; return long.TryParse(Get(section,key).Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out v) ? v : 0; }
    }

    // ---------- DLL inspection without loading it ----------
    sealed class PeInfo {
        public bool Valid,X64,Dll,Managed; public string Error="";
        public readonly List<string> Exports=new List<string>();
        public string ModInfo;   // data export LOH_ModInfo (see docs/모드_약속.md)
    }
    static class Pe {
        static int U16(byte[] b,long o) { if(o<0 || o+2>b.Length) throw new InvalidDataException("PE 범위 밖"); return b[o]|(b[o+1]<<8); }
        static uint U32(byte[] b,long o) { if(o<0 || o+4>b.Length) throw new InvalidDataException("PE 범위 밖"); return (uint)(b[o]|(b[o+1]<<8)|(b[o+2]<<16)|(b[o+3]<<24)); }
        static string CString(byte[] b,long o,int max,Encoding encoding) {
            if(o<0 || o>=b.Length) return null; long end=o; while(end<b.Length && end-o<max && b[end]!=0) end++;
            return encoding.GetString(b,(int)o,(int)(end-o));
        }
        public static byte[] ReadBytes(string path,long limit) {
            using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                if(s.Length>limit) throw new InvalidDataException("파일이 너무 큼");
                var b=new byte[s.Length];int done=0;while(done<b.Length){int n=s.Read(b,done,b.Length-done);if(n<=0)break;done+=n;}
                return b;
            }
        }
        public static PeInfo Read(string path) { try { return Parse(ReadBytes(path,96L<<20)); } catch(Exception e) { var bad=new PeInfo();bad.Error=e.Message;return bad; } }
        public static PeInfo Parse(byte[] b) {
            var info=new PeInfo();
            try {
                if(U16(b,0)!=0x5a4d) throw new InvalidDataException("PE 파일 아님");
                long pe=U32(b,0x3c); if(U32(b,pe)!=0x4550) throw new InvalidDataException("PE 서명 오류");
                int machine=U16(b,pe+4),sectionCount=U16(b,pe+6),optionalSize=U16(b,pe+20),flags=U16(b,pe+22);long opt=pe+24;int magic=U16(b,opt);
                long dirs=opt+(magic==0x20b?112:magic==0x10b?96:-1); if(dirs<opt) throw new InvalidDataException("PE 형식 오류");
                uint dirCount=U32(b,dirs-4); if(sectionCount>96) throw new InvalidDataException("과도한 섹션 수");
                info.X64=machine==0x8664;info.Dll=(flags&0x2000)!=0;
                var sections=new List<uint[]>();long table=opt+optionalSize;
                for(int i=0;i<sectionCount;i++){long s=table+40*i;sections.Add(new uint[]{U32(b,s+12),Math.Max(U32(b,s+8),U32(b,s+16)),U32(b,s+20)});}
                Func<uint,long> at=rva=>{foreach(var s in sections)if(rva>=s[0]&&rva<s[0]+s[1])return (long)(rva-s[0]+s[2]);return -1;};
                if(dirCount>14 && optionalSize>=(dirs-opt)+15*8) info.Managed=U32(b,dirs+14*8)!=0;
                uint exportRva=dirCount>0?U32(b,dirs):0,exportSize=dirCount>0?U32(b,dirs+4):0;
                if(exportRva!=0) {
                    long e=at(exportRva);
                    if(e>=0) {
                        uint names=Math.Min(U32(b,e+24),4096u);long functions=at(U32(b,e+28)),nameTable=at(U32(b,e+32)),ordinals=at(U32(b,e+36));
                        for(uint i=0;i<names && nameTable>=0 && ordinals>=0;i++) {
                            string name=CString(b,at(U32(b,nameTable+4*i)),256,Encoding.ASCII); if(String.IsNullOrEmpty(name)) continue;
                            info.Exports.Add(name);
                            if(name=="LOH_ModInfo" && functions>=0) {
                                uint target=U32(b,functions+4*U16(b,ordinals+2*i));
                                if(target<exportRva || target>=exportRva+exportSize) info.ModInfo=CString(b,at(target),1024,Encoding.UTF8);
                            }
                        }
                    }
                }
                info.Valid=true;
            } catch(Exception error) { info.Error=error.Message; }
            return info;
        }
    }

    sealed class DllVersion {
        public string Text="",Source="";public bool CanPause;public PeInfo Pe;
        public static readonly DllVersion None=new DllVersion();
    }
    static class ModVersion {
        sealed class Cached { public long Length; public DateTime Write; public DllVersion Value; }
        static readonly Dictionary<string,Cached> cache=new Dictionary<string,Cached>(StringComparer.OrdinalIgnoreCase);
        static readonly Encoding latin1=Encoding.GetEncoding(28591);
        public static string Stem(string path) { return Regex.Replace(Path.GetFileNameWithoutExtension(path)??"",@"_v\d+$",""); }
        public static Dictionary<string,string> ParseModInfo(string text) {
            if(String.IsNullOrEmpty(text) || !text.StartsWith("LOH_ModInfo 1",StringComparison.Ordinal)) return null;
            var r=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string part in text.Substring(13).Split(new[]{';','\n'},StringSplitOptions.RemoveEmptyEntries)) { int eq=part.IndexOf('='); if(eq>0) r[part.Substring(0,eq).Trim()]=part.Substring(eq+1).Trim(); }
            return r;
        }
        // Order: our LOH_ModInfo export, the Windows version resource, then "<name> 1.2.3" text inside the DLL.
        public static DllVersion Detect(string path) {
            FileInfo file;try{file=new FileInfo(path);if(!file.Exists)return DllVersion.None;}catch{return DllVersion.None;}
            lock(cache){Cached c;if(cache.TryGetValue(path,out c)&&c.Length==file.Length&&c.Write==file.LastWriteTimeUtc)return c.Value;}
            var v=new DllVersion();byte[] bytes=null;
            try{bytes=Pe.ReadBytes(path,96L<<20);v.Pe=Pe.Parse(bytes);}catch(Exception e){v.Pe=new PeInfo();v.Pe.Error=e.Message;}
            var info=ModVersion.ParseModInfo(v.Pe.ModInfo);string text;
            if(info!=null && info.TryGetValue("version",out text) && text!=""){v.Text=text;v.Source="DLL 안 버전 정보(LOH_ModInfo)";}
            if(v.Text==""){try{var fvi=FileVersionInfo.GetVersionInfo(path);string fv=(fvi.ProductVersion??fvi.FileVersion??"").Trim();if(fv!=""){v.Text=fv;v.Source="파일 버전 정보";}}catch{}}
            if(v.Text=="" && bytes!=null){
                var m=Regex.Match(latin1.GetString(bytes),Regex.Escape(Stem(path))+@"[ _]v?(\d+(?:\.\d+){1,3})(?![\d.])");
                if(m.Success){v.Text=m.Groups[1].Value;v.Source="DLL 안 이름 옆 버전 글자";}
            }
            v.CanPause=v.Pe.Exports.Contains("LOH_ModManagerPause") && v.Pe.Exports.Contains("LOH_ModManagerResume");
            lock(cache)cache[path]=new Cached{Length=file.Length,Write=file.LastWriteTimeUtc,Value=v};
            return v;
        }
        // The version the running game actually loaded, from this run's log line "<name> 1.2.3 ... pid=N".
        public static string FromLog(string logPath,string stem,int pid,DateTime startUtc) {
            try {
                var file=new FileInfo(logPath);if(!file.Exists || (startUtc!=DateTime.MinValue && file.LastWriteTimeUtc<startUtc.AddSeconds(-2)))return "";
                byte[] head;using(var s=new FileStream(logPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){head=new byte[(int)Math.Min(s.Length,512*1024)];int done=0;while(done<head.Length){int n=s.Read(head,done,head.Length-done);if(n<=0)break;done+=n;}}
                var m=Regex.Match(Text.Decode(head),Regex.Escape(stem)+@"\s+v?(\d+(?:\.\d+){1,3})[^\r\n]*?pid=0*"+pid+@"\b");
                return m.Success?m.Groups[1].Value:"";
            } catch { return ""; }
        }
    }

    // ---------- the game process ----------
    sealed class GameProcess { public int Pid; public long Creation; public DateTime StartUtc=DateTime.MinValue; }
    static class GameProbe {
        public const string ProcessName="ThreeKingdom";
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessTimes(IntPtr process,out long creation,out long exit,out long kernel,out long user);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageNameW(IntPtr process,int flags,StringBuilder name,ref int size);
        // GetProcessesByName costs about 2 ms; enumerating modules is done separately and rarely.
        // With a game folder, a ThreeKingdom.exe started from another folder (another install, a test copy) is not ours.
        public static List<GameProcess> Find(string game=null) {
            var list=new List<GameProcess>();Process[] found;
            try{found=Process.GetProcessesByName(ProcessName);}catch{return list;}
            string folder=game==null?null:Path.GetFullPath(game).TrimEnd('\\');
            foreach(var p in found){using(p){
                var g=new GameProcess{Pid=p.Id};IntPtr h=OpenProcess(0x1000,false,p.Id);string image=null;
                if(h!=IntPtr.Zero){
                    long c,e,k,u;if(GetProcessTimes(h,out c,out e,out k,out u)){g.Creation=c;g.StartUtc=DateTime.FromFileTimeUtc(c);}
                    var name=new StringBuilder(1024);int size=name.Capacity;if(QueryFullProcessImageNameW(h,0,name,ref size))image=name.ToString();
                    CloseHandle(h);
                }
                if(folder!=null&&image!=null&&!String.Equals(Path.GetDirectoryName(image),folder,StringComparison.OrdinalIgnoreCase))continue;
                list.Add(g);
            }}
            list.Sort((a,b)=>b.Creation.CompareTo(a.Creation));return list;
        }
        public static HashSet<string> LoadedModules(int pid) {
            try{using(var p=Process.GetProcessById(pid)){var r=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(ProcessModule m in p.Modules)r.Add(m.FileName);return r;}}catch{return null;}
        }
    }

    // ---------- what the bootstrap entry reported (LOH_ModManager\Runtime-<pid>-<entry>.ini) ----------
    sealed class RuntimeModule { public string Id="",Entry="";public int State,Error,Detail; }
    sealed class RuntimeState {
        public bool Found,ConfigError;public string Revision="";public DateTime LastWriteUtc;public uint Heartbeat;
        public readonly List<string> Entries=new List<string>();
        public readonly Dictionary<string,RuntimeModule> Modules=new Dictionary<string,RuntimeModule>(StringComparer.OrdinalIgnoreCase);
        public static readonly RuntimeState Empty=new RuntimeState();
    }
    static class RuntimeReader {
        static readonly Regex name=new Regex(@"^Runtime-(\d+)(?:-(.+))?\.ini$",RegexOptions.IgnoreCase);
        public static RuntimeState Read(string managerDir,GameProcess game) {
            var rs=new RuntimeState();if(game==null)return rs;string[] files;
            try{files=Directory.GetFiles(managerDir,"Runtime-"+game.Pid+"*.ini");}catch{return rs;}
            Array.Sort(files,StringComparer.OrdinalIgnoreCase);
            foreach(string file in files){
                var m=name.Match(Path.GetFileName(file));if(!m.Success||m.Groups[1].Value!=game.Pid.ToString(CultureInfo.InvariantCulture))continue;
                var ini=IniFile.Read(file);if(ini==null||ini.Section("Manager")==null)continue;
                long creation=(ini.Long("Manager","CreationHigh")<<32)|(ini.Long("Manager","CreationLow")&0xffffffffL);
                if(game.Creation!=0&&creation!=game.Creation)continue; // an older process that had the same pid
                rs.Found=true;rs.Revision=ini.Get("Manager","Revision");rs.Heartbeat=(uint)ini.Long("Manager","Heartbeat");
                if(ini.Get("Manager","Error")!="")rs.ConfigError=true;
                string entry=ini.Get("Manager","Entry");if(entry=="")entry=m.Groups[2].Value;if(entry!="")rs.Entries.Add(entry);
                try{var w=File.GetLastWriteTimeUtc(file);if(w>rs.LastWriteUtc)rs.LastWriteUtc=w;}catch{}
                foreach(string section in ini.Order){
                    if(section.Equals("Manager",StringComparison.OrdinalIgnoreCase))continue;
                    var mod=new RuntimeModule{Id=section,Entry=entry,State=(int)ini.Long(section,"State"),Error=(int)ini.Long(section,"Error"),Detail=(int)ini.Long(section,"Detail")};
                    RuntimeModule old;if(rs.Modules.TryGetValue(section,out old)&&(old.State==3||old.State==4))continue;
                    rs.Modules[section]=mod;
                }
            }
            return rs;
        }
        // the run before: the newest Runtime file's process (the files stay after the game closes)
        public static RuntimeState Last(string managerDir){
            try{
                var newest=new DirectoryInfo(managerDir).GetFiles("Runtime-*.ini").OrderByDescending(f=>f.LastWriteTimeUtc).FirstOrDefault();if(newest==null)return new RuntimeState();
                var m=name.Match(newest.Name);if(!m.Success)return new RuntimeState();
                var ini=IniFile.Read(newest.FullName);long creation=ini==null?0:(ini.Long("Manager","CreationHigh")<<32)|(ini.Long("Manager","CreationLow")&0xffffffffL);
                return Read(managerDir,new GameProcess{Pid=int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),Creation=creation});
            }catch{return new RuntimeState();}
        }
        // a mod that did not work in a run: short label (card) and the full text, "" when it worked
        public static string ProblemLabel(RuntimeModule m){
            if(m==null)return "";
            if(m.State==2)return "파일 없음";if(m.State==5)return "이름 충돌";if(m.State==6)return "불러오기 실패";if(m.State==7)return "설정 오류";
            if(m.State==3||m.State==4){if(m.Detail==3)return "초기화 오류";if(m.Detail==4)return "스스로 멈춤";if(m.Detail==6)return "데이터 꺼짐";}
            return "";
        }
        public static string Problem(RuntimeModule m){
            if(ProblemLabel(m)=="")return "";
            return m.State==3||m.State==4?Status.DetailText(m.Detail):Status.StateText(m.State,m.Error);
        }
    }

    // ---------- registered mods as the PowerShell backend sees them ----------
    sealed class ModEntry {
        public string Id="",Name="",Path="",Version="",Route="",Data="",DataState="",Log="",Suffix="";
        public bool Enabled,Present,Prepared,External,Combined;
        // One list (0.7): besides DLL rows (Type "") there are data mods ("data"), workshop mods ("workshop") and installed
        // packages ("package": file replacement, patcher, entry-style DLL). Locked rows cannot be switched here yet.
        public string Type="",Source="",PackageKind="",Info="";public bool Locked;public int Missing;
        public bool Parked;   // package: a patcher switched off in the list (its changes undone, the record kept to switch it on again)
        public string Tool="";   // package: the kept copy of what was installed (LOH_ModManager\Tools\…), where its loh-mod.json is
        public bool NotList;   // data mod whose ModInfo.json is one object, not a list: the game skips it
        public bool IsDll { get { return Type==""; } }
        public bool IsData { get { return Type=="data"||Type=="workshop"; } }
        public string Kind { get { return Type=="data"?"데이터":Type=="workshop"?"창작마당":Type=="package"?PackageKind:Data!=""?"DLL+데이터":"DLL"; } }
        public ModEntry Clone() { return (ModEntry)MemberwiseClone(); }
        public static ModEntry From(Dictionary<string,object> d) {
            return new ModEntry{Id=J.S(d,"Id"),Name=J.S(d,"Name"),Path=J.S(d,"Path"),Version=J.S(d,"Version"),Route=J.S(d,"Route"),Data=J.S(d,"Data"),
                DataState=J.S(d,"DataState"),Log=J.S(d,"Log"),Suffix=J.S(d,"Suffix"),Enabled=J.B(d,"Enabled"),Present=J.B(d,"Present"),
                Prepared=J.B(d,"Prepared"),External=J.B(d,"External"),Combined=J.B(d,"Combined")};
        }
    }
    sealed class Issue { public string Level="",Text=""; }
    sealed class Catalog {
        public readonly List<ModEntry> Rows=new List<ModEntry>();
        public readonly List<Issue> Issues=new List<Issue>();
        public readonly List<string> Entries=new List<string>();
        public readonly List<KeyValuePair<string,string>> EntryKinds=new List<KeyValuePair<string,string>>();
        public readonly List<Dictionary<string,object>> Installed=new List<Dictionary<string,object>>();
        public readonly List<Dictionary<string,object>> Parked=new List<Dictionary<string,object>>();   // patchers switched off (끔), kept beside their record
        public Dictionary<string,object> PatchState;   // GameAssembly.dll patchers still applied? (Get-MMPatchState)
        public readonly List<string> StaleLinks=new List<string>();   // 세력금고-style game links made from an older GameAssembly.dll (Get-MMStaleLinks)
        public readonly List<Dictionary<string,object>> Foreign=new List<Dictionary<string,object>>();   // entry DLLs put in the game folder outside the manager (Get-MMForeignEntries)
        public readonly List<Dictionary<string,object>> TakenLoaders=new List<Dictionary<string,object>>();   // loader entries (BepInEx, MelonLoader) taken over before 0.12.44 (Get-MMTakenLoaders)
        public bool Managed,Complete;public string Revision="";
        public ModEntry Find(string id){foreach(var r in Rows)if(r.Id==id)return r;return null;}
    }
    static class CatalogReader {
        public static string ManagerDir(string game){return Path.Combine(game,"LOH_ModManager");}
        static readonly Dictionary<string,string> KnownSuffix=new Dictionary<string,string>{{"dokbak","hsd01"},{"jade","gh02"},{"editor","ige02"}};
        // Instant first paint from the saved catalog; the backend fills in the rest a moment later.
        public static Catalog Quick(string game) {
            var c=new Catalog();string dir=ManagerDir(game);
            var owner=J.ReadFile(Path.Combine(dir,"Owner.json"));var saved=J.ReadFile(Path.Combine(dir,"Catalog.json"));
            c.Managed=owner!=null&&J.S(owner,"State")!="Failed";c.Revision=J.S(owner,"Revision");
            c.Entries.AddRange(J.Strings(J.Get(owner,"Entries")));if(c.Entries.Count==0&&J.S(owner,"Entry")!="")c.Entries.Add(J.S(owner,"Entry"));
            foreach(var d in J.Items(J.Get(saved,"Modules"))){
                var r=ModEntry.From(d);try{r.Present=File.Exists(r.Path);}catch{}
                // Catalog.json has no log names; every bundled mod logs to <DLL name>.log (the backend fills the real value later).
                if(r.Log==""&&!r.External&&r.Path!="")r.Log=Path.GetFileNameWithoutExtension(r.Path)+".log";
                // Catalog.json has no data suffixes either; the bundled hybrid mods use these (Manager.Core.ps1 MMDefinitions).
                if(r.Suffix==""){string sfx;if(KnownSuffix.TryGetValue(r.Id,out sfx))r.Suffix=sfx;}
                c.Rows.Add(r);
            }
            return c;
        }
        public static Catalog FromSnapshot(Dictionary<string,object> snap) {
            var c=new Catalog{Complete=true};
            foreach(var d in J.Items(J.Get(snap,"Rows")))c.Rows.Add(ModEntry.From(d));
            foreach(var d in J.Items(J.Get(snap,"Issues")))c.Issues.Add(new Issue{Level=J.S(d,"Level"),Text=J.S(d,"Text")});
            foreach(var d in J.Items(J.Get(snap,"Entries")))c.EntryKinds.Add(new KeyValuePair<string,string>(J.S(d,"Name"),J.S(d,"Kind")));
            c.Installed.AddRange(J.Items(J.Get(snap,"Installed")));c.Parked.AddRange(J.Items(J.Get(snap,"Parked")));c.PatchState=J.Obj(J.Get(snap,"PatchState"));
            foreach(var d in J.Items(J.Get(snap,"StaleLinks")))c.StaleLinks.Add(J.S(d,"Name"));
            c.Foreign.AddRange(J.Items(J.Get(snap,"Foreign")));
            c.TakenLoaders.AddRange(J.Items(J.Get(snap,"TakenLoaders")));
            var owner=J.Obj(J.Get(snap,"Owner"));c.Managed=J.B(snap,"Managed");c.Revision=J.S(owner,"Revision");
            c.Entries.AddRange(J.Strings(J.Get(owner,"Entries")));if(c.Entries.Count==0&&J.S(owner,"Entry")!="")c.Entries.Add(J.S(owner,"Entry"));
            return c;
        }
    }

    // ---------- screen state of one mod row ----------
    enum Badge { Off, Planned, Running, Waiting, Error, Unknown }
    // Quiet: the usual "nothing to do" line — the card hides it (the detail panel still shows it)
    sealed class RowState { public Badge Badge;public string BadgeText="",Summary="";public bool ActualOn,Quiet; }
    static class Status {
        // Runtime-*.ini State codes written by src/entry.c (docs/상태표.md).
        public static string StateText(int state,int error) {
            switch(state){
                case 1:return "이번 실행에서 꺼 둠";case 2:return "DLL 파일을 찾지 못함";case 3:return "모드매니저가 불러옴";case 4:return "이미 올라와 있던 같은 DLL을 그대로 씀";
                case 5:return "다른 경로의 같은 이름 DLL이 이미 쓰는 중";case 6:return "DLL을 불러오지 못함(Windows 오류 "+error+")";case 7:return "등록 경로나 모드 이름이 잘못됨";
                default:return "상태 코드 "+state;
            }
        }
        // LOH_ModManagerState() return values (Detail).
        public static string DetailText(int detail) {
            switch(detail){
                case 0:return "불러옴 · 이 모드는 상태를 알려 주지 않음";case 1:return "시작 준비 중";case 2:return "정상 동작(모드가 직접 확인)";
                case 3:return "게임 버전·함수 연결 또는 초기화 오류를 모드가 알림 — 로그 확인";case 4:return "모드가 스스로 동작을 멈췄다고 알림";
                case 5:return "일부 기능만 준비됨";case 6:return "게임 안 모드 목록에서 이 모드의 데이터가 꺼져 있음";case 7:return "일시중지됨";
                default:return "알 수 없는 응답 "+detail;
            }
        }
        public static RowState Compute(ModEntry row,bool want,bool unsaved,bool reserved,GameProcess game,RuntimeState rt,ICollection<string> loaded,string runningVersion,string fileVersion,bool? listedOn=null) {
            var r=new RowState();string tail=unsaved?" · 아직 저장 안 함":reserved?" · 예약됨(게임을 끄면 적용)":"";
            if(game==null){
                if(!want){r.Badge=Badge.Off;r.BadgeText="꺼짐";r.Summary=(row.Enabled?"저장하면 다음 실행부터 빠집니다":"다음 실행에서 불러오지 않습니다")+tail;r.Quiet=!row.Enabled&&tail=="";}
                else if(!row.Present&&!row.Prepared){r.Badge=Badge.Error;r.BadgeText="파일 없음";r.Summary="DLL 파일을 찾지 못했습니다: "+row.Path;}
                else if(row.Data!=""&&row.DataState=="데이터 없음"){r.Badge=Badge.Error;r.BadgeText="데이터 없음";r.Summary="필요한 데이터 폴더(MODs\\"+row.Data+")가 없습니다";}
                else{r.Badge=Badge.Planned;r.BadgeText="켜짐";bool listOff=row.Data!=""&&listedOn==false;
                    r.Summary="게임을 켜면 자동으로 함께 켜집니다"+(tail==""&&!listOff?" · 따로 할 일 없음":tail)+(listOff?" · 게임 모드 목록에서는 꺼져 있음(게임 안에서 켜 주세요)":"");r.Quiet=tail==""&&!listOff;}
                return r;
            }
            RuntimeModule m=null;if(rt!=null)rt.Modules.TryGetValue(row.Id,out m);
            bool here=loaded!=null&&row.Path!=""&&loaded.Contains(row.Path);
            bool sameName=loaded!=null&&!here&&row.Path!=""&&loaded.Any(p=>String.Equals(System.IO.Path.GetFileName(p),System.IO.Path.GetFileName(row.Path),StringComparison.OrdinalIgnoreCase));
            if(m!=null&&(m.State==3||m.State==4)){
                r.ActualOn=true;r.Badge=Badge.Running;r.BadgeText="실행 중";r.Summary=DetailText(m.Detail);r.Quiet=m.State==3&&(m.Detail==0||m.Detail==2);
                if(m.Detail==3){r.Badge=Badge.Error;r.BadgeText="초기화 오류";}
                else if(m.Detail==4){r.Badge=Badge.Error;r.BadgeText="멈춤";}
                // A mod may say "data off" before the game has loaded mod data; trust the game's own mod list when it says on.
                else if(m.Detail==6){if(listedOn==true)r.Summary="게임 모드 목록에서는 켜져 있음 · 모드가 아직 데이터를 확인하지 못했다고 알림";else{r.Badge=Badge.Error;r.BadgeText="데이터 꺼짐";}}
                else if(m.Detail==7){r.Badge=Badge.Off;r.BadgeText="일시중지";}
                if(m.State==4)r.Summary+=" · 다른 로더가 먼저 올려 둔 DLL";
            } else if(m!=null&&m.State==1){
                r.ActualOn=here||sameName;
                if(r.ActualOn){r.Badge=Badge.Error;r.BadgeText="끄기 무시됨";r.Summary="꺼 두었지만 모드매니저 밖 다른 로더가 이 DLL을 올렸습니다";}
                else{r.Badge=Badge.Off;r.BadgeText="꺼짐";r.Summary="이번 실행에서 꺼 둠";}
            } else if(m!=null){
                r.ActualOn=here;r.Badge=Badge.Error;r.Summary=StateText(m.State,m.Error);
                r.BadgeText=m.State==2?"파일 없음":m.State==5?"이름 충돌":m.State==6?"불러오기 실패":"설정 오류";
            } else if(here||sameName){
                r.ActualOn=true;r.Badge=sameName?Badge.Error:Badge.Running;r.BadgeText=sameName?"이름 충돌":"실행 중";
                r.Summary=sameName?"다른 경로의 같은 이름 DLL이 올라와 있습니다":"모드매니저 밖 다른 로더가 불러옴";
            } else if(rt==null||!rt.Found){
                r.Badge=Badge.Unknown;r.BadgeText="응답 없음";r.Summary="게임은 켜져 있지만 모드매니저와 아직 연결되지 않았습니다(시작 중이거나 모드매니저 없이 켬)";
            } else {
                r.Badge=Badge.Off;r.BadgeText="꺼짐";r.Summary="이번 실행 설정에 없던 모드";
            }
            // Turned off after this run failed to load it: the next run simply leaves it out.
            if(r.Badge==Badge.Error&&!want&&!r.ActualOn){r.Badge=Badge.Waiting;r.BadgeText="다음 실행 대기";r.Summary="이번 실행: "+r.Summary+" · 다음 실행부터 꺼짐"+tail;return r;}
            if(r.Badge!=Badge.Running&&r.Badge!=Badge.Off)r.Quiet=false;
            if(r.Badge!=Badge.Error&&r.Badge!=Badge.Unknown&&r.ActualOn!=want){
                r.Quiet=false;r.Badge=Badge.Waiting;r.BadgeText="다음 실행 대기";r.Summary=(want?"지금은 꺼져 있고 다음 실행부터 켜집니다":"지금은 실행 중이고 다음 실행부터 꺼집니다")+tail;
            } else if(r.Badge==Badge.Running&&runningVersion!=""&&fileVersion!=""&&runningVersion!=fileVersion){
                r.Quiet=false;r.Badge=Badge.Waiting;r.BadgeText="새 버전 대기";r.Summary="지금 "+runningVersion+" 실행 중 · 다음 실행에 "+fileVersion;
            } else if(unsaved||reserved){
                if(r.Badge==Badge.Running||r.Badge==Badge.Off){r.Quiet=false;r.Badge=Badge.Waiting;r.BadgeText="다음 실행 대기";r.Summary=(want?"켜짐 유지, 순서·불러오는 방식 변경":"꺼짐 유지")+tail;}
            }
            return r;
        }
    }

    // ---------- PowerShell backend for changes (apply / remove / restore / inventory) ----------
    static class Backend {
        public static string AppDir=AppDomain.CurrentDomain.BaseDirectory;
        static string Quote(string s){return "\""+s.Replace("\"","\\\"").TrimEnd('\\')+"\"";}
        public static Dictionary<string,object> Run(string action,string game,object request) {
            string temp=null;
            try{
                if(request!=null){temp=Path.Combine(Path.GetTempPath(),"loh-manager-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllText(temp,J.Serializer.Serialize(request),new UTF8Encoding(true));}
                string exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
                string args="-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(Path.Combine(AppDir,"Manager.Cli.ps1"))+" -Action "+action+" -GamePath "+Quote(game);
                if(temp!=null)args+=" -ChoicesFile "+Quote(temp);
                var info=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                using(var p=Process.Start(info)){
                    var error=new StringBuilder();p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(error)error.AppendLine(e.Data);};p.BeginErrorReadLine();
                    string output=p.StandardOutput.ReadToEnd();p.WaitForExit();
                    Dictionary<string,object> response=null;
                    try{response=J.Serializer.Deserialize<Dictionary<string,object>>(output.Trim());}catch{}
                    if(response==null){string e;lock(error)e=error.ToString().Trim();throw new Exception(e==""?"관리 기능의 응답을 읽지 못했습니다.":e);}
                    if(!J.B(response,"Success"))throw new Exception(J.S(response,"Error"));
                    return J.Obj(J.Get(response,"Result"))??new Dictionary<string,object>();
                }
            }finally{if(temp!=null)try{File.Delete(temp);}catch{}}
        }
    }

    // ---------- the post of each mod (DC gallery) and the version in its title ----------
    // Our mods are posted on the 삼국지14 minor gallery; the title carries the version: one mod per post → one number
    // ("…(1.3.0)", "…v8.74"), several mods in one post → "name number" pairs ("취향표시 0.5.2 · 금란선옥 1.1.0").
    // Any mod can get a link from the player (Links.txt). Nothing is downloaded: the player decides and downloads from the post.
    sealed class ModPost { public string Id="",Name="",Url="";public string[] Names=new string[0]; }
    static class Posts {
        const string Gallery="https://gall.dcinside.com/mgallery/board/view/?id=san14&no=";
        public static readonly List<ModPost> Known=new List<ModPost>{
            new ModPost{Id="manager",Name="모드매니저(이 프로그램)",Url=Gallery+"78820",Names=new[]{"모드매니저","모드 매니저","모드관리","모드 관리"}},
            new ModPost{Id="dokbak",Name="독박육아",Url=Gallery+"78129",Names=new[]{"독박육아"}},
            new ModPost{Id="gift",Name="선물·화제 취향 표시",Url=Gallery+"78833",Names=new[]{"취향표시","취향 표시"}},
            new ModPost{Id="jade",Name="금란선옥",Url=Gallery+"78833",Names=new[]{"금란선옥"}},
            new ModPost{Id="pad",Name="패드 모드",Url=Gallery+"76033",Names=new[]{"패드모드","패드 모드"}},
            // 창작마당 모드의 하위 패치(번역): "wspatch:<창작마당 번호>"
            new ModPost{Id="wspatch:3473873587",Name="춘화추월 한글화",Url=Gallery+"70813",Names=new[]{"춘화추월 한글화","한글화"}},
        };
        static string File{get{return Path.Combine(Store.Root,"Links.txt");}}
        // the player's own links: one "id<TAB>url" per line; an empty url hides a built-in one
        public static Dictionary<string,string> Saved(){
            var map=new Dictionary<string,string>();
            try{if(System.IO.File.Exists(File))foreach(var line in System.IO.File.ReadAllLines(File,Encoding.UTF8)){int t=line.IndexOf('\t');if(t>0)map[line.Substring(0,t)]=line.Substring(t+1).Trim();}}catch{}
            return map;
        }
        public static void Save(string id,string url){
            var map=Saved();map[id]=(url??"").Trim();
            try{Directory.CreateDirectory(Store.Root);System.IO.File.WriteAllLines(File,map.Select(p=>p.Key+"\t"+p.Value),new UTF8Encoding(false));}catch{}
        }
        public static ModPost Known1(string id){return Known.FirstOrDefault(k=>k.Id==id);}
        public static string LinkFor(string id){string u;if(Saved().TryGetValue(id,out u))return u;var k=Known1(id);return k==null?"":k.Url;}
        public static bool IsWeb(string url){Uri u;return Uri.TryCreate(url??"",UriKind.Absolute,out u)&&(u.Scheme==Uri.UriSchemeHttp||u.Scheme==Uri.UriSchemeHttps||u.Scheme=="steam");}
        // "v1.2.3" / "1.2" / "8.84" → "1.2.3"; "" when there is none
        public static string Clean(string v){var m=Regex.Match(v??"",@"\d+(?:\.\d+)+|\d+");return m.Success?m.Value:"";}
        public static int Compare(string a,string b){
            var x=Clean(a).Split('.');var y=Clean(b).Split('.');
            for(int i=0;i<Math.Max(x.Length,y.Length);i++){int p=0,q=0;if(i<x.Length)int.TryParse(x[i],out p);if(i<y.Length)int.TryParse(y[i],out q);if(p!=q)return p<q?-1:1;}
            return 0;
        }
        // The version for one mod in a post title: "name 1.2.3" (name, then optional space/(/v), then the number) wins;
        // otherwise, when this post is only this mod's, the title's single version number. "" when not found.
        public static string VersionIn(string title,IEnumerable<string> names,bool onlyThisMod){
            title=title??"";
            // spaces in the name do not count: "빠른거래" finds "빠른 거래 모드 0.6.5" (11-02)
            foreach(var n in names){string loose=String.Join(@"\s*",(n??"").Where(ch=>!Char.IsWhiteSpace(ch)).Select(ch=>Regex.Escape(ch.ToString())));if(loose=="")continue;
                var m=Regex.Match(title,loose+@"\s*[(\[]?\s*[vV]?(\d+(?:\.\d+)+)");if(m.Success)return m.Groups[1].Value;}
            if(!onlyThisMod)return "";
            var all=Regex.Matches(title,@"(?<![\d.])[vV]?(\d+(?:\.\d+)+)(?![\d.])").Cast<Match>().Select(m=>m.Groups[1].Value).Distinct().ToList();
            return all.Count==1?all[0]:"";
        }
        // A date in a title, for posts that mark updates by day instead of a version ("10/22 업데이트", "10월 22일", "(261017)"):
        // the latest one; a month/day without a year is this year, or last year when that would be in the future. null when none.
        public static DateTime? DateIn(string title,DateTime today){
            DateTime? best=null;title=title??"";
            Action<int,int,int> take=(y,m,d)=>{if(m<1||m>12||d<1||d>DateTime.DaysInMonth(y,m))return;var t=new DateTime(y,m,d);if(best==null||t>best)best=t;};
            Action<int,int> monthDay=(m,d)=>{if(m<1||m>12||d<1||d>31)return;int y=today.Year;if(d<=DateTime.DaysInMonth(y,m)&&new DateTime(y,m,d)>today.AddDays(1))y--;take(y,m,d);};
            foreach(Match m in Regex.Matches(title,@"(?<![\d./])(\d{1,2})/(\d{1,2})(?![\d/])"))monthDay(int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value));
            foreach(Match m in Regex.Matches(title,@"(\d{1,2})\s*월\s*(\d{1,2})\s*일"))monthDay(int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value));
            foreach(Match m in Regex.Matches(title,@"(?<![\d.])(2\d)(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])(?![\d.])")){int y=2000+int.Parse(m.Groups[1].Value);if(y<=today.Year)take(y,int.Parse(m.Groups[2].Value),int.Parse(m.Groups[3].Value));}
            return best;
        }
        // the post's title from its page (the subject line, else <title> without " - 갤러리 이름")
        public static string TitleOf(string html){
            var m=Regex.Match(html??"","<span class=\"title_subject\">([^<]*)");
            string t=m.Success?m.Groups[1].Value:"";
            if(t==""){m=Regex.Match(html??"","<title>([^<]*)</title>",RegexOptions.IgnoreCase);if(m.Success){t=m.Groups[1].Value;int cut=t.LastIndexOf(" - ");if(cut>0)t=t.Substring(0,cut);}}
            return System.Net.WebUtility.HtmlDecode(t).Trim();
        }
        // A post's title for the update check. Opening a DC post counts a view (every time, cookie or not), its gallery's subject
        // search list does not: so DC posts are looked up there by number, never opened. Other sites: the page itself.
        // A title read in the last 10 minutes is reused, so pressing the button again asks nothing.
        // Every answer is kept 10 minutes — found, not found and failed alike — in PostTitles.txt, so neither pressing again
        // nor reopening the window asks the site again.
        public static readonly TimeSpan Reuse=TimeSpan.FromMinutes(10);
        static Dictionary<string,Tuple<DateTime,string,string>> recent;   // url → (when, title, error)
        static string RecentFile{get{return Path.Combine(Store.Root,"PostTitles.txt");}}
        static Dictionary<string,Tuple<DateTime,string,string>> Recent(){
            if(recent!=null)return recent;recent=new Dictionary<string,Tuple<DateTime,string,string>>();
            try{if(System.IO.File.Exists(RecentFile))foreach(var line in System.IO.File.ReadAllLines(RecentFile,Encoding.UTF8)){
                var p=line.Split('\t');long ticks;if(p.Length>=4&&long.TryParse(p[0],out ticks))recent[p[1]]=Tuple.Create(new DateTime(ticks),p[2],p[3]);}}catch{}
            return recent;
        }
        static void Remember(string url,string title,string error){
            lock(typeof(Posts)){
                var map=Recent();map[url]=Tuple.Create(DateTime.Now,title??"",error??"");
                try{Directory.CreateDirectory(Store.Root);System.IO.File.WriteAllLines(RecentFile,map.Where(x=>DateTime.Now-x.Value.Item1<TimeSpan.FromDays(1))
                    .Select(x=>x.Value.Item1.Ticks+"\t"+x.Key+"\t"+x.Value.Item2.Replace('\t',' ')+"\t"+x.Value.Item3.Replace('\t',' ').Replace('\n',' ').Replace('\r',' ')),new UTF8Encoding(false));}catch{}
            }
        }
        public static string TitleFor(string url,IEnumerable<string> keywords){
            lock(typeof(Posts)){Tuple<DateTime,string,string> r;if(Recent().TryGetValue(url,out r)&&DateTime.Now-r.Item1<Reuse&&DateTime.Now>=r.Item1){if(r.Item3!="")throw new Exception(r.Item3);return r.Item2;}}
            string title;Uri u;
            try{
                if(Uri.TryCreate(url??"",UriKind.Absolute,out u)&&u.Host.EndsWith("dcinside.com",StringComparison.OrdinalIgnoreCase))title=TitleFromSearch(u,keywords);
                else title=TitleOf(Fetch(url));
            }catch(Exception e){Remember(url,"",e.Message);throw;}
            Remember(url,title,"");
            return title;
        }
        // at least 0.4 s between any two page requests to the site, across all posts of one check
        static DateTime lastAsk=DateTime.MinValue;
        static string Gently(string address){
            lock(typeof(Posts)){var wait=lastAsk.AddMilliseconds(400)-DateTime.Now;if(wait>TimeSpan.Zero)System.Threading.Thread.Sleep(wait);lastAsk=DateTime.Now;}
            return Fetch(address);
        }
        // gall.dcinside.com/[mgallery/|mini/]board/view/?id=X&no=N, or m.dcinside.com/board/X/N (gallery kind unknown: minor first)
        static string TitleFromSearch(Uri u,IEnumerable<string> keywords){
            string id="",no="";var lists=new List<string>();
            var q=Regex.Match(u.Query,@"[?&]id=([^&]+)");var n=Regex.Match(u.Query,@"[?&]no=(\d+)");
            if(q.Success&&n.Success){id=q.Groups[1].Value;no=n.Groups[1].Value;int cut=u.AbsolutePath.IndexOf("/board/",StringComparison.OrdinalIgnoreCase);lists.Add("https://gall.dcinside.com"+(cut>0?u.AbsolutePath.Substring(0,cut):"")+"/board/lists/");}
            else{var m=Regex.Match(u.AbsolutePath,@"/board/([^/]+)/(\d+)");if(m.Success){id=m.Groups[1].Value;no=m.Groups[2].Value;lists.Add("https://gall.dcinside.com/mgallery/board/lists/");lists.Add("https://gall.dcinside.com/board/lists/");lists.Add("https://gall.dcinside.com/mini/board/lists/");}}
            if(id=="")throw new Exception("디시 게시글 주소 모양이 아닙니다");
            long target=long.Parse(no);int asked=0;const int Most=12;   // pages for one post, at most
            Func<string,string> get=address=>{if(asked>=Most)throw new Exception("이 글을 찾느라 너무 많이 물어봐서 멈췄습니다");asked++;return Gently(address);};
            string query="?id="+Uri.EscapeDataString(id);
            // 1) the subject search: one or two pages for a recent post whose title holds the mod's name (DC searches only about
            //    the newest 10,000 posts this way)
            foreach(var kw in keywords.Where(k=>(k??"").Trim().Length>=2).Select(k=>k.Trim()).Distinct().Take(3))
                foreach(var list in lists){
                    for(int page=1;page<=2&&asked<4;page++){
                        string html;try{html=get(list+query+"&s_type=search_subject&s_keyword="+Uri.EscapeDataString(kw)+"&page="+page);}catch{break;}
                        string found=RowTitle(html,no);if(found!="")return found;
                        var nos=ListNumbers(html);
                        if(nos.Count<20||nos.Min()<target)break;   // newest first: past the number, or the last page
                    }
                }
            // 2) any title, any age: jump through the plain list (100 a page) to where the number should be, a few times at most
            foreach(var list in lists){
                List<long> first;try{first=ListNumbers(get(list+query+"&list_num=100&page=1"));}catch{continue;}
                if(first.Count==0)continue;   // not this kind of gallery
                long top=first.Max();if(target>top)return "";
                int low=1,high=int.MaxValue;int page=(int)((top-target)/100)+1;
                for(int step=0;asked<Most&&low<=high;step++){
                    page=Math.Max(low,Math.Min(high,page));
                    string html;try{html=get(list+query+"&list_num=100&page="+page);}catch{return "";}
                    string found=RowTitle(html,no);if(found!="")return found;
                    var nos=ListNumbers(html);
                    if(nos.Count==0){high=page-1;page=(low+Math.Min(high,page))/2;continue;}   // past the end
                    long max=nos.Max(),min=nos.Min();
                    if(max>target&&min<target)return "";   // where it would be, and it is not there: deleted
                    double span=Math.Max(100,max-min+1);   // the numbers one page spans here (deleted posts widen it)
                    if(min>target){low=page+1;page+=Math.Max(1,(int)Math.Round((min-target)/span+0.5));}   // newer than the number: further pages
                    else{high=page-1;page-=Math.Max(1,(int)Math.Round((target-max)/span+0.5));}
                }
                return "";
            }
            return "";
        }
        // the post numbers of a list page, newest first; notices and surveys pinned on top are not counted
        public static List<long> ListNumbers(string html){
            return Regex.Matches(html??"","<tr class=\"ub-content[^\"]*\"[^>]*data-no=\"(\\d+)\"[^>]*data-type=\"([^\"]*)\"").Cast<Match>()
                .Where(x=>x.Groups[2].Value.IndexOf("notice",StringComparison.OrdinalIgnoreCase)<0&&x.Groups[2].Value.IndexOf("survey",StringComparison.OrdinalIgnoreCase)<0)
                .Select(x=>long.Parse(x.Groups[1].Value)).ToList();
        }
        // one row of a gallery list: <tr data-no="N"> … <a href="…no=N…">title</a>
        public static string RowTitle(string html,string no){
            var hit=Regex.Match(html??"","data-no=\""+no+"\"[\\s\\S]{0,2000}?<a [^>]*no="+no+"[^0-9][^>]*>([\\s\\S]*?)</a>");
            return hit.Success?System.Net.WebUtility.HtmlDecode(Regex.Replace(hit.Groups[1].Value,"<[^>]+>","")).Trim():"";
        }
        public static string Fetch(string url){
            System.Net.ServicePointManager.SecurityProtocol|=System.Net.SecurityProtocolType.Tls12;
            var request=(System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            request.UserAgent="LOH-ModManager (+https://github.com/nugssorc/loh-modmanager)";   // says what it is
            request.Timeout=12000;request.ReadWriteTimeout=12000;request.AutomaticDecompression=System.Net.DecompressionMethods.GZip|System.Net.DecompressionMethods.Deflate;
            using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return reader.ReadToEnd();
        }
    }

    // ---------- 패치노트: what each update check saw in a mod's post title, and the version this PC had then ----------
    // %LOCALAPPDATA%\LOH_ModManager\Versions.json — mod id → {Posts:[{At,Version,Title}], Used:[{At,Version}]}, oldest first.
    static class History {
        static string File{get{return Path.Combine(Store.Root,"Versions.json");}}
        static Dictionary<string,object> Load(){return J.ReadFile(File)??new Dictionary<string,object>();}
        public static void Note(IEnumerable<Tuple<string,string,string,string>> seen){   // id, version in the title, title, version used here
            try{
                var all=Load();bool changed=false;string today=DateTime.Now.ToString("yyyy-MM-dd");
                foreach(var x in seen){
                    if(String.IsNullOrEmpty(x.Item1))continue;
                    var mod=J.Obj(J.Get(all,x.Item1));if(mod==null){mod=new Dictionary<string,object>();all[x.Item1]=mod;}
                    var posts=J.Items(J.Get(mod,"Posts")).ToList();var used=J.Items(J.Get(mod,"Used")).ToList();
                    if(x.Item3!=""&&(posts.Count==0||J.S(posts[posts.Count-1],"Title")!=x.Item3)){posts.Add(new Dictionary<string,object>{{"At",today},{"Version",x.Item2},{"Title",x.Item3}});changed=true;}
                    if(x.Item4!=""&&(used.Count==0||J.S(used[used.Count-1],"Version")!=x.Item4)){used.Add(new Dictionary<string,object>{{"At",today},{"Version",x.Item4}});changed=true;}
                    mod["Posts"]=posts.Skip(Math.Max(0,posts.Count-50)).ToList();mod["Used"]=used.Skip(Math.Max(0,used.Count-50)).ToList();
                }
                if(changed)J.WriteFile(File,all);
            }catch{}
        }
        public static List<Dictionary<string,object>> Posts(string id){return J.Items(J.Get(J.Obj(J.Get(Load(),id)),"Posts")).ToList();}
        public static List<Dictionary<string,object>> Used(string id){return J.Items(J.Get(J.Obj(J.Get(Load(),id)),"Used")).ToList();}
    }

    // ---------- mod info file (loh-mod.json, public/모드정보_양식.md) ----------
    // Beside the mod: loh-mod.json in its own folder, or <DLL name>.loh-mod.json (DLLs right in the game folder or sharing one).
    // Gives the post link, version, settings files, and the code it changes, so DLL mods that change the same place can be shown.
    sealed class ModChange { public string Kind="",Target="",Address="",Note="";public long Size; }
    sealed class ModManifest { public string File="",Name="",Version="",Page="",Game="",Github="";public int Format;public List<string> Settings=new List<string>();public List<ModChange> Changes=new List<ModChange>();
        public List<string[]> Notes=new List<string[]>(); }   // Github: "owner/repo" whose releases it ships on; Notes: [version, date, text] as the mod wrote them
    static class Manifests {
        public const string FileName="loh-mod.json";
        static readonly Dictionary<string,Tuple<long,ModManifest>> cache=new Dictionary<string,Tuple<long,ModManifest>>(StringComparer.OrdinalIgnoreCase);
        // the folder a mod lives in, and whether it is the game folder itself (then only <DLL>.loh-mod.json counts)
        public static string FolderOf(ModEntry r,string game,out bool atRoot){
            atRoot=false;
            try{
                if(r!=null&&r.Type=="package"){if(r.Tool!=""&&Directory.Exists(r.Tool))return r.Tool;   // the copy the manager keeps of it
                    string kept=Path.Combine(game??"","LOH_ModManager","Installed",r.Id.Substring(4));return System.IO.File.Exists(Path.Combine(kept,FileName))?kept:null;}   // else the loh-mod.json kept with its record
                if(r==null||String.IsNullOrEmpty(r.Path))return null;
                string dir=r.IsData||r.Type=="workshop"?r.Path:Path.GetDirectoryName(r.Path);if(dir==null||!Directory.Exists(dir))return null;
                atRoot=String.Equals(Path.GetFullPath(dir).TrimEnd('\\'),Path.GetFullPath(game??"").TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);
                return dir;
            }catch{return null;}
        }
        public static ModManifest For(ModEntry r,string game){
            bool atRoot;string dir=FolderOf(r,game,out atRoot);if(dir==null)return null;
            string own=r.IsDll?Path.Combine(dir,Path.GetFileNameWithoutExtension(r.Path)+"."+FileName):null;
            if(own!=null&&System.IO.File.Exists(own))return Read(own);
            string shared=Path.Combine(dir,FileName);return !atRoot&&System.IO.File.Exists(shared)?Read(shared):null;
        }
        public static ModManifest Read(string file){
            try{
                var fi=new FileInfo(file);long stamp=fi.LastWriteTimeUtc.Ticks^(fi.Length<<44);Tuple<long,ModManifest> hit;   // size too: a rewrite within one clock tick keeps the time
                if(cache.TryGetValue(file,out hit)&&hit.Item1==stamp)return hit.Item2;
                var o=J.ReadFile(file);ModManifest m=null;
                if(o!=null){
                    m=new ModManifest{File=file,Name=J.S(o,"name").Trim(),Version=J.S(o,"version").Trim(),Page=J.S(o,"page").Trim(),Game=J.S(o,"game").Trim()};
                    int f;int.TryParse(J.S(o,"format"),out f);m.Format=f;m.Settings=J.Strings(J.Get(o,"settings")).Select(x=>x.Trim()).Where(x=>x!="").ToList();
                    string gh=J.S(o,"github").Trim();if(System.Text.RegularExpressions.Regex.IsMatch(gh,@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))m.Github=gh;
                    foreach(var n in J.Items(J.Get(o,"notes"))){string t=J.S(n,"text").Trim();if(t!="")m.Notes.Add(new[]{J.S(n,"version").Trim(),J.S(n,"date").Trim(),t});}
                    foreach(var ch in J.Items(J.Get(o,"changes"))){long size;long.TryParse(J.S(ch,"size"),out size);
                        m.Changes.Add(new ModChange{Kind=J.S(ch,"kind").Trim().ToLowerInvariant(),Target=J.S(ch,"target").Trim(),Address=J.S(ch,"address").Trim(),Note=J.S(ch,"note").Trim(),Size=size});}
                }
                cache[file]=Tuple.Create(stamp,m);return m;
            }catch{return null;}
        }
        // an internet shortcut (.url) in the mod's own folder: its URL= line
        public static string UrlIn(ModEntry r,string game){
            bool atRoot;string dir=FolderOf(r,game,out atRoot);if(dir==null||atRoot)return "";
            try{foreach(var f in Directory.GetFiles(dir,"*.url"))foreach(var line in System.IO.File.ReadAllLines(f))if(line.StartsWith("URL=",StringComparison.OrdinalIgnoreCase)){string u=line.Substring(4).Trim();if(Posts.IsWeb(u))return u;}}catch{}
            return "";
        }
        public static long Address(string text){
            text=(text??"").Trim();if(text.StartsWith("0x",StringComparison.OrdinalIgnoreCase))text=text.Substring(2);
            long v;return text!=""&&long.TryParse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out v)?v:-1;
        }
        // Two mods change the same place: the same target name, or (same game version) overlapping addresses.
        // Both hook → a soft overlap (hooks usually chain); a byte patch on either side → cannot work together.
        public static List<Conflict> Find(List<Tuple<string,ModManifest>> mods){
            var found=new List<Conflict>();
            for(int i=0;i<mods.Count;i++)for(int j=i+1;j<mods.Count;j++){
                var a=mods[i].Item2;var b=mods[j].Item2;if(a==null||b==null)continue;
                bool sameGame=a.Game!=""&&String.Equals(a.Game,b.Game,StringComparison.OrdinalIgnoreCase);
                var where=new List<string>();bool hard=false;
                foreach(var x in a.Changes)foreach(var y in b.Changes){
                    bool same=x.Target!=""&&String.Equals(x.Target,y.Target,StringComparison.OrdinalIgnoreCase);
                    if(!same&&sameGame){long p=Address(x.Address),q=Address(y.Address);if(p>=0&&q>=0)same=p<q+Math.Max(1,y.Size)&&q<p+Math.Max(1,x.Size);}
                    if(!same)continue;
                    hard|=x.Kind=="patch"||y.Kind=="patch";
                    string label=x.Target!=""?x.Target:y.Target!=""?y.Target:x.Address;if(!where.Contains(label))where.Add(label);
                }
                if(where.Count==0)continue;
                found.Add(new Conflict{A=mods[i].Item1,B=mods[j].Item1,Kind=hard?"dll-write":"dll-hook",Hard=hard,Count=where.Count,
                    Detail=(hard?"같은 게임 코드를 덮어씀: ":"같은 게임 함수에 끼어듦: ")+String.Join(", ",where.Take(3))+(where.Count>3?" 외 "+(where.Count-3)+"곳":"")});
            }
            return found;
        }
    }

    // ---------- profiles and reserved changes (%LOCALAPPDATA%\LOH_ModManager\<game>\) ----------
    // ---------- finding the game: a folder picked before, then every Steam library (other drives too) ----------
    static class GameFolder {
        const string AppId="3020510";
        static string File{get{return Path.Combine(Store.Root,"Game.txt");}}
        public static bool Valid(string folder){try{return !String.IsNullOrEmpty(folder)&&System.IO.File.Exists(Path.Combine(folder,"ThreeKingdom.exe"));}catch{return false;}}
        // A picked folder → the game folder: itself, a folder above it (picked MODs or another sub folder), or one below (picked common, steamapps or a library).
        public static string Resolve(string picked,out string note){
            note="";if(String.IsNullOrEmpty(picked))return null;
            try{
                picked=Path.GetFullPath(picked).TrimEnd('\\');if(Valid(picked))return picked;
                for(var up=Directory.GetParent(picked);up!=null;up=up.Parent)if(Valid(up.FullName)){note="고른 폴더("+picked+")가 게임 폴더 안쪽이라, 게임 폴더로 맞췄습니다: "+up.FullName;return up.FullName;}
                foreach(var down in new[]{Path.Combine(picked,"LegendOfHeros"),Path.Combine(picked,"common","LegendOfHeros"),Path.Combine(picked,"steamapps","common","LegendOfHeros"),InLibrary(picked)})
                    if(Valid(down)){note="고른 폴더 안에서 게임 폴더를 찾아 맞췄습니다: "+down;return down;}
            }catch{}
            return null;
        }
        // Steam's record of the installed game: build number and when Steam last updated it (steamapps\appmanifest_3020510.acf)
        public static Tuple<string,DateTime> Build(string game){
            try{
                var common=Directory.GetParent(Path.GetFullPath(game).TrimEnd('\\'));var steamapps=common==null?null:common.Parent;if(steamapps==null)return null;
                string acf=Path.Combine(steamapps.FullName,"appmanifest_"+AppId+".acf");if(!System.IO.File.Exists(acf))return null;
                string text=System.IO.File.ReadAllText(acf);var b=Regex.Match(text,"\"buildid\"\\s+\"(\\d+)\"");if(!b.Success)return null;
                var u=Regex.Match(text,"\"LastUpdated\"\\s+\"(\\d+)\"");long secs;
                DateTime when=u.Success&&long.TryParse(u.Groups[1].Value,out secs)&&secs>0?new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(secs).ToLocalTime():DateTime.MinValue;
                return Tuple.Create(b.Groups[1].Value,when);
            }catch{return null;}
        }
        public static string Remembered(){try{var t=System.IO.File.Exists(File)?System.IO.File.ReadAllText(File).Trim():"";return Valid(t)?t:null;}catch{return null;}}
        public static void Remember(string folder){try{Directory.CreateDirectory(Store.Root);System.IO.File.WriteAllText(File,folder);}catch{}}
        // libraryfolders.vdf: "path" "D:\\SteamLibrary" (new) or "1" "D:\\SteamLibrary" (old, numbered keys)
        public static List<string> Libraries(string vdf){
            var list=new List<string>();if(String.IsNullOrEmpty(vdf))return list;
            foreach(Match m in Regex.Matches(vdf,"^\\s*\"(path|\\d+)\"\\s+\"((?:[^\"\\\\]|\\\\.)+)\"",RegexOptions.Multiline)){
                string p=m.Groups[2].Value.Replace("\\\\","\\");if(m.Groups[1].Value!="path"&&!p.Contains(":"))continue;   // old numbered keys also hold non-paths
                if(!list.Contains(p,StringComparer.OrdinalIgnoreCase))list.Add(p);
            }
            return list;
        }
        // the game folder inside one library, from its appmanifest (installdir), or null
        public static string InLibrary(string library){
            try{
                string acf=Path.Combine(library,"steamapps","appmanifest_"+AppId+".acf");if(!System.IO.File.Exists(acf))return null;
                var m=Regex.Match(System.IO.File.ReadAllText(acf),"\"installdir\"\\s+\"([^\"]+)\"");string folder=Path.Combine(library,"steamapps","common",m.Success?m.Groups[1].Value:"LegendOfHeros");
                return Valid(folder)?folder:null;
            }catch{return null;}
        }
        static IEnumerable<string> SteamRoots(){
            var roots=new List<string>{@"C:\Program Files (x86)\Steam"};   // first: the usual place, properly cased (HKCU's SteamPath is lower case)
            Action<Microsoft.Win32.RegistryKey,string,string> read=(hive,key,name)=>{try{using(var k=hive.OpenSubKey(key)){var v=k==null?null:k.GetValue(name) as string;if(!String.IsNullOrEmpty(v))roots.Add(v.Replace('/','\\'));}}catch{}};
            read(Microsoft.Win32.Registry.LocalMachine,@"SOFTWARE\WOW6432Node\Valve\Steam","InstallPath");
            read(Microsoft.Win32.Registry.LocalMachine,@"SOFTWARE\Valve\Steam","InstallPath");
            read(Microsoft.Win32.Registry.CurrentUser,@"Software\Valve\Steam","SteamPath");
            return roots.Distinct(StringComparer.OrdinalIgnoreCase);
        }
        public static string FromSteam(){
            foreach(var root in SteamRoots()){
                string vdf="";try{string f=Path.Combine(root,"steamapps","libraryfolders.vdf");if(System.IO.File.Exists(f))vdf=System.IO.File.ReadAllText(f);}catch{}
                foreach(var library in new[]{root}.Concat(Libraries(vdf))){var found=InLibrary(library);if(found!=null)return found;}
            }
            return null;
        }
    }
    sealed class ProfileItem { public string Id="",Name="",Path="",Route="";public bool Enabled,External; }
    sealed class Profile { public string Name="";public DateTime Saved;public List<ProfileItem> Items=new List<ProfileItem>(); }
    static class Store {
        // tests: LOH_MM_STORE_ROOT keeps screenshots and checks away from the user's own settings
        public static string Root=!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOH_MM_STORE_ROOT"))?Environment.GetEnvironmentVariable("LOH_MM_STORE_ROOT"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LOH_ModManager");
        public static string Folder(string game) {
            string key=Path.GetFullPath(game).TrimEnd('\\').ToUpperInvariant();
            using(var sha=SHA256.Create()){string tag=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").Substring(0,16);return Path.Combine(Root,tag);}
        }
        static string Safe(string name){var b=new StringBuilder();foreach(char c in name.Trim())b.Append(Path.GetInvalidFileNameChars().Contains(c)?'_':c);return b.Length==0?"프로필":b.ToString();}
        static Dictionary<string,object> ToJson(Profile p){
            var items=new List<object>();foreach(var i in p.Items)items.Add(new Dictionary<string,object>{{"Id",i.Id},{"Name",i.Name},{"Path",i.Path},{"Route",i.Route},{"Enabled",i.Enabled},{"External",i.External}});
            return new Dictionary<string,object>{{"Format","LOH_ModManager.Profile 1"},{"Name",p.Name},{"Saved",p.Saved.ToString("o")},{"Items",items}};
        }
        static Profile FromJson(Dictionary<string,object> d){
            if(d==null||J.S(d,"Format")!="LOH_ModManager.Profile 1")return null;
            var p=new Profile{Name=J.S(d,"Name")};DateTime t;if(DateTime.TryParse(J.S(d,"Saved"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out t))p.Saved=t;
            foreach(var i in J.Items(J.Get(d,"Items")))p.Items.Add(new ProfileItem{Id=J.S(i,"Id"),Name=J.S(i,"Name"),Path=J.S(i,"Path"),Route=J.S(i,"Route"),Enabled=J.B(i,"Enabled"),External=J.B(i,"External")});
            return p.Name==""?null:p;
        }
        public static List<Profile> Profiles(string game){
            var r=new List<Profile>();string dir=Path.Combine(Folder(game),"Profiles");if(!Directory.Exists(dir))return r;
            foreach(string f in Directory.GetFiles(dir,"*.json")){var p=FromJson(J.ReadFile(f));if(p!=null)r.Add(p);}
            r.Sort((a,b)=>String.Compare(a.Name,b.Name,StringComparison.CurrentCulture));return r;
        }
        public static void SaveProfile(string game,Profile p){J.WriteFile(Path.Combine(Folder(game),"Profiles",Safe(p.Name)+".json"),ToJson(p));}
        // Deleting a profile moves it aside instead of erasing it.
        public static void RetireProfile(string game,string name){
            string f=Path.Combine(Folder(game),"Profiles",Safe(name)+".json");if(!File.Exists(f))return;
            string bin=Path.Combine(Folder(game),"지운 프로필");Directory.CreateDirectory(bin);File.Move(f,Path.Combine(bin,Safe(name)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"));
        }
        public const string CodePrefix="LOHMM1:";
        public static string Export(Profile p){
            byte[] raw=Encoding.UTF8.GetBytes(J.Serializer.Serialize(ToJson(p)));
            using(var m=new MemoryStream()){using(var z=new DeflateStream(m,CompressionMode.Compress,true))z.Write(raw,0,raw.Length);return CodePrefix+Convert.ToBase64String(m.ToArray());}
        }
        public static Profile Import(string code){
            code=(code??"").Trim();if(!code.StartsWith(CodePrefix,StringComparison.Ordinal))throw new FormatException("프로필 코드는 "+CodePrefix+" 로 시작해야 합니다.");
            byte[] packed=Convert.FromBase64String(code.Substring(CodePrefix.Length));
            using(var z=new DeflateStream(new MemoryStream(packed),CompressionMode.Decompress))using(var r=new StreamReader(z,Encoding.UTF8)){
                char[] buffer=new char[1<<20];int n=r.ReadBlock(buffer,0,buffer.Length);if(n==buffer.Length)throw new FormatException("프로필 코드가 너무 큽니다.");
                var p=FromJson(J.Serializer.Deserialize<Dictionary<string,object>>(new string(buffer,0,n)));if(p==null)throw new FormatException("프로필 코드를 읽지 못했습니다.");return p;
            }
        }
        // Order rules per game: which side wins on the same row (game test pending until Confirmed) and the winners the player chose.
        public static string RulesPath(string game){return Path.Combine(Folder(game),"Rules.json");}
        // The game's rule, checked in game on 10-31 with the order-test mods (A above B → B wins, B above A → A wins): on the same
        // row the later mod in the game's mod list wins. Kept overridable in Rules.json in case a game update changes it.
        public static bool LaterWins(string game,out bool confirmed){var d=J.ReadFile(RulesPath(game));confirmed=true;return d==null||J.Get(d,"LaterWins")==null||J.B(d,"LaterWins");}
        // Data mods the manager put on that the game has not run yet: the game writes a newly seen mod as off on its first run,
        // so the manager turns them on again once the game has closed.
        public static string FreshPath(string game){return Path.Combine(Folder(game),"FreshData.json");}
        public static List<string> Fresh(string game){return J.Strings(J.Get(J.ReadFile(FreshPath(game)),"Suffixes"));}
        // FirstRun: kept while the game had no mod list at all (never run) — the window shows them as on, with the order set then.
        public static bool FirstRun(string game){return J.B(J.ReadFile(FreshPath(game)),"FirstRun");}
        public static List<string> FreshOrder(string game){return J.Strings(J.Get(J.ReadFile(FreshPath(game)),"Order"));}
        public static void SetFresh(string game,IEnumerable<string> suffixes,bool firstRun=false,IEnumerable<string> order=null){
            var list=suffixes.Where(s=>!String.IsNullOrEmpty(s)).Distinct().ToList();var d=new Dictionary<string,object>{{"Format","LOH_ModManager.FreshData 1"},{"Suffixes",list}};
            if(firstRun&&list.Count>0){d["FirstRun"]=true;d["Order"]=(order??new string[0]).Where(s=>!String.IsNullOrEmpty(s)).Distinct().ToList();}
            try{J.WriteFile(FreshPath(game),d);}catch{}
        }
        public static string PendingPath(string game){return Path.Combine(Folder(game),"Pending.json");}
        public static void SavePending(string game,Dictionary<string,object> choices,string revision,string summary){
            J.WriteFile(PendingPath(game),new Dictionary<string,object>{{"Format","LOH_ModManager.Pending 1"},{"Game",game},{"Created",DateTime.Now.ToString("o")},{"Revision",revision},{"Summary",summary},{"Choices",choices}});
        }
        public static Dictionary<string,object> LoadPending(string game){var d=J.ReadFile(PendingPath(game));return d!=null&&J.S(d,"Format")=="LOH_ModManager.Pending 1"?d:null;}
        // A finished or cancelled reservation is kept as a record, not erased.
        public static void ClosePending(string game,string outcome){
            string f=PendingPath(game);if(!File.Exists(f))return;string dir=Path.Combine(Folder(game),"지난 예약");Directory.CreateDirectory(dir);
            File.Move(f,Path.Combine(dir,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+outcome+".json"));
        }
    }

    // ---------- one list: data mods, workshop mods and installed packages as rows beside the DLL rows ----------
    static class OneList {
        // the first part's kept tool copy (absolute), if it is inside the game folder
        static string ToolOf(string game,List<Dictionary<string,object>> parts){
            foreach(var p in parts){string t=J.S(p,"Tool");if(t=="")continue;
                try{string full=System.IO.Path.GetFullPath(System.IO.Path.Combine(game,t));if(full.StartsWith(System.IO.Path.GetFullPath(game).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))return full;}catch{}}
            return "";
        }
        static readonly Dictionary<string,string> PartKind=new Dictionary<string,string>{{"Overlay","파일 교체"},{"Patcher","패치"},{"ScriptPatch","패치"},{"FilePatch","패치"},{"ProxyDll","게임 폴더 DLL"},{"Recorded","수동 설치"}};
        static string KindOf(Dictionary<string,object> part){string k=J.S(part,"Kind"),v;return PartKind.TryGetValue(k,out v)?v:k=="HostPlugin"?"공통 로더 기능":k=="Data"?"데이터":k=="WorkshopPatch"?"데이터 패치":k.EndsWith("Dll")?"DLL":"모드";}   // never a DLL label on what is not one
        // Data rows: on/off and order come from the game's mod list (real install only; a test copy shows them off and fixed).
        // Packages: a record's file-replacement / patcher / entry-DLL parts make one locked row; its data parts are data rows.
        public static List<ModEntry> Extra(string game,Catalog catalog,ICollection<string> linkedData){
            var rows=new List<ModEntry>();var fromPackage=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var described=DescribedBy(catalog);
            foreach(var item in catalog.Installed){
                var parts=J.Items(J.Get(item,"Parts")).ToList();string name=J.S(item,"Name");
                foreach(var part in parts)if((J.S(part,"Kind")=="Data"||J.S(part,"Kind")=="HostPlugin"||J.S(part,"Kind")=="ScriptPatch")&&J.S(part,"Suffix")!="")fromPackage[J.S(part,"Suffix")]=name;
                var locked=parts.Where(x=>PartKind.ContainsKey(J.S(x,"Kind"))).ToList();
                // every part shows: parts with no row of their own (a shared loader's plugin — no DLL in the list, no data folder) get the
                // install's own row, beside its DLL or data rows in the same card (삼혼비급탭 holding the loader showed only the loader row,
                // 11-02). A workshop patch already shows inside its original's card, a link inside its mod, a data part without strIDEnd
                // as its MODs folder row; a DLL part described by another install shows in that one's card.
                if(locked.Count==0)locked=parts.Where(x=>J.S(x,"ExternalId")==""&&J.S(x,"Suffix")==""&&J.S(x,"Kind")!="WorkshopPatch"&&J.S(x,"Kind")!="Link"&&J.S(x,"Kind")!="Data").ToList();
                if(locked.Count==0)continue;
                int missing;int.TryParse(J.S(item,"Missing"),out missing);
                string info=String.Join(" · ",locked.Select(x=>(J.S(x,"Kind")=="Patcher"||J.S(x,"Kind")=="FilePatch")&&J.S(x,"Summary")!=""?J.S(x,"Summary"):J.S(x,"Title")+" (파일 "+J.S(x,"Files")+"개)"));
                rows.Add(new ModEntry{Id="pkg:"+J.S(item,"Id"),Type="package",Name=name,PackageKind=String.Join("+",locked.Select(x=>KindOf(x)).Distinct()),
                    Enabled=true,Locked=true,Present=missing==0,Missing=missing,Source="모드매니저 설치 · "+J.S(item,"Installed").Replace('T',' '),Info=info,Path=J.S(item,"Source"),Tool=ToolOf(game,locked)});
            }
            // installs switched off (set aside): rows in 끔 (a switch on restores them)
            foreach(var item in catalog.Parked){
                var parts=J.Items(J.Get(item,"Parts")).ToList();var locked=parts.Where(x=>PartKind.ContainsKey(J.S(x,"Kind"))).ToList();if(locked.Count==0)locked=parts;if(locked.Count==0)continue;   // any install set aside shows in 끔
                string info=String.Join(" · ",locked.Select(x=>(J.S(x,"Kind")=="Patcher"||J.S(x,"Kind")=="FilePatch")&&J.S(x,"Summary")!=""?J.S(x,"Summary"):J.S(x,"Title")));
                string vault=J.S(item,"Vault");string tool=vault!=""?ToolOf(vault,locked):"";
                rows.Add(new ModEntry{Id="pkg:"+J.S(item,"Id"),Type="package",Name=J.S(item,"Name"),PackageKind=String.Join("+",locked.Select(x=>KindOf(x)).Distinct()),
                    Enabled=false,Locked=true,Parked=true,Present=true,Source="모드매니저 설치 · 꺼 둠 "+J.S(item,"Removed").Replace('T',' '),Info=info,Path=J.S(item,"Source"),Tool=tool});
            }
            List<string> order=null;bool real=DataMods.IsRealInstall(game);var selection=real?DataMods.GameSelection(out order):null;
            // The game has never run (no list yet): what the player turned on then stays on until the manager can tick it.
            var waiting=real&&selection==null&&DataMods.ListMissing()&&Store.FirstRun(game)?new HashSet<string>(Store.Fresh(game)):null;
            if(waiting!=null)order=Store.FreshOrder(game);
            var data=new List<ModEntry>();
            // On/off only from the real install's list (Scan itself reads the player's file even for a test copy).
            foreach(var m in DataMods.Scan(game,linkedData)){
                bool workshop=m.Source=="창작마당";string folder=System.IO.Path.GetFileName(m.Folder);string pkg;
                data.Add(new ModEntry{Id=(workshop?"ws:":"data:")+folder,Type=workshop?"workshop":"data",Name=m.Name,Path=m.Folder,Suffix=m.Suffix,Enabled=m.Suffix!=""&&(selection!=null?selection.ContainsKey(m.Suffix)&&selection[m.Suffix]:waiting!=null&&waiting.Contains(m.Suffix)),Present=true,NotList=m.NotList,
                    Source=workshop?"창작마당 구독 "+folder:fromPackage.TryGetValue(m.Suffix,out pkg)?"모드매니저 설치 · "+pkg:"MODs 폴더에 직접 넣음"});
            }
            // A row id is the folder's name, and the scan also reads one level down (MODs\<folder>\<sub>\ModInfo.json): two folders of
            // the same name in different places (a pack's leftovers, a backup copy) gave two rows one id and the window failed to draw
            // (디시 79077, 11-01). The second gets its parent folder in its id.
            var used=new HashSet<string>(rows.Select(r=>r.Id));
            foreach(var r in data){if(used.Add(r.Id))continue;string kind=r.Id.Substring(0,r.Id.IndexOf(':')+1),id=kind+System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(r.Path))+"/"+System.IO.Path.GetFileName(r.Path),tried=id;
                for(int n=2;!used.Add(tried);n++)tried=id+"#"+n;r.Id=tried;}
            // The game's list order first; mods it has not listed yet keep folder order at the end.
            if(order!=null)data=data.Select((r,i)=>new{r,i}).OrderBy(x=>{int k=order.IndexOf(x.r.Suffix);return k<0?order.Count+x.i:k;}).Select(x=>x.r).ToList();
            rows.AddRange(data);
            return rows;
        }
        // One priority list from per-kind orders (DLL: Modules.ini, data: game list) and the window's last interleave (hints).
        // Each kind keeps its own order; the hints only decide how the kinds are woven together.
        public static List<ModEntry> Weave(List<ModEntry> dll,List<ModEntry> extra,IList<string> hints){
            var queues=new List<Queue<ModEntry>>{new Queue<ModEntry>(dll),new Queue<ModEntry>(extra.Where(r=>r.IsData)),new Queue<ModEntry>(extra.Where(r=>!r.IsData))};
            Func<ModEntry,int> hint=r=>{int k=hints==null?-1:hints.IndexOf(r.Id);return k<0?int.MaxValue:k;};
            var result=new List<ModEntry>();
            while(queues.Any(q=>q.Count>0)){var next=queues.Where(q=>q.Count>0).OrderBy(q=>hint(q.Peek())).First();result.Add(next.Dequeue());}
            return result;
        }
        // A DLL card whose data the game lists on its own (no data row: 독박육아 and the like). The game reads that data where its list has it, so the card stands
        // there: what the screen shows is what the game reads, and a save with nothing moved writes the same order back (nothing changes for the player).
        public static bool IsLinked(ModEntry r){return r.IsDll&&r.Data!=""&&r.Suffix!="";}
        public static List<string> GameOrder(string game){
            List<string> order=null;if(DataMods.IsRealInstall(game)&&DataMods.GameSelection(out order)==null&&DataMods.ListMissing()&&Store.FirstRun(game))order=Store.FreshOrder(game);
            return order;
        }
        public static void PlaceLinked(List<ModEntry> rows,IList<string> order){
            if(order==null||order.Count==0)return;
            Func<ModEntry,int> place=r=>{int k=order.IndexOf(r.Suffix);return k<0?int.MaxValue:k;};
            foreach(var r in rows.Where(x=>IsLinked(x)&&order.Contains(x.Suffix)).OrderBy(place).ToList()){
                int was=rows.IndexOf(r);rows.RemoveAt(was);
                int at=rows.FindIndex(x=>x.IsData&&place(x)>place(r));
                if(at<0){at=rows.FindLastIndex(x=>x.IsData)+1;if(at==0)at=was;}
                rows.Insert(at,r);
            }
        }
        // The DLL load order to save. A linked card stands by its data, not by its DLL: unless the player moved it, its DLL keeps its place among the DLLs.
        public static List<string> DllOrder(IList<string> saved,IList<string> now,ICollection<string> moved,ICollection<string> linked){
            var result=now.Where(id=>!linked.Contains(id)).ToList();
            foreach(string id in saved.Where(linked.Contains).Where(now.Contains).Concat(now.Where(x=>linked.Contains(x)&&!saved.Contains(x))).ToList()){
                bool free=moved.Contains(id)||!saved.Contains(id);IList<string> by=free?now:saved;int i=by.IndexOf(id);
                string next=by.Skip(i+1).FirstOrDefault(result.Contains);
                if(next==null)result.Add(id);else result.Insert(result.IndexOf(next),id);
            }
            return result;
        }
        // Which installed package a row belongs to: "pkg:<id>" (locked row), "ext:<ExternalId>" (its DLL part), "sfx:<strIDEnd>" (its data).
        public sealed class Owner { public string Id="",Name="",Kind="",Original="",RollId="",RollTo="",RollFrom=""; }
        // A DLL part's card is the install that put in the file describing it (<dll name>.loh-mod.json beside it), when another one did:
        // a shared loader belongs to whichever package installed first, but its description names the mod it stands for (빠른거래
        // describes the HeroesVow loader, 11-02 — with 삼혼비급탭 installed first the loader row took 삼혼비급탭's place).
        public static Dictionary<string,string> DescribedBy(Catalog catalog){   // ExternalId → the install that describes it (the backend's listing says)
            var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in catalog.Installed)foreach(var part in J.Items(J.Get(item,"Parts"))){string ext=J.S(part,"ExternalId"),by=J.S(part,"DescribedBy");if(ext!=""&&by!=""&&by!=J.S(item,"Id"))result[ext]=by;}
            return result;
        }
        public static Dictionary<string,Owner> Owners(Catalog catalog){
            var map=new Dictionary<string,Owner>(StringComparer.OrdinalIgnoreCase);var described=DescribedBy(catalog);var byId=new Dictionary<string,Owner>();
            foreach(var item in catalog.Installed){
                string id=J.S(item,"Id"),name=J.S(item,"Name");var back=J.Get(item,"RollBack") as Dictionary<string,object>;
                string rollId=back!=null?J.S(back,"Id"):"",rollTo=back!=null?J.S(back,"Version"):"",rollFrom=back!=null?J.S(back,"NewVersion"):"";   // replaced an older install still in the 휴지통
                map["pkg:"+id]=byId[id]=new Owner{Id=id,Name=name,Kind="package",RollId=rollId,RollTo=rollTo,RollFrom=rollFrom};
                foreach(var part in J.Items(J.Get(item,"Parts"))){
                    var o=new Owner{Id=id,Name=name,Kind=J.S(part,"Kind"),Original=J.S(part,"Original"),RollId=rollId,RollTo=rollTo,RollFrom=rollFrom};
                    if(J.S(part,"ExternalId")!="")map["ext:"+J.S(part,"ExternalId")]=o;
                    if(J.S(part,"Suffix")!="")map["sfx:"+J.S(part,"Suffix")]=o;
                    if(o.Kind=="ProxyDll")map["pkg:"+id]=new Owner{Id=id,Name=name,Kind="ProxyDll",Original=J.S(part,"Title"),RollId=rollId,RollTo=rollTo,RollFrom=rollFrom};
                }
            }
            foreach(var d in described){Owner to;if(byId.TryGetValue(d.Value,out to))map["ext:"+d.Key]=new Owner{Id=to.Id,Name=to.Name,Kind="ExternalDll",RollId=to.RollId,RollTo=to.RollTo,RollFrom=to.RollFrom};}
            return map;
        }
        public static List<string> LoadHints(string game){try{return J.Strings(J.Get(J.ReadFile(System.IO.Path.Combine(Store.Folder(game),"Order.json")),"Ids"));}catch{return new List<string>();}}
        public static void SaveHints(string game,IEnumerable<string> ids){try{J.WriteFile(System.IO.Path.Combine(Store.Folder(game),"Order.json"),new Dictionary<string,object>{{"Format","LOH_ModManager.Order 1"},{"Ids",ids.ToList()}});}catch{}}
    }

    // ---------- conflicts between data mods: found from their files, no source needed ----------
    // Tables are JSON. Head tables (Drug, GameEvent, ConversationInfo, Variant...) replace a row with the same key; line tables
    // (…EC, …CC, Conversation) append, so they do not conflict with each other — but a row swapped by a later mod drops the lines hung on it. New rows end with the mod's strIDEnd, so a mod touching rows that
    // end with another mod's strIDEnd is a patch for it. Equipment materials are listed in .dat files and loaded with
    // Dictionary.Add: the same material name in two mods is a load error. The same strIDEnd in two mods cannot work either.
    sealed class ModFacts { public string Suffix="";public long Stamp;public readonly Dictionary<string,HashSet<string>> Rows=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);public readonly Dictionary<string,string> Names=new Dictionary<string,string>(StringComparer.Ordinal);public readonly HashSet<string> Materials=new HashSet<string>(StringComparer.Ordinal);
        // the rows themselves (to tell what really differs), each table's key and, when one key holds several rows (시나리오 무장:
        // 무장×시나리오), the field that tells them apart; menus and the files the game needs beside others
        public readonly Dictionary<string,List<Dictionary<string,object>>> Data=new Dictionary<string,List<Dictionary<string,object>>>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string,string> Key=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase),Second=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public bool Menu,MenuCond;public readonly List<string> Missing=new List<string>(),MenuNames=new List<string>();
        public readonly List<string> Anims=new List<string>();   // BattleCardAnimation\<card id>.dat: the 무투 animations it brings (original or new cards)
        public readonly HashSet<string> Images=new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // pictures in its image bundle (Assets/Resource/Image/…png|jpg), from the bundle's manifest
        // line tables (…EC, …CC, Conversation): line file → parent id (its first …ID field) → how many lines it hangs there
        public readonly Dictionary<string,Dictionary<string,int>> Lines=new Dictionary<string,Dictionary<string,int>>(StringComparer.OrdinalIgnoreCase);
    }
    sealed class Conflict {
        public string A="",B="",Kind="",Detail="";public bool Hard;   // Kind: same-id, material, override, patch (A patches B)
        public int Count;public List<string> Items=new List<string>();   // every clashing row as the player reads it ("무장 손령양"), for the tooltip
        public List<string> Where=new List<string>();   // the same rows as file · id · name ("Hero.json · WJ11003 · 손령양"), for the details
        // override, row by row: what each mod changed from the game's own row. The game takes the lower mod's row whole (every field, game
        // check 11-03), so the upper one loses what it changed there that the lower row puts back to the game's value. ALost/BLost: that
        // mod loses something so when it is the upper one (a row swapped whole — 이벤트·메뉴 — always, or a row the game has none of)
        public List<RowClash> Rows=new List<RowClash>();public bool ALost,BLost;
    }
    // Kind: merge (fields over the original object: 무장·도시), scenario (무장×시나리오, Count rows), menu, whole (row swapped with what hung
    // on it), value (GameValue, one row). A/B: fields each mod changed from the original row, null when the game has no such row; Diff: A vs B.
    // lines: one mod hangs lines (…EC, …CC, Conversation) on a row the other swaps whole; ByA: A hangs them, LineFile: which file.
    sealed class RowClash { public string Item="",Kind="",LineFile="";public int Count=1;public bool ByA;public List<string> A,B,Diff=new List<string>(); }
    static class Conflicts {
        static readonly Dictionary<string,ModFacts> cache=new Dictionary<string,ModFacts>(StringComparer.OrdinalIgnoreCase);
        public static bool LineTable(string file){string n=Path.GetFileNameWithoutExtension(file);return n.EndsWith("EC",StringComparison.Ordinal)||n.EndsWith("CC",StringComparison.Ordinal)||n=="Conversation";}
        // the table a line table hangs on (the game's naming: GameEventCC → GameEvent, Conversation → ConversationInfo)
        static string HeadOf(string lineFile){string n=Path.GetFileNameWithoutExtension(lineFile);return (n=="Conversation"?"ConversationInfo":n.Substring(0,n.Length-2))+".json";}
        public static string KeyOf(Dictionary<string,object> row){
            if(row.ContainsKey("id"))return "id";
            foreach(var k in row.Keys)if(k.EndsWith("ID",StringComparison.Ordinal))return k;
            return row.ContainsKey("name")?"name":null;   // Variant: rows are named variables; userIndex is only a slot number
        }
        // a table of one row without a key (GameValue): the row itself is the item
        const string OneRow="\u0001row";
        // The game's own rows (StreamingAssets\Json) for some ids: the text is searched for "field":"id" and only those rows are read, so
        // the big tables (이벤트, 대화: 100MB+) are never parsed whole. Remembered per file stamp.
        static readonly Dictionary<string,List<Dictionary<string,object>>> originals=new Dictionary<string,List<Dictionary<string,object>>>(StringComparer.Ordinal);
        static Dictionary<string,List<Dictionary<string,object>>> Originals(string file,string field,ICollection<string> ids){
            var result=new Dictionary<string,List<Dictionary<string,object>>>(StringComparer.Ordinal);
            try{
                var info=new FileInfo(file);if(!info.Exists)return result;string tag=file+"|"+info.LastWriteTimeUtc.Ticks+"|"+field+"|";
                var missing=new List<string>();
                lock(originals){foreach(var id in ids){List<Dictionary<string,object>> rows;if(originals.TryGetValue(tag+id,out rows))result[id]=rows;else missing.Add(id);}}
                if(missing.Count==0)return result;
                string text;   // not Text.Read: the game's tables go past its 16MB cap (시나리오 무장 25MB, 이벤트 140MB)
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream,new UTF8Encoding(false),true))text=reader.ReadToEnd();
                if(field==OneRow){object[] all=J.Serializer.DeserializeObject(text) as object[];var rows=new List<Dictionary<string,object>>();
                    if(all!=null&&all.Length==1&&all[0] is Dictionary<string,object>)rows.Add((Dictionary<string,object>)all[0]);
                    foreach(var id in missing){result[id]=rows;lock(originals)originals[tag+id]=rows;}return result;}
                foreach(var id in missing){var rows=new List<Dictionary<string,object>>();
                    foreach(string needle in new[]{"\""+field+"\":\""+id+"\"","\""+field+"\": \""+id+"\""}){int at=0;
                        while((at=text.IndexOf(needle,at,StringComparison.Ordinal))>=0){
                            int start=text.LastIndexOf("\n{",at,StringComparison.Ordinal);start=start<0?text.LastIndexOf('{',at):start+1;int end=-1,depth=0;bool str=false,esc=false;
                            for(int i=start;start>=0&&i<text.Length;i++){char ch=text[i];
                                if(str){if(esc)esc=false;else if(ch=='\\')esc=true;else if(ch=='"')str=false;continue;}
                                if(ch=='"')str=true;else if(ch=='{')depth++;else if(ch=='}'&&--depth==0){end=i;break;}}
                            if(end<0)break;
                            try{var row=J.Serializer.DeserializeObject(text.Substring(start,end-start+1)) as Dictionary<string,object>;if(row!=null&&Str(row,field)==id)rows.Add(row);}catch{}
                            at=end;}
                        if(rows.Count>0)break;}
                    result[id]=rows;lock(originals)originals[tag+id]=rows;}
            }catch{}
            return result;
        }
        // fields a mod row sets to something else than the game's row (fields it leaves out are not changes)
        static List<string> Changed(Dictionary<string,object> mod,Dictionary<string,object> original){
            return mod.Keys.Where(k=>k!="userIndex"&&Str(mod,k)!=Str(original,k)).ToList();
        }
        // The game's own table names in Korean; a table not listed keeps its file name.
        static readonly Dictionary<string,string> TableNames=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"Hero","무장"},{"Equipment","장비"},{"Drug","약품"},{"Book","책"},{"GameEvent","이벤트"},{"InteractOption","상호작용"},
            {"ConversationInfo","대화"},{"Plot","줄거리"},{"BattleInfo","전투"},{"ArgumentInfo","설전"},{"MockWarInfo","모의전"},{"MockWarArmyInfo","모의전 부대"},{"Mission","임무"},{"Topic","화제"},{"HeroTitle","칭호"},
            {"OfficialTitle","관직"},{"Ci_City","도시"},{"Ci_Building","건물"},{"TradeGoods","교역품"},{"BGM","배경음악"},{"GameStoryHero","시나리오 무장"},{"Festival","명절"},{"GameValue","게임 상수"}};
        // tables whose rows the game writes field by field over the original (모드패치 가이드 2-6): a clash there changes only the fields
        // that differ; every other table swaps the whole row (and what hung on it)
        static readonly HashSet<string> Overwrite=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Hero","FunctionNPC","Ci_City"};
        static string Str(Dictionary<string,object> row,string key){object v;return row.TryGetValue(key,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
        static string RowKey(Dictionary<string,object> row,string key,string second){if(key==OneRow)return "*";string id=Str(row,key);if(id=="")return null;return second==null?id:id+"\u0001"+Str(row,second);}
        // the fields whose values differ (a missing field reads as empty); all: how many fields the two rows have between them
        static List<string> Differ(Dictionary<string,object> a,Dictionary<string,object> b,out int all){
            var names=a.Keys.Concat(b.Keys.Where(k=>!a.ContainsKey(k))).Where(k=>k!="userIndex").ToList();all=names.Count;
            return names.Where(k=>Str(a,k)!=Str(b,k)).ToList();
        }
        public static string TableName(string file){string n=Path.GetFileNameWithoutExtension(file),k;return TableNames.TryGetValue(n,out k)?k:n;}
        // A row's shown name (Korean first) so a clash reads "Hero WJ11003 손령양", not just a number; "" when the key is the name.
        static string RowName(Dictionary<string,object> row,string key){
            if(key=="name")return "";Func<string,string> s=k=>{object v;return row.TryGetValue(k,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture).Trim():"";};
            string kr=s("surnameKr")+s("nameKr");return kr!=""?kr:s("surname")+s("name");
        }
        static long StampOf(string folder){
            long s=0;try{foreach(var f in new DirectoryInfo(folder).GetFiles("*.json"))s=Math.Max(s,f.LastWriteTimeUtc.Ticks);
                string m=Path.Combine(folder,@"AssetBundlesEquipment\Material");if(Directory.Exists(m))foreach(var f in new DirectoryInfo(m).GetFiles("*.dat"))s=Math.Max(s,f.LastWriteTimeUtc.Ticks);
                string a=Path.Combine(folder,"BattleCardAnimation");if(Directory.Exists(a))foreach(var f in new DirectoryInfo(a).GetFiles("*.dat"))s=Math.Max(s,f.LastWriteTimeUtc.Ticks);
                string im=Path.Combine(folder,"AssetBundlesImage");if(Directory.Exists(im))foreach(var f in new DirectoryInfo(im).GetFiles("*.manifest"))s=Math.Max(s,f.LastWriteTimeUtc.Ticks);}catch{}
            return s;
        }
        public static ModFacts Read(string folder,string suffix){
            long stamp=StampOf(folder);ModFacts facts;
            lock(cache){if(cache.TryGetValue(folder,out facts)&&facts.Stamp==stamp&&facts.Suffix==suffix)return facts;}
            facts=new ModFacts{Suffix=suffix??"",Stamp=stamp};
            try{
                var files=new HashSet<string>(Directory.GetFiles(folder,"*.json").Select(Path.GetFileName),StringComparer.OrdinalIgnoreCase);
                foreach(string file in Directory.GetFiles(folder,"*.json")){
                    string name=Path.GetFileName(file);if(name.Equals("ModInfo.json",StringComparison.OrdinalIgnoreCase))continue;
                    object parsed;try{parsed=J.Serializer.DeserializeObject(Text.Read(file));}catch{continue;}
                    var list=parsed as object[];if(list==null||list.Length==0)continue;var first=list[0] as Dictionary<string,object>;if(first==null)continue;
                    // line tables append, so they do not clash with each other; only where each hangs its lines is kept
                    if(LineTable(name)){string link=first.Keys.FirstOrDefault(k=>k.EndsWith("ID",StringComparison.Ordinal));if(link==null)continue;var hung=new Dictionary<string,int>(StringComparer.Ordinal);
                        foreach(var item in list){var row=item as Dictionary<string,object>;string p=row==null?"":Str(row,link);if(p=="")continue;int n;hung.TryGetValue(p,out n);hung[p]=n+1;}
                        if(hung.Count>0)facts.Lines[name]=hung;continue;}
                    // NPC talk menus (type 0) are the ones the game hangs on only when a mod after has ConversationCondition.json; building
                    // buttons (type 5, 계략부) show either way (game check 11-03)
                    if(name.Equals("InteractOption.json",StringComparison.OrdinalIgnoreCase))foreach(var item in list){var row=item as Dictionary<string,object>;if(row==null)continue;string type=Str(row,"type");if(type!=""&&type!="0")continue;
                        facts.Menu=true;if(Str(row,"parentID")==""){string shown=Str(row,"nameKr");if(shown=="")shown=Str(row,"name");if(shown!=""&&!facts.MenuNames.Contains(shown))facts.MenuNames.Add(shown);}}
                    string key=KeyOf(first);if(key==null&&list.Length==1)key=OneRow;if(key==null)continue;var ids=new HashSet<string>(StringComparer.Ordinal);var rows=new List<Dictionary<string,object>>();bool dup=false;
                    if(key==OneRow){ids.Add("*");rows.Add(first);}
                    else foreach(var item in list){var row=item as Dictionary<string,object>;object v;if(row!=null&&row.TryGetValue(key,out v)&&v!=null){string s=Convert.ToString(v,CultureInfo.InvariantCulture);if(s!=""){if(!ids.Add(s))dup=true;rows.Add(row);string shown=RowName(row,key);if(shown!="")facts.Names[name+"|"+s]=shown;}}}
                    if(ids.Count>0){facts.Rows[name]=ids;facts.Data[name]=rows;facts.Key[name]=key;}
                    if(dup)foreach(var f in first.Keys){   // several rows per key: the first field that makes each row one of a kind is the second key
                        if(f==key||f=="userIndex")continue;var pairs=new HashSet<string>(StringComparer.Ordinal);bool unique=true;
                        foreach(var row in rows){object a,b;row.TryGetValue(key,out a);row.TryGetValue(f,out b);if(!pairs.Add(Convert.ToString(a,CultureInfo.InvariantCulture)+"\u0001"+Convert.ToString(b,CultureInfo.InvariantCulture))){unique=false;break;}}
                        if(unique){facts.Second[name]=f;break;}
                    }
                }
                // files the game reads only beside another (모드패치 가이드 2-6; the menu one checked in the game 11-03)
                facts.MenuCond=files.Contains("ConversationCondition.json");
                if(facts.MenuCond&&!files.Contains("InteractOption.json"))facts.Missing.Add("'ConversationCondition.json'은 'InteractOption.json'이 없어 게임이 읽지 않습니다.");
                if(files.Contains("GameStory.json")&&(!files.Contains("FriendGroup.json")||!files.Contains("GameStoryEC.json")))facts.Missing.Add("게임이 이 모드를 읽다 오류가 날 수 있습니다(GameStory.json 은 FriendGroup.json·GameStoryEC.json 과 함께 있어야 함).");
                foreach(var f in files.OrderBy(x=>x,StringComparer.Ordinal)){string n=Path.GetFileNameWithoutExtension(f);if(!n.EndsWith("EC",StringComparison.Ordinal)&&!n.EndsWith("CC",StringComparison.Ordinal))continue;
                    string head=n.Substring(0,n.Length-2);if(head==""||head=="GameEvent"||head=="Mission"||head=="Plot"||files.Contains(head+".json"))continue;
                    facts.Missing.Add("'"+f+"'은 본 표 '"+head+".json'이 없어 게임이 읽지 않습니다. 이 파일의 내용은 게임에 적용되지 않습니다.");}
                string anim=Path.Combine(folder,"BattleCardAnimation");
                if(Directory.Exists(anim))facts.Anims.AddRange(Directory.GetFiles(anim,"*.dat").Select(Path.GetFileNameWithoutExtension));
                // the one image bundle the game reads for it (image_<strIDEnd>): its manifest lists every asset ("Assets:" lines, at times
                // quoted with \uXXXX). Pictures only; no manifest → nothing known, so nothing is said about it
                string manifest=Path.Combine(folder,@"AssetBundlesImage\image_"+(suffix??"")+".manifest");
                if(!String.IsNullOrEmpty(suffix)&&File.Exists(manifest)){bool assets=false;
                    foreach(string line in File.ReadAllLines(manifest,Encoding.UTF8)){
                        if(line.StartsWith("Assets:",StringComparison.Ordinal)){assets=true;continue;}if(!assets)continue;
                        if(!line.StartsWith("- ",StringComparison.Ordinal)){if(line.Length>0&&!Char.IsWhiteSpace(line[0]))break;continue;}
                        string p=line.Substring(2).Trim();if(p.Length>1&&p[0]=='"'&&p[p.Length-1]=='"')try{p=System.Text.RegularExpressions.Regex.Unescape(p.Substring(1,p.Length-2));}catch{continue;}
                        p=p.Replace('\\','/');string low=p.ToLowerInvariant();if(low.EndsWith(".png")||low.EndsWith(".jpg")||low.EndsWith(".jpeg"))facts.Images.Add(p);}}
                string mat=Path.Combine(folder,@"AssetBundlesEquipment\Material");
                if(Directory.Exists(mat))foreach(string dat in Directory.GetFiles(mat,"*.dat")){
                    try{using(var r=new BinaryReader(File.OpenRead(dat),new UTF8Encoding(false,true))){int n=r.ReadInt32();if(n<0||n>100000)continue;for(int i=0;i<n;i++)facts.Materials.Add(r.ReadString());}}catch{}
                }
            }catch{}
            lock(cache)cache[folder]=facts;
            return facts;
        }
        // mods: (row id, data folder, strIDEnd). Pairs in list order; a patch is reported as "A patches B". originals: the game's Json folder
        // (StreamingAssets\Json) to tell what each mod changed from the game's own rows; without it every clashing row counts as changed by both.
        public static List<Conflict> Find(IList<Tuple<string,string,string>> mods,string originals=null){
            var facts=mods.Select(m=>Read(m.Item2,m.Item3)).ToList();var result=new List<Conflict>();
            for(int i=0;i<mods.Count;i++)for(int j=i+1;j<mods.Count;j++){
                var a=facts[i];var b=facts[j];string ia=mods[i].Item1,ib=mods[j].Item1;
                if(a.Suffix!=""&&a.Suffix==b.Suffix){result.Add(new Conflict{A=ia,B=ib,Kind="same-id",Hard=true,Detail="같은 모드 ID("+a.Suffix+")"});continue;}
                var shared=a.Materials.Where(b.Materials.Contains).ToList();
                if(shared.Count>0)result.Add(new Conflict{A=ia,B=ib,Kind="material",Hard=true,Count=shared.Count,Detail="같은 재질 이름 "+shared.Count+"개("+String.Join(", ",shared.Take(3))+") — 함께 켜면 불러오기 오류"});
                var over=new List<string>();var aPatchesB=new List<string>();var bPatchesA=new List<string>();var overItems=new List<string>();var overWhere=new List<string>();
                foreach(var t in a.Rows){HashSet<string> other;if(!b.Rows.TryGetValue(t.Key,out other))continue;
                    foreach(var id in t.Value){if(!other.Contains(id))continue;string label=Path.GetFileNameWithoutExtension(t.Key)+" "+id,shown;bool named=a.Names.TryGetValue(t.Key+"|"+id,out shown)||b.Names.TryGetValue(t.Key+"|"+id,out shown);if(named)label+=" "+shown;
                        if(b.Suffix!=""&&id.EndsWith(b.Suffix,StringComparison.Ordinal))aPatchesB.Add(label);else if(a.Suffix!=""&&id.EndsWith(a.Suffix,StringComparison.Ordinal))bPatchesA.Add(label);}}
                // overrides: the same row (key, and the second key where one key holds several rows) in both, with different content —
                // the same content in both is no clash. Each side is compared with the game's row to tell who really changed what.
                var clashes=new List<RowClash>();bool aLost=false,bLost=false;
                foreach(var t in a.Data){List<Dictionary<string,object>> rowsB;if(!b.Data.TryGetValue(t.Key,out rowsB))continue;
                    string key=a.Key[t.Key],sec;if(!a.Second.TryGetValue(t.Key,out sec))b.Second.TryGetValue(t.Key,out sec);
                    var mapB=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);foreach(var r in rowsB){string k=RowKey(r,key,sec);if(k!=null)mapB[k]=r;}
                    var pairs=new List<Tuple<string,string,Dictionary<string,object>,Dictionary<string,object>,List<string>>>();   // id, row key, a, b, fields that differ
                    foreach(var ra in t.Value){string k=RowKey(ra,key,sec);Dictionary<string,object> rb;if(k==null||!mapB.TryGetValue(k,out rb))continue;
                        string id=key==OneRow?"":Str(ra,key);if((b.Suffix!=""&&id.EndsWith(b.Suffix,StringComparison.Ordinal))||(a.Suffix!=""&&id.EndsWith(a.Suffix,StringComparison.Ordinal)))continue;   // a patch: above
                        int all;var diff=Differ(ra,rb,out all);if(diff.Count>0)pairs.Add(Tuple.Create(id,k,ra,rb,diff));}
                    if(pairs.Count==0)continue;
                    var game=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);   // row key → the game's row
                    if(!String.IsNullOrEmpty(originals))foreach(var found in Originals(Path.Combine(originals,t.Key),key,pairs.Select(p=>p.Item1).Distinct().ToList()))
                        foreach(var r in found.Value){string k=RowKey(r,key,sec);if(k!=null&&!game.ContainsKey(k))game[k]=r;}
                    string head=Path.GetFileNameWithoutExtension(t.Key);
                    foreach(var g in pairs.GroupBy(p=>p.Item1)){string id=g.Key,shown;
                        bool named=a.Names.TryGetValue(t.Key+"|"+id,out shown)||b.Names.TryGetValue(t.Key+"|"+id,out shown)
                            ||a.Names.TryGetValue("Hero.json|"+id,out shown)||b.Names.TryGetValue("Hero.json|"+id,out shown);   // 시나리오 무장 rows carry no name: the hero's
                        var x=new RowClash{Count=g.Count()};List<string> ca=new List<string>(),cb=new List<string>();bool known=true;
                        foreach(var p in g){foreach(var f in p.Item5)if(!x.Diff.Contains(f))x.Diff.Add(f);Dictionary<string,object> o;
                            if(!game.TryGetValue(p.Item2,out o)){known=false;continue;}
                            foreach(var f in Changed(p.Item3,o))if(!ca.Contains(f))ca.Add(f);foreach(var f in Changed(p.Item4,o))if(!cb.Contains(f))cb.Add(f);}
                        if(known){x.A=ca;x.B=cb;}
                        x.Kind=head=="InteractOption"?"menu":key==OneRow?"value":sec!=null&&x.Count>1?"scenario":Overwrite.Contains(head)||sec!=null?"merge":"whole";
                        string item=key==OneRow?TableName(t.Key):TableName(t.Key)+" "+(named?shown:id);
                        x.Item=x.Kind=="menu"?"상호작용 메뉴 '"+(named?shown:id)+"'":x.Kind=="scenario"?item+"("+(sec=="storyID"?"시나리오":"줄")+" "+x.Count+"개)":item;
                        clashes.Add(x);bool swapped=x.Kind=="whole"||x.Kind=="menu";   // the upper row goes with what hung on it, whatever the lower one carries
                        aLost|=x.A==null?swapped:swapped?x.A.Count>0:x.A.Except(x.B).Any();bLost|=x.B==null?swapped:swapped?x.B.Count>0:x.B.Except(x.A).Any();
                        over.Add(key==OneRow?head:head+" "+id+(named?" "+shown:""));overItems.Add(item);overWhere.Add(key==OneRow?t.Key:t.Key+" · "+id+(named?" · "+shown:""));}
                }
                // lines one mod hangs on a row the other swaps whole (모드패치 가이드 2-6: a swapped row starts with no lines): they are gone
                // when the one that hangs them is the upper one. A row both carry is a whole-row clash above; a row ending with a strIDEnd, a patch.
                foreach(var side in new[]{true,false}){var hang=side?a:b;var swap=side?b:a;
                    foreach(var lf in hang.Lines){string ht=HeadOf(lf.Key),hn=Path.GetFileNameWithoutExtension(ht);HashSet<string> swapped,own;
                        if(Overwrite.Contains(hn)||hn=="InteractOption"||swap.Second.ContainsKey(ht)||!swap.Rows.TryGetValue(ht,out swapped))continue;hang.Rows.TryGetValue(ht,out own);
                        foreach(var p in lf.Value){string id=p.Key,shown;if(!swapped.Contains(id)||(own!=null&&own.Contains(id)))continue;
                            if((a.Suffix!=""&&id.EndsWith(a.Suffix,StringComparison.Ordinal))||(b.Suffix!=""&&id.EndsWith(b.Suffix,StringComparison.Ordinal)))continue;
                            bool named=swap.Names.TryGetValue(ht+"|"+id,out shown);string item=TableName(ht)+" "+(named?shown:id);
                            clashes.Add(new RowClash{Item=item,Kind="lines",Count=p.Value,ByA=side,LineFile=lf.Key});if(side)aLost=true;else bLost=true;
                            over.Add(hn+" "+id+(named?" "+shown:""));overItems.Add(item);overWhere.Add(ht+" · "+id+(named?" · "+shown:"")+" ("+lf.Key+" 덧붙임 "+p.Value+"줄)");}}}
                if(aPatchesB.Count>0)result.Add(new Conflict{A=ia,B=ib,Kind="patch",Count=aPatchesB.Count,Detail="상대 모드의 항목 "+aPatchesB.Count+"개를 고침("+String.Join(", ",aPatchesB.Take(3))+")"});
                if(bPatchesA.Count>0)result.Add(new Conflict{A=ib,B=ia,Kind="patch",Count=bPatchesA.Count,Detail="상대 모드의 항목 "+bPatchesA.Count+"개를 고침("+String.Join(", ",bPatchesA.Take(3))+")"});
                if(over.Count>0)result.Add(new Conflict{A=ia,B=ib,Kind="override",Count=over.Count,Items=overItems,Where=overWhere,Rows=clashes,ALost=aLost,BLost=bLost,Detail="같은 항목 "+over.Count+"개("+String.Join(", ",over.Take(3))+(over.Count>3?" …":"")+")"});
            }
            return result;
        }
        // One order for the data rows: patches after (or before) what they patch, remembered winners over losers; otherwise the
        // current order. laterWins: the later mod in the game list wins on the same row. Returns ids in the new order.
        public static List<string> Sort(IList<string> current,IEnumerable<Conflict> found,bool laterWins){
            var edges=new List<KeyValuePair<string,string>>();   // first must come before second
            foreach(var c in found)if(c.Kind=="patch")edges.Add(laterWins?new KeyValuePair<string,string>(c.B,c.A):new KeyValuePair<string,string>(c.A,c.B));
            var set=new HashSet<string>(current);edges=edges.Where(e=>set.Contains(e.Key)&&set.Contains(e.Value)&&e.Key!=e.Value).ToList();
            var left=new List<string>(current);var result=new List<string>();
            while(left.Count>0){
                string pick=left.FirstOrDefault(x=>!edges.Any(e=>e.Value==x&&left.Contains(e.Key)));
                if(pick==null)pick=left[0];   // a cycle: keep the current order for what remains
                result.Add(pick);left.Remove(pick);
            }
            return result;
        }
    }

    // ---------- data-only mods (MODs / Steam workshop), read-only ----------
    sealed class DataMod { public string Name="",Folder="",Suffix="",Source="";public bool? Selected;public bool NotList; }
    static class DataMods {
        // LOH_MM_SELECTION_PATH: tests only (a fake game folder with its own list, or none, to see a fresh install).
        public static string SelectionPath=!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOH_MM_SELECTION_PATH"))?Environment.GetEnvironmentVariable("LOH_MM_SELECTION_PATH"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","FreeWing","ThreeKingdom","SaveData","ModSettings");
        // The game's mod list belongs to the real install; test copies (no Unity player beside the exe) must not borrow it,
        // even after a mod pack put a ThreeKingdom_Data folder there. Same rule as Test-MMRealInstall.
        public static bool IsRealInstall(string game){try{return Directory.Exists(Path.Combine(game,"ThreeKingdom_Data"))&&File.Exists(Path.Combine(game,"UnityPlayer.dll"));}catch{return false;}}
        public static bool? Listed(Dictionary<string,bool> selection,string suffix){bool on;if(selection==null||String.IsNullOrEmpty(suffix))return null;return selection.TryGetValue(suffix,out on)&&on;}
        public static Dictionary<string,bool> GameSelection(){List<string> order;return GameSelection(out order);}
        // No list file at all: the game has never run on this PC (it writes the file on its first run).
        public static bool ListMissing(){try{return !File.Exists(SelectionPath);}catch{return false;}}
        // Same file, with the entries in the game's order (null when the file is missing or not understood).
        public static Dictionary<string,bool> GameSelection(out List<string> order){
            order=null;string path=SelectionPath;
            try{
                var file=new FileInfo(path);if(!file.Exists||file.Length>1048576)return null;
                using(var r=new BinaryReader(new MemoryStream(File.ReadAllBytes(path)),new UTF8Encoding(false,true))){
                    int version=r.ReadInt32();byte auto=r.ReadByte();int count=r.ReadInt32();
                    if(version<202000000||version>210000000||auto>1||count<0||count>10000)return null;
                    var values=new Dictionary<string,bool>();var list=new List<string>();
                    for(int i=0;i<count;i++){string suffix=r.ReadString();byte on=r.ReadByte();if(suffix==""||suffix.Length>128||on>1||values.ContainsKey(suffix))return null;values[suffix]=on==1;list.Add(suffix);}
                    if(r.BaseStream.Position!=r.BaseStream.Length)return null;order=list;return values;
                }
            }catch{return null;}
        }
        public static List<DataMod> Scan(string game,ICollection<string> linkedData){
            var result=new List<DataMod>();var selection=GameSelection();
            var roots=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>(Path.Combine(game,"MODs"),"로컬")};
            try{string workshop=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(game).TrimEnd('\\'))),@"workshop\content\3020510");roots.Add(new KeyValuePair<string,string>(workshop,"창작마당"));}catch{}
            foreach(var root in roots){
                if(!Directory.Exists(root.Key))continue;
                foreach(string folder in Directory.GetDirectories(root.Key)){
                    var infos=new List<string>();string direct=Path.Combine(folder,"ModInfo.json");
                    if(File.Exists(direct))infos.Add(direct);else try{foreach(string sub in Directory.GetDirectories(folder)){string p=Path.Combine(sub,"ModInfo.json");if(File.Exists(p))infos.Add(p);}}catch{}
                    foreach(string info in infos){
                        string dir=Path.GetDirectoryName(info);if(linkedData!=null&&linkedData.Contains(Path.GetFileName(dir)))continue;
                        string text=Text.Read(info);if(text==null)continue;Dictionary<string,object> d=null;
                        try{object parsed=J.Serializer.DeserializeObject(text.Trim());d=parsed as Dictionary<string,object>;if(d==null){var arr=parsed as object[];if(arr!=null&&arr.Length>0)d=arr[0] as Dictionary<string,object>;}}catch{}
                        if(d==null)continue;string name=J.S(d,"nameKr");if(name=="")name=J.S(d,"name");if(name=="")name=Path.GetFileName(dir);
                        // The game itself only takes a list (JArray.Parse); one object is read here but skipped there.
                        var mod=new DataMod{Name=name,Folder=dir,Suffix=J.S(d,"strIDEnd"),Source=root.Value,NotList=text.TrimStart('﻿',' ','\t','\r','\n').StartsWith("{")};bool on;
                        if(selection!=null&&mod.Suffix!=""&&selection.TryGetValue(mod.Suffix,out on))mod.Selected=on;
                        result.Add(mod);
                    }
                }
            }
            return result;
        }
    }

    // ---------- live status, read in the background (no PowerShell) ----------
    sealed class LiveSnapshot {
        public GameProcess Game;public RuntimeState Runtime=RuntimeState.Empty;public HashSet<string> Loaded;
        public readonly Dictionary<string,DllVersion> Versions=new Dictionary<string,DllVersion>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string,string> RunningVersions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string,bool> Selection;   // the game's own mod list (ModSettings), null when unreadable
        public string Fingerprint="";
    }
    sealed class LiveMonitor : IDisposable {
        readonly string game,managerDir;readonly Action<LiveSnapshot> publish;readonly Action configChanged;
        readonly System.Threading.AutoResetEvent wake=new System.Threading.AutoResetEvent(true);volatile bool stopping,rescan;
        volatile List<ModEntry> rows=new List<ModEntry>();FileSystemWatcher watcher;string last=null;   // null: the first snapshot always goes out (a fresh install's one is empty)
        int modulesPid,moduleScans;DateTime modulesAt;HashSet<string> modules;
        readonly Dictionary<string,string> running=new Dictionary<string,string>();readonly Dictionary<string,int> tries=new Dictionary<string,int>();DateTime runningAt;
        DateTime selectionWrite=DateTime.MinValue;Dictionary<string,bool> selection;bool realInstall;
        public LiveMonitor(string game,Action<LiveSnapshot> publish,Action configChanged){
            this.game=game;managerDir=CatalogReader.ManagerDir(game);realInstall=DataMods.IsRealInstall(game);this.publish=publish;this.configChanged=configChanged;EnsureWatch();
            new System.Threading.Thread(Loop){IsBackground=true,Name="LiveMonitor",Priority=System.Threading.ThreadPriority.BelowNormal}.Start();
        }
        public void SetRows(IEnumerable<ModEntry> list){rows=new List<ModEntry>(list);wake.Set();}
        public void Poke(bool modulesToo){if(modulesToo)rescan=true;last=null;wake.Set();}
        public void EnsureWatch(){
            if(watcher!=null||!Directory.Exists(managerDir))return;
            try{
                var w=new FileSystemWatcher(managerDir){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size,IncludeSubdirectories=false};
                FileSystemEventHandler h=(s,e)=>Changed(e.Name);w.Changed+=h;w.Created+=h;w.Deleted+=h;w.Renamed+=(s,e)=>Changed(e.Name);
                w.EnableRaisingEvents=true;watcher=w;
            }catch{}
        }
        void Changed(string name){
            if(name==null)return;
            if(name.StartsWith("Runtime-",StringComparison.OrdinalIgnoreCase)){wake.Set();return;}
            foreach(string f in new[]{"Catalog.json","Owner.json","Modules.ini"})if(name.Equals(f,StringComparison.OrdinalIgnoreCase)){configChanged();return;}
        }
        void Loop(){while(!stopping){try{Tick();}catch{}wake.WaitOne(1000);}}
        void Tick(){
            var snap=new LiveSnapshot();var games=GameProbe.Find(game);snap.Game=games.Count>0?games[0]:null;var now=DateTime.UtcNow;
            if(snap.Game==null){modulesPid=0;modules=null;running.Clear();tries.Clear();}
            else{
                snap.Runtime=RuntimeReader.Read(managerDir,snap.Game);
                if(modulesPid!=snap.Game.Pid){modulesPid=snap.Game.Pid;moduleScans=0;modules=null;running.Clear();tries.Clear();}
                // The module list is only needed to catch DLLs loaded by something else; take it a few times per game run.
                double age=snap.Game.StartUtc==DateTime.MinValue?60:(now-snap.Game.StartUtc).TotalSeconds;
                if(rescan||(age>=4&&(moduleScans==0||(moduleScans<3&&(now-modulesAt).TotalSeconds>=20)))){rescan=false;var m=GameProbe.LoadedModules(snap.Game.Pid);if(m!=null)modules=m;moduleScans++;modulesAt=now;}
                snap.Loaded=modules;
            }
            var list=rows;var fp=new StringBuilder();
            if(snap.Game!=null)fp.Append(snap.Game.Pid).Append('/').Append(snap.Game.Creation).Append('|').Append(snap.Runtime.Found).Append(snap.Runtime.Revision).Append(snap.Runtime.ConfigError);
            foreach(var mod in snap.Runtime.Modules.Values)fp.Append('|').Append(mod.Id).Append(':').Append(mod.State).Append(',').Append(mod.Error).Append(',').Append(mod.Detail).Append(',').Append(mod.Entry);
            bool retry=(now-runningAt).TotalSeconds>=5;if(retry)runningAt=now;
            foreach(var r in list){
                if(r.Path==""||snap.Versions.ContainsKey(r.Path))continue;
                var v=ModVersion.Detect(r.Path);snap.Versions[r.Path]=v;fp.Append('|').Append(r.Path).Append('=').Append(v.Text);
                if(snap.Loaded!=null)fp.Append(snap.Loaded.Contains(r.Path)?'+':'-');
                if(snap.Game!=null&&r.Log!=""){
                    string found;int n;tries.TryGetValue(r.Id,out n);
                    if(!running.TryGetValue(r.Id,out found)&&retry&&n<8){
                        tries[r.Id]=n+1;string dir=System.IO.Path.GetDirectoryName(r.Path);
                        found=dir==null?"":ModVersion.FromLog(System.IO.Path.Combine(dir,r.Log),ModVersion.Stem(r.Path),snap.Game.Pid,snap.Game.StartUtc);
                        if(found!="")running[r.Id]=found;
                    }
                    if(running.TryGetValue(r.Id,out found)){snap.RunningVersions[r.Id]=found;fp.Append('@').Append(found);}
                }
            }
            // The game rewrites ModSettings only when its mod list changes; re-read it only then.
            if(realInstall)try{var w=File.GetLastWriteTimeUtc(DataMods.SelectionPath);if(w!=selectionWrite){selectionWrite=w;selection=DataMods.GameSelection();}}catch{}
            snap.Selection=selection;if(selection!=null)foreach(var kv in selection)fp.Append('|').Append(kv.Key).Append(kv.Value?'1':'0');
            snap.Fingerprint=fp.ToString();
            if(snap.Fingerprint!=last){last=snap.Fingerprint;publish(snap);}
        }
        public void Dispose(){stopping=true;wake.Set();if(watcher!=null){watcher.EnableRaisingEvents=false;watcher.Dispose();watcher=null;}}
    }

    // 모드관리.exe --probe <게임 폴더> [pid] : 화면 없이 지금 판정을 JSON 으로 낸다(검사·문제 확인용).
    static class Probe {
        public static Dictionary<string,object> Run(string game,int pid) {
            var catalog=CatalogReader.Quick(game);var games=GameProbe.Find(game);GameProcess g=null;var selection=DataMods.IsRealInstall(game)?DataMods.GameSelection():null;
            foreach(var x in games)if(pid==0||x.Pid==pid){g=x;break;}
            var rt=RuntimeReader.Read(CatalogReader.ManagerDir(game),g);var loaded=g==null?null:GameProbe.LoadedModules(g.Pid);var rows=new List<object>();
            foreach(var r in catalog.Rows){
                var v=ModVersion.Detect(r.Path);string dir=r.Path==""?null:Path.GetDirectoryName(r.Path);
                string running=g!=null&&r.Log!=""&&dir!=null?ModVersion.FromLog(Path.Combine(dir,r.Log),ModVersion.Stem(r.Path),g.Pid,g.StartUtc):"";
                var s=Status.Compute(r,r.Enabled,false,false,g,rt,loaded,running,v.Text,DataMods.Listed(selection,r.Suffix));RuntimeModule m;rt.Modules.TryGetValue(r.Id,out m);
                rows.Add(new Dictionary<string,object>{{"Id",r.Id},{"Name",r.Name},{"Badge",s.Badge.ToString()},{"BadgeText",s.BadgeText},{"Summary",s.Summary},
                    {"Version",v.Text},{"VersionSource",v.Source},{"RunningVersion",running},{"CanPause",v.CanPause},
                    {"State",m==null?-1:m.State},{"Detail",m==null?-1:m.Detail},{"Entry",m==null?"":m.Entry}});
            }
            return new Dictionary<string,object>{{"Pid",g==null?0:g.Pid},{"RuntimeFound",rt.Found},{"RuntimeRevision",rt.Revision},{"Revision",catalog.Revision},{"Entries",rt.Entries},{"Rows",rows}};
        }
    }

    // Updates a bound list in place so a routine refresh keeps item objects, selection and scroll position.
    static class ListSync {
        public static void Sync<T>(ObservableCollection<T> target,IList<T> desired,Func<T,string> key,Action<T,T> update) where T:class {
            var wanted=new HashSet<string>(desired.Select(key));
            for(int i=target.Count-1;i>=0;i--)if(!wanted.Contains(key(target[i])))target.RemoveAt(i);
            for(int i=0;i<desired.Count;i++){
                string k=key(desired[i]);int at=-1;for(int j=i;j<target.Count;j++)if(key(target[j])==k){at=j;break;}
                if(at<0)target.Insert(i,desired[i]);else{if(at!=i)target.Move(at,i);update(target[i],desired[i]);}
            }
        }
    }
}
