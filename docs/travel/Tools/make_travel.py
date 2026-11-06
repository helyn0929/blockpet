"""BlockPet 旅行（5.21：會長大的地圖、派寵物旅行、旅行護照、加營地好友）素材：SVG 原稿＋@3x PNG。
形狀與顏色取自設計稿 TravelGrow／TravelTrip。字一律用 TMP，不畫進圖。白色素材在 Unity 上色。"""
import json, os, io
import cairosvg
from PIL import Image
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
def rr(x, y, w, h, r, fill, extra=''):
    return f"<rect x='{x}' y='{y}' width='{w}' height='{h}' rx='{r}' fill='{fill}' {extra}/>"
BAG = "<rect x='2' y='4' width='14' height='15' rx='4' fill='#C2621A' stroke='#7A3A0E' stroke-width='1.5'/><path d='M6 4V2.5h6V4' fill='none' stroke='#7A3A0E' stroke-width='1.5'/><rect x='5' y='10' width='8' height='5' rx='1.5' fill='#E89A5A'/>"

# ── 地圖：地形 ──
emit('map_sea_wave_tile', 44, 20, "<path d='M3 13q8-6 16 0t16 0' fill='none' stroke='#FFFFFF' stroke-opacity='0.32' stroke-width='2' stroke-linecap='round'/>", use='海的波浪紋（平鋪在 #9FCFD6 上）')
emit('map_land_disc', 64, 64, "<circle cx='32' cy='32' r='32' fill='#FFFFFF'/>", use='陸地圓（白，上色後疊三層：淺水 #B8E0E4 半徑+20 → 沙灘 #F3E7C9 +7 → 草地 #BFDE9C；縮放成需要的半徑）')
emit('map_lake', 56, 36, "<ellipse cx='28' cy='18' rx='26' ry='16' fill='#9FCFD6' stroke='#E8F4EC' stroke-width='3'/><path d='M15 18q6.5-3 13 0t13 0' fill='none' stroke='#FFFFFF' stroke-opacity='0.6' stroke-width='1.6' stroke-linecap='round'/>", use='湖（可等比縮放 0.6～1.1）')
emit('map_hill', 60, 30, "<ellipse cx='30' cy='15' rx='30' ry='15' fill='#A6CF82'/><ellipse cx='22.5' cy='10.5' rx='13.5' ry='6.6' fill='#C9E6AA'/>", use='小山丘（可等比縮放 0.7～1.1）')
for nm, col in (('map_tree', '#6BAA55'), ('map_tree_dark', '#5A9548')):
    emit(nm, 20, 24, f"<ellipse cx='11' cy='19.2' rx='9' ry='3.6' fill='#4A7A3A' opacity='0.22'/><circle cx='9' cy='12' r='9' fill='{col}'/><circle cx='6.3' cy='9.3' r='4' fill='#8CC56E'/>", use='樹（樹冠＋亮點＋影子；縮放 0.6～1）；依 y 由上往下排')
emit('map_flower', 6, 6, "<circle cx='3' cy='3' r='2.2' fill='#FFFFFF'/>", use='小花（白，上色 #F6C945／#F28FA6／原色白）')
emit('map_fog', 110, 70, ''.join(f"<circle cx='{x}' cy='{y}' r='{r}' fill='#FFFFFF' opacity='0.85'/>" for x, y, r in ((55, 35, 28), (28, 30, 20), (82, 30, 22), (40, 50, 20), (72, 50, 22), (55, 18, 18))), use='下一塊地的雲霧（輕微左右飄 5 秒）；文字「加好友後這裡會長出地」用 TMP 11 Black #6B8A94')

