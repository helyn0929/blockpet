"""BlockPet 佈置營地（設計文件 5.28）素材：SVG 原稿＋@3x PNG。"""
import json, os, io
import cairosvg
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Icons'); os.makedirs(OUT, exist_ok=True)
M = []
def emit(name, w, h, body, use=''):
    svg = f"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>{body}</svg>"
    open(os.path.join(OUT, name + '.svg'), 'w').write(svg)
    im = Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=round(w*3), output_height=round(h*3)))).convert('RGBA')
    im.save(os.path.join(OUT, name + '@3x.png'))
    M.append({'file': name, 'size_1x': [w, h], 'png_px': list(im.size), 'use': use})
D = "M30 1.5 58.5 15 30 28.5 1.5 15Z"
def tile(name, stroke, so, fill, fo, use):
    emit(name, 60, 30, f"<path d='{D}' fill='{fill}' fill-opacity='{fo}' stroke='{stroke}' stroke-opacity='{so}' stroke-width='2' stroke-linejoin='round'/>", use)
tile('deco_tile_normal', '#FFFFFF', 0.55, '#FFFFFF', 0.12, '一般格子（菱形 60×30，錨點在中心）')
tile('deco_tile_ok', '#B8F5A0', 1, '#8CE66E', 0.32, '可以放（Unity 做 1.4 秒閃爍）')
tile('deco_tile_target', '#FFFFFF', 1, '#8CE66E', 0.75, '要放的那格')
tile('deco_tile_occupied', '#FFF8EC', 0.95, '#FFF8EC', 0.45, '家具佔的格子')
tile('deco_tile_selected', '#F6C945', 1, '#F6C945', 0.45, '選到的家具佔的格子')
tile('deco_tile_blocked', '#FFB4A3', 0.45, '#E86B4F', 0.18, '不能放')
emit('deco_dim', 4, 4, "<rect width='4' height='4' fill='#141E0A' fill-opacity='0.18'/>", '佈置時營地變暗的遮罩（拉滿全畫面）')
emit('deco_icon_build', 20, 20, "<g transform='scale(0.8333)' fill='none' stroke='#3A2E22' stroke-width='2.2' stroke-linejoin='round' stroke-linecap='round'><path d='M4 9.5h16'/><path d='M6 9.5v10M18 9.5v10'/><path d='M3 6h18l-2 3.5H5Z'/><path d='M10 19.5v-5h4v5'/></g>", '「佈置」按鈕圖示')
emit('deco_btn_pill', 46, 46, "<rect width='46' height='46' rx='23' fill='#FFF8EC'/>", '「佈置」按鈕底（9-slice 69；陰影 0 4 12 rgba(30,40,20,0.3) 另加）')
emit('deco_btn_done', 38, 38, "<rect width='38' height='38' rx='19' fill='#6FA45F'/>", '「完成」按鈕底（9-slice 57；下緣 0 3 0 #4A7A3F 另加）')
emit('deco_topbar', 4, 64, "<rect width='4' height='64' fill='#FFF8EC' fill-opacity='0.94'/>", '佈置中上方列底（拉寬）')
emit('deco_panel', 60, 60, "<path d='M0 22A22 22 0 0 1 22 0H38A22 22 0 0 1 60 22V60H0Z' fill='#FFF8EC'/>", '下方「還沒擺出來的家具」面板（9-slice 上 66 左右 66）')
emit('deco_card', 80, 92, "<rect width='80' height='92' rx='16' fill='#FFFCF4'/>", '面板裡的家具卡')
emit('deco_card_ring', 86, 98, "<rect x='1.5' y='1.5' width='83' height='95' rx='18.5' fill='none' stroke='#F6C945' stroke-width='3'/>", '選到的家具卡外框')
emit('deco_tag_dark', 20, 20, "<rect width='20' height='20' rx='10' fill='#3A2E22'/>", '家具上方小標「營火・點綠色格子移過去」（字 11 Black #FFF8EC）')
emit('deco_shadow', 60, 12, "<ellipse cx='30' cy='6' rx='30' ry='6' fill='#141E0A' fill-opacity='0.28'/>", '家具腳下的影子（寬度跟底座）')
json.dump(M, open(os.path.join(HERE, '..', 'decorate_assets.json'), 'w'), ensure_ascii=False, indent=1)
spec = {
 'doc': '設計文件 5.28 佈置營地；設計稿頁面「佈置營地」，元件 Decorate',
 'tile_1x': [60, 30], 'axes': {'i': 'down-right (+30, +15)', 'j': 'down-left (-30, +15)'},
 'cell_center': 'x = OX + (i - j) * 30, y = OY + (i + j + 1) * 15', 'origin_mockup': [205, 370],
 'area_mockup': {'center_x': [15, 378], 'center_y': [335, 625], 'cells': 120, 'placeable': 79, 'blocked': 41},
 'grid_table': '每個場景一張：存在的格子＋不能放的格子，由美術在 Unity 標（建議 ScriptableObject）',
 'furniture_footprint': {'tent': [2, 2], 'hammock': [2, 1], 'mat': [2, 1], 'screen': [2, 1], 'fire': [1, 1], 'easel': [1, 1], 'lantern': [1, 1], 'mailbox': [1, 1], 'lights': 'hang point'},
 'anchor': '底座中心；圖片底部對齊底座最下角往上 6，寬約 (w+d)*30*0.86、高＝寬*0.8',
 'z_order': '依 i + j + w + d，越大越前面',
 'fits_rule': '底座蓋到的每一格都要存在、不是 blocked、沒被其他家具佔用（自己原本的位置可以）',
 'edit_mode': {'dim': 'rgba(20,30,10,0.18)', 'topbar_h': 64, 'panel_h': 150, 'pet_opacity': 0.5, 'ok_pulse_s': 1.4},
 'colors': {'normal': ['#FFFFFF 55%', '#FFFFFF 12%'], 'ok': ['#B8F5A0', 'rgba(140,230,110,0.32)'], 'target': ['#FFFFFF', 'rgba(140,230,110,0.75)'],
            'occupied': ['#FFF8EC 95%', 'rgba(255,248,236,0.45)'], 'selected': ['#F6C945', 'rgba(246,201,69,0.45)'], 'blocked': ['rgba(255,180,163,0.45)', 'rgba(232,107,79,0.18)']}
}
json.dump(spec, open(os.path.join(HERE, '..', 'decorate_spec.json'), 'w'), ensure_ascii=False, indent=1)
print(len(M))
