"""BlockPet 圖鑑（DexHome／DexDetail／DexLocked／DexNew）素材：SVG 原稿＋@3x PNG。形狀與顏色取自設計稿。白色素材在 Unity 上色；陰影另加。"""
import json, os, io, math
import cairosvg
from PIL import Image, ImageDraw
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Icons'); os.makedirs(OUT, exist_ok=True)
M = []
def emit(name, w, h, body, vb=None, use='', s9=None):
    vb = vb or f'0 0 {w} {h}'
    svg = f"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='{vb}'>{body}</svg>"
    open(os.path.join(OUT, name + '.svg'), 'w').write(svg)
    im = Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=round(w*3), output_height=round(h*3)))).convert('RGBA')
    im.save(os.path.join(OUT, name + '@3x.png'))
    e = {'file': name, 'size_1x': [w, h], 'png_px': list(im.size), 'use': use}
    if s9: e['9slice_px_at3x_LTRB'] = s9
    M.append(e)
def png_only(name, im, w, h, use):
    im.save(os.path.join(OUT, name + '@3x.png')); M.append({'file': name, 'size_1x': [w, h], 'png_px': list(im.size), 'use': use + '（只有 PNG，漸層 SVG 不好轉）'})
def rr(x, y, w, h, r, fill, extra=''):
    return f"<rect x='{x}' y='{y}' width='{w}' height='{h}' rx='{r}' fill='{fill}' {extra}/>"
ln = "fill='none' stroke='#3A2E22' stroke-linecap='round' stroke-linejoin='round'"
# 頂部與分頁
emit('dex_btn_back', 44, 44, "<circle cx='22' cy='22' r='22' fill='#F2E9D8'/><path d='M25 15l-7 7 7 7' fill='none' stroke='#3A2E22' stroke-width='2.8' stroke-linecap='round' stroke-linejoin='round'/>", use='左上返回（含底）')
emit('dex_header_bg', 12, 12, rr(0, 0, 12, 12, 0, '#FFF8EC') + rr(0, 11, 12, 1, 0, '#EFE6D4'), use='頂部底（下緣線 1；拉伸到 390 × 112）', s9=[6, 6, 6, 6])
emit('dex_tab', 40, 28, "<rect x='0.75' y='0.75' width='38.5' height='26.5' rx='13.25' fill='#FFFCF4' stroke='#E2D6C0' stroke-width='1.5'/>", use='系列分頁（沒選）', s9=[42, 42, 42, 42])
emit('dex_tab_on', 40, 28, rr(0, 0, 40, 28, 14, '#3A2E22'), use='系列分頁（選中）', s9=[42, 42, 42, 42])
# 系列卡
emit('dex_series_card', 60, 60, rr(0, 0, 60, 60, 22, '#FFFCF4'), use='系列卡底（陰影 0 4 12 另加）', s9=[66, 66, 66, 66])
emit('dex_series_icon_bg', 40, 40, rr(0, 0, 40, 40, 12, '#FFFFFF'), use='品種圖示方塊（白，上色：BowBow #FCE7A6、貓咪 #F6E2F4、兔子 #E3EED9、萬聖節 #FFE3C7）')
emit('dex_egg', 24, 27, "<path d='M20 3C11 3 5 17 5 27c0 8.5 6.7 14 15 14s15-5.5 15-14C35 17 29 3 20 3Z' fill='#FFFCF4' stroke='#3A2E22' stroke-width='2.4'/>", '0 0 40 44', '品種蛋（底）')
emit('dex_egg_zigzag', 24, 27, "<path d='M9 25l5-4 4 4 4-4 4 4 5-4' fill='none' stroke='#FFFFFF' stroke-width='2.6' stroke-linecap='round' stroke-linejoin='round'/>", '0 0 40 44', '蛋上的鋸齒線（白，上色品種色，疊在 dex_egg 上）')
emit('dex_progress_track', 12, 6, rr(0, 0, 12, 6, 3, '#EFE6D4'), use='系列進度條軌道', s9=[9, 9, 9, 9])
emit('dex_progress_fill', 12, 6, rr(0, 0, 12, 6, 3, '#F6C945'), use='系列進度條填滿（寬＝收到格數 / 7）', s9=[9, 9, 9, 9])
emit('dex_tag_season', 40, 16, rr(0, 0, 40, 16, 8, '#3A2E22'), use='「季節限定」標籤底', s9=[24, 24, 24, 24])
# 格子
emit('dex_cell', 40, 40, rr(0, 0, 40, 40, 16, '#F7F0E2'), use='格子底（收到／一般沒收到；高 104）', s9=[48, 48, 48, 48])
emit('dex_cell_rare_empty', 40, 40, "<rect x='1' y='1' width='38' height='38' rx='15' fill='#F7F0E2' stroke='#E8D9A8' stroke-width='2' stroke-dasharray='5 4'/>", use='稀有格（還沒收到）：金色虛線框', s9=[48, 48, 48, 48])
emit('dex_cell_extreme', 40, 40, "<rect x='1' y='1' width='38' height='38' rx='15' fill='#FFF3C9' stroke='#F6C945' stroke-width='2'/>", use='收到極致型：金框', s9=[48, 48, 48, 48])
# 彩虹格：PNG 漸層
W3, H3 = 78*3, 104*3
im = Image.new('RGBA', (W3, H3), (0, 0, 0, 0)); base = Image.new('RGBA', (W3, H3), (255, 243, 201, 255))
cols = [(242,154,58),(111,164,95),(111,143,224),(215,138,208),(232,195,58),(242,154,58)]
px = base.load(); cx, cy = W3/2, H3/2
for y in range(H3):
    for x in range(W3):
        a = (math.degrees(math.atan2(y-cy, x-cx)) + 450) % 360 / 360 * 5
        i = int(a); t = a - i; c0, c1 = cols[i], cols[i+1]
        c = tuple(int(c0[k]*(1-t)+c1[k]*t) for k in range(3))
        b = px[x, y]; px[x, y] = tuple(int(b[k]*0.8 + c[k]*0.2) for k in range(3)) + (255,)