# ── 地圖：路邊小東西（旅行撿到）──
emit('map_prop_bench', 16, 12, "<g transform='translate(8 5)'><rect x='-7' y='-4' width='14' height='3' rx='1' fill='#B07A45'/><rect x='-7' y='0' width='14' height='3' rx='1' fill='#C98F55'/><rect x='-6' y='3' width='2' height='4' fill='#7A5230'/><rect x='4' y='3' width='2' height='4' fill='#7A5230'/></g>", use='長椅')
emit('map_prop_sign', 18, 20, "<g transform='translate(9 12)'><rect x='-1.2' y='-6' width='2.4' height='13' fill='#7A5230'/><path d='M-8 -11h13l3 3-3 3h-13Z' fill='#F6C945' stroke='#B88A1F' stroke-width='1'/></g>", use='路牌')
emit('map_prop_flowerbed', 20, 12, "<g transform='translate(10 5)'><ellipse cx='0' cy='2' rx='9' ry='4' fill='#8FBF6A'/><circle cx='-4' cy='0' r='2.6' fill='#F28FA6'/><circle cx='1' cy='-2' r='2.6' fill='#F6C945'/><circle cx='5' cy='1' r='2.6' fill='#FFFFFF'/></g>", use='花圃')
emit('map_prop_lamp', 10, 24, "<g transform='translate(5 17)'><rect x='-1' y='-10' width='2' height='16' fill='#5B6B6E'/><circle cx='0' cy='-12' r='3.6' fill='#FFE9A8' stroke='#5B6B6E' stroke-width='1.2'/></g>", use='路燈')

# ── 地圖：營地大頭針 ──
emit('map_pin_frame', 56, 56, "<circle cx='28' cy='28' r='26.5' fill='none' stroke='#FFFCF4' stroke-width='3'/>", use='好友營地大頭針外框（裡面放對方場景＋寵物，用 map_pin_mask 裁圓；陰影 0 4 10 另加）；旅行中目的地把框上色 #E86B4F')
emit('map_pin_mask', 56, 56, "<circle cx='28' cy='28' r='28' fill='#FFFFFF'/>", use='大頭針圓形遮罩')
emit('map_home_frame', 88, 88, "<circle cx='44' cy='44' r='42' fill='none' stroke='#F6C945' stroke-opacity='0.35' stroke-width='4'/><circle cx='44' cy='44' r='36' fill='none' stroke='#F6C945' stroke-width='4'/>", use='家的外框（76 圓＋外光圈；裡面同大頭針做法，遮罩用 map_home_mask）')
emit('map_home_mask', 76, 76, "<circle cx='38' cy='38' r='38' fill='#FFFFFF'/>", use='家的圓形遮罩')
emit('map_label_bg', 24, 16, rr(0, 0, 24, 16, 8, '#FFF8EC', "fill-opacity='0.95'"), use='營地名牌底（字 TMP 10 Black）', s9=[24, 24, 24, 24])
emit('map_home_label', 24, 20, rr(0, 2, 24, 18, 9, '#D9A92C') + rr(0, 0, 24, 18, 9, '#F6C945'), use='「家人營地（家）」黃色名牌（含下緣 2）', s9=[27, 27, 27, 30])
emit('map_stamp_badge', 22, 22, "<circle cx='11' cy='11' r='10' fill='#FFFCF4' stroke='#C2452B' stroke-width='2'/>", use='去過的營地：右上小章（字 TMP「印」或「×2」9 Black #C2452B，整個轉 -10°）')
emit('map_tag_pill', 24, 16, rr(0, 0, 24, 16, 8, '#FFFFFF'), use='大頭針上的標籤（白，上色：「BowBow 在這裡」#E86B4F、「目的地」#6F8FE0、客人營地名 #6F8FE0；字白 10 Black）', s9=[24, 24, 24, 24])
emit('map_route_dash', 12, 6, "<circle cx='3' cy='3' r='2.5' fill='#FFFFFF'/>", use='還沒去過的路：程式沿曲線每 11px 放一個點（或用 LineRenderer 虛線材質）；路本身用程式畫曲線')

# ── 寵物旅行 ──
emit('pet_backpack', 18, 20, BAG, use='寵物的小背包（疊在寵物身上，旅途中、在對方營地都背著）')
emit('away_sign', 110, 120, "<rect x='50' y='40' width='10' height='80' fill='#8A5A2E'/><path d='M10 20h90l-10 16 10 16H10Z' fill='#FFF8EC' stroke='#8A5A2E' stroke-width='3' stroke-linejoin='round'/>", use='自己營地：寵物出門時的木牌（字 TMP「去旅行了！」Huninn 14）')

