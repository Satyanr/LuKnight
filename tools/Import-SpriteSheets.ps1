$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class SpriteSheetImport {
 public static Bitmap Extract(Bitmap sheet,int x0,int y0,int w,int h) {
  bool[] solid=new bool[w*h];
  for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++) {
   bool good=true;
   for(int dy=-1;dy<=1 && good;dy++)for(int dx=-1;dx<=1;dx++) if(sheet.GetPixel(x0+x+dx,y0+y+dy).A<220){good=false;break;}
   solid[y*w+x]=good;
  }
  bool[] seen=new bool[w*h]; List<int> best=new List<int>();
  for(int i=0;i<solid.Length;i++) if(solid[i]&&!seen[i]) {
   List<int> component=new List<int>();Queue<int> q=new Queue<int>();q.Enqueue(i);seen[i]=true;
   while(q.Count>0){int n=q.Dequeue();component.Add(n);int x=n%w,y=n/w;
    foreach(int d in new int[]{-1,1,-w,w}){int k=n+d;if(k<0||k>=solid.Length||Math.Abs(k%w-x)>1||seen[k]||!solid[k])continue;seen[k]=true;q.Enqueue(k);}
   }
   if(component.Count>best.Count)best=component;
  }
  if(best.Count<w*h/12)throw new Exception("No complete sprite foreground");
  bool[] keep=new bool[w*h];
  foreach(int i in best){int x=i%w,y=i/w;for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)if(x+dx>=0&&x+dx<w&&y+dy>=0&&y+dy<h)keep[(y+dy)*w+x+dx]=true;}
  int l=w,t=h,r=0,b=0;
  for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(keep[y*w+x]&&sheet.GetPixel(x0+x,y0+y).A>10){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);}
  Bitmap frame=new Bitmap(r-l+1,b-t+1,PixelFormat.Format32bppArgb);
  for(int y=t;y<=b;y++)for(int x=l;x<=r;x++)if(keep[y*w+x])frame.SetPixel(x-l,y-t,sheet.GetPixel(x0+x,y0+y));
  return frame;
 }
 public static Rectangle[] Objects(Bitmap sheet) {
  int w=sheet.Width,h=sheet.Height;bool[] mask=new bool[w*h];int transparent=0;
  for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){
   if(sheet.GetPixel(x,y).A==0)transparent++;
   bool good=true;for(int dy=-1;dy<=1&&good;dy++)for(int dx=-1;dx<=1;dx++)if(sheet.GetPixel(x+dx,y+dy).A<220){good=false;break;}mask[y*w+x]=good;
  }
  if(transparent<w*h/10)throw new Exception("Not a transparent sprite sheet");
  var components=new List<Tuple<int,Rectangle>>();bool[] seen=new bool[w*h];
  for(int i=0;i<mask.Length;i++)if(mask[i]&&!seen[i]){
   var q=new Queue<int>();q.Enqueue(i);seen[i]=true;int size=0,l=w,t=h,r=0,b=0;
   while(q.Count>0){int n=q.Dequeue(),x=n%w,y=n/w;size++;l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);
    foreach(int d in new int[]{-1,1,-w,w}){int k=n+d;if(k<0||k>=mask.Length||Math.Abs(k%w-x)>1||seen[k]||!mask[k])continue;seen[k]=true;q.Enqueue(k);}}
   if(size>1000)components.Add(Tuple.Create(size,Rectangle.FromLTRB(Math.Max(0,l-3),Math.Max(0,t-3),Math.Min(w,r+4),Math.Min(h,b+4))));
  }
  if(components.Count<16)throw new Exception("Expected 16 separate figures, got "+components.Count);
  var chosen=components.OrderByDescending(c=>c.Item1).Take(16).Select(c=>c.Item2).OrderBy(r=>r.Top+r.Height/2).ToArray();
  var result=new List<Rectangle>();for(int row=0;row<4;row++)result.AddRange(chosen.Skip(row*4).Take(4).OrderBy(r=>r.Left));return result.ToArray();
 }
 public static void Import(string source,string folder,string prefix,bool sleep,bool expression){
  using(Bitmap sheet=new Bitmap(source)){
   var frames=new List<Bitmap>();
   foreach(var rect in Objects(sheet))frames.Add(Extract(sheet,rect.X,rect.Y,rect.Width,rect.Height));
   string[] names={"happy","wink","sad","dizzy","angry","surprised","determined","neutral"};
   for(int i=0;i<(expression?8:16);i++)using(Bitmap canvas=new Bitmap(510,660,PixelFormat.Format32bppArgb)){
    var f=frames[i]; double frameScale=Math.Min(450.0/f.Width,(sleep?330.0:590.0)/f.Height); float w=(float)(f.Width*frameScale),h=(float)(f.Height*frameScale);
    using(Graphics g=Graphics.FromImage(canvas)){g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(f,(510-w)/2,639-h,w,h);}
    string name=expression?names[i]:prefix+"_"+i.ToString("000");canvas.Save(System.IO.Path.Combine(folder,name+".png"),ImageFormat.Png);
   }
   foreach(var f in frames)f.Dispose();
  }
 }
}
"@
$sheetRoot = Join-Path $PSScriptRoot '..\output\imagegen\sprite-sources'
$assetRoot = Join-Path $PSScriptRoot '..\Assets\Characters\LuKnight'
foreach ($state in @('Idle','Walk','Sleep','Grabbed','Falling','Hanging','Climbing','Expressions')) {
    [SpriteSheetImport]::Import((Join-Path $sheetRoot "$state.png"),(Join-Path $assetRoot $state),$state.ToLowerInvariant(),($state -eq 'Sleep'),($state -eq 'Expressions'))
    Write-Output "Imported $state"
}


Copy-Item -LiteralPath (Join-Path $assetRoot 'Expressions\neutral.png') -Destination (Join-Path $assetRoot 'Reference\luknight_master.png') -Force
Remove-Item -LiteralPath (Join-Path $assetRoot 'Expressions\neutral.png')