mask = Image.new('L', (W3, H3), 0); ImageDraw.Draw(mask).rounded_rectangle([0, 0, W3-1, H3-1], radius=48, fill=255)
im.paste(base, (0, 0), mask); png_only('dex_cell_rainbow', im, 78, 104, '收到彩虹型：淡五色漸層底（一格 78 × 104）')
emit('dex_badge_count', 24, 16, rr(0, 0, 24, 16, 8, '#3A2E22'), use='「×2」次數標籤底（字 TMP 白 10 Black）', s9=[24, 24, 24, 24])
emit('dex_shop_cell', 40, 40, "<rect x='1' y='1' width='38' height='38' rx='15' fill='none' stroke='#E2D6C0' stroke-width='2' stroke-dasharray='5 4'/>", use='「去商店抽蛋」格（虛線）', s9=[48, 48, 48, 48])
emit('icon_shop_bag', 20, 20, "<g fill='none' stroke='#8A6A4E' stroke-width='2.4' stroke-linecap='round' stroke-linejoin='round'><path d='M4 9h16l-1.5 10h-13Z'/><path d='M9 9V6a3 3 0 0 1 6 0v3'/></g>", '0 0 24 24', '購物袋圖示')
# 詳細面板
emit('dex_sheet_panel', 60, 60, "<path d='M0 26 A26 26 0 0 1 26 0 H34 A26 26 0 0 1 60 26 V60 H0Z' fill='#FFF8EC'/>", use='詳細面板底（上方圓角 26）', s9=[84, 84, 84, 0])
emit('dex_sheet_grabber', 40, 5, rr(0, 0, 40, 5, 2.5, '#E2D6C0'), use='拖曳把手')
emit('dex_btn_close', 44, 44, "<circle cx='22' cy='22' r='22' fill='#F2E9D8'/><path d='M17 17l10 10M27 17l-10 10' stroke='#3A2E22' stroke-width='2.4' stroke-linecap='round'/>", use='右上關閉（含底）')
emit('dex_photo_mask', 40, 40, rr(0, 0, 40, 40, 20, '#FFFFFF'), use='大圖圓角 20 遮罩（354 × 200）', s9=[60, 60, 60, 60])
emit('dex_gen_pill', 40, 20, rr(0, 0, 40, 20, 10, '#FFF8EC', "fill-opacity='0.9'"), use='「第 2 代」膠囊', s9=[30, 30, 30, 30])
emit('dex_arrow_btn', 44, 44, "<circle cx='22' cy='22' r='22' fill='#000000' fill-opacity='0.25'/><path d='M25 15l-7 7 7 7' fill='none' stroke='#FFFFFF' stroke-width='3' stroke-linecap='round' stroke-linejoin='round'/>", use='上一代箭頭（下一代水平翻轉）')
emit('dex_info_box', 40, 40, rr(0, 0, 40, 40, 14, '#FFFCF4'), use='資訊框（進化日期、養了幾天、調色盤）', s9=[42, 42, 42, 42])
emit('dex_palette_mask', 40, 12, rr(0, 0, 40, 12, 6, '#FFFFFF'), use='調色盤五色條遮罩（色段用白色方塊上色）', s9=[18, 18, 18, 18])
emit('dex_dot', 8, 8, "<circle cx='4' cy='4' r='4' fill='#FFFFFF'/>", use='調色盤圖例圓點（8，白，上色）')
emit('dex_page_dot', 6, 6, "<circle cx='3' cy='3' r='3' fill='#3A2E22' fill-opacity='0.25'/>", use='第幾代位置點（其他）')
emit('dex_page_dot_on', 18, 6, rr(0, 0, 18, 6, 3, '#3A2E22'), use='第幾代位置點（目前）', s9=[9, 9, 9, 9])
# 還沒收到小卡、按鈕
emit('dex_popup_card', 60, 60, rr(0, 0, 60, 60, 24, '#FFF8EC'), use='還沒收到小卡底（陰影 0 16 40 另加）', s9=[72, 72, 72, 72])
emit('btn_primary', 40, 51, rr(0, 3, 40, 48, 15, '#D9A92C') + rr(0, 0, 40, 48, 15, '#F6C945'), use='「知道了」黃色按鈕（高 48；含下緣 3）', s9=[48, 48, 54, 57])
emit('btn_primary_lg', 40, 58, rr(0, 4, 40, 54, 18, '#D9A92C') + rr(0, 0, 40, 54, 18, '#F6C945'), use='「看圖鑑」黃色大按鈕（高 54；含下緣 4）', s9=[57, 57, 63, 66])
emit('dim', 4, 4, rr(0, 0, 4, 4, 0, '#14180E', "fill-opacity='0.5'"), use='遮罩（拉滿畫面；新增畫面用 0.7）')
# 收到新的一格
im = Image.new('RGBA', (900, 900), (0, 0, 0, 0)); dr = ImageDraw.Draw(im)
for k in range(12):
    a0 = k * 30
    dr.pieslice([0, 0, 899, 899], a0, a0 + 12, fill=(246, 201, 69, 115))
