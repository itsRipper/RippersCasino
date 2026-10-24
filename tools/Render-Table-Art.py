from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math, os, sys
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets'
FONT_DIR=Path(os.environ.get('WINDIR', 'C:/Windows'))/'Fonts'
S=3
INK='#172126'; PAPER='#ebe5d3'; GOLD='#b29a66'; RED='#903b37'
def font(n,bold=False):
 return ImageFont.truetype(str(FONT_DIR/('georgiab.ttf' if bold else 'georgia.ttf')),n*S)
class Art:
 def __init__(self,w,h,bg=(0,0,0,0)): self.w=w; self.h=h; self.im=Image.new('RGBA',(w*S,h*S),bg);self.d=ImageDraw.Draw(self.im)
 def rect(self,box,c,outline=None,width=1,r=0): self.d.rounded_rectangle(tuple(int(v*S) for v in box),r*S,fill=c,outline=outline,width=width*S)
 def ellipse(self,box,c,outline=None,width=1): self.d.ellipse(tuple(int(v*S) for v in box),fill=c,outline=outline,width=width*S)
 def line(self,pts,c,width=1): self.d.line([(int(x*S),int(y*S)) for x,y in pts],fill=c,width=width*S,joint='curve')
 def poly(self,pts,c): self.d.polygon([(int(x*S),int(y*S)) for x,y in pts],fill=c)
 def text(self,xy,t,n,c,bold=False,anchor='mm'): self.d.text((xy[0]*S,xy[1]*S),t,font=font(n,bold),fill=c,anchor=anchor)
 def finish(self): return self.im.resize((self.w,self.h),Image.Resampling.LANCZOS)
 def save(self,name): self.finish().save(OUT/name,optimize=True)
def suit(a,x,y,size,s,c):
 # Explicit four suit silhouettes; no dependency on symbol glyphs.
 q=size/2
 if s==1:a.poly([(x,y-q),(x+q*.8,y),(x,y+q),(x-q*.8,y)],c)
 elif s==2:
  a.ellipse((x-q*.85,y-q*.85,x+q*.05,y+q*.05),c);a.ellipse((x-q*.05,y-q*.85,x+q*.85,y+q*.05),c)
  a.poly([(x-q*.83,y-q*.22),(x+q*.83,y-q*.22),(x,y+q)],c)
 else:
  if s==0:
   for dx,dy in [(0,-.48),(-.42,.05),(.42,.05)]:a.ellipse((x+(dx-.42)*q,y+(dy-.42)*q,x+(dx+.42)*q,y+(dy+.42)*q),c)
  else:
   a.ellipse((x-q*.83,y-q*.1,x+q*.02,y+q*.7),c);a.ellipse((x-q*.02,y-q*.1,x+q*.83,y+q*.7),c);a.poly([(x-q*.8,y+q*.15),(x,y-q),(x+q*.8,y+q*.15)],c)
  a.poly([(x-q*.15,y+q*.2),(x+q*.15,y+q*.2),(x+q*.4,y+q),(x-q*.4,y+q)],c)
def court(a,rank,s,col):
 a.rect((25,31,71,101),'#ded5bb',GOLD,r=2)
 for flip in (False,True):
  b=Art(46,35)
  b.poly([(4,34),(8,24),(18,21),(28,21),(38,25),(42,34)],col)
  b.poly([(8,26),(23,34),(38,26),(29,34),(17,34)],GOLD)
  b.ellipse((15,7,31,25),'#c9ad84',INK)
  b.poly([(15,10),(16,5),(20,7),(23,3),(26,7),(30,5),(31,11)],GOLD)
  b.line([(19,14),(21,14)],INK);b.line([(26,14),(28,14)],INK);b.line([(24,15),(23,18),(26,18)],'#805c49')
  if rank==10:
   b.poly([(15,10),(17,5),(27,4),(32,9)],col);b.line([(9,18),(9,31)],GOLD,2)
  if rank==11:
   b.line([(17,12),(14,23),(19,25)],GOLD,2);b.poly([(12,33),(16,25),(31,25),(35,33)],col);b.ellipse((33,17,39,23),GOLD)
  if rank==12:b.poly([(18,20),(24,22),(29,19),(27,26),(23,29),(19,25)],INK)
  suit(b,7,17,7,s,GOLD);b.line([(36,12),(36,30)],GOLD,2)
  im=b.im.rotate(180) if flip else b.im
  a.im.alpha_composite(im,(25*S,(66 if flip else 31)*S))
 a.line([(27,66),(69,66)],GOLD)
