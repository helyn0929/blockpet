"""BlockPet 拍立得分割畫面（設計文件 5.26）素材：SVG 原稿＋@3x PNG。字一律用 TMP。"""
import json, os, io
import cairosvg
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Icons'); os.makedirs(OUT, exist_ok=True)
M = []
INK = '#2B2622'; BR = '#3A2E22'
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
# 線條圖示
emit('pl_icon_back', 28, 28, line("<path d='M15 5.5 8.5 12l6.5 6.5'/>", 28), '左上返回（沒有底，點擊範圍 44；白光在 Unity 加）')
emit('pl_icon_download', 23, 23, line("<path d='M12 4v11'/><path d='m7 10.5 5 5 5-5'/><path d='M5 19.5h14'/>", 23, sw=2.1), '右上下載（沒有底，點擊範圍 38×44；白光在 Unity 加）')
emit('pl_icon_share', 23, 23, line("<path d='M12 14V3.5'/><path d='m7.5 7.5 4.5-4.5 4.5 4.5'/><path d='M8 10H6v10h12V10h-2'/>", 23, sw=2.1), '右上分享（沒有底，點擊範圍 38×44；白光在 Unity 加）')
emit('pl_icon_edit', 20, 20, line("<path d='M4 20h4L19 9l-4-4L4 16Z'/><path d='m13.5 6.5 4 4'/>", 20, sw=2.1), '拍立得右下編輯鈕（透明沒有底；Unity 上色成畫框字色、透明度 70%；點擊範圍 34）')
emit('pl_dot_yellow', 5, 5, "<circle cx='2.5' cy='2.5' r='2.5' fill='#F6C945'/>", '編輯鈕下的黃點（在裝飾頁時顯示）')
emit('pl_page_on', 16, 5, "<rect width='16' height='5' rx='2.5' fill='#3A2E22'/>", '頁數小點：現在那頁')
emit('pl_page_off', 5, 5, "<circle cx='2.5' cy='2.5' r='2.5' fill='#3A2E22' fill-opacity='0.25'/>", '頁數小點：另一頁')
emit('pl_pet_frame', 48, 48, "<rect x='1.5' y='1.5' width='45' height='45' rx='18.5' fill='none' stroke='#FFFCF4' stroke-width='3'/>", '寵物頁的框（邊 3、圓角 20；裡面放營地底圖並用 pl_panel_mask 裁）', s9=[63, 63, 63, 63])
emit('pl_panel_mask', 48, 48, "<rect width='48' height='48' rx='20' fill='#FFFFFF'/>", '下方面板遮罩／裝飾頁底（上色 #FFFCF4）', s9=[63, 63, 63, 63])
emit('pl_status_pill', 28, 28, "<rect x='0.5' y='0.5' width='27' height='27' rx='13.5' fill='#FFF8EC' fill-opacity='0.75' stroke='#FFFFFF' stroke-opacity='0.7'/>", '寵物頁左上狀態膠囊（愛心＋進化條；毛玻璃在 Unity 加）', s9=[42, 42, 42, 42])
emit('pl_dtab_on', 28, 28, "<rect width='28' height='28' rx='14' fill='#3A2E22'/>", '裝飾頁小分頁（選到）字 13 Black #FFF8EC', s9=[42, 42, 42, 42])
emit('pl_dtab_off', 28, 28, "<rect width='28' height='28' rx='14' fill='#FFFCF4' fill-opacity='0.9'/>", '裝飾頁小分頁（沒選到）字 13 Black #6B5D4E', s9=[42, 42, 42, 42])
emit('pl_cell_alt', 52, 52, "<rect width='52' height='52' rx='14' fill='#F4ECDD'/>", '手畫貼紙／畫框格子（52）', s9=[42, 42, 42, 42])
emit('pl_icon_pencil', 16, 16, line("<path d='M4 20h4L19 9l-4-4L4 16Z'/><path d='m13.5 6.5 4 4'/>", 16), '標題旁的小鉛筆（只有拍照的人看得到；顏色跟畫框字色，透明度 55%）')
emit('pl_icon_send', 15, 15, "<g transform='scale(0.75)'><path d='M2 10 18 2l-4 16-4-6-8-2Z' fill='#FFFFFF'/></g>", '送出紙飛機（白，放在 pl_btn_send 上）')
emit('pl_icon_check', 18, 18, line("<path d='m5 12.5 4.5 4.5L19 7.5'/>", 18, '#7CD67C', 3), '「已存到手機相簿」勾勾')
emit('pl_icon_x', 10, 10, "<path d='M2 2l6 6M8 2 2 8' stroke='#FFFFFF' stroke-width='2.4' stroke-linecap='round'/>", '撕掉 × （放在 pl_btn_peel 上）')
# 底與按鈕
emit('pl_btn_send', 36, 36, "<circle cx='18' cy='18' r='18' fill='#E86B4F'/>", '送出鈕底')
emit('pl_btn_peel', 26, 26, "<circle cx='13' cy='13' r='13' fill='#FFFFFF'/><circle cx='13' cy='13' r='11' fill='#E86B4F'/>", '自己的貼紙右上撕掉鈕（陰影 0 2 6 另加）')
emit('pl_input_bar', 46, 46, "<rect width='46' height='46' rx='23' fill='#FFFCF4'/>", '留言框（左右 14；陰影 0 3 10 rgba(60,45,25,0.16) 另加）', s9=[69, 69, 69, 69])
emit('pl_cell', 60, 60, "<rect width='60' height='60' rx='16' fill='#FFFCF4'/>", '貼紙／畫框格子（陰影 0 2 5 rgba(60,45,25,0.12) 另加）', s9=[48, 48, 48, 48])
emit('pl_cell_pet', 60, 60, "<rect width='60' height='60' rx='16' fill='#FCEFC8'/>", '寵物貼紙格子', s9=[48, 48, 48, 48])
emit('pl_cell_ring', 66, 66, "<rect x='1.5' y='1.5' width='63' height='63' rx='18.5' fill='none' stroke='#F6C945' stroke-width='3'/>", '選到的畫框／濾鏡外框（濾鏡格 78 用 9-slice 拉）', s9=[60, 60, 60, 60])
emit('pl_filter_mask', 78, 78, "<rect width='78' height='78' rx='14' fill='#FFFFFF'/>", '濾鏡縮圖遮罩（用這張照片套濾鏡）')
emit('pl_sel_dash', 40, 40, "<rect x='1' y='1' width='38' height='38' rx='12' fill='none' stroke='#F6C945' stroke-width='2' stroke-dasharray='6 4'/>", '自己的貼紙選取中虛線框（貼紙外擴 6）', s9=[45, 45, 45, 45])
emit('pl_tag_dark', 20, 20, "<rect width='20' height='20' rx='10' fill='#3A2E22'/>", '「小美貼的」小標、「只有小美能換畫框」提示（字 11／13 Black #FFF8EC）', s9=[27, 27, 27, 27])
emit('pl_toast', 36, 36, "<rect width='36' height='36' rx='18' fill='#2B2622' fill-opacity='0.9'/>", '「已存到手機相簿」「打開手機的分享選單…」（字 15 Black 白）', s9=[51, 51, 51, 51])
emit('pl_pill_comment', 30, 30, "<rect width='30' height='30' rx='15' fill='#1B1712' fill-opacity='0.55'/>", '照片上的留言膠囊（毛玻璃 6 在 Unity 加；頭像 22＋字 13 Bold 白）', s9=[45, 45, 45, 45])
emit('pl_tag_cat', 28, 28, "<rect width='28' height='28' rx='14' fill='#FFFFFF'/>", '右上辨識類別標籤底（白色，Unity 上類別淡色；點 10＋字 13 Black 類別深色）', s9=[42, 42, 42, 42])
emit('pl_cat_dot', 14, 14, "<circle cx='7' cy='7' r='7' fill='#FFFFFF'/><circle cx='7' cy='7' r='5' fill='#F29A3A'/>", '類別色點（白圈 2；中間在 Unity 換類別色）')
emit('pl_scroll_track', 44, 4, "<rect width='44' height='4' rx='2' fill='#3A2E22' fill-opacity='0.12'/>", '貼紙列下方短滑動條')
emit('pl_scroll_thumb', 18, 4, "<rect width='18' height='4' rx='2' fill='#3A2E22' fill-opacity='0.45'/>", '滑動條小點（x 0～26 跟著捲動比例）')
emit('pl_photo_mask', 318, 318, "<rect width='318' height='318' fill='#FFFFFF'/>", '拍立得照片區（正方形，不圓角）')
emit('pl_card', 354, 431, "<rect width='354' height='431' fill='#FFFFFF'/><rect x='18' y='24' width='318' height='318' fill='#000000' fill-opacity='0.12'/>", '拍立得卡片示意（白；照片區 (18,24) 318×318；實作時卡片底用純色＋花樣圖）')
# 畫框花樣（平鋪）
emit('pl_frame_gingham', 22, 22, "<rect width='22' height='22' fill='#FFFFFF'/><rect width='11' height='22' fill='#F082A0' fill-opacity='0.32'/><rect width='22' height='11' fill='#F082A0' fill-opacity='0.32'/>", '畫框「格紋」平鋪 22')
emit('pl_frame_dots', 16, 16, "<rect width='16' height='16' fill='#FFF8E6'/><circle cx='8' cy='8' r='2.75' fill='#F6C945'/>", '畫框「星點」平鋪 16')
emit('pl_frame_stripe', 22.63, 22.63, "<rect width='22.63' height='22.63' fill='#FFFFFF'/><g fill='#6F8FE0' fill-opacity='0.22'><path d='M0 11.31 11.31 0H22.63L0 22.63Z'/><path d='M22.63 11.31V22.63H11.31Z'/></g>", '畫框「條紋」平鋪（45° 斜線 8 間 8）')
# 手畫貼紙
D = {
 'star': ("<path d='M50 5 61 37 95 38 68 59 79 93 50 73 20 94 32 59 5 37 39 36Z' fill='#FFFFFF' stroke='"+INK+"' stroke-width='4' stroke-linejoin='round'/><path d='M48 10 60 40 91 42 66 60 75 89 51 71 24 89 35 61 9 40 41 39Z' fill='none' stroke='"+INK+"' stroke-width='1.6' stroke-linejoin='round' opacity='0.75'/>", '星星'),
 'heart': ("<path d='M50 88C22 66 5 47 13 27c7-16 28-17 37 3 9-20 31-20 37-3 8 20-9 39-37 61Z' fill='#F7A8B8' stroke='"+INK+"' stroke-width='4' stroke-linejoin='round'/><path d='M27 32c3-6 9-8 13-6' fill='none' stroke='#FFFFFF' stroke-width='5' stroke-linecap='round'/>", '愛心'),
 'spark': ("<path d='M50 6C54 36 64 46 94 50 64 54 54 64 50 94 46 64 36 54 6 50 36 46 46 36 50 6Z' fill='#F6C945' stroke='"+INK+"' stroke-width='4' stroke-linejoin='round'/>", '閃光'),
 'cloud': ("<path d='M24 74C10 74 6 56 18 50 14 34 34 26 42 36 46 20 72 20 74 38 90 36 96 56 84 64 88 74 78 76 70 74Z' fill='#FFFFFF' stroke='"+INK+"' stroke-width='4' stroke-linejoin='round'/>", '雲'),
 'paw': ("<g fill='#B07A4A' stroke='"+INK+"' stroke-width='3.5'><ellipse cx='50' cy='66' rx='22' ry='18'/><ellipse cx='24' cy='42' rx='9' ry='12'/><ellipse cx='42' cy='26' rx='9' ry='12'/><ellipse cx='60' cy='26' rx='9' ry='12'/><ellipse cx='77' cy='42' rx='9' ry='12'/></g>", '腳印'),
 'bolt': ("<path d='M58 4 18 56h26l-8 40 46-56H56Z' fill='#FFE07A' stroke='"+INK+"' stroke-width='4' stroke-linejoin='round'/>", '閃電'),
}
for k, (b, n) in D.items():
    emit('pl_stk_' + k, 66, 66, f"<g transform='scale(0.66)'>{b}</g>", f'手畫貼紙「{n}」（預設 66；可超出卡片，下載時切掉）')
