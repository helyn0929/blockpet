"""Cut Unity sprites out of the artifact's own CSS (Camp8 top bar), rendered by Chromium at @3x
with a transparent background, so every sprite looks exactly like the design artifact."""
import json, os, sys
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
PACK = os.path.join(HERE, "..")
SPR = os.path.join(PACK, "Sprites")
SVG = os.path.join(PACK, "Source")
os.makedirs(SPR, exist_ok=True)
os.makedirs(SVG, exist_ok=True)

HEART = "M8 13 1.6 6.6A3.6 3.6 0 0 1 8 2.4a3.6 3.6 0 0 1 6.4 4.2Z"
CHEVRON = '<path d="m4 6 4 4 4-4"></path>'
ICON_TRAVEL = '<rect x="3" y="7" width="18" height="13" rx="3"></rect><path d="M9 7V5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2M3 12h18"></path><path d="M10 12v2h4v-2" fill="#3A2E22"></path>'
ICON_DEX = '<path d="M4 5a2 2 0 0 1 2-2h12v16H6a2 2 0 0 0-2 2Z"></path><path d="M4 21V5M18 19v2H6"></path><circle cx="8" cy="8.4" r="1.2" fill="#3A2E22"></circle><circle cx="11" cy="7.4" r="1.2" fill="#3A2E22"></circle><circle cx="14" cy="8.4" r="1.2" fill="#3A2E22"></circle><path d="M8.6 12.6c0-1.4 1.1-2.2 2.4-2.2s2.4.8 2.4 2.2-1.2 1.8-2.4 1.8-2.4-.4-2.4-1.8Z" fill="#3A2E22"></path>'
ICON_SET = '<circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z"></path>'

# name: (canvas w,h @1x, inner html, 9-slice border @3x [L,B,R,T] or None, note)
S = {}
def add(name, w, h, html, border=None, note=""):
    S[name] = (w, h, html, border, note)

pad = 24  # shadow padding @1x
# --- top bar panel ---
add("ui_topbar_panel", 60, 58,
    '<div style="position:absolute;left:0;top:0;width:60px;height:58px;box-sizing:border-box;border-radius:29px;'
    'background:rgba(255,248,236,0.62);border:1.5px solid rgba(255,255,255,0.65)"></div>',
    [87, 87, 87, 87], "狀態欄底板 370×58、圓角 29。設計稿另有 backdrop-filter: blur(8px)（PNG 無法內含，見 README）")
add("ui_topbar_panel_shadow", 60 + pad * 2, 58 + pad * 2,
    f'<div style="position:absolute;left:{pad}px;top:{pad}px;width:60px;height:58px;border-radius:29px;'
    'box-shadow:0 6px 16px rgba(30,40,20,0.18)"></div>',
    [159, 159, 159, 159], "狀態欄陰影：0 6 16 rgba(30,40,20,.18)；放在底板下層，四邊各外擴 24")
# --- avatar ---
add("ui_avatar_bg", 44, 44,
    '<div style="position:absolute;left:0;top:0;width:44px;height:44px;border-radius:50%;background:#FCE7A6"></div>',
    None, "頭像底 44×44 #FCE7A6；寵物圖 40×34（contain）置中疊上，超出圓的部分用遮罩裁掉")
add("ui_avatar_mask", 44, 44,
    '<div style="position:absolute;left:0;top:0;width:44px;height:44px;border-radius:50%;background:#FFFFFF"></div>',
    None, "頭像圓形遮罩")
add("ui_lv_badge", 20, 15,
    '<div style="position:absolute;left:0;top:0;width:20px;height:15px;box-sizing:border-box;border-radius:6px;'
    'background:#3A2E22;border:1.5px solid #FFF8EC"></div>',
    [18, 18, 18, 18], "Lv 徽章底，9-slice；寬 = 字寬 + 13；字 9px Black #FFF8EC")
# --- hearts ---
for nm, col in (("full", "#F06B5B"), ("empty", "#E6D9C3")):
    add(f"ui_heart_{nm}", 15, 14,
        f'<svg style="position:absolute;left:0;top:0" width="15" height="14" viewBox="0 0 16 14"><path d="{HEART}" fill="{col}"></path></svg>',
        None, f"愛心 15×14：{'滿' if nm=='full' else '空'} {col}")
add("ui_heart_half", 15, 14,
    f'<svg style="position:absolute;left:0;top:0" width="15" height="14" viewBox="0 0 16 14"><defs><clipPath id="l"><rect x="0" y="0" width="8" height="14"/></clipPath></defs>'
    f'<path d="{HEART}" fill="#E6D9C3"></path><path d="{HEART}" fill="#F06B5B" clip-path="url(#l)"></path></svg>',
    None, "補充：半顆心（設計稿只有滿／空，但規則是以半顆為單位）")
# --- want chip ---
add("ui_pill_17", 34, 17,
    '<div style="position:absolute;left:0;top:0;width:34px;height:17px;border-radius:999px;background:#FFFFFF"></div>',
    [25, 25, 25, 25], "「想吃」膠囊，白色 9-slice，用 Image.color 上類別底色；高 17")
# --- evo bar ---
add("ui_evo_track", 24, 8,
    '<div style="position:absolute;left:0;top:0;width:24px;height:8px;border-radius:999px;background:rgba(255,255,255,0.55)"></div>',
    [12, 12, 12, 12], "進化條軌道 高 8，白 55%")