# ── 頂部 ──
emit('travel_btn_back', 44, 44, "<circle cx='22' cy='22' r='22' fill='#F2E9D8'/><path d='M25 15l-7 7 7 7' fill='none' stroke='#3A2E22' stroke-width='2.8' stroke-linecap='round' stroke-linejoin='round'/>", use='左上返回（含底）')
emit('travel_header_bg', 12, 12, rr(0, 0, 12, 12, 0, '#FFF8EC', "fill-opacity='0.94'") + rr(0, 11, 12, 1, 0, '#EFE6D4'), use='頂部底（拉伸到 390 × 74）', s9=[6, 6, 6, 6])
emit('travel_btn_invite', 38, 38, "<circle cx='19' cy='19' r='18.25' fill='#E2E8F8' stroke='#6F8FE0' stroke-width='1.5'/><g transform='translate(10 10) scale(0.75)' fill='none' stroke='#4F6FC4' stroke-width='3.2' stroke-linecap='round' stroke-linejoin='round'><path d='M4 6h16v12H4Z'/><path d='m4 7 8 6 8-6'/></g>", use='好友邀請小圖示（藍色信封）')
emit('travel_badge_red', 18, 18, "<circle cx='9' cy='9' r='9' fill='#FFF8EC'/><circle cx='9' cy='9' r='7' fill='#E86B4F'/>", use='新邀請紅點（字 TMP 白 10 Black；兩位數時用 9-slice 版）')
emit('travel_badge_red_wide', 24, 18, rr(0, 0, 24, 18, 9, '#FFF8EC') + rr(2, 2, 20, 14, 7, '#E86B4F'), use='紅點（可拉寬）', s9=[27, 27, 27, 27])
emit('travel_btn_add', 40, 41, rr(0, 3, 40, 38, 19, '#D9A92C') + rr(0, 0, 40, 38, 19, '#F6C945'), use='「＋ 加營地好友」黃色膠囊（含下緣 3；字 TMP 13 Black）', s9=[57, 57, 57, 66])

# ── 底部：護照鈕、狀態卡 ──
emit('travel_btn_passport', 64, 76, "<path d='M12 0H48A16 16 0 0 1 64 16V60A16 16 0 0 1 48 76H12A12 12 0 0 1 0 64V12A12 12 0 0 1 12 0Z' fill='#3F5BA8'/><path d='M12 0A12 12 0 0 0 0 12V64A12 12 0 0 0 12 76H6V0Z' fill='#2E4686'/><g transform='translate(24 13)' fill='none' stroke='#FFE9A8' stroke-width='2'><circle cx='11' cy='10' r='6'/><path d='M5 10h12M11 4c2 2 2 10 0 12M11 4c-2 2-2 10 0 12'/></g>", use='左下護照鈕（字 TMP「護照」11 Black #FFE9A8、「N 個章」10 Black 白；陰影 0 6 14 另加）')
emit('icon_globe', 24, 24, "<g fill='none' stroke='#FFE9A8' stroke-width='2'><circle cx='12' cy='11' r='6'/><path d='M6 11h12M12 5c2 2 2 10 0 12M12 5c-2 2-2 10 0 12'/></g>", use='護照地球圖示（單獨）')
emit('travel_status_card', 40, 40, rr(0, 0, 40, 40, 18, '#FFF8EC', "fill-opacity='0.96'"), use='右下狀態卡底（陰影 0 6 14 另加）', s9=[54, 54, 54, 54])
emit('travel_progress_track', 12, 6, rr(0, 0, 12, 6, 3, '#EFE6D4'), use='旅途進度條軌道', s9=[9, 9, 9, 9])
emit('travel_progress_fill', 12, 6, rr(0, 0, 12, 6, 3, '#F6C945'), use='旅途進度條填滿', s9=[9, 9, 9, 9])