fade = Image.new('L', (900, 900), 0); fd = ImageDraw.Draw(fade)
for r in range(450, 0, -3):
    fd.ellipse([450-r, 450-r, 450+r, 450+r], fill=int(255 * (1 - (r/450)**2)))
im.putalpha(Image.composite(im.getchannel('A'), Image.new('L', (900, 900), 0), fade))
png_only('dex_new_rays', im, 300, 300, '寵物後面的金色光芒（12 秒轉一圈）')
emit('dex_new_tag', 40, 26, rr(0, 2, 40, 24, 12, '#B44A33') + rr(0, 0, 40, 24, 12, '#E86B4F'), use='「圖鑑新增！」紅色標籤（含下緣 2）', s9=[36, 36, 36, 42])
emit('dex_reward_pill', 40, 32, rr(0, 0, 40, 32, 16, '#FFF8EC', "fill-opacity='0.95'"), use='「全營地 +100 金幣」膠囊', s9=[48, 48, 48, 48])
emit('icon_coin', 18, 18, "<circle cx='9' cy='9' r='9' fill='#E0A21E'/><circle cx='9' cy='8' r='8' fill='#FFD54F'/>", use='金幣')
json.dump(M, open(os.path.join(HERE, '..', 'dex_assets.json'), 'w'), ensure_ascii=False, indent=1)
print(len(M))