def cards():
 atlas=Image.new('RGBA',(960,924))
 for card in range(53):
  a=Art(96,132);a.rect((1,1,94,130),PAPER,'#a79d85',r=5);a.rect((4,4,91,127),None,'#faf6e9',r=4)
  if card==52:
   a.rect((6,6,89,125),'#22383b',GOLD,r=3);a.rect((11,11,84,120),None,GOLD,r=2)
   for x in range(14,85,8):
    for y in range(17,117,8):a.poly([(x,y-2),(x+2,y),(x,y+2),(x-2,y)],'#52615a')
   a.ellipse((24,41,72,89),'#233334',GOLD);a.text((48,62),'R',30,GOLD,True);a.text((48,108),'CASINO',7,GOLD)
  else:
   r=card%13;s=card//13;c=RED if s in (1,2) else INK
   label=['A','2','3','4','5','6','7','8','9','10','J','Q','K'][r]
   a.text((15,19),label,24,c,True);suit(a,15,41,11,s,c)
   # Rotated matching corner is baked into the same atlas cell.
   corner=Art(30,45);corner.text((15,15),label,24,c,True);suit(corner,15,37,11,s,c)
   a.im.alpha_composite(corner.im.rotate(180),(65*S,85*S))
   if r>=10:court(a,r,s,c)
   elif r==0:
    suit(a,48,66,43,s,c);a.text((48,104),'RIPPERS',6,GOLD)
   else:
    n=r+1
    patterns={2:[(48,37),(48,94)],3:[(48,37),(48,66),(48,94)],4:[(34,38),(62,38),(34,94),(62,94)],5:[(34,38),(62,38),(48,66),(34,94),(62,94)],6:[(x,y) for y in (38,66,94) for x in (34,62)],7:[(x,y) for y in (38,66,94) for x in (34,62)]+[(48,52)],8:[(x,y) for y in (38,66,94) for x in (34,62)]+[(48,52),(48,80)],9:[(x,y) for y in (35,55,77,97) for x in (34,62)]+[(48,66)],10:[(x,y) for y in (35,55,77,97) for x in (34,62)]+[(48,45),(48,87)]}
    for x,y in patterns[n]:suit(a,x,y,15,s,c)
  atlas.alpha_composite(a.finish(),((card%10)*96,(card//10)*154))
 atlas.save(OUT/'cards.png',optimize=True)
def felt():
 a=Art(1112,468)
 a.ellipse((4,10,1108,466),'#070b0c')
 a.ellipse((3,2,1109,457),'#463e31','#78664a',2)
 a.ellipse((11,10,1101,449),'#252d2c','#b19a6b',1)
 a.ellipse((23,22,1089,437),'#142a28','#0c1515',5)
 a.ellipse((32,31,1080,428),None,'#3b5146',1)
 # Fine deliberate woven texture, low contrast at actual table scale.
 mask=Image.new('L',a.im.size);md=ImageDraw.Draw(mask);md.ellipse((26*S,25*S,1086*S,434*S),fill=255)
 weave=Image.new('RGBA',a.im.size);wd=ImageDraw.Draw(weave)
 for y in range(26,434,3):wd.line((26*S,y*S,1086*S,y*S),fill=(117,133,111,9),width=1)
 for x in range(26,1086,4):wd.line((x*S,25*S,x*S,434*S),fill=(8,15,13,28),width=1)
 weave.putalpha(Image.composite(weave.getchannel('A'),Image.new('L',a.im.size),mask));a.im.alpha_composite(weave)
 for x in (70,190,922,1042):a.ellipse((x,219,x+6,225),'#8e805c')
 a.text((556,233),'R',66,'#2b4138',True);a.text((556,280),'R I P P E R S   C A S I N O',12,'#435546')
 a.save('table-felt.png')
def portraits():
 atlas=Image.new('RGBA',(1536,256))
 for i in range(6):
  a=Art(256,256,'#111b1d')
  a.poly([(0,0),(165,0),(76,256),(0,256)],'#1c2828')
  for y in range(12,250,7):a.line([(8,y),(248,y)],'#202b2a')
  a.rect((7,7,248,248),None,'#485048',1,4)
  coat=['#4c5748','#535345','#51483a','#303d43','#464e43','#645443'][i]
  light=['#78816a','#827e62','#8e7754','#607379','#727e69','#958064'][i]
  # Different shoulder silhouettes, collars and plate carriers, kept readable at seat size.
  a.poly([(12,256),(25,209),(69,177),(92,169),(166,169),(193,182),(231,208),(247,256)],'#0a1215')
  a.poly([(22,256),(34,214),(76,185),(102,173),(159,175),(186,190),(219,214),(237,256)],coat)
  a.poly([(72,189),(89,178),(109,220),(158,220),(175,182),(192,192),(175,256),(85,256)],'#26332f')
  a.line([(35,215),(62,196),(71,201)],light,3);a.line([(215,214),(194,198)],light,2)
  for x in (70,176):
   a.poly([(x,193),(x+11,194),(x+8,251),(x-4,251)],'#82836b' if i!=3 else '#667878')
   a.rect((x-2,213,x+11,223),'#1b2827','#93917a',1,2)
  for x in (91,130):
   a.rect((x,226,x+31,253),'#3c463a','#6f7560',1,2);a.line([(x+4,232),(x+26,232)],'#202d29',2)
  a.poly([(85,156),(88,186),(127,208),(165,186),(168,156)],'#151f20')
  # Fully covered faces avoid tiny, uncanny facial features.
  a.ellipse((78,51,177,186),'#343e39','#090f12',3)
  a.poly([(88,109),(164,108),(170,152),(149,181),(117,185),(86,160)],'#252e2b')
  if i==0: # Olive ballistic helmet, visor goggles, fitted balaclava.
   a.ellipse((62,26,191,124),'#131d1f','#090f12',2)
   a.ellipse((67,29,187,113),coat)
   a.poly([(72,94),(74,59),(95,37),(144,30),(173,47),(184,91)],'#65705b')
   a.line([(84,55),(105,39),(144,37)],'#a0a38a',3)
   a.rect((64,88,190,104),'#303e35','#77836b',2,4)
   a.rect((76,101,177,130),'#141f22','#939781',2,7)
   for x in (83,132):a.poly([(x,106),(x+36,106),(x+30,122),(x+3,122)],'#4d6565');a.line([(x+4,109),(x+26,109)],'#a5b3a7',2)
   a.line([(93,141),(120,147),(151,139)],'#5c6a59',3)
   a.line([(95,155),(119,168),(150,155)],'#3e4e42',2)
  elif i==1: # Heavy hood and ribbed face wrap.
   a.poly([(58,172),(48,105),(62,50),(89,25),(140,21),(178,40),(198,97),(186,177),(165,158),(163,74),(143,52),(104,55),(85,80),(82,164)],coat)
   a.line([(62,119),(70,66),(95,39),(139,34),(172,56),(185,102)],light,3)
   a.poly([(85,115),(106,105),(149,108),(172,121),(167,163),(145,187),(114,186),(89,162)],'#454f43')
   for y in range(122,176,6):a.line([(93,y),(123,y+5),(161,y-2)],'#727663',2)
   a.rect((94,91,158,105),'#111b1d',r=4);a.line([(104,98),(115,97)],'#b1ac90',2);a.line([(139,97),(150,98)],'#aba98b',2)
   a.line([(81,159),(81,198)],'#a09778',2)
  elif i==2: # Knit cap, asymmetric goggles, dust respirator.
   a.ellipse((74,34,180,110),'#49483c','#0b1416',2)
   for x in range(84,176,9):a.line([(x,48),(x-4,94)],'#777460',2)
   a.rect((72,82,180,103),'#373b32','#777260',2,4)
   a.line([(78,112),(174,108)],'#807559',6)
   for x in (86,137):a.ellipse((x,101,x+34,128),'#101e23','#a59b7b',2);a.line([(x+8,106),(x+23,108)],'#4d696d',3)
   a.poly([(105,129),(151,128),(164,151),(147,175),(115,175),(97,151)],'#6f7462')
   for x in (110,121,132,143):a.line([(x,142),(x+3,163)],'#252f2d',4)
   a.line([(86,143),(102,147)],'#a1a088',3);a.line([(159,146),(175,141)],'#a1a088',3)
  elif i==3: # Black PMC helmet, headset and smoked ballistic glasses.
   a.ellipse((66,25,188,116),'#202e34','#0b1417',3)
   a.poly([(77,77),(81,52),(114,33),(158,39),(178,62),(181,84)],'#3d4f55')
   a.line([(92,49),(120,38),(151,43)],'#788b88',3)
   a.rect((60,84,81,142),'#2d3a3b','#77827a',2,4);a.rect((173,84,193,142),'#253135','#667b7b',2,4)
   a.line([(183,135),(180,160),(153,164)],'#171f20',5);a.ellipse((142,157,158,166),'#7c8270')
   a.poly([(81,101),(127,98),(173,103),(161,124),(138,124),(127,114),(114,124),(91,122)],'#0b191e')
   a.line([(86,104),(115,103)],'#77908c',3);a.line([(139,103),(164,106)],'#77908c',3)
   a.line([(100,144),(125,151),(154,142)],'#5c6e69',2);a.line([(114,177),(121,185),(142,177)],'#6b7a6e',2)
  elif i==4: # Rubber gas mask and pronounced round filters.
   a.poly([(66,94),(69,49),(94,30),(151,28),(183,56),(187,104),(170,119),(166,78),(146,61),(110,65),(87,91)],'#57614f')
   a.ellipse((78,70,178,181),'#252f2b','#75806a',2)
   for x in (86,139):
    a.ellipse((x,88,x+34,125),'#080f13','#8d947d',3);a.ellipse((x+5,94,x+28,119),'#33494a');a.line([(x+9,97),(x+23,97)],'#7d9793',2)
   a.poly([(122,122),(135,122),(147,149),(131,164),(113,148)],'#444f43')
   a.ellipse((93,150,136,190),'#15201c','#7e8970',3);a.ellipse((101,157,130,185),'#475244')
   for x in range(105,129,5):a.line([(x,164),(x,178)],'#14231f',2)
   a.ellipse((156,139,190,175),'#28382f','#91947a',3);a.line([(165,151),(180,151)],'#101e1b',3)
  else: # Scav field cap, bandaged eye and red-brown scarf.
   a.poly([(72,83),(78,48),(100,37),(152,40),(176,58),(181,86)],'#655d47')
   a.poly([(68,82),(164,80),(189,94),(164,100),(71,94)],'#3f483c');a.line([(82,54),(105,44),(150,47)],'#9a8a68',3)
   a.poly([(81,111),(119,92),(174,112),(170,126),(117,108),(86,124)],'#b0a38a')
   a.line([(86,112),(118,101),(170,119)],'#716f5d',2)
   a.line([(137,108),(153,108)],'#101b1d',3)
   a.poly([(82,130),(116,138),(170,126),(171,160),(146,183),(121,198),(92,176)],'#665041')
   a.line([(91,141),(118,151),(161,137)],'#a08667',3);a.line([(93,158),(121,168),(158,153)],'#8d775d',3)
   a.poly([(112,179),(126,183),(119,228),(98,216)],'#80644c');a.line([(106,204),(118,208)],'#ad9370',2)
  a.line([(27,234),(47,234)],light,3)
  atlas.alpha_composite(a.finish(),(256*i,0))
 atlas.save(OUT/'portraits.png',optimize=True)

def slots():
 for symbol in range(6):
  a=Art(320,320)
  if symbol==0:
   a.poly([(50,60),(270,60),(262,111),(188,184),(159,270),(84,270),(125,167),(203,113),(48,113)],'#30261d')
   a.poly([(55,57),(270,57),(262,103),(182,181),(152,261),(91,261),(132,165),(215,105),(52,105)],GOLD)
   a.poly([(62,64),(261,64),(254,98),(174,176),(146,252),(103,252),(141,166),(229,98),(59,98)],RED)
   a.line([(63,67),(257,67),(252,88)],'#cf8b72',3);a.line([(141,243),(171,176),(249,100)],'#682b2a',4)
  elif symbol==1:
   pts=[(39,109),(95,55),(222,55),(282,109),(160,269)]
   a.poly([(x+2,y+5) for x,y in pts],'#182d34');a.poly(pts,'#718d95')
   for poly,c in [([(39,109),(95,55),(116,109)],'#aabfc0'), ([(95,55),(160,55),(116,109)],'#d6e1d8'), ([(160,55),(222,55),(203,109)],'#819fa5'), ([(222,55),(282,109),(203,109)],'#d8e0d4'), ([(39,109),(116,109),(160,269)],'#375d6d'), ([(116,109),(203,109),(160,269)],'#7ca2a9'), ([(203,109),(282,109),(160,269)],'#294955')]:a.poly(poly,c)
   a.line(pts+[pts[0]],'#d1d8ca',3);a.line([(44,110),(279,110)],'#d1d8ca',2)
  elif symbol==2:
   a.rect((26,88,294,235),'#241e19','#6d5a3c',r=14);a.rect((29,80,291,224),GOLD,'#e0cb92',2,12);a.rect((38,90,282,214),'#1c2526','#65583e',2,7)
   a.line([(47,100),(273,100)],'#d1ba81',2);a.line([(47,203),(273,203)],'#7e6c48',2);a.text((160,153),'BAR',66,PAPER,True)
  elif symbol==3:
   a.ellipse((140,34,180,70),'#77603b','#d4bc7f',3)
   a.poly([(142,64),(178,64),(204,82),(219,126),(224,190),(251,222),(248,238),(71,238),(68,222),(96,190),(101,124),(116,82)],'#6f542f')
   a.poly([(144,68),(175,68),(197,88),(211,135),(215,191),(240,223),(81,223),(105,191),(110,130),(124,85)],'#b59a5b')
   a.poly([(126,90),(144,79),(139,180),(121,207),(112,207),(119,177)],'#ded0a0')
   a.line([(177,80),(198,128),(202,190),(219,215)],'#80653b',7)
   a.ellipse((72,218,249,246),'#b79b61','#e2cd91',2);a.ellipse((89,224,232,240),'#3d3526');a.ellipse((143,235,178,266),'#c2a360','#ead499',2)
  elif symbol==4:
   a.line([(106,174),(143,116),(187,53)],'#756947',9);a.line([(197,179),(185,123),(187,53)],'#756947',9)
   a.poly([(180,74),(198,48),(248,41),(231,71),(205,83)],'#556b4c');a.line([(190,75),(239,49)],'#99a075',3)
   for x,y in [(100,205),(206,213)]:
    a.ellipse((x-59,y-63,x+59,y+59),'#3b2020','#a37c55',2);a.ellipse((x-54,y-60,x+54,y+51),'#893837');a.ellipse((x-43,y-52,x+31,y+25),'#a94d43');a.ellipse((x-33,y-44,x-10,y-31),'#dbba91');a.line([(x+45,y-20),(x+43,y+25),(x+19,y+45)],'#612b2b',6)
  else:
   a.poly([(218,91),(230,50),(270,46),(283,65),(257,89)],'#53664a');a.line([(231,78),(270,55)],'#a6ae7b',3)
   def oval(rx,ry,c):
    pts=[]
    for k in range(100):
     t=k*math.tau/100;x=rx*math.cos(t);y=ry*math.sin(t)
     pts.append((159+x*.83+y*.56,175-x*.56+y*.83))
    a.poly(pts,c)
   oval(111,78,'#5a4b2d');oval(107,75,GOLD);oval(101,69,'#c3a353')
   oval(94,63,'#d0b969')
   a.line([(75,173),(84,143),(110,117),(141,101),(174,99),(198,108)],'#e9dca5',8)
   a.line([(116,242),(148,246),(182,236),(219,209),(241,177)],'#9c8444',7)
   for x,y in [(102,162),(131,143),(162,136),(194,159),(204,189),(159,205),(116,201),(95,182),(179,175)]:a.line([(x,y),(x+2,y+2)],'#e0d29a',2)
   a.line([(216,108),(229,84)],'#756947',6)
  a.save('symbol-'+str(symbol)+'.png')
 a=Art(765,373,'#131c1e');a.rect((1,1,763,371),'#373c39','#8a7957',2,5);a.rect((8,8,756,365),'#222c2c','#0b1112',2,3)
 for x in (23,265,507):
  a.rect((x-5,22,x+239,351),'#0b1011','#817255',2,4);a.rect((x-2,25,x+236,347),'#948770','#c0b18b',1,2)
 for x in (13,751):
  for y in (13,359):a.ellipse((x-3,y-3,x+3,y+3),'#827c64','#0b1112');a.line([(x-2,y),(x+2,y)],'#c4b58b')
 for x in (75,317,559):a.line([(x,12),(x+130,12)],'#81755a',2)
 a.save('slot-cabinet.png')

def backroom():
 a=Art(1112,468);a.rect((3,4,1109,464),'#101616','#4f4e43',2,12);a.rect((11,12,1101,456),'#272c29','#746d55',1,8);a.rect((20,21,1092,447),'#202722','#111818',3,6)
 for y in range(25,445,3):
  light=int(8*max(0,1-abs(y-210)/230));a.line([(24,y),(1088,y)],(82+light,79+light,62+light,18),1)
 for x in (32,1080):
  for y in (33,435):a.ellipse((x-3,y-3,x+3,y+3),'#827c63','#111919');a.line([(x-2,y),(x+2,y)],'#b1a98a')
 a.text((556,410),'L A S T   S E A T',13,'#59604f');a.save('backroom-table.png')
if __name__=='__main__':
 OUT.mkdir(exist_ok=True);cards();felt();portraits();slots();backroom();print('Authored casino asset set.')