# ── 派寵物出發（底部面板）──
emit('travel_dim', 4, 4, rr(0, 0, 4, 4, 0, '#14180E', "fill-opacity='0.45'"), use='面板後面的暗色遮罩（拉伸滿版）')
emit('travel_sheet_panel', 60, 60, "<path d='M0 26 A26 26 0 0 1 26 0 H34 A26 26 0 0 1 60 26 V60 H0Z' fill='#FFF8EC'/>", use='底部面板底（上方圓角 26）', s9=[84, 84, 84, 0])
emit('travel_sheet_grabber', 40, 5, rr(0, 0, 40, 5, 2.5, '#E2D6C0'), use='拖曳把手')
emit('travel_photo_mask', 40, 40, rr(0, 0, 40, 40, 18, '#FFFFFF'), use='對方營地預覽圓角 18 遮罩（354 × 130）', s9=[54, 54, 54, 54])
emit('travel_info_cell', 40, 40, rr(0, 0, 40, 40, 14, '#FFFCF4'), use='「路程／在那邊玩／回到家」資訊格', s9=[42, 42, 42, 42])
emit('travel_btn_go', 60, 58, rr(0, 4, 60, 54, 18, '#D9A92C') + rr(0, 0, 60, 54, 18, '#F6C945'), use='「派 BowBow 去旅行」黃色大按鈕（含下緣 4；左邊放 pet_backpack 20 × 22）', s9=[54, 54, 54, 66])

# ── 在對方營地玩／對方看到 ──
emit('travel_glass_bar', 60, 52, "<rect x='0.75' y='0.75' width='58.5' height='50.5' rx='25.25' fill='#FFF8EC' fill-opacity='0.62' stroke='#FFFFFF' stroke-opacity='0.65' stroke-width='1.5'/>", use='上方毛玻璃 bar（Unity 另加模糊；同 5.16 ②）', s9=[78, 78, 78, 78])
emit('travel_btn_back_sm', 40, 40, "<circle cx='20' cy='20' r='20' fill='#FFF8EC'/><path d='M22.5 13.5 16 20l6.5 6.5' fill='none' stroke='#3A2E22' stroke-width='2.8' stroke-linecap='round' stroke-linejoin='round'/>", use='bar 裡的返回地圖（40）')
emit('travel_tag_traveling', 24, 18, rr(0, 0, 24, 18, 9, '#6F8FE0'), use='「旅行中」藍色標籤（字白 11 Black）', s9=[27, 27, 27, 27])
emit('bubble_left_tail', 40, 32, "<path d='M14 0H26A14 14 0 0 1 40 14V18A14 14 0 0 1 26 32H4A4 4 0 0 1 0 28V14A14 14 0 0 1 14 0Z' fill='#FFFFFF'/>", use='寵物對話泡泡（左下角尖；陰影 0 3 8 另加）', s9=[42, 42, 42, 42])
emit('bubble_right_tail', 40, 32, "<path d='M14 0H26A14 14 0 0 1 40 14V28A4 4 0 0 1 36 32H14A14 14 0 0 1 0 18V14A14 14 0 0 1 14 0Z' fill='#FFFFFF'/>", use='寵物對話泡泡（右下角尖）', s9=[42, 42, 42, 42])
emit('travel_popup_card', 40, 40, rr(0, 0, 40, 40, 20, '#FFF8EC'), use='「BowBow 從家人營地來玩了！」白卡、自己營地的旅行中卡片（陰影 0 10 24 另加）', s9=[60, 60, 60, 60])
emit('travel_avatar_bg', 48, 48, "<circle cx='24' cy='24' r='24' fill='#FCE7A6'/>", use='客人寵物頭像底')

