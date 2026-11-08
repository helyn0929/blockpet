"""BlockPet 寄明信片（設計文件 5.21，11-08）素材：SVG 原稿＋@3x PNG。字一律用 TMP。"""
import json, os, io
import cairosvg
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Icons'); os.makedirs(OUT, exist_ok=True)
M = []
BR = '#3A2E22'
def emit(name, w, h, body, use='', s9=None):
    svg = f"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>{body}</svg>"
    open(os.path.join(OUT, name + '.svg'), 'w').write(svg)
    im = Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=round(w*3), output_height=round(h*3)))).convert('RGBA')
    im.save(os.path.join(OUT, name + '@3x.png'))
    e = {'file': name, 'size_1x': [w, h], 'png_px': list(im.size), 'use': use}
    if s9: e['9slice_px_at3x_LTRB'] = s9
    M.append(e)
def line(paths, size, color=BR, sw=2.2):
    k = size / 24
    return f"<g transform='scale({k})' fill='none' stroke='{color}' stroke-width='{sw}' stroke-linecap='round' stroke-linejoin='round'>{paths}</g>"
emit('pc_icon_draw', 16, 16, line("<rect x='4' y='6' width='16' height='13' rx='2.5'/><path d='M4 10h16'/><path d='M9 6V4M15 6V4'/>", 16), '「抽好友」按鈕圖示')
emit('pc_btn_draw', 34, 34, "<rect width='34' height='34' rx='17' fill='#F6C945'/>", '「抽好友」按鈕底（下緣 0 3 0 #D9A92C 另加）', s9=[51,51,51,51])
emit('pc_chip_free', 20, 16, "<rect width='20' height='16' rx='8' fill='#FFFFFF' fill-opacity='0.6'/>", '按鈕裡「今天免費」／金幣價格小膠囊（字 10 Black #6B4A12）', s9=[24,24,24,24])
emit('pc_dot_red', 16, 16, "<circle cx='8' cy='8' r='8' fill='#FFFFFF'/><circle cx='8' cy='8' r='6' fill='#E86B4F'/>", '有營地想加你們時的紅點（字 9 Black 白）')
emit('pc_card', 60, 60, "<rect width='60' height='60' rx='26' fill='#FFF8EC'/>", '配對營地卡底（310×456，9-slice 78；陰影 0 16 40 rgba(20,30,20,0.35) 另加）', s9=[78,78,78,78])
emit('pc_btn_yes', 50, 50, "<rect width='50' height='50' rx='15' fill='#F6C945'/>", '「加好友」「寄出」「收進護照」「回寄一張」黃鈕（下緣 #D9A92C）', s9=[45,45,45,45])
emit('pc_btn_no', 50, 50, "<rect x='0.75' y='0.75' width='48.5' height='48.5' rx='14.25' fill='#FFFFFF' stroke='#E2D6C0' stroke-width='1.5'/>", '「略過」「晚點再回」「再編輯」白鈕', s9=[45,45,45,45])
emit('pc_btn_wait', 50, 50, "<rect width='50' height='50' rx='15' fill='#F2E9D8'/>", '「等對方確認…」灰底', s9=[45,45,45,45])
emit('pc_spinner', 16, 16, "<circle cx='8' cy='8' r='6.75' fill='none' stroke='#C9B99E' stroke-width='2.5'/><path d='M8 1.25A6.75 6.75 0 0 1 14.75 8' fill='none' stroke='#6B5D4E' stroke-width='2.5' stroke-linecap='round'/>", '等待轉圈（0.9 秒一圈）')
emit('pc_tag_country', 20, 22, "<rect width='20' height='22' rx='11' fill='#FFF8EC' fill-opacity='0.92'/>", '卡片左上國家小標（字 12 Black）', s9=[30,30,30,30])
emit('pc_tag_same', 20, 22, "<rect width='20' height='22' rx='11' fill='#F6C945'/>", '「你們都愛拍美食！」黃標', s9=[30,30,30,30])
emit('pc_icon_noface', 22, 22, line("<circle cx='12' cy='9' r='4'/><path d='M5 20c1.5-4 12.5-4 14 0'/><path d='M4 4l16 16'/>", 22, '#6B5D4E', 2), '有人臉不能寄（照片上變灰時放中間，下面字「有人臉」11 Black）')
emit('pc_noface_bar', 40, 34, "<rect x='0.75' y='0.75' width='38.5' height='32.5' rx='11.25' fill='#FFF1C9' stroke='#F6C945' stroke-width='1.5'/>", '選照片上方「有人臉的照片不能寄」提示條', s9=[36,36,36,36])
emit('pc_badge_deco', 20, 24, "<g transform='rotate(6 10 12)'><rect x='3' y='2' width='14' height='19' fill='#FFFFFF'/><rect x='5' y='4' width='10' height='9' fill='#F6C945'/></g>", '相簿縮圖右下「已做成拍立得」記號（陰影另加）')
emit('pc_check', 24, 24, "<circle cx='12' cy='12' r='12' fill='#FFFFFF'/><circle cx='12' cy='12' r='10' fill='#F6C945'/><path d='m7 12.3 3.2 3.2L17 8.7' fill='none' stroke='#3A2E22' stroke-width='2.8' stroke-linecap='round' stroke-linejoin='round'/>", '選到的照片右上勾勾')
emit('pc_sheet', 60, 60, "<path d='M0 26A26 26 0 0 1 26 0H34A26 26 0 0 1 60 26V60H0Z' fill='#FFF8EC'/>", '選照片的下方面板（高 640，9-slice 上 78 左右 78）')
emit('pc_handle', 40, 5, "<rect width='40' height='5' rx='2.5' fill='#E2D6C0'/>", '面板上方拖拉條')
emit('pc_stamp', 58, 58, "<path d='M4 4h50v50H4Z' fill='#F7B6C2' stroke='#FFFFFF' stroke-width='4' stroke-dasharray='3 3'/><rect x='10' y='10' width='38' height='38' fill='#FFF1C9'/><circle cx='29' cy='31' r='11' fill='#F29A3A'/><path d='M18 21c3-6 19-6 22 0' fill='none' stroke='#6FA45F' stroke-width='3'/>", '明信片右上的郵票（每個好友營地換中間圖案／顏色）')
emit('pc_postmark', 92, 92, "<circle cx='46' cy='46' r='43' fill='none' stroke='#C93C32' stroke-opacity='0.85' stroke-width='3'/><path d='M11 46h70' stroke='#C93C32' stroke-opacity='0.85' stroke-width='2'/>", '郵戳圓框（上：營地名 11 Black、下：日期 10 Black，顏色 rgba(201,60,50,0.9)；轉 −14°）')
emit('pc_postmark_s', 34, 34, "<circle cx='17' cy='17' r='16' fill='#FFFFFF' fill-opacity='0.4' stroke='#C93C32' stroke-opacity='0.8' stroke-width='2'/>", '護照小拍立得上的小郵戳（日期 7 Black）')
emit('pc_note', 30, 30, "<rect width='30' height='30' rx='14' fill='#FFF8EC' fill-opacity='0.92'/>", '說明小膠囊（「收到的明信片放在護照…」「24 小時內沒回…」）', s9=[42,42,42,42])
emit('pc_passport_bg', 4, 4, "<rect width='4' height='4' fill='#2E4686'/>", '護照深藍底（拉滿）')
emit('pc_passport_page', 60, 60, "<rect width='60' height='60' rx='18' fill='#F2E6CB'/>", '護照內頁（9-slice 54）', s9=[54,54,54,54])
emit('pc_mini_polaroid', 92, 112, "<rect width='92' height='112' fill='#FFFFFF'/><rect x='6' y='7' width='80' height='80' fill='#000000' fill-opacity='0.12'/>", '護照裡的小拍立得（照片區 (6,7) 80×80；字 10 霞鶩文楷；陰影 0 3 8 另加）')
emit('pc_empty_slot', 92, 112, "<rect x='1' y='1' width='90' height='110' rx='6' fill='none' stroke='#B9AE9C' stroke-width='2' stroke-dasharray='6 4'/>", '「還沒交換過」虛線空格（中間「？」20＋字 11 Black #8A7B68）')
json.dump(M, open(os.path.join(HERE, '..', 'postcard_assets.json'), 'w'), ensure_ascii=False, indent=1)
spec = {
 'doc': '設計文件 5.21 寄明信片（2026-11-08）；設計稿頁面「寄明信片」，元件 Postcard（view=draw/waiting/incoming/pick/reply/back/passport）',
 'rules': {'draw_free_per_day': 1, 'draw_extra': '金幣（價格待定，設計稿 50）', 'friend_limit': 10, 'match': '依興趣（最常拍的類別一樣優先）、國家隨機',
   'visible_on_match': ['營地名稱', '寵物圖', '品種', '成員數', '最常拍的類別', '國家'], 'both_confirm': True, 'confirm_expire_h': '24（暫定）',
   'face_block': 'iOS Vision／Android ML Kit 人臉偵測；有臉的照片變灰不能選', 'postcard': '拍立得（5.26）；已裝飾過的直接用裝飾好的版本，寄出當下的版本固定',
   'reply_timeout_h': 24, 'timeout_fallback': '對方營地風景明信片（營地＋寵物畫面，自動產生）', 'storage': '護照（不進相簿、不能拿來餵）', 'block_report': True},
 'draw_card': {'x': 40, 'y': 128, 'w': 310, 'h': 456, 'radius': 26, 'scene_h': 230, 'flip': '0.7s rotateY 90→0'},
 'pick_sheet': {'h': 640, 'radius': 26, 'grid_cols': 3, 'gap': 8, 'cell_radius': 12, 'face': 'grayscale + rgba(240,234,224,0.72) 遮罩', 'selected_ring': '3 #F6C945'},
 'back_card': {'x': 62, 'y': 168, 'w': 266, 'h': 324, 'photo': [14, 18, 238, 238], 'caption_y': 262, 'pop': '0.6s，轉 −6°', 'stamp': [186, -14, 58, 'rotate 8°'], 'postmark': [150, 6, 92, 'rotate −14°，0.5s 後蓋下']},
 'passport': {'bg': '#2E4686', 'page': '#F2E6CB', 'row': '每個好友一排：頭像 30＋營地名 14 Black #2E4686＋數量 11', 'card': [92, 112], 'gap': 12, 'tilt_deg': [-4, 3]}
}
json.dump(spec, open(os.path.join(HERE, '..', 'postcard_spec.json'), 'w'), ensure_ascii=False, indent=1)
print(len(M))
