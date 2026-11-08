"""BlockPet 首頁 v9（設計文件 5.25）素材：SVG 原稿＋@3x PNG。字一律用 TMP。"""
import json, os, io, sys
import cairosvg
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from icon_paths import I
OUT = os.path.join(HERE, '..', 'Icons'); os.makedirs(OUT, exist_ok=True)
M = []
def emit(name, w, h, body, use='', s9=None):
    svg = f"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>{body}</svg>"
    open(os.path.join(OUT, name + '.svg'), 'w').write(svg)
    im = Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=round(w*3), output_height=round(h*3)))).convert('RGBA')
    im.save(os.path.join(OUT, name + '@3x.png'))
    e = {'file': name, 'size_1x': [w, h], 'png_px': list(im.size), 'use': use}
    if s9: e['9slice_px_at3x_LTRB'] = s9
    M.append(e)
def icon(key, size, color='#3A2E22', sw=2.2):
    k = size / 24
    return f"<g transform='scale({k})' fill='none' stroke='{color}' stroke-width='{sw}' stroke-linecap='round' stroke-linejoin='round'>{I[key]}</g>"
# 線條圖示（沒有底；Unity 另加白光可用 icon_*_glow 版）
for key, name, size, use in [('house','icon_home_line',28,'左上 Home（沒有底，點擊範圍 44）'),('chev','icon_menu_chevron',26,'右上選單箭頭（沒有底；展開轉 180°）'),
                             ('polar','icon_album_line',28,'底部相簿（放在 home_btn_round 上）'),('bubbles','icon_chat_line',28,'底部聊天（放在 home_btn_round 上）')]:
    emit(name, size, size, icon(key, size), use=use + '；#3A2E22 粗 2.2')
# Home、箭頭的淡白光在 Unity 用 Shadow／Outline 元件加（白 #FFFCF4，約 1.5～4 px 模糊），不另出圖
# 圓形毛玻璃底（Unity 另加 blur）
emit('home_btn_round', 50, 50, "<circle cx='25' cy='25' r='24.25' fill='#FFF8EC' fill-opacity='0.72' stroke='#FFFFFF' stroke-opacity='0.85' stroke-width='1.5'/>", use='相簿／聊天的圓底 50（陰影 0 3 8 另加）')
# 相機按鈕
emit('home_btn_camera', 72, 72, "<circle cx='36' cy='36' r='34' fill='#FFFCF4'/><circle cx='36' cy='36' r='30' fill='#E86B4F'/><path d='M6 36a30 30 0 0 0 60 0a30 30 0 0 1-60 3Z' fill='#000' fill-opacity='0.08'/><g transform='translate(20 20) scale(0.8)'><rect x='4' y='11' width='32' height='23' rx='6' fill='#FFFFFF'/><path d='M14 11l3-5h6l3 5' fill='#FFFFFF'/><circle cx='20' cy='22' r='6.5' fill='#E86B4F'/><circle cx='20' cy='22' r='3.6' fill='#FFFFFF'/></g>", use='底部相機 72（陰影 0 6 14 另加）')
emit('badge_red', 18, 18, "<circle cx='9' cy='9' r='9' fill='#FFFFFF'/><circle cx='9' cy='9' r='7' fill='#E86B4F'/>", use='未讀數字／新照片紅點（數字 TMP 10 Black 白）')
# 貼紙
def shape(x, y, S, rad, cut):
    """貼紙外形：圓角正方形，右下角切掉 cut（翹起的地方）。"""
    return (f"M{x+rad} {y}H{x+S-rad}A{rad} {rad} 0 0 1 {x+S} {y+rad}V{y+S-cut}L{x+S-cut} {y+S}"
            f"H{x+rad}A{rad} {rad} 0 0 1 {x} {y+S-rad}V{y+rad}A{rad} {rad} 0 0 1 {x+rad} {y}Z")
def sticker(S, b, rad, peel, gold=False):
    cut = S * 0.2 if not gold else S * 0.22
    path = shape(0, 0, S, rad, cut)
    pe = f"<defs><linearGradient id='p' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#FFFFFF'/><stop offset='0.55' stop-color='#EDE6D8'/><stop offset='1' stop-color='#D8CFBE'/></linearGradient></defs><path d='M{S-cut} {S}L{S} {S-cut}L{S-cut} {S-cut}Z' fill='url(#p)'/>"
    if gold:
        # 2026-11-06 設計文件 5.25 ④：細黃邊 2 #F6C945，畫在貼紙內側（黃色底＋往內縮 2 的白色貼紙）
        e = 2
        body = f"<path d='{path}' fill='#F6C945'/><path d='{shape(e, e, S - 2*e, rad - e, cut - e*0.586)}' fill='#FFFFFF'/>"
    else:
        body = f"<path d='{path}' fill='#FFFFFF'/>"
    return pe + body + pe.split('</defs>')[1]
emit('sticker_frame', 66, 66, sticker(66, 4, 10, 14), use='一般照片貼紙的白邊（照片 58×58 圓角 7 放在 (4,4)，用 sticker_photo_mask 裁）；右下角翹起；整張不透明度 85%')
emit('sticker_photo_mask', 58, 58, "<rect width='58' height='58' rx='7' fill='#FFFFFF'/>", use='一般貼紙照片遮罩')
emit('sticker_latest_frame', 88, 88, sticker(88, 5, 12, 19, gold=True), use='最新照片貼紙（細黃邊 2 #F6C945 在內側；照片 78×78 放在 (5,5)；不透明）')
emit('sticker_latest_mask', 78, 78, "<rect width='78' height='78' rx='8' fill='#FFFFFF'/>", use='最新貼紙照片遮罩')
emit('sticker_avatar_ring', 26, 26, "<circle cx='13' cy='13' r='11.75' fill='none' stroke='#FFFFFF' stroke-width='2.5'/>", use='貼紙左下頭像的白圈（一般用 22、最新用 26，頭像圓形裁切放裡面）')
emit('tag_latest', 36, 18, "<rect y='2' width='36' height='16' rx='8' fill='#D9A92C'/><rect width='36' height='16' rx='8' fill='#F6C945'/>", use='「最新」標籤（字 TMP 10 Black #3A2E22）', s9=[24, 24, 24, 30])
emit('tag_more', 36, 22, "<rect x='1' y='1' width='34' height='20' rx='10' fill='#3A2E22' stroke='#FFF8EC' stroke-width='2'/>", use='「+N」更多貼紙（字 TMP 12 Black 白）', s9=[33, 33, 33, 33])
emit('pet_bubble', 40, 40, "<path d='M16 0H24A16 16 0 0 1 40 16V24A16 16 0 0 1 24 40H4A4 4 0 0 1 0 36V16A16 16 0 0 1 16 0Z' fill='#FFFFFF'/>", use='寵物泡泡（左下角尖；「好餓…」「飽飽的～」「乾淨了！」等；陰影 0 4 10 另加）', s9=[48, 48, 48, 48])
json.dump(M, open(os.path.join(HERE, '..', 'home9_assets.json'), 'w'), ensure_ascii=False, indent=1)
print(len(M))