add("ui_evo_mask", 24, 8,
    '<div style="position:absolute;left:0;top:0;width:24px;height:8px;border-radius:999px;background:#FFFFFF"></div>',
    [12, 12, 12, 12], "進化條遮罩（Mask），色段放在裡面")
add("ui_evo_segment", 4, 4,
    '<div style="position:absolute;left:0;top:0;width:4px;height:4px;background:#FFFFFF"></div>',
    None, "色段，白色；Image.color 上類別色")
add("ui_evo_ready_glow", 24 + 28, 8 + 28,
    '<div style="position:absolute;left:14px;top:14px;width:24px;height:8px;border-radius:999px;'
    'box-shadow:0 0 0 2px #F6C945, 0 0 10px rgba(246,201,69,0.8)"></div>',
    [54, 54, 54, 54], "補充：滿條光框（設計稿的金色 #F6C945 發光樣式），四邊外擴 14，呼吸 1.8s")
# --- coin ---
add("ui_coin_pill", 40, 30,
    '<div style="position:absolute;left:0;top:0;width:40px;height:30px;box-sizing:border-box;border-radius:15px;'
    'background:rgba(255,255,255,0.75);border:1.5px solid rgba(255,255,255,0.9)"></div>',
    [45, 45, 45, 45], "金幣按鈕底 高 30，9-slice")
add("ui_coin_dot", 14, 14,
    '<span style="position:absolute;left:0;top:0;width:14px;height:14px;border-radius:50%;background:#FFD54F;box-shadow:inset -2px -2px 0 #E0A21E"></span>',
    None, "金幣圖示 14×14")
add("ui_coin_plus", 22, 24,
    '<span style="position:absolute;left:0;top:0;width:22px;height:22px;border-radius:50%;background:#F6C945;box-shadow:0 2px 0 #D9A92C;'
    'display:flex;align-items:center;justify-content:center"><svg width="11" height="11" viewBox="0 0 12 12" fill="none" stroke="#3A2E22" '
    'stroke-width="2.4" stroke-linecap="round"><path d="M6 2v8M2 6h8"></path></svg></span>',
    None, "金幣右側＋按鈕 22×22（下緣含 2px 立體陰影，所以圖高 24）")
# --- menu toggle ---
mpad = 12
for nm, bg in (("closed", "rgba(255,248,236,0.62)"), ("open", "rgba(246,201,69,0.9)")):
    add(f"ui_menu_btn_{nm}", 46 + mpad * 2, 32 + mpad * 2,
        f'<div style="position:absolute;left:{mpad}px;top:{mpad}px;width:46px;height:32px;box-sizing:border-box;border-radius:16px;'
        f'border:1.5px solid rgba(255,255,255,0.8);background:{bg};box-shadow:0 3px 8px rgba(30,40,20,0.22)"></div>',
        None, f"右上選單按鈕底（{'收合' if nm=='closed' else '展開'}）46×32，含陰影，四邊外擴 12")
add("ui_icon_chevron", 16, 16,
    f'<svg style="position:absolute;left:0;top:0" width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="#3A2E22" stroke-width="2.4" '
    f'stroke-linecap="round" stroke-linejoin="round">{CHEVRON}</svg>',
    None, "選單箭頭 16×16；展開時旋轉 180°（0.25s）")
add("ui_menu_tile", 46 + mpad * 2, 46 + mpad * 2,
    f'<div style="position:absolute;left:{mpad}px;top:{mpad}px;width:46px;height:46px;box-sizing:border-box;border-radius:15px;'
    'background:rgba(255,248,236,0.72);border:1.5px solid rgba(255,255,255,0.8);box-shadow:0 3px 8px rgba(30,40,20,0.22)"></div>',
    None, "選單項目方塊 46×46，含陰影，四邊外擴 12")
for nm, body in (("travel", ICON_TRAVEL), ("dex", ICON_DEX), ("settings", ICON_SET)):
    add(f"ui_icon_{nm}", 22, 22,
        f'<svg style="position:absolute;left:0;top:0" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#3A2E22" stroke-width="2" '
        f'stroke-linecap="round" stroke-linejoin="round">{body}</svg>',
        None, {"travel": "旅行", "dex": "圖鑑", "settings": "設定"}[nm] + "圖示 22×22")

if __name__ == "__main__":
    meta = []
    with sync_playwright() as p:
        b = p.chromium.launch()
        pg = b.new_page(device_scale_factor=3)
        for name, (w, h, html, border, note) in S.items():
            pg.set_viewport_size({"width": w, "height": h})
            doc = f'<!doctype html><html><head><meta charset="utf-8"><style>html,body{{margin:0;background:transparent}}</style></head><body><div style="position:relative;width:{w}px;height:{h}px">{html}</div></body></html>'
            pg.set_content(doc)
            pg.screenshot(path=os.path.join(SPR, name + ".png"), omit_background=True, clip={"x": 0, "y": 0, "width": w, "height": h})
            # editable source: same markup wrapped as SVG foreignObject is fragile in design tools, so keep plain HTML source
            with open(os.path.join(SVG, name + ".html"), "w") as f:
                f.write(doc)
            meta.append({"file": f"Sprites/{name}.png", "px": [w * 3, h * 3], "size_1x": [w, h], "border_LBRT_px": border, "note": note})
        b.close()
    from PIL import Image
    for f in os.listdir(SPR):
        im = Image.open(os.path.join(SPR, f)); im.convert("RGBA").save(os.path.join(SPR, f))
    json.dump(meta, open(os.path.join(PACK, "sprites.json"), "w"), ensure_ascii=False, indent=1)
    print(len(meta), "sprites")
