"""照片儲存／分享用的「BlockPet」浮水印（設計：原始照片右下角）。
白字＋柔和深色陰影、透明底，輸出 PNG bytes 到 Assets/Resources/UI/watermark_blockpet.bytes（Unity 用 Texture2D.LoadImage 讀，永遠可讀取，不用設匯入設定）。
用法：python make_watermark.py（需要 Pillow）"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
FONT = os.path.join(ROOT, 'Assets', 'Art', 'Fonts', 'NotoSansCJKtc-Black.otf')
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'UI', 'watermark_blockpet.bytes')

TEXT = 'BlockPet'
SIZE = 120          # 字級（px）
PAD = 24            # 四周留給陰影

font = ImageFont.truetype(FONT, SIZE)
l, t, r, b = font.getbbox(TEXT)
w, h = r - l + PAD * 2, b - t + PAD * 2

shadow = Image.new('L', (w, h), 0)
ImageDraw.Draw(shadow).text((PAD - l, PAD - t + 3), TEXT, font=font, fill=150)
shadow = shadow.filter(ImageFilter.GaussianBlur(6))

img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
img.putalpha(shadow)
img = Image.composite(Image.new('RGBA', (w, h), (30, 24, 18, 255)), Image.new('RGBA', (w, h), (0, 0, 0, 0)), shadow)
ImageDraw.Draw(img).text((PAD - l, PAD - t), TEXT, font=font, fill=(255, 255, 255, 235))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT, format='PNG')
print(OUT, img.size)
