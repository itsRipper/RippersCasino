"""Author boss sprite sheets and Factory scenery with integer-grid drawing."""
from pathlib import Path
import struct, zlib, math, sys
CHECK = "--check" in sys.argv
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets'
INK='#11151c'; SKIN='#bf8d6d'; LIGHT='#dfb18b'; SHADE='#825d4c'
DARK='#292d36'; STEEL='#525c65'; EDGE='#8e999b'; IVORY='#d9d4b5'
OLIVE='#62674a'; O_HI='#91916a'; O_DARK='#3b4334'; RUST='#8c5542'; RED='#b44e43'
def rgba(c):
 if c is None:return (0,0,0,0)
 if isinstance(c,tuple):return c
 return tuple(int(c[i:i+2],16) for i in (1,3,5))+(255,)
class Canvas:
 def __init__(self,w,h,bg=None):self.w=w;self.h=h;self.p=[rgba(bg)]*(w*h)
 def dot(self,x,y,c):
  x=int(x);y=int(y)
  if 0<=x<self.w and 0<=y<self.h:self.p[y*self.w+x]=rgba(c)
 def rect(self,x,y,w,h,c):
  for yy in range(int(y),int(y+h)):
   for xx in range(int(x),int(x+w)):self.dot(xx,yy,c)
 def line(self,a,b,c,width=1):
  x,y=map(int,a);ex,ey=map(int,b);dx=abs(ex-x);sx=1 if x<ex else -1;dy=-abs(ey-y);sy=1 if y<ey else -1;e=dx+dy
  while True:
   self.rect(x-width//2,y-width//2,width,width,c)
   if x==ex and y==ey:break
   e2=2*e
   if e2>=dy:e+=dy;x+=sx
   if e2<=dx:e+=dx;y+=sy
 def poly(self,pts,c):
  for y in range(math.floor(min(p[1] for p in pts)),math.ceil(max(p[1] for p in pts))+1):
   xs=[]
   for a,b in zip(pts,pts[1:]+pts[:1]):
    if (a[1]<=y+.5<b[1]) or (b[1]<=y+.5<a[1]):xs.append(a[0]+(y+.5-a[1])*(b[0]-a[0])/(b[1]-a[1]))
   xs.sort()
   for a,b in zip(xs[::2],xs[1::2]):self.rect(math.ceil(a-.5),y,math.ceil(b-.5)-math.ceil(a-.5),1,c)
 def paste(self,other,x,y,scale=1):
  for yy in range(other.h):
   for xx in range(other.w):
    c=other.p[yy*other.w+xx]
    if c[3]:
     px=x+xx*scale;py=y+yy*scale
     if c[3]<255 and 0<=px<self.w and 0<=py<self.h:
      below=self.p[py*self.w+px];a=c[3]/255;outA=a+below[3]/255*(1-a)
      c=tuple(round((c[k]*a+below[k]*below[3]/255*(1-a))/outA) for k in range(3))+(round(outA*255),)
     self.rect(px,py,scale,scale,c)
 def save(self,path):
  def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
  rows=b''.join(b'\0'+bytes(v for pixel in self.p[y*self.w:(y+1)*self.w] for v in pixel) for y in range(self.h))
  path.parent.mkdir(parents=True,exist_ok=True)
  data=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',self.w,self.h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows,9))+chunk(b'IEND',b'')
  if CHECK:
   if path.parent.name!='artifacts':
    from PIL import Image
    with Image.open(path) as image:
     assert image.size==(self.w,self.h) and image.convert('RGBA').tobytes()==bytes(v for pixel in self.p for v in pixel),'Asset differs from source: '+str(path)
  else:path.write_bytes(data)
def limb(c,points,colour,highlight=None,width=5):
 for a,b in zip(points,points[1:]):c.line(a,b,INK,width+2)
 for a,b in zip(points,points[1:]):c.line(a,b,colour,width)
 if highlight:
  a,b=points[0],points[1];c.line((a[0]-1,a[1]),(b[0]-1,b[1]),highlight,1)
def boot(c,ankle,sneaker=False,back=False):
 x,y=ankle
 c.poly([(x-3,y-2),(x+2,y-2),(x+2,y),(x+6,y+1),(x+6,y+3),(x-3,y+3)],INK)
 colour=IVORY if sneaker else ('#313a3d' if back else '#454c50')
 c.rect(x-2,y-1,4,3,colour);c.rect(x+1,y+1,4,1,colour)
 c.line((x-2,y+2),(x+5,y+2),'#a5aaa0' if sneaker else '#69716f')
# Explicit contact/passing poses; front/back legs trade roles, never mirror bodies.
POSES=[([(22,31),(17,36),(10,40)],[(28,31),(34,36),(38,41)]),
       ([(22,31),(15,34),(13,38)],[(28,31),(33,37),(34,42)]),
       ([(22,30),(16,34),(19,37)],[(28,30),(29,36),(25,42)]),
       ([(22,30),(22,34),(27,37)],[(28,30),(25,37),(19,42)]),
       ([(22,31),(31,35),(35,41)],[(28,31),(22,37),(15,40)]),
       ([(22,31),(30,37),(31,42)],[(28,31),(24,34),(21,37)]),
       ([(22,30),(26,36),(25,42)],[(28,30),(32,34),(29,37)]),
       ([(22,30),(21,37),(17,42)],[(28,30),(34,35),(36,38)])]

def runner(kind,frame):
 c=Canvas(48,48);bob=(0,1,1,0,0,1,1,0)[frame]
 pants={'killa':DARK,'tagilla':'#44433e','gluhar':'#62674f','shturman':'#444f42','sanitar':'#5b6256','reshala':'#8f9580'}[kind]
 darkpants={'killa':'#20242c','tagilla':'#303431','gluhar':'#424b3e','shturman':'#303c33','sanitar':'#3d4943','reshala':'#626e60'}[kind]
 far,near=POSES[frame]
 limb(c,far,darkpants,width=5);boot(c,far[-1],kind=='reshala',True)
 # Far swinging arm, partially occluded by jacket/rig.
 fararm=[(20,21-bob),(14 if frame<4 else 19,26-bob),(12 if frame<4 else 22,30-bob)]
 armcolour=SHADE if kind in ('tagilla','gluhar') else darkpants
 limb(c,fararm,armcolour,width=4)
 # Body contours vary deliberately rather than dressing one generic soldier.
 if kind=='tagilla':body=[(20,17-bob),(30,17-bob),(34,22-bob),(31,32-bob),(19,32-bob),(16,23-bob)];base=SKIN
 elif kind=='shturman':body=[(19,17-bob),(31,17-bob),(34,24-bob),(32,35-bob),(16,35-bob),(17,23-bob)];base='#51535a'
 else:body=[(21,17-bob),(30,17-bob),(33,23-bob),(31,32-bob),(18,32-bob),(17,23-bob)];base={'killa':DARK,'gluhar':'#30343c','sanitar':'#356b8b','reshala':'#514940'}[kind]
 c.poly(body,INK);c.poly([(x+1 if x<25 else x-1,y+1 if y<25 else y-1) for x,y in body],base)
 if kind in ('killa','gluhar','tagilla'):
  c.rect(20,18-bob,12,11,INK);c.rect(21,18-bob,10,10,OLIVE if kind=='killa' else O_DARK)
  c.rect(22,19-bob,8,3,O_HI if kind=='killa' else OLIVE)
  for x in (21,25,29):c.rect(x,24-bob,3,6,INK);c.rect(x,24-bob,2,5,OLIVE)
  c.rect(18,18-bob,2,7,STEEL)
 elif kind=='shturman':
  for x,y,w in [(19,18,5),(23,19,4),(27,18,4),(30,17,3)]:
   c.rect(x,y-bob,w,4,'#887e6a');c.rect(x,y-bob,w-1,1,'#b8a88a')
  c.line((25,23-bob),(25,32-bob),'#797a77');c.rect(17,30-bob,6,4,'#3e4447')
 elif kind=='sanitar':
  c.rect(23,19-bob,5,11,DARK);c.line((21,20-bob),(23,29-bob),'#629bb0');c.line((29,20-bob),(27,31-bob),'#629bb0')
  c.line((20,19-bob),(30,32-bob),RUST,2);c.rect(27,29-bob,9,7,INK);c.rect(28,30-bob,7,5,'#b17a43');c.rect(29,30-bob,5,1,'#d6a15e');c.rect(30,31-bob,2,3,IVORY);c.rect(29,32-bob,4,1,IVORY)
 elif kind=='reshala':
  c.line((25,20-bob),(25,30-bob),'#8d8774');c.line((20,19-bob),(24,23-bob),'#746b5a');c.rect(19,27-bob,4,1,'#8d8774');c.rect(28,26-bob,3,1,'#393b38')
 c.rect(19,31-bob,12,2,INK);c.rect(25,31-bob,3,1,EDGE)
 # The near leg sits in front of coat tails and keeps the same anchor in all sheets.
 limb(c,near,pants,'#555e63' if kind=='killa' else ('#b2b49b' if kind=='reshala' else '#7b8068'),5);boot(c,near[-1],kind=='reshala')
 if kind=='killa':
  for pts in (near,far):
   for a,b in zip(pts,pts[1:]):c.line((a[0]-1,a[1]+2),(b[0]-1,b[1]),IVORY)
 elif kind in ('gluhar','reshala'):
  for pts in (near,far):
   k=pts[1];c.rect(k[0]-2,k[1]-1,3,2,O_DARK);c.rect(k[0]+1,k[1]+2,2,1,O_HI)
 elif kind=='tagilla':
  k=near[1];c.rect(k[0]-2,k[1]-1,5,3,INK);c.rect(k[0]-1,k[1],3,1,STEEL)
 if kind=='sanitar':
  c.rect(18,28-bob,9,7,INK);c.rect(19,29-bob,7,5,'#b17a43');c.rect(19,29-bob,6,1,'#d6a15e');c.rect(21,30-bob,2,3,IVORY);c.rect(20,31-bob,4,1,IVORY)
 # Heads: flat, deliberate colour clusters, no texture/noise or gradients.
 if kind=='killa':
  c.poly([(25,3-bob),(33,3-bob),(37,7-bob),(38,17-bob),(32,20-bob),(24,17-bob),(22,9-bob)],INK)
  c.poly([(25,4-bob),(32,4-bob),(35,7-bob),(35,12-bob),(24,12-bob),(23,9-bob)],STEEL)
  for x in (26,29,32):c.rect(x,4-bob,1,7,IVORY)
  c.rect(30,11-bob,8,8,EDGE);c.rect(31,12-bob,6,6,DARK);c.rect(32,13-bob,6,2,INK);c.rect(23,12-bob,5,5,'#414b52')
 elif kind=='tagilla':
  c.rect(25,4-bob,8,4,INK);c.rect(24,4-bob,8,2,DARK);c.rect(21,5-bob,4,2,DARK)
  c.poly([(29,6-bob),(37,7-bob),(37,19-bob),(32,21-bob),(27,16-bob),(27,9-bob)],INK)
  c.poly([(29,7-bob),(36,8-bob),(36,18-bob),(32,20-bob),(28,15-bob),(28,9-bob)],STEEL)
  c.rect(29,10-bob,7,4,INK);c.rect(30,11-bob,5,1,EDGE);c.rect(30,16-bob,6,2,RED);c.rect(33,18-bob,2,1,IVORY)
 elif kind=='shturman':
  c.poly([(25,4-bob),(32,4-bob),(36,8-bob),(36,16-bob),(30,20-bob),(22,18-bob),(21,10-bob)],INK)
  c.poly([(25,5-bob),(31,5-bob),(34,8-bob),(31,9-bob),(26,9-bob),(24,16-bob),(22,15-bob),(22,10-bob)],'#666971')
  c.poly([(28,9-bob),(34,10-bob),(36,14-bob),(33,18-bob),(28,17-bob),(26,13-bob)],SHADE)
  c.rect(31,11-bob,3,1,INK);c.rect(33,12-bob,3,2,SKIN);c.rect(28,16-bob,6,2,DARK)
 else:
  c.poly([(26,4-bob),(32,4-bob),(35,7-bob),(35,10-bob),(38,12-bob),(36,14-bob),(35,18-bob),(28,18-bob),(25,14-bob),(24,8-bob)],INK)
  c.poly([(27,5-bob),(32,5-bob),(34,8-bob),(34,11-bob),(37,12-bob),(34,14-bob),(34,17-bob),(29,17-bob),(26,13-bob),(25,8-bob)],SKIN)
  c.rect(29,7-bob,4,3,LIGHT);c.rect(34,11-bob,2,2,LIGHT);c.rect(32,10-bob,3,1,INK);c.rect(26,11-bob,2,3,SHADE);c.rect(31,15-bob,4,2,SHADE)
  if kind=='sanitar':
   c.rect(25,6-bob,10,3,IVORY);c.rect(26,6-bob,8,1,'#f0e4c5');c.rect(32,8-bob,2,1,'#aaa891');c.rect(29,4-bob,4,2,LIGHT)
  else:
   c.rect(26,4-bob,7,3,DARK);c.rect(25,6-bob,3,4,DARK);c.rect(28,4-bob,4,1,STEEL if kind=='gluhar' else '#645844');c.dot(33,14-bob,DARK)
 # Forward equipment remains coherent through the run, with a small shoulder bounce.
 if kind=='tagilla':
  c.line((11,9-bob),(35,29-bob),INK,4);c.line((11,9-bob),(35,29-bob),RUST,2)
  c.poly([(6,4-bob),(12,3-bob),(19,10-bob),(13,15-bob),(6,9-bob)],INK);c.poly([(7,5-bob),(12,4-bob),(17,10-bob),(13,13-bob),(7,9-bob)],STEEL);c.line((8,5-bob),(12,5-bob),EDGE)
  limb(c,[(30,21-bob),(35,24-bob),(32,27-bob)],SKIN,LIGHT,5);c.rect(30,25-bob,5,4,INK);c.rect(31,26-bob,4,2,RED)
  c.rect(31,21-bob,2,2,SHADE);c.dot(33,23-bob,DARK)
 else:
  weaponY=25-bob
  if kind=='shturman':
   c.line((24,27-bob),(45,24-bob),INK,3);c.line((24,27-bob),(39,25-bob),RUST,2);c.line((37,24-bob),(46,23-bob),STEEL);c.rect(32,21-bob,7,2,INK);c.rect(32,21-bob,5,1,EDGE);c.line((32,27-bob),(32,31-bob),INK,2)
  elif kind=='killa':
   c.rect(24,24-bob,18,3,INK);c.rect(28,24-bob,13,1,STEEL);c.rect(41,24-bob,5,1,STEEL);c.rect(31,27-bob,5,4,INK);c.rect(32,28-bob,3,2,STEEL);c.rect(28,22-bob,5,2,INK)
  elif kind=='gluhar':
   c.rect(27,24-bob,17,4,INK);c.rect(30,24-bob,11,1,STEEL);c.rect(43,25-bob,4,2,STEEL);c.rect(28,28-bob,3,4,INK);c.rect(35,28-bob,2,3,INK);c.rect(32,22-bob,6,2,INK)
  elif kind=='sanitar':
   c.rect(32,25-bob,11,3,INK);c.rect(35,25-bob,7,1,STEEL);c.rect(34,28-bob,3,3,INK)
  elif kind=='reshala':
   c.rect(34,25-bob,9,2,INK);c.rect(35,25-bob,7,1,'#c6a667');c.rect(35,27-bob,3,3,INK);c.rect(36,27-bob,1,2,'#957648')
  elbow=(29,27-bob) if frame<4 else (28,26-bob)
  colour=SKIN if kind=='gluhar' else {'killa':DARK,'shturman':'#51535a','sanitar':'#356b8b','reshala':'#514940'}[kind]
  limb(c,[(28,21-bob),elbow,(35,27-bob)],colour,LIGHT if kind=='gluhar' else None,4)
  if kind=='killa':c.line((27,21-bob),(28,26-bob),IVORY)
  if kind=='gluhar':c.rect(28,22-bob,2,3,DARK);c.dot(30,24-bob,DARK)
  glove='#73a397' if kind=='sanitar' else DARK;c.rect(33,26-bob,4,3,INK);c.rect(34,26-bob,3,2,glove)
 return c

def factory_layers():
 far=Canvas(384,280,'#141c20');mid=Canvas(384,280);front=Canvas(384,280)
 # Concrete bays, factory glazing and fractured panes, explicitly authored clusters.
 for x in (0,128,256):
  far.rect(x+3,3,122,274,'#1a2327');far.line((x+4,2),(x+4,278),'#263136');far.line((x+3,142),(x+122,142),'#11191d')
  far.rect(x+17,21,96,72,'#0e181e');far.rect(x+20,24,90,65,'#2b3d44')
  for dx in (20,43,66,89):far.rect(x+dx,24,20,29,'#34474c');far.rect(x+dx,57,20,31,'#293b43')
  far.poly([(x+22,25),(x+40,25),(x+38,39),(x+32,35),(x+22,45)],'#192b33')
  far.poly([(x+70,60),(x+85,58),(x+85,76),(x+79,70),(x+72,80)],'#12232b')
  for dx in (40,63,86,109):far.rect(x+dx,24,3,65,'#151f23')
  far.rect(x+17,53,96,4,'#151f23');far.rect(x+14,91,102,3,'#354043')
  far.line((x+87,165),(x+95,174),'#0f171b');far.line((x+95,174),(x+91,183),'#0f171b')
  far.rect(x+5,7,5,270,'#293134');far.rect(x+11,7,2,270,'#0d171c')
 # Overhead steel trusses and catwalk rails.
 far.rect(0,8,384,5,'#303a3b');far.rect(0,13,384,2,'#0b1419')
 for x in range(0,384,64):far.line((x,13),(x+62,47),'#242f33',3)
 far.rect(0,112,384,4,'#283538');far.rect(0,137,384,8,'#111b20');far.rect(0,137,384,1,'#3a4140')
 for x in range(0,384,24):far.rect(x,114,2,22,'#303b3c')
 # Pipe network, valves, ventilation and a pair of storage tanks.
 mid.rect(0,157,384,7,'#10191d');mid.rect(0,158,384,2,'#3d4039');mid.rect(0,162,384,1,'#222f31')
 for x in (15,186,327):
  mid.rect(x,27,9,232,'#10191c');mid.rect(x+1,27,6,230,'#373c37');mid.rect(x+1,29,2,226,'#4b4940')
  for y in (48,131,220):mid.rect(x-2,y,13,4,'#182427');mid.rect(x-2,y,13,1,'#66604a')
 mid.rect(56,178,71,79,'#0d171b');mid.rect(58,180,67,74,'#263236');mid.rect(60,183,3,68,'#39423e')
 mid.rect(64,170,55,9,'#19282c');mid.rect(66,171,51,2,'#3a4646')
 for y in (194,235):mid.rect(58,y,67,3,'#111e23');mid.rect(61,y,62,1,'#414740')
 mid.rect(141,187,33,54,'#111c21');mid.rect(143,189,29,50,'#2e3939')
 for y in range(195,232,5):mid.rect(147,y,21,2,'#121e23')
 mid.rect(235,193,66,60,'#101b20');mid.rect(237,195,62,56,'#29383a');mid.rect(241,200,53,18,'#14232a');mid.rect(246,204,7,3,'#49605e');mid.rect(258,204,6,3,'#635b43')
 # Industrial lamps and restrained flat pixel light, no gradients.
 for x in (45,278):
  mid.line((x,15),(x,38),'#0c151a');mid.poly([(x-9,39),(x+9,39),(x+14,45),(x-14,45)],'#4d4a3b');mid.rect(x-8,45,16,2,'#a39263')
  mid.poly([(x-7,48),(x+7,48),(x+20,72),(x-20,72)],(108,96,60,14))
 # Hanging cable has a deliberately stepped sag.
 for a,b in [((195,20),(215,39)),((215,39),(242,44)),((242,44),(267,30))]:mid.line(a,b,'#0b151b',2)
 # Suspended lamps and conveyor rollers keep the same vanishing plane.
 for x in (101,224,356):
  mid.line((x,17),(x,92),'#0c1518',2);mid.poly([(x-12,92),(x+12,92),(x+17,98),(x-17,98)],'#5c5745');mid.rect(x-10,99,20,2,'#baa474')
 mid.rect(0,250,384,4,'#3a4644')
 for x in range(6,384,18):mid.rect(x,254,10,3,'#111d20');mid.rect(x,254,9,1,'#5b6255')
 # Low foreground crates, steel floor and warning stripes stay below runner bodies.
 front.rect(0,266,384,14,'#10191e');front.rect(0,266,384,2,'#3b423d')
 for x in range(0,384,16):front.line((x,273),(x+7,273),'#263235')
 for x,w in [(29,27),(310,35)]:
  front.rect(x,245,w,21,'#10191c');front.rect(x+1,246,w-2,19,'#3d3c30');front.rect(x+3,249,w-6,1,'#605842');front.line((x+3,250),(x+w-4,263),'#242c2a');front.line((x+3,263),(x+w-4,250),'#242c2a')
 for x in (132,146,160):front.poly([(x,266),(x+6,266),(x+2,273),(x-4,273)],'#716044')
 for x,y,w in [(88,263,7),(177,265,4),(273,262,8)]:front.rect(x,y,w,2,'#49504a')
 return far,mid,front

def main():
 ids=('killa','tagilla','gluhar','shturman','sanitar','reshala')
 contact=Canvas(6*48*3,8*48*3,'#202a30')
 for column,kind in enumerate(ids):
  sheet=Canvas(192,96)
  frames=[runner(kind,f) for f in range(8)]
  assert len({tuple(art.p) for art in frames})==8,'Run poses must differ'
  for art in frames:
   assert all(art.p[y*48+x][3]==0 for x,y in [(x,0) for x in range(48)]+[(x,47) for x in range(48)]+[(0,y) for y in range(48)]+[(47,y) for y in range(48)]),'Frame touches atlas boundary'
  for f,art in enumerate(frames):
   sheet.paste(art,(f%4)*48,(f//4)*48);contact.paste(art,column*144,f*144,3)
   assert any(p[3] for p in art.p),'Empty frame'
  colours=set(sheet.p);assert len(colours)<=40,(kind,len(colours))
  sheet.save(OUT/('boss-'+kind+'.png'));print(kind,'192x96 RGBA, colours:',len(colours))
 contact.save(ROOT/'artifacts/PixelRunners.png')
 layers=factory_layers();preview=Canvas(768,560)
 for name,layer in zip(('far','machinery','floor'),layers):layer.save(OUT/('factory-'+name+'.png'));preview.paste(layer,0,0,2)
 preview.save(ROOT/'artifacts/Factory.png')
 print('Factory: three 384x280 cached pixel layers. Previews in artifacts/.')
if __name__=='__main__':main()
