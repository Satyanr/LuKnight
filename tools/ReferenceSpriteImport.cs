using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class ReferenceSpriteImport {
 public static Bitmap Cell(Bitmap source,int index,int columns,int rows) {
  int x0=(int)Math.Round(index%columns*source.Width/(double)columns);
  int y0=(int)Math.Round(index/columns*source.Height/(double)rows);
  int w=(int)Math.Round((index%columns+1)*source.Width/(double)columns)-x0;
  int h=(int)Math.Round((index/columns+1)*source.Height/(double)rows)-y0;
  Bitmap cell=new Bitmap(w,h,PixelFormat.Format32bppArgb);
  for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
   Color p=source.GetPixel(x0+x,y0+y);
   double green=p.G-Math.Max(p.R,p.B);
   if(green>12) {
    // Chroma-key supplied green source; preserve the cyan markings (B ~= G).
    double alpha=Math.Max(0,1-green/180.0);
    if(alpha<.08) p=Color.Transparent;
    else p=Color.FromArgb((int)(p.A*alpha),p.R,Math.Min(p.G,Math.Max(p.R,p.B)),p.B);
   }
   cell.SetPixel(x,y,p);
  }
  // Retain the principal character only; remove detached generation specks.
  bool[] solid=new bool[w*h];
  for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){
   bool ok=true;for(int dy=-1;dy<=1&&ok;dy++)for(int dx=-1;dx<=1;dx++)if(cell.GetPixel(x+dx,y+dy).A<=180){ok=false;break;}
   solid[y*w+x]=ok;
  }
  bool[] seen=new bool[w*h]; List<int> best=new List<int>();
  for(int i=0;i<seen.Length;i++)if(!seen[i]&&solid[i]){
   var q=new Queue<int>(); var component=new List<int>(); q.Enqueue(i);seen[i]=true;
   while(q.Count>0){int n=q.Dequeue(),x=n%w,y=n/w;component.Add(n);
    foreach(int d in new[]{-1,1,-w,w}){int k=n+d;if(k<0||k>=seen.Length||Math.Abs(k%w-x)>1||seen[k]||!solid[k])continue;seen[k]=true;q.Enqueue(k);}}
   if(component.Count>best.Count)best=component;
  }
  if(best.Count<w*h/15)throw new Exception("Missing character in cell "+index);
  bool[] keep=new bool[w*h];
  foreach(int n in best)for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){
   int x=n%w+dx,y=n/w+dy;if(x>=0&&x<w&&y>=0&&y<h)keep[y*w+x]=true;}
  for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(!keep[y*w+x])cell.SetPixel(x,y,Color.Transparent);
  return cell;
 }
 public static void Save(Bitmap cell,string path,double headWidth,double headX,double baseline,double targetWidth=250,double targetX=255) {
  // Scale by anatomical head landmark, NEVER by pose/silhouette bounds.
  double scale=targetWidth/headWidth;
  using(var canvas=new Bitmap(510,660,PixelFormat.Format32bppArgb)) {
   using(var g=Graphics.FromImage(canvas)){
    g.CompositingMode=CompositingMode.SourceCopy;
    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode=PixelOffsetMode.HighQuality;
    g.DrawImage(cell,new RectangleF((float)(targetX-headX*scale),(float)(639-baseline*scale),(float)(cell.Width*scale),(float)(cell.Height*scale)),new RectangleF(0,0,cell.Width,cell.Height),GraphicsUnit.Pixel);
   }
   // A rejected import must never silently clip an ear or foot.
   for(int y=0;y<660;y++)if(canvas.GetPixel(0,y).A>0||canvas.GetPixel(509,y).A>0)throw new Exception("Clipped side: "+path);
   for(int x=0;x<510;x++)if(canvas.GetPixel(x,0).A>0||canvas.GetPixel(x,659).A>0)throw new Exception("Clipped top/bottom: "+path);
   canvas.Save(path,ImageFormat.Png);
  }
 }
 public static void SavePart(Bitmap cell,string path,double topTrim=0) {
  int l=cell.Width,t=cell.Height,r=0,b=0;
  for(int y=0;y<cell.Height;y++)for(int x=0;x<cell.Width;x++)if(cell.GetPixel(x,y).A>20){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);}
  t+=(int)((b-t+1)*topTrim);
  using(var part=cell.Clone(Rectangle.FromLTRB(l,t,r+1,b+1),PixelFormat.Format32bppArgb))part.Save(path,ImageFormat.Png);
 }

}
