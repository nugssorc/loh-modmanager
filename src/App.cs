// 관리창 화면(WPF). 화면 모양은 MainWindow.xaml, 상태 판정·백엔드는 Core.cs.
// 화면 스레드는 기다리지 않는다: 실행 상태는 LiveMonitor(백그라운드 1초)가, 변경은 PowerShell 백엔드가 별도 스레드에서 한다.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace LohModManager {
    abstract class Bindable : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Set<T>(ref T field,T value,string name){if(Object.Equals(field,value))return;field=value;var h=PropertyChanged;if(h!=null)h(this,new PropertyChangedEventArgs(name));}
    }
    sealed class ModRowView : Bindable {
        public string Id="";public ModEntry Entry;public RowState State;
        string name,badgeText,badgeTip,kindTip,changeTip,versionText,kindText,summary,changeText,orderText,orderPad,routeText,conflictText,conflictTip,linkTip="";bool conflictMild,hasLink;Brush badgeBrush,badgeFore,kindFore,dropBrush=Brushes.Transparent;Visibility changeVisibility=Visibility.Collapsed,conflictVisibility=Visibility.Collapsed;
        public string ConflictText{get{return conflictText;}set{Set(ref conflictText,value,"ConflictText");ConflictVisibility=String.IsNullOrEmpty(value)?Visibility.Collapsed:Visibility.Visible;}}
        public string ConflictTip{get{return conflictTip;}set{Set(ref conflictTip,value,"ConflictTip");}}
        public bool ConflictMild{get{return conflictMild;}set{Set(ref conflictMild,value,"ConflictMild");}}   // only the same rows: both still work, the lower one wins there
        // the card's icons at the right end of its first line: 게시글 (dimmer when there is no link yet) and 패치노트
        public string LinkTip{get{return linkTip;}set{Set(ref linkTip,value,"LinkTip");}}
        public bool HasLink{get{return hasLink;}set{Set(ref hasLink,value,"HasLink");}}
        public Visibility ConflictVisibility{get{return conflictVisibility;}set{Set(ref conflictVisibility,value,"ConflictVisibility");}}
        public string Name{get{return name;}set{Set(ref name,value,"Name");}}
        public string BadgeText{get{return badgeText;}set{Set(ref badgeText,value,"BadgeText");}}
        public string BadgeTip{get{return badgeTip;}set{Set(ref badgeTip,value,"BadgeTip");}}
        public string KindTip{get{return kindTip;}set{Set(ref kindTip,value,"KindTip");}}
        public string ChangeTip{get{return changeTip;}set{Set(ref changeTip,value,"ChangeTip");}}
        public Brush BadgeBrush{get{return badgeBrush;}set{Set(ref badgeBrush,value,"BadgeBrush");}}
        public Brush BadgeFore{get{return badgeFore;}set{Set(ref badgeFore,value,"BadgeFore");}}
        public string VersionText{get{return versionText;}set{Set(ref versionText,value,"VersionText");}}
        public string KindText{get{return kindText;}set{Set(ref kindText,value,"KindText");}}
        public Brush KindFore{get{return kindFore;}set{Set(ref kindFore,value,"KindFore");}}
        public string Summary{get{return summary;}set{Set(ref summary,value,"Summary");}}
        public string ChangeText{get{return changeText;}set{Set(ref changeText,value,"ChangeText");ChangeVisibility=String.IsNullOrEmpty(value)?Visibility.Collapsed:Visibility.Visible;}}
        public Visibility ChangeVisibility{get{return changeVisibility;}set{Set(ref changeVisibility,value,"ChangeVisibility");}}
        public string OrderText{get{return orderText;}set{Set(ref orderText,value,"OrderText");}}
        // "0", "00"…: as many digits as the longest number in the list, so every card's number column is that wide (numbers right-aligned)
        public string OrderPad{get{return orderPad;}set{Set(ref orderPad,value,"OrderPad");}}
        public string RouteText{get{return routeText;}set{Set(ref routeText,value,"RouteText");}}
        public Brush DropBrush{get{return dropBrush;}set{Set(ref dropBrush,value,"DropBrush");}}
        // while another card is held: a ring on the cards it clashes with where it is now (what happens is said on the held card)
        Brush ringBrush=Brushes.Transparent;public Brush RingBrush{get{return ringBrush;}set{Set(ref ringBrush,value,"RingBrush");}}
        // while a card is held: the colour of the drop spot above this card
        Brush zoneBrush=Brushes.Transparent;public Brush ZoneBrush{get{return zoneBrush;}set{Set(ref zoneBrush,value,"ZoneBrush");}}
        Thickness cardMargin=new Thickness(0,0,0,6);public Thickness CardMargin{get{return cardMargin;}set{Set(ref cardMargin,value,"CardMargin");}}
        // while a card is held: the gap above this card, a drop spot, in that spot's colour, with a line on top where the colour changes
        Brush gapBrush=Brushes.Transparent,edgeBrush=Brushes.Transparent;public Brush GapBrush{get{return gapBrush;}set{Set(ref gapBrush,value,"GapBrush");}}public Brush EdgeBrush{get{return edgeBrush;}set{Set(ref edgeBrush,value,"EdgeBrush");}}
        // 하위 패치(창작마당 모드에 덮은 번역 등): the card shows them inside, folded with the small "접기 N" / "펼치기 N" pill
        List<SubRowView> subs;bool subsOpen=false;Visibility subsVisibility=Visibility.Collapsed,subCountVisibility=Visibility.Collapsed;string subCount="";
        public static Action<string,bool> OpenChanged;
        public List<SubRowView> Subs{get{return subs;}set{Set(ref subs,value,"Subs");Fit();}}
        public bool SubsOpen{get{return subsOpen;}set{if(subsOpen==value)return;Set(ref subsOpen,value,"SubsOpen");Fit();if(OpenChanged!=null)OpenChanged(Id,value);}}
        public string SubCount{get{return subCount;}set{Set(ref subCount,value,"SubCount");}}
        public Visibility SubsVisibility{get{return subsVisibility;}set{Set(ref subsVisibility,value,"SubsVisibility");}}
        public Visibility SubCountVisibility{get{return subCountVisibility;}set{Set(ref subCountVisibility,value,"SubCountVisibility");}}
        void Fit(){bool any=subs!=null&&subs.Count>0;SubCountVisibility=any?Visibility.Visible:Visibility.Collapsed;SubsVisibility=any&&subsOpen?Visibility.Visible:Visibility.Collapsed;
            SubCount=any?(subsOpen?"접기 ":"펼치기 ")+subs.Count:"";}   // says what a click does, and how many lines it holds
        public void CopyFrom(ModRowView o){Entry=o.Entry;State=o.State;Name=o.Name;Subs=o.Subs;subsOpen=o.subsOpen;Fit();BadgeText=o.BadgeText;BadgeTip=o.BadgeTip;KindTip=o.KindTip;ChangeTip=o.ChangeTip;BadgeBrush=o.BadgeBrush;BadgeFore=o.BadgeFore;VersionText=o.VersionText;KindText=o.KindText;KindFore=o.KindFore;Summary=o.Summary;ChangeText=o.ChangeText;OrderText=o.OrderText;OrderPad=o.OrderPad;RouteText=o.RouteText;ConflictText=o.ConflictText;ConflictTip=o.ConflictTip;ConflictMild=o.ConflictMild;LinkTip=o.LinkTip;HasLink=o.HasLink;}
    }
    sealed class SubRowView { public string Id{get;set;} public string Title{get;set;} public string Info{get;set;} public string Kind{get;set;} public Brush KindFore{get;set;}
        public Visibility KindVisibility{get{return String.IsNullOrEmpty(Kind)?Visibility.Collapsed:Visibility.Visible;}} public string BadgeText{get;set;} public string BadgeTip{get;set;} public Brush BadgeBrush{get;set;} public Brush BadgeFore{get;set;}
        // a bundled patcher's item (상한해제 1~4, 올인원 한글보강…): a checkbox — ticking installs it, unticking takes it out
        public string Group="",Record="",Key="";public int Number;
        // a workshop patch line (번역 등): its own 게시글 and 패치노트 icons; Owner is the card it sits in
        public string Owner="";public bool PatchIcons;public string LinkTip{get;set;} public bool HasLink{get;set;}
        public Visibility IconsVisibility{get{return PatchIcons?Visibility.Visible:Visibility.Collapsed;}}public bool Checkable{get;set;} public bool CheckEnabled{get;set;}
        public Visibility CheckVisibility{get{return Checkable?Visibility.Visible:Visibility.Collapsed;}}
        bool on;public void Start(bool value){on=value;}public bool Actual;   // Actual: as it is in the game files (On may be a tick not saved yet)
        public bool On{get{return on;}set{if(on==value)return;on=value;if(Toggled!=null)Toggled(this,value);}}   // only the player's click comes here
        public static Action<SubRowView,bool> Toggled;
    }
    sealed class DataModView { public string Name{get;set;} public string SourceText{get;set;} public string SelectionText{get;set;} public Brush SelectionBrush{get;set;} }
    sealed class AlertView { public string Text{get;set;} public Brush Brush{get;set;} }
    sealed class InstallChoice { public List<string> Keys=new List<string>();public Dictionary<string,object> Options=new Dictionary<string,object>();public bool Manual; }   // Manual: 직접 설치(기록) instead
    sealed class PackageView {
        public string Id{get;set;} public string Name{get;set;} public string BadgeText{get;set;} public string BadgeTip{get;set;} public Brush BadgeBrush{get;set;} public Brush BadgeFore{get;set;} public string Summary{get;set;} public string Warning{get;set;}
        public Visibility WarningVisibility{get{return String.IsNullOrEmpty(Warning)?Visibility.Collapsed:Visibility.Visible;}}
        public Dictionary<string,object> Data;
    }
    sealed class InventoryRow {
        public string Name{get;set;} public string Source{get;set;} public string Type{get;set;} public string Method{get;set;} public string Entry{get;set;} public string State{get;set;}
        public string Evidence="",Path="",Key="",RemoveHint="";public bool CanRegister,CanRemove;
    }

    // ListSync plus: when rows above the view are added or removed, the first visible row stays first (as the 0.4 grid did).
    static class Anchored {
        public static ScrollViewer Viewer(DependencyObject d){
            if(d is ScrollViewer)return (ScrollViewer)d;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var s=Viewer(VisualTreeHelper.GetChild(d,i));if(s!=null)return s;}
            return null;
        }
        public static void Sync(ListBox list,ObservableCollection<ModRowView> items,List<ModRowView> rows){
            var viewer=Viewer(list);string anchor=null;int top=0;double y=0;
            bool pixel=VirtualizingPanel.GetScrollUnit(list)==ScrollUnit.Pixel;   // the mod lists scroll by pixels (tall cards with 하위 lines)
            if(viewer!=null&&!pixel){top=(int)viewer.VerticalOffset;if(top>0&&top<items.Count)anchor=items[top].Id;}
            else if(viewer!=null&&viewer.VerticalOffset>0)foreach(var it in items){   // the first card still showing, and where its top is
                var c=list.ItemContainerGenerator.ContainerFromItem(it) as FrameworkElement;if(c==null||!c.IsVisible)continue;
                var at=c.TranslatePoint(new Point(0,0),viewer);if(at.Y+c.ActualHeight>0){anchor=it.Id;y=at.Y;break;}}
            ListSync.Sync(items,rows,x=>x.Id,(t,s)=>t.CopyFrom(s));
            if(anchor==null)return;
            if(!pixel){for(int i=0;i<items.Count;i++)if(items[i].Id==anchor){if(i!=top)viewer.ScrollToVerticalOffset(i);break;}return;}
            list.UpdateLayout();var same=items.FirstOrDefault(x=>x.Id==anchor);var box=same==null?null:list.ItemContainerGenerator.ContainerFromItem(same) as FrameworkElement;if(box==null)return;
            double moved=box.TranslatePoint(new Point(0,0),viewer).Y-y;if(Math.Abs(moved)>0.5)viewer.ScrollToVerticalOffset(viewer.VerticalOffset+moved);
        }
    }

    // Colours: one palette, dark and light. The XAML names these keys as DynamicResource; code reads the current brush with B().
    // Neutral greys + one accent; status colours only as soft tints with strong text (pills), never as solid blocks.
    static class Look {
        public static SolidColorBrush Hex(string hex){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
        static readonly Dictionary<string,string[]> palette=new Dictionary<string,string[]>{   // key → { dark, light }
            {"Bg",new[]{"#161618","#F2F2F6"}},{"Panel",new[]{"#1D1D20","#FAFAFC"}},{"Card",new[]{"#252529","#FFFFFF"}},{"CardAlt",new[]{"#202023","#F6F6F9"}},
            {"Raised",new[]{"#2C2C31","#FFFFFF"}},{"RaisedHover",new[]{"#37373D","#EDEDF2"}},{"Line",new[]{"#16FFFFFF","#14000000"}},{"LineStrong",new[]{"#26FFFFFF","#24000000"}},
            {"Text",new[]{"#F2F2F4","#1D1D1F"}},{"Text2",new[]{"#D2D2D8","#3A3A3F"}},{"Sub",new[]{"#A1A1A9","#6B6B72"}},{"Dim",new[]{"#85858D","#86868B"}},
            {"Accent",new[]{"#0A84FF","#007AFF"}},{"AccentHover",new[]{"#3D9CFF","#1A88FF"}},{"AccentText",new[]{"#FFFFFF","#FFFFFF"}},
            {"AccentSoft",new[]{"#260A84FF","#16007AFF"}},{"AccentLine",new[]{"#990A84FF","#80007AFF"}},
            {"Ok",new[]{"#32D74B","#1D9A44"}},{"OkSoft",new[]{"#2232D74B","#1C34C759"}},{"OkLine",new[]{"#6632D74B","#6634C759"}},
            {"Warn",new[]{"#FFB340","#B25F00"}},{"WarnSoft",new[]{"#24FF9F0A","#1FFF9500"}},{"WarnLine",new[]{"#66FF9F0A","#73FF9500"}},
            {"Bad",new[]{"#FF6B63","#D2281F"}},{"BadSoft",new[]{"#22FF453A","#14FF3B30"}},{"BadLine",new[]{"#59FF453A","#59FF3B30"}},
            {"Neutral",new[]{"#1CFFFFFF","#0F000000"}},{"Well",new[]{"#18181B","#EEEEF2"}},{"SegmentBg",new[]{"#232327","#E5E5EA"}},{"SegmentOn",new[]{"#3A3A41","#FFFFFF"}},
            {"Thumb",new[]{"#30FFFFFF","#30000000"}},{"ThumbHover",new[]{"#55FFFFFF","#55000000"}},{"Overlay",new[]{"#D9101012","#D9F2F2F6"}},
            // kind labels: quiet text colours — workshop in Steam blue, data sage, DLL lavender, patcher sand
            {"KindWorkshop",new[]{"#6CB8F2","#1A6CAD"}},{"KindData",new[]{"#7DC6A5","#22835D"}},{"KindDll",new[]{"#A99DF0","#5A4EC2"}},{"KindPatch",new[]{"#D6B06C","#916210"}},
            // buttons, top tabs, title row and list headings (a skin changes these; by default they repeat the keys above)
            {"BtnBg",new[]{"#2C2C31","#FFFFFF"}},{"BtnHover",new[]{"#37373D","#EDEDF2"}},{"BtnText",new[]{"#F2F2F4","#1D1D1F"}},{"BtnLine",new[]{"#26FFFFFF","#24000000"}},
            {"SegText",new[]{"#A1A1A9","#6B6B72"}},{"SegTextOn",new[]{"#F2F2F4","#1D1D1F"}},{"HeadBg",new[]{"#00000000","#00000000"}},{"HeadText",new[]{"#F2F2F4","#1D1D1F"}},{"HeadSub",new[]{"#73737C","#97979E"}},
            {"HeadingText",new[]{"#F2F2F4","#1D1D1F"}},{"MeanderBrush",new[]{"#00000000","#00000000"}}};
        // ⚙ 테마: each one recolours everything (background, cards, buttons, text, accent) in one family of tones; 기본 keeps the keys above.
        // 수묵 = the game's menus — pale ink-wash paper, dark slate title bar and buttons, teal accent, burgundy for danger
        static readonly Dictionary<string,string> ink=new Dictionary<string,string>{
            {"Bg","#E8E8E4"},{"Panel","#C8F4F4F1"},{"Card","#E6FBFBF9"},{"CardAlt","#D2F1F1EE"},{"Raised","#F7F7F4"},{"RaisedHover","#E9EAE6"},{"Line","#1F2B3A42"},{"LineStrong","#3D2B3A42"},
            {"Text","#1C262C"},{"Text2","#2F3C43"},{"Sub","#4E5C63"},{"Dim","#76838A"},
            {"Accent","#2A8C99"},{"AccentHover","#33A0AE"},{"AccentText","#FFFFFF"},{"AccentSoft","#262A8C99"},{"AccentLine","#A62A8C99"},
            {"Bad","#9E2B2B"},{"BadSoft","#1F9E2B2B"},{"BadLine","#669E2B2B"},
            {"SegmentBg","#26323A"},{"SegmentOn","#46565F"},{"SegText","#AEBBC1"},{"SegTextOn","#FFFFFF"},
            {"BtnBg","#3A4A52"},{"BtnHover","#4B5D66"},{"BtnText","#F1F3F2"},{"BtnLine","#5A6D76"},
            {"HeadBg","#F02A363D"},{"HeadText","#F4F1E8"},{"HeadSub","#AFBDC3"},{"HeadingText","#22313A"},
            {"Overlay","#D9E8E8E4"},{"Thumb","#40000000"},{"ThumbHover","#70000000"},{"Well","#2E8C969A"}};
        // 먹빛: dark ink slate, warm paper-white text, the same teal
        static readonly Dictionary<string,string> night=new Dictionary<string,string>{
            {"Bg","#14191C"},{"Panel","#1A2125"},{"Card","#222B30"},{"CardAlt","#1E262A"},{"Raised","#283238"},{"RaisedHover","#313D44"},{"Line","#1AFFFFFF"},{"LineStrong","#2EFFFFFF"},
            {"Text","#ECEAE3"},{"Text2","#CFD3CF"},{"Sub","#9AA6A8"},{"Dim","#6E7B7E"},
            {"Accent","#3AA8B5"},{"AccentHover","#55BCC8"},{"AccentText","#FFFFFF"},{"AccentSoft","#2E3AA8B5"},{"AccentLine","#993AA8B5"},
            {"SegmentBg","#101518"},{"SegmentOn","#33424A"},{"SegText","#8F9EA3"},{"SegTextOn","#F1EFE8"},
            {"BtnBg","#2B373E"},{"BtnHover","#36454D"},{"BtnText","#ECEAE3"},{"BtnLine","#3C4C54"},
            {"HeadText","#ECEAE3"},{"HeadSub","#7F8D91"},{"HeadingText","#ECEAE3"},{"Overlay","#D914191C"},{"Well","#151B1E"}};
        // 청자: pale celadon paper, soft green buttons, deep green accent
        static readonly Dictionary<string,string> celadon=new Dictionary<string,string>{
            {"Bg","#E3ECE6"},{"Panel","#F1F6F2"},{"Card","#FAFCFA"},{"CardAlt","#EEF4F0"},{"Raised","#FFFFFF"},{"RaisedHover","#E4EDE7"},{"Line","#1A2F4A3F"},{"LineStrong","#332F4A3F"},
            {"Text","#1E2B26"},{"Text2","#33433D"},{"Sub","#56675F"},{"Dim","#7D8C85"},
            {"Accent","#2F7D6B"},{"AccentHover","#3A927E"},{"AccentText","#FFFFFF"},{"AccentSoft","#1F2F7D6B"},{"AccentLine","#992F7D6B"},
            {"SegmentBg","#D3E0D8"},{"SegmentOn","#FFFFFF"},{"SegText","#56675F"},{"SegTextOn","#1E2B26"},
            {"BtnBg","#DCE8E1"},{"BtnHover","#CFDFD6"},{"BtnText","#1E2B26"},{"BtnLine","#B5C9BD"},
            {"HeadText","#1E2B26"},{"HeadSub","#7D8C85"},{"HeadingText","#2F5D50"},{"Overlay","#D9E3ECE6"},{"Well","#E6EEE8"}};
        // 쪽빛: deep indigo night, periwinkle accent
        static readonly Dictionary<string,string> indigo=new Dictionary<string,string>{
            {"Bg","#121726"},{"Panel","#182033"},{"Card","#1F2940"},{"CardAlt","#1B2439"},{"Raised","#26314B"},{"RaisedHover","#2E3A57"},{"Line","#1AFFFFFF"},{"LineStrong","#2EFFFFFF"},
            {"Text","#E9ECF5"},{"Text2","#C9CFDE"},{"Sub","#9AA3BA"},{"Dim","#6C7590"},
            {"Accent","#6F8CFF"},{"AccentHover","#8AA1FF"},{"AccentText","#FFFFFF"},{"AccentSoft","#2E6F8CFF"},{"AccentLine","#996F8CFF"},
            {"SegmentBg","#0F1422"},{"SegmentOn","#2E3A57"},{"SegText","#8E97AE"},{"SegTextOn","#F0F2F8"},
            {"BtnBg","#26314B"},{"BtnHover","#2F3C5A"},{"BtnText","#E9ECF5"},{"BtnLine","#36425F"},
            {"HeadText","#E9ECF5"},{"HeadSub","#6C7590"},{"HeadingText","#E9ECF5"},{"Overlay","#D9121726"},{"Well","#131929"}};
        // 맑음: bright and airy — near-white grey, white cards without lines, soft grey pill buttons, clear blue
        static readonly Dictionary<string,string> clear=new Dictionary<string,string>{
            {"Bg","#F5F5F7"},{"Panel","#FBFBFD"},{"Card","#FFFFFF"},{"CardAlt","#F7F7F9"},{"Raised","#FFFFFF"},{"RaisedHover","#F0F0F3"},{"Line","#0F000000"},{"LineStrong","#1F000000"},
            {"Text","#1D1D1F"},{"Text2","#3A3A3C"},{"Sub","#6E6E73"},{"Dim","#98989D"},
            {"Accent","#0071E3"},{"AccentHover","#0077ED"},{"AccentText","#FFFFFF"},{"AccentSoft","#140071E3"},{"AccentLine","#800071E3"},
            {"SegmentBg","#E8E8ED"},{"SegmentOn","#FFFFFF"},{"SegText","#6E6E73"},{"SegTextOn","#1D1D1F"},
            {"BtnBg","#E8E8ED"},{"BtnHover","#DCDCE1"},{"BtnText","#1D1D1F"},{"BtnLine","#00000000"},
            {"HeadText","#1D1D1F"},{"HeadSub","#98989D"},{"HeadingText","#1D1D1F"},{"Overlay","#D9F5F5F7"},{"Well","#F2F2F5"}};
        static readonly Dictionary<string,Dictionary<string,string>> themes=new Dictionary<string,Dictionary<string,string>>{{"ink",ink},{"night",night},{"celadon",celadon},{"indigo",indigo},{"clear",clear}};
        static Dictionary<string,Brush> current=Build(false);
        public static bool Light{get;private set;}
        static Dictionary<string,Brush> Build(bool light){
            var d=palette.ToDictionary(p=>p.Key,p=>(Brush)Hex(p.Value[light?1:0]));
            Dictionary<string,string> over;if(themes.TryGetValue(View.Palette,out over))foreach(var p in over)d[p.Key]=Hex(p.Value);
            if(View.Skin)d["MeanderBrush"]=Skin.Meander;
            // a picture of the user's under any theme: panels, cards and wells let it show through (수묵's own keys already do)
            if(View.HasPicture&&!View.Skin)foreach(var k in new[]{"Panel","Card","CardAlt","Well"}){var c=d[k] as SolidColorBrush;if(c==null)continue;
                byte a=(byte)(k=="Card"?0xF0:k=="Well"?0x99:0xD0);if(c.Color.A>a)d[k]=Hex(String.Format("#{0:X2}{1:X2}{2:X2}{3:X2}",a,c.Color.R,c.Color.G,c.Color.B));}
            return d;
        }
        // a theme other than 기본 has its own brightness
        public static bool? FixedLight{get{var p=View.Palettes.FirstOrDefault(x=>x[0]==View.Palette);return p==null||p[2]==""?(bool?)null:p[2]=="light";}}
        public static Brush B(string key){return current[key];}
        // switches the palette and writes it where the XAML's DynamicResource looks
        public static void Use(bool light,ResourceDictionary into){
            if(FixedLight!=null)light=FixedLight.Value;Light=light;current=Build(light);if(into==null)return;foreach(var p in current)into[p.Key]=p.Value;
            // button shape: 맑음 rounds them into pills, 수묵 keeps the game's near-square plaques
            double radius=View.Palette=="clear"?15:View.Skin?4:7;into["BtnRadius"]=new CornerRadius(radius);
            into["RailRadius"]=new CornerRadius(Math.Max(2,radius-2));   // a plate inside a button group (2 px inset): the same curve
        }
        public static Brush Drop{get{return B("Accent");}}
        public static Brush Ok{get{return B("Ok");}}
        public static Brush Warn{get{return B("Warn");}}
        public static Brush Bad{get{return B("Bad");}}
        public static Brush Sub{get{return B("Sub");}}
        public static Brush Dim{get{return B("Dim");}}
        static readonly Dictionary<Badge,string[]> badges=new Dictionary<Badge,string[]>{   // pill: soft tint + strong text
            {Badge.Running,new[]{"OkSoft","Ok"}},{Badge.Planned,new[]{"AccentSoft","Accent"}},{Badge.Off,new[]{"Neutral","Sub"}},
            {Badge.Waiting,new[]{"WarnSoft","Warn"}},{Badge.Error,new[]{"BadSoft","Bad"}},{Badge.Unknown,new[]{"Neutral","Dim"}}};
        public static Brush Back(Badge b){return B(badges[b][0]);}
        // hover text on the pill: what the colour means and whether the player has to do anything
        public static string Tip(Badge b){
            switch(b){
                case Badge.Running:return "게임 안에서 지금 작동 중입니다.";
                case Badge.Planned:return "켜져 있습니다. 게임을 켜면 자동으로 적용되니 따로 할 일은 없습니다.";
                case Badge.Off:return "꺼 둔 모드입니다. 게임에 들어가지 않습니다.";
                case Badge.Waiting:return "다음에 게임을 켤 때 바뀝니다 — 아래 설명을 확인하세요.";
                case Badge.Error:return "문제가 있습니다 — 아래 설명이나 자세히 보기를 확인하세요.";
                default:return "아직 상태를 확인하지 못했습니다.";
            }
        }
        public static Brush Fore(Badge b){return B(badges[b][1]);}
        public static Brush Kind(string kind){kind=kind??"";return B(kind.Contains("패치")?"KindPatch":kind=="창작마당"?"KindWorkshop":kind=="데이터"?"KindData":kind.Contains("DLL")?"KindDll":"Dim");}
    }

    // Window and dialog backgrounds: the user's own picture (⚙ 배경 그림, any theme) or, for 수묵, its ink-wash painting
    // (made with web ChatGPT for this program, not taken from the game). ⚙ 그림 진하기 lays a wash of the theme's colour over it.
    static class Skin {
        static Brush back,paper,meander;static BitmapSource picture;static string pictureFrom;
        public static void Repaint(){back=null;paper=null;}
        public static void Forget(){picture=null;pictureFrom=null;Repaint();}   // a new picture: read it again
        static BitmapSource Load(string name){
            try{using(var s=typeof(Skin).Assembly.GetManifestResourceStream(name)){if(s==null)return null;var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.StreamSource=s;b.EndInit();b.Freeze();return b;}}catch{return null;}
        }
        // a picture file, read whole (the file stays free) and kept no wider than 2560 px
        public static BitmapSource LoadFile(string path){
            try{
                Func<int,BitmapImage> read=width=>{var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.CreateOptions=BitmapCreateOptions.IgnoreImageCache;b.UriSource=new Uri(path);if(width>0)b.DecodePixelWidth=width;b.EndInit();b.Freeze();return b;};
                var full=read(0);return full.PixelWidth>2560?read(2560):full;
            }catch{return null;}
        }
        static BitmapSource Picture{get{
            string want=View.HasPicture?View.PicturePath:"";
            if(picture==null||pictureFrom!=want){picture=want!=""?LoadFile(want):Load("skin_bg.jpg");pictureFrom=want;}
            return picture;}}
        static Color Wash{get{if(!View.HasPicture)return Color.FromRgb(0xE8,0xE8,0xE4);var b=Look.B("Bg") as SolidColorBrush;return b!=null?b.Color:Colors.Gray;}}
        // the 수묵 painting is anchored bottom-right (its mountains and fortress sit there); a picture of the user's is centred
        static DrawingBrush Washed(BitmapSource b,Color c,byte alpha){
            var g=new DrawingGroup();var r=new Rect(0,0,b.PixelWidth,b.PixelHeight);g.Children.Add(new ImageDrawing(b,r));
            if(alpha>0)g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(alpha,c.R,c.G,c.B)),null,new RectangleGeometry(r)));
            bool own=View.HasPicture;return new DrawingBrush(g){Stretch=Stretch.UniformToFill,AlignmentX=own?AlignmentX.Center:AlignmentX.Right,AlignmentY=own?AlignmentY.Center:AlignmentY.Bottom};
        }
        public static Brush Back{get{
            if(back!=null)return back;var b=Picture;
            back=b==null?(Brush)new SolidColorBrush(Wash):Washed(b,Wash,(byte)Math.Round(255*(100-View.InkStrength)/100.0*0.92));
            back.Freeze();return back;}}
        // dialogs: the same picture washed pale, so small grey text stays readable over it
        public static Brush Paper{get{
            if(paper!=null)return paper;var b=Picture;Color c=View.HasPicture?Wash:Color.FromRgb(0xF4,0xF2,0xEC);
            paper=b==null?(Brush)new SolidColorBrush(c):Washed(b,c,0xB8);paper.Freeze();return paper;}}
        public static bool Shown{get{return View.Skin||View.HasPicture;}}
        // 回 meander: light grey on dark slate, one tile 14×7
        public static Brush Meander{get{
            if(meander!=null)return meander;
            var g=new DrawingGroup();g.Children.Add(new GeometryDrawing(Look.Hex("#2A363D"),null,new RectangleGeometry(new Rect(0,0,14,7))));
            g.Children.Add(new GeometryDrawing(null,new Pen(Look.Hex("#9FB0B7"),0.9),Geometry.Parse("M0,6 H6 V1 H1 V4 H4 M7,1 H13 V6 H8 V3 H11")));
            meander=new DrawingBrush(g){TileMode=TileMode.Tile,Viewport=new Rect(0,0,14,7),ViewportUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,14,7),ViewboxUnits=BrushMappingMode.Absolute};meander.Freeze();return meander;}}
    }

    // ▾ ▸ ▴ typed in the text come from another font and sit lower than the Korean letters: draw the arrow, centred on the text
    static class Glyph {
        public static object Text(string text,string arrow){
            var p=new StackPanel{Orientation=Orientation.Horizontal};
            p.Children.Add(new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center});
            var a=new System.Windows.Shapes.Path{Data=Geometry.Parse(arrow=="up"?"M0,4 L4,0 L8,4":arrow=="right"?"M0,0 L4,4 L0,8":"M0,0 L4,4 L8,0"),StrokeThickness=1.6,
                StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(arrow=="right"?8:7,1,0,0)};
            a.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(Control),1)});
            p.Children.Add(a);return p;
        }
        // ▶ ◀ ▲ ▼ beside a label, drawn too: as text they make the button taller than its neighbours
        public static object Icon(string text,string shape,bool after){
            var p=new StackPanel{Orientation=Orientation.Horizontal};var label=new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center};
            var a=new System.Windows.Shapes.Path{Data=Geometry.Parse(shape=="left"?"M7,0 L0,4.5 L7,9 Z":shape=="up"?"M0,7 L4.5,0 L9,7 Z":shape=="down"?"M0,0 L9,0 L4.5,7 Z":"M0,0 L7,4.5 L0,9 Z"),
                VerticalAlignment=VerticalAlignment.Center,Margin=after?new Thickness(7,1,0,0):new Thickness(0,1,7,0)};
            a.SetBinding(System.Windows.Shapes.Shape.FillProperty,new System.Windows.Data.Binding("Foreground"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(Control),1)});
            if(after){p.Children.Add(label);p.Children.Add(a);}else{p.Children.Add(a);p.Children.Add(label);}return p;
        }
    }

    // 모드매니저 자기 업데이트: GitHub 공개 저장소 nugssorc/loh-modmanager 의 최신 릴리스(애셋 loh-modmanager-<판>.zip = 공개판 zip).
    // 받기 → 크기·SHA-256(릴리스가 주는 digest) → 풀어서 묶음 목록(_files\manifest.json)·버전 확인 → 창을 닫고 작은 스크립트가
    // 지금 폴더를 백업, 새 파일로 바꾸고, 다 같은지 확인한 뒤 다시 연다(실패하면 백업으로 되돌림). 개발판(묶인 모드가 든 personal)은 알림만.
    // ---------- a mod that ships on GitHub releases (its loh-mod.json "github"): latest release, its zip, its notes ----------
    static class ModRelease {
        public static SelfUpdate.Release Latest(string repo){
            string api=Environment.GetEnvironmentVariable("LOH_MM_MOD_RELEASE_URL");if(String.IsNullOrEmpty(api))api="https://api.github.com/repos/"+repo+"/releases/latest";   // tests: a local file
            string text;using(var r=SelfUpdate.Open(api))using(var reader=new StreamReader(r.GetResponseStream(),Encoding.UTF8))text=reader.ReadToEnd();
            var d=J.Serializer.Deserialize<Dictionary<string,object>>(text);
            var rel=new SelfUpdate.Release{Version=Posts.Clean(J.S(d,"tag_name")),Page=J.S(d,"html_url"),Notes=J.S(d,"body")};if(rel.Page=="")rel.Page="https://github.com/"+repo+"/releases/latest";
            var asset=J.Items(J.Get(d,"assets")).FirstOrDefault(a=>J.S(a,"name").EndsWith(".zip",StringComparison.OrdinalIgnoreCase));
            if(asset!=null){rel.Asset=J.S(asset,"name");rel.Url=J.S(asset,"browser_download_url");long.TryParse(J.S(asset,"size"),out rel.Size);string g=J.S(asset,"digest");if(g.StartsWith("sha256:"))rel.Digest=g.Substring(7).ToUpperInvariant();}
            return rel;
        }
        // every published release as {버전, 날짜, 설명}, newest first
        public static List<string[]> Releases(string repo,string testUrl){
            string api=Environment.GetEnvironmentVariable(testUrl);if(String.IsNullOrEmpty(api))api="https://api.github.com/repos/"+repo+"/releases?per_page=20";   // tests: a local file
            string text;using(var r=SelfUpdate.Open(api))using(var reader=new StreamReader(r.GetResponseStream(),Encoding.UTF8))text=reader.ReadToEnd();
            var list=new List<string[]>();
            foreach(var d in J.Serializer.Deserialize<List<Dictionary<string,object>>>(text)){
                if(J.S(d,"draft")=="True"||J.S(d,"prerelease")=="True")continue;
                string date=J.S(d,"published_at");if(date.Length>=10)date=date.Substring(0,10);
                list.Add(new[]{Posts.Clean(J.S(d,"tag_name")),date,J.S(d,"body").Replace("\r\n","\n").Trim()});
            }
            return list;
        }
        // every published release's notes, newest first (a release note without its own heading gets "## v<버전> (<날짜>)"): the patch notes window shows the history, not just the latest
        public static string AllNotes(string repo){
            return String.Join("\n\n",Releases(repo,"LOH_MM_MOD_RELEASES_URL").Select(x=>x[2].StartsWith("#")?x[2]:"## v"+x[0]+(x[1]!=""?" ("+x[1]+")":"")+(x[2]!=""?"\n"+x[2]:"")));
        }
        // the part of a release note under its '바뀐 점' heading (the 받기·참고 parts repeat in every release), else all of it
        public static string Changes(string body){
            var m=System.Text.RegularExpressions.Regex.Match(body,@"(?ms)^#{1,6}[^\n]*바뀐 점[^\n]*\n(.*?)(?=^#{1,6}\s|\z)");
            return m.Success&&m.Groups[1].Value.Trim()!=""?m.Groups[1].Value.Trim():body;
        }
        // its zip into the manager's own folder (checked like the manager's own update); returns the file to install
        public static string Download(SelfUpdate.Release rel,string id){
            if(rel.Url=="")throw new Exception("릴리스에 압축(zip)이 붙어 있지 않습니다");
            string dir=Path.Combine(SelfUpdate.Folder,"mods",System.Text.RegularExpressions.Regex.Replace(id,@"[^A-Za-z0-9_.-]","_"),rel.Version);if(Directory.Exists(dir))Directory.Delete(dir,true);Directory.CreateDirectory(dir);
            string zip=Path.Combine(dir,Path.GetFileName(rel.Asset));
            using(var r=SelfUpdate.Open(rel.Url))using(var s=r.GetResponseStream())using(var f=File.Create(zip))s.CopyTo(f);
            if(rel.Size>0&&new FileInfo(zip).Length!=rel.Size)throw new Exception("받은 파일 크기가 다릅니다(받다가 끊김)");
            if(rel.Digest!=""){string h;using(var st=File.OpenRead(zip))using(var a=System.Security.Cryptography.SHA256.Create())h=BitConverter.ToString(a.ComputeHash(st)).Replace("-","");if(h!=rel.Digest)throw new Exception("받은 파일이 올라간 것과 다릅니다(SHA-256 불일치)");}
            return zip;
        }
    }
    static class SelfUpdate {
        public const string Repo="nugssorc/loh-modmanager";
        public static readonly string Page="https://github.com/"+Repo+"/releases/latest";
        // tests point this at a local release file (file:///…)
        static string Api{get{string o=Environment.GetEnvironmentVariable("LOH_MM_UPDATE_URL");return String.IsNullOrEmpty(o)?"https://api.github.com/repos/"+Repo+"/releases/latest":o;}}
        public sealed class Release { public string Version="",Url="",Page="",Notes="",Digest="",Asset="";public long Size; }
        public static string Folder{get{return Path.Combine(Store.Root,"Update");}}
        public static string Edition(){return J.S(J.ReadFile(Path.Combine(Backend.AppDir,"_files","manifest.json")),"Edition");}
        public static System.Net.WebResponse Open(string url){
            System.Net.ServicePointManager.SecurityProtocol|=System.Net.SecurityProtocolType.Tls12;
            var request=System.Net.WebRequest.Create(url);var http=request as System.Net.HttpWebRequest;
            if(http!=null){http.UserAgent="LOH-ModManager";http.Accept="application/vnd.github+json";http.Timeout=15000;http.ReadWriteTimeout=60000;}
            return request.GetResponse();
        }
        public static Release Latest(){
            string text;using(var r=Open(Api))using(var reader=new StreamReader(r.GetResponseStream(),Encoding.UTF8))text=reader.ReadToEnd();
            var d=J.Serializer.Deserialize<Dictionary<string,object>>(text);
            var rel=new Release{Version=Posts.Clean(J.S(d,"tag_name")),Page=J.S(d,"html_url"),Notes=J.S(d,"body")};if(rel.Page=="")rel.Page=Page;
            var asset=J.Items(J.Get(d,"assets")).FirstOrDefault(a=>System.Text.RegularExpressions.Regex.IsMatch(J.S(a,"name"),@"^loh-modmanager-[\d.]+\.zip$",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            if(asset!=null){rel.Asset=J.S(asset,"name");rel.Url=J.S(asset,"browser_download_url");long.TryParse(J.S(asset,"size"),out rel.Size);string g=J.S(asset,"digest");if(g.StartsWith("sha256:"))rel.Digest=g.Substring(7).ToUpperInvariant();}
            return rel;
        }
        static string Sha(string path){using(var s=File.OpenRead(path))using(var a=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(a.ComputeHash(s)).Replace("-","");}
        // can this copy replace its own files? (an install under Program Files without rights cannot)
        public static bool Writable(){try{string t=Path.Combine(Backend.AppDir,".update-test");File.WriteAllText(t,"x");File.Delete(t);return true;}catch{return false;}}
        // download, check, unpack: returns the folder holding the new 모드관리.exe
        public static string Prepare(Release rel){
            if(rel.Url=="")throw new Exception("릴리스에 모드매니저 zip(loh-modmanager-"+rel.Version+".zip)이 없습니다.");
            string dir=Path.Combine(Folder,rel.Version);if(Directory.Exists(dir))Directory.Delete(dir,true);Directory.CreateDirectory(dir);
            string zip=Path.Combine(dir,rel.Asset);
            using(var r=Open(rel.Url))using(var s=r.GetResponseStream())using(var f=File.Create(zip))s.CopyTo(f);
            if(rel.Size>0&&new FileInfo(zip).Length!=rel.Size)throw new Exception("받은 파일 크기가 다릅니다(받다가 끊김). 다시 해 주세요.");
            if(rel.Digest!=""&&Sha(zip)!=rel.Digest)throw new Exception("받은 파일이 올라간 것과 다릅니다(SHA-256 불일치). 다시 해 주세요.");
            string unpacked=Path.Combine(dir,"new");System.IO.Compression.ZipFile.ExtractToDirectory(zip,unpacked);
            string root=Directory.GetFiles(unpacked,"모드관리.exe",SearchOption.AllDirectories).Select(Path.GetDirectoryName).OrderBy(p=>p.Length).FirstOrDefault();
            if(root==null)throw new Exception("받은 zip 에 모드관리.exe 가 없습니다.");
            var m=J.ReadFile(Path.Combine(root,"_files","manifest.json"));
            if(m==null||J.S(m,"Edition")!="public"||Posts.Compare(J.S(m,"Version"),rel.Version)!=0)throw new Exception("받은 zip 이 모드매니저 "+rel.Version+" 공개판이 아닙니다.");
            foreach(var f in J.Items(J.Get(m,"Files"))){string p=Path.Combine(root,"_files",J.S(f,"Path").Replace('/','\\'));if(!File.Exists(p)||Sha(p)!=J.S(f,"SHA256").ToUpperInvariant())throw new Exception("받은 zip 안의 파일이 깨졌습니다: "+J.S(f,"Path"));}
            return root;
        }
        const string Script=@"param([int]$WaitPid,[string]$New,[string]$Target,[string]$Backup,[string]$Exe,[string]$RunArgs,[string]$Done)
# 모드매니저가 만든 업데이트 스크립트: 창이 닫히길 기다렸다가 지금 폴더를 백업하고 새 파일로 바꾼 뒤 다시 연다. 실패하면 백업으로 되돌린다.
$ErrorActionPreference='Stop';$result=[ordered]@{Ok=$false;Error='';Backup=$Backup}
try{$p=Get-Process -Id $WaitPid -ErrorAction SilentlyContinue;if($p){$null=$p.WaitForExit(30000)}}catch{}
try{
    New-Item -ItemType Directory -Path $Backup -Force | Out-Null
    Get-ChildItem -LiteralPath $Target -Force | Copy-Item -Destination $Backup -Recurse -Force
    for($i=0;$i -lt 30;$i++){try{Get-ChildItem -LiteralPath $New -Force | Copy-Item -Destination $Target -Recurse -Force;break}catch{if($i -eq 29){throw};Start-Sleep -Milliseconds 500}}
    foreach($f in Get-ChildItem -LiteralPath $New -Recurse -File){$t=Join-Path $Target $f.FullName.Substring($New.Length+1);if((Get-FileHash -LiteralPath $t -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash){throw ('바꾼 파일 확인 실패: '+$t)}}
    $result.Ok=$true
}catch{
    $result.Error=$_.Exception.Message
    try{Get-ChildItem -LiteralPath $Backup -Force | Copy-Item -Destination $Target -Recurse -Force}catch{$result.Error+=' / 되돌리기도 실패: '+$_.Exception.Message}
}
[IO.File]::WriteAllText($Done,($result | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$RunArgs=if($RunArgs){[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RunArgs))}else{''}
if($RunArgs){Start-Process -FilePath $Exe -ArgumentList $RunArgs}else{Start-Process -FilePath $Exe}
";
        // writes the script and starts it hidden; the caller then closes the window
        public static void Launch(string newRoot,string from,string to){
            string dir=Folder,target=Backend.AppDir.TrimEnd('\\'),script=Path.Combine(dir,"apply.ps1");
            File.WriteAllText(script,Script,new UTF8Encoding(true));
            string backup=Path.Combine(dir,"backup-"+from+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            // the same arguments again (-GamePath …), passed encoded so quotes and spaces survive both command lines
            string args=Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Join(" ",Environment.GetCommandLineArgs().Skip(1).Select(a=>a.Contains(" ")?"\""+a+"\"":a))));
            Func<string,string> q=s=>"\""+s.TrimEnd('\\')+"\"";
            File.WriteAllText(Path.Combine(dir,"pending.txt"),from+"\t"+to);
            var info=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "+q(script)+" -WaitPid "+Process.GetCurrentProcess().Id+" -New "+q(newRoot)+" -Target "+q(target)+" -Backup "+q(backup)
                +" -Exe "+q(Path.Combine(target,"모드관리.exe"))+" -RunArgs "+(args==""?"\"\"":args)+" -Done "+q(Path.Combine(dir,"done.json"))){UseShellExecute=false,CreateNoWindow=true};
            Process.Start(info);
        }
        // after the restart: what happened (null when there was no update)
        public static string Outcome(string version){
            string pending=Path.Combine(Folder,"pending.txt"),done=Path.Combine(Folder,"done.json");if(!File.Exists(pending))return null;
            string text;var d=J.ReadFile(done);var p=File.ReadAllText(pending).Split('\t');
            if(d==null)text="모드매니저 업데이트 결과를 찾지 못했습니다(지금 v"+version+").";
            else if(J.B(d,"Ok"))text="모드매니저를 v"+(p.Length>1?p[1]:version)+" 로 업데이트했습니다. 이전 버전은 "+J.S(d,"Backup")+" 에 있습니다.";
            else text="모드매니저 업데이트가 실패해 이전 버전(v"+version+")으로 되돌렸습니다: "+J.S(d,"Error");
            try{File.Delete(pending);if(File.Exists(done))File.Delete(done);foreach(var old in Directory.GetDirectories(Folder,"backup-*").OrderByDescending(x=>x).Skip(2))Directory.Delete(old,true);
                foreach(var got in Directory.GetDirectories(Folder).Where(x=>!Path.GetFileName(x).StartsWith("backup-")))Directory.Delete(got,true);}catch{}   // the downloaded zips
            return text;
        }
    }

    // ⚙ settings: screen scale, theme, painting strength, window place, daily update check. %LOCALAPPDATA%\LOH_ModManager\View.txt, key=value.
    static class View {
        public static double Scale=1.0;public static bool ScaleAuto=true;public static double DetailHeight=double.NaN;public static string Palette="basic";public static int InkStrength=100;public static string Picture="";public static bool RememberWindow=true,AutoUpdate=false,SelfCheck=true;public static string LastUpdate="",LastSelfCheck="";
        public static readonly Dictionary<string,string> Skip=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);   // GitHub repo -> the version gone back from (no notice for it)
        public static bool Skin{get{return Palette=="ink";}}
        // ⚙ 배경 그림: a copy kept in the settings folder (Background-<time>.<ext>), so moving or deleting the original changes nothing
        public static string PicturePath{get{return Picture==""?"":Path.Combine(Store.Root,Picture);}}
        public static bool HasPicture{get{return Picture!=""&&System.IO.File.Exists(PicturePath);}}
        // copies the picture in (null = no picture); "" when done, else what went wrong
        public static string SetPicture(string source){
            try{
                if(source==null){Picture="";Save();global::LohModManager.Skin.Forget();return "";}
                if(global::LohModManager.Skin.LoadFile(source)==null)return "그림을 읽지 못했습니다: "+Path.GetFileName(source);
                // a new name each time, so nothing that remembers the last file (WPF's image cache) can hand back the old picture
                Directory.CreateDirectory(Store.Root);string name="Background-"+DateTime.Now.ToString("yyyyMMddHHmmssfff")+Path.GetExtension(source).ToLowerInvariant();
                System.IO.File.Copy(source,Path.Combine(Store.Root,name),true);
                foreach(var old in Directory.GetFiles(Store.Root,"Background*.*"))if(!Path.GetFileName(old).Equals(name,StringComparison.OrdinalIgnoreCase))try{System.IO.File.Delete(old);}catch{}   // only our own earlier copies
                Picture=name;Save();global::LohModManager.Skin.Forget();return "";
            }catch(Exception e){return "그림을 넣지 못했습니다: "+e.Message;}
        }
        public static double Left=double.NaN,Top=double.NaN,Width=double.NaN,Height=double.NaN;public static bool Maximized;
        public static readonly double[] Scales={0.7,0.8,0.9,1.0,1.1,1.25,1.5};
        // 자동: the design size (1320 × 900) fits the screen's work area — shrinks only for a large Windows scaling (FHD at 125–150 %), never above 100 %
        public const double DesignWidth=1320,DesignHeight=900;
        public static double AutoScale(){var a=SystemParameters.WorkArea;double s=Math.Min(1.0,Math.Min((a.Width-40)/DesignWidth,(a.Height-40)/DesignHeight));return Math.Max(0.6,Math.Floor(s*20)/20);}
        // name → {label, brightness ("" = follows 화면 밝기), swatches: background, button, accent}
        public static readonly string[][] Palettes={new[]{"basic","기본","","#1D1D20","#2C2C31","#0A84FF"},new[]{"ink","수묵 (영웅입지전)","light","#E8E8E4","#3A4A52","#2A8C99"},
            new[]{"night","먹빛","dark","#1A2125","#2B373E","#3AA8B5"},new[]{"celadon","청자","light","#E3ECE6","#DCE8E1","#2F7D6B"},new[]{"indigo","쪽빛","dark","#182033","#26314B","#6F8CFF"},new[]{"clear","맑음","light","#F5F5F7","#E8E8ED","#0071E3"}};
        public static readonly int[] InkStrengths={100,75,50,25};
        static string File{get{return Path.Combine(Store.Root,"View.txt");}}
        static double D(string v,double d){double r;return Double.TryParse(v,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out r)?r:d;}
        public static void Load(){
            string size=null;
            try{if(!System.IO.File.Exists(File)){Scale=AutoScale();return;}
                foreach(var line in System.IO.File.ReadAllLines(File)){int eq=line.IndexOf('=');if(eq<1)continue;string k=line.Substring(0,eq).Trim(),v=line.Substring(eq+1).Trim();
                    if(k=="scale"){double sc=D(v,1);if(Scales.Contains(sc))Scale=sc;}else if(k=="size")size=v;else if(k=="detail")DetailHeight=D(v,double.NaN);else if(k=="theme"&&Palettes.Any(a=>a[0]==v))Palette=v;else if(k=="skin"&&v=="ink")Palette="ink";else if(k=="ink"){int s;if(Int32.TryParse(v,out s)&&InkStrengths.Contains(s))InkStrength=s;}else if(k=="picture")Picture=v;
                    else if(k.StartsWith("skip:"))Skip[k.Substring(5)]=v;
                    else if(k=="remember")RememberWindow=v!="0";else if(k=="autoupdate")AutoUpdate=v=="1";else if(k=="selfcheck")SelfCheck=v!="0";else if(k=="lastselfcheck")LastSelfCheck=v;
                    else if(k=="lastupdate")LastUpdate=v;else if(k=="left")Left=D(v,double.NaN);else if(k=="top")Top=D(v,double.NaN);else if(k=="width")Width=D(v,double.NaN);else if(k=="height")Height=D(v,double.NaN);else if(k=="max")Maximized=v=="1";}
            }catch{}
            // size=auto | a picked scale; before 0.12.38 only scale= was kept, and 100 % was the untouched default
            if(size==null)ScaleAuto=Scale==1.0;else{double sc=D(size,0);ScaleAuto=!Scales.Contains(sc);if(!ScaleAuto)Scale=sc;}
            if(ScaleAuto)Scale=AutoScale();
        }
        public static void Save(){
            Func<double,string> n=d=>Double.IsNaN(d)?"":d.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
            try{Directory.CreateDirectory(Store.Root);System.IO.File.WriteAllLines(File,Skip.Select(x=>"skip:"+x.Key+"="+x.Value).Concat(new[]{"size="+(ScaleAuto?"auto":n(Scale)),"scale="+n(ScaleAuto?1.0:Scale),"detail="+n(DetailHeight),"theme="+Palette,"ink="+InkStrength,"picture="+Picture,"remember="+(RememberWindow?"1":"0"),"autoupdate="+(AutoUpdate?"1":"0"),"selfcheck="+(SelfCheck?"1":"0"),"lastselfcheck="+LastSelfCheck,"lastupdate="+LastUpdate,
                "left="+n(Left),"top="+n(Top),"width="+n(Width),"height="+n(Height),"max="+(Maximized?"1":"0")}));}catch{}
        }
    }

    // Type scale for text built in code: the same six sizes as MainWindow.xaml (FsDisplay…FsCaption), read from its resources.
    static class Fs {
        static double Get(string key,double fallback){try{var v=Application.Current==null?null:Application.Current.TryFindResource(key);return v is double?(double)v:fallback;}catch{return fallback;}}
        public static double Display{get{return Get("FsDisplay",22);}}
        public static double Title{get{return Get("FsTitle",17);}}
        public static double Headline{get{return Get("FsHeadline",15);}}
        public static double Body{get{return Get("FsBody",13);}}
        public static double Secondary{get{return Get("FsSecondary",12);}}
        public static double Caption{get{return Get("FsCaption",11.5);}}
    }
    // Durations for the XAML storyboards (StaticResource, read once when the window loads). Windows' "animation effects" off,
    // or software-only drawing (render tier 0: remote desktop, no graphics driver) → every one is zero.
    // Animations tick at the monitor's refresh rate (WPF's default is 60 frames a second, choppy on a 120/144 Hz screen).
    static class Motion {
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern bool EnumDisplaySettings(string device,int mode,ref DevMode dm);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        struct DevMode{[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr,SizeConst=32)] public string Name;public short SpecVersion,DriverVersion,Size,DriverExtra;public int Fields,X,Y,Orientation,FixedOutput;
            public short Color,Duplex,YResolution,TTOption,Collate;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr,SizeConst=32)] public string FormName;
            public short LogPixels;public int BitsPerPel,PelsWidth,PelsHeight,DisplayFlags,DisplayFrequency;}
        public static bool On{get{try{return SystemParameters.ClientAreaAnimation&&(RenderCapability.Tier>>16)>0;}catch{return true;}}}
        public static int Refresh{get{try{var dm=new DevMode();dm.Size=(short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DevMode));return EnumDisplaySettings(null,-1,ref dm)?dm.DisplayFrequency:60;}catch{return 60;}}}
        public static void Install(ResourceDictionary into){
            try{int hz=Math.Max(60,Math.Min(240,Refresh));if(hz>60)System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline),new FrameworkPropertyMetadata{DefaultValue=hz});}catch{}
            Func<int,Duration> ms=n=>new Duration(TimeSpan.FromMilliseconds(On?n:0));
            into["MotionHover"]=ms(120);into["MotionHoverOut"]=ms(200);into["MotionPress"]=ms(80);into["MotionRelease"]=ms(240);into["MotionFade"]=ms(200);
        }
        // windows open with their content fading in (opacity only: moving text by sub-pixels makes it shimmer)
        public static void FadeIn(UIElement e){if(e==null||!On)return;e.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,new Duration(TimeSpan.FromMilliseconds(180))){EasingFunction=new SineEase{EasingMode=EasingMode.EaseOut}});}
        // fades out, then runs done (collapse) unless something showed it again meanwhile (still) — at once when motion is off
        public static void FadeOut(UIElement e,Func<bool> still,Action done){
            if(!On){done();return;}
            var a=new DoubleAnimation(0,new Duration(TimeSpan.FromMilliseconds(160))){EasingFunction=new SineEase{EasingMode=EasingMode.EaseOut}};
            a.Completed+=(s,x)=>{if(still()){done();e.BeginAnimation(UIElement.OpacityProperty,null);}};e.BeginAnimation(UIElement.OpacityProperty,a);
        }
    }
    // Korean wraps between any two syllables in WPF ("…카드를 끌어 / 서"); other apps wrap Korean at spaces (CSS word-break: keep-all).
    // Every wrapping TextBlock gets an invisible WORD JOINER (U+2060) between the letters of each short word, so lines break at spaces.
    // Long runs without spaces (paths, links) are left alone so they can still wrap. Bound text goes through a converter, set text is redone on change.
    static class KeepWords {
        const char WJ='\u2060';const int MaxWord=14;
        static bool Hangul(char c){return c>='\uAC00'&&c<='\uD7A3';}
        public static string Keep(string s){
            if(String.IsNullOrEmpty(s)||s.IndexOf(WJ)>=0||!s.Any(Hangul))return s;
            var sb=new System.Text.StringBuilder(s.Length*2);int i=0;
            while(i<s.Length){
                if(Char.IsWhiteSpace(s[i])){sb.Append(s[i++]);continue;}
                int j=i;while(j<s.Length&&!Char.IsWhiteSpace(s[j]))j++;
                string w=s.Substring(i,j-i);bool join=w.Length<=MaxWord&&w.Any(Hangul)&&w.IndexOfAny(new[]{'\\','/'})<0;
                for(int k=0;k<w.Length;k++){if(join&&k>0)sb.Append(WJ);sb.Append(w[k]);}
                i=j;
            }
            return sb.ToString();
        }
        sealed class Conv:System.Windows.Data.IValueConverter{
            public object Convert(object v,Type t,object p,System.Globalization.CultureInfo c){var s=v as string;return s!=null?Keep(s):v;}
            public object ConvertBack(object v,Type t,object p,System.Globalization.CultureInfo c){return v;}
        }
        static readonly Conv conv=new Conv();
        static readonly System.ComponentModel.DependencyPropertyDescriptor textProp=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
        // SizeChanged, not Loaded: WPF only broadcasts Loaded into subtrees that have their own Loaded handlers, so most text never saw it.
        static readonly DependencyProperty SeenProperty=DependencyProperty.RegisterAttached("KeepWordsSeen",typeof(bool),typeof(KeepWords));
        public static void Install(){
            EventManager.RegisterClassHandler(typeof(TextBlock),FrameworkElement.SizeChangedEvent,new SizeChangedEventHandler((s,e)=>Attach((TextBlock)s)));
        }
        static void Attach(TextBlock tb){
            if(tb.TextWrapping==TextWrapping.NoWrap||(bool)tb.GetValue(SeenProperty))return;
            tb.SetValue(SeenProperty,true);
            var b=System.Windows.Data.BindingOperations.GetBinding(tb,TextBlock.TextProperty);
            if(b!=null){
                if(b.Converter==null&&String.IsNullOrEmpty(b.StringFormat)&&b.Source==null&&b.RelativeSource==null&&String.IsNullOrEmpty(b.ElementName)&&b.Path!=null)
                    tb.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding{Path=b.Path,Mode=System.Windows.Data.BindingMode.OneWay,Converter=conv,TargetNullValue=b.TargetNullValue,FallbackValue=b.FallbackValue});
                return;
            }
            if(DependencyPropertyHelper.GetValueSource(tb,TextBlock.TextProperty).BaseValueSource!=BaseValueSource.Local)return;
            if(tb.Inlines.Count>1)return;   // formatted runs: leave as written
            Changed(tb,EventArgs.Empty);textProp.AddValueChanged(tb,Changed);
        }
        static void Changed(object s,EventArgs e){var tb=(TextBlock)s;string k=Keep(tb.Text);if(!ReferenceEquals(k,tb.Text)&&k!=tb.Text)tb.Text=k;}
    }
    // UI typeface: the one this Windows already uses for its own windows (message font), Malgun Gothic for Hangul it lacks.
    // 0.12.39~0.12.42 shipped Pretendard inside the exe; some PCs drew it broken, so the window keeps the user's own font.
    static class UiFont {
        public static FontFamily Family=new FontFamily("Segoe UI, Malgun Gothic");
        public static void Install(ResourceDictionary into){
            try{var own=SystemFonts.MessageFontFamily;if(own!=null&&!string.IsNullOrEmpty(own.Source))Family=new FontFamily(own.Source+", Malgun Gothic");}catch{}
            into["UiFont"]=Family;
        }
    }
    // Light or dark: follows Windows (apps setting) unless the user picked one; the choice lives in %LOCALAPPDATA%\LOH_ModManager\Theme.txt.
    static class Theme {
        public static string Mode="auto";   // auto | light | dark
        static string File{get{return Path.Combine(Store.Root,"Theme.txt");}}
        public static void Load(){try{var t=System.IO.File.Exists(File)?System.IO.File.ReadAllText(File).Trim():"";if(t=="light"||t=="dark")Mode=t;}catch{}}
        public static void Save(){try{Directory.CreateDirectory(Store.Root);System.IO.File.WriteAllText(File,Mode);}catch{}}
        public static bool SystemLight(){
            try{using(var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")){var v=k==null?null:k.GetValue("AppsUseLightTheme");return v is int&&(int)v==1;}}catch{return false;}
        }
        public static bool WantLight{get{return Mode=="light"||(Mode=="auto"&&SystemLight());}}
        public static string Label{get{return Mode=="light"?"☀":Mode=="dark"?"☾":"◐";}}
        public static string Tip{get{return "화면: "+(Mode=="light"?"밝게":Mode=="dark"?"어둡게":"자동(Windows 설정을 따름)")+" · 누르면 "+(Mode=="auto"?"밝게":Mode=="light"?"어둡게":"자동")+"으로 바꿉니다";}}
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        // the native title bar follows too (Windows 10 2004+ uses 20, older 1809 builds 19)
        public static void TitleBar(Window w){
            try{var h=new System.Windows.Interop.WindowInteropHelper(w).Handle;if(h==IntPtr.Zero)return;int dark=Look.Light&&!View.Skin?0:1;if(DwmSetWindowAttribute(h,20,ref dark,4)!=0)DwmSetWindowAttribute(h,19,ref dark,4);}catch{}
        }
    }

    sealed class ManagerWindow {
        public readonly Window Window;
        const string Version="0.12.52";
        static readonly string[] Routes={"hid.dll","version.dll","winhttp.dll","winmm.dll"};
        static readonly HashSet<string> FixedPath=new HashSet<string>{"gift","jade","research"};
        string game;
        Catalog catalog=new Catalog();Dictionary<string,object> pending;
        // one list: data / workshop / package rows as read from the game (before reservation and edits), and the last interleave
        List<ModEntry> extraRows=new List<ModEntry>();List<string> hints=new List<string>();
        List<ModEntry> baseRows=new List<ModEntry>(),working=new List<ModEntry>();
        readonly HashSet<string> unregistered=new HashSet<string>();
        LiveSnapshot live=new LiveSnapshot();LiveMonitor monitor;bool liveKnown;
        bool busy,catalogLoading,catalogAgain,catalogReset,catalogFailed;
        readonly ObservableCollection<ModRowView> active=new ObservableCollection<ModRowView>(),inactive=new ObservableCollection<ModRowView>();
        readonly ObservableCollection<AlertView> alerts=new ObservableCollection<AlertView>();
        List<InventoryRow> inventory;List<Profile> profileCache;
        // selectedId: the row the details show; selectedIds: every row picked with Ctrl/Shift (one list at a time).
        // Setting selectedId alone (after a save, an install…) picks just that row.
        readonly HashSet<string> selectedIds=new HashSet<string>();string primaryId;bool selecting;
        string selectedId{get{return primaryId;}set{primaryId=value;selectedIds.Clear();if(value!=null)selectedIds.Add(value);}}
        string pendingSingle;   // a plain click on a card of a picked group: picks it alone on release, unless it starts a drag
        readonly DispatcherTimer reloadTimer,pendingTimer,freshTimer;HashSet<string> freshSet=new HashSet<string>();
        // drag and drop (cards: the window's own drag; Explorer files: OLE onto DropLayer)
        Point dragStart,dragGrab;ModRowView dragItem;ListBox dragSource;ModRowView dropMarked;bool dragging;double scrollCarry;
        Canvas dragLayer;System.Windows.Shapes.Rectangle dragGhost;Border dragResult,dragResultBack,dragResultPill;TextBlock dragResultText;DispatcherTimer dragTimer;Border activePanel,inactivePanel;bool dropOff;
        // posts, settings files, last update check (id → version in the post title)
        Button settingsButton;StackPanel routePanel;TextBlock postLinkText;readonly Dictionary<string,string> updates=new Dictionary<string,string>();
        // 기록하며 직접 설치: the open recording (its install id), if any
        string watchId="",watchName="",watchEntryNote="",watchContent="";
        // the last game run as the entry reported it (kept after the game closes): mods that did not work there
        RuntimeState lastRun=new RuntimeState();

        readonly ListBox activeList,inactiveList,dataList;readonly DataGrid inventoryGrid;
        readonly TextBlock gamePath,gameText,gameSub,changeCount,loadingText,activeCount,inactiveCount,activeEmpty,inactiveEmpty,noticeText,detailTitle,detailStatus,detailInfo,detailTech,busyText,inventoryDetail;
        readonly System.Windows.Shapes.Ellipse gameDot;readonly Border notice,busyLayer,activeEnd,activeRail;readonly Canvas activeEndLayer;
        readonly Button saveButton,launchButton,revertButton,profileButton,noticeAction1,noticeAction2,locateButton,unregisterButton,openFolder,openLog,removeButton,patchRebuild;
        readonly ToggleButton tabMods,tabInventory,alertsToggle,advancedToggle,dataToggle;
        readonly ItemsControl alertList;readonly StackPanel advancedPanel,routeChips;readonly Grid modsView,inventoryView;
        Action noticeFirst,noticeSecond;
        // external mod packages (archives dropped on the window)
        readonly ListBox packageList;readonly Grid packagesView;readonly Border dropLayer;readonly ToggleButton tabPackages;
        readonly TextBlock packagesEmpty,packageTitle,packageInfo,packageDocName;readonly TextBox packageDoc;readonly Button packageUninstall;
        readonly Queue<string> installQueue=new Queue<string>();bool installing,packagesLoaded;string selectedPackage;
        readonly Border trashZone;readonly Button entryButton,offOthersButton;List<Conflict> conflicts=new List<Conflict>();Dictionary<string,ModFacts> dataFacts=new Dictionary<string,ModFacts>();Dictionary<string,OneList.Owner> owners=new Dictionary<string,OneList.Owner>();

        T F<T>(string name) where T:class{var x=Window.FindName(name) as T;if(x==null)throw new InvalidOperationException("화면 요소 없음: "+name);return x;}

        public ManagerWindow(string gamePathArg) {
            game=gamePathArg;
            using(var s=typeof(ManagerWindow).Assembly.GetManifestResourceStream("MainWindow.xaml"))Window=(Window)XamlReader.Load(s);
            // theme before anything paints; menus, tooltips and dialogs find the same keys through the application
            Theme.Load();View.Load();Look.Use(Theme.WantLight,Window.Resources);
            if(Application.Current!=null)Application.Current.Resources.MergedDictionaries.Add(Window.Resources);
            FitScreen();
            Window.SourceInitialized+=(s,e)=>{Theme.TitleBar(Window);TextMode();};
            Window.Loaded+=(s,e)=>Motion.FadeIn(Window.Content as UIElement);
            Window.DpiChanged+=(s,e)=>TextMode();
            Microsoft.Win32.UserPreferenceChangedEventHandler follow=(s,e)=>{if(Theme.Mode=="auto")Window.Dispatcher.BeginInvoke(new Action(()=>{if(Look.Light!=Theme.WantLight)ApplyTheme();}));};
            Microsoft.Win32.SystemEvents.UserPreferenceChanged+=follow;Window.Closed+=(s,e)=>Microsoft.Win32.SystemEvents.UserPreferenceChanged-=follow;
            Window.Title="영웅입지전 · 모드매니저 "+Version;
            var versionLabel=F<TextBlock>("VersionLabel");versionLabel.Text=Version+" · 패치노트";versionLabel.MouseLeftButtonUp+=(s,e)=>ShowSelfNotes();
            versionLabel.MouseEnter+=(s,e)=>versionLabel.TextDecorations=TextDecorations.Underline;versionLabel.MouseLeave+=(s,e)=>versionLabel.TextDecorations=null;
            F<Button>("SettingsButtonTop").Click+=(s,e)=>OpenSettings();
            ApplyScale();PlaceWindow();Window.SizeChanged+=(s,e)=>FitDetail();
            var detailRow=F<RowDefinition>("DetailRowDef");if(!Double.IsNaN(View.DetailHeight))detailRow.Height=new GridLength(Math.Max(detailRow.MinHeight,Math.Min(View.DetailHeight,700)));
            F<GridSplitter>("DetailSplitter").DragCompleted+=(s,e)=>{View.DetailHeight=detailRow.ActualHeight;View.Save();};
            ApplySkin();F<DockPanel>("HeaderRow").SizeChanged+=(s,e)=>FitSkinHead();
            // Set the window icon explicitly: the taskbar otherwise keeps a cached blank icon for an exe replaced in place.
            try{using(var s=typeof(ManagerWindow).Assembly.GetManifestResourceStream("app.ico"))if(s!=null){var d=new System.Windows.Media.Imaging.IconBitmapDecoder(s,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);Window.Icon=d.Frames[0];}}catch{}
            activeList=F<ListBox>("ActiveList");inactiveList=F<ListBox>("InactiveList");dataList=F<ListBox>("DataList");inventoryGrid=F<DataGrid>("InventoryGrid");
            gamePath=F<TextBlock>("GamePath");gameText=F<TextBlock>("GameText");gameSub=F<TextBlock>("GameSub");changeCount=F<TextBlock>("ChangeCount");loadingText=F<TextBlock>("LoadingText");
            activeCount=F<TextBlock>("ActiveCount");inactiveCount=F<TextBlock>("InactiveCount");activeEmpty=F<TextBlock>("ActiveEmpty");inactiveEmpty=F<TextBlock>("InactiveEmpty");
            noticeText=F<TextBlock>("NoticeText");detailTitle=F<TextBlock>("DetailTitle");detailStatus=F<TextBlock>("DetailStatus");detailInfo=F<TextBlock>("DetailInfo");detailTech=F<TextBlock>("DetailTech");
            busyText=F<TextBlock>("BusyText");inventoryDetail=F<TextBlock>("InventoryDetail");gameDot=F<System.Windows.Shapes.Ellipse>("GameDot");notice=F<Border>("Notice");busyLayer=F<Border>("Busy");activeEnd=F<Border>("ActiveEnd");activeRail=F<Border>("ActiveRail");activeEndLayer=F<Canvas>("ActiveEndLayer");
            saveButton=F<Button>("SaveButton");launchButton=F<Button>("LaunchButton");profileButton=F<Button>("ProfileButton");noticeAction1=F<Button>("NoticeAction1");noticeAction2=F<Button>("NoticeAction2");
            locateButton=F<Button>("LocateButton");unregisterButton=F<Button>("UnregisterButton");openFolder=F<Button>("OpenFolder");openLog=F<Button>("OpenLog");removeButton=F<Button>("RemoveButton");patchRebuild=F<Button>("PatchRebuild");
            tabMods=F<ToggleButton>("TabMods");tabInventory=F<ToggleButton>("TabInventory");alertsToggle=F<ToggleButton>("AlertsToggle");advancedToggle=F<ToggleButton>("AdvancedToggle");dataToggle=F<ToggleButton>("DataToggle");
            alertList=F<ItemsControl>("Alerts");advancedPanel=F<StackPanel>("AdvancedPanel");routeChips=F<StackPanel>("RouteChips");modsView=F<Grid>("ModsView");inventoryView=F<Grid>("InventoryView");
            packageList=F<ListBox>("PackageList");packagesView=F<Grid>("PackagesView");dropLayer=F<Border>("DropLayer");tabPackages=F<ToggleButton>("TabPackages");
            packagesEmpty=F<TextBlock>("PackagesEmpty");packageTitle=F<TextBlock>("PackageTitle");packageInfo=F<TextBlock>("PackageInfo");packageDocName=F<TextBlock>("PackageDocName");packageDoc=F<TextBox>("PackageDoc");
            packageUninstall=F<Button>("PackageUninstall");
            trashZone=F<Border>("TrashZone");trashText=F<TextBlock>("TrashText");trashTint=F<Border>("TrashTint");entryButton=F<Button>("EntryButton");offOthersButton=F<Button>("OffOthersButton");
            foreach(var l in new[]{activeList,inactiveList})l.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,new RoutedEventHandler(IconClick));
            offOthersButton.Click+=(s,e)=>TurnOffClashing(SelectedRow());F<Button>("SortButton").Click+=(s,e)=>AutoSort();

            activeList.ItemsSource=active;inactiveList.ItemsSource=inactive;alertList.ItemsSource=alerts;
            foreach(var list in new[]{activeList,inactiveList}){
                var l=list;
                l.SelectionMode=SelectionMode.Extended;   // Ctrl+클릭 · Shift+클릭 · Ctrl+A
                l.SelectionChanged+=(s,e)=>OnSelect(l,e);
                l.MouseDoubleClick+=(s,e)=>{if(OnCardControl(e.OriginalSource as DependencyObject))return;var item=ItemAt(l,e.GetPosition(l));if(item!=null)Toggle(item.Id,l==inactiveList,null);};
                l.PreviewMouseLeftButtonDown+=(s,e)=>{
                    if(dragging)return;dragStart=e.GetPosition(Window);dragItem=ItemAt(l,e.GetPosition(l));dragSource=l;pendingSingle=null;
                    // a plain press on one of several picked cards keeps the group (it may be dragged); the release picks it alone
                    if(dragItem!=null&&e.ClickCount==1&&Keyboard.Modifiers==ModifierKeys.None&&l.SelectedItems.Count>1&&l.SelectedItems.Contains(dragItem)){pendingSingle=dragItem.Id;e.Handled=true;l.Focus();}
                };
                l.PreviewMouseLeftButtonUp+=(s,e)=>{if(pendingSingle!=null&&!dragging){var id=pendingSingle;pendingSingle=null;selectedId=id;Render();}pendingSingle=null;};
                l.PreviewMouseMove+=(s,e)=>DragBegin(l,e);
                // only Explorer files (the DropLayer cover) may be dropped over a list; cards move with DragTrack below
                l.DragOver+=(s,e)=>{if(!e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.None;e.Handled=true;}};
            }
            enableButton=F<Button>("EnableButton");disableButton=F<Button>("DisableButton");upButton=F<Button>("UpButton");downButton=F<Button>("DownButton");
            enableButton.Click+=(s,e)=>ToggleMany(SelectedRows(),true);
            disableButton.Click+=(s,e)=>ToggleMany(SelectedRows(),false);
            upButton.Click+=(s,e)=>Move(-1);downButton.Click+=(s,e)=>Move(1);
            F<Button>("AllOffButton").Click+=(s,e)=>AllOff();revertButton=F<Button>("RevertButton");revertButton.Click+=(s,e)=>Revert();
            saveButton.Click+=(s,e)=>Save();F<Button>("RefreshButton").Click+=(s,e)=>Refresh();launchButton.Click+=(s,e)=>Launch();
            profileButton.Click+=(s,e)=>ShowProfileMenu();F<Button>("ChooseGame").Click+=(s,e)=>ChooseGame();
            F<Button>("NoticeClose").Click+=(s,e)=>HideNotice();
            noticeAction1.Click+=(s,e)=>{var a=noticeFirst;if(a!=null)a();};noticeAction2.Click+=(s,e)=>{var a=noticeSecond;if(a!=null)a();};
            alertsToggle.Click+=(s,e)=>alertList.Visibility=alertsToggle.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
            advancedToggle.Click+=(s,e)=>{advancedPanel.Visibility=advancedToggle.IsChecked==true?Visibility.Visible:Visibility.Collapsed;advancedToggle.Content=Glyph.Text("고급 설정",advancedToggle.IsChecked==true?"down":"right");};advancedToggle.Content=Glyph.Text("고급 설정","right");
            dataToggle.Click+=(s,e)=>{bool on=dataToggle.IsChecked==true;dataList.Visibility=on?Visibility.Visible:Visibility.Collapsed;dataToggle.Content=on?Glyph.Text("데이터 모드 숨기기","up"):Glyph.Text("데이터 모드 보기","down");if(on)ScanDataMods();};dataToggle.Content=Glyph.Text("데이터 모드 보기","down");
            dataToggle.Visibility=Visibility.Collapsed;   // data mods are rows of the main list now
            tabMods.Click+=(s,e)=>ShowTab(0);tabPackages.Click+=(s,e)=>ShowTab(1);tabInventory.Click+=(s,e)=>ShowTab(2);
            foreach(string route in Routes){var chip=new RadioButton{Content=route,GroupName="route",Style=(Style)Window.FindResource("Chip"),Tag=route};chip.Checked+=(s,e)=>SetRoute((string)((RadioButton)s).Tag);routeChips.Children.Add(chip);}
            locateButton.Click+=(s,e)=>Locate();unregisterButton.Click+=(s,e)=>Unregister();
            openFolder.Click+=(s,e)=>{var r=SelectedRow();if(r==null)return;if(File.Exists(r.Path))Process.Start("explorer.exe","/select,\""+r.Path+"\"");else if(Directory.Exists(r.Path))Process.Start("explorer.exe","\""+r.Path+"\"");};
            openLog.Click+=(s,e)=>OpenLogFile();
            removeButton.Click+=(s,e)=>{var rows=SelectedRows();if(rows.Count>1)DeleteMany(rows);else DeleteRow(SelectedRow());};
            // a held card: the window keeps the mouse, so the wheel and the edge auto-scroll work while dragging (trash zone = delete)
            dragLayer=F<Canvas>("DragLayer");dragGhost=F<System.Windows.Shapes.Rectangle>("DragGhost");dragResult=F<Border>("DragResult");dragResultBack=F<Border>("DragResultBack");dragResultPill=F<Border>("DragResultPill");dragResultText=F<TextBlock>("DragResultText");activePanel=F<Border>("ActivePanel");inactivePanel=F<Border>("InactivePanel");
            settingsButton=F<Button>("SettingsButton");routePanel=F<StackPanel>("RoutePanel");postLinkText=F<TextBlock>("PostLinkText");
            settingsButton.Click+=(s,e)=>OpenSettings(SelectedRow());F<Button>("LinkButton").Click+=(s,e)=>EditLink(SelectedRow());
            F<Button>("UpdateButton").Click+=(s,e)=>CheckUpdates();F<Button>("BugButton").Click+=(s,e)=>BugReport();
            dragTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};dragTimer.Tick+=(s,e)=>DragAutoScroll();
            Window.PreviewMouseMove+=(s,e)=>{if(dragging){DragTrack(e.GetPosition(dragLayer));e.Handled=true;}};
            Window.PreviewMouseLeftButtonUp+=(s,e)=>{if(dragging){e.Handled=true;DragEnd(true);}};
            Window.PreviewMouseWheel+=(s,e)=>{if(dragging){e.Handled=true;DragWheel(e.Delta);}};
            Window.LostMouseCapture+=(s,e)=>{if(dragging&&Mouse.Captured!=Window)DragEnd(false);};
            Window.Deactivated+=(s,e)=>{if(dragging)DragEnd(false);};
            entryButton.Click+=(s,e)=>SwitchEntry(SelectedRow());
            // Archives or folders dragged from Explorer: a cover appears, dropping starts the install preview.
            dropOnBox=F<Border>("DropOn");dropOffBox=F<Border>("DropOff");
            Window.PreviewDragEnter+=(s,e)=>{if(!busy&&!dragging&&e.Data.GetDataPresent(DataFormats.FileDrop)){dropLayer.Visibility=Visibility.Visible;fileOverActive=fileOverInactive=false;}};
            dropLayer.DragOver+=(s,e)=>{e.Effects=busy?DragDropEffects.None:DragDropEffects.Copy;e.Handled=true;if(!busy)FileDragTrack(e);};
            dropLayer.DragLeave+=(s,e)=>FileDragEnd();
            dropLayer.Drop+=(s,e)=>{
                var place=fileOverActive||fileOverInactive?new DropPlace{Off=fileOverInactive}:null;   // the bottom of that list
                FileDragEnd();e.Handled=true;var files=e.Data.GetData(DataFormats.FileDrop) as string[];
                // Explorer waits until Drop returns: open the preview afterwards.
                var dlls=files==null?new string[0]:files.Where(f=>File.Exists(f)&&f.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).ToArray();var rest=files==null?new string[0]:files.Except(dlls).ToArray();
                if(dlls.Length>0)Window.Dispatcher.BeginInvoke(new Action(()=>{if(!busy)AddExternal(dlls[0]);}));   // a DLL mod: added where it is, one at a time
                if(rest.Length>0)Window.Dispatcher.BeginInvoke(new Action(()=>QueueInstall(rest,place)));
            };
            patchRebuild.Click+=(s,e)=>RebuildPatches(null);
            F<Button>("RestoreButton").Click+=(s,e)=>{if(!busy)RestoreInstalled();};
            subPanel=F<StackPanel>("SubPanel");ModRowView.OpenChanged=(id,open)=>{var o=OpenCards();if(open?o.Add(id):o.Remove(id))SaveOpenCards();};   // only a real change is written
            SubRowView.Toggled=(row,on)=>Window.Dispatcher.BeginInvoke(new Action(()=>SubToggled(row,on)));   // after the click has finished
            F<Button>("LaunchButton").Content=Glyph.Icon("게임 실행","play",false);
            gamePath.MouseLeftButtonUp+=(s,e)=>{if(Directory.Exists(game))Process.Start("explorer.exe","\""+game+"\"");};
            packageList.SelectionChanged+=(s,e)=>{var p=packageList.SelectedItem as PackageView;selectedPackage=p==null?null:p.Id;ShowPackage();};
            packageUninstall.Click+=(s,e)=>UninstallPackage();
            F<Button>("RegisterButton").Click+=(s,e)=>{var i=inventoryGrid.SelectedItem as InventoryRow;if(i==null)return;if(!i.CanRegister&&i.Type!="입구·프록시 DLL"){MessageBox.Show(Window,i.Evidence,i.Type);return;}AddExternal(i.Path);};
            F<Button>("InventoryRemove").Click+=(s,e)=>{var i=inventoryGrid.SelectedItem as InventoryRow;if(i==null)return;if(!i.CanRemove){MessageBox.Show(Window,i.RemoveHint,"제거 방법 안내");return;}RemoveInstalled(i.Key,i.Name);};
            inventoryGrid.SelectionChanged+=(s,e)=>{var i=inventoryGrid.SelectedItem as InventoryRow;inventoryDetail.Text=i==null?"":i.Name+" / "+i.Type+"\n"+i.Path+"\n"+i.Evidence;};
            inventoryGrid.MouseDoubleClick+=(s,e)=>{var i=inventoryGrid.SelectedItem as InventoryRow;if(i!=null&&i.CanRegister)AddExternal(i.Path);};
            Window.PreviewKeyDown+=OnKey;Window.Closing+=OnClosing;Window.Closing+=(s,e)=>{if(!e.Cancel)RememberPlace();};Window.Closed+=(s,e)=>{if(monitor!=null)monitor.Dispose();};

            reloadTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(800)};reloadTimer.Tick+=(s,e)=>{if(busy)return;reloadTimer.Stop();ReloadCatalog(false);};
            freshTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(6)};freshTimer.Tick+=(s,e)=>{if(busy||live.Game!=null)return;freshTimer.Stop();ReviveFresh();};
            pendingTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};pendingTimer.Tick+=(s,e)=>{pendingTimer.Stop();if(live.Game==null&&pending!=null&&!busy)ApplyPending(true);};
            Window.Loaded+=(s,e)=>OpenGame(game);
        }

        // ---------- loading ----------
        void OpenGame(string folder) {
            if(monitor!=null){monitor.Dispose();monitor=null;}
            game=folder;gamePath.Text="게임 경로: "+game;profileCache=null;subPending.Clear();catalog=CatalogReader.Quick(game);pending=LoadPending();unregistered.Clear();inventory=null;inventoryGrid.ItemsSource=null;
            hints=OneList.LoadHints(game);freshSet=new HashSet<string>(Store.Fresh(game));try{extraRows=OneList.Extra(game,catalog,Linked(catalog));}catch{extraRows=new List<ModEntry>();}
            packagesLoaded=false;packageList.ItemsSource=null;selectedPackage=null;if(packagesView.Visibility==Visibility.Visible)LoadPackages();
            RebuildBase();working=baseRows.Select(r=>r.Clone()).ToList();movedCards.Clear();selectedId=null;liveKnown=false;live=new LiveSnapshot();linkCache.Clear();
            var dispatcher=Window.Dispatcher;
            monitor=new LiveMonitor(game,snap=>dispatcher.BeginInvoke(new Action(()=>OnLive(snap))),()=>dispatcher.BeginInvoke(new Action(()=>{reloadTimer.Stop();reloadTimer.Start();})));
            monitor.SetRows(AllRows());Render();ReloadCatalog(true);
            watchId="";watchName="";FindOpenWatch();if(watchId!="")ShowWatchNotice();LoadLastRun();CheckGameBuild();
            string updated=SelfUpdate.Outcome(Version);if(updated!=null)ShowNotice(updated.Contains("실패")?"warn":"ok",updated,null,null,null,null);
            if(View.AutoUpdate&&View.LastUpdate!=DateTime.Now.ToString("yyyy-MM-dd")){var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};t.Tick+=(s,e)=>{t.Stop();if(!busy)CheckUpdates(true);};t.Start();}
            else if(View.SelfCheck&&View.LastSelfCheck!=DateTime.Now.ToString("yyyy-MM-dd")){var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};t.Tick+=(s,e)=>{t.Stop();CheckSelf();};t.Start();}
            if(!File.Exists(Path.Combine(game,"ThreeKingdom.exe")))ShowNotice("error","게임을 찾지 못했습니다(Steam 라이브러리를 모두 찾아봄): "+game+"\n'게임 경로 설정'에서 ThreeKingdom.exe 가 있는 폴더를 골라 주세요. 고른 경로는 다음에도 그대로 씁니다.","게임 경로 설정",ChooseGame,null,null);
        }
        Dictionary<string,object> LoadPending(){
            var p=Store.LoadPending(game);if(p==null)return null;
            if(!String.Equals(Path.GetFullPath(J.S(p,"Game")).TrimEnd('\\'),Path.GetFullPath(game).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))return null;
            return p;
        }
        IEnumerable<ModEntry> AllRows(){return working.Concat(baseRows).Concat(catalog.Rows).Where(r=>r.IsDll);}
        static HashSet<string> Linked(Catalog c){return new HashSet<string>(c.Rows.Where(r=>r.Data!="").Select(r=>r.Data),StringComparer.OrdinalIgnoreCase);}
        void Refresh(){CheckGameBuild();ReloadCatalog(false);if(monitor!=null)monitor.Poke(true);if(inventory!=null&&inventoryView.Visibility==Visibility.Visible)ScanInventory();if(dataToggle.IsChecked==true)ScanDataMods();if(packagesView.Visibility==Visibility.Visible)LoadPackages();}
        void ReloadCatalog(bool reset) {
            if(catalogLoading){catalogAgain=true;catalogReset|=reset;return;}
            catalogLoading=true;catalogFailed=false;Render();string folder=game;var startRows=working.Select(r=>r.Clone()).ToList();
            Task.Run(()=>{var c=CatalogReader.FromSnapshot(Backend.Run("Catalog",folder,null));return Tuple.Create(c,OneList.Extra(folder,c,Linked(c)));}).ContinueWith(t=>{
                catalogLoading=false;if(folder!=game)return;
                if(t.IsFaulted){catalogFailed=true;ShowNotice("error","모드 목록을 읽지 못했습니다: "+Message(t.Exception),"다시 시도",()=>ReloadCatalog(false),null,null);}
                else{
                    // Edits are judged against the list the player saw (before this reload), so a change from outside — the game's own
                    // list, the manager ticking new data mods — is not mistaken for one and kept back as 'unsaved'.
                    bool wasDirty=Dirty();var oldBase=baseRows.ToDictionary(r=>r.Id);
                    catalog=t.Result.Item1;extraRows=t.Result.Item2;owners=OneList.Owners(catalog);RebuildBase();
                    // A reset (after save/install) must not throw away edits the player made while this reload was running.
                    bool editedMeanwhile=Changes(working,startRows)>0;
                    if((reset&&!editedMeanwhile)||!wasDirty){working=baseRows.Select(r=>r.Clone()).ToList();movedCards.Clear();}
                    else{
                        var fresh=baseRows.ToDictionary(r=>r.Id);
                        foreach(var w in working){ModEntry o,n;if(oldBase.TryGetValue(w.Id,out o)&&o.Enabled==w.Enabled&&fresh.TryGetValue(w.Id,out n))w.Enabled=n.Enabled;}   // untouched rows follow the new list
                        working=Merge(working,baseRows);
                    }
                    ScanConflicts();   // on the list just read: the first one, from the quick read, knows no DLL card's data
                    if(reset)unregistered.Clear();
                    if(placeAfterReload!=null){var got=PlaceNew(placeAfterReload);if(got!=null)revealIds=got;if(got!=null||++placeAfterReload.Tries>2)placeAfterReload=null;}   // a reload already running may not have it yet
                    if(monitor!=null){monitor.EnsureWatch();monitor.SetRows(AllRows());}
                    ShowPendingNotice();ShowPatchNotice();ShowLinkNotice();ShowWorkshopNotice();if(!FixTakenEntry())ShowForeignNotice();ShowFirstRunNotice();
                }
                if(revealIds!=null){var shown0=active.Concat(inactive).FirstOrDefault(x=>revealIds.Contains(x.Id));if(shown0!=null)selectedId=shown0.Id;}
                Render();
                if(revealIds!=null){var ids=revealIds;revealIds=null;   // the new mod: picked, and its list scrolled to it once laid out
                    Window.Dispatcher.BeginInvoke(new Action(()=>{var on=active.FirstOrDefault(x=>ids.Contains(x.Id));var off=on==null?inactive.FirstOrDefault(x=>ids.Contains(x.Id)):null;
                        if(on!=null)activeList.ScrollIntoView(on);else if(off!=null)inactiveList.ScrollIntoView(off);}),System.Windows.Threading.DispatcherPriority.Loaded);}
                if(catalogAgain){bool again=catalogReset;catalogAgain=false;catalogReset=false;ReloadCatalog(again);}
            },TaskScheduler.FromCurrentSynchronizationContext());
        }
        static string ListPrint(Dictionary<string,bool> s){return s==null?"":String.Join(",",s.OrderBy(x=>x.Key).Select(x=>x.Key+(x.Value?"1":"0")));}
        void OnLive(LiveSnapshot snap) {
            var before=live.Game;bool wasKnown=liveKnown;string oldList=ListPrint(live.Selection);live=snap;liveKnown=true;
            if(wasKnown&&ListPrint(snap.Selection)!=oldList&&!busy){reloadTimer.Stop();reloadTimer.Start();}   // the game list changed (in game or elsewhere)
            if(wasKnown&&before!=null&&snap.Game==null)freshTimer.Start();
            if(wasKnown&&before!=null&&snap.Game==null){LoadLastRun();if(pending==null&&watchId==""){var started=before.StartUtc;Window.Dispatcher.BeginInvoke(new Action(()=>AfterRunNotice(started)),DispatcherPriority.Background);}}
            else if(snap.Game==null&&snap.Selection!=null&&!freshTimer.IsEnabled&&Store.FirstRun(game))freshTimer.Start();   // the game made its first list (seen or not, window open or not)
            if(wasKnown&&before!=null&&snap.Game==null&&pending!=null){ShowNotice("warn","게임이 꺼졌습니다. 3초 뒤 예약한 변경을 적용합니다.",null,null,"예약 취소",CancelPending);pendingTimer.Stop();pendingTimer.Start();}
            else if(!wasKnown||(before==null)!=(snap.Game==null))ShowPendingNotice();
            if(catalog.Complete)ShowFirstRunNotice();   // once; the catalog load may have finished while busy
            Render();
        }

        // ---------- the model: catalog (saved) → base (+ reservation) → working (+ unsaved edits) ----------
        List<string> savedDllOrder=new List<string>(),gameOrder;readonly HashSet<string> movedCards=new HashSet<string>();   // cards the player moved since the list was read (a linked card's DLL and data follow only then)
        void RebuildBase() {
            var rows=catalog.Rows.Select(r=>r.Clone()).ToList();
            var ch=pending==null?null:J.Obj(J.Get(pending,"Choices"));
            if(ch!=null){
                var removes=new HashSet<string>(J.Strings(J.Get(ch,"_remove")));rows.RemoveAll(r=>removes.Contains(r.Id));
                foreach(var d in J.Items(J.Get(ch,"_add"))){string id=J.S(d,"Id");if(id==""||rows.Any(r=>r.Id==id))continue;var e=new ModEntry{Id=id,Name=J.S(d,"Name"),Path=J.S(d,"Path"),Route=J.S(d,"Route"),External=true};try{e.Present=File.Exists(e.Path);}catch{}rows.Add(e);}
                foreach(var r in rows){if(ch.ContainsKey(r.Id))r.Enabled=J.B(ch,r.Id);if(ch.ContainsKey(r.Id+"_route"))r.Route=J.S(ch,r.Id+"_route");if(ch.ContainsKey(r.Id+"_path"))r.Path=J.S(ch,r.Id+"_path");}
                var order=J.Strings(J.Get(ch,"_order"));
                if(order.Count>0)rows=rows.Select((r,i)=>new{r,i}).OrderBy(x=>{int k=order.IndexOf(x.r.Id);return k<0?order.Count+x.i:k;}).Select(x=>x.r).ToList();
            }
            var extra=extraRows.Select(r=>r.Clone()).ToList();
            var data=ch==null?null:J.Obj(J.Get(ch,"_data"));var dataOrder=ch==null?new List<string>():J.Strings(J.Get(ch,"_dataOrder"));
            if(data!=null)foreach(var r in extra)if(r.IsData&&r.Suffix!=""&&data.ContainsKey(r.Suffix))r.Enabled=J.B(data,r.Suffix);
            var patchOff=ch==null?new List<string>():J.Strings(J.Get(ch,"_patchOff"));var patchOn=ch==null?new List<string>():J.Strings(J.Get(ch,"_patchOn"));
            foreach(var r in extra)if(Parks(r)){string pid=r.Id.Substring(4);if(patchOff.Contains(pid))r.Enabled=false;if(patchOn.Contains(pid))r.Enabled=true;}
            var patchOrder=ch==null?new List<string>():J.Strings(J.Get(ch,"_patchOrder"));
            if(patchOrder.Count>0)extra=extra.Select((r,i)=>new{r,i}).OrderBy(x=>{int k=IsPatchRow(x.r)?patchOrder.IndexOf(x.r.Id.Substring(4)):-1;return k<0?patchOrder.Count+x.i:k;}).Select(x=>x.r).ToList();
            if(dataOrder.Count>0)extra=extra.Select((r,i)=>new{r,i}).OrderBy(x=>{int k=x.r.IsData?dataOrder.IndexOf(x.r.Suffix):-1;return k<0?dataOrder.Count+x.i:k;}).Select(x=>x.r).ToList();
            // a linked card is placed by its data below; left in the DLL queue it would hold back every DLL after it
            gameOrder=OneList.GameOrder(game);IList<string> read=dataOrder.Count>0?dataOrder:gameOrder;var byData=rows.Where(r=>OneList.IsLinked(r)&&read!=null&&read.Contains(r.Suffix)).ToList();
            baseRows=OneList.Weave(rows.Except(byData).ToList(),extra,hints);baseRows.AddRange(byData);
            // one row per id whatever the lists hold: every lookup by id (changes, merge, drawing) needs that, and a doubled one must
            // not stop the window from opening (디시 79077)
            baseRows=baseRows.GroupBy(r=>r.Id).Select(g=>g.First()).ToList();
            savedDllOrder=rows.Select(r=>r.Id).ToList();OneList.PlaceLinked(baseRows,read);
            // patch cards stand in the order they are applied (the backend's), whatever the kept card order says: a 가이드 patch put above
            // another by 자리 찾기 would otherwise show at the bottom, and the next save would move it there. One out of place goes just
            // before the card it is applied before (the others stay where they are).
            var applied=patchOrder.Count>0?new List<string>():PatchOrderNow();
            for(int pass=0;pass<applied.Count*applied.Count;pass++){bool moved=false;
                for(int k=1;k<applied.Count;k++){int a=baseRows.FindIndex(r=>IsPatchRow(r)&&r.Id=="pkg:"+applied[k-1]),b=baseRows.FindIndex(r=>IsPatchRow(r)&&r.Id=="pkg:"+applied[k]);
                    if(a<0||b<0||a<b)continue;var row=baseRows[a];baseRows.RemoveAt(a);baseRows.Insert(b,row);moved=true;}
                if(!moved)break;}
        }
        List<ModEntry> Merge(List<ModEntry> edits,List<ModEntry> fresh) {
            var byId=fresh.ToDictionary(r=>r.Id);var result=new List<ModEntry>();var seen=new HashSet<string>();
            foreach(var w in edits){
                ModEntry b;
                if(byId.TryGetValue(w.Id,out b)){var u=b.Clone();u.Enabled=w.Enabled;u.Route=w.Route;if(!FixedPath.Contains(w.Id))u.Path=w.Path;result.Add(u);seen.Add(w.Id);}
                else if(w.External&&catalog.Find(w.Id)==null){result.Add(w);seen.Add(w.Id);}
            }
            foreach(var b in fresh)if(!seen.Contains(b.Id)&&!unregistered.Contains(b.Id))result.Add(b.Clone());
            return result;
        }
        static bool SamePath(string a,string b){return String.Equals(a??"",b??"",StringComparison.OrdinalIgnoreCase);}
        static bool Differs(ModEntry a,ModEntry b){return a.Enabled!=b.Enabled||!String.Equals(a.Route,b.Route,StringComparison.OrdinalIgnoreCase)||!SamePath(a.Path,b.Path);}
        bool OrderChanged(List<ModEntry> a,List<ModEntry> b){
            var inB=new HashSet<string>(b.Select(r=>r.Id));var inA=new HashSet<string>(a.Select(r=>r.Id));
            return !a.Where(r=>inB.Contains(r.Id)).Select(r=>r.Id).SequenceEqual(b.Where(r=>inA.Contains(r.Id)).Select(r=>r.Id));
        }
        int Changes(List<ModEntry> edits,List<ModEntry> reference){
            var byId=reference.ToDictionary(r=>r.Id);int n=0;
            foreach(var w in edits){ModEntry b;if(!byId.TryGetValue(w.Id,out b)||Differs(w,b))n++;}
            var ids=new HashSet<string>(edits.Select(r=>r.Id));n+=reference.Count(r=>!ids.Contains(r.Id));
            if(OrderChanged(edits,reference))n++;
            return n;
        }
        bool Dirty(){return Changes(working,baseRows)>0||subPending.Count>0;}
        bool FirstSave(){return catalog!=null&&catalog.Complete&&!catalog.Managed&&live.Game==null;}   // never saved here: 저장 puts the entry in (디시 79077: an install that needs it said 'save first' with 저장 off)
        bool EntryLost(){return catalog!=null&&catalog.Complete&&catalog.Managed&&catalog.Entries.Any(e=>catalog.EntryKinds.Any(k=>k.Value=="empty"&&String.Equals(k.Key,e,StringComparison.OrdinalIgnoreCase)));}   // the alert says to press 저장: it must be on with nothing else changed
        ModEntry Row(string id){return id==null?null:working.FirstOrDefault(r=>r.Id==id);}
        // the picked rows in list order (the primary one alone when nothing else is picked)
        List<ModEntry> SelectedRows(){var rows=working.Where(r=>selectedIds.Contains(r.Id)).ToList();if(rows.Count==0){var r=SelectedRow();if(r!=null)rows.Add(r);}return rows;}
        ModEntry SelectedRow(){return Row(selectedId);}

        // ---------- drawing ----------
        // ---------- one card per mod: what one archive installed (its patcher, DLL, data mod) shows as one card ----------
        // The first piece (the patcher, else the DLL) stands for the rest, which show as its lines and switch and move with it.
        // Only the screen groups them: each piece keeps its own load order / game list place and is saved as before.
        Dictionary<string,List<ModEntry>> family=new Dictionary<string,List<ModEntry>>();HashSet<string> riders=new HashSet<string>();
        void Regroup(){
            family.Clear();riders.Clear();
            foreach(var g in working.Select(r=>new{r,o=OwnerOf(r)}).Where(x=>x.o!=null).GroupBy(x=>x.o.Id)){
                var m=g.Select(x=>x.r).ToList();if(m.Count<2)continue;
                var head=m.FirstOrDefault(r=>r.Type=="package")??m.FirstOrDefault(r=>r.IsDll)??m[0];
                family[head.Id]=m.Where(r=>r!=head).ToList();foreach(var r in family[head.Id])riders.Add(r.Id);
            }
        }
        List<ModEntry> Family(ModEntry r){var all=new List<ModEntry>();if(r==null)return all;all.Add(r);List<ModEntry> k;if(family.TryGetValue(r.Id,out k))all.AddRange(k);return all;}
        bool Shown(ModEntry r){return !riders.Contains(r.Id);}
        // a DLL added by the manager or an install: the name its own loh-mod.json gives, else the one it was registered with
        // the name its card shows (loh-mod.json name, 개발판/배포판): the details, update table and patch notes say the same
        string CardName(ModEntry r){var v=r==null?null:active.Concat(inactive).FirstOrDefault(x=>x.Id==r.Id);return v!=null&&!String.IsNullOrEmpty(v.Name)?v.Name:(r==null?"":r.Name);}
        string ShownName(ModEntry r){if(r!=null&&r.IsDll&&r.External){var mi=Manifests.For(r,game);if(mi!=null&&mi.Name!="")return mi.Name;}return r==null?"":r.Name;}
        // the personal edition carries our mods built in (개발판); the same mod installed from its public package is the 배포판
        static readonly HashSet<string> BuiltIn=new HashSet<string>{"dokbak","gift","jade","editor","pad","research"};
        string EditionMark(ModEntry r){
            if(r==null||!r.IsDll||SelfUpdate.Edition()=="public")return "";
            if(BuiltIn.Contains(r.Id))return " (개발판)";
            string file=Path.GetFileName(r.Path??"");
            return r.External&&file!=""&&working.Any(o=>BuiltIn.Contains(o.Id)&&String.Equals(Path.GetFileName(o.Path??""),file,StringComparison.OrdinalIgnoreCase))?" (배포판)":"";
        }
        string CannotSwitchFamily(ModEntry r,bool enable){foreach(var m in Family(r)){if(m.Enabled==enable&&m!=r)continue;string why=CannotSwitch(m,enable);if(why!="")return why;}return "";}
        RowState StateOf(ModEntry r,Dictionary<string,ModEntry> baseById,out bool unsaved,out bool reserved){
            ModEntry b;baseById.TryGetValue(r.Id,out b);ModEntry c=catalog.Find(r.Id);
            unsaved=b==null||Differs(r,b);reserved=pending!=null&&b!=null&&(c==null||Differs(b,c));
            DllVersion v;live.Versions.TryGetValue(r.Path??"",out v);string running;live.RunningVersions.TryGetValue(r.Id,out running);
            var st=r.IsDll?Status.Compute(r,r.Enabled,unsaved,reserved,live.Game,live.Runtime,live.Loaded,running??"",v==null?"":v.Text,DataMods.Listed(live.Selection,r.Suffix)):ExtraStatus(r);
            if(!liveKnown){st.Badge=Badge.Unknown;st.BadgeText="확인 중";}
            return st;
        }
        // the lines of a card: the pieces riding under it, or a DLL mod's own DLL and data (DLL+데이터)
        List<SubRowView> PieceLines(ModEntry r,Dictionary<string,ModEntry> baseById){
            var lines=new List<SubRowView>();List<ModEntry> kids;
            if(family.TryGetValue(r.Id,out kids)){
                foreach(var m in new[]{r}.Concat(kids)){bool u,res;var st=StateOf(m,baseById,out u,out res);DllVersion mv;live.Versions.TryGetValue(m.Path??"",out mv);string ver=InstalledVersion(m,mv);
                    string title=m==r&&m.IsDll&&!String.IsNullOrEmpty(m.Path)?Path.GetFileName(m.Path):m.Name;   // the card carries the archive's name; its own line, the file
                    lines.Add(new SubRowView{Id="piece:"+m.Id,Title=title+(ver!=""?" "+ver:""),Kind=m.Kind,KindFore=Look.Kind(m.Kind),Info=st.Summary!=""?"  ·  "+st.Summary:"",
                        BadgeText=st.BadgeText+(u?" · 저장 전":""),BadgeBrush=Look.Back(st.Badge),BadgeFore=Look.Fore(st.Badge),BadgeTip="같은 압축으로 설치된 조각입니다. 카드를 켜고 끄거나 옮기면 함께 움직입니다.\n"+Look.Tip(st.Badge)});}
            }
            else if(r.IsDll&&r.Data!=""){
                bool u,res;var st=StateOf(r,baseById,out u,out res);
                lines.Add(new SubRowView{Id="piece:dll:"+r.Id,Title=Path.GetFileName(r.Path??""),Kind="DLL",KindFore=Look.Kind("DLL"),Info="  ·  게임이 켜질 때 모드매니저가 불러옴",BadgeText=st.BadgeText,BadgeBrush=Look.Back(st.Badge),BadgeFore=Look.Fore(st.Badge),BadgeTip="이 모드의 프로그램(DLL)입니다.\n"+Look.Tip(st.Badge)});
                // the data half: in MODs or not, and ticked in the game's own mod list or not
                bool? listed=DataMods.Listed(live.Selection,r.Suffix);bool missing=r.DataState=="데이터 없음";
                Badge db=missing?Badge.Error:!r.Enabled?Badge.Off:listed==false?Badge.Waiting:Badge.Planned;
                string dt=missing?"데이터 없음":!r.Enabled?"꺼짐":listed==false?"게임 목록에서 꺼짐":"켜짐";
                lines.Add(new SubRowView{Id="piece:data:"+r.Id,Title="MODs\\"+r.Data,Kind="데이터",KindFore=Look.Kind("데이터"),Info="  ·  게임 모드 목록에 들어가는 데이터",BadgeText=dt,BadgeBrush=Look.Back(db),BadgeFore=Look.Fore(db),
                    BadgeTip="이 모드의 데이터입니다. DLL 과 함께 켜고 끕니다."+(listed==false&&r.Enabled?"\n게임 안 모드 목록에서 꺼져 있습니다 — 저장하면 모드매니저가 켜 둡니다(게임을 한 번 켰다 꺼야 할 수 있음).":"")});
            }
            return lines;
        }
        void Render() {
            Regroup();
            var baseById=baseRows.ToDictionary(r=>r.Id);var a=new List<ModRowView>();var i=new List<ModRowView>();int n=0;
            foreach(var r in working){
                if(!Shown(r))continue;   // a piece of another card: shown as its line
                bool unsaved,reserved;var st=StateOf(r,baseById,out unsaved,out reserved);
                DllVersion v;live.Versions.TryGetValue(r.Path??"",out v);
                unsaved=unsaved||Family(r).Skip(1).Any(m=>{ModEntry mb;return !baseById.TryGetValue(m.Id,out mb)||Differs(m,mb);});   // a piece changed: the card is unsaved too
                var view=new ModRowView{Id=r.Id,Entry=r,State=st};string link;if(!linkCache.TryGetValue(r.Id,out link))linkCache[r.Id]=link=LinkOf(r);
                view.HasLink=link!="";view.LinkTip=link!=""?"게시글 열기 — "+link:"게시글 링크 넣기…";
                view.Name=ShownName(r)+EditionMark(r);view.BadgeText=st.BadgeText;view.BadgeTip=Look.Tip(st.Badge);view.BadgeBrush=Look.Back(st.Badge);view.BadgeFore=Look.Fore(st.Badge);view.VersionText=InstalledVersion(r,v);view.KindText=r.Kind;view.KindTip=KindTip(r);
                view.Summary=st.Quiet&&!unsaved&&!reserved?"":st.Summary;
                view.KindFore=Look.Kind(r.Kind);
                view.ChangeText=unsaved?"저장 전":reserved?"예약됨":"";view.ChangeTip=unsaved?"아직 저장하지 않은 변경입니다. 저장해야 게임에 적용됩니다.":reserved?"게임이 켜져 있어 예약해 두었습니다. 게임을 끄면 적용됩니다.":"";
                RuntimeModule lm;string lastLabel=live.Game==null&&r.IsDll&&r.Enabled&&!unsaved&&!reserved&&lastRun.Modules.TryGetValue(r.Id,out lm)?RuntimeReader.ProblemLabel(lm):"";
                if(lastLabel!=""){lastRun.Modules.TryGetValue(r.Id,out lm);view.ChangeText="지난 실행: "+lastLabel;view.ChangeTip="지난 게임 실행에서 이 모드는 제대로 동작하지 않았습니다: "+RuntimeReader.Problem(lm)+"\n목록에는 켜져 있어도 그 실행에서는 꺼져 있었습니다. 다음 실행에서 다시 시도하며, '로그 열기'나 ⚙ 설정의 '문제 보고서'로 원인을 볼 수 있습니다.";view.Summary=st.Summary+" · 지난 실행: "+RuntimeReader.Problem(lm);}
                string posted;if(lastLabel==""&&!unsaved&&!reserved&&updates.TryGetValue(r.Id,out posted)){
                    DateTime day,at;
                    if(DatedNews(r,posted,out day,out at)){view.ChangeText="새 소식 "+day.ToString("M/d");view.ChangeTip="게시글 제목의 날짜("+day.ToString("M/d")+")가 이 모드를 이 PC에 넣은 날("+at.ToString("M/d")+")보다 뒤입니다. 새 버전일 수 있으니 카드 오른쪽의 게시글 아이콘으로 확인해 보세요.\n(제목에 버전이 없어 날짜로 짐작했습니다)";}
                    else if(!posted.StartsWith("@")&&Posts.Compare(InstalledVersion(r,v),posted)<0){view.ChangeText="새 버전 v"+posted;view.ChangeTip="게시글에 새 버전(v"+posted+")이 올라와 있습니다. 카드 오른쪽의 게시글 아이콘으로 받으세요.";}
                }
                var mine=ActiveConflicts(r);
                if(mine.Count>0){bool hard=mine.Any(x=>x.Hard),order=mine.Any(x=>x.Kind=="patch"&&!PatchInOrder(x));
                    bool loses=mine.Any(x=>x.Kind=="override"&&UpperLoses(x)&&WinnerId(x)!=r.Id),wins=mine.Any(x=>x.Kind=="override"&&WinnerId(x)==r.Id);   // the upper one loses what the lower row puts back
                    bool fix=mine.Any(x=>x.Kind=="override"&&OrderFixes(x));   // the lower one only copies the game's rows: swapping keeps both
                    view.ConflictMild=!hard&&!order&&!loses&&!fix&&mine.All(x=>x.Kind=="override");
                    view.ConflictText=hard?"⚠ 함께 쓸 수 없음":order?"⚠ 순서 확인":fix?"⚠ 겹침 "+mine.Count+" · 순서 바꾸면 해결":loses&&mine.All(x=>x.Kind=="override")?"⚠ 겹침 "+mine.Count+" · 기능 빠질 수 있음":view.ConflictMild?"겹침 "+mine.Count+(wins?" · 이 모드 것이 적용":" · 함께 사용 가능"):"⚠ 겹침 "+mine.Count;
                    view.ConflictTip=TipOf(mine,r.Id);}
                // a menu mod without ConversationCondition.json and no mod after it that has one: its menus do not show (game check 11-03)
                string safeNote;string menuWhy=MenuProblem(r,working,out safeNote);ModFacts facts;dataFacts.TryGetValue(r.Id,out facts);
                string animWhy=menuWhy==null?AnimProblem(r,working):null;
                if(menuWhy!=null){view.ConflictMild=false;view.ConflictText="⚠ 메뉴 안 보임";view.ConflictTip=menuWhy+(String.IsNullOrEmpty(view.ConflictTip)?"":"\n\n"+view.ConflictTip);}
                else if(animWhy!=null){view.ConflictMild=false;view.ConflictText="⚠ 카드 연출 안 나옴";view.ConflictTip=animWhy+(String.IsNullOrEmpty(view.ConflictTip)?"":"\n\n"+view.ConflictTip);}
                else if(facts!=null&&facts.Missing.Count>0&&r.Enabled&&(String.IsNullOrEmpty(view.ConflictText)||view.ConflictMild)){view.ConflictMild=false;view.ConflictText="⚠ 파일 빠짐 의심";view.ConflictTip=String.Join("\n",facts.Missing)+(String.IsNullOrEmpty(view.ConflictTip)?"":"\n\n"+view.ConflictTip);}view.OrderText=r.Enabled?(++n).ToString():"";
                // pictures it shares with another mod: the upper one's shows (game check 11-03) — its own tag only when nothing else is said
                int imageCount;string imageWhy=ImageNote(r,working,out imageCount);
                if(imageWhy!=null){if(String.IsNullOrEmpty(view.ConflictText)){view.ConflictMild=true;view.ConflictText="그림 겹침 "+imageCount;view.ConflictTip=imageWhy;}else view.ConflictTip=(view.ConflictTip??"")+"\n\n"+imageWhy;}
                view.RouteText=r.IsDll&&r.Route!=""&&!r.Route.Equals("version.dll",StringComparison.OrdinalIgnoreCase)?r.Route:"";
                if(r.Type=="workshop"){view.SubsOpen=OpenCards().Contains(r.Id);view.Subs=SubsOf(r);}
                else if(r.Type=="package"){var cap=CapOf(r);if(cap!=null){view.SubsOpen=OpenCards().Contains(r.Id);view.Subs=CapSubs(cap);   // 상한해제: its menus as lines, a clash only on the menu that clashes
                    if(cap.Blocked.Count>0){view.ConflictText="⚠ 함께 쓸 수 없음 "+cap.Blocked.Count;view.ConflictTip=String.Join("\n",cap.Blocked.Values);}}
                    else{var aio=AioOf(r);if(aio!=null){view.SubsOpen=OpenCards().Contains(r.Id);view.Subs=AioSubs(aio);   // 올인원: its items as lines
                        int bad=aio.Items.Count(x=>x.Failed),gave=aio.Items.Count(x=>x.Yielded);if(bad>0){view.ConflictText="⚠ 실패 "+bad;view.ConflictTip="지난 설치에서 들어가지 못한 항목: "+String.Join(", ",aio.Items.Where(x=>x.Failed).Select(x=>x.Name));}
                        else if(gave>0){view.ConflictText=aio.Items.Any(x=>x.Free)?"↻ 다시 쌓으면 들어감 "+gave:"⚠ 자리를 내줌 "+gave;view.ConflictTip=YieldWhy(r);}}}
                    if(view.ConflictText==""||view.ConflictText==null){string why=YieldWhy(r);if(why!=""){view.ConflictText="⚠ 자리를 내줌";view.ConflictTip=why;}}
                    // a patch whose own step failed in the last rebuild is not in the game file, whatever the list says (moved under one it needs, …)
                    var inst=IsPatchRow(r)&&r.Enabled?catalog.Installed.FirstOrDefault(d=>"pkg:"+J.S(d,"Id")==r.Id):null;
                    if(inst!=null&&J.Get(inst,"LastOk")!=null&&!J.B(inst,"LastOk")){view.ConflictText="⚠ 들어가지 않음";view.ConflictTip="지난번 다시 쌓기에서 이 패치가 들어가지 않았습니다. 패치가 남긴 말: "+J.S(inst,"LastTail")+". 목록에서 위로 옮기거나, 같은 자리를 고치는 다른 패치를 확인하세요.";}}
                if(r.Type=="package"&&(view.Subs==null||view.Subs.Count==0)){var ps=PromiseSubs(r);if(ps.Count>0){view.SubsOpen=OpenCards().Contains(r.Id);view.Subs=ps;}}   // a 양식 patch: its items
                var pieces=PieceLines(r,baseById);
                if(family.ContainsKey(r.Id)){var owner=OwnerOf(r);if(owner!=null&&owner.Name!="")view.Name=(ShownName(r)!=r.Name?ShownName(r):owner.Name)+Family(r).Select(EditionMark).FirstOrDefault(x=>x!="");   // the mod's own name when its loh-mod.json gives one   // one archive: its name, and every kind it holds
                    view.KindText=String.Join("+",Family(r).SelectMany(m=>m.Kind.Split('+')).Distinct());view.KindFore=Look.Kind(view.KindText);
                    view.KindTip="한 압축으로 함께 설치된 "+Family(r).Count+"개 조각(DLL·데이터·패치)을 한 카드로 묶었습니다. 켜고 끄기·순서·삭제가 함께 움직입니다. 조각은 카드 안 줄로 보입니다.";}
                if(pieces.Count>0){view.SubsOpen=OpenCards().Contains(r.Id);view.Subs=pieces.Concat(view.Subs??new List<SubRowView>()).ToList();
                    var off=Family(r).Skip(1).Where(m=>r.Enabled&&!m.Enabled).ToList();
                    if(off.Count>0&&(view.ChangeText==null||view.ChangeText=="")){view.ChangeText="일부 꺼짐";view.ChangeTip="이 카드에 딸린 조각 중 꺼진 것: "+String.Join(", ",off.Select(m=>m.Name))+"\n카드를 껐다 켜면 함께 켜집니다.";}}
                (r.Enabled?a:i).Add(view);
            }
            string pad=new string('0',Math.Max(1,n.ToString().Length));foreach(var v in a)v.OrderPad=pad;
            selecting=true;
            try{
                i=i.OrderBy(v=>v.Name??"",StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"),true)).ToList();   // 끔: no load order, so 가나다 order
                Anchored.Sync(activeList,active,a);Anchored.Sync(inactiveList,inactive,i);
                foreach(var pair in new[]{Tuple.Create(activeList,active),Tuple.Create(inactiveList,inactive)}){
                    var want=pair.Item2.Where(x=>selectedIds.Contains(x.Id)).ToList();var have=pair.Item1.SelectedItems.Cast<ModRowView>().ToList();
                    if(want.Count==have.Count&&want.All(have.Contains))continue;
                    pair.Item1.SelectedItems.Clear();foreach(var x in want)pair.Item1.SelectedItems.Add(x);
                }
            }finally{selecting=false;}
            activeCount.Text=active.Count+"개";inactiveCount.Text=inactive.Count+"개";
            activeEmpty.Visibility=active.Count==0?Visibility.Visible:Visibility.Collapsed;inactiveEmpty.Visibility=inactive.Count==0?Visibility.Visible:Visibility.Collapsed;
            RenderBand();RenderAlerts();ShowDetails();
        }
        RowState ExtraStatus(ModEntry r){
            var s=new RowState();bool running=live.Game!=null;
            if(r.Type=="package"){
                if(r.Parked&&!r.Enabled){s.Badge=Badge.Off;s.BadgeText="꺼짐";s.Summary="꺼 둠 — 게임 파일을 원래대로 되돌려 둔 상태입니다. 켜면 다시 설치합니다 · "+r.Info;return s;}
                if(r.Parked){s.Badge=Badge.Waiting;s.BadgeText="켤 예정";s.Summary="저장하면 다시 설치합니다 · "+r.Info;return s;}
                if(!r.Enabled){s.Badge=Badge.Waiting;s.BadgeText="끌 예정";s.Summary="저장하면 덮어쓴 게임 파일을 되돌립니다 · "+r.Info;return s;}
                if(r.Missing>0){s.Badge=Badge.Error;s.BadgeText="파일 빠짐 "+r.Missing;s.Summary="설치 기록의 파일 "+r.Missing+"개가 지금 없습니다 · "+r.Info;}
                else{s.Badge=running?Badge.Running:Badge.Planned;s.BadgeText=running?"실행 중":"켜짐";s.Summary=r.Info;}
                return s;
            }
            if(r.Suffix==""){s.Badge=Badge.Unknown;s.BadgeText="ID 없음";s.Summary="ModInfo.json 에 strIDEnd 가 없어 게임 모드 목록으로 켜고 끌 수 없습니다";return s;}
            if(r.Enabled){s.Badge=running?Badge.Running:Badge.Planned;s.BadgeText=running?"실행 중":"켜짐";}else{s.Badge=Badge.Off;s.BadgeText="꺼짐";}
            if(freshSet.Contains(r.Suffix)){
                s.Badge=Badge.Waiting;s.BadgeText="새 모드";
                s.Summary=DataMods.ListMissing()?"게임을 아직 한 번도 켜지 않아 게임 모드 목록이 없습니다 — 게임을 타이틀 화면까지 한 번 켰다 끄면 모드매니저가 켜 둡니다"
                    :"게임은 새 데이터 모드를 첫 실행 때 꺼 둡니다 — 게임을 한 번 켰다 끄면 모드매니저가 다시 켜 둡니다(바로 쓰려면 게임 안 모드 목록에서 켜기)";
                return s;
            }
            s.Summary=(r.Type=="workshop"?"창작마당":"MODs\\"+Path.GetFileName(r.Path))+" · ID "+r.Suffix+" · 게임 모드 목록에서 "+(r.Enabled?"켜짐":"꺼짐");s.Quiet=true;
            return s;
        }
        static string KindTip(ModEntry r){
            if(r.Type=="workshop")return "창작마당 — Steam 창작마당에서 구독한 데이터 모드입니다. 게임 원본 파일은 그대로 두고 표·그림을 덧씌우며, 같은 걸 바꾸는 모드끼리는 목록 아래쪽 것이 적용됩니다.";
            if(r.Type=="data")return "데이터 모드 — 게임 원본 파일은 그대로 두고, 무장·아이템·이벤트 같은 표와 그림을 게임이 읽을 때 덧씌웁니다. 같은 걸 바꾸는 모드끼리는 목록 아래쪽 것이 적용되니 순서가 중요합니다.";
            if(r.Type=="package")return IsPatchRow(r)?r.Kind+" — 게임 원본 파일(주로 게임 코드 GameAssembly.dll, 일부는 화면·글꼴 파일)을 직접 고쳐 쓰는 모드입니다. 데이터 모드 순서와는 상관없고 패치끼리의 순서만 의미가 있습니다. 꺼짐으로 옮기면 저장할 때 고친 파일을 되돌리고(시간이 걸림), 다시 켜면 다시 고칩니다."
                :r.Kind+" — 모드매니저로 설치한 모드입니다. 꺼짐으로 옮기면 저장할 때 넣었던 파일을 잠시 빼 두고, 다시 켜면 다시 넣습니다.";
            return r.Kind.Contains("데이터")?"DLL+데이터 — 게임과 함께 불러오는 프로그램(DLL)과 덧씌우는 데이터가 같이 든 모드입니다. 데이터 쪽은 목록 순서의 영향을 받습니다."
                :"DLL 모드 — 게임이 켜질 때 모드매니저가 함께 불러오는 작은 프로그램입니다. 게임 원본 파일은 그대로 두고 화면·조작 같은 동작을 바꿉니다. 데이터 모드 순서와는 상관없습니다. 같은 기능을 고치는 DLL 끼리는 부딪힐 수 있고, 보통 목록 아래쪽 DLL 이 이깁니다. 이상하면 순서를 바꾸거나 하나를 꺼 보세요.";
        }
        static string VersionText(ModEntry r,DllVersion v){
            if(r.Combined&&r.Version!="")return r.Version;string t=v!=null&&v.Text!=""?v.Text:r.Version;
            return t==""?"":"v"+t.TrimStart('v','V');
        }
        void RenderBand() {
            var g=live.Game;int changes=Changes(working,baseRows)+subPending.Count;
            if(!liveKnown){gameDot.Fill=Look.Fore(Badge.Unknown);gameText.Text="게임 상태 확인 중";gameSub.Text="";}
            else if(g==null){
                gameDot.Fill=Look.Fore(Badge.Off);gameText.Text="게임 꺼짐";
                gameSub.Text=!catalog.Managed&&catalog.Complete?"모드매니저를 처음 쓰는 게임입니다 — 저장하면 모드를 불러오는 파일(version.dll)을 넣습니다(원래 파일은 백업).":changes>0?"저장해야 게임에 적용됩니다. 다음에 게임을 켤 때 왼쪽 목록을 위에서부터 적용합니다.":"새 모드는 압축 파일을 이 창으로 드래그하면 설치됩니다.";
            } else {
                gameDot.Fill=Look.Fore(Badge.Running);gameText.Text="게임 실행 중";
                string start=g.StartUtc==DateTime.MinValue?"":" · "+g.StartUtc.ToLocalTime().ToString("HH:mm")+" 시작";
                string state=!live.Runtime.Found?" · 모드매니저 연결 확인 안 됨(아직 시작 중이거나 모드매니저 없이 켬)":live.Runtime.Revision==catalog.Revision||catalog.Revision==""?" · 지금 저장된 설정으로 실행 중":" · 이전 설정으로 실행 중 — 다음 실행부터 새 설정";
                gameSub.Text=start.TrimStart(' ','·')+state+" · 게임이 켜져 있을 때 저장하면, 게임을 끌 때 적용됩니다.";gameSub.ToolTip="게임 프로세스 번호(PID) "+g.Pid;
            }
            changeCount.Text=changes>0?"저장하지 않은 변경 "+changes+"개":pending!=null?"예약된 변경 있음 · 게임을 끄면 적용":"";
            loadingText.Text=catalogLoading?"목록 확인 중…":catalogFailed?"목록 확인 실패":"";
            bool running=g!=null;bool first=FirstSave(),lost=EntryLost();saveButton.IsEnabled=(changes>0||first||lost)&&!busy&&catalog.Complete;
            launchButton.IsEnabled=liveKnown&&!running&&!busy;revertButton.IsEnabled=changes>0&&!busy;revertButton.Visibility=changes>0?Visibility.Visible:Visibility.Collapsed;launchButton.Content=running?(object)"게임 실행 중":Glyph.Icon("게임 실행","play",false);
            launchButton.ToolTip=running?"게임이 이미 켜져 있습니다.":"Steam으로 게임을 켭니다 (F9). 저장하지 않은 변경이 있으면 먼저 저장할지 묻습니다.";
            saveButton.ToolTip=changes==0&&first?"모드매니저를 처음 쓰는 게임입니다. 저장하면 모드를 불러오는 파일(version.dll)을 넣습니다(원래 파일은 백업).":changes==0&&lost?"게임 폴더에서 사라진 입구 파일을 다시 넣습니다.":changes==0?"바꾼 내용이 없어 저장할 것이 없습니다.":running?"게임이 켜져 있어 지금은 예약만 해 둡니다. 게임을 끄면 모드매니저가 바로 적용합니다 (Ctrl+S).":"바꾼 내용을 게임에 적용합니다 (Ctrl+S). 적용하기 전 상태는 백업해 둡니다.";
            saveButton.Content=running?"예약 저장 (게임을 끄면 적용)":changes>0?"저장 ("+changes+")":"저장";
            saveButton.Style=(Style)Window.FindResource(changes>0||first||lost?(running?"WarnBtn":"PrimaryBtn"):"Btn");   // accent only when there is something to save
            string match=MatchingProfile();profileButton.Content=Glyph.Text(match==null?"프로필":"프로필: "+match,"down");
        }
        // ---------- 문제 보고서: what was on, overlaps, the game's own log (errors, crash), each mod's log tail, the manager's errors ----------
        // One text to read or paste in a comment; the Windows user folder is replaced by %USERPROFILE%.
        static string GameLogDir{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","FreeWing","ThreeKingdom");}}
        static string CrashDir{get{return Path.Combine(Path.GetTempPath(),"FreeWing","ThreeKingdom","Crashes");}}
        static List<string> Tail(string file,int maxBytes){
            var lines=new List<string>();
            try{using(var fs=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){long start=Math.Max(0,fs.Length-maxBytes);fs.Seek(start,SeekOrigin.Begin);
                using(var r=new StreamReader(fs,new UTF8Encoding(false),true)){if(start>0)r.ReadLine();string l;while((l=r.ReadLine())!=null)lines.Add(l);}}}catch{}
            return lines;
        }
        // the game's log of the last run: lines that say something went wrong (and the crash marker), with the mods' own names
        static List<string> GameLogProblems(string file,IEnumerable<string> modWords,out bool crashed){
            crashed=false;var all=Tail(file,600000);var picked=new List<string>();var words=modWords.Where(w=>w.Length>=4).ToList();
            for(int i=0;i<all.Count;i++){
                string l=all[i];bool crash=l.Contains("Crash!!!");crashed|=crash;
                bool bad=crash||l.Contains("Exception")||l.StartsWith("Error")||l.Contains("NullReference")||l.Contains("MOD读取失败")||words.Any(w=>l.IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0);
                if(!bad)continue;picked.Add(l);if(i+1<all.Count&&all[i+1].TrimStart().StartsWith("at ")){picked.Add(all[i+1]);}
            }
            return picked.Distinct().Skip(Math.Max(0,picked.Distinct().Count()-60)).ToList();
        }
        // the top line shows the game's build and update date; a build that changed since last time gets a notice (the game updates often)
        void CheckGameBuild(){
            var b=GameFolder.Build(game);
            gamePath.Text=(b==null?"":"게임 빌드 "+b.Item1+(b.Item2==DateTime.MinValue?"":" ("+b.Item2.ToString("MM-dd")+" 업데이트)")+"  ·  ")+"경로: "+game;
            gamePath.ToolTip=(b==null?"Steam 기록(appmanifest)을 찾지 못해 게임 빌드를 모릅니다.\n":"Steam 이 기록한 게임 빌드 "+b.Item1+(b.Item2==DateTime.MinValue?"":", 마지막 업데이트 "+b.Item2.ToString("yyyy-MM-dd HH:mm"))+"\n")+"게임 경로: "+game+" — 누르면 탐색기로 엽니다. 바꾸려면 오른쪽 위 '게임 경로 설정…'";
            if(b==null)return;string file=Path.Combine(Store.Folder(game),"GameBuild.txt"),seen="";
            try{if(File.Exists(file))seen=File.ReadAllText(file).Trim();Directory.CreateDirectory(Path.GetDirectoryName(file));if(seen!=b.Item1)File.WriteAllText(file,b.Item1);}catch{}
            if(seen!=""&&seen!=b.Item1&&watchId=="")
                ShowNotice("info","게임이 업데이트됐습니다 (빌드 "+seen+" → "+b.Item1+(b.Item2==DateTime.MinValue?"":", "+b.Item2.ToString("MM-dd HH:mm"))+"). DLL 모드는 새 게임 버전과 안 맞으면 스스로 꺼질 수 있습니다. 게임을 한 번 켠 뒤 카드의 '지난 실행' 표시를 보거나, 업데이트 확인으로 모드 새 버전을 찾아보세요.","업데이트 확인",()=>{HideNotice();CheckUpdates();},null,null);
        }
        void LoadLastRun(){lastRun=RuntimeReader.Last(Path.Combine(game,"LOH_ModManager"));}
        List<ModEntry> LastRunProblems(){RuntimeModule m;return working.Where(r=>r.IsDll&&lastRun.Modules.TryGetValue(r.Id,out m)&&RuntimeReader.ProblemLabel(m)!="").ToList();}
        // when the game closes: did it crash, did a mod switch itself off or fail to load — said once, with the report one click away
        void AfterRunNotice(DateTime started){
            bool crashed=LastRunCrashed(started==DateTime.MinValue?DateTime.UtcNow.AddHours(-12):started);var bad=LastRunProblems();
            if(!crashed&&bad.Count==0)return;
            RuntimeModule m;string names=String.Join(", ",bad.Select(r=>{lastRun.Modules.TryGetValue(r.Id,out m);return r.Name+"("+RuntimeReader.ProblemLabel(m)+")";}));
            string text=(crashed?"게임이 비정상으로 꺼진 것 같습니다(게임 로그에 튕김 기록). ":"")+(bad.Count>0?"방금 실행에서 제대로 동작하지 않은 모드: "+names+". 목록에서는 켜져 있어도 그 실행에서는 꺼져 있었습니다. ":"")+"문제 보고서로 원인을 보거나 제보할 수 있습니다.";
            ShowNotice("warn",text,"문제 보고서",()=>{HideNotice();ShowReport();},null,null);
        }
        bool LastRunCrashed(DateTime since){
            bool crashed;GameLogProblems(Path.Combine(GameLogDir,"Player.log"),new string[0],out crashed);if(crashed)return true;
            try{return Directory.Exists(CrashDir)&&new DirectoryInfo(CrashDir).GetDirectories().Any(d=>d.CreationTimeUtc>=since.AddMinutes(-1));}catch{return false;}
        }
        string ProblemReport(){
            var o=new List<string>{"[모드매니저 문제 보고서] v"+Version+" · "+DateTime.Now.ToString("yyyy-MM-dd HH:mm")};
            try{string acf=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(game.TrimEnd('\\'))),"appmanifest_3020510.acf");if(File.Exists(acf)){var m=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(acf),"\"buildid\"\\s+\"(\\d+)\"");if(m.Success)o[0]+=" · 게임 빌드 "+m.Groups[1].Value;}}catch{}
            o.Add("");o.Add("■ 켜 둔 모드 (적용 순서)");int n=0;
            foreach(var v in active){DllVersion dv;live.Versions.TryGetValue(v.Entry==null?"":v.Entry.Path??"",out dv);o.Add("  "+(++n)+". "+v.Name+" "+v.VersionText+" ["+v.KindText+"] "+v.BadgeText+(v.State!=null&&v.State.Summary!=""&&!v.State.Quiet?" — "+v.State.Summary:""));}
            var run=live.Game!=null?live.Runtime:lastRun;
            if(run.Found){o.Add("");o.Add("■ "+(live.Game!=null?"지금 실행":"지난 실행")+"에서 모드매니저 입구가 받은 모드 상태 ("+run.LastWriteUtc.ToLocalTime().ToString("MM-dd HH:mm")+")");
                foreach(var p in run.Modules){var row=Row(p.Key);string bad=RuntimeReader.Problem(p.Value);o.Add("  "+(row==null?p.Key:row.Name)+": "+(bad!=""?"⚠ "+bad:Status.StateText(p.Value.State,p.Value.Error)+((p.Value.State==3||p.Value.State==4)?" · "+Status.DetailText(p.Value.Detail):"")));}}
            var on=new HashSet<string>(working.Where(x=>x.Enabled).Select(x=>x.Id));var clash=conflicts.Where(c=>on.Contains(c.A)&&on.Contains(c.B)).ToList();
            if(clash.Count>0){o.Add("");o.Add("■ 모드매니저가 찾은 겹침");foreach(var c in clash)o.Add("  "+NameOf(c.A)+" ↔ "+NameOf(c.B)+": "+(c.Hard?"함께 쓸 수 없음 — ":"")+c.Detail);}
            var words=working.Where(x=>x.Enabled&&x.IsDll&&!String.IsNullOrEmpty(x.Path)).Select(x=>Path.GetFileNameWithoutExtension(x.Path)).ToList();
            foreach(var name in new[]{"Player.log","Player-prev.log"}){
                string file=Path.Combine(GameLogDir,name);if(!File.Exists(file))continue;bool crashed;var lines=GameLogProblems(file,words,out crashed);
                o.Add("");o.Add("■ 게임 로그 "+name+" ("+File.GetLastWriteTime(file).ToString("MM-dd HH:mm")+(crashed?", 비정상 종료 기록 있음":"")+") — 오류·모드 관련 줄 "+lines.Count+"개");
                foreach(var l in lines)o.Add("  "+l);
            }
            try{if(Directory.Exists(CrashDir)){var last=new DirectoryInfo(CrashDir).GetDirectories().OrderByDescending(d=>d.CreationTime).Take(3).ToList();if(last.Count>0){o.Add("");o.Add("■ 게임 튕김 기록 (최근 "+last.Count+"개)");foreach(var d in last)o.Add("  "+d.CreationTime.ToString("MM-dd HH:mm")+"  "+d.Name);}}}catch{}
            foreach(var r in working.Where(x=>x.Enabled&&x.IsDll&&x.Log!=""&&!String.IsNullOrEmpty(x.Path))){
                string dir=Path.GetDirectoryName(r.Path);string log=dir==null?"":Path.Combine(dir,r.Log);if(!File.Exists(log))continue;
                var lines=Tail(log,20000);o.Add("");o.Add("■ "+r.Name+" 로그 ("+File.GetLastWriteTime(log).ToString("MM-dd HH:mm")+") 끝 20줄");foreach(var l in lines.Skip(Math.Max(0,lines.Count-20)))o.Add("  "+l);
            }
            string errors=Path.Combine(Store.Root,"Errors.log");
            if(File.Exists(errors)){var lines=Tail(errors,8000);o.Add("");o.Add("■ 모드매니저 오류 기록 끝 15줄");foreach(var l in lines.Skip(Math.Max(0,lines.Count-15)))o.Add("  "+l);}
            string text=String.Join(Environment.NewLine,o);string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if(home.Length>3)text=text.Replace(home,"%USERPROFILE%");
            return text;
        }
        // 버그 제보: a comment on the manager's post at DC (no files there, 400 characters a comment) — a short summary to paste
        void BugReport(){
            string post=Posts.Known1("manager").Url;string summary=ShortReport(300);
            int pick=Dialogs.Buttons3(Window,"버그 제보",
                "버그 제보는 디시인사이드에 올린 모드매니저 원글('입지전)모드매니저 v…', 다운로드 링크가 있는 글)의 댓글로 받습니다.\n"
                +"디시는 파일을 붙일 수 없고 댓글이 400자까지라, '요약 복사'로 아래 짧은 요약을 복사해 댓글에 붙이고 무슨 일이 있었는지 한두 줄 적어 주세요.\n\n"
                +summary+"\n\n"
                +"· 설치가 안 되는 모드는 압축 파일 이름과 받은 곳(게시글 주소)을 적어 주세요.\n"
                +"· 다른 분이 만든 모드 자체의 문제는 그 모드의 게시글에 알려 주세요.",
                "요약 복사하고 게시글 열기","자세한 보고서 보기");
            if(pick==1){try{Clipboard.SetText(summary);}catch{}OpenLink(post);ShowNotice("ok","요약을 복사했습니다. 게시글 댓글 칸에 붙여 넣고(Ctrl+V) 무슨 일이 있었는지 한두 줄 덧붙여 주세요.",null,null,null,null);}
            else if(pick==2)ShowReport();
        }
        // the report cut to fit a DC comment with room for the player's own words: version, build, what went wrong, then the mods on
        string ShortReport(int limit){
            string build="";var b=GameFolder.Build(game);if(b!=null)build=b.Item1;
            Func<string,int,string> cut=(t,n)=>{t=(t??"").Trim();return t.Length<=n?t:t.Substring(0,n-1)+"…";};
            var lines=new List<string>{"[모드매니저 v"+Version+(build!=""?" · 게임 "+build:"")+"]"};
            var words=working.Where(x=>x.Enabled&&x.IsDll&&!String.IsNullOrEmpty(x.Path)).Select(x=>Path.GetFileNameWithoutExtension(x.Path)).ToList();
            bool crashed=false;var log=new List<string>();string player=Path.Combine(GameLogDir,"Player.log");
            if(File.Exists(player))log=GameLogProblems(player,words,out crashed);
            RuntimeModule m;var bad=LastRunProblems().Select(r=>{lastRun.Modules.TryGetValue(r.Id,out m);return r.Name+"("+RuntimeReader.ProblemLabel(m)+")";}).ToList();
            if(crashed)lines.Add("지난 실행: 튕김 기록 있음");
            if(bad.Count>0)lines.Add(cut("안 된 모드: "+String.Join(", ",bad),90));
            var err=log.FirstOrDefault(l=>l.Contains("Exception")||l.StartsWith("Error")||l.Contains("MOD读取失败"));if(err!=null)lines.Add(cut("오류: "+err,80));
            string text=String.Join("\n",lines);
            var mods=active.Select(v=>cut(v.Name,14)+(v.VersionText!=""?" "+v.VersionText:"")).ToList();
            string list="켜짐 "+mods.Count+"개: ";int shown=0;
            foreach(var x in mods){string next=list+(shown>0?", ":"")+x;if(text.Length+1+next.Length+8>limit)break;list=next;shown++;}
            if(shown<mods.Count)list+=(shown>0?" ":"")+"외 "+(mods.Count-shown)+"개";
            text+="\n"+list;string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);if(home.Length>3)text=text.Replace(home,"%USERPROFILE%");
            return text;
        }
        void ShowReport(){
            if(busy)return;string text="";
            RunBusy("문제 보고서를 만드는 중… (게임 로그와 모드 로그를 읽습니다)",()=>{return "";},x=>{text=ProblemReport();Dialogs.Report(Window,text,GameLogDir);},null);
        }

        // ---------- ⚙ settings: scale, theme, painting strength, window place, daily update check ----------
        void ApplySkin(){
            if(Skin.Shown)Window.Background=Skin.Back;else Window.SetResourceReference(Window.BackgroundProperty,"Bg");
            foreach(var n in new[]{"SkinHead","SkinEdge","SkinFoot"})F<Border>(n).Visibility=View.Skin?Visibility.Visible:Visibility.Collapsed;
            FitSkinHead();Theme.TitleBar(Window);
        }
        // the dark band covers the title row (window margin 16 + the row + half its gap); the meander sits on its lower edge
        void FitSkinHead(){
            var row=F<DockPanel>("HeaderRow");double h=(16+row.ActualHeight+7)*View.Scale;
            F<Border>("SkinHead").Height=h;F<Border>("SkinEdge").Margin=new Thickness(0,h,0,0);
        }
        void ApplyScale(){var root=Window.Content as FrameworkElement;if(root!=null)root.LayoutTransform=View.Scale==1.0?Transform.Identity:new ScaleTransform(View.Scale,View.Scale);FitDetail();}
        // narrow window (or a large scale): the fixed button block takes less of the description's room
        void FitDetail(){var b=Window.FindName("DetailButtons") as FrameworkElement;if(b==null)return;double w=Window.ActualWidth/View.Scale;b.Width=w>0&&w<1180?390:480;}
        void PlaceWindow(){
            if(!View.RememberWindow||Double.IsNaN(View.Width)||Double.IsNaN(View.Height)){var a=SystemParameters.WorkArea;Window.Width=Math.Max(Window.MinWidth,Math.Min(Window.Width,a.Width-20));Window.Height=Math.Max(Window.MinHeight,Math.Min(Window.Height,a.Height-20));return;}
            var screen=new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight);
            double w=Math.Max(Window.MinWidth,Math.Min(View.Width,screen.Width)),h=Math.Max(Window.MinHeight,Math.Min(View.Height,screen.Height));
            if(!Double.IsNaN(View.Left)&&!Double.IsNaN(View.Top)&&screen.IntersectsWith(new Rect(View.Left+40,View.Top+10,Math.Max(1,w-80),40))){Window.WindowStartupLocation=WindowStartupLocation.Manual;Window.Left=View.Left;Window.Top=View.Top;}
            Window.Width=w;Window.Height=h;if(View.Maximized)Window.WindowState=WindowState.Maximized;
        }
        void RememberPlace(){
            if(!View.RememberWindow)return;var r=Window.WindowState==WindowState.Normal?new Rect(Window.Left,Window.Top,Window.Width,Window.Height):Window.RestoreBounds;
            if(!r.IsEmpty){View.Left=r.Left;View.Top=r.Top;View.Width=r.Width;View.Height=r.Height;}View.Maximized=Window.WindowState==WindowState.Maximized;View.Save();
        }
        void OpenSettings(){
            Dialogs.Settings(Window,
                scale=>{View.ScaleAuto=scale<=0;View.Scale=View.ScaleAuto?View.AutoScale():scale;View.Save();ApplyScale();},
                mode=>{Theme.Mode=mode;Theme.Save();ApplyTheme();},
                palette=>{View.Palette=palette;View.Save();ApplyTheme();ApplySkin();},
                strength=>{View.InkStrength=strength;View.Save();Skin.Repaint();ApplySkin();},
                file=>{string why=View.SetPicture(file);if(why!="")return why;ApplyTheme();return "";},
                ()=>{try{Directory.CreateDirectory(Store.Root);Process.Start("explorer.exe","\""+Store.Root+"\"");}catch{}},
                ShowReport);
        }

        // ---------- theme and screen ----------
        void ApplyTheme(){
            Look.Use(Theme.WantLight,Window.Resources);Theme.TitleBar(Window);Skin.Repaint();ApplySkin();   // a picture's wash takes the theme's colour
            Render();RenderBand();RenderAlerts();   // brushes handed to rows and alerts come from the palette in use
            if(packagesLoaded&&packagesView.Visibility==Visibility.Visible)LoadPackages();
        }
        // never opens larger than the screen (1366×768 laptops, 1080p at 150 %); the minimum shrinks with it
        void FitScreen(){
            var area=SystemParameters.WorkArea;
            Window.Width=Math.Min(Window.Width,Math.Max(760,area.Width-32));Window.Height=Math.Min(Window.Height,Math.Max(520,area.Height-32));
            Window.MinWidth=Math.Min(Window.MinWidth,Window.Width);Window.MinHeight=Math.Min(Window.MinHeight,Window.Height);
        }
        // pixel-snapped text at 100–125 %, smooth outlines from 150 % up (4K monitors, high-DPI laptops); follows the monitor the window is on
        void TextMode(){try{double scale=VisualTreeHelper.GetDpi(Window).DpiScaleX;TextOptions.SetTextFormattingMode(Window,scale>=1.5?TextFormattingMode.Ideal:TextFormattingMode.Display);}catch{}}
        string alertErrors="";   // a new [오류] opens the collapsed list once (a lost entry stopped every DLL mod behind '알림 1개'); closing it stays closed
        void RenderAlerts() {
            var list=new List<AlertView>();
            foreach(var issue in catalog.Issues){if(issue.Level=="대기"&&live.Game==null)continue;list.Add(new AlertView{Text="["+issue.Level+"] "+issue.Text,Brush=issue.Level=="오류"?Look.Bad:issue.Level=="대기"?Look.Sub:Look.Warn});}
            if(live.Game!=null&&live.Runtime.ConfigError)list.Add(new AlertView{Text="[오류] 게임이 모드매니저 설정 파일(Modules.ini)을 읽다가 오류가 났습니다. 저장을 한 번 더 눌러 주세요.",Brush=Look.Bad});
            // a patcher part left out because another patcher already uses the same place (상한해제 4번 vs 올인원)
            if(catalog.PatchState!=null)foreach(var why in J.Strings(J.Get(catalog.PatchState,"Blocked")))list.Add(new AlertView{Text="[함께 쓸 수 없음] "+why,Brush=Look.Warn});
            // the game DLL has room for only so many appended code sections; one more appending patch may not fit
            if(catalog.PatchState!=null&&J.Get(catalog.PatchState,"SectionRoom")!=null){int room=Int(J.Get(catalog.PatchState,"SectionRoom"));if(room>=0&&room<=2)list.Add(new AlertView{Text="[구역 자리] GameAssembly.dll 에 패치 구역을 더 붙일 자리가 "+room+"칸 남았습니다. 구역을 붙이는 패치를 더 넣으면 들어가지 않을 수 있습니다.",Brush=Look.Warn});}
            foreach(var t in catalog.TakenLoaders){string loader=J.S(t,"Loader"),original=J.S(t,"Original");
                list.Add(new AlertView{Text="[로더 입구] '"+J.S(t,"Name")+"'은 "+loader+" 로더가 게임을 켤 때 원래 이름("+original+")으로 먼저 실행돼야 하는 파일이라, 모드매니저가 불러오면 "+loader+" 가 돌지 않을 수 있습니다. 그 줄을 고르고 '원래 방식으로 되돌리기'를 누르세요.",Brush=Look.Warn});}
            foreach(var f in catalog.Foreign)list.Add(new AlertView{Text="[모드매니저 밖 DLL] "+ForeignText(f)+" 그대로 둬도 됩니다. 목록에서 켜고 끄려면 이 DLL 파일을 모드매니저 창에 끌어다 놓으세요.",Brush=Look.Sub});
            alerts.Clear();foreach(var x in list)alerts.Add(x);
            string errors=String.Join("\n",list.Where(a=>a.Text.StartsWith("[오류]")).Select(a=>a.Text));if(errors!=alertErrors){alertErrors=errors;if(errors!="")alertsToggle.IsChecked=true;}
            alertsToggle.Visibility=list.Count>0?Visibility.Visible:Visibility.Collapsed;alertsToggle.Content=Glyph.Text("알림 "+list.Count+"개",alertsToggle.IsChecked==true?"up":"down");
            alertList.Visibility=list.Count>0&&alertsToggle.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
        }
        // the buttons between the lists only light up when they would do something for the picked cards
        // (위로·아래로 move cards of the 켬 list; the 끔 list has no order)
        Button enableButton,disableButton,upButton,downButton;
        void RailState(){
            var picked=SelectedRows();bool on=picked.Any(x=>x.Enabled),off=picked.Any(x=>!x.Enabled);
            enableButton.IsEnabled=off&&!busy;disableButton.IsEnabled=on&&!busy;upButton.IsEnabled=downButton.IsEnabled=on&&!busy;
        }
        void ShowDetails() {
            RailState();
            var r=SelectedRow();var view=r==null?null:active.Concat(inactive).FirstOrDefault(x=>x.Id==r.Id);
            foreach(var b in new Control[]{openFolder,openLog,locateButton,unregisterButton,removeButton,advancedToggle})b.IsEnabled=r!=null;
            if(r==null)foreach(var b in new Control[]{openFolder,openLog,removeButton,advancedToggle})b.ToolTip="먼저 목록에서 모드를 고르세요.";
            else{openFolder.ToolTip="이 모드가 들어 있는 폴더를 탐색기로 엽니다.";removeButton.ToolTip="고른 모드를 삭제합니다. 지우기 전에 백업해 두므로 '설치 정보' 탭의 '지운 모드 되살리기'로 되돌릴 수 있습니다. 카드를 이 칸으로 드래그해도 됩니다.";advancedToggle.ToolTip="불러오는 방식, DLL 파일 위치처럼 평소에는 바꿀 일이 없는 설정입니다.";}
            if(r==null||view==null){settingsButton.IsEnabled=false;settingsButton.ToolTip="먼저 목록에서 모드를 고르세요.";entryButton.Visibility=offOthersButton.Visibility=Visibility.Collapsed;detailTitle.Text="모드를 고르면 자세한 내용이 여기에 나옵니다.";detailStatus.Text="";detailInfo.Text="두 번 클릭하거나 드래그해서 켜고 끄며, 저장을 눌러야 게임에 적용됩니다.";detailTech.Text="";FillSubPanel(null);return;}
            var many=SelectedRows();
            if(many.Count>1){
                detailTitle.Text="모드 "+many.Count+"개 선택";detailStatus.Text=String.Join(", ",many.Select(x=>x.Name));
                int on=many.Count(x=>x.Enabled);
                detailInfo.Text=(on>0?"켜짐 "+on+"개":"")+(on>0&&on<many.Count?" · ":"")+(on<many.Count?"꺼짐 "+(many.Count-on)+"개":"")+"\n켜기·끄기·위로·아래로·삭제는 고른 것 모두에 한 번에 적용됩니다. 고른 카드 하나를 잡고 끌면 함께 움직입니다.";
                detailTech.Text="Ctrl+클릭으로 하나씩 더하거나 빼고, Shift+클릭으로 범위를, Ctrl+A 로 그 목록 전체를 고릅니다. Delete 키로도 삭제합니다.";
                foreach(var b in new Control[]{openFolder,openLog,locateButton,unregisterButton,advancedToggle,settingsButton})b.IsEnabled=false;
                foreach(var b in new Control[]{openFolder,openLog,advancedToggle,settingsButton})b.ToolTip="여러 개를 고른 상태에서는 쓸 수 없습니다. 모드 하나만 고르세요.";
                removeButton.IsEnabled=!busy;removeButton.ToolTip="고른 "+many.Count+"개를 한 번에 삭제합니다(한 번만 확인). 창작마당 구독 모드는 빼고 알려 줍니다.";
                entryButton.Visibility=offOthersButton.Visibility=Visibility.Collapsed;routePanel.Visibility=Visibility.Collapsed;FillSubPanel(null);
                return;
            }
            if(!r.IsDll){
                detailTitle.Text=CardName(r)+"  ·  "+view.BadgeText;detailStatus.Text=view.State.Summary;
                var lines=new List<string>{"종류: "+r.Kind,"출처: "+r.Source};
                string madeFor=MadeFor(r);if(madeFor!="")lines.Add(madeFor);
                if(r.IsData)lines.Add("폴더: "+r.Path+(r.Suffix!=""?" · ID(strIDEnd) "+r.Suffix:""));else lines.Add("원본: "+r.Path);
                var twins=working.Where(o=>o!=r&&o.IsData&&r.IsData&&o.Suffix!=""&&o.Suffix==r.Suffix).Select(o=>o.Name+" ("+o.Kind+")").ToList();
                if(twins.Count>0)lines.Add("⚠ 같은 ID 를 쓰는 모드: "+String.Join(", ",twins)+" — 게임 목록에서 함께 켜지고 꺼집니다. 하나만 남기세요.");
                if(r.NotList)lines.Add("⚠ 게임이 이 모드의 ModInfo.json 을 읽지 못합니다(맨 바깥이 [ ] 목록이 아니라 { } 하나). 게임 모드 목록에 나오지 않아, 켜 두어도 게임이 목록을 다시 쓸 때 빠져 꺼짐으로 돌아갑니다."
                    +(r.Source.StartsWith("모드매니저 설치")?" 저장하면 모드매니저가 [ ] 로 감싸 고칩니다.":" 파일 맨 앞에 [ , 맨 뒤에 ] 를 붙이면 게임이 읽습니다."));
                detailInfo.Text=String.Join("\n",lines);
                detailTech.Text=r.IsData?"켜기·끄기·순서는 저장할 때 게임 모드 목록(ModSettings)에 씁니다(게임을 끈 상태, 원래 목록은 그 저장의 백업에)."
                    :"게임 파일을 직접 바꾸는 모드(파일 교체·패치 등)는 여기서 끌 수 없습니다. 빼려면 '설치 정보' 탭에서 제거하세요.";
                foreach(var b in new Control[]{openLog,locateButton,unregisterButton})b.IsEnabled=false;routePanel.Visibility=Visibility.Collapsed;
                openFolder.IsEnabled=r.IsData&&Directory.Exists(r.Path);removeButton.IsEnabled=true;ShowEntryButton(r);ShowConflicts(r);ShowPostAndSettings(r);FillSubPanel(r);return;
            }
            FillSubPanel(null);detailTitle.Text=CardName(r)+"  ·  "+view.BadgeText;detailStatus.Text=view.State.Summary;
            DllVersion v;live.Versions.TryGetValue(r.Path??"",out v);string running;live.RunningVersions.TryGetValue(r.Id,out running);
            var info=new List<string>();
            string ver=v!=null&&v.Text!=""?"v"+v.Text.TrimStart('v','V')+" ("+v.Source+")":r.Version!=""?"v"+r.Version+" (등록 기록)":"버전 정보 없음";
            if(v!=null&&v.Text!=""&&r.Version!=""&&r.Version!=v.Text&&!r.Combined)ver+=" · 등록 기록은 "+r.Version;
            if(!String.IsNullOrEmpty(running))ver+=" · 지금 게임에 올라간 버전 "+running;
            info.Add("버전: "+ver);
            string made=MadeFor(r);if(made!="")info.Add(made);
            info.Add("종류: "+r.Kind+(r.Data!=""?" · 데이터 MODs\\"+r.Data+" — "+(r.DataState==""?"확인 전":r.DataState):""));
            info.Add("경로: "+r.Path+(r.Present||r.Path==""?"":"  (파일 없음)"));
            detailInfo.Text=String.Join("\n",info);
            var tech=new List<string>{"관리 이름 "+r.Id+" · 불러오는 방식 "+(r.Route==""?"version.dll":r.Route)};
            RuntimeModule m;if(live.Game==null&&lastRun.Modules.TryGetValue(r.Id,out m))tech.Add("지난 실행: "+m.Entry+" · "+Status.StateText(m.State,m.Error)+((m.State==3||m.State==4)?" · "+Status.DetailText(m.Detail):""));
            if(live.Game!=null&&live.Runtime.Modules.TryGetValue(r.Id,out m))tech.Add("이번 실행: "+m.Entry+" · State "+m.State+" ("+Status.StateText(m.State,m.Error)+") · 응답 "+m.Detail+" ("+Status.DetailText(m.Detail)+")");
            if(v!=null&&v.CanPause)tech.Add("이 모드는 게임 중 일시중지를 지원한다고 밝힘(LOH_ModManagerPause/Resume)");
            detailTech.Text=String.Join("\n",tech);ShowConflicts(r);
            selecting=true;try{foreach(RadioButton chip in routeChips.Children)chip.IsChecked=String.Equals((string)chip.Tag,r.Route==""?"version.dll":r.Route,StringComparison.OrdinalIgnoreCase);}finally{selecting=false;}
            locateButton.IsEnabled=!FixedPath.Contains(r.Id);unregisterButton.IsEnabled=r.External;removeButton.IsEnabled=true;ShowEntryButton(r);
            routePanel.Visibility=Visibility.Visible;ShowPostAndSettings(r);
            openLog.IsEnabled=r.Log!="";
            openLog.ToolTip=r.Log!=""?"이 모드가 남긴 기록(로그)을 메모장으로 엽니다. 모드가 제대로 안 될 때 확인하세요.":"이 모드는 로그 파일을 남기지 않습니다.";
            unregisterButton.ToolTip=r.External?"'DLL 파일 추가'로 넣은 모드를 목록에서 뺍니다. DLL 파일 자체는 지우지 않습니다. 저장해야 적용됩니다.":"'DLL 파일 추가'로 넣은 모드만 목록에서 뺄 수 있습니다. 이 모드는 '삭제…'를 쓰세요.";
        }

        // ---------- posts, settings files, update check ----------
        readonly Dictionary<string,string> linkCache=new Dictionary<string,string>();   // the cards ask on every redraw; emptied when the catalog is read again or a link is edited
        string LinkOf(ModEntry r){string from;return LinkOf(r,out from);}
        string LinkOf(ModEntry r,out string from){
            from="";if(r==null)return "";string u;
            if(Posts.Saved().TryGetValue(r.Id,out u)){from="직접 넣음";return u;}   // the player's choice wins; empty = no link
            var m=Manifests.For(r,game);if(m!=null&&Posts.IsWeb(m.Page)){from="모드의 "+Manifests.FileName;return m.Page;}
            u=Manifests.UrlIn(r,game);if(u!=""){from="모드 폴더의 바로가기(.url)";return u;}
            var k=Posts.Known1(r.Id);if(k!=null){from="모드매니저 기본";return k.Url;}
            if(r.Type=="workshop"){string id=Path.GetFileName((r.Path??"").TrimEnd('\\'));if(id!=""&&id.All(Char.IsDigit)){from="창작마당";return "steam://url/CommunityFilePage/"+id;}}
            return "";
        }
        // the installed version: the DLL's own, else what the mod info file says
        string InstalledVersion(ModEntry r,DllVersion v){string t=VersionText(r,v);if(t!="")return t;var m=Manifests.For(r,game);return m!=null&&m.Version!=""?"v"+m.Version.TrimStart('v','V'):"";}
        void OpenLink(string link){try{Process.Start(link);}catch(Exception e){MessageBox.Show(Window,e.Message,"링크를 열지 못했습니다");}}
        void EditLink(ModEntry r){
            if(r==null)return;
            string url=Dialogs.Ask(Window,"게시글 링크 — "+r.Name,"이 모드의 게시글(디시 글 등) 주소를 붙여 넣으세요. 비우고 확인하면 링크를 지웁니다.\n업데이트 확인은 게시글 제목의 버전을 읽습니다(모드 하나면 v1.2.3, 여러 모드면 '모드이름 1.2.3').",LinkOf(r),false);
            if(url==null)return;url=url.Trim();
            if(url!=""&&!Posts.IsWeb(url)){MessageBox.Show(Window,"http:// 또는 https:// 로 시작하는 주소를 넣어 주세요.","게시글 링크");return;}
            Posts.Save(r.Id,url);updates.Remove(r.Id);linkCache.Clear();Render();
        }
        void ShowPostAndSettings(ModEntry r){
            string link=LinkOf(r);
            string from;LinkOf(r,out from);postLinkText.Inlines.Clear();postLinkText.Inlines.Add(new System.Windows.Documents.Run("게시글 링크: "));postLinkText.ToolTip=null;
            if(link=="")postLinkText.Inlines.Add(new System.Windows.Documents.Run("없음"));
            else{   // the address itself opens the post (asked in 디시, 11-03)
                var open=new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(link)){ToolTip="누르면 게시글이 열립니다."};string target=link;open.Click+=(s,e)=>OpenLink(target);
                postLinkText.Inlines.Add(open);postLinkText.Inlines.Add(new System.Windows.Documents.Run("  ("+from+")"));}
            var files=SettingsFiles(r);settingsButton.IsEnabled=files.Count>0;
            settingsButton.ToolTip=files.Count==0?"이 모드에는 설정 파일(ini)이 없습니다.":"이 모드의 설정 파일("+String.Join(", ",files.Select(Path.GetFileName))+")을 메모장으로 엽니다. 고쳐서 저장한 뒤 게임을 다시 켜면 적용됩니다.";
            string posted;
            DateTime day,at;
            if(updates.TryGetValue(r.Id,out posted)&&posted.StartsWith("@")){
                detailInfo.Text+="\n게시글 제목의 날짜: "+posted.Substring(1)+(DatedNews(r,posted,out day,out at)?" — 이 모드를 넣은 날("+at.ToString("yyyy-MM-dd")+")보다 뒤라 새 버전일 수 있습니다":" — 넣은 뒤로 바뀐 소식 없음");
            }
            else if(updates.TryGetValue(r.Id,out posted)){
                DllVersion v;live.Versions.TryGetValue(r.Path??"",out v);string now=InstalledVersion(r,v);
                detailInfo.Text+="\n게시글의 버전: "+(posted==""?"제목에서 못 찾음":"v"+posted+(now!=""&&Posts.Compare(now,posted)<0?" — 새 버전이 있습니다":now!=""&&Posts.Compare(now,posted)>0?" — 지금 쓰는 버전이 더 최신":" — 최신"));
            }
        }
        // Which game the mod was made for, for reference only (many mods keep working after a game update): the mod info file's
        // "game" when it gives one, else the date of its own files (kept from the author's archive) against Steam's last game update.
        const string MadeNote=" — 참고용: 업데이트 안 한 모드도 대부분 잘 됩니다. 이상하면 게시글에서 새 버전을 확인해 보세요";
        string MadeFor(ModEntry r){
            var b=GameFolder.Build(game);var info=Manifests.For(r,game);
            if(info!=null&&info.Game!=""){
                bool build=info.Game.All(char.IsDigit);
                if(b!=null&&build&&info.Game==b.Item1)return "만든 게임 버전: 지금 게임(빌드 "+b.Item1+")에 맞춤 · 모드 정보 파일";
                return "만든 게임 버전: "+(build?"빌드 ":"")+info.Game+" · 모드 정보 파일"+(b!=null&&build?" (지금 빌드 "+b.Item1+")"+MadeNote:"");
            }
            if(r.Type=="workshop"||b==null||b.Item2==DateTime.MinValue)return "";   // Steam keeps workshop files current itself
            DateTime when=DateTime.MinValue;
            try{
                string p=r.Path??"";var files=File.Exists(p)?new[]{new FileInfo(p)}:Directory.Exists(p)?new DirectoryInfo(p).GetFiles().Where(f=>!f.Extension.Equals(".ini",StringComparison.OrdinalIgnoreCase)&&!f.Extension.Equals(".log",StringComparison.OrdinalIgnoreCase)).ToArray():new FileInfo[0];
                foreach(var f in files)if(f.LastWriteTime>when)when=f.LastWriteTime;   // settings and logs change on this PC: not the author's date
            }catch{}
            if(when==DateTime.MinValue)return "";
            return when<b.Item2?"만든 때: 파일 날짜 "+when.ToString("yyyy-MM-dd")+", 게임 마지막 업데이트("+b.Item2.ToString("yyyy-MM-dd")+") 전"+MadeNote
                :"만든 때: 파일 날짜 "+when.ToString("yyyy-MM-dd")+", 게임 마지막 업데이트("+b.Item2.ToString("yyyy-MM-dd")+") 뒤";
        }
        static readonly string[] NotSettings={"Modules.ini","desktop.ini"};
        // *.ini beside the mod (its DLL's folder, or its MODs folder); in the game folder itself only files named after the DLL; "설정/setting/config" first
        List<string> SettingsFiles(ModEntry r){
            var list=new List<string>();
            try{
                var info=Manifests.For(r,game);
                if(info!=null&&info.Settings.Count>0){string home=Path.GetFullPath(Path.GetDirectoryName(info.File)).TrimEnd('\\')+"\\";
                    foreach(var n in info.Settings){string f=Path.GetFullPath(Path.Combine(home,n));string ext=Path.GetExtension(f).ToLowerInvariant();
                        if(f.StartsWith(home,StringComparison.OrdinalIgnoreCase)&&(ext==".ini"||ext==".cfg"||ext==".txt"||ext==".json"||ext==".toml"||ext==".yaml"||ext==".yml")&&File.Exists(f)&&!list.Contains(f))list.Add(f);}
                    if(list.Count>0)return list;}
                if(r==null||r.Type=="package"||r.Type=="workshop"||String.IsNullOrEmpty(r.Path))return list;
                string dir=r.IsData?r.Path:Path.GetDirectoryName(r.Path);if(dir==null||!Directory.Exists(dir))return list;
                bool atRoot=SamePath(Path.GetFullPath(dir).TrimEnd('\\'),Path.GetFullPath(game).TrimEnd('\\'));string stem=Path.GetFileNameWithoutExtension(r.Path);
                foreach(var f in Directory.GetFiles(dir,"*.ini")){string n=Path.GetFileName(f);if(NotSettings.Contains(n,StringComparer.OrdinalIgnoreCase))continue;if(atRoot&&!n.StartsWith(stem,StringComparison.OrdinalIgnoreCase))continue;list.Add(f);}
            }catch{}
            return list.OrderBy(f=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f),"설정|setting|config",System.Text.RegularExpressions.RegexOptions.IgnoreCase)?0:1).ThenBy(f=>f,StringComparer.OrdinalIgnoreCase).ToList();
        }
        void OpenSettings(ModEntry r){
            var files=SettingsFiles(r);if(files.Count==0)return;
            Action<string> open=f=>{try{Process.Start("notepad.exe","\""+f+"\"");}catch(Exception e){MessageBox.Show(Window,e.Message,"설정 파일을 열지 못했습니다");}};
            if(files.Count==1){open(files[0]);return;}
            var menu=new ContextMenu{PlacementTarget=settingsButton,Placement=PlacementMode.Bottom};
            foreach(var f in files){var file=f;var m=new MenuItem{Header=Path.GetFileName(f)};m.Click+=(s,e)=>open(file);menu.Items.Add(m);}
            menu.IsOpen=true;
        }
        sealed class UpdateRow { public string Id,Name,Url,Installed="",Posted="",Title="",Error="",Github="",Mod="";public string[] Names;public bool Only;public DateTime? Since,Dated;
            // what the card keeps: the version, or "@yyyy-MM-dd" when the title has only a day
            public string Kept{get{return Posted!=""?Posted:Dated!=null?"@"+Dated.Value.ToString("yyyy-MM-dd"):"";}}
            public bool Newer{get{return !Skipped&&Error==""&&(Posted!=""?Installed!=""&&Posts.Compare(Installed,Posted)<0:Dated!=null&&Since!=null&&Dated.Value.Date>Since.Value.Date);}}
            public bool Skipped{get{string v;return Github!=""&&Error==""&&Posted!=""&&View.Skip.TryGetValue(Github,out v)&&v==Posted&&Installed!=""&&Posts.Compare(Installed,Posted)<0;}}   // gone back from it
        }
        // When the mod's files came onto this PC: the later of created (copied here) and written, newest file of its own folder.
        static DateTime? InstalledAt(ModEntry r){
            try{
                string p=r==null?"":r.Path??"";DateTime best=DateTime.MinValue;
                IEnumerable<FileInfo> files=File.Exists(p)?new[]{new FileInfo(p)}:Directory.Exists(p)?new DirectoryInfo(p).GetFiles():new FileInfo[0];
                foreach(var f in files){var t=f.CreationTime>f.LastWriteTime?f.CreationTime:f.LastWriteTime;if(t>best)best=t;}
                return best==DateTime.MinValue?(DateTime?)null:best;
            }catch{return null;}
        }
        // a title day ("@yyyy-MM-dd") later than the day the mod came in: maybe a new version
        static bool DatedNews(ModEntry r,string kept,out DateTime day,out DateTime at){
            at=DateTime.MinValue;
            if(kept==null||!kept.StartsWith("@")||!DateTime.TryParseExact(kept.Substring(1),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out day)){day=DateTime.MinValue;return false;}
            var since=InstalledAt(r);if(since==null)return false;at=since.Value;
            return day.Date>at.Date;
        }
        // ---------- 모드매니저 자기 업데이트 (GitHub 릴리스) ----------
        SelfUpdate.Release selfRelease;
        void CheckSelf(){
            View.LastSelfCheck=DateTime.Now.ToString("yyyy-MM-dd");View.Save();
            Task.Run(()=>SelfUpdate.Latest()).ContinueWith(t=>{if(t.IsFaulted||busy||watchId!="")return;selfRelease=t.Result;if(Posts.Compare(Version,selfRelease.Version)<0)ShowSelfNotice(selfRelease);},TaskScheduler.FromCurrentSynchronizationContext());
        }
        void ShowSelfNotice(SelfUpdate.Release rel){
            if(SelfUpdate.Edition()!="public"){ShowNotice("info","모드매니저 새 버전 v"+rel.Version+" 이 GitHub 에 올라왔습니다(지금 v"+Version+"). 이 버전은 개발판이라 스스로 바꾸지 않습니다.","릴리스 보기",()=>OpenLink(rel.Page),null,null);return;}   // the notice has its own 닫기
            string size=rel.Size>0?" (약 "+Math.Max(1,rel.Size>>20)+"MB)":"";
            ShowNotice("info","모드매니저 새 버전 v"+rel.Version+" 이 나왔습니다(지금 v"+Version+"). '지금 업데이트'를 누르면 받아서 바꾸고 창을 다시 엽니다"+size+". 모드 설정과 게임 파일은 그대로입니다.","지금 업데이트",()=>StartSelfUpdate(rel),"바뀐 점 보기",ShowSelfNotes);
        }
        // 패치노트 of the manager itself (the version beside its name at the top, and the notice's '바뀐 점 보기'): its GitHub releases
        void ShowSelfNotes(){
            var rel=selfRelease;bool newer=rel!=null&&Posts.Compare(Version,rel.Version)<0;
            Dialogs.SelfNotes(Window,Version,newer?rel.Version:"",()=>ModRelease.Releases(SelfUpdate.Repo,"LOH_MM_SELF_RELEASES_URL"),()=>OpenLink(SelfUpdate.Page),
                newer&&SelfUpdate.Edition()=="public"&&!busy?(Action)(()=>StartSelfUpdate(rel)):null);
        }
        // a mod on GitHub releases: its zip, then the same install preview as a dropped archive; on OK the old version comes out first
        // (to the 휴지통), and if the new one does not go in, the old one comes back
        readonly Dictionary<string,SelfUpdate.Release> modReleases=new Dictionary<string,SelfUpdate.Release>();
        readonly Dictionary<string,string> modNotes=new Dictionary<string,string>();   // every release's notes, read once (patch notes window)
        readonly Dictionary<string,string> replaceFor=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,string[]> replaceVersions=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);   // downloaded zip -> {old version, new version}   // downloaded zip → record it replaces
        void StartModUpdate(string id){
            if(busy)return;var r=Row(id);SelfUpdate.Release rel;lock(modReleases)modReleases.TryGetValue(id,out rel);if(r==null||rel==null)return;
            if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 업데이트할 수 있습니다.","모드매니저");return;}
            if(Dirty()){MessageBox.Show(Window,"저장하지 않은 변경이 있습니다. 먼저 저장하거나 '되돌리기'를 누른 뒤 업데이트해 주세요.","모드매니저");return;}
            string name=r.Name;var owner=OwnerOf(r);DllVersion dv;live.Versions.TryGetValue(r.Path??"",out dv);string oldVersion=Posts.Clean(InstalledVersion(r,dv));
            RunBusy(name+" 새 버전을 GitHub 에서 받는 중…",()=>ModRelease.Download(rel,id),zip=>{if(owner!=null){replaceFor[zip]=owner.Id;replaceVersions[zip]=new[]{oldVersion,rel.Version};}QueueInstall(new[]{zip});},
                error=>MessageBox.Show(Window,name+" 새 버전을 받지 못했습니다: "+error+". 지금 버전은 그대로입니다.","모드매니저",MessageBoxButton.OK,MessageBoxImage.Warning));
        }
        void StartSelfUpdate(SelfUpdate.Release rel){
            if(busy)return;
            if(Dirty()){MessageBox.Show(Window,"저장하지 않은 변경이 있습니다. 먼저 저장하거나 '되돌리기'를 누른 뒤 업데이트해 주세요.","모드매니저 업데이트");return;}
            if(watchId!=""){MessageBox.Show(Window,"'"+watchName+"' 수동 설치를 기록하는 중입니다. 먼저 '설치 끝' 또는 '기록 취소'를 눌러 주세요.","모드매니저 업데이트");return;}
            if(!SelfUpdate.Writable()){if(MessageBox.Show(Window,"이 모드매니저가 있는 폴더("+Backend.AppDir+")에 쓸 권한이 없어 스스로 바꿀 수 없습니다.\n릴리스 화면을 열어 새 버전을 직접 받을까요?","모드매니저 업데이트",MessageBoxButton.YesNo)==MessageBoxResult.Yes)OpenLink(rel.Page);return;}
            HideNotice();
            RunBusy("모드매니저 v"+rel.Version+" 받는 중… (받은 뒤 창이 닫혔다가 다시 열립니다)",()=>SelfUpdate.Prepare(rel),root=>{
                SelfUpdate.Launch(root,Version,rel.Version);Window.Close();Application.Current.Shutdown();
            },error=>MessageBox.Show(Window,error+"\n\n지금 버전은 그대로입니다.","업데이트하지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Warning));
        }
        // Reads each linked post's title once (only when the button is pressed) from the gallery search list, so no post view is
        // counted; takes the version for each mod from the title, shows a table.
        void CheckUpdates(){CheckUpdates(false);}
        void CheckUpdates(bool quiet){
            if(busy)return;
            var rows=new List<UpdateRow>();var me=Posts.Known1("manager");
            rows.Add(new UpdateRow{Id=me.Id,Name=me.Name,Url=Posts.LinkFor(me.Id),Names=me.Names,Installed=Version});
            foreach(var r in working){
                var info=Manifests.For(r,game);string gh=info!=null?info.Github:"";
                string url=LinkOf(r);if((url==""||!url.StartsWith("http",StringComparison.OrdinalIgnoreCase))&&gh=="")continue;if(rows.Any(x=>x.Id==r.Id))continue;
                if(gh!="")url="https://github.com/"+gh+"/releases/latest";   // its releases, not the post title
                DllVersion v;live.Versions.TryGetValue(r.Path??"",out v);var k=Posts.Known1(r.Id);
                var names=(k!=null?k.Names:new string[0]).Concat(new[]{r.Name}).Concat(info!=null&&info.Name!=""?new[]{info.Name}:new string[0]).Distinct().ToArray();
                var owner=OwnerOf(r);rows.Add(new UpdateRow{Id=r.Id,Name=CardName(r),Url=url,Names=names,Installed=Posts.Clean(InstalledVersion(r,v)),Since=InstalledAt(r),Github=gh,Mod=owner!=null?owner.Id:r.Id});
            }
            rows=rows.Where(x=>x.Url!="").ToList();foreach(var x in rows)x.Only=rows.Where(y=>y.Url==x.Url).Select(y=>y.Mod).Distinct().Count()==1;   // pieces of one install count once
            Func<List<UpdateRow>> read=()=>{
                // the manager itself: its GitHub release is the truth (the post title is only a fallback)
                try{var rel=SelfUpdate.Latest();if(rel.Version!=""){var m=rows[0];m.Posted=rel.Version;m.Title="GitHub 릴리스 v"+rel.Version;m.Url=rel.Page;selfRelease=rel;}}catch{}
                foreach(var x in rows.Where(y=>y.Github!="")){   // mods on GitHub releases: the same, from their own repo
                    try{var rel=ModRelease.Latest(x.Github);x.Posted=rel.Version;x.Title="GitHub 릴리스 v"+rel.Version;x.Url=rel.Page;lock(modReleases)modReleases[x.Id]=rel;}catch(Exception e){x.Error=e.Message;x.Title="GitHub 릴리스";}}
                foreach(var group in rows.GroupBy(x=>x.Url)){if(group.All(x=>x.Title.StartsWith("GitHub 릴리스")))continue;
                    string title="",error="";
                    try{title=Posts.TitleFor(group.Key,group.SelectMany(x=>x.Names));if(title=="")error="이 글의 제목을 찾지 못했습니다(제목에 모드 이름이 없거나 글이 지워짐)";}catch(Exception e){error=e.Message;}
                    foreach(var x in group){x.Title=title;x.Error=error;x.Posted=error==""?Posts.VersionIn(title,x.Names,x.Only):"";if(error==""&&x.Posted=="")x.Dated=Posts.DateIn(title,DateTime.Today);}   // no version: the title's day ("12/24 업뎃")
                }
                return rows;
            };
            // the manager's own row: '지금 업데이트' when this public copy can replace itself, else its GitHub release page
            Func<UpdateRow,string> target=x=>x.Id=="manager"&&selfRelease!=null&&x.Newer&&SelfUpdate.Edition()=="public"?"self:update":x.Github!=""&&x.Newer?"gh:"+x.Id:x.Url;
            Action<List<UpdateRow>> show=done=>Dialogs.Updates(Window,done.Select(x=>new string[]{x.Name,x.Installed,x.Posted,x.Title,x.Error,target(x),x.Dated!=null?x.Dated.Value.ToString("M/d"):"",x.Newer?"1":"",x.Since!=null?x.Since.Value.ToString("M/d"):"",x.Skipped?"1":""}).ToList(),
                url=>{if(url=="self:update"){if(selfRelease!=null)StartSelfUpdate(selfRelease);}else if(url.StartsWith("gh:"))StartModUpdate(url.Substring(3));else OpenLink(url);});
            if(quiet){   // on open, once a day: no busy cover, a notice only when something is newer
                View.LastUpdate=DateTime.Now.ToString("yyyy-MM-dd");View.Save();
                Task.Run(read).ContinueWith(t=>{
                    if(t.IsFaulted)return;var done=t.Result;foreach(var x in done)if(x.Error=="")updates[x.Id]=x.Skipped?x.Installed:x.Kept;NoteHistory(done);Render();
                    var newer=done.Where(x=>x.Newer).ToList();
                    if(selfRelease!=null&&Posts.Compare(Version,selfRelease.Version)<0&&watchId==""){ShowSelfNotice(selfRelease);return;}   // the manager first: one click updates it
                    if(newer.Count>0&&watchId=="")ShowNotice("info","새 버전이 있는 모드 "+newer.Count+"개: "+String.Join(", ",newer.Select(x=>x.Posted!=""?x.Name+" v"+x.Posted:x.Name+"(제목 날짜 "+x.Dated.Value.ToString("M/d")+")"))+" (게시글 제목 기준)","자세히",()=>{HideNotice();show(done);},null,null);
                },TaskScheduler.FromCurrentSynchronizationContext());
                return;
            }
            RunBusy("디시 게시글 제목에서 버전을 확인하는 중…",read,done=>{
                foreach(var x in done)if(x.Error=="")updates[x.Id]=x.Skipped?x.Installed:x.Kept;NoteHistory(done);
                Render();show(done);
            },null);
        }

        void NoteHistory(List<UpdateRow> done){History.Note(done.Where(x=>x.Error=="").Select(x=>Tuple.Create(x.Id,x.Posted,x.Title,x.Installed)));}
        // 패치노트: post titles the update checks saw, the versions used here, and for a package the guide of each version installed
        // (records kept by the manager, the old ones in the 휴지통 too). The post's own text is not read: that would count a view.
        void ShowHistory(ModEntry r,SubPatch patch=null){
            if(r==null)return;
            var posts=History.Posts(r.Id);var used=History.Used(r.Id);var docs=new List<string[]>();
            // the card's install: a DLL head row (a shared loader another pack describes) still shows the guides of the pack that put it in
            var card=r.Type=="package"?null:OwnerOf(r);string pkgId=r.Type=="package"?r.Id:card!=null&&card.Id!=""?"pkg:"+card.Id:"";
            if(pkgId!=""){
                string stem=NameStem(card!=null?card.Name:r.Name);string root=Path.Combine(game,"LOH_ModManager","Installed");
                try{if(Directory.Exists(root))foreach(var dir in Directory.GetDirectories(root)){
                    var rec=J.ReadFile(Path.Combine(dir,"record.json"));if(rec==null||J.S(rec,"Doc").Trim()=="")continue;
                    if("pkg:"+J.S(rec,"Id")!=pkgId&&NameStem(J.S(rec,"Name"))!=stem)continue;
                    string st=J.S(rec,"State");DateTime when;string at=DateTime.TryParse(J.S(rec,"Installed"),out when)?when.ToString("yyyy-MM-dd"):"";
                    docs.Add(new[]{at,J.S(rec,"Name")+(st=="Installed"?"":J.B(rec,"Parked")?" (꺼 둠)":" (지운 버전)"),J.S(rec,"DocName"),J.S(rec,"Doc"),st=="Installed"?"0":"1"});
                }}catch{}
                // the one in use first, then the newest; the same guide once (installing the same pack again keeps a record each time)
                docs=docs.OrderBy(x=>x[4]).ThenByDescending(x=>x[0]).GroupBy(x=>x[2]+"\n"+x[3].Trim()).Select(g=>g.First()).ToList();
            }
            string link=patch!=null?PatchLink(patch):LinkOf(r);var info=Manifests.For(r,game);var written=info!=null?info.Notes:new List<string[]>();string repo=info!=null?info.Github:"";
            var back=OwnerOf(r);if(back!=null&&back.RollId=="")back=null;
            Action<SelfUpdate.Release> show=rel=>Dialogs.History(Window,CardName(r),link,posts.Select(x=>new[]{J.S(x,"At"),J.S(x,"Version"),J.S(x,"Title")}).Reverse().ToList(),used.Select(x=>new[]{J.S(x,"At"),J.S(x,"Version")}).Reverse().ToList(),docs,
                written,rel,View.AutoUpdate,on=>{View.AutoUpdate=on;View.Save();},()=>{if(link!="")OpenLink(link);else if(patch!=null)EditPatchLink(patch);else EditLink(r);},
                back!=null?"'지금 업데이트' 전에 쓰던 v"+back.RollTo+Particle(back.RollTo,"으로","로")+" 되돌립니다.":"",back!=null?(Action)(()=>RollBack(r,back)):null);
            SelfUpdate.Release known;string allKnown;lock(modReleases){modReleases.TryGetValue(r.Id,out known);modNotes.TryGetValue(r.Id,out allKnown);}
            Func<SelfUpdate.Release,string,SelfUpdate.Release> withAll=(rel,all)=>rel==null||String.IsNullOrEmpty(all)?rel:new SelfUpdate.Release{Version=rel.Version,Url=rel.Url,Page=rel.Page,Notes=all,Digest=rel.Digest,Asset=rel.Asset,Size=rel.Size};
            if(repo==""){show(null);return;}
            if(known!=null&&allKnown!=null){show(withAll(known,allKnown));return;}
            string id=r.Id;Task.Run(()=>{SelfUpdate.Release rel=known;string all=null;try{if(rel==null)rel=ModRelease.Latest(repo);}catch{}try{all=ModRelease.AllNotes(repo);}catch{}return Tuple.Create(rel,all);})
                .ContinueWith(t=>{var rel=t.Result.Item1;var all=t.Result.Item2;lock(modReleases){if(rel!=null)modReleases[id]=rel;if(all!=null)modNotes[id]=all;}show(withAll(rel,all));},TaskScheduler.FromCurrentSynchronizationContext());   // its release notes (every release), read once
        }
        // '이전 버전으로 되돌리기': the install '지금 업데이트' put in goes to the 휴지통, the one it replaced comes back; that version is not announced again
        void RollBack(ModEntry r,OneList.Owner o){
            if(o==null||o.RollId==""||!CanTouchGameFiles())return;
            string name=CardName(r),from=o.RollFrom,to=o.RollTo;var info=Manifests.For(r,game);string repo=info!=null?info.Github:"";
            if(!Dialogs.Confirm(Window,"이전 버전으로 되돌리기",name+Particle(name,"을","를")+" v"+from+"에서 v"+to+Particle(to,"으로","로")+" 되돌릴까요? 지금 버전은 '지운 모드'로 옮겨 두니 나중에 다시 되살릴 수 있습니다.","되돌리기"))return;
            RunBusy("이전 버전으로 되돌리는 중… "+name,()=>Backend.Run("RollBack",game,new Dictionary<string,object>{{"Id",o.Id}}),result=>{
                if(repo!=""&&from!=""){View.Skip[repo]=from;View.Save();}
                foreach(var key in updates.Where(x=>x.Value==from).Select(x=>x.Key).ToList())updates.Remove(key);
                ReloadCatalog(true);LoadPackages();
                ShowNotice("ok",name+Particle(name,"을","를")+" v"+to+Particle(to,"으로","로")+" 되돌렸습니다. v"+from+" 알림은 다시 띄우지 않고, 더 새 버전이 나오면 알려 드립니다."+(J.S(result,"Note")!=""?" "+J.S(result,"Note"):""),null,null,null,null);
            },error=>MessageBox.Show(Window,"되돌리지 못했습니다: "+error+". 지금 버전은 그대로입니다.","모드매니저",MessageBoxButton.OK,MessageBoxImage.Warning));
        }
        // 을/를, 으로/로 after a word: its last Hangul syllable (ㄹ takes 로), or the last digit read aloud (0 영, 1 일, 3 삼, 6 육 …); otherwise both.
        // A trailing (…) is an aside: the word before it decides ("독박육아 (배포판)를")
        static string Particle(string word,string withFinal,string without){
            string w=(word??"").TrimEnd();while(w.EndsWith(")")&&w.LastIndexOf('(')>0)w=w.Substring(0,w.LastIndexOf('(')).TrimEnd();
            if(w=="")return withFinal+"("+without+")";char c=w[w.Length-1];int final=-1;
            if(c>='\uAC00'&&c<='\uD7A3')final=(c-0xAC00)%28;
            else if(c>='0'&&c<='9')final=new[]{21,8,0,16,0,0,1,8,8,0}[c-'0'];   // 영 일 이 삼 사 오 육 칠 팔 구 (final consonant index)
            else if(char.IsLetter(c))return withFinal+"("+without+")";
            if(final<0)return withFinal+"("+without+")";
            bool ro=withFinal=="으로";
            return final==0||(ro&&final==8)?without:withFinal;
        }
        // a mod's name without its version ("상한해제 종합 패처 V1.0.2" → "상한해제 종합 패처", "올인원패처 (8)" → "올인원패처")
        static string NameStem(string name){return System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(name??"",@"[vV]?\d+(\.\d+)+",""),@"\(\d+\)","").Trim().ToLowerInvariant();}

        // ---------- editing ----------
        void OnSelect(ListBox list,SelectionChangedEventArgs e){
            if(selecting)return;var picked=list.SelectedItems.Cast<ModRowView>().ToList();if(picked.Count==0)return;
            var added=e.AddedItems.OfType<ModRowView>().LastOrDefault();
            string primary=added!=null?added.Id:picked.Any(x=>x.Id==primaryId)?primaryId:picked[0].Id;
            selectedIds.Clear();foreach(var x in picked)selectedIds.Add(x.Id);primaryId=primary;
            selecting=true;try{(list==activeList?inactiveList:activeList).SelectedItems.Clear();}finally{selecting=false;}
            ShowDetails();
        }
        // why a row cannot be switched that way ("" = it can)
        static string CannotSwitch(ModEntry r,bool enable){
            if(r.Locked&&!enable&&!Parks(r))return "'"+r.Name+"'("+r.Kind+")은 게임 파일을 직접 바꾸는 모드라 여기서 끌 수 없습니다. 빼려면 '설치 정보' 탭에서 제거하세요.";
            if(r.IsData&&r.Suffix=="")return "'"+r.Name+"'은 ModInfo.json 에 strIDEnd 가 없어 게임 모드 목록으로 켜고 끌 수 없습니다.";
            return "";
        }
        void SwitchRow(ModEntry r,bool enable,int? index){
            bool wasOn=r.Enabled;
            foreach(var m in Family(r)){if(CannotSwitch(m,enable)!="")continue;m.Enabled=enable;
                if(m.IsData)foreach(var o in working)if(o!=m&&o.IsData&&o.Suffix==m.Suffix)o.Enabled=enable;}   // the game list keys by strIDEnd
            if(enable&&index.HasValue)PlaceActive(r.Id,index.Value);
            else if(enable&&!wasOn)PlaceActive(r.Id,int.MaxValue);   // switched on without a drop spot: bottom of the load order (wins, like a new install)
        }
        void Toggle(string id,bool enable,int? index){
            var r=Row(id);if(r==null)return;
            string why=CannotSwitchFamily(r,enable);if(why!=""){ShowNotice(r.Locked?"info":"warn",why,null,null,null,null);return;}
            SwitchRow(r,enable,index);
            selectedId=id;Render();
            // show where it landed: the list it moved into scrolls to it (selected there), once that list has laid it out
            var list=r.Enabled?activeList:inactiveList;var rows=r.Enabled?active:inactive;
            Window.Dispatcher.BeginInvoke(new Action(()=>{var shown=rows.FirstOrDefault(x=>x.Id==id);if(shown!=null)list.ScrollIntoView(shown);}),System.Windows.Threading.DispatcherPriority.Loaded);
        }
        // Puts id at position index among the enabled rows; disabled rows keep their places.
        void PlaceActive(string id,int index){   // index: among the cards on screen (pieces of a card are not counted)
            movedCards.Add(id);var r=Row(id);var kids=Family(r).Skip(1).ToList();var others=working.Where(x=>x.Enabled&&x.Id!=id&&Shown(x)).ToList();working.Remove(r);foreach(var k in kids)working.Remove(k);
            index=Math.Max(0,Math.Min(index,others.Count));
            int at=index<others.Count?working.IndexOf(others[index]):others.Count>0?LastOf(others[others.Count-1])+1:0;
            working.Insert(at,r);working.InsertRange(at+1,kids);   // its pieces right after it: their own orders follow the card
        }
        int LastOf(ModEntry card){int at=working.IndexOf(card);foreach(var k in Family(card).Skip(1)){int j=working.IndexOf(k);if(j>at)at=j;}return at;}
        // several picked rows: the ones that can go that way move together, in their order; the rest are named once
        void ToggleMany(List<ModEntry> rows,bool enable){
            rows=rows.Where(r=>r.Enabled!=enable).ToList();if(rows.Count==0)return;
            if(rows.Count==1){Toggle(rows[0].Id,enable,null);return;}
            var skipped=rows.Where(r=>CannotSwitchFamily(r,enable)!="").ToList();var moved=rows.Except(skipped).ToList();
            foreach(var r in moved)SwitchRow(r,enable,null);
            selectedIds.Clear();foreach(var r in moved)selectedIds.Add(r.Id);primaryId=moved.Count>0?moved[0].Id:null;Render();
            if(skipped.Count>0)ShowNotice("info",(enable?"켜지":"끄지")+" 못한 모드 "+skipped.Count+"개: "+String.Join(", ",skipped.Select(r=>r.Name))+" — "+(enable?"ModInfo.json 에 ID(strIDEnd)가 없습니다.":"게임 파일을 직접 바꾸는 모드라 '설치 정보' 탭에서 제거해야 합니다."),null,null,null,null);
            if(moved.Count>0){var list=enable?activeList:inactiveList;var shown=(enable?active:inactive).FirstOrDefault(x=>x.Id==moved[0].Id);if(shown!=null)Window.Dispatcher.BeginInvoke(new Action(()=>list.ScrollIntoView(shown)),DispatcherPriority.Loaded);}
        }
        // the picked rows that are on move one place as a block (stops at the top or bottom)
        void Move(int delta){
            var picked=SelectedRows().Where(x=>x.Enabled).ToList();if(picked.Count==0)return;
            var on=working.Where(x=>x.Enabled&&Shown(x)).ToList();var at=picked.Select(x=>on.IndexOf(x)).OrderBy(x=>x).ToList();
            if(delta<0&&at[0]==0||delta>0&&at[at.Count-1]==on.Count-1)return;
            foreach(var r in delta<0?picked.OrderBy(x=>on.IndexOf(x)):picked.OrderByDescending(x=>on.IndexOf(x))){var now=working.Where(x=>x.Enabled&&Shown(x)).ToList();PlaceActive(r.Id,now.IndexOf(r)+delta);}
            Render();var shown=active.FirstOrDefault(x=>x.Id==(delta<0?picked.OrderBy(y=>on.IndexOf(y)).First():picked.OrderByDescending(y=>on.IndexOf(y)).First()).Id);if(shown!=null)activeList.ScrollIntoView(shown);
        }
        // "Reset": every mod to the right list. Only the lists change; Save applies it (the entry then loads nothing).
        void AllOff(){
            if(busy)return;int n=0;foreach(var r in working)if(r.Enabled&&(!r.Locked||Parks(r))&&!(r.IsData&&r.Suffix=="")){r.Enabled=false;n++;}   // installs (patches, loader bundles) switch off like the 끄기 button
            if(n==0){ShowNotice("info","이미 모든 모드가 꺼져 있습니다.",null,null,null,null);return;}
            Render();ShowNotice("info","모드 "+n+"개를 모두 껐습니다(아직 저장 전). 저장하면 다음 실행은 모드 없이 켜집니다. 취소하려면 위쪽 '되돌리기'를 누르세요.",null,null,null,null);
        }
        void Revert(){
            if(busy||!Dirty())return;working=baseRows.Select(r=>r.Clone()).ToList();movedCards.Clear();unregistered.Clear();subPending.Clear();
            if(monitor!=null)monitor.SetRows(AllRows());Render();ShowNotice("info","저장하지 않은 변경을 모두 취소했습니다.",null,null,null,null);
        }
        void SetRoute(string route){if(selecting)return;var r=SelectedRow();if(r==null||String.Equals(r.Route,route,StringComparison.OrdinalIgnoreCase))return;r.Route=route;Render();}
        void Locate(){
            var r=SelectedRow();if(r==null)return;
            var d=new Microsoft.Win32.OpenFileDialog{Filter="모드 DLL (*.dll)|*.dll",Title=r.Name+" DLL 위치"};
            if(d.ShowDialog(Window)==true){r.Path=d.FileName;try{r.Present=File.Exists(r.Path);}catch{}if(monitor!=null)monitor.SetRows(AllRows());Render();}
        }
        void Unregister(){
            var r=SelectedRow();if(r==null||!r.External)return;
            working.Remove(r);if(baseRows.Any(b=>b.Id==r.Id))unregistered.Add(r.Id);selectedId=null;Render();
            ShowNotice("info","'"+r.Name+"'을 목록에서 뺍니다. 저장하면 적용되며, DLL 파일은 그대로 둡니다.",null,null,null,null);
        }
        void OpenLogFile(){
            var r=SelectedRow();if(r==null||r.Log=="")return;string dir=Path.GetDirectoryName(r.Path);string log=dir==null?"":Path.Combine(dir,r.Log);
            if(File.Exists(log))Process.Start("notepad.exe","\""+log+"\"");else MessageBox.Show(Window,"아직 로그가 없습니다: "+log,"로그");
        }
        void OnKey(object sender,KeyEventArgs e){
            if(dragging&&e.Key==Key.Escape){DragEnd(false);e.Handled=true;return;}
            if(busy)return;bool alt=(Keyboard.Modifiers&ModifierKeys.Alt)!=0;Key key=e.Key==Key.System?e.SystemKey:e.Key;
            if(key==Key.S&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){Save();e.Handled=true;}
            else if(key==Key.F5){Refresh();e.Handled=true;}
            else if(key==Key.F9){Launch();e.Handled=true;}
            else if(alt&&(key==Key.Up||key==Key.Down)){Move(key==Key.Up?-1:1);e.Handled=true;}
            else if(key==Key.Enter&&(activeList.IsKeyboardFocusWithin||inactiveList.IsKeyboardFocusWithin)){var rows=SelectedRows();if(rows.Count>0){ToggleMany(rows,!rows[0].Enabled);e.Handled=true;}}
            else if(key==Key.Delete&&(activeList.IsKeyboardFocusWithin||inactiveList.IsKeyboardFocusWithin)){var rows=SelectedRows();if(rows.Count>1)DeleteMany(rows);else if(rows.Count==1)DeleteRow(rows[0]);e.Handled=true;}
        }

        // ---------- drag and drop ----------
        // a double-click on the card's own controls (the 접기/펼치기 pill, a checkbox, a line inside) is not a switch of the card
        static bool OnCardControl(DependencyObject d){
            while(d!=null&&!(d is ListBoxItem)){
                if(d is System.Windows.Controls.Primitives.ButtonBase)return true;   // ToggleButton, CheckBox, Button
                var fe=d as FrameworkElement;if(fe!=null&&fe.DataContext is SubRowView)return true;
                d=d is Visual||d is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);
            }
            return false;
        }
        static ModRowView ItemAt(ListBox list,Point p){
            var hit=list.InputHitTest(p) as DependencyObject;
            // text pieces (Run) are not visuals: climb them through the logical tree
            while(hit!=null&&!(hit is ListBoxItem))hit=hit is Visual||hit is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(hit):LogicalTreeHelper.GetParent(hit);
            var item=hit as ListBoxItem;return item==null?null:item.DataContext as ModRowView;
        }
        static int IndexAt(ListBox list,Point p){
            int last=-1;
            for(int i=0;i<list.Items.Count;i++){
                var c=list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;if(c==null||!c.IsVisible)continue;last=i;
                var top=c.TranslatePoint(new Point(0,0),list);if(p.Y<top.Y+c.ActualHeight/2)return i;
            }
            if(last>=0&&last<list.Items.Count-1)return last+1;   // past the cards built so far, with more below them in a long list: not the end of the list
            return list.Items.Count;
        }
        // Past the drag distance the window takes the mouse: a picture of the card follows the pointer, the blue line marks the
        // drop spot, the wheel scrolls the list under the pointer and holding near a list's top/bottom edge scrolls it.
        void DragBegin(ListBox list,MouseEventArgs e){
            if(dragging||e.LeftButton!=MouseButtonState.Pressed||dragItem==null||dragSource!=list||busy)return;
            if(pendingSingle==null&&!list.SelectedItems.Contains(dragItem))return;   // the press re-picked: wait for its selection
            var d=e.GetPosition(Window)-dragStart;
            if(Math.Abs(d.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(d.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            var box=list.ItemContainerGenerator.ContainerFromItem(dragItem) as FrameworkElement;
            if(box==null||box.ActualWidth<1||box.ActualHeight<1){dragItem=null;return;}
            dragGrab=e.GetPosition(box)-d;
            dragGhost.Width=box.ActualWidth;dragGhost.Height=box.ActualHeight;dragGhost.Fill=Snapshot(box);dragGhost.Visibility=Visibility.Visible;dragResult.Width=box.ActualWidth;dragResult.Height=box.ActualHeight;dragResultBack.MaxWidth=box.ActualWidth*0.72;
            if(!Mouse.Capture(Window)){dragGhost.Visibility=Visibility.Collapsed;dragGhost.Fill=null;dragItem=null;return;}
            pendingSingle=null;dragGroup=selectedIds.Contains(dragItem.Id)&&selectedIds.Count>1?SelectedRows().Select(r=>r.Id).ToList():null;
            trashText.Text=dragGroup!=null?"🗑 여기에 놓으면 고른 "+dragGroup.Count+"개를 삭제합니다":"🗑 여기에 놓으면 삭제합니다";
            dragging=true;scrollCarry=0;trashZone.Visibility=Visibility.Visible;ShowTargets(true);PaintZones();dragTimer.Start();DragTrack(e.GetPosition(dragLayer));
        }
        // a still picture (a live VisualBrush would go blank when the list scrolls the card's container away)
        static Brush Snapshot(FrameworkElement box){
            var src=PresentationSource.FromVisual(box);var m=src!=null&&src.CompositionTarget!=null?src.CompositionTarget.TransformToDevice:Matrix.Identity;
            int w=Math.Max(1,(int)Math.Ceiling(box.ActualWidth*m.M11)),h=Math.Max(1,(int)Math.Ceiling(box.ActualHeight*m.M22));
            var dv=new DrawingVisual();using(var dc=dv.RenderOpen())dc.DrawRectangle(new VisualBrush(box),null,new Rect(0,0,box.ActualWidth,box.ActualHeight));
            var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap(w,h,96*m.M11,96*m.M22,PixelFormats.Pbgra32);bmp.Render(dv);bmp.Freeze();
            return new ImageBrush(bmp);
        }
        bool Over(FrameworkElement el,Point p){
            if(!el.IsVisible)return false;var q=dragLayer.TranslatePoint(p,el);
            return q.X>=0&&q.Y>=0&&q.X<=el.ActualWidth&&q.Y<=el.ActualHeight;
        }
        void DragTrack(Point p){
            ClearDrop();
            bool trash=Over(trashZone,p);trashZone.SetResourceReference(Border.BorderBrushProperty,trash?"Bad":"BadLine");if(trash)trashTint.SetResourceReference(Border.BackgroundProperty,"BadSoft");else trashTint.Background=Brushes.Transparent;
            bool onActive=!trash&&Over(activePanel,p),onInactive=!trash&&dropOff&&Over(inactivePanel,p);
            Mark(activePanel,true,onActive);if(dropOff)Mark(inactivePanel,true,onInactive);
            if(!onActive){MoveGhost(p.X-dragGrab.X,p.Y-dragGrab.Y);ClearHints();return;}
            int index=IndexAt(activeList,dragLayer.TranslatePoint(p,activeList));
            if(index<active.Count){dropMarked=active[index];dropMarked.DropBrush=Look.Drop;}else activeEnd.BorderBrush=Look.Drop;
            MoveGhost(p.X-dragGrab.X,p.Y-dragGrab.Y);
            DragHints(index);
        }
        void MoveGhost(double x,double y){foreach(var el in new UIElement[]{dragGhost,dragResult}){Canvas.SetLeft(el,x);Canvas.SetTop(el,y);}}
        // One held card over the 켬 list: the card itself says what a drop at the line does, in its colour and one line (the eye is on it), the cards it clashes
        // with get a ring, and every drop spot is coloured — green where the held card keeps what it brings, yellow where something of it or of another gives way,
        // red where a drop breaks a mod — the same rules as the tags (the lower row wins whole; a
        // patch after what it patches; NPC menus need a mod with ConversationCondition.json after them). A card takes the colour of the spot above it,
        // and the line where the colour changes has the blue drop line's shape, so a boundary never falls across a card. The last spot is the same line
        // right under the last card, scrolling with the list (room below it while a card is held, so a full list can reach it), and what is left below it
        // takes its colour. The scrollbar's track shows the whole list's zones, so spots out of view can be seen before the card gets there.
        readonly List<ModRowView> hinted=new List<ModRowView>();Brush endEdge=Brushes.Transparent;const double EndRoom=44,CardGap=6;
        void ClearHints(bool zones=false){foreach(var v in hinted)v.RingBrush=Brushes.Transparent;hinted.Clear();dragResult.Visibility=Visibility.Collapsed;
            if(zones){foreach(var v in active){v.ZoneBrush=Brushes.Transparent;v.GapBrush=Brushes.Transparent;v.EdgeBrush=Brushes.Transparent;v.DropBrush=Brushes.Transparent;v.CardMargin=new Thickness(0,0,0,CardGap);}
                endEdge=Brushes.Transparent;activeEnd.BorderBrush=Brushes.Transparent;activeEnd.Background=Brushes.Transparent;activeEndLayer.Visibility=Visibility.Collapsed;activeRail.Visibility=Visibility.Collapsed;activeRail.Background=null;}}   // the zones stay while the card is held, off the list too
        // the end spot follows the last card (while it is built: a long list scrolled up has none, the track shows that spot then)
        void TrackEnd(){
            int n=active.Count;var last=n>0?activeList.ItemContainerGenerator.ContainerFromIndex(n-1) as FrameworkElement:null;
            if(n>0&&(last==null||!last.IsVisible)){activeEnd.Visibility=Visibility.Collapsed;return;}
            var top=n>0?last.TranslatePoint(new Point(0,0),activeList):new Point(0,0);double y=n>0?top.Y+last.ActualHeight:0,h=activeList.ActualHeight-y;
            if(h<=0){activeEnd.Visibility=Visibility.Collapsed;return;}
            Canvas.SetLeft(activeEnd,top.X);Canvas.SetTop(activeEnd,y);activeEnd.Width=n>0?last.ActualWidth:activeList.ActualWidth;activeEnd.Height=h;activeEnd.Visibility=Visibility.Visible;
        }
        void DragHints(int index){
            var shown=Judge(index);
            foreach(var kv in shown)kv.Key.RingBrush=ToneBrush(Tone(kv.Value.Item2)+"Line");
            foreach(var v in hinted.Where(h=>!shown.ContainsKey(h)).ToList())v.RingBrush=Brushes.Transparent;
            hinted.Clear();hinted.AddRange(shown.Keys);
            if(shown.Count==0){dragResult.Visibility=Visibility.Collapsed;return;}   // nothing it clashes with: the held card keeps its look
            // the worst of it speaks, the top one if several are as bad; the rest are only counted (each card's tag tells them after the drop)
            var said=active.Where(shown.ContainsKey).Select(v=>shown[v]).Where(t=>t.Item1!="").ToList();
            var first=said.OrderByDescending(t=>t.Item2=="bad"?2:t.Item2=="warn"?1:0).FirstOrDefault();
            string tone=Tone(WorstKind(shown));
            dragResult.Background=ToneBrush(tone+"Soft");dragResult.BorderBrush=ToneBrush(tone+"Line");
            dragResultBack.Visibility=first==null?Visibility.Collapsed:Visibility.Visible;
            if(first!=null){string kindTone=Tone(first.Item2);dragResultPill.Background=ToneBrush(kindTone+"Soft");dragResultText.Foreground=ToneBrush(kindTone);dragResultText.Text=first.Item1+(said.Count>1?" 외 "+(said.Count-1)+"개":"");}
            dragResult.Visibility=Visibility.Visible;
        }
        static string Tone(string kind){return kind=="bad"?"Bad":kind=="warn"?"Warn":"Ok";}
        static string WorstKind(Dictionary<ModRowView,Tuple<string,string>> shown){return shown.Values.Any(t=>t.Item2=="bad")?"bad":shown.Values.Any(t=>t.Item2=="warn")?"warn":"ok";}
        Brush ToneBrush(string key){return (Window.TryFindResource(key) as Brush)??Brushes.Transparent;}
        void PaintZones(){
            if(active.Count>0)active[active.Count-1].CardMargin=new Thickness(0,0,0,CardGap+EndRoom);activeEndLayer.Visibility=Visibility.Visible;TrackEnd();
            int n=active.Count;var kinds=new string[n+1];bool any=false;
            for(int i=0;i<=n;i++){var j=Judge(i);if(j.Count>0)any=true;kinds[i]=WorstKind(j);}
            if(!any)return;   // nothing it clashes with: the list keeps its look
            Func<int,string> tone=i=>Tone(kinds[i]);
            Func<string,Brush> brush=ToneBrush;
            Func<int,Brush> edge=i=>i>0&&tone(i)!=tone(i-1)?brush(tone(i)):Brushes.Transparent;
            for(int i=0;i<n;i++){active[i].ZoneBrush=brush(tone(i)+"Soft");active[i].GapBrush=brush(tone(i)+"Soft");active[i].EdgeBrush=edge(i);}
            endEdge=edge(n);activeEnd.Background=brush(tone(n)+"Soft");activeEnd.BorderBrush=endEdge;
            // the track: each card's share of the list in its spot's colour, the end spot always wide enough to see
            var view=Anchored.Viewer(activeList);double extent=view!=null?view.ExtentHeight:0,track=view!=null?view.ViewportHeight:0;
            if(n==0||track<=0||extent<=track+1)return;
            double tail=Math.Max(EndRoom/(extent+EndRoom),6/track),share=(1-tail)/n;
            Func<int,Color> color=i=>{var b=brush(tone(i)+"Line") as SolidColorBrush;return b!=null?b.Color:Colors.Transparent;};
            var g=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(0,1)};
            for(int i=0;i<n;i++){g.GradientStops.Add(new GradientStop(color(i),i*share));g.GradientStops.Add(new GradientStop(color(i),(i+1)*share));}
            g.GradientStops.Add(new GradientStop(color(n),n*share));g.GradientStops.Add(new GradientStop(color(n),1));
            g.Freeze();activeRail.Background=g;activeRail.Visibility=Visibility.Visible;
        }
        // card → (text, ok / warn / bad) for a drop of the held card at the line before active[index]; bad beats warn beats ok
        Dictionary<ModRowView,Tuple<string,string>> Judge(int index){
            var rowChoice=new HashSet<ModRowView>();   // cards where rows also overlap: pictures win the other way round, so the note says both
            var shown=new Dictionary<ModRowView,Tuple<string,string>>();
            Action<ModRowView,string,string> say=(v,text,kind)=>{Tuple<string,string> had;if(v==null||(shown.TryGetValue(v,out had)&&(had.Item2=="bad"||(had.Item2=="warn"&&kind=="ok"))))return;shown[v]=Tuple.Create(text,kind);};
            bool confirmed;bool later=LaterWins(out confirmed);
            if(dragGroup!=null||dragItem==null||!later)return shown;
            string x=dragItem.Id;
            // pieces of a card (a package's data mod inside its card) are read by the game where the card puts them, like the tags count them: every rule below goes by pieces
            var mine=new HashSet<string>(Family(Row(x)).Select(r=>r.Id));mine.Add(x);
            Func<string,string> headOf=id=>{foreach(var kv in family)if(kv.Value.Any(r=>r.Id==id))return kv.Key;return id;};
            Func<string,ModRowView> card=id=>{string h=headOf(id);var r=Row(h);return r!=null&&r.Enabled&&!mine.Contains(h)?active.FirstOrDefault(a=>a.Id==h):null;};
            var ids=active.Select(a=>a.Id).ToList();Func<string,bool> aboveLine=id=>{int i=ids.IndexOf(headOf(id));return i>=0&&i<index;};
            Func<List<string>,List<string>> pieces=o=>o.SelectMany(id=>{var r=Row(id);return r==null?new List<string>{id}:Family(r).Select(k=>k.Id).ToList();}).ToList();
            Func<string,string> nameOf=id=>{var r=Row(id);return r!=null?r.Name:"";};Func<string,string> subject=id=>{string s=nameOf(id);return s+Particle(s,"이","가");};
            foreach(var c in conflicts){
                if(mine.Contains(c.A)==mine.Contains(c.B))continue;string me=mine.Contains(c.A)?c.A:c.B,y=me==c.A?c.B:c.A;var v=card(y);if(v==null)continue;
                string winner=aboveLine(y)?me:y,upper=winner==me?y:me;
                if(c.Kind=="override"){
                    if(Lost(c,upper))say(v,upper==y?"⚠ "+nameOf(y)+" 변경 일부 사라짐":"⚠ 이 모드 변경 일부 사라짐","bad");
                    else if(Choice(c)){rowChoice.Add(v);say(v,winner==y?"겹치는 부분은 "+subject(y)+" 우선함":"겹치는 부분은 이 모드가 우선함","warn");}
                    else say(v,"","ok");}   // both keep what they change: nothing to say
                else if(c.Kind=="patch"&&(c.A==me)!=aboveLine(y))say(v,"⚠ 패치가 대상보다 위","bad");   // A patches B: A must be below B
            }
            // NPC menus: a menu mod without ConversationCondition.json shows only with such a mod after it — counted where the held card's
            // place decides it (the held card itself, or a mod it would leave without that file after it)
            Func<string,bool> live=id=>{var r=Row(id);return r!=null&&(r.Enabled||mine.Contains(id));};
            Func<string,bool> cond=id=>{ModFacts f;return live(id)&&dataFacts.TryGetValue(id,out f)&&f.MenuCond;};
            Func<string,bool> bare=id=>{ModFacts f;return live(id)&&dataFacts.TryGetValue(id,out f)&&f.Menu&&!f.MenuCond;};
            var cards=ids.Where(id=>id!=x).ToList();int from=ids.IndexOf(x);
            cards.Insert(Math.Max(0,Math.Min(index-(from>=0&&from<index?1:0),cards.Count)),x);
            var then=pieces(cards);var now=pieces(ids);
            Func<List<string>,string,bool> hidden=(o,m)=>{int i=o.IndexOf(m);return i>=0&&!o.Skip(i+1).Any(cond)&&o.Take(i).Any(p=>live(p)&&SharesMenu(p,m));};
            foreach(var m in then.Where(bare).ToList()){
                if(!hidden(then,m))continue;
                if(mine.Contains(m)){string held1=m;var above=then.Take(then.IndexOf(m)).Where(p=>!mine.Contains(p)&&(cond(p)||(live(p)&&SharesMenu(p,held1)))).ToList();if(above.Count>0)say(card(above.Last()),"⚠ 메뉴 안 보임","bad");}
                else if(!hidden(now,m))say(card(m),"⚠ 메뉴 안 보임","bad");   // fine where things are now, not after this drop
            }
            // original cards' 무투 animations: only the last data mod's show — the held card's own, or one it would go below
            Func<string,bool> data=id=>live(id)&&dataFacts.ContainsKey(id);
            Func<List<string>,string,bool> lowest=(o,m)=>{int i=o.IndexOf(m);return i>=0&&!o.Skip(i+1).Any(data);};
            var firstUp=ids.Where(id=>id!=x).ToList();firstUp.Insert(0,x);var top=pieces(firstUp);   // the held card above everything: below nothing
            foreach(var m in then.Where(id=>data(id)&&OriginalAnims(id)>0).ToList()){
                if(lowest(then,m))continue;
                if(mine.Contains(m)){var below=then.Skip(then.IndexOf(m)+1).Where(p=>data(p)&&!mine.Contains(p)).ToList();if(below.Count>0)say(card(below.Last()),"⚠ 카드 연출 안 나옴","bad");}
                else if(mine.Any(data)&&lowest(top,m))say(card(m),"⚠ 카드 연출 안 나옴","bad");
            }
            // pictures both bring: the upper (first read) mod's shows (game check 11-03), the opposite of rows — where both overlap, one note says each;
            // a broken row or menu still speaks alone. Only facts: which order to keep, or whether to switch one off, is the player's choice.
            var heldImages=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var p in mine){ModFacts held;if(dataFacts.TryGetValue(p,out held))heldImages.UnionWith(held.Images);}
            if(heldImages.Count>0)
                foreach(var id in now){ModFacts o;if(mine.Contains(id)||!dataFacts.TryGetValue(id,out o)||!o.Images.Overlaps(heldImages))continue;var v=card(id);if(v==null)continue;
                    bool under=aboveLine(id);Tuple<string,string> had;if(shown.TryGetValue(v,out had)&&had.Item2=="bad")continue;
                    if(rowChoice.Contains(v))shown[v]=Tuple.Create(under?"그림은 "+nameOf(id)+" · 그 밖은 이 모드가 우선함":"그림은 이 모드 · 그 밖은 "+subject(id)+" 우선함",under?"warn":"ok");
                    else shown[v]=Tuple.Create(under?"그림은 "+subject(id)+" 우선함":"그림은 이 모드가 우선함",under?"warn":"ok");}
            return shown;
        }
        static bool Choice(Conflict c){return c.Rows.Any(x=>x.A!=null&&x.B!=null?x.A.Intersect(x.B).Any(x.Diff.Contains):x.Kind!="whole"&&x.Kind!="menu");}
        // the list under the pointer, or the one the card came from
        void DragWheel(int delta){
            var p=Mouse.GetPosition(dragLayer);var list=Over(inactiveList,p)?inactiveList:Over(activeList,p)?activeList:dragSource;
            var v=list==null?null:Anchored.Viewer(list);if(v==null)return;
            int lines=SystemParameters.WheelScrollLines;   // as the wheel does without a card: that many cards per notch, -1 = a page
            for(int n=Math.Max(1,Math.Abs(delta)/120);n>0;n--){
                if(lines<0){if(delta>0)v.PageUp();else v.PageDown();}
                else for(int k=0;k<lines;k++){if(delta>0)v.LineUp();else v.LineDown();}
            }
            Retrack();
        }
        // runs every 30 ms while a card is held, so a still pointer near the edge keeps scrolling; faster the closer (or past) the edge
        void DragAutoScroll(){
            if(!dragging)return;TrackEnd();var p=Mouse.GetPosition(dragLayer);
            foreach(var list in new[]{activeList,inactiveList}){
                if(!list.IsVisible)continue;var q=dragLayer.TranslatePoint(p,list);double h=list.ActualHeight;
                if(q.X<0||q.X>list.ActualWidth||q.Y<-90||q.Y>h+90)continue;
                double zone=Math.Min(60,h/4),edge=q.Y<zone?-(zone-q.Y)/zone:q.Y>h-zone?(q.Y-(h-zone))/zone:0;
                if(edge==0)break;edge=Math.Max(-1,Math.Min(1,edge));
                var v=Anchored.Viewer(list);if(v==null)break;
                scrollCarry+=edge*(0.05+0.2*Math.Abs(edge))*60;   // pixels per tick: about a card every few ticks (the lists scroll by pixels)
                int step=(int)scrollCarry;if(step==0)return;scrollCarry-=step;
                v.ScrollToVerticalOffset(v.VerticalOffset+step);Retrack();return;
            }
            scrollCarry=0;
        }
        void Retrack(){Window.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>{if(dragging)DragTrack(Mouse.GetPosition(dragLayer));}));}
        void DragEnd(bool drop){
            if(!dragging)return;dragging=false;dragTimer.Stop();scrollCarry=0;
            var item=dragItem;dragItem=null;var p=Mouse.GetPosition(dragLayer);
            dragGhost.Visibility=Visibility.Collapsed;dragGhost.Fill=null;dragResult.Visibility=Visibility.Collapsed;trashZone.SetResourceReference(Border.BorderBrushProperty,"BadLine");trashTint.Background=Brushes.Transparent;
            if(Mouse.Captured==Window)Window.ReleaseMouseCapture();
            bool toTrash=drop&&Over(trashZone,p),toActive=drop&&!toTrash&&Over(activePanel,p),toInactive=drop&&!toTrash&&!toActive&&dropOff&&Over(inactivePanel,p);
            int index=toActive?IndexAt(activeList,dragLayer.TranslatePoint(p,activeList)):0;
            ClearDrop();ClearHints(true);trashZone.Visibility=Visibility.Collapsed;ShowTargets(false);if(item==null||busy)return;
            var group=dragGroup;dragGroup=null;
            if(group!=null){
                var rows=group.Select(Row).Where(r=>r!=null).ToList();
                if(toTrash)Window.Dispatcher.BeginInvoke(new Action(()=>DeleteMany(rows)));
                else if(toActive)PlaceGroup(rows,index);
                else if(toInactive)ToggleMany(rows,false);
                return;
            }
            if(toTrash){var r=Row(item.Id);Window.Dispatcher.BeginInvoke(new Action(()=>DeleteRow(r)));}
            else if(toActive){int old=active.IndexOf(active.FirstOrDefault(x=>x.Id==item.Id));if(old>=0&&old<index)index--;Toggle(item.Id,true,index);}
            else if(toInactive)Toggle(item.Id,false,null);
        }
        List<string> dragGroup;TextBlock trashText;Border trashTint;
        // a dropped group goes in at the drop line, in its own order, turned on (rows that cannot be turned on are named)
        void PlaceGroup(List<ModEntry> rows,int index){
            var anchor=index<active.Count?active[index].Id:null;
            while(anchor!=null&&rows.Any(r=>r.Id==anchor)){int k=active.IndexOf(active.First(x=>x.Id==anchor))+1;anchor=k<active.Count?active[k].Id:null;}   // the first row below the line that is not moving
            var skipped=rows.Where(r=>!r.Enabled&&CannotSwitchFamily(r,true)!="").ToList();var moving=rows.Except(skipped).ToList();
            foreach(var r in moving){
                if(!r.Enabled)SwitchRow(r,true,null);
                var on=working.Where(x=>x.Enabled&&x.Id!=r.Id&&Shown(x)).ToList();int at=anchor==null?on.Count:on.FindIndex(x=>x.Id==anchor);PlaceActive(r.Id,at<0?on.Count:at);
            }
            selectedIds.Clear();foreach(var r in moving)selectedIds.Add(r.Id);primaryId=moving.Count>0?moving[0].Id:null;Render();
            if(skipped.Count>0)ShowNotice("info","켜지 못한 모드 "+skipped.Count+"개: "+String.Join(", ",skipped.Select(r=>r.Name))+" — ModInfo.json 에 ID(strIDEnd)가 없습니다.",null,null,null,null);
        }
        // While a card is held: the 켬 list always takes it (turn on / move), the 끔 list when it can be turned off, the cover below deletes.
        void ShowTargets(bool on){
            var r=on&&dragItem!=null?Row(dragItem.Id):null;
            dropOff=on&&dragSource==activeList&&r!=null&&(dragGroup!=null?dragGroup.Select(Row).Any(x=>x!=null&&(!x.Locked||Parks(x))):(!r.Locked||Parks(r)));
            Mark(activePanel,on,false);Mark(inactivePanel,dropOff,false);
        }
        static void Mark(Border panel,bool target,bool over){
            if(!target){panel.ClearValue(Border.BorderBrushProperty);panel.ClearValue(Border.BorderThicknessProperty);panel.ClearValue(Border.BackgroundProperty);panel.Padding=new Thickness(14,12,8,8);return;}
            panel.SetResourceReference(Border.BorderBrushProperty,over?"Accent":"AccentLine");panel.BorderThickness=new Thickness(2);panel.Padding=new Thickness(13,11,7,7);   // 1 px thicker, 1 px less padding: nothing moves
            if(over)panel.SetResourceReference(Border.BackgroundProperty,"AccentSoft");else panel.ClearValue(Border.BackgroundProperty);
        }
        void ClearDrop(){if(dropMarked!=null){dropMarked.DropBrush=Brushes.Transparent;dropMarked=null;}activeEnd.BorderBrush=endEdge;}

        // ---------- save / reserve ----------
        Dictionary<string,object> Choices(List<ModEntry> all){
            var rows=all.Where(r=>r.IsDll).ToList();
            var c=new Dictionary<string,object>();var add=new List<object>();
            foreach(var r in rows){
                c[r.Id]=r.Enabled;if(r.Route!="")c[r.Id+"_route"]=r.Route;var saved=catalog.Find(r.Id);
                if(saved==null)add.Add(new Dictionary<string,object>{{"Id",r.Id},{"Name",r.Name},{"Path",r.Path},{"Route",r.Route==""?"version.dll":r.Route}});
                else if(!SamePath(saved.Path,r.Path)&&!FixedPath.Contains(r.Id))c[r.Id+"_path"]=r.Path;
            }
            var ids=new HashSet<string>(rows.Select(r=>r.Id));
            c["_add"]=add;c["_remove"]=catalog.Rows.Where(s=>s.External&&!ids.Contains(s.Id)).Select(s=>s.Id).ToList();c["_order"]=OneList.DllOrder(savedDllOrder,rows.Select(r=>r.Id).ToList(),movedCards,new HashSet<string>(rows.Where(OneList.IsLinked).Select(r=>r.Id)));
            var data=all.Where(r=>r.IsData&&r.Suffix!="").ToList();var read=extraRows.Where(r=>r.IsData&&r.Suffix!="").ToList();
            // the game list's order, a linked card's data included: where the card stands is where its data is read
            Func<ModEntry,bool> listed=r=>OneList.IsLinked(r)&&gameOrder!=null&&gameOrder.Contains(r.Suffix);
            var seqNow=all.Where(r=>(r.IsData&&r.Suffix!="")||listed(r)).Select(r=>r.Suffix).Distinct().ToList();
            var readRows=read.Concat(catalog.Rows.Where(listed)).ToList();OneList.PlaceLinked(readRows,gameOrder);var seqRead=readRows.Select(r=>r.Suffix).Distinct().ToList();
            bool dataChanged=data.Any(r=>{var f=read.FirstOrDefault(x=>x.Id==r.Id);return f==null||f.Enabled!=r.Enabled;})
                ||!seqNow.SequenceEqual(seqRead);
            var patchNow=PatchOrderNow();var patchWanted=all.Where(r=>IsPatchRow(r)&&r.Enabled).Select(r=>r.Id.Substring(4)).ToList();
            // patchers moved between 켬 and 끔: off ones are undone (parked), on ones installed again where the list puts them
            var patchOff=all.Where(r=>Parks(r)&&!r.Enabled&&!r.Parked).Select(r=>r.Id.Substring(4)).ToList();var patchOn=all.Where(r=>Parks(r)&&r.Enabled&&r.Parked).Select(r=>r.Id.Substring(4)).ToList();
            // a whole card going off whose install the list cannot switch (a shared loader, or plugins of one): the install is set aside —
            // a shared part goes to the one needing it — and its DLL rows stay on here, for another install may still use them (빠른거래 ·
            // 삼혼비급탭, 11-02: with 삼혼비급탭 in first, the loader in 빠른거래's card belongs to 삼혼비급탭)
            foreach(var g in all.Where(r=>!Parks(r)).Select(r=>new{r,o=OwnerOf(r)}).Where(x=>x.o!=null&&x.o.Kind!="package").GroupBy(x=>x.o.Id)){
                if(g.Any(x=>x.r.Enabled)||!g.Any(x=>baseRows.Any(b=>b.Id==x.r.Id&&b.Enabled))||!SetsAside(g.Key))continue;
                if(!patchOff.Contains(g.Key))patchOff.Add(g.Key);foreach(var x in g)if(x.r.IsDll)c[x.r.Id]=true;   // it leaves the DLL list with its install
            }
            if(patchOff.Count>0||patchOn.Count>0){c["_patchOff"]=patchOff;c["_patchOn"]=patchOn;c["_patchOrder"]=patchWanted;}
            else if(patchWanted.Count>1&&!patchWanted.SequenceEqual(patchNow))c["_patchOrder"]=patchWanted;
            if(dataChanged){var map=new Dictionary<string,object>();foreach(var r in data)map[r.Suffix]=r.Enabled;c["_data"]=map;c["_dataOrder"]=seqNow;}
            return c;
        }
        string ChangeSummary(List<ModEntry> rows,List<ModEntry> reference){
            var byId=reference.ToDictionary(r=>r.Id);var on=new List<string>();var off=new List<string>();int other=0;
            foreach(var r in rows){ModEntry b;if(!byId.TryGetValue(r.Id,out b)){if(r.Enabled)on.Add(r.Name);else other++;}else if(r.Enabled!=b.Enabled)(r.Enabled?on:off).Add(r.Name);else if(Differs(r,b))other++;}
            var parts=new List<string>();if(on.Count>0)parts.Add("켜짐: "+String.Join(", ",on));if(off.Count>0)parts.Add("꺼짐: "+String.Join(", ",off));
            if(OrderChanged(rows,reference))parts.Add("순서 바꿈");if(other>0)parts.Add("그 밖 "+other+"개");
            int removed=reference.Count(b=>!rows.Any(r=>r.Id==b.Id));if(removed>0)parts.Add("등록 해제 "+removed+"개");
            return parts.Count==0?"변경 없음":String.Join(" · ",parts);
        }
        void Save(){
            if(busy||(!Dirty()&&!FirstSave()&&!EntryLost()))return;if(WatchBlocked())return;if(!catalog.Complete){ShowNotice("warn","목록을 아직 확인하는 중입니다. 잠시 뒤 다시 눌러 주세요.",null,null,null,null);return;}
            bool cards=Changes(working,baseRows)>0||FirstSave()||EntryLost();
            if(live.Game!=null&&subPending.Count>0)MessageBox.Show(Window,"올인원·상한해제 같은 패치 항목은 게임을 끈 뒤에 저장할 수 있습니다.","모드매니저");
            if(!ConfirmPatchWork(live.Game==null))return;   // item ticks run only with the game off
            if(!ConfirmPatchOrder()||!ConfirmMenuOrder())return;
            if(!cards){if(live.Game==null)ApplySubs(null);return;}
            var choices=Choices(working);
            if(live.Game!=null){
                try{Store.SavePending(game,choices,catalog.Revision,ChangeSummary(working,baseRows));}catch(Exception e){MessageBox.Show(Window,e.Message,"예약하지 못했습니다");return;}
                hints=working.Select(r=>r.Id).ToList();OneList.SaveHints(game,hints);
                pending=LoadPending();RebuildBase();working=baseRows.Select(r=>r.Clone()).ToList();movedCards.Clear();unregistered.Clear();Render();ShowPendingNotice();
                return;
            }
            hints=working.Select(r=>r.Id).ToList();OneList.SaveHints(game,hints);
            RunApply(choices,false,subPending.Count>0?(Action)(()=>ApplySubs(null)):null);
        }
        void RunApply(Dictionary<string,object> choices,bool fromPending,Action then=null){
            bool dataTouched=working.Any(r=>r.Enabled&&r.Data!=""&&(catalog.Find(r.Id)==null||!catalog.Find(r.Id).Enabled));
            RunBusy(fromPending?"예약한 변경을 적용하는 중…":"저장하는 중…",()=>Backend.Run("Apply",game,choices),result=>{
                if(pending!=null){try{Store.ClosePending(game,"적용");}catch{}pending=null;}
                unregistered.Clear();
                // The backend ticks data that came back into MODs in the game's own mod list (ModSettings).
                var ticked=J.Strings(J.Get(result,"Selection")).Select(s=>{var r=working.FirstOrDefault(x=>x.Suffix==s);return r==null?s:r.Name;}).ToList();
                RememberWaiting(result);
                // One-object ModInfo.json the backend just wrapped in [ ]: the game now sees a new mod and turns it off on its
                // first run, so the ones left on are revived like a fresh install.
                var repaired=J.Strings(J.Get(result,"Repaired"));var revive=repaired.Where(s=>working.Any(x=>x.Suffix==s&&x.Enabled)).ToList();
                if(revive.Count>0){Store.SetFresh(game,Store.Fresh(game).Concat(revive).Distinct(),Store.FirstRun(game),Store.FreshOrder(game));freshSet=new HashSet<string>(Store.Fresh(game));}
                string fixedNote=repaired.Count>0?" 게임이 읽지 못하던 ModInfo.json 을 고쳤습니다: "+String.Join(", ",repaired.Select(s=>{var r=working.FirstOrDefault(x=>x.Suffix==s);return r==null?s:r.Name;}))+"."
                    +(revive.Count>0?" 게임은 처음 보는 데이터 모드를 첫 실행 때 꺼 두므로, 게임을 한 번 켰다 끄면 모드매니저가 다시 켜 둡니다(그다음 실행부터 적용).":""):"";
                string note=J.S(result,"SelectionNote");var dataDone=J.Strings(J.Get(result,"Data"));string dataNote=J.S(result,"DataNote");
                if(dataNote!="")note=(note+" "+dataNote).Trim();else if(dataDone.Count>0)note=(note+" 데이터 모드 "+dataDone.Count+"개를 게임 모드 목록에서 켜고 껐습니다.").Trim();
                string data=ticked.Count>0?" 게임 모드 목록에도 체크해 두었습니다: "+String.Join(", ",ticked)+".":note!=""?" "+note:dataTouched?" 데이터가 있는 모드를 켰다면 게임 안 모드 목록에서도 켜 주세요.":"";
                ReloadCatalog(true);
                ShowNotice("ok",(fromPending?"예약한 변경을 적용했습니다. ":"저장했습니다. ")+"다음에 게임을 켤 때 반영됩니다."+data+fixedNote+String.Concat(J.Items(J.Get(result,"EntryMoved")).Select(m=>"\n다른 모드가 "+J.S(m,"From")+" 자리를 써서, 모드매니저 입구를 "+J.S(m,"To")+" 로 옮겼습니다. 따로 하실 일은 없습니다."))+"\n백업: "+J.S(result,"Backup"),null,null,null,null);
                var patchOrder=J.Strings(J.Get(choices,"_patchOrder"));var patchOff=J.Strings(J.Get(choices,"_patchOff"));var patchOn=J.Strings(J.Get(choices,"_patchOn"));
                if(patchOff.Count>0||patchOn.Count>0){Window.Dispatcher.BeginInvoke(new Action(()=>SwitchPatches(patchOff,patchOn,patchOrder,then)));return;}
                if(patchOrder.Count>0){Window.Dispatcher.BeginInvoke(new Action(()=>RebuildPatches(patchOrder,then)));return;}
                if(then!=null)then();
            },error=>{
                if(fromPending)ShowNotice("error","예약한 변경을 적용하지 못했습니다: "+error,"다시 시도",()=>ApplyPending(false),"예약 취소",CancelPending);
                else MessageBox.Show(Window,error,"저장하지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Warning);
            });
        }
        // ---------- start the game ----------
        // Steam install: steam://rungameid (as players normally start it). Other folders (test copies): the exe itself.
        void Launch(){
            if(busy||live.Game!=null)return;if(watchId!=""&&MessageBox.Show(Window,"'"+watchName+"' 수동 설치를 기록하는 중입니다. 게임이 켜져 있으면 '설치 끝'을 누를 수 없습니다. 그래도 켤까요?","모드매니저",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            if(pending!=null){var ch=J.Obj(J.Get(pending,"Choices"));if(ch!=null){RunApply(ch,true,StartGame);return;}}
            if(Dirty()){
                var answer=MessageBox.Show(Window,"저장 안 한 변경이 있습니다("+ChangeSummary(working,baseRows)+").\n\n확인: 저장한 뒤 게임을 켭니다.\n취소: 게임을 켜지 않습니다.","게임 실행",MessageBoxButton.OKCancel,MessageBoxImage.Question);
                if(answer!=MessageBoxResult.OK)return;
                if(!catalog.Complete){ShowNotice("warn","목록을 아직 확인하는 중입니다. 잠시 뒤 다시 눌러 주세요.",null,null,null,null);return;}
                hints=working.Select(r=>r.Id).ToList();OneList.SaveHints(game,hints);
                RunApply(Choices(working),false,StartGame);return;
            }
            StartGame();
        }
        void StartGame(){
            try{
                string steamapps=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(game).TrimEnd('\\')));
                if(steamapps!=null&&File.Exists(Path.Combine(steamapps,"appmanifest_3020510.acf"))){
                    Process.Start("steam://rungameid/3020510");
                    ShowNotice("info","Steam 으로 게임을 켜는 중입니다. 켜지면 위쪽 띠와 줄 색이 실행 중으로 바뀝니다.",null,null,null,null);
                }else{
                    Process.Start(new ProcessStartInfo(Path.Combine(game,"ThreeKingdom.exe")){WorkingDirectory=game,UseShellExecute=false});
                    ShowNotice("info","게임 실행 파일을 직접 켰습니다(Steam 설치 폴더가 아님).",null,null,null,null);
                }
                launchButton.IsEnabled=false;if(monitor!=null)monitor.Poke(false);
            }catch(Exception e){MessageBox.Show(Window,e.Message,"게임을 켜지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        void ApplyPending(bool automatic){
            if(pending==null)return;if(live.Game!=null){ShowPendingNotice();return;}
            if(automatic&&catalog.Complete&&J.S(pending,"Revision")!=catalog.Revision){
                ShowNotice("warn","예약한 뒤에 다른 곳(다른 모드매니저·설치기)에서 설정이 바뀌었습니다. 예약 내용을 확인한 뒤 적용해 주세요: "+J.S(pending,"Summary"),"그래도 적용",()=>ApplyPending(false),"예약 취소",CancelPending);return;
            }
            var ch=J.Obj(J.Get(pending,"Choices"));if(ch==null){CancelPending();return;}
            RunApply(ch,true);
        }
        void CancelPending(){
            try{Store.ClosePending(game,"취소");}catch(Exception e){MessageBox.Show(Window,e.Message,"예약을 취소하지 못했습니다");return;}
            pending=null;RebuildBase();working=baseRows.Select(r=>r.Clone()).ToList();movedCards.Clear();unregistered.Clear();
            ShowNotice("info","예약을 취소했습니다. 게임 파일은 바뀌지 않았습니다.",null,null,null,null);Render();
        }
        void ShowPendingNotice(){
            if(pending==null||!liveKnown||!catalog.Complete||busy)return;string summary=J.S(pending,"Summary");
            if(live.Game!=null)ShowNotice("warn","예약됨 — 게임을 끄면 이 모드매니저가 바로 적용합니다: "+summary+"\n모드매니저를 닫아 두면 다음에 열 때 묻습니다.",null,null,"예약 취소",CancelPending);
            else ShowNotice("warn","지난번에 예약한 변경이 있습니다: "+summary,"지금 적용",()=>ApplyPending(false),"예약 취소",CancelPending);
        }
        void OnClosing(object sender,CancelEventArgs e){
            if(busy){e.Cancel=true;return;}
            if(Dirty()&&MessageBox.Show(Window,"저장 안 한 변경이 있습니다. 저장하지 않고 닫을까요?","모드매니저",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){e.Cancel=true;return;}
            if(pending!=null&&live.Game!=null&&MessageBox.Show(Window,"예약한 변경은 모드매니저가 열려 있어야 게임을 끌 때 바로 적용됩니다.\n지금 닫으면 다음에 모드매니저를 열 때 물어봅니다. 닫을까요?","모드매니저",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;
        }

        // ---------- busy work and notices ----------
        // Work always goes to the thread pool (Task.Run): Task.Factory.StartNew inside a UI continuation ran on the UI thread
        // and froze the window during long installs. A clock under the label shows it is still going.
        DispatcherTimer busyClock;DateTime busyStart;string busyLabel;
        void RunBusy<T>(string label,Func<T> work,Action<T> done,Action<string> failed){
            if(busy)return;busy=true;busyLabel=label;busyStart=DateTime.Now;busyText.Text=label;busyLayer.Visibility=Visibility.Visible;RenderBand();
            if(busyClock==null){busyClock=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};busyClock.Tick+=(s,e)=>{var spent=DateTime.Now-busyStart;if(spent.TotalSeconds>=3)busyText.Text=busyLabel+ProgressLine()+"\n지난 시간 "+(int)spent.TotalMinutes+":"+spent.Seconds.ToString("00");};}
            try{File.Delete(ProgressFile());}catch{}   // a line left by a job that stopped half way is not this one's
            busyClock.Start();
            Task.Run(work).ContinueWith(t=>{
                busy=false;busyClock.Stop();busyLayer.Visibility=Visibility.Collapsed;
                if(t.IsFaulted){string m=Message(t.Exception);if(failed!=null)failed(m);else MessageBox.Show(Window,m,"처리하지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Warning);}
                else done(t.Result);
                Render();
            },TaskScheduler.FromCurrentSynchronizationContext());
        }
        // a long job's own line (the backend's Write-MMProgress: which place it is trying now)
        string ProgressFile(){return Path.Combine(game??"","LOH_ModManager","Work","progress.txt");}
        string ProgressLine(){try{var f=ProgressFile();if(File.Exists(f)){var s=File.ReadAllText(f).Trim();if(s!="")return "\n"+s;}}catch{}return "";}
        static string Message(AggregateException e){var x=e==null?null:e.Flatten().InnerExceptions.FirstOrDefault();return x==null?"알 수 없는 오류":x.Message;}
        void ShowNotice(string kind,string text,string first,Action firstAction,string second,Action secondAction){
            var keys=new Dictionary<string,string[]>{{"ok",new[]{"OkSoft","OkLine"}},{"warn",new[]{"WarnSoft","WarnLine"}},{"error",new[]{"BadSoft","BadLine"}},{"info",new[]{"AccentSoft","AccentLine"}}};
            notice.SetResourceReference(Border.BackgroundProperty,keys[kind][0]);notice.SetResourceReference(Border.BorderBrushProperty,keys[kind][1]);noticeText.Text=text;noticeHiding=false;notice.BeginAnimation(UIElement.OpacityProperty,null);notice.Visibility=Visibility.Visible;
            noticeFirst=firstAction;noticeSecond=secondAction;
            noticeAction1.Content=first;noticeAction1.Visibility=first==null?Visibility.Collapsed:Visibility.Visible;
            noticeAction2.Content=second;noticeAction2.Visibility=second==null?Visibility.Collapsed:Visibility.Visible;
        }
        bool noticeHiding;   // fading out: counts as gone
        void HideNotice(){noticeHiding=true;Motion.FadeOut(notice,()=>noticeHiding,()=>notice.Visibility=Visibility.Collapsed);noticeFirst=null;noticeSecond=null;if(watchId!="")Window.Dispatcher.BeginInvoke(new Action(()=>{if(watchId!=""&&(notice.Visibility!=Visibility.Visible||noticeHiding))ShowWatchNotice();}));}

        // ---------- external DLL, removal, restore ----------
        void AddExternal(string path){
            RunBusy("DLL 형식 확인 중…",()=>Backend.Run("InspectFile",game,new Dictionary<string,object>{{"Path",path}}),inspected=>{
                if(!J.B(inspected,"CanRegister")){
                    // hid/version/winmm-named mods (아이템잠금 등): the install path absorbs them under another name (10-31 디시)
                    // (not one already sitting in the game folder under its own name: copying it would load it twice)
                    if(J.S(inspected,"Type")=="입구·프록시 DLL"&&SamePath(Path.GetDirectoryName(J.S(inspected,"Path")),game.TrimEnd('\\'))){
                        var f=catalog.Foreign.FirstOrDefault(x=>String.Equals(J.S(x,"Name"),Path.GetFileName(path),StringComparison.OrdinalIgnoreCase));
                        if(f!=null&&MessageBox.Show(Window,ForeignText(f)+"\n\n모드매니저로 가져올까요? (다른 이름으로 바꿔 두고 모드매니저가 불러옵니다. 목록에서 켜고 끌 수 있습니다.)","게임 폴더 DLL",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes){AdoptForeign(J.S(f,"Name"));return;}
                        if(f==null)MessageBox.Show(Window,"이미 게임 폴더에 "+Path.GetFileName(path)+" 이름으로 들어가 있는 DLL 입니다(모드매니저 입구이거나 모드매니저가 넣은 파일).","게임 폴더 DLL");
                        return;}
                    if(J.S(inspected,"Type")=="입구·프록시 DLL"&&MessageBox.Show(Window,Path.GetFileName(path)+" 은 게임 폴더에 그 이름 그대로 두는 DLL 모드입니다.\n모드매니저가 함께 실행하도록 설치할까요? (다른 이름으로 넣어 두고 모드매니저가 불러옵니다. 같은 이름을 쓰는 다른 모드와 함께 쓸 수 있습니다.)","게임 폴더 DLL",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes){QueueInstall(new[]{J.S(inspected,"Path")});return;}
                    if(J.S(inspected,"Type")!="입구·프록시 DLL")MessageBox.Show(Window,J.S(inspected,"Evidence"),J.S(inspected,"Type"));return;
                }
                string full=J.S(inspected,"Path");if(working.Any(r=>SamePath(r.Path,full))){MessageBox.Show(Window,"이미 목록에 있는 DLL입니다.");return;}
                string name=Dialogs.Ask(Window,"DLL 파일 추가","목록에 보일 이름을 정해 주세요 (64비트 DLL 확인됨 · 게임에서 잘 되는지는 직접 확인해야 합니다)",J.S(inspected,"Name"),false);
                if(String.IsNullOrWhiteSpace(name))return;
                var e=new ModEntry{Id="ext_"+Guid.NewGuid().ToString("N"),Name=name.Trim(),Path=full,Route="version.dll",External=true,Present=true,Version=J.S(inspected,"Version")};
                working.Add(e);selectedId=e.Id;if(monitor!=null)monitor.SetRows(AllRows());ShowTab(0);
                ShowNotice("info","'"+e.Name+"'을 오른쪽(꺼짐) 목록에 추가했습니다. 켜려면 두 번 클릭한 뒤 저장하세요. DLL 파일은 복사하지 않고 그 자리에서 불러오니, 파일을 옮기거나 지우지 마세요.",null,null,null,null);
            },null);
        }
        void RemoveInstalled(string key,string name){
            if(Dirty()){MessageBox.Show(Window,"먼저 지금 변경을 저장해 주세요. 저장한 뒤에 고른 모드만 제거하거나 복원합니다.","모드매니저");return;}
            if(key==""){MessageBox.Show(Window,"'DLL 파일 추가'로 넣은 모드는 '고급 설정 → 목록에서 빼기'를 쓰세요. DLL 파일은 남겨 둡니다.","제거 방법 안내");return;}
            if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 제거할 수 있습니다. 실행 중 DLL은 빼지 않습니다.","모드매니저");return;}
            RunBusy("제거 계획 확인 중…",()=>Backend.Run("RemovalPlan",game,new Dictionary<string,object>{{"Key",key}}),plan=>{
                var lines=new List<string>{"제거 대상: "+J.S(plan,"Name"),""};
                foreach(var t in J.Items(J.Get(plan,"Targets")))lines.Add(J.S(t,"Original"));
                lines.Add("");lines.Add(J.S(plan,"Note"));lines.Add("");lines.Add("지운 파일은 보관함으로 옮겨 두므로 '설치 정보' 탭의 '지운 모드 되살리기'로 되돌릴 수 있습니다.");
                if(!Dialogs.Confirm(Window,"선택 모드 제거 확인",String.Join(Environment.NewLine,lines),"백업 후 제거"))return;
                RunBusy("제거하는 중…",()=>Backend.Run("Remove",game,new Dictionary<string,object>{{"Key",key},{"Token",J.S(plan,"Token")}}),result=>{
                    selectedId=null;ReloadCatalog(true);if(inventory!=null)ScanInventory();
                    ShowNotice("ok",J.S(result,"Name")+"을 설치 목록에서 제거했습니다. 복원용 백업: "+J.S(result,"Backup"),null,null,null,null);
                },null);
            },null);
        }
        void RestoreInstalled(){
            if(Dirty()){MessageBox.Show(Window,"먼저 지금 변경을 저장해 주세요.","모드매니저");return;}
            if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 복원할 수 있습니다.","모드매니저");return;}
            RunBusy("제거 기록 읽는 중…",()=>Backend.Run("Removed",game,null),result=>{
                var records=J.Items(J.Get(result,"Items")).ToList();if(records.Count==0){MessageBox.Show(Window,"되살릴 수 있는 지운 모드가 없습니다.","모드매니저");return;}
                int pick=Dialogs.Choose(Window,"지운 모드 되살리기",records.Select(r=>J.S(r,"Name")+"  /  "+J.S(r,"Date")+"  /  "+J.S(r,"State")).ToList(),
                    i=>String.Join(Environment.NewLine,J.Strings(J.Get(records[i],"Paths")).Concat(new[]{J.S(records[i],"Note")})),"선택 복원");
                if(pick<0)return;string id=J.S(records[pick],"Id"),kind=J.S(records[pick],"Kind");
                RunBusy("복원하는 중…",()=>Backend.Run("Restore",game,new Dictionary<string,object>{{"Id",id},{"Kind",kind}}),restored=>{
                    ReloadCatalog(true);if(inventory!=null)ScanInventory();if(packagesLoaded)LoadPackages();
                    var lost=J.Strings(J.Get(restored,"Missing"));string note=J.S(restored,"Note");
                    ShowNotice(lost.Count>0?"warn":"ok",J.S(restored,"Name")+"을 되살렸습니다. 다음에 게임을 켤 때부터 적용됩니다."+(lost.Count>0?" 보관함에 없던 파일 "+lost.Count+"개는 빠졌습니다.":"")+(note!=""?" "+note:""),null,null,null,null);
                },null);
            },null);
        }

        // ---------- inventory and data mods (only when asked) ----------
        void ShowTab(int tab){
            tabMods.IsChecked=tab==0;tabPackages.IsChecked=tab==1;tabInventory.IsChecked=tab==2;
            modsView.Visibility=tab==0?Visibility.Visible:Visibility.Collapsed;packagesView.Visibility=tab==1?Visibility.Visible:Visibility.Collapsed;inventoryView.Visibility=tab==2?Visibility.Visible:Visibility.Collapsed;
            if(tab==1&&!packagesLoaded)LoadPackages();
            if(tab==2&&inventory==null)ScanInventory();
        }
        void ScanInventory(){
            RunBusy("전체 목록을 만드는 중… (게임 폴더 전체를 읽어 몇 초 걸립니다)",()=>Backend.Run("Scan",game,null),result=>{
                inventory=J.Items(J.Get(result,"Items")).Select(d=>new InventoryRow{Name=J.S(d,"Name"),Source=J.S(d,"Source"),Type=J.S(d,"Type"),Method=J.S(d,"Method"),Entry=J.S(d,"Entry"),State=J.S(d,"State"),
                    Evidence=J.S(d,"Evidence"),Path=J.S(d,"Path"),Key=J.S(d,"Key"),RemoveHint=J.S(d,"RemoveHint"),CanRegister=J.B(d,"CanRegister"),CanRemove=J.B(d,"CanRemove")}).ToList();
                inventoryGrid.ItemsSource=inventory;inventoryDetail.Text=J.S(result,"Note");
            },null);
        }
        void ScanDataMods(){
            var linked=new HashSet<string>(working.Where(r=>r.Data!="").Select(r=>r.Data),StringComparer.OrdinalIgnoreCase);string folder=game;
            Task.Run(()=>DataMods.Scan(folder,linked)).ContinueWith(t=>{
                if(t.IsFaulted||folder!=game)return;
                dataList.ItemsSource=t.Result.Select(m=>new DataModView{Name=m.Name,SourceText=m.Source+" · "+m.Folder,
                    SelectionText=m.Selected==null?"게임 선택 기록 없음":m.Selected.Value?"게임에서 켜짐":"게임에서 꺼짐",SelectionBrush=m.Selected==true?Look.Ok:Look.Dim}).ToList();
            },TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------- external mod packages: drop → preview with checkboxes → install with a record → exact removal ----------
        // Install, removal and game-list ticks change files, so they need the game off and no unsaved list edits
        // (a DLL part registers itself in the catalog, which reloads the lists).
        bool WatchBlocked(){
            if(watchId=="")return false;
            MessageBox.Show(Window,"'"+watchName+"' 수동 설치를 기록하는 중입니다. 그동안 모드매니저가 게임 파일을 바꾸면 그 모드의 파일로 함께 기록되니, 먼저 위쪽 안내의 '설치 끝' 또는 '기록 취소'를 눌러 주세요.","모드매니저");
            return true;
        }
        bool PackageBlocked(string what,bool keepEdits=false){   // keepEdits: removing works on what is saved; the reload after it keeps the unsaved edits
            if(WatchBlocked())return true;
            if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 "+what+" 수 있습니다. 실행 중에는 게임 파일을 바꾸지 않습니다.","모드매니저");return true;}
            if(!keepEdits&&Dirty()){MessageBox.Show(Window,"저장하지 않은 변경이 있습니다. 먼저 저장하거나 '되돌리기'를 누른 뒤 "+what+" 수 있습니다.","모드매니저");return true;}
            return false;
        }
        // ---------- files dragged in from Explorer (11-02) ----------
        // Two big drop boxes cover the 켬 and 끔 lists (like the trash zone): 켬 puts the new mod on at the bottom of the load order,
        // 끔 puts it in switched off at the bottom of that list. Dropped elsewhere it lands where installs land. The rows move once
        // the install is done (unsaved, like a card moved by hand).
        sealed class DropPlace { public bool Off;public string Before;public List<string> Ids=new List<string>();public int Tries; }
        DropPlace installPlace,placeAfterReload;List<string> revealIds;bool fileOverActive,fileOverInactive;Border dropOnBox,dropOffBox;
        void FileDragTrack(DragEventArgs e){
            var p=e.GetPosition(dragLayer);fileOverActive=Over(activePanel,p);fileOverInactive=!fileOverActive&&Over(inactivePanel,p);
            foreach(var t in new[]{Tuple.Create(dropOnBox,activePanel,fileOverActive),Tuple.Create(dropOffBox,inactivePanel,fileOverInactive)}){
                var box=t.Item1;var panel=t.Item2;box.Visibility=panel.IsVisible?Visibility.Visible:Visibility.Collapsed;if(!panel.IsVisible)continue;
                var at=panel.TranslatePoint(new Point(0,0),dropLayer);Canvas.SetLeft(box,at.X);Canvas.SetTop(box,at.Y);box.Width=panel.ActualWidth;box.Height=panel.ActualHeight;
                box.BorderThickness=new Thickness(t.Item3?4:2);box.Opacity=t.Item3?1:0.82;
            }
        }
        void FileDragEnd(){dropLayer.Visibility=Visibility.Collapsed;}
        // the rows of the just-installed record(s), moved to where they were dropped; false while the list does not show them yet
        List<string> PlaceNew(DropPlace p){
            var ids=new HashSet<string>(p.Ids);
            var mine=working.Where(r=>{if(r.Id.StartsWith("pkg:")&&ids.Contains(r.Id.Substring(4)))return true;var o=OwnerOf(r);return o!=null&&ids.Contains(o.Id);}).ToList();
            if(mine.Count==0)return null;
            foreach(var r in mine){working.Remove(r);if(!r.Locked)r.Enabled=!p.Off;}
            int at=p.Off||p.Before==null?-1:working.FindIndex(r=>r.Id==p.Before);
            if(at>=0)working.InsertRange(at,mine);else working.AddRange(mine);
            if(monitor!=null)monitor.SetRows(AllRows());
            return mine.Select(r=>r.Id).ToList();
        }
        void QueueInstall(IEnumerable<string> paths,DropPlace place=null){
            if(busy||installing)return;
            if(PackageBlocked("설치할",true))return;   // like removing: the reload after it keeps unsaved edits
            installPlace=place;foreach(var p in paths)installQueue.Enqueue(p);
            NextInstall();
        }
        void NextInstall(){
            if(installQueue.Count==0){installing=false;installPlace=null;return;}
            installing=true;AnalyzeNext(installQueue.Dequeue(),"");
        }
        // a zip with a password: the window asks (the mod's post gives it) and tries again; a wrong one asks again, 취소 skips the archive.
        // Nothing is tried on the player's behalf and nothing is kept (user decision 11-01).
        void AnalyzeNext(string path,string password){
            string replacing;replaceFor.TryGetValue(path,out replacing);   // an update: the kind in use is ticked
            RunBusy("압축을 풀어 살펴보는 중… "+Path.GetFileName(path.TrimEnd('\\')),()=>Backend.Run("Analyze",game,new Dictionary<string,object>{{"Path",path},{"Replaces",replacing??""},{"Password",password}}),plan=>{
                string stage=J.S(plan,"StageId");
                var choice=Dialogs.InstallPlan(Window,plan,Path.Combine(Store.Folder(game),"Staging",stage,"content"),Version);
                if(choice!=null&&choice.Manual){installQueue.Clear();installing=false;StartWatch(J.S(plan,"Name"),J.S(plan,"Source"),stage);return;}
                if(choice==null||choice.Keys.Count==0){DiscardStage(stage);NextInstall();return;}
                var keys=choice.Keys;bool patcher=J.S(plan,"Kind")=="AllInOne";
                if(live.Game!=null){MessageBox.Show(Window,"게임이 켜져 있어 설치하지 않았습니다. 게임을 끈 뒤 다시 드래그해 주세요.","모드매니저");DiscardStage(stage);NextInstall();return;}
                string oldRecord;replaceFor.TryGetValue(path,out oldRecord);replaceFor.Remove(path);
                if(String.IsNullOrEmpty(oldRecord))oldRecord=J.Items(J.Get(plan,"Components")).Where(c=>keys.Contains(J.S(c,"Key"))).Select(c=>J.S(c,"Replaces")).FirstOrDefault(x=>x!="");   // another version of this patch: '설치' replaces it
                string[] versions;replaceVersions.TryGetValue(path,out versions);replaceVersions.Remove(path);string folder0=game;
                Action<string> notInstalled=error=>{MessageBox.Show(Window,error,"설치하지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Warning);DiscardStage(stage);NextInstall();};
                Action<Dictionary<string,object>> installed=null;
                installed=result=>{
                    // a 가이드 patch that did not go in at the bottom: say why and what happens, then try the places (each a rebuild)
                    if(J.B(result,"NeedPlace")){
                        string pn=J.S(result,"Name");var places=J.Strings(J.Get(result,"Places"));
                        string msg="'"+pn+"'"+Particle(pn,"이","가")+" 목록 맨 아래(지금 게임 파일 위)에서는 들어가지 않았습니다.\n패치가 남긴 말: "+J.S(result,"Tail")
                            +"\n\n왜 이런가요?\n이 패치는 지금 게임 파일(다른 패치가 모두 입혀진 상태) 위에서는 스스로 멈췄습니다. 이유는 위 '패치가 남긴 말'에 있습니다. 다른 패치보다 먼저 입혀지면 들어갈 수 있는 경우가 많아, 아래 자리에 차례로 넣어 봅니다. 사람이 하려면 뒤의 패치를 모두 원래대로 되돌리고, 이 패치를 넣고, 뒤의 패치를 다시 넣어야 하는 일입니다."
                            +"\n\n모드매니저가 할 일\n1. 아래 자리에 차례로 넣어 보며 원본에서 다시 쌓습니다(자리마다 한 번).\n"+String.Join("\n",places.Select(p=>"   · '"+p+"' 바로 위"))
                            +"\n2. 들어가는 자리를 찾으면 거기서 멈추고, 그 순서가 목록에 남습니다.\n3. 어느 자리에도 안 들어가면 다른 패치는 원래대로 다시 쌓고, 패치가 남긴 말을 보여 드립니다."
                            +"\n\n예상 시간: 최대 약 "+J.S(result,"Minutes")+"분 (지난번 다시 쌓기 기록 기준)\n그동안 게임을 켜지 마세요. '취소'를 누르면 아무것도 바뀌지 않습니다.";
                        if(!Dialogs.Confirm(Window,"패치 자리 찾기",msg,"자리 찾기 시작")){DiscardStage(stage);NextInstall();return;}
                        var placing=new Dictionary<string,object>(choice.Options);placing["guide_place"]=true;placing["guide_tail"]=J.S(result,"Tail");
                        RunBusy("패치 자리 찾는 중…",()=>InstallReplacing(folder0,oldRecord,versions,new Dictionary<string,object>{{"StageId",stage},{"Keys",keys},{"Options",placing}}),installed,notInstalled);
                        return;
                    }
                    var ticked=J.Strings(J.Get(result,"Selection"));string note=J.S(result,"SelectionNote");
                    if(ticked.Count>0){Store.SetFresh(game,Store.Fresh(game).Concat(ticked),Store.FirstRun(game),Store.FreshOrder(game));freshSet=new HashSet<string>(Store.Fresh(game));}
                    RememberWaiting(result);
                    bool dll=J.Items(J.Get(plan,"Components")).Any(c=>keys.Contains(J.S(c,"Key"))&&J.S(c,"Kind")=="ExternalDll")&&J.Strings(J.Get(result,"DllOff")).Count==0;   // one put in switched off (a DLL of that name is on) is not said to be on
                    selectedPackage=J.S(result,"Id");
                    if(installPlace!=null){var ids=J.Strings(J.Get(result,"Ids"));if(ids.Count==0)ids=new List<string>{J.S(result,"Id")};placeAfterReload=new DropPlace{Off=installPlace.Off,Before=installPlace.Before,Ids=ids};}
                    ReloadCatalog(false);LoadPackages();if(dataToggle.IsChecked==true)ScanDataMods();
                    var blockedParts=J.Strings(J.Get(result,"Blocked"));
                    if(blockedParts.Count>0){   // installed, but some of it could not go in with another patcher: say that first, in the warning colour
                        ShowNotice("warn","'"+J.S(result,"Name")+"' 설치했지만 함께 쓸 수 없어 넣지 못한 항목이 있습니다:\n· "+String.Join("\n· ",blockedParts)+"\n나머지는 들어갔습니다. 이 내용은 '설치 정보' 탭과 위쪽 알림에도 남습니다. 다음에 게임을 켤 때 반영됩니다.",null,null,null,null);
                        NextInstall();return;
                    }
                    ShowNotice("ok","'"+J.S(result,"Name")+"' 설치: "+String.Join(", ",J.Strings(J.Get(result,"Parts")))+" (파일 "+J.S(result,"Files")+"개)."
                        +(ticked.Count>0?" 게임은 새 데이터 모드를 첫 실행 때 꺼 두므로, 게임을 한 번 켰다 끄면 모드매니저가 다시 켜 둡니다(그다음 실행부터 적용). 바로 쓰려면 게임 안 모드 목록에서 켜세요.":note!=""?" "+note:"")
                        +(dll?" DLL 은 '모드 관리' 탭 왼쪽(적용)에 켜진 채로 들어갔습니다.":"")+(J.S(result,"Note")!=""?" "+J.S(result,"Note"):"")+" 다음에 게임을 켤 때 반영됩니다.",null,null,null,null);
                    NextInstall();
                };
                RunBusy("설치하는 중… "+J.S(plan,"Name")+(patcher?"\n(패치가 게임 파일을 백업하고 고치는 중 — 몇 분 걸릴 수 있습니다)":""),()=>InstallReplacing(folder0,oldRecord,versions,new Dictionary<string,object>{{"StageId",stage},{"Keys",keys},{"Options",choice.Options}}),installed,notInstalled);
            },error=>{
                bool wrong=error.Contains("LOH_MM_PASSWORD_WRONG");
                if(wrong||error.Contains("LOH_MM_PASSWORD_NEEDED")){
                    string typed=Dialogs.Ask(Window,"압축 비밀번호",wrong?"비밀번호가 맞지 않습니다. 모드 게시글에 적힌 비밀번호를 다시 넣어 주세요.":"'"+Path.GetFileName(path.TrimEnd('\\'))+"'에 비밀번호가 걸려 있습니다. 모드 게시글에 적힌 비밀번호를 넣어 주세요.","",false);
                    if(typed==null){NextInstall();return;}   // 취소: this archive is skipped
                    AnalyzeNext(path,typed);return;
                }
                MessageBox.Show(Window,Path.GetFileName(path.TrimEnd('\\'))+"\n\n"+error,"살펴보지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Warning);NextInstall();});
        }
        // an update: the old version out first, the new one in; if that fails, the old one back
        static Dictionary<string,object> InstallReplacing(string game,string oldRecord,string[] versions,Dictionary<string,object> request){
            if(String.IsNullOrEmpty(oldRecord))return Backend.Run("InstallPackage",game,request);
            var missing=J.Strings(J.Get(Backend.Run("StageMissing",game,new Dictionary<string,object>{{"StageId",request["StageId"]}}),"Missing"));   // before the old one comes out: nothing touched when the new one cannot go in
            if(missing.Count>0)throw new Exception("새 버전을 넣지 못해서 이전 버전을 그대로 두었습니다. 이유: 압축에서 풀어 둔 파일 '"+String.Join("', '",missing)+"' 이 사라졌습니다. Windows 보안이 격리했을 수 있습니다.");
            Backend.Run("UninstallPackage",game,new Dictionary<string,object>{{"Id",oldRecord}});
            request["Replaces"]=oldRecord;   // the settings the player changed come along
            Dictionary<string,object> result;
            try{result=Backend.Run("InstallPackage",game,request);}
            catch(Exception e){try{Backend.Run("Restore",game,new Dictionary<string,object>{{"Kind","package"},{"Id",oldRecord}});}catch(Exception back){throw new Exception("새 버전을 넣지 못했고 이전 버전도 되돌리지 못했습니다. 이전 버전은 '지운 모드' 에서 되살릴 수 있습니다. 이유: "+e.Message+" / "+back.Message);}throw new Exception("새 버전을 넣지 못해서 이전 버전을 그대로 두었습니다. 이유: "+e.Message);}
            if(J.B(result,"NeedPlace")){try{Backend.Run("Restore",game,new Dictionary<string,object>{{"Kind","package"},{"Id",oldRecord}});}catch{}return result;}   // the window asks first; the try takes the old one out again
            if(versions!=null)try{Backend.Run("ReplaceNote",game,new Dictionary<string,object>{{"Id",J.S(result,"Id")},{"Old",oldRecord},{"Version",versions!=null?versions[0]:""},{"NewVersion",versions!=null?versions[1]:""}});}catch{}   // for '이전 버전으로 되돌리기'
            return result;
        }
        void DiscardStage(string stage){if(stage=="")return;string folder=game;Task.Run(()=>{try{Backend.Run("DiscardStage",folder,new Dictionary<string,object>{{"StageId",stage}});}catch{}});}
        static readonly Dictionary<string,string> PartKind=new Dictionary<string,string>{{"Data","데이터 모드"},{"Overlay","게임 파일 교체"},{"ProxyDll","게임 폴더 DLL"},{"ExternalDll","DLL 모드"},{"Patcher","패치"},{"HostPlugin","공통 로더 기능"},{"AbsorbedDll","모드매니저 실행 DLL"},{"ScriptPatch","패치(스크립트)"},{"FilePatch","패치(게임 파일)"},{"Link","게임 연결 정보"},{"Recorded","수동 설치(기록)"},{"WorkshopPatch","창작마당 하위 패치"},{"SelfInstall","모드"}};
        public static string PartLabel(string kind){string s;return PartKind.TryGetValue(kind,out s)?s:kind;}
        void LoadPackages(){
            string folder=game;packagesLoaded=true;
            Task.Run(()=>Backend.Run("Installed",folder,null)).ContinueWith(t=>{
                if(folder!=game)return;
                if(t.IsFaulted){packageList.ItemsSource=null;packagesEmpty.Visibility=Visibility.Visible;packagesEmpty.Text="설치 정보를 읽지 못했습니다: "+Message(t.Exception);ShowPackage();return;}
                var views=new List<PackageView>();bool patchers=false;
                foreach(var d in J.Items(J.Get(t.Result,"Items"))){
                    var parts=J.Items(J.Get(d,"Parts")).ToList();var data=parts.Where(p=>J.S(p,"Kind")=="Data"&&J.S(p,"Suffix")!="").ToList();
                    int missing;int.TryParse(J.S(d,"Missing"),out missing);var conflicts=J.Strings(J.Get(d,"Conflicts"));
                    Badge badge;string text,tip;
                    int undone=parts.Where(p=>J.S(p,"Kind")=="WorkshopPatch").Sum(p=>Int(J.Get(p,"Changed")));
                    if(missing>0){badge=Badge.Error;text="파일 빠짐 "+missing;tip="설치할 때 넣은 파일 중 "+missing+"개가 지금 없습니다. 다시 설치하거나 제거하세요.";}
                    else if(undone>0){badge=Badge.Waiting;text="풀림 "+undone;tip="창작마당 원본이 업데이트되어 패치가 풀렸습니다. '모드 관리' 탭에서 원본 카드를 고르고 '다시 적용'을 누르세요.";}
                    else if(data.Count>0&&data.All(p=>J.Get(p,"GameListOn") is bool&&!(bool)J.Get(p,"GameListOn"))){badge=Badge.Off;text="꺼 둠";tip="설치되어 있지만 '모드 관리' 탭에서 꺼 둔 상태입니다.";}
                    else{badge=Badge.Running;text="설치됨";tip="설치되어 있습니다. 켜고 끄기와 순서는 '모드 관리' 탭에서 정합니다.";}
                    patchers|=parts.Any(x=>J.S(x,"Kind")=="Patcher"||J.S(x,"Kind")=="ScriptPatch");
                    DateTime when;string date=DateTime.TryParse(J.S(d,"Installed"),out when)?when.ToString("MM-dd HH:mm"):"";
                    views.Add(new PackageView{Id=J.S(d,"Id"),Name=J.S(d,"Name"),BadgeText=text,BadgeTip=tip,BadgeBrush=Look.Back(badge),BadgeFore=Look.Fore(badge),Data=d,
                        Summary=String.Join(" · ",parts.Select(p=>PartLabel(J.S(p,"Kind"))).Distinct())+(date==""?"":" · "+date+" 설치"),
                        Warning=conflicts.Count>0?"다른 모드와 같은 파일 "+conflicts.Count+"개를 씀 — 나중에 설치한 쪽이 적용됩니다":""});
                }
                packageList.ItemsSource=views;packagesEmpty.Text="모드매니저로 설치한 모드가 없습니다.\n모드 압축 파일을 이 창으로 드래그해 보세요.";
                patchRebuild.Visibility=patchers?Visibility.Visible:Visibility.Collapsed;   // only someone with a patcher ever needs it
                packagesEmpty.Visibility=views.Count==0?Visibility.Visible:Visibility.Collapsed;
                var keep=views.FirstOrDefault(v=>v.Id==selectedPackage)??views.FirstOrDefault();packageList.SelectedItem=keep;selectedPackage=keep==null?null:keep.Id;ShowPackage();
            },TaskScheduler.FromCurrentSynchronizationContext());
        }
        PackageView SelectedPackage(){return packageList.SelectedItem as PackageView;}
        void ShowPackage(){
            var p=SelectedPackage();
            packageUninstall.IsEnabled=false;
            if(p==null){packageTitle.Text="목록에서 모드를 고르면 자세한 내용과 설명서가 여기에 나옵니다.";packageInfo.Text="";packageDocName.Text="";packageDoc.Text="";return;}
            var d=p.Data;var lines=new List<string>();bool hasData=false;
            DateTime when;lines.Add("설치: "+(DateTime.TryParse(J.S(d,"Installed"),out when)?when.ToString("yyyy-MM-dd HH:mm"):J.S(d,"Installed"))+" · 원본: "+J.S(d,"Source"));
            foreach(var part in J.Items(J.Get(d,"Parts"))){
                string kind=J.S(part,"Kind"),title=J.S(part,"Title"),files=J.S(part,"Files");object on=J.Get(part,"GameListOn");
                if(kind=="Data"){
                    hasData|=J.S(part,"Suffix")!="";
                    lines.Add("· 데이터 모드 '"+title+"' — 파일 "+files+"개 · 게임 목록: "+(on is bool?((bool)on?"켜짐":"꺼짐"):J.S(part,"Suffix")==""?"ID 없음(게임 안 목록에서 켜기)":"기록 없음(게임을 한 번 켜면 생김)"));
                }
                else if(kind=="Recorded"){lines.Add("· 수동 설치(기록) — 파일 "+files+"개. 설치기를 직접 실행해 넣은 것을 설치 전후로 비교해 기록했습니다. 제거하면 기록대로 빼고 백업해 둔 파일은 되돌립니다.");}
                else if(kind=="Overlay")lines.Add("· 게임 파일 교체 — 파일 "+files+"개 (덮인 원본은 백업, 제거하면 되돌림)");
                else if(kind=="ProxyDll")lines.Add("· 게임 폴더 DLL "+title+" — 게임 폴더에 그 이름 그대로 두는 모드(늘 켜짐, 끄려면 제거)");
                else if(kind=="ExternalDll")lines.Add("· DLL 모드 "+title+" — '모드 관리' 탭에서 켜고 끄고 순서를 정합니다");
                else if(kind=="AbsorbedDll")lines.Add("· 모드매니저 실행 DLL "+title+" — 모드매니저가 함께 실행합니다('모드 관리' 탭에서 켜고 끔)");
                else if(kind=="Link")lines.Add("· "+title+" — 설치할 때 지금 게임 파일로 만든 연결 정보(패치를 다시 적용하거나 게임이 업데이트되면 모드매니저가 다시 만듦)");
                else if(kind=="HostPlugin"){hasData|=J.S(part,"Suffix")!="";lines.Add("· 공통 로더 기능 '"+title+"' — 파일 "+files+"개"+(J.S(part,"Suffix")!=""?" · 게임 목록: "+(on is bool?((bool)on?"켜짐":"꺼짐"):"기록 없음(게임을 한 번 켜면 생김)"):""));}
                else if(kind=="Patcher"){
                    lines.Add("· 패치 "+title+" — "+J.S(part,"Summary")+" (게임 파일 "+files+"개를 직접 고침, 제거하면 패치의 「원래대로」)");
                    var failed=J.Strings(J.Get(part,"Failed"));if(failed.Count>0)lines.Add("⚠ 패치가 실패한 항목: "+String.Join(", ",failed));
                }
                else if(kind=="ScriptPatch"){hasData|=J.S(part,"Suffix")!="";lines.Add("· 패치 "+title+" — "+J.S(part,"Summary")+" (GameAssembly.dll 을 고침, 순서는 '모드 관리' 탭)"+(J.S(part,"Suffix")!=""?" · 데이터 모드 게임 목록: "+(on is bool?((bool)on?"켜짐":"꺼짐"):"기록 없음(게임을 한 번 켜면 생김)"):""));
                    var blocked=J.Get(part,"Blocked") as System.Collections.IEnumerable;if(blocked!=null&&!(blocked is string))foreach(var b in blocked)lines.Add("   ⚠ 함께 쓸 수 없음: "+J.S(b as Dictionary<string,object>,"Why"));}
                else if(kind=="FilePatch")lines.Add("· 패치 "+title+" — "+J.S(part,"Summary")+" (게임 파일 "+files+"개를 직접 고침, 제거하면 패치의 「원래대로」)");
                else if(kind=="WorkshopPatch"){int changed=Int(J.Get(part,"Changed"));lines.Add("· 창작마당 하위 패치 — 창작마당 "+J.S(part,"WorkshopId")+(J.S(part,"Parent")!=""?" ("+J.S(part,"Parent")+")":"")+" 폴더의 파일 "+files+"개를 덮음. '모드 관리' 탭의 원본 카드 안에 보입니다."+(changed>0?" ⚠ 원본 업데이트로 "+changed+"개가 풀림 — 원본 카드를 고르고 '다시 적용'.":""));}
                else lines.Add("· "+kind+" "+title);
            }
            int missing;int.TryParse(J.S(d,"Missing"),out missing);
            if(missing>0)lines.Add("⚠ 기록에 있는 파일 "+missing+"개가 지금 없습니다(다른 곳에서 지웠거나 게임 업데이트).");
            var conflicts=J.Strings(J.Get(d,"Conflicts"));
            if(conflicts.Count>0){lines.Add("⚠ 같은 파일을 쓰는 다른 모드(나중에 설치한 쪽이 적용됨):");lines.AddRange(conflicts.Take(8).Select(c=>"   "+c));if(conflicts.Count>8)lines.Add("   … 외 "+(conflicts.Count-8)+"개");}
            if(hasData)lines.Add("켜고 끄기와 적용 순서는 '모드 관리' 탭에서 정합니다.");
            packageTitle.Text=p.Name+"  ·  "+p.BadgeText;packageInfo.Text=String.Join("\n",lines);
            string doc=J.S(d,"Doc");packageDocName.Text=doc==""?"설명서 없음":"설명서 · "+J.S(d,"DocName");packageDoc.Text=doc;
            packageUninstall.IsEnabled=!busy;
        }
        void UninstallPackage(PackageView p=null){
            if(p==null)p=SelectedPackage();if(p==null||busy)return;if(PackageBlocked("제거할",true))return;
            var parts=J.Items(J.Get(p.Data,"Parts")).Select(x=>"· "+PartLabel(J.S(x,"Kind"))+" "+J.S(x,"Title")+" (파일 "+J.S(x,"Files")+"개)").ToList();
            var message=new List<string>{"제거: "+p.Name,""};message.AddRange(parts);
            message.Add("");message.Add("넣은 파일은 설치 기록 보관함(LOH_ModManager\\Installed\\"+p.Id+"\\removed-…)으로 옮기고, 덮였던 원본은 되돌립니다.");
            message.Add("설치 뒤에 다른 곳에서 바뀐 파일은 건드리지 않고 남깁니다. 나중에 넣은 다른 설치가 같은 파일을 쓰면 그 설치가 원본을 넘겨받습니다.");
            if(J.Items(J.Get(p.Data,"Parts")).Any(x=>J.S(x,"Kind")=="Patcher")){
                message.Add("");message.Add("패치: 올인원이 바꾼 게임 파일을 설치 전 원본으로 되돌립니다(모드매니저가 보관한 원본, 옛 올인원이 남긴 백업).");
                message.Add("원본이 없는 파일은 그대로 두니, 그때는 스팀 「게임 파일 무결성 확인」을 쓰세요.");
            }
            if(J.Items(J.Get(p.Data,"Parts")).Any(x=>J.S(x,"Kind")=="FilePatch")){message.Add("");message.Add("패치: 패치의 「원래대로」(--restore)로 고친 게임 파일을 패치 백업에서 되돌립니다. 설치 뒤 게임 업데이트로 파일이 바뀌었으면 되돌리지 않고 그대로 둡니다.");}
            if(!Dialogs.Confirm(Window,"모드 제거 확인",String.Join(Environment.NewLine,message),"제거"))return;
            RunBusy("제거하는 중… "+p.Name,()=>Backend.Run("UninstallPackage",game,new Dictionary<string,object>{{"Id",p.Id}}),result=>{
                var kept=J.Strings(J.Get(result,"Kept"));selectedPackage=null;
                ReloadCatalog(false);LoadPackages();if(dataToggle.IsChecked==true)ScanDataMods();
                string note=J.S(result,"Note");
                ShowNotice(kept.Count>0||note.Contains("못했")?"warn":"ok","'"+p.Name+"'을 제거했습니다."+(kept.Count>0?" 설치 뒤에 바뀐 파일 "+kept.Count+"개는 그대로 두었습니다: "+String.Join(", ",kept.Take(3)):"")+(note!=""?" "+note:"")+"\n보관함: "+J.S(result,"Vault"),null,null,null,null);
            },null);
        }

        // ---------- patch stack (patchers that rewrite GameAssembly.dll) ----------
        static bool IsPatchRow(ModEntry r){return r.Type=="package"&&r.PackageKind.Contains("패치");}
        // any install's own row goes to 끔 by setting its files aside (patchers too: they also undo what they changed)
        static bool Parks(ModEntry r){return r.Type=="package";}
        // an install the list cannot switch off row by row: another install needs a file of it, or it has parts with no row (a shared
        // loader's plugins load whatever is in place, whatever the DLL and game lists say)
        bool SetsAside(string installId){var item=catalog.Installed.FirstOrDefault(x=>J.S(x,"Id")==installId);
            return item!=null&&(J.Strings(J.Get(item,"NeededBy")).Count>0||J.Items(J.Get(item,"Parts")).Any(p=>J.S(p,"Kind")=="HostPlugin"));}
        // Steam put its own GameAssembly.dll back (game update or integrity check): the patchers are gone until rebuilt from the new
        // original. Asked once per state; a rebuild runs third-party patchers for a minute or two, so it waits for a click.
        string patchNoticeShown;
        void ShowPatchNotice(){
            var s=catalog.PatchState;if(s==null||busy||pending!=null)return;
            string names=String.Join(", ",J.Strings(J.Get(s,"Patchers")));
            // GameAssembly.dll changed but it is not Steam's original: another patcher was run outside the manager (its patches are still there)
            if(J.B(s,"Outside")&&!J.B(s,"NeedsRebuild")){
                if(patchNoticeShown=="outside")return;patchNoticeShown="outside";
                ShowNotice("info","모드매니저 밖에서 게임 본체(GameAssembly.dll)가 바뀌었습니다. 다른 패치를 직접 설치하셨다면 게임에는 그대로 적용되고, 모드매니저로 넣은 패치("+names+")도 그대로입니다.\n다만 모드매니저로 패치를 넣거나 빼면 원본부터 다시 입히면서 밖에서 넣은 패치는 빠지니, 그때 한 번 더 설치해 주세요.",
                    "이대로 쓰기",()=>{HideNotice();if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 눌러 주세요.","모드매니저");return;}RunBusy("지금 상태를 기억하는 중…",()=>Backend.Run("PatchKeep",game,new Dictionary<string,object>()),result=>ReloadCatalog(false),null);},
                    "나중에",HideNotice);
                return;
            }
            if(!J.B(s,"NeedsRebuild"))return;
            string key=J.S(s,"Build")+"|"+J.B(s,"Updated")+"|"+J.B(s,"Lost");if(key==patchNoticeShown)return;patchNoticeShown=key;
            string why=J.B(s,"Updated")&&J.S(s,"StackBuild")!=""?"게임이 업데이트되어(빌드 "+J.S(s,"StackBuild")+" → "+J.S(s,"Build")+")":J.B(s,"Updated")?"게임이 업데이트되어":"게임 파일이 바뀌어(무결성 확인 등)";
            if(!J.B(s,"CanRebuild")){ShowNotice("warn",why+" 패치("+names+")가 풀린 것 같지만, 지금 GameAssembly.dll 에 다른 패치가 남아 있어 새 원본으로 쓸 수 없습니다. Steam 「게임 파일 무결성 확인」을 한 뒤 '설치 정보' 탭의 '패치 다시 적용'을 누르세요.",null,null,null,null);return;}
            ShowNotice("warn",why+" 패치가 풀렸습니다: "+names+". 다시 적용하면 새 게임 파일에 순서대로 다시 입힙니다(패치에 따라 시간이 걸릴 수 있습니다)."+(live.Game!=null?" 게임을 끈 뒤 누르세요.":""),"지금 다시 적용",()=>{HideNotice();RebuildPatches(null);},"나중에",HideNotice);
        }
        // 세력금고 reads the game's functions through a profile made from GameAssembly.dll; after an update or an integrity check
        // (anything the manager did not rebuild itself) it switches itself off until the profile is made again. Asked once per set.
        // ---------- 하위 패치: a workshop mod's files overwritten by a patch (번역 등), shown inside its card ----------
        // cards start folded; the ones the player unfolds stay unfolded next time (per game folder, OpenCards.txt)
        HashSet<string> openCards;string openCardsFor;StackPanel subPanel;
        string OpenCardsFile{get{return Path.Combine(Store.Folder(game),"OpenCards.txt");}}
        HashSet<string> OpenCards(){
            if(openCards==null||openCardsFor!=game){openCardsFor=game;openCards=new HashSet<string>();
                try{if(File.Exists(OpenCardsFile))foreach(var l in File.ReadAllLines(OpenCardsFile))if(l.Trim()!="")openCards.Add(l.Trim());}catch{}}
            return openCards;
        }
        void SaveOpenCards(){try{Directory.CreateDirectory(Path.GetDirectoryName(OpenCardsFile));File.WriteAllLines(OpenCardsFile,OpenCards().OrderBy(x=>x));}catch{}}
        static int Int(object o){int n;return int.TryParse(Convert.ToString(o),out n)?n:0;}
        sealed class SubPatch { public string Id="",Name="",WorkshopId="";public int Files,Changed,NoOriginal;public bool Subscribed;public Dictionary<string,object> Item;public List<string> Exts=new List<string>(); }
        // what a patch's files are, from their kinds: data (json·txt…), a DLL, or art (bundles, pictures) — said on its line
        static string FilesKind(List<string> exts){
            var data=new[]{".json",".txt",".csv",".xml",".ini",".lua",".yaml",".toml",".cfg"};var art=new[]{".png",".jpg",".jpeg",".bundle",".assets",".ab",".unity3d",".wav",".ogg",".mp3",""};
            var kinds=new List<string>();if(exts.Any(e=>data.Contains(e)))kinds.Add("데이터");if(exts.Any(e=>e==".dll"))kinds.Add("DLL");if(exts.Any(e=>art.Contains(e)))kinds.Add("그림·에셋");
            if(exts.Any(e=>!data.Contains(e)&&e!=".dll"&&!art.Contains(e)))kinds.Add("기타 파일");
            return kinds.Count==0?"파일":String.Join("+",kinds);
        }
        List<SubPatch> Patches(string workshopId){
            var list=new List<SubPatch>();
            foreach(var item in catalog.Installed)foreach(var part in J.Items(J.Get(item,"Parts")))
                if(J.S(part,"Kind")=="WorkshopPatch"&&(workshopId==null||J.S(part,"WorkshopId")==workshopId))
                    list.Add(new SubPatch{Id=J.S(item,"Id"),Name=J.S(item,"Name"),WorkshopId=J.S(part,"WorkshopId"),Files=Int(J.Get(part,"Files")),Changed=Int(J.Get(part,"Changed")),NoOriginal=Int(J.Get(part,"NoOriginal")),Subscribed=J.Get(part,"Subscribed") as bool? ?? true,Item=item,Exts=J.Strings(J.Get(part,"Exts"))});
            return list;
        }
        static string WorkshopIdOf(ModEntry r){string id=Path.GetFileName((r.Path??"").TrimEnd('\\'));return id!=""&&id.All(Char.IsDigit)?id:"";}
        List<SubRowView> SubsOf(ModEntry r){
            string ws=WorkshopIdOf(r);if(ws=="")return null;
            return Patches(ws).Select(p=>{Badge b=p.Changed>0?Badge.Waiting:r.Enabled?Badge.Planned:Badge.Off;
                string fk=FilesKind(p.Exts);string exts=String.Join("·",p.Exts.Where(e=>e!="").Select(e=>e.TrimStart('.')).Take(4));
                return new SubRowView{Owner=r.Id,PatchIcons=true,HasLink=PatchLink(p)!="",LinkTip=PatchLink(p)!=""?"게시글 열기 — "+PatchLink(p):"게시글 링크 넣기…",Id=p.Id,Title=p.Name,Kind=fk+" 패치",KindFore=Look.Kind(fk.Split('+')[0]),Info="  ·  창작마당 원본 위에 덮어씀 · "+fk+" 파일 "+p.Files+"개"+(exts!=""?"("+exts+")":"")+(PatchLink(p)!=""?" · 게시글 있음":""),BadgeText=p.Changed>0?"풀림 "+p.Changed+"개":r.Enabled?"적용됨":"원본과 함께 꺼짐",BadgeBrush=Look.Back(b),BadgeFore=Look.Fore(b),
                    BadgeTip=p.Changed>0?"창작마당 원본이 업데이트되어 패치한 파일 "+p.Changed+"개가 원본으로 돌아갔습니다. 이 모드를 고르고 '다시 적용'을 누르세요.":r.Enabled?"원본 모드의 파일 위에 덮여 있습니다. 원본을 켜면 함께 적용됩니다.":"원본 모드가 꺼져 있어 패치도 게임에 들어가지 않습니다."};}).ToList();
        }
        // the patch post: the player's link, else a known one for that workshop mod (춘화추월 한글화 → 디시 70813)
        string PatchLink(SubPatch p){string u;if(Posts.Saved().TryGetValue("patch:"+p.Id,out u))return u;var k=Posts.Known1("wspatch:"+p.WorkshopId);return k==null?"":k.Url;}
        void EditPatchLink(SubPatch p){
            string url=Dialogs.Ask(Window,"게시글 링크 — "+p.Name,"이 패치(번역 등)의 게시글 주소를 붙여 넣으세요. 비우고 확인하면 링크를 지웁니다.",PatchLink(p),false);
            if(url==null)return;url=url.Trim();if(url!=""&&!Posts.IsWeb(url)){MessageBox.Show(Window,"http:// 또는 https:// 로 시작하는 주소를 넣어 주세요.","게시글 링크");return;}
            Posts.Save("patch:"+p.Id,url);linkCache.Clear();Render();
        }
        // ---------- 상한해제 종합 패처: its menus (1 동행 · 2 병력 · 3 랜덤무장 · 4 AI 발탁) as lines of its card, each on or off ----------
        sealed class CapState { public string Id="",Name="";public List<int> Menus=new List<int>(),Applied=new List<int>();public Dictionary<int,string> Blocked=new Dictionary<int,string>(); }
        static readonly string[] CapNames={"","동행 상한 해제","병력 상한 해제","랜덤무장 능력 상한 해제","AI 발탁 책략 사용"};
        static readonly string[] CapDetail={"","동행 200명 · 동행·용병 합계 202명 · 무투 참가자 선택 제한 해제","단일 부대 병력 상한 20,000명","선호 특성 강도 3 · 무작위 특성 0~5 · 능력치 보정 -15~+15 · 최저 5 (새로 발탁되는 무장부터)","AI 소속 무장이 10명 미만이면 월간 판단에서 인재 발탁을 먼저 씀"};
        CapState CapOf(ModEntry r){
            if(r==null||r.Type!="package"||!r.Id.StartsWith("pkg:"))return null;string id=r.Id.Substring(4);
            foreach(var item in catalog.Installed){if(J.S(item,"Id")!=id)continue;
                foreach(var part in J.Items(J.Get(item,"Parts"))){if(J.S(part,"Flavor")!="capunlock")continue;
                    var c=new CapState{Id=id,Name=J.S(item,"Name")};
                    foreach(var m in J.Strings(J.Get(part,"Menus")))c.Menus.Add(Int(m));foreach(var m in J.Strings(J.Get(part,"Applied")))c.Applied.Add(Int(m));
                    var blocked=J.Get(part,"Blocked") as System.Collections.IEnumerable;if(blocked!=null&&!(blocked is string))foreach(var b in blocked){var d=b as Dictionary<string,object>;if(d!=null)c.Blocked[Int(J.Get(d,"Menu"))]=J.S(d,"Why");}
                    return c;}}
            return null;
        }
        // why a patcher (or part of it) gave way to one lower in the list (Invoke-MMPatchRebuild's decisions, kept in its record)
        string YieldWhy(ModEntry r){
            if(r==null||!r.Id.StartsWith("pkg:"))return "";string id=r.Id.Substring(4);
            foreach(var item in catalog.Installed)if(J.S(item,"Id")==id){var y=J.Obj(J.Get(item,"Yield"));if(y==null)return "";return String.Join(" ",J.Strings(J.Get(y,"Why")));}
            return "";
        }
        List<SubRowView> CapSubs(CapState c){
            var list=new List<SubRowView>();
            for(int n=1;n<=4;n++){string why;bool on=c.Applied.Contains(n),clash=c.Blocked.TryGetValue(n,out why);Badge b=on?Badge.Running:clash?Badge.Waiting:Badge.Off;
                var row=new SubRowView{Id=c.Id+":"+n,Group="cap",Record=c.Id,Number=n,Checkable=true,CheckEnabled=!busy,Title=n+" "+CapNames[n],Info="  ·  "+CapDetail[n],BadgeText=on?"적용됨":clash?"함께 쓸 수 없음":"꺼짐",BadgeBrush=Look.Back(b),BadgeFore=Look.Fore(b),
                    BadgeTip=on?"게임 파일에 들어가 있습니다. 체크를 끄면 이 항목만 뺍니다(몇 초).":clash?why:"체크하면 이 항목을 설치합니다(몇 초)."};
                row.Start(c.Menus.Contains(n));list.Add(Pend(row,c.Menus.Contains(n)));}
            return list;
        }
        // ---------- 올인원: its items (한글폰트 · 한글보강 · 스킨케어 · 랜덤픽스 · 호전성 · 상성 · 진형 · 전술 · 무쌍) as lines, each a checkbox ----------
        sealed class AioItem { public string Key="",Name="",Info="";public bool On,Failed,Yielded,Free; }
        sealed class AioState { public string Id="",Name="";public List<AioItem> Items=new List<AioItem>();public List<string> Held=new List<string>();public bool Free; }   // Held: items that give way (not in the file)
        AioState AioOf(ModEntry r){
            if(r==null||r.Type!="package"||!r.Id.StartsWith("pkg:"))return null;string id=r.Id.Substring(4);
            foreach(var item in catalog.Installed){if(J.S(item,"Id")!=id)continue;
                foreach(var part in J.Items(J.Get(item,"Parts"))){if(J.S(part,"Kind")!="Patcher")continue;var a=new AioState{Id=id,Name=J.S(item,"Name")};
                    var y=J.Obj(J.Get(item,"Yield"));if(y!=null){a.Held=J.Strings(J.Get(y,"Items"));a.Free=J.B(y,"Free");}
                    foreach(var x in J.Items(J.Get(part,"Items")))a.Items.Add(new AioItem{Key=J.S(x,"Key"),Name=J.S(x,"Name"),Info=J.S(x,"Info"),On=J.B(x,"On"),Failed=J.B(x,"Failed"),Yielded=J.B(x,"Yielded"),Free=J.B(x,"Free")});
                    return a.Items.Count>0?a:null;}}
            return null;
        }
        List<SubRowView> AioSubs(AioState a){
            return a.Items.Select(x=>{Badge b=x.Failed?Badge.Error:x.Yielded?Badge.Waiting:x.On?Badge.Running:Badge.Off;
                var row=new SubRowView{Id=a.Id+":"+x.Key,Group="aio",Record=a.Id,Key=x.Key,Checkable=true,CheckEnabled=!busy,Title=x.Name,Info=x.Info!=""?"  ·  "+x.Info:"",BadgeText=x.Failed?"실패":x.Free?"다시 쌓으면 들어감":x.Yielded?"자리를 내줌":x.On?"적용됨":"꺼짐",BadgeBrush=Look.Back(b),BadgeFore=Look.Fore(b),
                    BadgeTip=x.Free?"함께 쓸 수 없던 상대 항목이 꺼져 이제 들어갈 수 있습니다. '패치 다시 적용'을 누르거나 이 체크를 껐다 켜면 들어갑니다. 체크를 끄는 건 바로 끝납니다.":x.Yielded?"목록에서 아래에 있는 패치와 같은 자리를 써서 이번에는 넣지 않았습니다. 게임 파일에 없으니 체크를 끄는 건 바로 끝납니다. 이 항목을 쓰려면 올인원을 그 패치 아래로 옮기세요.":x.Failed?"지난 설치에서 이 항목이 들어가지 못했습니다(올인원 기록 patcher.log).":"체크하면 이 항목의 패치만 돌려 켜고, 끄면 그 파일만 원본으로 되돌립니다(보통 수십 초)."};
                row.Start(x.On);return Pend(row,x.On);}).ToList();
        }
        // a patch installed by its loh-mod.json ("patch"): its items as lines with checkboxes, whatever the mod is
        List<SubRowView> PromiseSubs(ModEntry r){
            var rows=new List<SubRowView>();if(r==null||r.Type!="package"||!r.Id.StartsWith("pkg:"))return rows;string id=r.Id.Substring(4);
            foreach(var item in catalog.Installed){if(J.S(item,"Id")!=id)continue;
                foreach(var part in J.Items(J.Get(item,"Parts"))){if(J.S(part,"Flavor")!="promise")continue;var chosen=J.Strings(J.Get(part,"Chosen"));
                    foreach(var x in J.Items(J.Get(part,"PromiseItems"))){string key=J.S(x,"Id");bool on=chosen.Contains(key);Badge b=on?Badge.Running:Badge.Off;
                        var row=new SubRowView{Id=id+":"+key,Group="promise",Record=id,Key=key,Checkable=true,CheckEnabled=!busy,Title=J.S(x,"Name"),Info=J.S(x,"Note")!=""?"  ·  "+J.S(x,"Note"):"",
                            BadgeText=on?"적용됨":"꺼짐",BadgeBrush=Look.Back(b),BadgeFore=Look.Fore(b),BadgeTip=on?"게임 파일에 들어가 있습니다. 체크를 끄면 패치를 다시 돌려 이 항목만 뺍니다.":"고르지 않았습니다. 체크하면 패치를 다시 돌려 이 항목도 넣습니다."};
                        row.Start(on);rows.Add(Pend(row,on));}}}
            return rows;
        }
        // ---------- patch items ticked but not saved yet ----------
        // A bundled patch's items (올인원 · 상한해제 · 양식 패치) wait for 저장 like the cards do: ticking several costs one run each, so
        // the ticks are kept here (row id → wanted state) and 저장 applies them per mod in one go (user decision 11-01).
        sealed class SubPend { public string Group="",Record="",Key="",Title="";public int Number;public bool On; }
        Dictionary<string,SubPend> subPending=new Dictionary<string,SubPend>();
        // the row as it is, or as it will be once saved
        SubRowView Pend(SubRowView row,bool actual){
            row.Actual=actual;SubPend p;
            if(subPending.TryGetValue(row.Id,out p)){
                if(p.On==actual){subPending.Remove(row.Id);return row;}   // the list caught up with it (or it was ticked back)
                row.Start(p.On);row.BadgeText=p.On?"저장하면 켜짐":"저장하면 꺼짐";row.BadgeBrush=Look.Back(Badge.Waiting);row.BadgeFore=Look.Fore(Badge.Waiting);row.BadgeTip="저장을 누르면 적용합니다.";
            }
            return row;
        }
        bool SubWanted(SubRowView row,bool actual){SubPend p;return subPending.TryGetValue(row.Id,out p)?p.On:actual;}
        void SubToggled(SubRowView row,bool on){
            if(busy){Render();return;}
            if(row.Group=="cap"){
                var cap=CapOf(new ModEntry{Id="pkg:"+row.Record,Type="package"});if(cap==null){Render();return;}
                int left=CapSubs(cap).Count(x=>x.Number==row.Number?on:x.On);
                if(left==0){MessageBox.Show(Window,"하나는 남겨 두어야 합니다. 모두 빼려면 '설치 정보' 탭에서 제거하세요.","상한해제");Render();return;}
            }
            if(row.Group=="promise"&&PromiseSubs(Row("pkg:"+row.Record)).Count(x=>x.Key==row.Key?on:x.On)==0){MessageBox.Show(Window,"하나 이상은 켜 두어야 합니다. 모두 빼려면 카드를 꺼짐으로 옮기세요.","모드매니저");Render();return;}
            if(on==row.Actual)subPending.Remove(row.Id);else subPending[row.Id]=new SubPend{Group=row.Group,Record=row.Record,Key=row.Key,Number=row.Number,Title=row.Title,On=on};
            Render();
        }
        // 저장: each mod's ticks in one run (올인원: one answer set; 상한해제: the numbers it keeps; 양식 패치: its items)
        void ApplySubs(Action then){
            if(subPending.Count==0){if(then!=null)then();return;}
            var order=PatchOrderOnScreen();string folder=game;var jobs=new List<Tuple<string,string,Dictionary<string,object>>>();
            foreach(var g in subPending.Values.GroupBy(p=>p.Group+"|"+p.Record)){
                var first=g.First();var args=new Dictionary<string,object>{{"Id",first.Record},{"Order",order}};
                if(first.Group=="aio"){var a=AioOf(new ModEntry{Id="pkg:"+first.Record,Type="package"});args["Changes"]=g.ToDictionary(p=>p.Key,p=>(object)p.On);jobs.Add(Tuple.Create("AioItems",a==null?"올인원":a.Name,args));}
                else if(first.Group=="cap"){var c=CapOf(new ModEntry{Id="pkg:"+first.Record,Type="package"});if(c==null)continue;args["Menus"]=CapSubs(c).Where(x=>x.On).Select(x=>x.Number).OrderBy(n=>n).ToList();jobs.Add(Tuple.Create("CapMenus","상한해제",args));}
                else if(first.Group=="promise"){var r=Row("pkg:"+first.Record);args["Items"]=PromiseSubs(r).Where(x=>x.On).Select(x=>x.Key).ToList();jobs.Add(Tuple.Create("PromiseItems",r==null?first.Title:r.Name,args));}
            }
            subPending.Clear();
            RunBusy("패치 항목을 바꾸는 중… (보통 수십 초, 길면 몇 분)",()=>{
                var lines=new List<string>();bool warn=false;
                foreach(var j in jobs){
                    try{var r=Backend.Run(j.Item1,folder,j.Item3);var failed=J.Strings(J.Get(r,"Failed"));var blocked=J.Strings(J.Get(r,"Blocked"));var freed=J.Strings(J.Get(r,"Freed"));
                        warn|=failed.Count>0||blocked.Count>0||(j.Item1=="PromiseItems"&&!J.B(r,"Ok"));
                        lines.Add((j.Item1=="AioItems"?"올인원 항목을 바꿨습니다: ":j.Item2+" 항목을 바꿨습니다: ")+J.S(r,"Tail")+(failed.Count>0?" · 실패한 패치: "+String.Join(", ",failed):"")
                            +(freed.Count>0?" · "+String.Join(", ",freed)+" 에서 자리를 내줬던 항목은 '패치 다시 적용'을 누르면 들어갑니다.":""));}
                    catch(Exception x){warn=true;lines.Add(j.Item2+" 항목을 바꾸지 못했습니다: "+x.Message);}
                }
                return Tuple.Create(lines,warn);
            },result=>{
                ReloadCatalog(false);if(packagesView.Visibility==Visibility.Visible)LoadPackages();
                ShowNotice(result.Item2?"warn":"ok",String.Join("\n",result.Item1)+" 다음에 게임을 켤 때 반영됩니다.",null,null,null,null);
                if(then!=null)then();
            },null);
        }
        // the bottom panel: the same items as checkboxes, each with its line of detail or the reason it clashes
        void FillSubLines(List<SubRowView> rows,Func<SubRowView,string> detail,Func<SubRowView,bool> clash){
            subPanel.Visibility=Visibility.Visible;
            foreach(var row in rows){var r=row;bool warn=clash(r);
                var box=new Border{BorderThickness=new Thickness(3,0,0,0),CornerRadius=new CornerRadius(3,8,8,3),Padding=new Thickness(12,6,10,6),Margin=new Thickness(0,0,0,5)};box.SetResourceReference(Border.BackgroundProperty,"CardAlt");box.SetResourceReference(Border.BorderBrushProperty,warn?"Warn":r.On?"Accent":"Line");
                var body=new StackPanel();box.Child=body;
                var check=new CheckBox{Content=r.Title+"  ·  "+r.BadgeText,IsChecked=r.On,IsEnabled=!busy,FontWeight=FontWeights.SemiBold,ToolTip=r.BadgeTip};check.Click+=(s,e)=>SubToggled(r,check.IsChecked==true);body.Children.Add(check);
                string text=detail(r);if(text!=""){var note=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(24,2,0,2),FontSize=Fs.Secondary};note.SetResourceReference(TextBlock.ForegroundProperty,warn?"Warn":"Sub");body.Children.Add(note);}
                subPanel.Children.Add(box);}
        }
        void FillCapPanel(CapState c){FillSubLines(CapSubs(c),r=>{string why;return c.Blocked.TryGetValue(r.Number,out why)?"⚠ "+why:CapDetail[r.Number];},r=>c.Blocked.ContainsKey(r.Number));}
        void FillAioPanel(AioState a){FillSubLines(AioSubs(a),r=>{var x=a.Items.First(i=>i.Key==r.Key);return x.Failed?"⚠ 지난 설치에서 들어가지 못했습니다(올인원 기록 patcher.log).":x.Info;},r=>a.Items.First(i=>i.Key==r.Key).Failed);}        void SetCapMenus(CapState c,List<int> menus,string what){
            if(busy)return;if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 바꿀 수 있습니다. 실행 중에는 게임 파일을 바꾸지 않습니다.","모드매니저");return;}
            var order=PatchOrderOnScreen();   // cards moved but not saved: this order decides who wins, and is kept
            RunBusy("상한해제 "+what+" … (보통 몇 초, 다른 패치를 다시 쌓아야 하면 1~2분)",()=>Backend.Run("CapMenus",game,new Dictionary<string,object>{{"Id",c.Id},{"Menus",menus},{"Order",order}}),result=>{
                ReloadCatalog(false);if(packagesView.Visibility==Visibility.Visible)LoadPackages();var blocked=J.Strings(J.Get(result,"Blocked"));var freed=J.Strings(J.Get(result,"Freed"));
                string text="상한해제 "+what+" — "+J.S(result,"Tail")+" 다음에 게임을 켤 때 반영됩니다.";
                // what gave way to the number just turned off was left out as it was: putting it back is a rebuild, on the player's word
                if(freed.Count>0)ShowNotice("warn",text+"\n"+String.Join(", ",freed)+" 에서 자리를 내줬던 항목이 이제 들어갈 수 있습니다(지금은 빠진 채, 충돌 없음). 넣으려면 패치를 다시 적용하세요(1~2분).","지금 넣기",()=>{HideNotice();RebuildPatches(null);},"나중에",HideNotice);
                else ShowNotice(blocked.Count>0?"warn":"ok",text,null,null,null,null);
            },null);
        }
        void FillSubPanel(ModEntry r){
            subPanel.Children.Clear();var cap=CapOf(r);if(cap!=null){FillCapPanel(cap);return;}var aio=AioOf(r);if(aio!=null){FillAioPanel(aio);return;}
            var promised=PromiseSubs(r);if(promised.Count>0){FillSubLines(promised,x=>x.Info.Replace("  ·  ",""),x=>false);return;}
            var list=r==null||r.Type!="workshop"?new List<SubPatch>():Patches(WorkshopIdOf(r));
            subPanel.Visibility=list.Count>0?Visibility.Visible:Visibility.Collapsed;
            foreach(var p in list){var pp=p;
                var box=new Border{BorderThickness=new Thickness(3,0,0,0),CornerRadius=new CornerRadius(3,8,8,3),Padding=new Thickness(12,8,10,8),Margin=new Thickness(0,0,0,6)};box.SetResourceReference(Border.BackgroundProperty,"CardAlt");box.SetResourceReference(Border.BorderBrushProperty,"Accent");
                var body=new StackPanel();box.Child=body;
                body.Children.Add(new TextBlock{Text="하위 패치 · "+p.Name,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
                string state=p.Changed>0?"⚠ 창작마당 원본이 업데이트되어 패치한 파일 "+p.Changed+"개가 원본으로 돌아갔습니다. '다시 적용'을 누르세요. 원본이 바뀌어 번역이 안 맞는 줄이 있을 수 있으니 새 번역이 올라왔는지 게시글도 확인하세요."
                    :"원본 폴더의 파일 "+p.Files+"개를 덮어 두었습니다."+(r.Enabled?"":" 원본이 꺼져 있어 지금은 게임에 들어가지 않습니다.");
                if(p.NoOriginal>0)state+=" 설치 전부터 덮여 있던 파일 "+p.NoOriginal+"개는 원본이 없어, 제거하면 Steam 에서 다시 받아야 합니다.";
                var note=new TextBlock{Text=state,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,8),FontSize=Fs.Secondary};note.SetResourceReference(TextBlock.ForegroundProperty,p.Changed>0?"Warn":"Sub");body.Children.Add(note);
                var buttons=new WrapPanel();body.Children.Add(buttons);string link=PatchLink(p);

                var again=new Button{Content="다시 적용",Margin=new Thickness(0,0,8,0),IsEnabled=p.Changed>0&&!busy,ToolTip=p.Changed>0?"보관해 둔 패치 파일을 원본 폴더에 다시 덮습니다(Steam 이 새로 받은 원본은 백업).":"지금은 패치가 그대로 덮여 있습니다."};again.Click+=(s,e)=>ReapplyWorkshop(pp.Id,pp.Name);buttons.Children.Add(again);
                var remove=new Button{Content="패치 제거…",Margin=new Thickness(0,0,8,0),ToolTip="이 패치만 빼고 원본 파일로 되돌립니다. 창작마당 원본 모드는 그대로 둡니다."};remove.Click+=(s,e)=>UninstallPackage(new PackageView{Id=pp.Id,Name=pp.Name,Data=pp.Item});buttons.Children.Add(remove);
                if(link!=""){var edit=new Button{Content="링크 바꾸기…",ToolTip="이 패치의 게시글 주소를 바꿉니다."};edit.Click+=(s,e)=>EditPatchLink(pp);buttons.Children.Add(edit);}
                subPanel.Children.Add(box);
            }
        }
        void ReapplyWorkshop(string id,string name){
            if(busy)return;   // only the workshop folder changes: unsaved list edits do not matter, a running game does
            if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 다시 적용할 수 있습니다. 실행 중에는 모드 파일을 바꾸지 않습니다.","모드매니저");return;}
            if(watchId!=""){MessageBox.Show(Window,"'"+watchName+"' 수동 설치를 기록하는 중입니다. 먼저 위쪽 안내의 '설치 끝' 또는 '기록 취소'를 눌러 주세요.","모드매니저");return;}
            RunBusy("패치를 다시 적용하는 중… "+name,()=>Backend.Run("WorkshopReapply",game,new Dictionary<string,object>{{"Id",id}}),result=>{
                ReloadCatalog(false);if(packagesView.Visibility==Visibility.Visible)LoadPackages();int lost=Int(J.Get(result,"Lost"));
                ShowNotice(lost>0?"warn":"ok","'"+name+"' 패치를 다시 적용했습니다(파일 "+J.S(result,"Done")+"개)."+(lost>0?" 보관해 둔 패치 사본 "+lost+"개가 없어 넣지 못했습니다 — 패치 압축을 다시 끌어다 놓으세요.":"")+" 다음에 게임을 켤 때 반영됩니다.",null,null,null,null);
            },null);
        }
        // Steam updated an original: its patch is gone until applied again. Asked once per set of undone patches.
        string workshopNoticeShown;
        void ShowWorkshopNotice(){
            if(busy||pending!=null)return;var undone=Patches(null).Where(p=>p.Changed>0&&p.Subscribed).ToList();
            string key=String.Join("|",undone.Select(p=>p.Id+":"+p.Changed));if(undone.Count==0||key==workshopNoticeShown)return;workshopNoticeShown=key;
            ShowNotice("warn","창작마당 원본이 업데이트되어 패치가 풀렸습니다: "+String.Join(", ",undone.Select(p=>p.Name))+". 다시 적용하면 보관해 둔 패치 파일을 다시 덮습니다(원본이 바뀌어 번역이 안 맞는 줄이 있을 수 있으니 게시글도 확인하세요).",
                "지금 다시 적용",()=>{HideNotice();ReapplyWorkshop(undone[0].Id,undone[0].Name);},"나중에",HideNotice);
        }
        string linkNoticeShown;
        // An entry DLL a mod's own installer put in the game folder (가야 지도 winhttp.dll, 11-01): the game loads it whatever the list says.
        static string ForeignText(Dictionary<string,object> f){
            var related=J.Strings(J.Get(f,"Related"));
            return J.S(f,"Name")+" — 모드매니저 밖에서 들어와("+J.S(f,"Time")+") 게임을 켤 때마다 함께 실행되는 DLL 입니다. 모드매니저 목록에 없어 여기서 끄거나 지울 수 없습니다."+(related.Count>0?" 같은 때 들어온 폴더: "+String.Join(", ",related)+".":"");
        }
        // another mod's DLL now sits where our entry was (its installer, run outside the manager, wrote over it): our mods stopped
        // loading — saved once, with the game off, the entry moves to a free name (the save notice says so). Once per window.
        bool entryFixTried;
        bool FixTakenEntry(){
            if(entryFixTried||busy||pending!=null||live.Game!=null||watchId!=""||Dirty()||!catalog.Complete)return false;
            bool taken=catalog.EntryKinds.Any(k=>k.Value=="foreign"&&catalog.Entries.Any(e=>String.Equals(e,k.Key,StringComparison.OrdinalIgnoreCase)));
            // the HeroesVow common loader left behind another entry name (0.12.44~0.12.45): the save puts it back behind version.dll
            bool hostOff=working.Any(r=>r.Type==""&&r.Enabled&&Path.GetFileName(r.Path??"").Equals("HeroesVowNativeModHost.dll",StringComparison.OrdinalIgnoreCase)&&!String.Equals(r.Route,"version.dll",StringComparison.OrdinalIgnoreCase))
                &&!catalog.EntryKinds.Any(k=>k.Value=="foreign"&&String.Equals(k.Key,"version.dll",StringComparison.OrdinalIgnoreCase));
            if(!taken&&!hostOff)return false;
            entryFixTried=true;RunApply(Choices(working),false);return true;
        }
        // once per DLL (name and time, remembered per game): leaving it is fine — the manager runs beside it on a free entry name,
        // and taking it over can stop a mod that has to run first under its own name (11-03 디시 79344 댓글: asked again at every start)
        string ForeignSeenFile{get{return Path.Combine(Store.Folder(game),"ForeignSeen.txt");}}
        static string ForeignKey(Dictionary<string,object> f){return J.S(f,"Name")+"|"+J.S(f,"Time");}
        void ShowForeignNotice(){
            if(catalog.Foreign.Count==0||busy||pending!=null)return;
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);try{if(File.Exists(ForeignSeenFile))seen.UnionWith(File.ReadAllLines(ForeignSeenFile));}catch{}
            var f=catalog.Foreign.FirstOrDefault(x=>!seen.Contains(ForeignKey(x)));if(f==null)return;
            seen.Add(ForeignKey(f));try{Directory.CreateDirectory(Path.GetDirectoryName(ForeignSeenFile));File.WriteAllLines(ForeignSeenFile,seen);}catch{}
            ShowNotice("info",ForeignText(f)+"\n그대로 둬도 모드매니저와 함께 돌아갑니다. 목록에서 켜고 끄고 싶을 때만 가져오세요. 원래 이름으로 먼저 실행돼야 하는 모드는 가져오면 동작하지 않을 수 있고, 그때는 그 줄의 '원래 방식으로 되돌리기'로 돌릴 수 있습니다. 이 안내는 DLL 마다 한 번만 띄웁니다.",
                "모드매니저로 가져오기",()=>AdoptForeign(J.S(f,"Name")),"빼 두기…",()=>SetAsideForeign(f));
        }
        bool CanTouchGameFiles(){
            if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 눌러 주세요.","모드매니저");return false;}
            if(Dirty()){MessageBox.Show(Window,"저장하지 않은 변경이 있습니다. 먼저 저장하거나 '되돌리기'를 눌러 주세요.","모드매니저");return false;}
            return !busy;
        }
        void AdoptForeign(string name){
            if(!CanTouchGameFiles())return;HideNotice();
            RunBusy("가져오는 중… "+name,()=>Backend.Run("EntryAdopt",game,new Dictionary<string,object>{{"Name",name}}),result=>{
                ReloadCatalog(true);LoadPackages();var related=J.Strings(J.Get(result,"Related"));
                ShowNotice("ok","'"+name+"'을 모드매니저가 가져왔습니다. 이제 모드 관리 목록에서 켜고 끌 수 있습니다(지금은 켜짐). '설치 정보' 탭에서 지울 수도 있습니다."+(related.Count>0?" 같은 때 들어온 폴더("+String.Join(", ",related)+")는 그대로 둡니다.":""),null,null,null,null);
            },null);
        }
        void SetAsideForeign(Dictionary<string,object> f){
            if(!CanTouchGameFiles())return;string name=J.S(f,"Name");var related=J.Strings(J.Get(f,"Related"));var folders=new List<string>();
            if(related.Count>0){
                var answer=MessageBox.Show(Window,name+" 을(를) 보관함으로 옮깁니다.\n같은 때 들어온 폴더("+String.Join(", ",related)+", 모두 약 "+J.S(f,"TotalMB")+"MB)도 함께 옮길까요?\n\n예: 폴더까지 함께  ·  아니요: "+name+" 만  ·  취소\n(지우지 않습니다. '설치 정보' 탭의 '지운 모드 되살리기'로 되돌릴 수 있습니다.)","빼 두기",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
                if(answer==MessageBoxResult.Cancel)return;if(answer==MessageBoxResult.Yes)folders.AddRange(related);
            }else if(MessageBox.Show(Window,name+" 을(를) 보관함으로 옮길까요? 지우지 않으며 '설치 정보' 탭의 '지운 모드 되살리기'로 되돌릴 수 있습니다.","빼 두기",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            HideNotice();
            RunBusy("보관함으로 옮기는 중… "+name,()=>Backend.Run("EntryAside",game,new Dictionary<string,object>{{"Name",name},{"Folders",folders}}),result=>{
                ReloadCatalog(true);if(inventory!=null)ScanInventory();
                ShowNotice("ok","'"+name+"'"+(folders.Count>0?"과 "+String.Join(", ",folders):"")+"을(를) 보관함으로 옮겼습니다(파일 "+J.S(result,"Moved")+"개). 다음 게임부터 실행되지 않습니다. 되돌리려면 '설치 정보' 탭의 '지운 모드 되살리기'.",null,null,null,null);
            },null);
        }
        void ShowLinkNotice(){
            if(catalog.StaleLinks.Count==0||busy||pending!=null)return;var s=catalog.PatchState;if(s!=null&&J.B(s,"NeedsRebuild"))return;   // the rebuild remakes them too
            string key=String.Join("|",catalog.StaleLinks);if(key==linkNoticeShown)return;linkNoticeShown=key;
            ShowNotice("warn","게임 파일(GameAssembly.dll)이 바뀌어 "+String.Join(", ",catalog.StaleLinks)+" 의 게임 연결 정보를 다시 만들어야 합니다(그전엔 그 모드가 스스로 꺼짐)."+(live.Game!=null?" 게임을 끈 뒤 누르세요.":""),"다시 연결",()=>{HideNotice();RefreshLinks();},"나중에",HideNotice);
        }
        void RefreshLinks(){
            if(busy)return;if(live.Game!=null){ShowNotice("warn","게임을 끈 뒤에 다시 연결할 수 있습니다.",null,null,null,null);return;}
            RunBusy("게임 연결 정보를 다시 만드는 중…",()=>Backend.Run("LinkRefresh",game,null),result=>{
                var steps=J.Items(J.Get(result,"Steps")).ToList();bool ok=steps.All(x=>J.B(x,"Ok"));ReloadCatalog(false);if(packagesLoaded)LoadPackages();
                ShowNotice(ok?"ok":"warn",steps.Count==0?"다시 만들 연결 정보가 없습니다.":String.Join(" · ",steps.Select(x=>J.S(x,"Name")+": "+J.S(x,"Tail"))),null,null,null,null);
            },error=>ShowNotice("error","게임 연결 정보를 다시 만들지 못했습니다: "+error,null,null,null,null));
        }
        List<string> PatchOrderNow(){return catalog.Installed.Select(d=>new{d,i=J.S(d,"PatchIndex")}).Where(x=>x.i!=""&&x.i!="-1").OrderBy(x=>int.Parse(x.i)).Select(x=>J.S(x.d,"Id")).ToList();}
        // the patcher order on screen when the player moved cards but has not saved: a checkbox sends it, it decides who wins
        List<string> PatchOrderOnScreen(){var w=working.Where(r=>IsPatchRow(r)&&r.Enabled).Select(r=>r.Id.Substring(4)).ToList();return w.Count>1&&!w.SequenceEqual(PatchOrderNow())?w:new List<string>();}
        // patchers moved to 끔 / back to 켬 (saved): each one's own undo or reinstall, then the order on screen
        void SwitchPatches(List<string> off,List<string> on,List<string> order,Action then=null){
            bool patch=off.Concat(on).Any(id=>{var r=Row("pkg:"+id);return r!=null&&IsPatchRow(r);});
            if(busy)return;if(live.Game!=null){ShowNotice("warn","게임을 끈 뒤에 "+(patch?"패치":"모드")+"를 켜고 끌 수 있습니다.",null,null,null,null);return;}
            RunBusy(patch?"패치를 "+(off.Count>0&&on.Count>0?"켜고 끄는":off.Count>0?"끄는":"켜는")+" 중… 고친 게임 파일을 되돌리고 남은 패치를 다시 적용합니다 (바꾸는 범위가 넓은 패치일수록 오래 걸립니다)"
                :off.Count>0&&on.Count>0?"모드를 켜고 끄는 중… 파일을 빼 두거나 다시 넣습니다":off.Count>0?"모드를 끄는 중… 넣었던 파일을 잠시 빼 둡니다":"모드를 켜는 중… 빼 둔 파일을 다시 넣습니다",
                ()=>Backend.Run("PatchSwitch",game,new Dictionary<string,object>{{"Off",off},{"On",on},{"Order",order??new List<string>()}}),result=>{
                var steps=J.Items(J.Get(result,"Steps")).ToList();bool ok=steps.All(s=>J.B(s,"Ok"));
                ReloadCatalog(true);if(packagesLoaded)LoadPackages();
                var gave=J.Strings(J.Get(result,"Gave"));string gaveText=gave.Count>0?"\n자리를 내줌(목록에서 아래가 우선): "+String.Join(" / ",gave):"";
                ShowNotice(ok&&gave.Count==0?"ok":"warn",(patch?"패치: ":"")+String.Join(" · ",steps.Select(s=>J.S(s,"Name")+" — "+J.S(s,"Tail")))+". 다음에 게임을 켤 때부터 적용됩니다."+gaveText,null,null,null,null);
                if(then!=null&&ok)then();
            },error=>ShowNotice("error",(patch?"패치":"모드")+"를 켜고 끄지 못했습니다: "+error,null,null,null,null));
        }
        // 저장 that changes what patches wrote over the game's files (a patcher going to 끔, ticked patch items) takes from seconds to
        // minutes: one word before it, listing what changes (11-02: cards and item ticks alike)
        // a patch moved under one it was seen not to go in under (자리 찾기, or a save where it then failed): say why before saving
        bool ConfirmPatchOrder(){
            var screen=working.Where(r=>IsPatchRow(r)&&r.Enabled).Select(r=>r.Id.Substring(4)).ToList();if(screen.Count<2)return true;
            var saved=baseRows.Where(r=>IsPatchRow(r)&&r.Enabled).Select(r=>r.Id.Substring(4)).ToList();string msg="";
            foreach(var d in catalog.Installed){string id=J.S(d,"Id");int me=screen.IndexOf(id);if(me<0||msg!="")continue;
                foreach(var a in J.Items(J.Get(d,"Above"))){
                    string other=J.S(a,"Id");int at=screen.IndexOf(other);if(at<0||at>me)continue;
                    int sMe=saved.IndexOf(id),sAt=saved.IndexOf(other);if(sMe>=0&&sAt>=0&&sAt<sMe)continue;   // already saved that way: the card's tag says it
                    var o=catalog.Installed.FirstOrDefault(x=>J.S(x,"Id")==other);if(o==null)continue;
                    string mine=J.S(d,"Name"),theirs=J.S(o,"Name"),tail=J.S(a,"Tail");
                    msg="'"+mine+"'"+Particle(mine,"을","를")+" '"+theirs+"' 아래로 옮겼습니다. "
                        +(tail!=""?"이 패치는 전에 그 아래에서는 들어가지 않았습니다.\n패치가 남긴 말: "+tail:"이 패치는 자기보다 먼저 입혀지는 '"+theirs+"'"+Particle(theirs,"이","가")+" 있으면 들어가지 않았습니다.")
                        +"\n\n왜 이런가요?\n이 패치는 '"+theirs+"'"+Particle(theirs,"이","가")+" 먼저 입혀진 게임 파일에서는 스스로 멈췄습니다. 이유는 위 '패치가 남긴 말'에 있습니다. 모드매니저는 목록 위에서부터 차례로 패치를 입히므로, 아래로 옮기면 '"+theirs+"'"+Particle(theirs,"이","가")+" 먼저 입혀지고 이 패치는 들어가지 못합니다. 어느 쪽이 이기는지 고르는 겹침과 달리, 이 순서에서는 이 패치가 아예 빠집니다."
                        +"\n\n이대로 저장하면\n이 패치는 게임 파일에 들어가지 않습니다(꺼진 것과 같음). 카드에는 '⚠ 들어가지 않음'이 붙고, 다른 패치는 그대로 들어갑니다. 다시 위로 옮겨 저장하면 들어갑니다.";
                    break;
                }
            }
            // a patch that would give up part of itself to another in this order (it does not now): from the places each one changed
            var yields=new List<string>();
            if(!screen.SequenceEqual(saved)){try{yields=J.Strings(J.Get(Backend.Run("YieldPreview",game,new Dictionary<string,object>{{"Order",screen}}),"Lines"));}catch{}}
            if(yields.Count>0)msg=(msg==""?"":msg+"\n\n────────\n\n")+"이 순서로 저장하면 일부 패치가 다른 패치에 자리를 내줍니다.\n"+String.Join("\n",yields.Select(l=>"· "+l))
                +"\n\n왜 이런가요?\n두 패치가 게임 파일의 같은 자리를 고치면, 목록에서 아래에 있는 패치가 그 자리를 갖고 위의 패치는 그 부분만 빠집니다. 빠진 부분 말고는 둘 다 들어갑니다."
                +"\n\n이대로 저장하면\n자리를 내준 부분은 게임에 적용되지 않고, 카드에 '⚠ 자리를 내줌'이 붙습니다. 원래 순서로 되돌리면 다시 들어갑니다.";
            return msg==""||Dialogs.Confirm(Window,"패치 순서 확인",msg,"그래도 저장");
        }
        bool ConfirmPatchWork(bool items){
            var lines=baseRows.Where(b=>IsPatchRow(b)&&b.Enabled&&!b.Parked&&working.Any(w=>w.Id==b.Id&&!w.Enabled)).Select(b=>"· "+b.Name+" 끄기").ToList();
            bool off=lines.Count>0;
            if(items)foreach(var g in subPending.Values.GroupBy(p=>p.Group+"|"+p.Record)){
                var first=g.First();string name=first.Group=="cap"?"상한해제":first.Group=="aio"?"올인원":first.Title;
                var r=Row("pkg:"+first.Record);if(first.Group!="cap"&&r!=null)name=r.Name;
                lines.Add("· "+name+" 항목: "+String.Join(" · ",g.Select(p=>p.Title+" "+(p.On?"켜짐":"꺼짐"))));
            }
            if(lines.Count==0)return true;
            return MessageBox.Show(Window,"저장하면 게임 파일을 직접 고치는 패치를 바꿉니다:\n"+String.Join("\n",lines)+"\n\n"
                +"패치가 고친 파일을 원래대로 되돌리거나 바뀐 부분만 다시 적용합니다. 바꾸는 범위에 따라 수십 초에서 몇 분 걸립니다."
                +(off?"\n끈 패치는 다시 켜면 그때 다시 설치합니다(골라 둔 항목은 그대로).":"")
                +"\n\n확인: 저장합니다.\n취소: 저장하지 않습니다(바꾼 것은 그대로).",
                "패치 바꾸기",MessageBoxButton.OKCancel,MessageBoxImage.Information)==MessageBoxResult.OK;
        }
        void RebuildPatches(List<string> order,Action then=null){
            if(busy)return;if(live.Game!=null){ShowNotice("warn","게임을 끈 뒤에 패치를 다시 적용할 수 있습니다.",null,null,null,null);return;}
            RunBusy("보관해 둔 원본 파일에 패치를 순서대로 다시 적용하는 중… (바꾸는 범위가 넓은 패치일수록 오래 걸립니다)",()=>Backend.Run("PatchRebuild",game,new Dictionary<string,object>{{"Order",order??new List<string>()}}),result=>{
                var steps=J.Items(J.Get(result,"Steps")).ToList();bool ok=steps.All(s=>J.B(s,"Ok"));
                ReloadCatalog(true);if(packagesLoaded)LoadPackages();
                var gave=J.Strings(J.Get(result,"Gave"));string gaveText=gave.Count>0?"\n자리를 내줌(목록에서 아래가 우선): "+String.Join(" / ",gave):"";
                ShowNotice(ok&&gave.Count==0?"ok":"warn","패치를 다시 적용했습니다: "+String.Join(" → ",steps.Select(s=>J.S(s,"Name")+(J.B(s,"Ok")?" ✓":" ✗("+J.S(s,"Tail")+")")))+(steps.Count==0?" (패치 없음)":"")+". 다음에 게임을 켤 때부터 적용됩니다."+gaveText,null,null,null,null);
                // '게임 실행' saved a new order: who gives way changed under the player's feet — say it before the game starts
                if(then!=null&&then.Equals(new Action(StartGame))&&J.B(result,"GaveChanged")&&gave.Count>0
                    &&MessageBox.Show(Window,"패치 순서대로 다시 적용했습니다. 이번 순서에서는 이렇게 됩니다(목록에서 아래가 우선):\n\n· "+String.Join("\n· ",gave)+"\n\n이대로 게임을 켤까요?","게임 실행 전 확인",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
                if(then!=null)then();
            },error=>ShowNotice("error","패치를 다시 적용하지 못했습니다: "+error+" (다시 적용하기 직전 게임 파일이 Backups\\Rebuild-… 에 백업되어 있습니다)",null,null,null,null));
        }

        // ---------- new data mods the game switched off on its first run ----------
        void ReviveFresh(){
            var fresh=Store.Fresh(game);if(fresh.Count==0||!DataMods.IsRealInstall(game))return;
            var list=DataMods.GameSelection();if(list==null)return;   // still no list (the game has not run yet): keep waiting
            var off=fresh.Where(s=>list.ContainsKey(s)&&!list[s]).ToList();var gone=fresh.Where(s=>!list.ContainsKey(s)).ToList();
            var request=new Dictionary<string,object>{{"Suffixes",off}};var order=Store.FirstRun(game)?Store.FreshOrder(game):new List<string>();if(order.Count>0)request["Order"]=order;
            Store.SetFresh(game,new string[0]);freshSet.Clear();   // one try: after this the player's own choices in game stand
            if(off.Count==0)return;
            RunBusy("새로 넣은 데이터 모드를 다시 켜는 중…",()=>Backend.Run("DataOn",game,request),result=>{
                var names=off.Select(s=>{var r=working.FirstOrDefault(x=>x.Suffix==s);return r==null?s:r.Name;}).ToList();
                ReloadCatalog(false);
                ShowNotice("ok","게임이 첫 실행에서 꺼 둔 새 데이터 모드 "+off.Count+"개를 다시 켰습니다: "+String.Join(", ",names)+". 다음 게임 실행부터 적용됩니다."+(gone.Count>0?" (게임 목록에서 빠진 것 "+gone.Count+"개: 게임이 그 ModInfo.json 을 읽지 못한 모드)":""),null,null,null,null);
            },error=>ShowNotice("warn","새 데이터 모드를 다시 켜지 못했습니다: "+error+" 게임 안 모드 목록에서 켜 주세요.",null,null,null,null));
        }

        // No game mod list yet (the game has never run): the backend could not tick data mods, so the window keeps them
        // (Waiting on, Unwait forgotten, with the order) and ReviveFresh ticks them once the game has made its list.
        void RememberWaiting(Dictionary<string,object> result){
            var add=J.Strings(J.Get(result,"Waiting"));var drop=new HashSet<string>(J.Strings(J.Get(result,"Unwait")));var order=J.Strings(J.Get(result,"WaitingOrder"));
            if(add.Count==0&&drop.Count==0)return;
            Store.SetFresh(game,Store.Fresh(game).Where(s=>!drop.Contains(s)).Concat(add),true,order.Count>0?order:Store.FreshOrder(game));
            freshSet=new HashSet<string>(Store.Fresh(game));
        }
        bool firstRunNoticeShown;
        void ShowFirstRunNotice(){
            if(firstRunNoticeShown||busy||pending!=null||!DataMods.IsRealInstall(game)||!DataMods.ListMissing())return;firstRunNoticeShown=true;
            ShowNotice("warn","게임을 아직 한 번도 켜지 않아 게임 모드 목록 파일이 없습니다. DLL 모드는 저장하면 바로 적용되고, 데이터·창작마당 모드는 켜 두고 저장하면 모드매니저가 기억해 두었다가 게임을 타이틀 화면까지 한 번 켰다 끄면 자동으로 켭니다.","▶ 게임 실행",()=>{HideNotice();StartGame();},"닫기",HideNotice);
        }

        // ---------- conflicts and order ----------
        // Data folder of a row: data/workshop rows are their folder; a DLL+data mod's folder is in MODs or parked in DisabledData.
        string DataFolder(ModEntry r){
            if(r.IsData)return r.Path;if(!r.IsDll||r.Data=="")return null;
            string live=Path.Combine(game,"MODs",r.Data);if(Directory.Exists(live))return live;
            string parked=Path.Combine(game,"LOH_ModManager","DisabledData",r.Id,r.Data);return Directory.Exists(parked)?parked:null;
        }
        void ScanConflicts(){
            string folder=game;var mods=working.Select(r=>new{r,f=DataFolder(r)}).Where(x=>x.f!=null).Select(x=>Tuple.Create(x.r.Id,x.f,x.r.Suffix)).ToList();
            var infos=working.Where(r=>r.IsDll).Select(r=>Tuple.Create(r.Id,Manifests.For(r,game))).Where(x=>x.Item2!=null&&x.Item2.Changes.Count>0).ToList();
            string json=Path.Combine(game,"ThreeKingdom_Data","StreamingAssets","Json");
            Task.Run(()=>Tuple.Create(Conflicts.Find(mods,json).Concat(Manifests.Find(infos)).ToList(),mods.ToDictionary(m=>m.Item1,m=>Conflicts.Read(m.Item2,m.Item3))))
                .ContinueWith(t=>{if(folder!=game||t.IsFaulted)return;conflicts=t.Result.Item1;dataFacts=t.Result.Item2;Render();},TaskScheduler.FromCurrentSynchronizationContext());
        }
        const string MenuLinkPage="https://github.com/nugssorc/loh-menu-link/releases/latest";   // 메뉴 연결 모드: offered only when no order can fix a menu
        string WinnerId(Conflict c){bool confirmed;bool later=LaterWins(out confirmed);return (later?Position(c.A)>Position(c.B):Position(c.A)<Position(c.B))?c.A:c.B;}
        // Original cards' 무투 animations a mod brings (BattleCardAnimation\<id>.dat whose id the game has; a new card's file is the mod's own).
        // The game puts every card back to its own animation each time it reads a data mod, so these show only from the last data mod (game
        // check 11-03); a new card's animation stays wherever its mod is.
        HashSet<string> gameAnims;string gameAnimsFor;
        int OriginalAnims(string id){
            ModFacts f;if(id==null||!dataFacts.TryGetValue(id,out f)||f.Anims.Count==0)return 0;
            if(gameAnimsFor!=game){gameAnimsFor=game;gameAnims=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try{foreach(var p in Directory.GetFiles(Path.Combine(game,"ThreeKingdom_Data","StreamingAssets","BattleCardAnimation"),"*.dat"))gameAnims.Add(Path.GetFileNameWithoutExtension(p));}catch{}}
            return f.Anims.Count(a=>gameAnims.Contains(a));
        }
        // the reason when such a mod has a data mod after it (null when fine)
        string AnimProblem(ModEntry r,List<ModEntry> list){
            int n=r==null||!r.Enabled?0:OriginalAnims(r.Id);if(n==0)return null;
            bool confirmed;if(!LaterWins(out confirmed))return null;
            var on=list.Where(x=>x.Enabled&&dataFacts.ContainsKey(x.Id)).ToList();int me=on.FindIndex(x=>x.Id==r.Id);if(me<0)return null;
            var after=on.Skip(me+1).ToList();if(after.Count==0)return null;string first=after[0].Name;
            string text="이 모드는 원작 카드 "+n+"장의 무투 연출을 바꿉니다. 게임은 데이터 모드를 하나 읽을 때마다 카드 연출을 원작으로 되돌려서, 켜진 데이터 모드 중 맨 아래에 있어야 연출이 나옵니다(게임 확인). 지금 아래에 '"+first+"'"
                +(after.Count>1?" 등 "+after.Count+"개가":Particle(first,"이","가"))+" 있습니다. 맨 아래로 끌어 놓거나 '자동 정렬'을 누르세요.";
            var anim=on.Where(x=>OriginalAnims(x.Id)>0).ToList();
            if(anim.Count>1)text+="\n원작 카드 연출을 바꾸는 모드가 둘 이상이면 맨 아래 하나만 나옵니다: '"+anim.Last().Name+"'.";
            return text;
        }
        // A data mod with menus (InteractOption) but no ConversationCondition.json, redefining a menu row (same optionID) of a mod read
        // before it: its menus are dropped unless a mod read after it has that file (game check 11-03, two test mods sharing a row).
        // Without a shared row its menus show whatever the order (game check 11-03: 무협입지전 with no file mod at all, and with one only
        // above it). The reason when they are dropped (null when fine); safe: when fine now, which move would drop them.
        // Pictures two enabled data mods both bring (the image paths in their bundle manifests): the upper (first read) mod's shows,
        // the opposite of the tables (game check 11-03, two test mods on the tavern keeper's portrait). The note for r, null when none.
        string ImageNote(ModEntry r,List<ModEntry> list,out int count){
            count=0;ModFacts f;if(r==null||!r.Enabled||!dataFacts.TryGetValue(r.Id,out f)||f.Images.Count==0)return null;
            bool confirmed;bool later=LaterWins(out confirmed);var on=list.Where(x=>x.Enabled&&dataFacts.ContainsKey(x.Id)).ToList();int me=on.FindIndex(x=>x.Id==r.Id);if(me<0)return null;
            var notes=new List<string>();var all=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<on.Count;i++){if(i==me)continue;var o=on[i];var shared=dataFacts[o.Id].Images.Where(f.Images.Contains).ToList();if(shared.Count==0)continue;all.UnionWith(shared);
                bool up=later?me<i:me>i;string winner=up?r.Name:o.Name;string example=shared[0].StartsWith("Assets/Resource/Image/",StringComparison.OrdinalIgnoreCase)?shared[0].Substring(22):shared[0];
                notes.Add("'"+o.Name+"'"+Particle(o.Name,"과","와")+" 같은 그림 "+shared.Count+"장을 바꿉니다(예: "+example+"). 그림은 다른 데이터와 반대로 목록에서 위에 있는 모드의 그림이 우선합니다. 지금은 겹치는 그림에서 '"+winner+"'의 그림이 우선합니다."+(up?"":" 이 모드 그림을 쓰려면 '"+o.Name+"'보다 위로 옮기세요.")+" (모드매니저가 게임에서 직접 확인한 동작입니다.)");}
            count=all.Count;return notes.Count==0?null:String.Join("\n",notes);
        }
        bool SharesMenu(string a,string b){ModFacts fa,fb;HashSet<string> ra,rb;return a!=b&&dataFacts.TryGetValue(a,out fa)&&dataFacts.TryGetValue(b,out fb)&&fa.Rows.TryGetValue("InteractOption.json",out ra)&&fb.Rows.TryGetValue("InteractOption.json",out rb)&&ra.Overlaps(rb);}
        string MenuProblem(ModEntry r,List<ModEntry> list,out string safe){
            safe="";ModFacts f;if(r==null||!r.Enabled||!dataFacts.TryGetValue(r.Id,out f)||!f.Menu||f.MenuCond)return null;
            bool confirmed;bool later=LaterWins(out confirmed);var on=list.Where(x=>x.Enabled&&dataFacts.ContainsKey(x.Id)).ToList();int me=on.FindIndex(x=>x.Id==r.Id);if(me<0)return null;
            var sharer=(later?on.Take(me).Reverse():on.Skip(me+1)).FirstOrDefault(x=>SharesMenu(x.Id,r.Id));if(sharer==null)return null;
            var after=(later?on.Skip(me+1):on.Take(me).Reverse()).Where(x=>dataFacts[x.Id].MenuCond).ToList();
            if(after.Count>0){safe="지금 순서에서는 보이지만, '"+after.Last().Name+"'보다 "+(later?"아래":"위")+"로 옮기면 메뉴가 사라집니다.";return null;}
            var before=(later?on.Take(me).Reverse():on.Skip(me+1)).FirstOrDefault(x=>dataFacts[x.Id].MenuCond);
            // the fix: move it above the nearest mod with the file; only when there is none, the 메뉴 연결 모드 (an empty mod the player gets and puts last)
            return "이 모드의 NPC 선택지("+String.Join(", ",f.MenuNames.Take(4))+(f.MenuNames.Count>4?" …":"")+")가 지금 순서에서는 게임에 나오지 않습니다.\n\n왜 이런가요?\n이 모드는 '"+sharer.Name+"'"+Particle(sharer.Name,"과","와")+" 같은 번호의 선택지를 함께 고치는데, 메뉴 조건 파일(ConversationCondition.json)이 없습니다. 이럴 때 이 모드보다 뒤에 그 파일이 있는 모드가 없으면 이 모드의 선택지가 통째로 빠집니다. (모드매니저가 게임에서 직접 확인한 동작입니다. 같은 선택지를 함께 고치는 모드가 없으면 순서와 상관없이 나옵니다.)\n\n"
                +(before!=null?"해결 방법: '"+before.Name+"'보다 "+(later?"위":"아래")+"로 옮기세요. '자동 정렬'로도 맞출 수 있습니다."
                    :"해결 방법: 켜진 모드 중 그 파일이 있는 모드가 없어 순서로는 맞출 수 없습니다. '메뉴 연결 모드'를 받아 넣고 목록 맨 아래에 두세요. 빈 메뉴 표와 빈 메뉴 조건 파일만 든 모드라 아무것도 바꾸지 않습니다. 목록 맨 끝에서 선택지를 한 번 더 짜게 해서, 앞의 모든 모드의 선택지가 나옵니다. 받는 곳: "+MenuLinkPage);
        }
        // before a save: a menu mod that this order would leave without its menus (it was fine before)
        bool ConfirmMenuOrder(){
            foreach(var r in working.Where(x=>x.Enabled)){string safe;string now=MenuProblem(r,working,out safe);if(now==null)continue;
                var b=baseRows.FirstOrDefault(x=>x.Id==r.Id);string s2;if(b!=null&&MenuProblem(b,baseRows,out s2)!=null)continue;   // already so: the tag says it
                string msg="'"+r.Name+"'"+Particle(r.Name,"을","를")+" 이 순서로 저장하면 NPC 선택지가 게임에 나오지 않습니다.\n\n"+now.Substring(now.IndexOf("\n\n")+2)+"\n\n이대로 저장하면\n이 모드의 NPC 선택지가 게임에 나오지 않고, 카드에 '⚠ 메뉴 안 보임'이 붙습니다.";
                return Dialogs.Confirm(Window,"모드 순서 확인",msg,"그래도 저장");}
            return true;
        }
        bool LaterWins(out bool confirmed){return Store.LaterWins(game,out confirmed);}
        List<Conflict> ActiveConflicts(ModEntry r){
            var on=new HashSet<string>(working.Where(x=>x.Enabled).Select(x=>x.Id));
            return r.Enabled?conflicts.Where(c=>(c.A==r.Id||c.B==r.Id)&&on.Contains(c.A)&&on.Contains(c.B)).ToList():new List<Conflict>();
        }
        int Position(string id){return working.FindIndex(x=>x.Id==id);}
        // A patch is in order when it is applied after (later wins) / before (earlier wins) the mod it patches.
        bool PatchInOrder(Conflict c){bool confirmed;bool later=LaterWins(out confirmed);return later?Position(c.A)>Position(c.B):Position(c.A)<Position(c.B);}
        string NameOf(string id){var r=Row(id);return r==null?id:r.Name;}
        string Describe(Conflict c,string me){
            string other=NameOf(c.A==me?c.B:c.A);bool confirmed;bool later=LaterWins(out confirmed);
            if(c.Hard)return "함께 못 씀 — "+other+": "+c.Detail;
            if(c.Kind=="dll-hook")return "겹침 — "+other+": "+c.Detail+" · 대부분 함께 동작하지만, 이상하면 하나를 꺼 보세요";
            if(c.Kind=="patch")return (c.A==me?"이 모드는 "+other+"의 패치":other+"은 이 모드의 패치")+" — "+c.Detail+(PatchInOrder(c)?" · 순서 맞음":" · 순서 반대(자동 정렬)");
            return "겹침 — "+other+": "+c.Detail+" · 지금 적용되는 쪽 "+WinnerOf(c);
        }
        // override: the game takes the lower mod's row whole (game check 11-03), so the upper one loses what it changed that the lower row
        // puts back to the game's value; when the other way round nothing would be lost (the lower one copies the game's rows, or carries
        // every change of the upper one — a compatible version), swapping the two keeps both
        bool Lost(Conflict c,string id){return id==c.A?c.ALost:c.BLost;}
        bool UpperLoses(Conflict c){string w=WinnerId(c);return Lost(c,w==c.A?c.B:c.A);}
        bool OrderFixes(Conflict c){return UpperLoses(c)&&!Lost(c,WinnerId(c));}
        static string Few(List<string> fields){return String.Join(", ",fields.Take(4))+(fields.Count>4?" …":"");}
        // the clashing rows as the player reads them, worded by who is upper and lower now
        List<string> LinesOf(Conflict c){
            string w=WinnerId(c),up=NameOf(w==c.A?c.B:c.A),low=NameOf(w);bool aUp=w==c.B;var list=new List<string>();
            foreach(var x in c.Rows){
                if(x.Kind=="lines"){string hang=NameOf(x.ByA?c.A:c.B),swap=NameOf(x.ByA?c.B:c.A);
                    list.Add(((x.ByA?c.A:c.B)!=w?x.Item+": '"+hang+"'"+Particle(hang,"이","가")+" 이 줄에 붙인 결과·조건 "+x.Count+"줄이 아래 '"+swap+"'의 줄 교체로 사라질 수 있습니다."
                        :x.Item+": '"+swap+"'"+Particle(swap,"이","가")+" 이 줄을 통째로 바꾸지만, '"+hang+"'"+Particle(hang,"이","가")+" 붙인 결과·조건 "+x.Count+"줄은 그 뒤에 붙어 둘 다 적용됩니다."));continue;}
                if(x.Kind=="menu"){list.Add(x.Item+": 다른 칸 "+x.Diff.Count+"개("+Few(x.Diff)+") — 이름·내용은 아래 모드 것, 하위 메뉴는 둘 다 남음");continue;}
                if(x.A!=null&&x.B!=null&&(x.A.Count==0||x.B.Count==0)){string copy=NameOf(x.A.Count==0?c.A:c.B);list.Add(x.Item+": '"+copy+"'"+Particle(copy,"은","는")+" 원본 그대로입니다(바꾼 칸 없음).");continue;}
                if(x.Kind=="whole"){list.Add(x.Item+": 줄이 통째로 바뀝니다(다른 칸 "+x.Diff.Count+"개("+Few(x.Diff)+")). 위 모드가 이 줄에 붙인 결과·조건은 사라질 수 있습니다.");continue;}
                var cu=aUp?x.A:x.B;var cl=aUp?x.B:x.A;var back=cu!=null&&cl!=null?cu.Except(cl).ToList():new List<string>();   // the upper change the lower row puts back
                if(back.Count>0){list.Add(x.Item+": '"+up+"'"+Particle(up,"이","가")+" 바꾼 "+(x.Kind=="value"?"상수 ":"칸 ")+back.Count+"개("+Few(back)+")가 아래 '"+low+"' 줄"+(x.Kind=="value"?"에 덮여":"에서")+" 원래대로 돌아갑니다.");continue;}
                var both=cu!=null&&cl!=null?cu.Intersect(cl).Where(x.Diff.Contains).ToList():x.Diff;
                if(both.Count>0){list.Add(x.Item+": 두 모드가 같은 칸 "+both.Count+"개("+Few(both)+")를 다르게 바꿨습니다 — 아래 '"+low+"' 것이 적용됩니다.");continue;}
                list.Add(x.Item+": 위 '"+up+"'의 변경은 아래 '"+low+"' 줄에도 그대로 있습니다.");
            }
            return list;
        }
        string WinnerOf(Conflict c){bool confirmed;bool later=LaterWins(out confirmed);return (later?Position(c.A)>Position(c.B):Position(c.A)<Position(c.B))?NameOf(c.A):NameOf(c.B);}
        // The card tag's tooltip: who it clashes with, which rows by name, who wins now, and what to do. A mod that clashes a lot
        // shows a few rows and a few mods with the rest counted, so the tooltip stays a few lines.
        string TipOf(List<Conflict> mine,string me){
            const int MaxMods=4,MaxItems=5;bool confirmed;string low=LaterWins(out confirmed)?"아래":"위";var lines=new List<string>();
            foreach(var c in mine.Take(MaxMods)){
                if(c.Kind!="override"){lines.Add(Describe(c,me));continue;}
                string other=NameOf(c.A==me?c.B:c.A);bool mineWins=WinnerId(c)==me;
                bool loses=UpperLoses(c);
                if(loses&&mineWins)lines.Add(other+WithJosa(other)+" 같은 항목 "+c.Count+"개를 바꿉니다. 이 모드가 목록에서 "+low+"라 이 모드 것이 적용되고, "+other+"의 그 항목은 빠집니다.");
                else lines.Add(other+WithJosa(other)+" 같은 항목 "+c.Count+"개를 바꿉니다. 목록에서 "+low+"인 '"+WinnerOf(c)+"' 것이 "+(loses?"통째로 ":"")+"적용됩니다.");
                var rows=c.Rows.Count>0?LinesOf(c):c.Items;   // what differs, row by row
                foreach(var l in rows.Take(MaxItems))lines.Add("· "+l);if(rows.Count>MaxItems)lines.Add("· 외 "+(rows.Count-MaxItems)+"개");
                if(OrderFixes(c)){string copy=WinnerOf(c),moved=NameOf(WinnerId(c)==c.A?c.B:c.A);lines.Add("'"+copy+"'"+Particle(copy,"을","를")+" '"+moved+"' 위로 옮기면 둘 다 적용됩니다. '자동 정렬'로도 맞출 수 있습니다.");}
                else if(loses)lines.Add("순서로는 두 모드의 변경을 모두 살릴 수 없습니다. 더 중요한 모드를 아래로 끌어 놓으세요.");
                else lines.Add("함께 켜 두어도 됩니다. 위 칸만 순서에 따라 달라집니다. 우선하고 싶은 모드를 "+low+"로 끌어 놓으세요.");
            }
            if(mine.Count>MaxMods)lines.Add("그 밖에 모드 "+(mine.Count-MaxMods)+"개와도 겹칩니다.");
            var spots=mine.Take(MaxMods).Where(c=>c.Kind=="override"&&c.Where.Count>0).ToList();   // file · id only; the names are in the lines above
            if(spots.Count>0)lines.Add("");
            foreach(var c in spots)lines.Add("겹치는 자리"+(spots.Count>1?" — "+NameOf(c.A==me?c.B:c.A):"")+": "+String.Join(", ",c.Where.Take(MaxItems).Select(w=>String.Join(" · ",w.Split(new[]{" · "},StringSplitOptions.None).Take(2))))
                +(c.Where.Count>MaxItems?" 외 "+(c.Where.Count-MaxItems)+"개":""));
            return String.Join("\n",lines);
        }
        // 과/와 after a name: by the last Hangul syllable's final consonant, digits read in Korean; anything else 와.
        static string WithJosa(string name){
            char ch=name.TrimEnd().LastOrDefault();
            if(ch>='가'&&ch<='힣')return (ch-'가')%28!=0?"과":"와";
            if(ch>='0'&&ch<='9')return "013678".IndexOf(ch)>=0?"과":"와";
            return "와";
        }
        void ShowConflicts(ModEntry r){
            offOthersButton.Visibility=Visibility.Collapsed;
            string safe;string menu=MenuProblem(r,working,out safe);ModFacts facts;dataFacts.TryGetValue(r.Id,out facts);var extra=new List<string>();   // the mod's own files and the order its menus need
            if(menu!=null)extra.Add("⚠ 메뉴 안 보임 — "+menu);else if(safe!="")extra.Add("메뉴 — "+safe);
            if(facts!=null)foreach(var m in facts.Missing)extra.Add("⚠ 파일 빠짐 의심 — "+m);
            if(extra.Count>0)detailTech.Text=(detailTech.Text==""?"":detailTech.Text+"\n")+String.Join("\n",extra);
            var mine=ActiveConflicts(r);if(mine.Count==0)return;
            bool confirmed;bool later=LaterWins(out confirmed);
            string low=later?"아래":"위";
            detailTech.Text=(detailTech.Text==""?"":detailTech.Text+"\n")+String.Join("\n",mine.Select(c=>{var ls=c.Kind=="override"?LinesOf(c):new List<string>();return "⚠ "+Describe(c,r.Id)+(ls.Count>0?"\n   · "+String.Join("\n   · ",ls.Take(20))+(ls.Count>20?"\n   · 외 "+(ls.Count-20)+"개":""):"");}))
                +(mine.Any(c=>c.Kind=="override"&&!UpperLoses(c))?"\n두 모드를 함께 켜 두어도 됩니다. 겹치는 항목만 목록 "+low+"쪽 모드 것이 적용되고, 나머지는 둘 다 적용됩니다. 우선하고 싶은 모드를 "+low+"로 끌어 놓으세요. 이 표시는 순서와 관계없이 남습니다."+(confirmed?"":" (목록 "+low+"쪽이 우선한다는 것은 게임 확인 전 가정)")
                :"\n(같은 항목은 게임 모드 목록의 "+(later?"뒤":"앞")+"쪽이 이김"+(confirmed?" — 게임에서 확인함)":" — 게임 확인 전 가정)"))
                +String.Concat(mine.Where(c=>c.Kind=="override"&&c.Where.Count>0).Select(c=>"\n\n겹치는 자리 — "+NameOf(c.A==r.Id?c.B:c.A)+" (표 파일 · 항목 번호 · 이름)\n  "
                    +String.Join("\n  ",c.Where.Take(50))+(c.Where.Count>50?"\n  … 외 "+(c.Where.Count-50)+"개":"")));   // every clashing row, so the exact place can be looked up
            offOthersButton.Visibility=mine.Any(c=>c.Hard)?Visibility.Visible:Visibility.Collapsed;
        }
        // Data rows only; DLL and package rows keep their places. Data rows go back into the slots data rows held.
        void AutoSort(){
            if(busy)return;bool confirmed;bool later=LaterWins(out confirmed);var start=working.Select(r=>r.Id).ToList();
            var slots=working.Select((r,i)=>new{r,i}).Where(x=>x.r.IsData||(x.r.IsDll&&x.r.Data!="")).ToList();
            var ids=slots.Select(x=>x.r.Id).ToList();var sorted=Conflicts.Sort(ids,conflicts,later);
            bool patches=ids.Where((id,i)=>sorted[i]!=id).Any();
            // menus: a mod with NPC menus but no ConversationCondition.json goes just above the nearest mod that has one — unless that
            // would change who wins a row it shares with a mod it passes (then it is only said).
            var menuMoves=new List<string>();var menuKept=new List<string>();
            Func<int,bool> cond=k=>{ModFacts g;var x=Row(sorted[k]);return x!=null&&x.Enabled&&dataFacts.TryGetValue(sorted[k],out g)&&g.MenuCond;};
            if(later)for(int pass=0;pass<sorted.Count;pass++){bool again=false;
                for(int i=0;i<sorted.Count&&!again;i++){string id=sorted[i];var row=Row(id);ModFacts f;
                    if(row==null||!row.Enabled||!dataFacts.TryGetValue(id,out f)||!f.Menu||f.MenuCond)continue;
                    if(Enumerable.Range(i+1,sorted.Count-i-1).Any(cond))continue;   // one after it has the file: its menus show
                    if(!Enumerable.Range(0,i).Any(k=>{var o=Row(sorted[k]);return o!=null&&o.Enabled&&SharesMenu(sorted[k],id);}))continue;   // no row shared with a mod before it: they show anyway
                    int j=Enumerable.Range(0,i).Where(cond).DefaultIfEmpty(-1).Last();if(j<0)continue;
                    var passed=sorted.Skip(j).Take(i-j).ToList();string above=NameOf(sorted[j]);
                    if(passed.Any(p=>conflicts.Any(c=>c.Kind=="override"&&((c.A==id&&c.B==p)||(c.A==p&&c.B==id))))){string k="'"+row.Name+"'"+Particle(row.Name,"은","는")+" '"+above+"' 위로 옮기면 겹치는 항목의 이기는 쪽이 바뀌어 옮기지 않았습니다.";if(!menuKept.Contains(k))menuKept.Add(k);continue;}
                    sorted.RemoveAt(i);sorted.Insert(j,id);menuMoves.Add("'"+row.Name+"'"+Particle(row.Name,"을","를")+" '"+above+"' 위로 — 메뉴가 보이게");again=true;}
                if(!again)break;}
            // a mod whose clashing rows would put back changes of the mod above (copies of the game's rows) goes just above it when the other way round nothing is lost (the game takes the lower row whole,
            // game check 11-03) — unless that changes who wins a row it shares with a mod it passes (then it is only said)
            if(later)for(int pass=0;pass<sorted.Count;pass++){bool again=false;
                foreach(var c in conflicts.Where(x=>x.Kind=="override")){
                    int ia=sorted.IndexOf(c.A),ib=sorted.IndexOf(c.B);if(ia<0||ib<0)continue;
                    string upId=ia<ib?c.A:c.B,lowId=ia<ib?c.B:c.A;var ru=Row(upId);var rl=Row(lowId);
                    if(ru==null||rl==null||!ru.Enabled||!rl.Enabled||!Lost(c,upId)||Lost(c,lowId))continue;
                    int i=Math.Max(ia,ib),j=Math.Min(ia,ib);var passed=sorted.Skip(j+1).Take(i-j-1).ToList();
                    if(passed.Any(p=>conflicts.Any(o=>o.Kind=="override"&&((o.A==lowId&&o.B==p)||(o.A==p&&o.B==lowId))))){string k="'"+rl.Name+"'"+Particle(rl.Name,"은","는")+" '"+ru.Name+"' 위로 옮기면 겹치는 항목의 이기는 쪽이 바뀌어 옮기지 않았습니다.";if(!menuKept.Contains(k))menuKept.Add(k);continue;}
                    sorted.RemoveAt(i);sorted.Insert(j,lowId);menuMoves.Add("'"+rl.Name+"'"+Particle(rl.Name,"을","를")+" '"+ru.Name+"' 위로 — '"+ru.Name+"'의 변경이 원래대로 돌아가지 않게");again=true;break;}
                if(!again)break;}
            // original cards' 무투 animations show only from the last data mod: such a mod goes to the bottom, unless it passes a mod it shares rows
            // with or one that patches it (then it is only said)
            if(later){var anim=sorted.Where(id=>{var x=Row(id);return x!=null&&x.Enabled&&OriginalAnims(id)>0;}).ToList();
                foreach(var id in anim){var ra=Row(id);int i=sorted.IndexOf(id);
                    var passed=sorted.Skip(i+1).Where(p=>{var x=Row(p);return x!=null&&x.Enabled;}).ToList();
                    if(passed.All(anim.Contains))continue;   // nothing but other such mods below: as low as it gets
                    if(passed.Any(p=>conflicts.Any(o=>(o.Kind=="override"||o.Kind=="patch")&&((o.A==id&&o.B==p)||(o.A==p&&o.B==id))))){string k="'"+ra.Name+"'"+Particle(ra.Name,"은","는")+" 맨 아래로 옮기면 겹치는 항목의 이기는 쪽이 바뀌어 옮기지 않았습니다.";if(!menuKept.Contains(k))menuKept.Add(k);continue;}
                    sorted.Remove(id);sorted.Add(id);menuMoves.Add("'"+ra.Name+"'"+Particle(ra.Name,"을","를")+" 맨 아래로 — 원작 카드 연출은 맨 아래 모드 것만 나옵니다");}}
            int moved=ids.Where((id,i)=>sorted[i]!=id).Count();
            if(moved>0){var byId=working.ToDictionary(r=>r.Id);for(int k=0;k<slots.Count;k++)working[slots[k].i]=byId[sorted[k]];movedCards.UnionWith(working.Where(OneList.IsLinked).Select(r=>r.Id));}
            // cards holding only a game-file patch go to the top of the 켜짐 list, in their order (where a patch card sits among DLL and data
            // mods changes nothing: patches go into the file before the game starts); a card with data or DLL pieces stays, so their order does not move
            // first the last order in which every patch went in on this PC (PatchGoodOrder.json from the backend), among the patch cards' own places
            var good=new List<string>();string gatherGood="";try{good=J.Strings(J.Get(J.ReadFile(Path.Combine(game,"LOH_ModManager","PatchGoodOrder.json")),"Ids"));}catch{}
            if(good.Count>1){var ps=working.Select((r,i)=>new{r,i}).Where(x=>x.r.Enabled&&IsPatchRow(x.r)).ToList();
                var sortedPs=ps.Select((x,k)=>new{x.r,k,g=good.IndexOf(x.r.Id.Substring(4))}).OrderBy(x=>x.g<0?good.Count+x.k:x.g).ThenBy(x=>x.k).Select(x=>x.r).ToList();
                for(int k=0;k<ps.Count;k++)working[ps[k].i]=sortedPs[k];
                if(!ps.Select(x=>x.r).SequenceEqual(sortedPs))gatherGood=" 게임 파일 패치는 마지막으로 모두 들어갔던 순서로 맞췄습니다.";}
            var onPatch=working.Where(r=>r.Enabled&&IsPatchRow(r)).ToList();var gather=onPatch.Where(r=>r.PackageKind=="패치"&&Family(r).Count<=1).ToList();int gathered=0;
            if(gather.Count>0){var was=working.Select(r=>r.Id).ToList();foreach(var g in gather)working.Remove(g);int top=working.FindIndex(r=>r.Enabled);if(top<0)top=working.Count;working.InsertRange(top,gather);
                gathered=gather.Count(g=>was.IndexOf(g.Id)!=working.IndexOf(g));}
            string gatherNote=gathered>0?" 게임 파일 패치 "+gathered+"개를 맨 위에 모았습니다"+(onPatch.Count>gather.Count?"(데이터·DLL 이 함께 든 카드는 제자리)":"")+".":"";
            // game-file patches: one seen not to go in under another (자리 찾기, or a save where it then failed) goes just above it
            var pslots=working.Select((r,i)=>new{r,i}).Where(x=>IsPatchRow(x.r)&&x.r.Enabled).ToList();var pids=pslots.Select(x=>x.r.Id.Substring(4)).ToList();var porig=pids.ToList();
            for(int pass=0;pass<pids.Count*pids.Count;pass++){bool again=false;
                foreach(var d in catalog.Installed){string pid=J.S(d,"Id");int me=pids.IndexOf(pid);if(me<0)continue;
                    foreach(var a in J.Items(J.Get(d,"Above"))){string other=J.S(a,"Id");int at=pids.IndexOf(other);if(at<0||at>me)continue;
                        pids.RemoveAt(me);pids.Insert(at,pid);string nm=J.S(d,"Name");menuMoves.Add("'"+nm+"'"+Particle(nm,"을","를")+" '"+NameOf("pkg:"+other)+"' 위로 — 그 아래에서는 들어가지 않습니다");again=true;break;}
                    if(again)break;}
                if(!again)break;}
            int pmoved=pids.Where((x,i)=>porig[i]!=x).Count();
            if(pmoved>0){var byId=working.ToDictionary(r=>r.Id);for(int k=0;k<pslots.Count;k++)working[pslots[k].i]=byId["pkg:"+pids[k]];}
            moved=working.Select((r,i)=>start[i]!=r.Id).Count(b=>b);if(moved>0)Render();
            string kept=menuKept.Count>0?" "+String.Join(" ",menuKept):"";
            ShowNotice(moved>0?"info":"ok",moved>0?"모드 "+moved+"개의 순서를 바꿨습니다(저장 전)"+(menuMoves.Count>0?": "+String.Join("; ",menuMoves)+".":".")+gatherGood+gatherNote+(patches?" 패치·번역은 원본 모드 "+(later?"아래":"위")+"로 옮겼습니다.":"")+(confirmed?"":" 같은 부분에서 목록 "+(later?"아래":"위")+"쪽이 우선한다는 것은 아직 게임에서 확인하지 않았습니다.")+kept
                :"지금 순서가 규칙에 맞습니다. 바꿀 것이 없습니다."+kept,null,null,null,null);
        }
        // 게시글 / 패치노트 icons: the card (ModRowView) or one of its workshop patch lines (SubRowView); the card gets picked first
        void IconClick(object sender,RoutedEventArgs e){
            var b=e.OriginalSource as Button;string what=b==null?null:b.Tag as string;if(what==null)return;e.Handled=true;
            var v=b.DataContext as ModRowView;var sub=b.DataContext as SubRowView;var r=Row(v!=null?v.Id:sub!=null?sub.Owner:null);if(r==null)return;
            selectedId=r.Id;Render();
            if(sub!=null){var p=Patches(null).FirstOrDefault(x=>x.Id==sub.Id);if(p==null)return;
                if(what=="post"){string link=PatchLink(p);if(link!="")OpenLink(link);else EditPatchLink(p);}else ShowHistory(new ModEntry{Id="pkg:"+p.Id,Type="package",Name=p.Name},p);return;}
            if(what=="post"){string link=LinkOf(r);if(link!="")OpenLink(link);else EditLink(r);}else ShowHistory(r);
        }
        void TurnOffClashing(ModEntry r){
            if(r==null)return;var others=ActiveConflicts(r).Where(c=>c.Hard).Select(c=>c.A==r.Id?c.B:c.A).Distinct().ToList();
            foreach(var id in others){var o=Row(id);if(o!=null&&!o.Locked)o.Enabled=false;}
            selectedId=r.Id;Render();ShowNotice("info","함께 켤 수 없는 모드 "+others.Count+"개를 껐습니다(저장 전): "+String.Join(", ",others.Select(NameOf)),null,null,null,null);
        }

        // ---------- delete (trash) and entry absorption ----------
        OneList.Owner OwnerOf(ModEntry r){
            OneList.Owner o=null;if(r==null)return null;
            if(r.Type=="package")owners.TryGetValue("pkg:"+r.Id.Substring(4),out o);
            else if(r.IsDll&&r.External)owners.TryGetValue("ext:"+r.Id,out o);
            else if(r.Type=="data"&&r.Suffix!="")owners.TryGetValue("sfx:"+r.Suffix,out o);
            return o;
        }
        // One place to delete any local mod: installed packages by their record, the manager's own DLL mods and MODs folders with
        // the backed-up removal, a plain registered DLL by unregistering. Workshop mods are Steam's: unsubscribe there.
        void DeleteRow(ModEntry r){
            if(r==null||busy)return;
            if(r.Type=="workshop"){string ws=Path.GetFileName(r.Path);
                ShowNotice("info","'"+r.Name+"'은 창작마당 구독 모드라 여기서 지우지 않습니다(지워도 Steam 이 다시 받습니다). Steam 에서 구독을 해지하세요. 그때까지는 꺼짐으로 두면 됩니다.","창작마당 페이지 열기",()=>{try{Process.Start("steam://url/CommunityFilePage/"+ws);}catch(Exception e){MessageBox.Show(Window,e.Message);}},null,null);return;}
            if(r.Parked){
                if(Dirty()){MessageBox.Show(Window,"먼저 지금 변경을 저장해 주세요.","모드매니저");return;}
                if(!Dialogs.Confirm(Window,"모드 삭제 확인","삭제: "+r.Name+" (꺼 둔 패치)\n\n게임 파일은 이미 원래대로입니다. 목록에서 빼서 휴지통으로 옮깁니다. '설치 정보' 탭의 '지운 모드 되살리기'로 다시 넣을 수 있습니다.","삭제"))return;
                string pid=r.Id.Substring(4);string pname=r.Name;
                RunBusy("삭제하는 중… "+pname,()=>Backend.Run("ParkedRemove",game,new Dictionary<string,object>{{"Id",pid}}),result=>{selectedId=null;ReloadCatalog(true);if(packagesLoaded)LoadPackages();ShowNotice("ok","'"+pname+"'을 휴지통으로 옮겼습니다.",null,null,null,null);},null);
                return;
            }
            var owner=OwnerOf(r);
            if(owner!=null){UninstallPackageById(owner.Id);return;}
            if(r.IsDll&&r.External){selectedId=r.Id;Unregister();return;}
            if(r.IsDll){RemoveInstalled("managed:"+r.Id,r.Name);return;}
            if(r.Type=="data"){RemoveInstalled("data:"+r.Path,r.Name);return;}
            ShowNotice("warn","'"+r.Name+"'은 여기서 지울 수 없습니다.",null,null,null,null);
        }
        // several picked rows: one list, one confirmation, then each removed the way it would be alone (no more questions)
        void DeleteMany(List<ModEntry> rows){
            if(busy||rows.Count==0)return;if(rows.Count==1){DeleteRow(rows[0]);return;}
            var steam=rows.Where(r=>r.Type=="workshop").ToList();var listOnly=rows.Where(r=>r.IsDll&&r.External&&OwnerOf(r)==null).ToList();
            var jobs=new List<Tuple<string,string,string>>();   // kind, key or id, name
            foreach(var r in rows.Except(steam).Except(listOnly)){
                var o=OwnerOf(r);if(o!=null){if(!jobs.Any(j=>j.Item1=="package"&&j.Item2==o.Id))jobs.Add(Tuple.Create("package",o.Id,o.Name));}
                else if(r.IsDll)jobs.Add(Tuple.Create("remove","managed:"+r.Id,r.Name));
                else if(r.Type=="data")jobs.Add(Tuple.Create("remove","data:"+r.Path,r.Name));
            }
            if(jobs.Count>0&&listOnly.Count>0){ShowNotice("info","'DLL 파일 추가'로 넣은 모드("+String.Join(", ",listOnly.Select(r=>r.Name))+")는 목록에서 빼는 것이라 저장이 필요합니다. 그것들만 따로 골라 삭제하거나, 나머지를 먼저 삭제하세요.",null,null,null,null);return;}
            if(jobs.Count==0&&listOnly.Count>0){foreach(var r in listOnly){working.Remove(r);if(baseRows.Any(b=>b.Id==r.Id))unregistered.Add(r.Id);}selectedId=null;Render();
                ShowNotice("info",listOnly.Count+"개를 목록에서 뺍니다: "+String.Join(", ",listOnly.Select(r=>r.Name))+". 저장하면 적용되며, DLL 파일은 그대로 둡니다.",null,null,null,null);return;}
            if(jobs.Count==0){DeleteRow(steam[0]);return;}
            if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 삭제할 수 있습니다. 실행 중 DLL은 빼지 않습니다.","모드매니저");return;}
            if(watchId!=""){MessageBox.Show(Window,"'"+watchName+"' 수동 설치를 기록하는 중입니다. 먼저 '설치 끝' 또는 '기록 취소'를 눌러 주세요.","모드매니저");return;}
            var lines=new List<string>{"고른 모드 "+jobs.Count+"개를 삭제합니다:",""};lines.AddRange(jobs.Select(j=>"· "+j.Item3+(j.Item1=="package"?" (모드매니저로 설치 — 함께 들어간 것 모두)":"")));
            if(steam.Count>0){lines.Add("");lines.Add("창작마당 구독 모드는 여기서 지우지 않습니다(Steam 에서 구독 해지): "+String.Join(", ",steam.Select(r=>r.Name)));}
            lines.Add("");lines.Add("넣었던 파일은 보관함으로 옮기고, 덮어썼던 게임 원본 파일은 되돌립니다. '설치 정보' 탭의 '지운 모드 되살리기'로 하나씩 되돌릴 수 있습니다.");
            if(!Dialogs.Confirm(Window,"모드 "+jobs.Count+"개 삭제 확인",String.Join(Environment.NewLine,lines),jobs.Count+"개 삭제"))return;
            string folder=game;
            RunBusy("고른 모드 "+jobs.Count+"개를 삭제하는 중…",()=>{
                var done=new List<string>();var failed=new List<string>();
                foreach(var j in jobs){
                    try{
                        if(j.Item1=="package")Backend.Run("UninstallPackage",folder,new Dictionary<string,object>{{"Id",j.Item2}});
                        else{var plan=Backend.Run("RemovalPlan",folder,new Dictionary<string,object>{{"Key",j.Item2}});Backend.Run("Remove",folder,new Dictionary<string,object>{{"Key",j.Item2},{"Token",J.S(plan,"Token")}});}
                        done.Add(j.Item3);
                    }catch(Exception x){failed.Add(j.Item3+" ("+x.Message+")");}
                }
                return Tuple.Create(done,failed);
            },result=>{
                selectedId=null;ReloadCatalog(false);if(packagesLoaded)LoadPackages();if(inventory!=null)ScanInventory();
                ShowNotice(result.Item2.Count>0?"warn":"ok",result.Item1.Count+"개를 삭제했습니다"+(result.Item1.Count>0?": "+String.Join(", ",result.Item1):"")+"."+(result.Item2.Count>0?"\n삭제하지 못함: "+String.Join(", ",result.Item2):"")+" 되돌리려면 '설치 정보' 탭의 '지운 모드 되살리기'.",null,null,null,null);
            },null);
        }
        void UninstallPackageById(string id){
            var item=catalog.Installed.FirstOrDefault(d=>J.S(d,"Id")==id);if(item==null){ShowNotice("warn","설치 기록을 찾지 못했습니다. 새로고침 뒤 다시 해 주세요.",null,null,null,null);return;}
            if(PackageBlocked("삭제할",true))return;string name=J.S(item,"Name");
            var message=new List<string>{"삭제: "+name+" (모드매니저로 설치한 모드 — 함께 들어간 것 모두)",""};
            message.AddRange(J.Items(J.Get(item,"Parts")).Select(x=>"· "+PartLabel(J.S(x,"Kind"))+" "+J.S(x,"Title")+" (파일 "+J.S(x,"Files")+"개)"));
            message.Add("");message.Add("넣었던 파일은 보관함으로 옮기고, 덮어썼던 게임 원본 파일은 되돌립니다. '설치 정보' 탭의 '지운 모드 되살리기'로 다시 넣을 수 있습니다.");
            if(J.Items(J.Get(item,"Parts")).Any(x=>J.S(x,"Kind")=="Patcher"||J.S(x,"Kind")=="FilePatch"))message.Add("패치: 게임 파일을 설치 전 원본으로 되돌립니다(원본이 없는 파일은 스팀 「게임 파일 무결성 확인」으로).");
            if(!Dialogs.Confirm(Window,"모드 삭제 확인",String.Join(Environment.NewLine,message),"삭제"))return;
            RunBusy("삭제하는 중… "+name,()=>Backend.Run("UninstallPackage",game,new Dictionary<string,object>{{"Id",id}}),result=>{
                var kept=J.Strings(J.Get(result,"Kept"));string note=J.S(result,"Note");selectedId=null;
                ReloadCatalog(true);if(packagesLoaded)LoadPackages();
                ShowNotice(kept.Count>0||note.Contains("못했")?"warn":"ok","'"+name+"'을 삭제했습니다."+(kept.Count>0?" 설치 뒤 바뀐 파일 "+kept.Count+"개는 남겼습니다.":"")+(note!=""?" "+note:"")+" 되돌리려면 '설치 정보' 탭의 '지운 모드 되살리기'.",null,null,null,null);
            },null);
        }
        void ShowEntryButton(ModEntry r){
            var o=OwnerOf(r);entryButton.Visibility=Visibility.Collapsed;if(o==null)return;
            if(o.Kind=="ProxyDll"){entryButton.Content="모드매니저로 함께 실행";entryButton.ToolTip="게임 폴더의 "+o.Original+" 를 다른 이름으로 바꿔 두고 모드매니저가 불러오게 합니다. 그러면 목록에서 켜고 끌 수 있고, 같은 파일 이름을 쓰는 다른 모드와 함께 쓸 수 있습니다.";entryButton.Visibility=Visibility.Visible;}
            else if(o.Kind=="AbsorbedDll"){entryButton.Content="원래 방식으로 되돌리기 ("+o.Original+")";entryButton.ToolTip="모드매니저가 실행하던 것을 풀고, 제작자가 정한 대로 게임 폴더에 "+o.Original+" 이름으로 되돌립니다. 모드매니저로 실행했을 때 게임에서 잘 안 되면 쓰세요.";entryButton.Visibility=Visibility.Visible;}
            entryButton.IsEnabled=!busy;
        }
        void SwitchEntry(ModEntry r){
            var o=OwnerOf(r);if(o==null||busy)return;bool absorb=o.Kind=="ProxyDll";
            if(PackageBlocked(absorb?"바꿀":"되돌릴"))return;
            RunBusy(absorb?"모드매니저로 함께 실행하도록 바꾸는 중…":"원래 방식으로 되돌리는 중…",()=>Backend.Run("PackageEntry",game,new Dictionary<string,object>{{"Id",o.Id},{"Absorb",absorb}}),result=>{
                selectedId=null;ReloadCatalog(false);if(packagesLoaded)LoadPackages();
                ShowNotice("ok",absorb?"'"+o.Name+"'의 "+o.Original+" 를 모드매니저가 함께 실행하도록 바꿨습니다. 목록에 켜진 DLL 모드로 들어갔습니다. 게임에서 잘 되는지 확인하고, 안 되면 그 모드의 '원래 방식으로 되돌리기'를 누르세요."
                    :"'"+o.Name+"'을 원래 방식("+o.Original+")으로 되돌렸습니다. 이제 이 모드는 모드매니저 목록에서 켜고 끌 수 없습니다.",null,null,null,null);
            },null);
        }

        // ---------- profiles ----------
        List<Profile> Profiles(){if(profileCache==null)try{profileCache=Store.Profiles(game);}catch{profileCache=new List<Profile>();}return profileCache;}
        Profile Snapshot(string name){var p=new Profile{Name=name,Saved=DateTime.Now};foreach(var r in working)p.Items.Add(new ProfileItem{Id=r.Id,Name=r.Name,Path=r.Path,Route=r.Route,Enabled=r.Enabled,External=r.External});return p;}
        string MatchingProfile(){
            var on=working.Where(r=>r.Enabled&&!r.Locked).Select(r=>r.Id+"@"+r.Route.ToLowerInvariant()).ToList();var onDll=working.Where(r=>r.Enabled&&r.IsDll).Select(r=>r.Id+"@"+r.Route.ToLowerInvariant()).ToList();
            foreach(var p in Profiles()){bool withData=p.Items.Any(i=>IsDataId(i.Id));if(p.Items.Where(i=>i.Enabled&&!i.Id.StartsWith("pkg:")).Select(i=>i.Id+"@"+i.Route.ToLowerInvariant()).SequenceEqual(withData?on:onDll))return p.Name;}
            return null;
        }
        // ---------- 수동으로 설치(기록) ----------
        void FindOpenWatch(){
            try{
                string root=Path.Combine(game,"LOH_ModManager","Installed");if(!Directory.Exists(root))return;
                foreach(var dir in Directory.GetDirectories(root)){var r=J.ReadFile(Path.Combine(dir,"record.json"));if(r!=null&&J.S(r,"State")=="Watching"){watchId=J.S(r,"Id");watchName=J.S(r,"Name");return;}}
            }catch{}
        }
        void StartWatch(string name,string source,string stage){
            if(busy)return;if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 시작할 수 있습니다.","모드매니저");if(stage!="")DiscardStage(stage);return;}
            if(Dirty()){MessageBox.Show(Window,"저장하지 않은 변경이 있습니다. 먼저 저장하거나 '되돌리기'를 누른 뒤 시작해 주세요.","모드매니저");if(stage!="")DiscardStage(stage);return;}
            RunBusy("설치 전 게임 폴더 상태를 기록하는 중… (게임 본체 파일 백업에 몇 초)",()=>Backend.Run("WatchStart",game,new Dictionary<string,object>{{"Name",name??""},{"Source",source??""},{"StageId",stage??""}}),result=>{
                watchId=J.S(result,"Id");watchName=J.S(result,"Name");
                watchEntryNote=String.Join("\n",J.Items(J.Get(result,"EntryMoved")).Select(m=>"'"+watchName+"'"+Particle(watchName,"이","가")+" 게임 입구("+J.S(m,"From")+") 자리를 써서, 모드매니저 입구를 "+J.S(m,"To")+" 로 옮겨 둡니다. 따로 하실 일은 없습니다."));
                ShowWatchNotice();
                string content=stage==""?"":Path.Combine(Store.Folder(game),"Staging",stage,"content");
                watchContent=content;if(content!=""&&Directory.Exists(content)){try{Process.Start("explorer.exe","\""+content+"\"");}catch{}FollowInstallers(content);}   // the unpacked archive: its installer is run from here, and followed
            },error=>{MessageBox.Show(Window,error,"기록을 시작하지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Warning);if(stage!="")DiscardStage(stage);});
        }
        void ShowWatchNotice(){
            ShowNotice("warn",(watchEntryNote!=""?watchEntryNote+"\n":"")+(installerRunning?"수동으로 설치하는 중: "+watchName+"\n설치 창이 실행 중입니다. 설치를 끝까지 진행해 창을 닫으면 모드매니저가 알아서 기록을 끝냅니다.":
                "수동으로 설치하는 중: "+watchName+"\n열린 폴더에서 그 모드의 설치 파일(.bat·.exe)을 실행하세요. 설치 창이 닫히면 모드매니저가 알아서 기록을 끝냅니다. 파일을 직접 넣는 모드라면 다 넣은 뒤 '설치 끝'을 누르세요. 그동안 게임은 켜지 마세요."),
                "설치 끝",FinishWatch,"기록 취소",CancelWatch);
        }
        // ---------- the installer's start and end, seen from here ----------
        // Whatever the player starts from the unpacked folder (a .bat runs in cmd.exe with its path on the command line; an .exe or the
        // patch's own python lives in it) is followed, and what those start in turn. Once one was seen and none has been left for a few
        // seconds, the recording ends by itself. A mod copied in by hand still ends with '설치 끝'.
        System.Threading.Timer installerTimer;string installerFolder="";HashSet<int> installerPids=new HashSet<int>();bool installerRunning;int installerQuiet;
        void FollowInstallers(string folder){
            StopFollowingInstallers();installerFolder=Path.GetFullPath(folder).TrimEnd('\\');installerPids=new HashSet<int>();installerRunning=false;installerQuiet=-1;
            installerTimer=new System.Threading.Timer(_=>PollInstallers(),null,1000,1000);
        }
        void StopFollowingInstallers(){var t=installerTimer;installerTimer=null;if(t!=null)t.Dispose();installerFolder="";installerRunning=false;}
        void PollInstallers(){
            string folder=installerFolder;if(folder=="")return;
            var all=new List<Tuple<int,int,string,string>>();
            try{
                using(var q=new System.Management.ManagementObjectSearcher("SELECT ProcessId,ParentProcessId,Name,CommandLine,ExecutablePath FROM Win32_Process"))
                    foreach(System.Management.ManagementObject o in q.Get())using(o)all.Add(Tuple.Create(Convert.ToInt32(o["ProcessId"]),Convert.ToInt32(o["ParentProcessId"]),(o["Name"] as string)??"",((o["CommandLine"] as string)??"")+"|"+((o["ExecutablePath"] as string)??"")));
            }catch{return;}
            int self=Process.GetCurrentProcess().Id;var alive=new HashSet<int>();
            for(bool grew=true;grew;){grew=false;
                foreach(var p in all){if(alive.Contains(p.Item1)||p.Item1==self||p.Item2==self||p.Item3.Equals("explorer.exe",StringComparison.OrdinalIgnoreCase))continue;
                    if(installerPids.Contains(p.Item1)||alive.Contains(p.Item2)||p.Item4.IndexOf(folder,StringComparison.OrdinalIgnoreCase)>=0){alive.Add(p.Item1);grew=true;}}}
            installerPids=alive;   // only the living: a number Windows hands out again later is not taken for the installer
            if(alive.Count>0){installerQuiet=0;if(!installerRunning){installerRunning=true;Window.Dispatcher.BeginInvoke(new Action(()=>{if(watchId!="")ShowWatchNotice();}));}return;}
            if(installerQuiet<0)return;   // nothing started yet
            if(++installerQuiet<3)return;
            Window.Dispatcher.BeginInvoke(new Action(()=>{
                if(watchId==""||installerTimer==null){StopFollowingInstallers();return;}
                if(busy||live.Game!=null)return;   // the next tick tries again
                StopFollowingInstallers();FinishWatch(true);
            }));
        }
        void FinishWatch(){FinishWatch(false);}
        void FinishWatch(bool auto){   // auto: the installer it followed has ended by itself
            if(busy||watchId=="")return;if(live.Game!=null){MessageBox.Show(Window,"게임을 끈 뒤에 '설치 끝'을 눌러 주세요.","모드매니저");return;}
            string id=watchId;StopFollowingInstallers();
            RunBusy("설치 전과 비교해 바뀐 파일을 찾는 중…",()=>Backend.Run("WatchFinish",game,new Dictionary<string,object>{{"Id",id}}),result=>{
                watchId="";watchName="";
                var ticked=J.Strings(J.Get(result,"Selection"));if(ticked.Count>0){Store.SetFresh(game,Store.Fresh(game).Concat(ticked),Store.FirstRun(game),Store.FreshOrder(game));freshSet=new HashSet<string>(Store.Fresh(game));}
                RememberWaiting(result);
                string note=J.S(result,"Note"),selNote=J.S(result,"SelectionNote");
                ShowNotice(note!=""?"warn":"ok","'"+J.S(result,"Name")+"' 수동 설치를 기록했습니다: 새 파일 "+J.S(result,"Added")+"개 · 바뀐 파일 "+J.S(result,"Changed")+"개 · 지운 파일 "+J.S(result,"Removed")+"개. '설치 정보' 탭에서 보고 제거할 수 있습니다."+(note!=""?"\n"+note:"")+(selNote!=""?" "+selNote:""),"설치 정보 보기",()=>{HideNotice();selectedPackage=J.S(result,"Id");ShowTab(1);LoadPackages();},null,null);
                ReloadCatalog(true);if(packagesLoaded)LoadPackages();if(monitor!=null)monitor.Poke(true);
            },error=>{
                // the installer ended without changing anything: most often the maker's program stopped itself (it says why in its window)
                if(auto&&error.Contains("바뀐 파일이 없습니다")){
                    if(watchContent!=""&&Directory.Exists(watchContent))FollowInstallers(watchContent);   // run again from the open folder: followed again
                    ShowNotice("warn","설치 프로그램이 끝났지만 게임 폴더에 바뀐 파일이 없습니다. 설치 프로그램 창의 메시지를 확인하세요. 제작자 프로그램이 스스로 설치를 멈췄을 수 있습니다(예: 다른 패치와 함께 쓸 수 없음). 다시 하려면 열린 폴더에서 한 번 더 실행하고, 그만두려면 '기록 취소'를 누르세요.","설치 끝",FinishWatch,"기록 취소",CancelWatch);return;}
                MessageBox.Show(Window,error,"기록을 끝내지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Warning);ShowWatchNotice();});
        }
        void CancelWatch(){
            if(busy||watchId=="")return;
            if(MessageBox.Show(Window,"'"+watchName+"' 기록을 취소할까요?\n그동안 설치기가 넣은 파일은 그대로 남고, 모드매니저의 기록만 지웁니다(나중에 모드매니저로 제거할 수 없음).","기록 취소",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            string id=watchId;StopFollowingInstallers();
            RunBusy("기록을 취소하는 중…",()=>Backend.Run("WatchCancel",game,new Dictionary<string,object>{{"Id",id}}),result=>{watchId="";watchName="";HideNotice();ShowNotice("info","수동 설치 기록을 취소했습니다.",null,null,null,null);},
                error=>{MessageBox.Show(Window,error,"기록을 취소하지 못했습니다");ShowWatchNotice();});
        }

        void ShowProfileMenu(){
            var menu=new ContextMenu{PlacementTarget=profileButton,Placement=PlacementMode.Bottom};
            var save=new MenuItem{Header="지금 목록을 프로필로 저장…"};save.Click+=(s,e)=>SaveProfile();menu.Items.Add(save);
            var profiles=Profiles();if(profiles.Count>0)menu.Items.Add(new Separator());
            foreach(var p in profiles){
                var profile=p;var item=new MenuItem{Header=p.Name+"   ("+p.Items.Count(i=>i.Enabled)+"개 켜짐)"};
                var load=new MenuItem{Header="불러오기"};load.Click+=(s,e)=>LoadProfile(profile);item.Items.Add(load);
                var export=new MenuItem{Header="코드로 내보내기 (복사)"};export.Click+=(s,e)=>{Clipboard.SetText(Store.Export(profile));ShowNotice("info","'"+profile.Name+"' 프로필 코드를 복사했습니다. 다른 PC의 모드매니저에서 '코드로 가져오기'에 붙여 넣으면 됩니다.",null,null,null,null);};item.Items.Add(export);
                var retire=new MenuItem{Header="지우기"};retire.Click+=(s,e)=>{if(MessageBox.Show(Window,"'"+profile.Name+"' 프로필을 지울까요? (지운 프로필 보관함으로 옮깁니다)","프로필",MessageBoxButton.YesNo)==MessageBoxResult.Yes){Store.RetireProfile(game,profile.Name);profileCache=null;RenderBand();}};item.Items.Add(retire);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var import=new MenuItem{Header="코드로 가져오기…"};import.Click+=(s,e)=>ImportProfile();menu.Items.Add(import);
            menu.IsOpen=true;
        }
        void SaveProfile(){
            string name=Dialogs.Ask(Window,"프로필 저장","프로필 이름 (예: 평소 플레이, 패드 시험) — 켜 둔 모드와 순서를 저장합니다",MatchingProfile()??"",false);
            if(String.IsNullOrWhiteSpace(name))return;name=name.Trim();
            if(Profiles().Any(p=>p.Name==name)&&MessageBox.Show(Window,"같은 이름의 프로필이 있습니다. 지금 목록으로 바꿀까요?","프로필",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            try{Store.SaveProfile(game,Snapshot(name));profileCache=null;}catch(Exception e){MessageBox.Show(Window,e.Message,"프로필을 저장하지 못했습니다");return;}
            ShowNotice("ok","'"+name+"' 프로필을 저장했습니다. 게임 파일은 바뀌지 않았습니다.",null,null,null,null);RenderBand();
        }
        void ImportProfile(){
            string code=Dialogs.Ask(Window,"프로필 가져오기","내보내기로 복사한 코드("+Store.CodePrefix+"…)를 붙여 넣으세요",Clipboard.ContainsText()&&Clipboard.GetText().StartsWith(Store.CodePrefix)?Clipboard.GetText():"",true);
            if(String.IsNullOrWhiteSpace(code))return;Profile p;
            try{p=Store.Import(code);}catch(Exception e){MessageBox.Show(Window,e.Message,"프로필 코드를 읽지 못했습니다");return;}
            if(Profiles().Any(x=>x.Name==p.Name)&&MessageBox.Show(Window,"'"+p.Name+"' 프로필이 이미 있습니다. 바꿀까요?","프로필",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            Store.SaveProfile(game,p);profileCache=null;ShowNotice("ok","'"+p.Name+"' 프로필을 가져왔습니다. 프로필 메뉴에서 불러오면 목록에 적용됩니다.",null,null,null,null);RenderBand();
        }
        // Loading only changes the lists; nothing reaches the game folder until Save.
        static bool IsDataId(string id){return id.StartsWith("data:")||id.StartsWith("ws:");}
        void LoadProfile(Profile p){
            var byId=p.Items.ToDictionary(i=>i.Id);int missing=0,turnedOff=0;bool withData=p.Items.Any(i=>IsDataId(i.Id));
            foreach(var r in working){
                ProfileItem item;if(r.Locked||(r.IsData&&!withData))continue;   // profiles from before 0.7 hold DLL rows only
                if(byId.TryGetValue(r.Id,out item)){r.Enabled=item.Enabled;if(Routes.Contains(item.Route,StringComparer.OrdinalIgnoreCase))r.Route=item.Route;}
                else if(r.Enabled){r.Enabled=false;turnedOff++;}
            }
            foreach(var item in p.Items.Where(i=>i.External&&working.All(r=>r.Id!=i.Id&&!SamePath(r.Path,i.Path)))){
                if(!File.Exists(item.Path)){if(item.Enabled)missing++;continue;}
                working.Add(new ModEntry{Id=System.Text.RegularExpressions.Regex.IsMatch(item.Id,"^ext_[0-9a-f]{32}$")?item.Id:"ext_"+Guid.NewGuid().ToString("N"),Name=item.Name,Path=item.Path,Route=item.Route==""?"version.dll":item.Route,External=true,Present=true,Enabled=item.Enabled});
            }
            foreach(var item in p.Items.Where(i=>!i.External&&i.Enabled&&!i.Id.StartsWith("pkg:")&&working.All(r=>r.Id!=i.Id)))missing++;
            var order=p.Items.Select(i=>i.Id).ToList();
            working=working.Select((r,i)=>new{r,i}).OrderBy(x=>{int k=order.IndexOf(x.r.Id);return k<0?order.Count+x.i:k;}).Select(x=>x.r).ToList();movedCards.UnionWith(working.Where(OneList.IsLinked).Select(r=>r.Id));
            if(monitor!=null)monitor.SetRows(AllRows());Render();
            ShowNotice("info","'"+p.Name+"' 프로필을 목록에 불러왔습니다. 저장을 눌러야 게임에 반영됩니다."+(turnedOff>0?" 프로필에 없던 모드 "+turnedOff+"개는 껐습니다.":"")+(missing>0?" 지금 PC에 없는 모드 "+missing+"개는 건너뛰었습니다.":""),null,null,null,null);
        }
        void ChooseGame(){
            if(Dirty()&&MessageBox.Show(Window,"저장하지 않은 변경을 버리고 다른 게임 경로로 바꿀까요?","모드매니저",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            string note;string folder=Dialogs.GamePath(Window,game,out note);if(folder==null)return;
            HideNotice();GameFolder.Remember(folder);OpenGame(folder);   // opens there next time too
            if(note!="")ShowNotice("info",note,null,null,null,null);
        }

        [STAThread] static void Main(string[] args){
            if(args.Length>=2&&args[0]=="--probe"){int pid=0;if(args.Length>=3)int.TryParse(args[2],out pid);var bytes=new System.Text.UTF8Encoding(false).GetBytes(J.Serializer.Serialize(Probe.Run(Path.GetFullPath(args[1]),pid)));using(var o=Console.OpenStandardOutput())o.Write(bytes,0,bytes.Length);return;}
            // sharp on every monitor: with app.manifest (PerMonitorV2) WPF re-renders at each monitor's scaling instead of stretching a bitmap
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",false);
            // .NET 4.8.1 tooltips (WCAG 2.1) keep the open one while the mouse crosses the hull between its button and it: a straight move
            // to the next button kept the old tooltip. Plain tooltips: each button shows its own as soon as the mouse is on it.
            AppContext.SetSwitch("Switch.System.Windows.Controls.ToolTip.OptOutOfWCAG21ToolTipBehavior",true);
            // the game: -GamePath, the game this copy lives in (LOH_ModManager\App), the folder picked before, any Steam library, the usual place
            string folder=null,here=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"..\.."));
            if(args.Length==2&&args[0]=="-GamePath")folder=Path.GetFullPath(args[1]);
            else if(GameFolder.Valid(here))folder=here;
            else folder=GameFolder.Remembered()??GameFolder.FromSteam()??@"C:\Program Files (x86)\Steam\steamapps\common\LegendOfHeros";
            // tooltips: show after 0.3 s (Windows' default felt slow), stay 20 s to read the long ones, also on greyed-out buttons (to say why)
            try{ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(300));}catch{}
            try{ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(20000));}catch{}
            try{ToolTipService.BetweenShowDelayProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(150));}catch{}
            try{ToolTipService.ShowOnDisabledProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(true));}catch{}
            var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
            // every unexpected error goes to %LOCALAPPDATA%\LOH_ModManager\Errors.log; the same one repeating (a timer) shows one box, not a pile
            string lastShown=null;bool boxOpen=false;
            app.DispatcherUnhandledException+=(s,e)=>{
                e.Handled=true;
                try{string dir=Store.Root;Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir,"Errors.log"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+Version+"\r\n"+e.Exception+"\r\n\r\n");}catch{}
                if(boxOpen||e.Exception.Message==lastShown)return;lastShown=e.Exception.Message;boxOpen=true;
                try{MessageBox.Show(e.Exception.Message+"\n\n(자세한 내용: %LOCALAPPDATA%\\LOH_ModManager\\Errors.log)","예상하지 못한 오류",MessageBoxButton.OK,MessageBoxImage.Error);}finally{boxOpen=false;}
            };
            Motion.Install(app.Resources);UiFont.Install(app.Resources);KeepWords.Install();
            var manager=new ManagerWindow(folder);app.Run(manager.Window);
        }
    }

    static class FolderPicker {
        [ComImport,Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialog{}
        [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem{void BindToHandler(IntPtr pbc,ref Guid bhid,ref Guid riid,out IntPtr ppv);void GetParent(out IShellItem ppsi);void GetDisplayName(uint sigdn,[MarshalAs(UnmanagedType.LPWStr)] out string name);void GetAttributes(uint mask,out uint attribs);void Compare(IShellItem psi,uint hint,out int order);}
        [ComImport,Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog{
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count,IntPtr types);void SetFileTypeIndex(uint index);void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events,out uint cookie);void Unadvise(uint cookie);void SetOptions(uint options);void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);void SetFolder(IShellItem item);void GetFolder(out IShellItem item);void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);void GetResult(out IShellItem item);
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] static extern void SHCreateItemFromParsingName(string path,IntPtr pbc,[MarshalAs(UnmanagedType.LPStruct)] Guid riid,out IShellItem item);
        const uint PickFolders=0x20,FileSystemOnly=0x40,PathMustExist=0x800;const uint FileSystemPath=0x80058000;
        // the chosen folder, or null when cancelled
        public static string Pick(Window owner,string title,string start){
            var dialog=(IFileDialog)new FileOpenDialog();
            try{
                dialog.SetOptions(PickFolders|FileSystemOnly|PathMustExist);dialog.SetTitle(title);dialog.SetOkButtonLabel("이 폴더 선택");
                if(!String.IsNullOrEmpty(start)&&Directory.Exists(start)){try{IShellItem at;SHCreateItemFromParsingName(start,IntPtr.Zero,typeof(IShellItem).GUID,out at);dialog.SetFolder(at);}catch{}}
                if(dialog.Show(owner==null?IntPtr.Zero:new System.Windows.Interop.WindowInteropHelper(owner).Handle)!=0)return null;   // cancelled
                IShellItem item;dialog.GetResult(out item);string path;item.GetDisplayName(FileSystemPath,out path);return path;
            }finally{Marshal.ReleaseComObject(dialog);}
        }
    }
    static class Dialogs {
        static Window Make(Window owner,string title,double width,double height){
            // never larger than the screen it opens on (small laptops, 150 % scaling)
            var area=SystemParameters.WorkArea;
            width*=View.Scale;height*=View.Scale;   // ⚙ 화면 크기 applies to every window
            var w=new Window{Title=title,Owner=owner,Width=Math.Min(width,area.Width-40),Height=Math.Min(height,area.Height-40),WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=false,
                FontFamily=UiFont.Family,FontSize=Fs.Body,UseLayoutRounding=true};
            // Same theme, buttons, tabs and scroll bars as the main window.
            if(owner!=null){w.Resources.MergedDictionaries.Add(owner.Resources);TextOptions.SetTextFormattingMode(w,TextOptions.GetTextFormattingMode(owner));}
            if(Skin.Shown)w.Background=Skin.Paper;else w.SetResourceReference(Window.BackgroundProperty,"Panel");w.SetResourceReference(Window.ForegroundProperty,"Text");
            w.SourceInitialized+=(s,e)=>Theme.TitleBar(w);w.Loaded+=(s,e)=>Motion.FadeIn(w.Content as UIElement);
            if(View.Scale!=1.0)w.Loaded+=(s,e)=>{var fe=w.Content as FrameworkElement;if(fe!=null)fe.LayoutTransform=new ScaleTransform(View.Scale,View.Scale);};
            return w;
        }
        static Border Tag(string text){var t=new TextBlock{Text=text,FontSize=Fs.Caption,FontWeight=FontWeights.SemiBold};t.SetResourceReference(TextBlock.ForegroundProperty,"Sub");var b=new Border{CornerRadius=new CornerRadius(10),Padding=new Thickness(8,1,8,3),VerticalAlignment=VerticalAlignment.Center,Child=t};b.SetResourceReference(Border.BackgroundProperty,"Neutral");return b;}
        // Install preview: one checkbox per part (data mod, game files, DLL), notes, the mod's own guide and the file list.
        // A recipe part (patcher) also carries its own questions as options: yes/no boxes and pick lists, sent back by key.
        // Returns the chosen part keys and option answers, or null when closed.
        public static InstallChoice InstallPlan(Window owner,Dictionary<string,object> plan,string unpacked,string version){
            var comps=J.Items(J.Get(plan,"Components")).ToList();var notes=J.Strings(J.Get(plan,"Notes"));
            bool installable=comps.Any(c=>!J.B(c,"Blocked"));string name=J.S(plan,"Name");
            bool hasOptions=comps.Any(c=>J.Items(J.Get(c,"Options")).Any());
            var w=Make(owner,"모드 설치 — "+name,920,hasOptions?860:720);w.MinWidth=640;w.MinHeight=480;
            var root=new DockPanel{Margin=new Thickness(18)};InstallChoice result=null;var answers=new Dictionary<string,Func<object>>();
            var head=new StackPanel{Margin=new Thickness(0,0,0,10)};
            head.Children.Add(new TextBlock{Text=name,FontSize=Fs.Title,FontWeight=FontWeights.Bold,TextTrimming=TextTrimming.CharacterEllipsis});
            head.Children.Add(new TextBlock{Text=J.S(plan,"Source"),Foreground=Look.Dim,FontSize=Fs.Secondary,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,2,0,0)});
            string replaceName=comps.Select(c=>J.S(c,"ReplaceName")).FirstOrDefault(x=>x!="")??"";
            head.Children.Add(new TextBlock{Text=replaceName!=""?"이전 버전 '"+replaceName+"' 이 설치돼 있습니다. '설치' 를 누르면 새 버전으로 바꿉니다. 이전 버전은 '지운 모드' 에 보관됩니다.":installable?"넣을 것을 고르세요. 설치 기록을 남기고, 덮이는 원본은 백업했다가 제거할 때 되돌립니다. 압축 안의 설치 프로그램은 실행하지 않습니다.":comps.Any(c=>J.S(c,"Kind")!="Unsupported"&&J.S(c,"Warning")!="")?"지금은 설치할 수 없습니다. 아래 빨간 글에 이유가 있습니다.":"모드매니저가 지원하지 않는 형식입니다. "+(J.Strings(J.Get(plan,"Notes")).Any(n=>n.Contains("들어 있는 설치 프로그램:"))?"게임에 그대로 넣을 모드 파일 없이 제작자의 설치 프로그램만 들어 있습니다. ":"")+"아래 '수동으로 설치…'를 눌러 제작자 사용법대로 설치하세요.",
                Foreground=Look.Sub,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
            DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
            var install=new Button{Content="설치",IsDefault=true,MinWidth=130,FontWeight=FontWeights.SemiBold,Style=w.TryFindResource("PrimaryBtn") as Style};
            var cancel=new Button{Content=installable?"취소":"닫기",IsCancel=true,MinWidth=90,Margin=new Thickness(0)};
            if(installable)buttons.Children.Add(install);buttons.Children.Add(cancel);
            // when the manager cannot install it (or the player would rather use the mod's own installer): the unpacked folder opens, the
            // player runs its installer, and the manager records what changed by itself (it sees the installer start and end). When the
            // manager cannot install it at all, this is the way: the button is the main one, and says so beside it.
            var manual=new Button{Content="수동으로 설치…",MinWidth=130,VerticalAlignment=VerticalAlignment.Center};
            if(!installable){manual.Style=w.TryFindResource("PrimaryBtn") as Style;manual.IsDefault=true;manual.FontWeight=FontWeights.SemiBold;w.Loaded+=(s,e)=>manual.Focus();}
            manual.Click+=(s,e)=>{result=new InstallChoice{Manual=true};w.DialogResult=true;};
            var manualNote=new TextBlock{Text="압축을 푼 폴더가 열리면 사용법대로 설치 프로그램(.bat·.exe)을 실행하세요. 모드매니저는 설치 전후를 비교해 바뀐 파일만 기록합니다. 나중에 '설치 정보'에서 제거할 수 있습니다. 설치 프로그램 창에 뜨는 오류는 제작자 프로그램이 판단한 것입니다. GameAssembly.dll 을 고치는 패치는 모드매니저가 패치를 다시 쌓을 때 다시 입히지 못하니, 그때는 설치 프로그램을 다시 실행하세요.",
                Foreground=installable?Look.Dim:Look.Warn,FontSize=Fs.Secondary,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,16,0)};
            var side=new DockPanel();DockPanel.SetDock(manual,Dock.Left);side.Children.Add(manual);side.Children.Add(manualNote);
            var bottom=new DockPanel{Margin=new Thickness(0,12,0,0)};buttons.Margin=new Thickness(0);DockPanel.SetDock(buttons,Dock.Right);bottom.Children.Add(buttons);bottom.Children.Add(side);
            DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
            var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star),MinHeight=120});
            // parts
            var boxes=new List<KeyValuePair<CheckBox,Dictionary<string,object>>>();var list=new StackPanel();
            foreach(var c in comps){
                bool blocked=J.B(c,"Blocked");string warn=J.S(c,"Warning");
                var content=new StackPanel{Margin=new Thickness(6,0,0,0)};var line=new WrapPanel();
                line.Children.Add(Tag(J.S(c,"Kind")=="Unsupported"?"지원 안 함":ManagerWindow.PartLabel(J.S(c,"Kind"))));
                line.Children.Add(new TextBlock{Text=J.S(c,"Title"),FontWeight=FontWeights.Medium,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center});
                content.Children.Add(line);
                content.Children.Add(new TextBlock{Text=J.S(c,"Detail"),Foreground=Look.Sub,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,0)});
                if(warn!="")content.Children.Add(new TextBlock{Text=warn,Foreground=blocked?Look.Bad:Look.Warn,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,0)});
                var box=new CheckBox{Content=content,IsChecked=J.B(c,"Default")&&!blocked,IsEnabled=!blocked,Margin=new Thickness(0,0,0,12),VerticalContentAlignment=VerticalAlignment.Top};
                boxes.Add(new KeyValuePair<CheckBox,Dictionary<string,object>>(box,c));list.Children.Add(box);
                string group=J.S(c,"Group");
                if(group!=""){var self=box;box.Checked+=(s,e)=>{foreach(var other in boxes)if(other.Key!=self&&J.S(other.Value,"Group")==group)other.Key.IsChecked=false;};}
                var options=J.Items(J.Get(c,"Options")).ToList();if(options.Count==0)continue;
                var panel=new StackPanel{Margin=new Thickness(26,-4,0,14),IsEnabled=box.IsChecked==true};
                foreach(var o in options){
                    string key=J.S(o,"Key"),label=J.S(o,"Label");
                    if(J.S(o,"Type")=="choice"){
                        var row=new DockPanel{Margin=new Thickness(0,0,0,6)};var combo=new ComboBox{MinWidth=260,Margin=new Thickness(10,0,0,0)};
                        int index=0;foreach(var ch in J.Items(J.Get(o,"Choices"))){var item=new ComboBoxItem{Content=J.S(ch,"Label"),Tag=J.S(ch,"Value")};combo.Items.Add(item);if(J.S(ch,"Value")==J.S(o,"Default"))index=combo.Items.Count-1;}
                        combo.SelectedIndex=index;DockPanel.SetDock(combo,Dock.Right);row.Children.Add(combo);
                        row.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});panel.Children.Add(row);
                        answers[key]=()=>{var item=combo.SelectedItem as ComboBoxItem;return item==null?"":(object)(string)item.Tag;};
                    } else {
                        var check=new CheckBox{Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap},IsChecked=J.B(o,"Default"),Margin=new Thickness(0,0,0,6)};check.SetResourceReference(Control.ForegroundProperty,"Text2");
                        panel.Children.Add(check);answers[key]=()=>check.IsChecked==true;
                    }
                }
                var owned=panel;box.Checked+=(s,e)=>owned.IsEnabled=true;box.Unchecked+=(s,e)=>owned.IsEnabled=false;list.Children.Add(panel);
            }
            if(comps.Count>0){
                var frame=new Border{CornerRadius=new CornerRadius(11),BorderThickness=new Thickness(1),Padding=new Thickness(16,14,16,2),Child=new ScrollViewer{Content=list,MaxHeight=hasOptions?470:290,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
                frame.SetResourceReference(Border.BackgroundProperty,"Card");frame.SetResourceReference(Border.BorderBrushProperty,"Line");
                body.Children.Add(frame);
            }
            // notes (installer scripts, families that need a recipe, files with no known place)
            if(notes.Count>0){
                var noteText=new TextBlock{Text=String.Join("\n",notes.Select(n=>"· "+n)),Foreground=Look.Warn,TextWrapping=TextWrapping.Wrap};
                var frame=new Border{BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(11),Padding=new Thickness(14,9,14,9),Margin=new Thickness(0,10,0,0),Child=noteText};
                frame.SetResourceReference(Border.BackgroundProperty,"WarnSoft");frame.SetResourceReference(Border.BorderBrushProperty,"WarnLine");
                Grid.SetRow(frame,1);body.Children.Add(frame);
            }
            // guide / files
            string doc=J.S(plan,"Doc"),docName=J.S(plan,"DocName");
            var tabs=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,6)};
            var docTab=new ToggleButton{Content=doc==""?"설명서 없음":"설명서 · "+docName,Style=(Style)w.FindResource("Tab"),IsEnabled=doc!=""};
            var fileTab=new ToggleButton{Content="넣을 파일",Style=(Style)w.FindResource("Tab"),IsEnabled=comps.Count>0};
            tabs.Children.Add(docTab);tabs.Children.Add(fileTab);Grid.SetRow(tabs,2);body.Children.Add(tabs);
            var viewer=Box("",true,true);viewer.FontSize=Fs.Secondary;Grid.SetRow(viewer,3);body.Children.Add(viewer);
            Func<List<string>> chosen=()=>boxes.Where(b=>b.Key.IsChecked==true).Select(b=>J.S(b.Value,"Key")).ToList();
            Action showFiles=()=>{
                var lines=new List<string>();
                foreach(var b in boxes.Where(x=>x.Key.IsChecked==true)){
                    var files=J.Items(J.Get(b.Value,"Files")).ToList();lines.Add("["+J.S(b.Value,"Title")+"] "+files.Count+"개");
                    lines.AddRange(files.Take(300).Select(f=>"   "+J.S(f,"Target")));if(files.Count>300)lines.Add("   … 외 "+(files.Count-300)+"개");lines.Add("");
                }
                viewer.Text=lines.Count==0?"고른 것이 없습니다.":String.Join(Environment.NewLine,lines);
            };
            Action<bool> show=files=>{docTab.IsChecked=!files;fileTab.IsChecked=files;if(files)showFiles();else viewer.Text=doc;viewer.ScrollToHome();};
            docTab.Click+=(s,e)=>show(false);fileTab.Click+=(s,e)=>show(true);
            Action update=()=>{int n=chosen().Count;install.IsEnabled=n>0;install.Content=n>0?"설치 ("+n+"개)":"설치";if(fileTab.IsChecked==true)showFiles();};
            foreach(var b in boxes){b.Key.Checked+=(s,e)=>update();b.Key.Unchecked+=(s,e)=>update();}
            install.Click+=(s,e)=>{result=new InstallChoice{Keys=chosen()};foreach(var a in answers)result.Options[a.Key]=a.Value();w.DialogResult=true;};
            show(doc==""&&comps.Count>0);update();
            root.Children.Add(body);w.Content=root;
            return w.ShowDialog()==true?result:null;
        }
        static StackPanel Buttons(Window w,string ok,Action onOk){
            var panel=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
            var yes=new Button{Content=ok,IsDefault=true,MinWidth=110};yes.Click+=(s,e)=>{onOk();w.DialogResult=true;};
            var no=new Button{Content="취소",IsCancel=true,MinWidth=90,Margin=new Thickness(0)};panel.Children.Add(yes);panel.Children.Add(no);return panel;
        }
        static TextBox Box(string text,bool multi,bool readOnly){
            return new TextBox{Text=text,AcceptsReturn=multi,TextWrapping=multi?TextWrapping.Wrap:TextWrapping.NoWrap,IsReadOnly=readOnly,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};   // colours: the theme's TextBox style
        }
        public static string Ask(Window owner,string title,string label,string initial,bool multi){
            var w=Make(owner,title,560,multi?320:200);var root=new DockPanel{Margin=new Thickness(18)};string result=null;
            var text=Box(initial,multi,false);var buttons=Buttons(w,"확인",()=>result=text.Text);
            // one line: the window grows with the explanation above it (a fixed height squeezed the box to a thin line once the text wrapped, 11-03 디시)
            if(!multi){text.VerticalAlignment=VerticalAlignment.Top;text.MinHeight=30;text.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;w.Height=double.NaN;w.SizeToContent=SizeToContent.Height;}
            DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            var caption=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8),Foreground=Look.Sub};DockPanel.SetDock(caption,Dock.Top);root.Children.Add(caption);
            root.Children.Add(text);w.Content=root;w.Loaded+=(s,e)=>{text.Focus();text.SelectAll();};
            return w.ShowDialog()==true?result:null;
        }
        // 패치노트 of one mod: posts seen (newest first), versions used here, guides of the installed versions
        public static void History(Window owner,string name,string link,List<string[]> posts,List<string[]> used,List<string[]> docs,List<string[]> written,SelfUpdate.Release release,bool auto,Action<bool> setAuto,Action openPost,string backTip,Action goBack){
            var w=Make(owner,"패치노트 — "+name,820,620);w.MinHeight=380;var root=new DockPanel{Margin=new Thickness(20)};
            var bottom=new DockPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
            var close=new Button{Content="닫기",IsCancel=true,MinWidth=90,Margin=new Thickness(0)};DockPanel.SetDock(close,Dock.Right);bottom.Children.Add(close);
            var post=new Button{Content=link!=""?"게시글 열기":"게시글 링크 넣기…",MinWidth=120};post.Click+=(s,e)=>{w.Close();openPost();};DockPanel.SetDock(post,Dock.Right);bottom.Children.Add(post);
            if(goBack!=null){var back=new Button{Content="이전 버전으로 되돌리기",MinWidth=150,ToolTip=backTip};back.Click+=(s,e)=>{w.Close();goBack();};DockPanel.SetDock(back,Dock.Right);bottom.Children.Add(back);}
            var box=new CheckBox{Content="하루 한 번 업데이트 자동 확인 (모드매니저를 켤 때, 새 버전이 있을 때만 알림)",IsChecked=auto,VerticalAlignment=VerticalAlignment.Center};box.Click+=(s,e)=>setAuto(box.IsChecked==true);
            if(goBack!=null){box.Margin=new Thickness(0,12,0,0);DockPanel.SetDock(box,Dock.Bottom);root.Children.Add(box);bottom.LastChildFill=false;}else bottom.Children.Add(box);   // its own line above the buttons
            var body=new StackPanel();
            Action<string,string> head=(t,sub)=>{body.Children.Add(new TextBlock{Text=t,FontSize=Fs.Headline,FontWeight=FontWeights.Bold,Margin=new Thickness(0,body.Children.Count==0?0:16,0,2)});
                if(sub!="")body.Children.Add(new TextBlock{Text=sub,Foreground=Look.Sub,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)});};
            Action<string> line=t=>body.Children.Add(new TextBlock{Text=t,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,2)});
            head("게시글 제목으로 확인한 버전","'업데이트 확인'이 게시글 제목을 읽을 때마다 바뀐 것이 여기에 쌓입니다. 바뀐 내용(패치노트)은 게시글 본문에 있습니다 — 모드매니저가 본문을 읽으면 조회수가 올라가서 읽지 않습니다.");
            if(posts.Count==0)line(link==""?"게시글 링크가 없습니다. 아래 '게시글 링크 넣기…'로 넣으면 확인할 수 있습니다.":"아직 없습니다. 위쪽 '업데이트 확인'을 누르면 처음 한 줄이 생깁니다.");
            foreach(var p in posts)line(p[0]+"   "+(p[1]!=""?"v"+p[1]:"버전 없음")+"   — "+p[2]);
            if(written!=null&&written.Count>0){head("모드가 적어 둔 패치노트","");foreach(var n in written)line((n[0]!=""?"v"+n[0].TrimStart('v','V'):"")+(n[1]!=""?"   "+n[1]:"")+"   — "+n[2]);}
            if(release!=null&&release.Notes.Trim()!=""){head("GitHub 릴리스 노트","");line("v"+release.Version);
                body.Children.Add(new TextBox{Text=release.Notes.Trim(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MaxHeight=320,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,4,0,0)});}
            head("이 PC에서 쓴 버전","");
            if(used.Count==0)line("아직 없습니다(업데이트 확인 때 함께 적습니다).");
            foreach(var u in used)line(u[0]+"   v"+u[1].TrimStart('v','V'));
            if(docs.Count>0){
                head("설치한 버전의 설명서","모드매니저로 설치할 때 압축 안에 있던 설명서입니다. 지운 버전도 휴지통에 남아 있는 동안은 보입니다.");
                foreach(var d in docs){
                    var title=new TextBlock{Text=d[0]+"   "+d[1]+(d[2]!=""?"   ("+d[2]+")":""),FontWeight=FontWeights.SemiBold};title.SetResourceReference(TextBlock.ForegroundProperty,"Text");   // the theme's text colour (the default was near black on dark)
                    var ex=new Expander{Header=title,IsExpanded=d==docs[0],Margin=new Thickness(0,4,0,4)};ex.SetResourceReference(Control.ForegroundProperty,"Text");
                    ex.Content=new TextBox{Text=d[3],IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MaxHeight=320,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,6,0,0)};
                    body.Children.Add(ex);
                }
            }
            root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});w.Content=root;w.ShowDialog();
        }
        // 패치노트 of the manager: each GitHub release's 바뀐 점, newest first (read off the window thread); 지금 업데이트 only when it can
        public static void SelfNotes(Window owner,string current,string newer,Func<List<string[]>> read,Action openPage,Action update){
            var w=Make(owner,"패치노트 — 모드매니저",820,620);w.MinHeight=380;var root=new DockPanel{Margin=new Thickness(20)};
            var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
            if(update!=null){var go=new Button{Content="지금 업데이트",MinWidth=120,FontWeight=FontWeights.SemiBold,Style=w.TryFindResource("PrimaryBtn") as Style};go.Click+=(s,e)=>{w.Close();update();};bottom.Children.Add(go);}
            var page=new Button{Content="GitHub 릴리스 열기",MinWidth=140};page.Click+=(s,e)=>openPage();bottom.Children.Add(page);
            var close=new Button{Content="닫기",IsCancel=true,MinWidth=90,Margin=new Thickness(0)};close.Click+=(s,e)=>w.Close();bottom.Children.Add(close);
            var head=new StackPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
            Func<string,string> newLine=v=>"지금 쓰는 버전 v"+current+(v!=""?" · 새 버전 v"+v+" 이 나왔습니다.":"");
            var top=new TextBlock{Text=newLine(newer),FontSize=Fs.Title,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap};head.Children.Add(top);
            head.Children.Add(new TextBlock{Text="GitHub 릴리스에 올린 버전별 바뀐 점입니다(최근 20개).",Foreground=Look.Sub,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)});
            var body=new StackPanel();body.Children.Add(new TextBlock{Text="패치노트를 받는 중…",Foreground=Look.Sub});
            root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});w.Content=root;
            Task.Run(read).ContinueWith(t=>{
                body.Children.Clear();
                if(t.IsFaulted){body.Children.Add(new TextBlock{Text="패치노트를 받지 못했습니다: "+t.Exception.GetBaseException().Message+". 아래 'GitHub 릴리스 열기'로 브라우저에서 볼 수 있습니다.",Foreground=Look.Bad,TextWrapping=TextWrapping.Wrap});return;}
                var list=t.Result;if(newer==""&&list.Count>0&&Posts.Compare(current,list[0][0])<0)top.Text=newLine(list[0][0]);   // not checked yet this run: the list tells
                foreach(var x in list){
                    body.Children.Add(new TextBlock{Text="v"+x[0]+(x[1]!=""?" ("+x[1]+")":"")+(Posts.Compare(x[0],current)==0?" — 지금 쓰는 버전":""),FontSize=Fs.Headline,FontWeight=FontWeights.Bold,Margin=new Thickness(0,body.Children.Count==0?0:16,0,4)});
                    string text=System.Text.RegularExpressions.Regex.Replace(ModRelease.Changes(x[2]).Replace("**","").Replace("`",""),@"(?m)^\s*[-*]\s+","· ");
                    if(text!="")body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,LineHeight=22});
                }
            },TaskScheduler.FromCurrentSynchronizationContext());
            w.ShowDialog();
        }
        // a message with two actions and 닫기: 1 = first, 2 = second, 0 = closed
        public static int Buttons3(Window owner,string title,string message,string first,string second){
            var w=Make(owner,title,680,500);var root=new DockPanel{Margin=new Thickness(18)};int pick=0;
            var panel=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
            var a=new Button{Content=first,IsDefault=true,MinWidth=110};a.Click+=(s,e)=>{pick=1;w.DialogResult=true;};
            var b=new Button{Content=second,MinWidth=110};b.Click+=(s,e)=>{pick=2;w.DialogResult=true;};
            var no=new Button{Content="닫기",IsCancel=true,MinWidth=90,Margin=new Thickness(0)};panel.Children.Add(a);panel.Children.Add(b);panel.Children.Add(no);
            DockPanel.SetDock(panel,Dock.Bottom);root.Children.Add(panel);
            root.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,LineHeight=21});w.Content=root;
            w.ShowDialog();return pick;
        }
        public static bool Confirm(Window owner,string title,string message,string ok){
            var w=Make(owner,title,780,440);var root=new DockPanel{Margin=new Thickness(18)};var buttons=Buttons(w,ok,()=>{});
            DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);root.Children.Add(Box(message,true,true));w.Content=root;
            return w.ShowDialog()==true;
        }
        // 게임 경로 설정: which folder (with the usual places), what is set now, a folder picker that also accepts a parent or a sub folder.
        public static string GamePath(Window owner,string current,out string note){
            var w=Make(owner,"게임 경로 설정",720,400);w.MinHeight=360;var root=new DockPanel{Margin=new Thickness(20),LastChildFill=false};string result=null,told="";
            var head=new StackPanel();DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
            head.Children.Add(new TextBlock{Text="영웅입지전이 설치된 폴더를 고릅니다",FontSize=Fs.Title,FontWeight=FontWeights.Bold});
            head.Children.Add(new TextBlock{Text="ThreeKingdom.exe 가 들어 있는 LegendOfHeros 폴더를 고르세요.\n그 안의 MODs 같은 하위 폴더나, 그 위의 common 폴더를 골라도 모드매니저가 알맞은 폴더로 맞춰 줍니다.",TextWrapping=TextWrapping.Wrap,Foreground=Look.Sub,Margin=new Thickness(0,8,0,0)});
            var where=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=Fs.Secondary,LineHeight=19,
                Text="보통 위치:  C:\\Program Files (x86)\\Steam\\steamapps\\common\\LegendOfHeros\n다른 드라이브에 깔았다면 (예):  D:\\SteamLibrary\\steamapps\\common\\LegendOfHeros\n어디인지 모르겠다면:  Steam 라이브러리 → 영웅입지전 우클릭 → 관리 → 로컬 파일 보기 (열린 폴더가 바로 그 폴더)"};
            var card=new Border{CornerRadius=new CornerRadius(9),Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,12,0,0),Child=where};card.SetResourceReference(Border.BackgroundProperty,"Card");head.Children.Add(card);
            var now=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0)};head.Children.Add(now);
            var problem=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0),Foreground=Look.Bad,Visibility=Visibility.Collapsed};head.Children.Add(problem);
            Action<string> fail=t=>{problem.Text=t;problem.Visibility=Visibility.Visible;};
            bool ok=GameFolder.Valid(current);
            now.Inlines.Add(new System.Windows.Documents.Run("지금 경로: "){Foreground=Look.Sub});now.Inlines.Add(new System.Windows.Documents.Run(current??""));
            now.Inlines.Add(new System.Windows.Documents.LineBreak());now.Inlines.Add(new System.Windows.Documents.Run(ok?"✓ 이 경로에서 게임을 찾았습니다":"✗ 이 경로에는 ThreeKingdom.exe 가 없습니다 — 아래 '폴더 고르기…'로 다시 골라 주세요"){Foreground=ok?Look.Ok:Look.Bad});
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);
            var pick=new Button{Content="폴더 고르기…",IsDefault=true,MinWidth=130,FontWeight=FontWeights.SemiBold,Style=w.TryFindResource("PrimaryBtn") as Style,ToolTip="탐색기 창에서 LegendOfHeros 폴더를 고릅니다."};
            var auto=new Button{Content="자동으로 다시 찾기",MinWidth=130,ToolTip="Steam 이 기억하는 모든 라이브러리(다른 드라이브 포함)에서 영웅입지전을 찾습니다."};
            var cancel=new Button{Content="취소",IsCancel=true,MinWidth=90,Margin=new Thickness(0)};
            buttons.Children.Add(pick);buttons.Children.Add(auto);buttons.Children.Add(cancel);root.Children.Add(buttons);
            pick.Click+=(s,e)=>{
                string start=GameFolder.Valid(current)?current:Directory.Exists(@"C:\Program Files (x86)\Steam\steamapps\common")?@"C:\Program Files (x86)\Steam\steamapps\common":null;
                string picked=FolderPicker.Pick(w,"영웅입지전 폴더(LegendOfHeros) 고르기",start);if(picked==null)return;
                string fixedNote;string found=GameFolder.Resolve(picked,out fixedNote);
                if(found==null){fail("이 폴더에서 영웅입지전을 찾지 못했습니다: "+picked+"\nThreeKingdom.exe 가 들어 있는 LegendOfHeros 폴더를 골라 주세요.");return;}
                result=found;told=fixedNote;w.DialogResult=true;};
            auto.Click+=(s,e)=>{
                string found=GameFolder.FromSteam();
                if(found==null){fail("Steam 라이브러리에서 영웅입지전을 찾지 못했습니다. '폴더 고르기…'로 직접 골라 주세요.");return;}
                result=found;told="Steam 라이브러리에서 게임을 찾았습니다: "+found;w.DialogResult=true;};
            w.Content=root;bool done=w.ShowDialog()==true;note=done?told:"";return done?result:null;
        }
        public static void Report(Window owner,string text,string logDir){
            var w=Make(owner,"문제 보고서",900,640);w.MinHeight=420;var root=new DockPanel{Margin=new Thickness(18)};
            var head=new TextBlock{Text="아래 내용을 복사해 디시 댓글이나 제작자에게 보내면 원인을 찾기 쉽습니다. 게임 로그의 오류 줄은 모드가 아닌 게임 자체 경고일 수도 있습니다.",Foreground=Look.Sub,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
            DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
            var bar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bar,Dock.Bottom);
            var copy=new Button{Content="복사",MinWidth=100,FontWeight=FontWeights.SemiBold,Style=w.TryFindResource("PrimaryBtn") as Style};copy.Click+=(s,e)=>{try{Clipboard.SetText(text);copy.Content="복사했습니다 ✓";}catch(Exception x){MessageBox.Show(w,x.Message,"복사하지 못했습니다");}};
            var save=new Button{Content="바탕화면에 저장",ToolTip="바탕화면에 '모드관리_문제보고서_날짜.txt' 로 저장합니다(파일로 첨부할 때)."};
            save.Click+=(s,e)=>{try{string f=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"모드관리_문제보고서_"+DateTime.Now.ToString("yyyyMMdd_HHmm")+".txt");File.WriteAllText(f,text,new UTF8Encoding(true));save.Content="저장했습니다 ✓";}catch(Exception x){MessageBox.Show(w,x.Message,"저장하지 못했습니다");}};
            var folder=new Button{Content="게임 로그 폴더 열기",ToolTip="Player.log(이번 실행)·Player-prev.log(지난 실행)가 있는 폴더입니다."};folder.Click+=(s,e)=>{try{if(Directory.Exists(logDir))Process.Start("explorer.exe","\""+logDir+"\"");}catch{}};
            var close=new Button{Content="닫기",IsCancel=true,MinWidth=90,Margin=new Thickness(0)};
            bar.Children.Add(folder);bar.Children.Add(save);bar.Children.Add(copy);bar.Children.Add(close);root.Children.Add(bar);
            var box=Box(text,true,true);box.FontFamily=new FontFamily("Consolas, Malgun Gothic");box.FontSize=Fs.Secondary;box.TextWrapping=TextWrapping.NoWrap;box.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;root.Children.Add(box);
            w.Content=root;w.ShowDialog();
        }
        // ⚙ 설정: every choice applies at once (and is saved); 닫기 closes
        public static void Settings(Window owner,Action<double> scale,Action<string> mode,Action<string> palette,Action<int> strength,Func<string,string> picture,Action openFolder,Action report){
            var w=Make(owner,"설정",640,600);w.MinHeight=420;var root=new DockPanel{Margin=new Thickness(22,18,22,18)};
            var close=new Button{Content="닫기",IsCancel=true,IsDefault=true,MinWidth=100,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
            var body=new StackPanel();root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            Action<string,string> head=(title,note)=>{body.Children.Add(new TextBlock{Text=title,FontSize=Fs.Headline,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,body.Children.Count==0?0:18,0,0)});
                if(note!="")body.Children.Add(new TextBlock{Text=note,FontSize=Fs.Secondary,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,8)});
                ((TextBlock)body.Children[body.Children.Count-1]).SetResourceReference(TextBlock.ForegroundProperty,note!=""?"Sub":"Text");};
            Func<string,object,bool,Action,RadioButton> chip=(group,content,on,act)=>{var r=new RadioButton{Content=content,GroupName=group,IsChecked=on,Style=(Style)w.FindResource("Chip"),Margin=new Thickness(0,0,6,6)};r.Checked+=(s,e)=>act();return r;};
            head("테마","바탕·카드·버튼·글자·강조 색을 한 벌로 맞춘 색 묶음입니다. '수묵'은 게임 메뉴처럼 옅은 수묵화 바탕에 짙은 제목 띠를 씌웁니다(그림은 모드매니저용으로 새로 그린 것).");
            var strengths=new WrapPanel{IsEnabled=Skin.Shown};var modes=new WrapPanel{IsEnabled=Look.FixedLight==null};
            // this window repaints with the main one: the washed painting only for 수묵 (light text on it would vanish)
            Action repaint=()=>{if(Skin.Shown)w.Background=Skin.Paper;else w.SetResourceReference(Window.BackgroundProperty,"Panel");Theme.TitleBar(w);strengths.IsEnabled=Skin.Shown;modes.IsEnabled=Look.FixedLight==null;};
            Action<string> pick=p=>{palette(p);repaint();};
            var themes=new WrapPanel();
            foreach(var p in View.Palettes){var pp=p;var label=new StackPanel{Orientation=Orientation.Horizontal};
                var swatch=new Border{CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center,Background=Look.Hex(pp[3]),Padding=new Thickness(3,2,3,2)};
                swatch.SetResourceReference(Border.BorderBrushProperty,"LineStrong");
                var dots=new StackPanel{Orientation=Orientation.Horizontal};foreach(var c in new[]{pp[4],pp[5]})dots.Children.Add(new System.Windows.Shapes.Ellipse{Width=8,Height=8,Fill=Look.Hex(c),Margin=new Thickness(1,0,1,0)});
                swatch.Child=dots;label.Children.Add(swatch);label.Children.Add(new TextBlock{Text=pp[1],VerticalAlignment=VerticalAlignment.Center});
                themes.Children.Add(chip("theme",label,View.Palette==pp[0],()=>pick(pp[0])));}
            body.Children.Add(themes);
            head("배경 그림","원하는 그림을 관리창 바탕에 깔 수 있습니다. 글자가 잘 보이도록 그림 위에 테마 색을 옅게 덮습니다. jpg·png·bmp 그림을 쓸 수 있습니다.");
            var pics=new WrapPanel{Margin=new Thickness(0,0,0,4)};var choose=new Button{Content="그림 고르기…"};var noPic=new Button{Content="그림 빼기",Visibility=View.HasPicture?Visibility.Visible:Visibility.Collapsed};
            var picNote=new TextBlock{FontSize=Fs.Secondary,TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed,Margin=new Thickness(0,4,0,0)};picNote.SetResourceReference(TextBlock.ForegroundProperty,"Warn");
            Action<string> setPic=file=>{string why=picture(file);picNote.Text=why;picNote.Visibility=why==""?Visibility.Collapsed:Visibility.Visible;noPic.Visibility=View.HasPicture?Visibility.Visible:Visibility.Collapsed;repaint();};
            choose.Click+=(s,e)=>{var d=new Microsoft.Win32.OpenFileDialog{Title="배경 그림 고르기",Filter="그림 (jpg·png·bmp)|*.jpg;*.jpeg;*.png;*.bmp"};if(d.ShowDialog(w)==true)setPic(d.FileName);};
            noPic.Click+=(s,e)=>setPic(null);
            pics.Children.Add(choose);pics.Children.Add(noPic);body.Children.Add(pics);body.Children.Add(picNote);
            head("그림 진하기","바탕 그림이 얼마나 짙게 보일지 정합니다. 글자가 그림에 묻혀 읽기 힘들면 낮추세요.");
            foreach(var s in View.InkStrengths){int v=s;strengths.Children.Add(chip("ink",v+"%",View.InkStrength==v,()=>strength(v)));}body.Children.Add(strengths);
            head("화면 크기","자동은 창이 화면에 다 들어가도록 크기를 맞춥니다(Windows 배율을 크게 쓰는 노트북·FHD 화면에서 글자가 너무 커 보이지 않게). 직접 고르면 그 크기를 계속 씁니다. 모든 창에 바로 적용됩니다.");
            var scales=new WrapPanel();scales.Children.Add(chip("scale","자동",View.ScaleAuto,()=>scale(0)));
            foreach(var sc in View.Scales){double v=sc;scales.Children.Add(chip("scale",(int)Math.Round(v*100)+"%",!View.ScaleAuto&&Math.Abs(View.Scale-v)<0.001,()=>scale(v)));}body.Children.Add(scales);
            head("화면 밝기","기본 테마에서만 씁니다. 다른 테마는 제 밝기가 정해져 있습니다.");
            foreach(var m in new[]{new[]{"auto","자동 (Windows 설정)"},new[]{"light","밝게"},new[]{"dark","어둡게"}}){var mm=m;modes.Children.Add(chip("mode",mm[1],Theme.Mode==mm[0],()=>mode(mm[0])));}body.Children.Add(modes);
            head("창","");
            var remember=new CheckBox{Content="창 크기와 위치를 기억해 다음에도 그대로 열기",IsChecked=View.RememberWindow,Margin=new Thickness(0,4,0,0)};remember.Click+=(s,e)=>{View.RememberWindow=remember.IsChecked==true;View.Save();};body.Children.Add(remember);
            head("업데이트","");
            var auto=new CheckBox{Content=new TextBlock{Text="모드매니저를 열 때 하루 한 번 업데이트 확인 (새 버전이 있을 때만 위쪽에 알림, 디시 게시글 제목만 읽음)",TextWrapping=TextWrapping.Wrap},IsChecked=View.AutoUpdate,Margin=new Thickness(0,4,0,0)};
            auto.Click+=(s,e)=>{View.AutoUpdate=auto.IsChecked==true;View.Save();};body.Children.Add(auto);
            var self=new CheckBox{Content=new TextBlock{Text="모드매니저 새 버전을 하루 한 번 확인 (GitHub 릴리스 — 새 버전이면 위쪽 알림의 '지금 업데이트'로 바로 바꿈)",TextWrapping=TextWrapping.Wrap},IsChecked=View.SelfCheck,Margin=new Thickness(0,6,0,0)};
            self.Click+=(s,e)=>{View.SelfCheck=self.IsChecked==true;View.Save();};body.Children.Add(self);
            head("기록·문제 해결","모드끼리 부딪쳐 게임이 튕기거나 이상할 때: 문제 보고서는 켜 둔 모드, 모드매니저가 찾은 겹침, 게임 로그의 오류·튕김 줄, 각 모드 로그의 끝부분을 한 번에 모읍니다(내 PC 사용자 폴더 이름은 가림).");
            var rep=new Button{Content="문제 보고서 만들기",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8)};rep.Click+=(s,e)=>{w.DialogResult=true;report();};body.Children.Add(rep);
            var folder=new Button{Content="모드매니저 설정 폴더 열기",HorizontalAlignment=HorizontalAlignment.Left,ToolTip="프로필, 화면 설정, 게시글 링크, 오류 기록(Errors.log)이 있는 폴더입니다. 문제를 제보할 때 Errors.log 를 보내 주세요."};folder.Click+=(s,e)=>openFolder();body.Children.Add(folder);
            var reset=new Button{Content="화면 설정 기본값으로",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};
            reset.Click+=(s,e)=>{scale(1.0);mode("auto");strength(100);picture(null);pick("basic");View.RememberWindow=true;View.AutoUpdate=false;View.SelfCheck=true;View.Save();w.DialogResult=true;};body.Children.Add(reset);
            w.Content=root;w.ShowDialog();
        }
        // update check result: mod · now · in the post · what it means · open the post. Row = {name, installed, posted, title, error, url}.
        public static void Updates(Window owner,List<string[]> rows,Action<string> open){
            var w=Make(owner,"업데이트 확인",860,520);w.MinHeight=360;var root=new DockPanel{Margin=new Thickness(20)};
            var head=new StackPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
            int newer=rows.Count(x=>x[7]=="1"),byDay=rows.Count(x=>x[7]=="1"&&x[2]=="");
            head.Children.Add(new TextBlock{Text=newer>0?"새 버전이 있는 모드 "+newer+"개"+(byDay>0?" (제목 날짜로 짐작한 "+byDay+"개 포함)":""):"모두 최신입니다(게시글 제목 기준)",FontSize=Fs.Title,FontWeight=FontWeights.Bold});
            head.Children.Add(new TextBlock{Text="각 모드의 게시글 제목에 적힌 버전과 지금 쓰는 버전을 비교했습니다. 버전이 없고 '12/24 업뎃' 처럼 날짜만 있으면, 그 날짜가 모드를 넣은 날보다 뒤인지 봅니다. 새 버전은 게시글에서 직접 받아, 압축 파일을 모드매니저로 드래그해 설치하세요.",Foreground=Look.Sub,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});
            var grid=new Grid();foreach(var width in new[]{2.2,0.9,0.9,2.0,1.1})grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width,GridUnitType.Star)});
            Action<int,int,UIElement> put=(row,col,el)=>{Grid.SetRow(el,row);Grid.SetColumn(el,col);grid.Children.Add(el);};
            grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            string[] heads={"모드","지금","게시글","",""};for(int i=0;i<4;i++)put(0,i,new TextBlock{Text=heads[i],Foreground=Look.Dim,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,8,8)});
            for(int i=0;i<rows.Count;i++){
                var x=rows[i];int row=i+1;grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                string state;Brush color;
                if(x[4]!=""){state="게시글을 읽지 못함";color=Look.Bad;}
                else if(x[2]==""&&x[6]!=""){state=x[7]=="1"?"새 버전일 수 있음":"넣은 뒤로 소식 없음";color=x[7]=="1"?Look.Warn:Look.Ok;}
                else if(x[2]==""){state="제목에 버전이 없음";color=Look.Dim;}
                else if(x[1]==""){state="지금 버전을 모름";color=Look.Dim;}
                else if(x.Length>9&&x[9]=="1"){state="되돌린 버전 (알림 안 함)";color=Look.Dim;}
                else{int c=Posts.Compare(x[1],x[2]);state=c<0?"새 버전 있음":c>0?"지금 버전이 더 최신":"최신";color=c<0?Look.Warn:c>0?Look.Sub:Look.Ok;}
                string tip=x[4]!=""?"오류: "+x[4]:"게시글 제목: "+x[3]+(x[2]==""&&x[6]!=""?"\n버전이 없어 제목 날짜("+x[6]+")를 이 모드를 넣은 날("+(x[8]==""?"모름":x[8])+")과 비교했습니다.":x[2]==""?"\n제목에 버전을 적어 두면 비교할 수 있습니다(모드 하나면 v1.2.3, 여러 모드면 '모드이름 1.2.3').":"");
                put(row,0,new TextBlock{Text=x[0],FontWeight=FontWeights.Medium,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,8,4)});
                put(row,1,new TextBlock{Text=x[1]!=""?"v"+x[1]:x[2]==""&&x[8]!=""?x[8]+" 넣음":"—",VerticalAlignment=VerticalAlignment.Center});
                put(row,2,new TextBlock{Text=x[2]!=""?"v"+x[2]:x[6]!=""?x[6]+" 글":"—",VerticalAlignment=VerticalAlignment.Center});
                put(row,3,new TextBlock{Text=state,Foreground=color,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,ToolTip=tip});
                var url=x[5];bool self=url=="self:update"||url.StartsWith("gh:"),modUpdate=url.StartsWith("gh:"),release=url.IndexOf("github.com",StringComparison.OrdinalIgnoreCase)>=0&&url.IndexOf("/releases",StringComparison.OrdinalIgnoreCase)>=0;
                var go=new Button{Content=self?"지금 업데이트":release?"릴리스 보기":"게시글 열기",Margin=new Thickness(0,3,0,3),ToolTip=modUpdate?"GitHub 릴리스에서 새 버전 압축을 받아 설치 미리보기를 엽니다. 확인을 누르면 옛 버전을 빼고 새 버전을 넣습니다.":self?"GitHub 에서 새 버전을 받아 바꾸고 창을 다시 엽니다. 모드 설정과 게임 파일은 그대로입니다.":url};
                if(self){var primary=w.TryFindResource("PrimaryBtn") as Style;if(primary!=null)go.Style=primary;}
                go.Click+=(s,e)=>{if(self)w.Close();open(url);};put(row,4,go);
            }
            var frame=new Border{CornerRadius=new CornerRadius(11),Padding=new Thickness(16,12,16,10),Child=new ScrollViewer{Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
            frame.SetResourceReference(Border.BackgroundProperty,"Card");
            var foot=new TextBlock{Text="모드매니저와 GitHub 로 올린 모드는 GitHub 릴리스로, 다른 모드는 게시글 제목으로 확인합니다(게시글 조회수에는 영향 없음). 다른 모드도 카드를 고르고 '링크 넣기…'에 게시글 주소를 넣으면 같이 확인합니다.",Foreground=Look.Dim,FontSize=Fs.Secondary,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
            DockPanel.SetDock(foot,Dock.Bottom);
            var close=new Button{Content="닫기",IsCancel=true,IsDefault=true,MinWidth=100,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(close,Dock.Bottom);
            root.Children.Add(close);root.Children.Add(foot);root.Children.Add(frame);w.Content=root;w.ShowDialog();
        }
        public static int Choose(Window owner,string title,List<string> items,Func<int,string> note,string ok){
            var w=Make(owner,title,800,460);var root=new DockPanel{Margin=new Thickness(18)};int picked=-1;
            var list=new ListBox{Height=190,BorderThickness=new Thickness(1),Padding=new Thickness(4),AllowDrop=false};foreach(var i in items)list.Items.Add(i);
            list.SetResourceReference(Control.BackgroundProperty,"Card");list.SetResourceReference(Control.BorderBrushProperty,"LineStrong");
            var detail=Box("",true,true);detail.Margin=new Thickness(0,10,0,0);list.SelectionChanged+=(s,e)=>{if(list.SelectedIndex>=0)detail.Text=note(list.SelectedIndex);};
            var buttons=Buttons(w,ok,()=>picked=list.SelectedIndex);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            DockPanel.SetDock(list,Dock.Top);root.Children.Add(list);root.Children.Add(detail);w.Content=root;list.SelectedIndex=0;
            return w.ShowDialog()==true?picked:-1;
        }
    }
}
