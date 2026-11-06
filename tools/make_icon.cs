// 관리창 아이콘 만들기: 어두운 판 위에 모드 카드 셋(초록·파랑·회색 상태 점) + 붉은 도장(管).
// 크기마다 따로 그려 작은 크기에서도 뭉개지지 않게 한다. 결과: src\app.ico, docs\images\아이콘.png
// 빌드·실행: csc /target:exe /reference:<WPF 3종>,System.Xaml.dll tools\make_icon.cs → make_icon.exe <저장소 폴더>
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class MakeIcon {
    static readonly int[] Sizes={16,20,24,32,40,48,64,96,128,256};
    static Color C(string hex){return (Color)ColorConverter.ConvertFromString(hex);}
    static Brush B(string hex){var b=new SolidColorBrush(C(hex));b.Freeze();return b;}
    static BitmapSource Draw(int size){
        var visual=new DrawingVisual();
        using(var g=visual.RenderOpen()){
            double s=size/256.0;bool tiny=size<=24,small=size<=40;
            g.PushTransform(new ScaleTransform(s,s));
            var back=new LinearGradientBrush(C("#2E3641"),C("#15191E"),new Point(0,0),new Point(0.4,1));back.Freeze();
            double inset=tiny?4:8;
            g.DrawRoundedRectangle(back,new Pen(B("#3FB59F"),tiny?14:5),new Rect(inset+(tiny?7:0),inset+(tiny?7:0),256-2*inset-(tiny?14:0),256-2*inset-(tiny?14:0)),48,48);
            string[] dots={"#34C06A","#3F7FE8","#7A838F"};string[] cards={"#EFE4C8","#EFE4C8","#B9B2A2"};
            double left=tiny?36:40,right=tiny?204:200,height=tiny?44:40,gap=tiny?64:57,top=tiny?42:50;
            for(int i=0;i<3;i++){
                double y=top+i*gap;
                g.DrawRoundedRectangle(B(cards[i]),null,new Rect(left,y,right-left,height),tiny?12:10,tiny?12:10);
                double r=tiny?17:12;g.DrawEllipse(B(dots[i]),null,new Point(left+(tiny?30:26),y+height/2),r,r);
                if(!small)g.DrawRoundedRectangle(B("#9C8E6E"),null,new Rect(left+52,y+height/2-5,(right-left)-(i==1?92:76),10),5,5);
            }
            if(!tiny){
                var seal=new Rect(small?150:146,small?150:146,small?86:92,small?86:92);
                g.PushTransform(new RotateTransform(-6,seal.X+seal.Width/2,seal.Y+seal.Height/2));
                g.DrawRoundedRectangle(B("#B8322A"),new Pen(B("#E2685C"),small?0:4),seal,12,12);
                if(!small){
                    var text=new FormattedText("管",System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("SimSun"),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal),70,B("#FBEFD9"));
                    g.DrawText(text,new Point(seal.X+(seal.Width-text.Width)/2,seal.Y+(seal.Height-text.Height)/2+2));
                }
                g.Pop();
            } else g.DrawRoundedRectangle(B("#C0392B"),null,new Rect(168,168,64,64),10,10);
            g.Pop();
        }
        var bmp=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bmp.Render(visual);bmp.Freeze();return bmp;
    }
    static BitmapSource FromPicture(BitmapSource source,Rect crop,double corner,int size){
        var cropped=new CroppedBitmap(source,new Int32Rect((int)crop.X,(int)crop.Y,(int)crop.Width,(int)crop.Height));
        var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
        using(var g=visual.RenderOpen()){
            g.PushClip(new RectangleGeometry(new Rect(0,0,size,size),size*corner,size*corner));
            g.DrawImage(cropped,new Rect(0,0,size,size));g.Pop();
        }
        var bmp=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bmp.Render(visual);bmp.Freeze();return bmp;
    }
    static byte[] Png(BitmapSource b){var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using(var m=new MemoryStream()){e.Save(m);return m.ToArray();}}
    [STAThread] static void Main(string[] args){
        string root=args.Length>0?args[0]:".";var images=new List<byte[]>();var frames=new List<BitmapSource>();
        // make_icon.exe <repo> --from <picture.png> [crop x y w h] [corner%]: use a finished picture (e.g. made with ChatGPT)
        // instead of drawing: crop to the icon tile, round the corners to transparent and scale it to every size.
        BitmapSource source=null;Rect crop=Rect.Empty;double corner=0.18;
        if(args.Length>2&&args[1]=="--from"){
            source=new BitmapImage(new Uri(Path.GetFullPath(args[2])));
            if(args.Length>=7)crop=new Rect(double.Parse(args[3]),double.Parse(args[4]),double.Parse(args[5]),double.Parse(args[6]));
            else crop=new Rect(0,0,source.PixelWidth,source.PixelHeight);
            if(args.Length>=8)corner=double.Parse(args[7])/100;
        }
        foreach(int size in Sizes){var b=source==null?Draw(size):FromPicture(source,crop,corner,size);frames.Add(b);images.Add(Png(b));}
        // ICO with PNG-compressed entries (Windows Vista and later).
        using(var o=new BinaryWriter(File.Create(Path.Combine(root,@"src\app.ico")))){
            o.Write((ushort)0);o.Write((ushort)1);o.Write((ushort)Sizes.Length);int offset=6+16*Sizes.Length;
            for(int i=0;i<Sizes.Length;i++){o.Write((byte)(Sizes[i]>=256?0:Sizes[i]));o.Write((byte)(Sizes[i]>=256?0:Sizes[i]));o.Write((byte)0);o.Write((byte)0);
                o.Write((ushort)1);o.Write((ushort)32);o.Write(images[i].Length);o.Write(offset);offset+=images[i].Length;}
            foreach(var png in images)o.Write(png);
        }
        // Preview: 256 icon plus every size at 1:1 on a dark and a light strip.
        var preview=new DrawingVisual();
        using(var g=preview.RenderOpen()){
            g.DrawRectangle(B("#202020"),null,new Rect(0,0,560,300));g.DrawRectangle(B("#F3F3F3"),null,new Rect(280,0,280,300));
            g.DrawImage(frames[frames.Count-1],new Rect(12,22,256,256));double x=292,y=20;
            for(int i=0;i<frames.Count-1;i++){var f=frames[i];g.DrawImage(f,new Rect(x,y,f.PixelWidth,f.PixelHeight));x+=f.PixelWidth+10;if(x>520){x=292;y+=110;}}
        }
        var shot=new RenderTargetBitmap(560,300,96,96,PixelFormats.Pbgra32);shot.Render(preview);
        File.WriteAllBytes(Path.Combine(root,@"docs\images\아이콘.png"),Png(shot));
        Console.WriteLine("app.ico: "+Sizes.Length+" sizes");
    }
}