# ── 回家蓋章、護照 ──
emit('passport_page', 40, 40, "<rect x='1' y='1' width='38' height='38' rx='7' fill='#F2E6CB' stroke='#E2D2A6' stroke-width='2'/>", use='護照內頁（回家卡 220 × 170、護照頁滿版；背景 #2E4686）', s9=[36, 36, 36, 36])
emit('passport_fold', 2, 40, rr(0.5, 0, 1, 40, 0, '#D9C79A'), use='回家卡內頁中間的摺線（拉長）')
emit('passport_stamp_ring', 92, 92, "<circle cx='46' cy='46' r='42' fill='none' stroke='#FFFFFF' stroke-width='4'/><circle cx='46' cy='46' r='34' fill='none' stroke='#FFFFFF' stroke-width='1.5'/>", use='章的雙圈（白，上色每個營地一色：#C2452B／#4F8F45／#3F5BA8／#B8891F…；中間放對方寵物 36 × 31；上方營地名、下方日期 TMP；整個歪 -8～10°）')
emit('passport_stamp_empty', 92, 92, "<circle cx='46' cy='46' r='42' fill='none' stroke='#B9AC96' stroke-width='2' stroke-dasharray='4 5'/>", use='還沒去過的空格（中間「？」Huninn 14 #B9AC96；下方「營地名＋還沒去過」）')
emit('passport_times_badge', 24, 16, rr(0, 0, 24, 16, 8, '#3A2E22'), use='「×2」次數標籤', s9=[24, 24, 24, 24])
emit('passport_btn_back', 44, 44, "<circle cx='22' cy='22' r='22' fill='#FFFFFF' fill-opacity='0.15'/><path d='M25 15l-7 7 7 7' fill='none' stroke='#FFE9A8' stroke-width='2.8' stroke-linecap='round' stroke-linejoin='round'/>", use='護照頁左上返回')
emit('travel_btn_primary', 60, 53, rr(0, 3, 60, 50, 15, '#D9A92C') + rr(0, 0, 60, 50, 15, '#F6C945'), use='「看護照」等一般黃色按鈕（含下緣 3）', s9=[45, 45, 45, 54])

# ── 加營地好友、好友邀請、營地代碼卡 ──
emit('friend_input_box', 40, 56, "<rect x='1' y='1' width='38' height='54' rx='14' fill='#FFFFFF' stroke='#6F8FE0' stroke-width='2'/>", use='營地代碼輸入框（藍框；寬拉到 354）', s9=[45, 45, 45, 45])
emit('friend_btn_disabled', 60, 52, rr(0, 0, 60, 52, 15, '#E2D6C0'), use='「送出好友邀請」沒打滿 6 碼時（灰）', s9=[45, 45, 45, 45])
emit('icon_paper_plane', 64, 64, "<circle cx='32' cy='32' r='32' fill='#E2E8F8'/><g fill='none' stroke='#4F6FC4' stroke-width='2.6' stroke-linejoin='round' stroke-linecap='round'><path d='M18 31 46 19 39 46 31 37Z'/><path d='M46 19 31 37v8l4-5'/></g>", use='送出邀請後的紙飛機圖示')
emit('friend_invite_card', 40, 40, rr(0, 0, 40, 40, 18, '#FFFCF4'), use='好友邀請卡底（內距 12）', s9=[54, 54, 54, 54])
emit('friend_thumb_mask', 72, 72, rr(0, 0, 72, 72, 16, '#FFFFFF'), use='邀請卡上對方營地縮圖遮罩')
emit('friend_btn_no', 40, 46, rr(0, 0, 40, 46, 14, '#F2E9D8'), use='「不要」', s9=[42, 42, 42, 42])
emit('friend_btn_yes', 40, 49, rr(0, 3, 40, 46, 14, '#D9A92C') + rr(0, 0, 40, 46, 14, '#F6C945'), use='「同意」（含下緣 3）', s9=[42, 42, 42, 51])
emit('camp_code_card', 40, 40, "<rect x='0.75' y='0.75' width='38.5' height='38.5' rx='17.25' fill='#E2E8F8' stroke='#6F8FE0' stroke-width='1.5'/>", use='空間資訊的藍色營地代碼卡（5.15 ③-2）', s9=[54, 54, 54, 54])
emit('camp_code_cell', 42, 52, rr(0, 2, 42, 50, 12, '#B9C7EC') + rr(0, 0, 42, 50, 12, '#FFFFFF'), use='營地代碼一格（42 × 50＋下緣 2；字 Huninn 26 #3F5BA8）')
emit('camp_code_btn_copy', 40, 40, "<rect x='0.75' y='0.75' width='38.5' height='38.5' rx='13.25' fill='#FFFFFF' stroke='#B9C7EC' stroke-width='1.5'/>", use='「複製」', s9=[42, 42, 42, 42])
emit('camp_code_btn_share', 40, 43, rr(0, 3, 40, 40, 14, '#4F6FC4') + rr(0, 0, 40, 40, 14, '#6F8FE0'), use='「分享」（藍、含下緣 3）', s9=[42, 42, 42, 51])

json.dump(M, open(os.path.join(HERE, '..', 'travel_assets.json'), 'w'), ensure_ascii=False, indent=1)
print(len(M), 'assets')
