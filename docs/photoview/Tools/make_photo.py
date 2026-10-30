"""BlockPet 相簿照片檢視 A（PhotoAltA）＋分割畫面快門 G（Camp8CamG）素材：SVG 原稿＋@3x PNG。形狀與顏色取自設計稿。白色素材在 Unity 上色或調透明度；陰影、毛玻璃另加。"""
import json, os, io
import cairosvg
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Icons'); os.makedirs(OUT, exist_ok=True)
M = []
def emit(name, w, h, body, vb=None, use='', s9=None, group=''):
    vb = vb or f'0 0 {w} {h}'
    svg = f"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='{vb}'>{body}</svg>"
    open(os.path.join(OUT, name + '.svg'), 'w').write(svg)
    im = Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=round(w*3), output_height=round(h*3)))).convert('RGBA')
    im.save(os.path.join(OUT, name + '@3x.png'))
    e = {'group': group, 'file': name, 'size_1x': [w, h], 'png_px': list(im.size), 'use': use}
    if s9: e['9slice_px_at3x_LTRB'] = s9
    M.append(e)
def rr(x, y, w, h, r, fill, extra=''):
    return f"<rect x='{x}' y='{y}' width='{w}' height='{h}' rx='{r}' fill='{fill}' {extra}/>"
G1 = '照片檢視 A（5.14 ⑥）'
emit('photo_btn_close', 44, 44, "<circle cx='22' cy='22' r='22' fill='#F2E9D8'/><path d='M17 17l10 10M27 17l-10 10' stroke='#3A2E22' stroke-width='2.4' stroke-linecap='round'/>", use='左上關閉（含底），回到相簿', group=G1)
emit('photo_polaroid_frame', 40, 40, rr(0, 0, 40, 40, 6, '#FFFCF4'), use='拍立得白框（寬 330；內距 12 12 14；陰影 0 10 24 另加；整張轉 ±1.5°）', s9=[24, 24, 24, 24], group=G1)
emit('photo_mask', 40, 40, rr(0, 0, 40, 40, 3, '#FFFFFF'), use='照片圓角 3 遮罩（306 × 306）', s9=[12, 12, 12, 12], group=G1)
emit('photo_tape', 80, 24, rr(0, 0, 80, 24, 0, '#F6C945', "fill-opacity='0.55'"), use='膠帶（拍立得上緣中間 (125, −12)，轉 −3°）', group=G1)
emit('photo_dot', 6, 6, "<circle cx='3' cy='3' r='3' fill='#3A2E22' fill-opacity='0.25'/>", use='位置點（其他張）', group=G1)
emit('photo_dot_on', 18, 6, rr(0, 0, 18, 6, 3, '#3A2E22'), use='位置點（目前這張）', s9=[9, 9, 9, 9], group=G1)
emit('photo_avatar', 36, 36, "<circle cx='18' cy='18' r='18' fill='#FFFFFF'/>", use='留言大頭貼底 36（白，上色成員顏色；首字 TMP 14 Black）；「誰拍的」22 圓也用這張縮小', group=G1)
emit('photo_input_bar', 12, 12, rr(0, 0, 12, 12, 0, '#FFF8EC', "fill-opacity='0.96'") + rr(0, 0, 12, 1, 0, '#EFE6D4'), use='底部輸入列底（上緣線 1 #EFE6D4；橫向拉伸）', s9=[6, 6, 6, 6], group=G1)
emit('chat_input', 60, 42, "<rect x='0.75' y='0.75' width='58.5' height='40.5' rx='20.25' fill='#FFFFFF' stroke='#E2D6C0' stroke-width='1.5'/>", use='留言輸入框（同聊天室素材包）', s9=[63, 63, 63, 63], group=G1)
emit('btn_send_on', 42, 45, "<circle cx='21' cy='24' r='21' fill='#D9A92C'/><circle cx='21' cy='21' r='21' fill='#F6C945'/><path d='M21 30V13M13.5 20.5 21 13l7.5 7.5' fill='none' stroke='#3A2E22' stroke-width='2.8' stroke-linecap='round' stroke-linejoin='round'/>", use='送出鈕（有字，含下緣立體 3；同聊天室素材包）', group=G1)
emit('btn_send_off', 42, 42, "<circle cx='21' cy='21' r='21' fill='#E2D6C0'/><path d='M21 30V13M13.5 20.5 21 13l7.5 7.5' fill='none' stroke='#FFFFFF' stroke-width='2.8' stroke-linecap='round' stroke-linejoin='round'/>", use='送出鈕（沒字；同聊天室素材包）', group=G1)
G2 = '分割畫面快門 G（5.16 ⑤）'
emit('btn_shutter_bg', 56, 56, "<circle cx='28' cy='28' r='28' fill='#1B1712' fill-opacity='0.15'/>", use='快門底（深色 15%；毛玻璃 4 在 Unity 另加）', group=G2)
emit('btn_shutter_ring', 56, 56, "<circle cx='28' cy='28' r='26.25' fill='none' stroke='#FFFFFF' stroke-width='3.5'/>", use='快門外圈（白，Unity 透明度 80%）', group=G2)
emit('btn_shutter_core', 42, 42, "<circle cx='21' cy='21' r='21' fill='#FFFFFF'/>", use='快門中間白圓（白，Unity 透明度 60%；放在外圈正中間）', group=G2)
emit('btn_shutter', 56, 56, "<circle cx='28' cy='28' r='28' fill='#1B1712' fill-opacity='0.15'/><circle cx='28' cy='28' r='26.25' fill='none' stroke='#FFFFFF' stroke-opacity='0.8' stroke-width='3.5'/><circle cx='28' cy='28' r='21' fill='#FFFFFF' fill-opacity='0.6'/>", use='快門合成預覽（三層已合在一起；要做按下效果時改用上面三張分開的）', group=G2)
emit('cam_toast_card', 40, 40, rr(0, 0, 40, 40, 14, '#FFFCF4'), use='拍完提示小卡（好好吃！命中喜好・進化 +12；陰影 0 3 8 另加）', s9=[42, 42, 42, 42], group=G2)
json.dump(M, open(os.path.join(HERE, '..', 'photo_shutter_assets.json'), 'w'), ensure_ascii=False, indent=1)
print(len(M))