OV = {
 'heart': ("<path d='M50 88C22 66 5 47 13 27c7-16 28-17 37 3 9-20 31-20 37-3 8 20-9 39-37 61Z' fill='#F06B5B' stroke='#FFFFFF' stroke-width='8' stroke-linejoin='round' paint-order='stroke'/>", '開心'),
 'note': ("<path d='M38 72V22l40-8v48' fill='none' stroke='#E86B4F' stroke-width='9' stroke-linejoin='round'/><circle cx='28' cy='74' r='13' fill='#E86B4F'/><circle cx='68' cy='64' r='13' fill='#E86B4F'/>", '歪頭'),
 'zz': ("<path d='M20 30h26L20 62h26' fill='none' stroke='#FFFFFF' stroke-width='16' stroke-linecap='round' stroke-linejoin='round'/><path d='M20 30h26L20 62h26' fill='none' stroke='#6F8FE0' stroke-width='8' stroke-linecap='round' stroke-linejoin='round'/><path d='M58 14h20L58 38h20' fill='none' stroke='#FFFFFF' stroke-width='14' stroke-linecap='round' stroke-linejoin='round'/><path d='M58 14h20L58 38h20' fill='none' stroke='#6F8FE0' stroke-width='6' stroke-linecap='round' stroke-linejoin='round'/>", '睡覺'),
 'drop': ("<path d='M50 10C64 34 76 48 76 64a26 26 0 0 1-52 0c0-16 12-30 26-54Z' fill='#7EC8F0' stroke='#FFFFFF' stroke-width='8' paint-order='stroke'/>", '流口水'),
}
for k, (b, n) in OV.items():
    emit('pl_pet_ov_' + k, 37, 37, f"<g transform='scale(0.37)'>{b}</g>", f'寵物貼紙「{n}」右上的小符號（寵物圖 92 的 40%，放在右 −6、上 −8）')
json.dump(M, open(os.path.join(HERE, '..', 'polaroid_assets.json'), 'w'), ensure_ascii=False, indent=1)
print(len(M))
