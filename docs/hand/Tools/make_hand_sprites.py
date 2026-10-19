"""把設計稿首頁 v8（Camp8）手牌區用到的 CSS 形狀，用 headless Chrome 以 3 倍解析度、透明背景切成 Unity sprite。
用法：python3 docs/hand/Tools/make_hand_sprites.py
輸出：Assets/Art/UI/Hand/Sprites/*.png、docs/hand/sprites.json（尺寸與 9-slice 邊界）、docs/hand/Source/*.html
"""
import json, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
PACK = os.path.normpath(os.path.join(HERE, ".."))
ROOT = os.path.normpath(os.path.join(PACK, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Art", "UI", "Hand", "Sprites")
SRC = os.path.join(PACK, "Source")
CHROME_CANDIDATES = [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Google Chrome 2.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
]

CREAM = "#FFFCF4"
CORAL = "#E86B4F"
INK = "#3A2E22"
GLASS = "rgba(255,248,236,0.72)"

CAMERA_SVG = ('<svg width="{s}" height="{s}" viewBox="0 0 40 40" style="display:block">'
              '<rect x="4" y="11" width="32" height="23" rx="6" fill="#FFFFFF"></rect>'
              '<path d="M14 11l3-5h6l3 5" fill="#FFFFFF"></path>'
              '<circle cx="20" cy="22" r="6.5" fill="#E86B4F"></circle>'
              '<circle cx="20" cy="22" r="3.6" fill="#FFFFFF"></circle></svg>')
CHAT_SVG = ('<svg width="28" height="28" viewBox="0 0 30 30" style="display:block">'
            '<path d="M5 6h20a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H13l-6 5v-5H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2Z" fill="#3A2E22"></path>'
            '<circle cx="10" cy="14" r="1.6" fill="#FFF8EC"></circle><circle cx="15" cy="14" r="1.6" fill="#FFF8EC"></circle>'
            '<circle cx="20" cy="14" r="1.6" fill="#FFF8EC"></circle></svg>')
EYE_SVG = ('<svg width="12" height="10" viewBox="0 0 24 18" fill="none" stroke="#8A7A66" stroke-width="2.4" style="display:block">'
           '<path d="M1 9s4-7 11-7 11 7 11 7-4 7-11 7S1 9 1 9Z"></path><circle cx="12" cy="9" r="3"></circle></svg>')

S = []


def add(name, w, h, html, border=None, note=""):
    S.append((name, w, h, html, border, note))


def box(x, y, w, h, style):
    return f'<div style="position:absolute;left:{x}px;top:{y}px;width:{w}px;height:{h}px;box-sizing:border-box;{style}"></div>'


def flat(name, r, color, note, size=None):
    """純色圓角 9-slice：中間留平直區。"""
    s = size or max(2 * r + 10, 20)
    add(name, s, s, box(0, 0, s, s, f"border-radius:{r}px;background:{color}"), [r * 3] * 4, note)


def shadow(name, r, css, pad, note):
    """只有陰影（形狀本身透明，CSS box-shadow 不畫在元素底下）。Unity：比形狀四邊各大 pad。"""
    s = 60
    add(name, s + pad * 2, s + pad * 2,
        box(pad, pad, s, s, f"border-radius:{r}px;box-shadow:{css}"),
        [(pad + r) * 3] * 4, note + f"；Unity 放在形狀下層，四邊各外擴 {pad}")


# ── 手牌卡片 92×124 ─────────────────────────────────────────────
flat("ui_hand_card", 10, CREAM, "手牌卡底 #FFFCF4 圓角 10")
shadow("ui_hand_card_shadow", 10, "0 6px 14px rgba(30,30,20,0.35)", 24, "手牌卡陰影 0 6 14 rgba(30,30,20,.35)")
add("ui_hand_card_camera", 92, 124,
    box(0, 0, 92, 124, f"border-radius:10px;background:{CORAL}") +
    box(5, 5, 82, 114, "border:2px dashed rgba(255,255,255,0.85);border-radius:8px"),
    None, "拍照牌（固定尺寸 92×124）：#E86B4F、內距 5 的白色虛線框")
add("ui_icon_camera", 40, 40, CAMERA_SVG.format(s=40), None, "相機圖示（拍照牌 30×30、相機鈕內 38×38）")
flat("ui_mask_r6", 6, "#FFFFFF", "照片遮罩 圓角 6（手牌照片 82×82）")
flat("ui_mask_r10", 10, "#FFFFFF", "照片遮罩 圓角 10（舉起照片 210×140）")
flat("ui_mask_r3", 3, "#FFFFFF", "照片遮罩 圓角 3（相簿鈕小縮圖 24×24）")

# ── 舉起的照片 230 寬 ───────────────────────────────────────────
flat("ui_popup", 16, CREAM, "舉起照片卡底 圓角 16")
shadow("ui_popup_shadow", 16, "0 16px 30px rgba(0,0,0,0.35)", 48, "舉起照片卡陰影 0 16 30 rgba(0,0,0,.35)")
flat("ui_btn_beige", 13, "#F2E9D8", "「放回」鈕 #F2E9D8 圓角 13")
add("ui_btn_yellow", 30, 33, box(0, 0, 30, 30, "border-radius:13px;background:#F6C945;box-shadow:0 3px 0 #D9A92C"),
    [39, 48, 39, 39], "「餵給寵物」鈕 #F6C945 圓角 13＋下緣 3 立體 #D9A92C；Unity 高 = 按鈕高 + 3")

# ── 出牌後：相簿／拍照／聊天 ────────────────────────────────────
add("ui_dock_tile", 92, 92,
    box(16, 16, 60, 60, f"border-radius:18px;background:{GLASS};border:1.5px solid rgba(255,255,255,0.8);"
                        "box-shadow:0 4px 10px rgba(30,30,20,0.25)"),
    None, "相簿／聊天方塊 60×60（含陰影外擴 16 → 92×92）。設計稿另有 backdrop-filter blur(8px)")
add("ui_dock_camera", 132, 132,
    f'<div style="position:absolute;left:24px;top:24px;width:84px;height:84px;box-sizing:border-box;border-radius:50%;'
    f'background:{CORAL};border:5px solid {CREAM};box-shadow:0 6px 14px rgba(30,30,20,0.4), inset 0 -4px 0 rgba(0,0,0,0.12);'
    f'display:flex;align-items:center;justify-content:center">{CAMERA_SVG.format(s=38)}</div>',
    None, "大拍照鈕 84×84（含陰影外擴 24 → 132×132），內含相機圖示")
add("ui_icon_chat", 28, 28, CHAT_SVG, None, "聊天圖示 28×28")
add("ui_album_minis", 76, 76,
    box(8 + 13, 8 + 11, 30, 36, f"border-radius:5px;background:{CREAM};transform:rotate(-10deg);box-shadow:0 2px 4px rgba(0,0,0,0.25)") +
    box(8 + 16, 8 + 10, 30, 36, f"border-radius:5px;background:{CREAM};transform:rotate(6deg);box-shadow:0 2px 4px rgba(0,0,0,0.25)"),
    None, "相簿鈕裡兩張疊起來的小照片（60×60 區域，外擴 8 → 76×76）；上面那張的縮圖在 Unity 疊上（24×24、轉 6°）")

# ── 營地最新照片卡（手牌最右）──────────────────────────────────
flat("ui_latest_card", 12, CREAM, "最新照片卡底 圓角 12")
shadow("ui_latest_back_shadow", 12, "0 4px 10px rgba(0,0,0,0.3)", 24, "後面疊的兩張卡的陰影")
shadow("ui_latest_glow_lo", 12, "0 0 0 3px #F6C945, 0 0 12px rgba(246,201,69,0.6), 0 8px 16px rgba(30,30,20,0.4)", 32,
       "最新照片卡金框發光（c5Glow 0%）")
shadow("ui_latest_glow_hi", 12, "0 0 0 3px #F6C945, 0 0 24px rgba(246,201,69,1), 0 8px 16px rgba(30,30,20,0.4)", 32,
       "最新照片卡金框發光（c5Glow 50%），與 lo 交替淡入淡出 1.8 秒")
add("ui_badge_new", 30, 19, box(0, 0, 30, 17, f"border-radius:8px;background:{CORAL};box-shadow:0 2px 0 #B44A33"),
    [24, 30, 24, 24], "「新」標籤 #E86B4F 圓角 8＋下緣 2 #B44A33；字 11 Black 白")
add("ui_icon_eye", 12, 10, EYE_SVG, None, "眼睛圖示 12×10 #8A7A66")
add("ui_circle", 20, 20, box(0, 0, 20, 20, "border-radius:50%;background:#FFFFFF"), None, "發布者頭像圓（白色，用 Image.color 上色）")

# ── 空狀態 ──────────────────────────────────────────────────────
flat("ui_glass_pill", 16, GLASS, "空狀態底 rgba(255,248,236,.72) 圓角 16")
flat("ui_pill_yellow", 15, "#F6C945", "「開啟設定」鈕 #F6C945 圓角 15")


def main():
    chrome = next((c for c in CHROME_CANDIDATES if os.path.exists(c)), None)
    if not chrome:
        sys.exit("找不到 Chrome，請安裝 Google Chrome")
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(SRC, exist_ok=True)
    manifest = []
    for name, w, h, html, border, note in S:
        doc = ('<!doctype html><html><head><meta charset="utf-8"></head>'
               f'<body style="margin:0;background:transparent;width:{w}px;height:{h}px;position:relative;overflow:hidden">'
               f'{html}</body></html>')
        src = os.path.join(SRC, name + ".html")
        with open(src, "w", encoding="utf-8") as f:
            f.write(doc)
        png = os.path.join(OUT, name + ".png")
        subprocess.run([chrome, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=3",
                        "--default-background-color=00000000", f"--window-size={w},{h}", f"--screenshot={png}",
                        "file://" + src], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        manifest.append({"file": "Sprites/" + name + ".png", "px": [w * 3, h * 3], "size_1x": [w, h],
                         "border_LBRT_px": border, "note": note})
        print("ok", name)
    with open(os.path.join(PACK, "sprites.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
