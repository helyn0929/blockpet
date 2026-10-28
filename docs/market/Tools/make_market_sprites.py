"""把設計稿「商店 A」（MarketA.dc.html）裡的家具、裝飾、擴建、寵物蛋與金幣圖示（inline SVG），
用 headless Chrome 以 3 倍解析度、透明背景存成 Unity sprite。
用法：python3 docs/market/Tools/make_market_sprites.py [檔名…]（只給檔名就只輸出那幾張，例如 item_potion）
輸出：Assets/Art/UI/Market/Sprites/*.png（檔名 furn_* / dec_* / exp_* / egg_* / item_* / ui_coin）
匯入設定由 Assets/Editor/TopBar/TopBarSpriteImporter.cs 自動套用（PPU 300）。
"""
import os, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Art", "UI", "Market", "Sprites")
CHROME_CANDIDATES = [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Google Chrome 2.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
]
SCALE = 3

# ── 設計稿 MarketA.dc.html lib() 原樣複製 ─────────────────────────────
A = {
    "lights": "<path d='M4 14 Q40 32 76 14' fill='none' stroke='#B88A5E' stroke-width='2.5'/><g transform='rotate(-8 20 30)'><rect x='10' y='22' width='20' height='24' rx='2' fill='#FFFFFF'/><rect x='12' y='24' width='16' height='14' fill='#8CC475'/></g><rect x='30' y='28' width='20' height='24' rx='2' fill='#FFFFFF'/><rect x='32' y='30' width='16' height='14' fill='#F2BE66'/><g transform='rotate(8 60 30)'><rect x='50' y='22' width='20' height='24' rx='2' fill='#FFFFFF'/><rect x='52' y='24' width='16' height='14' fill='#6F8FE0'/></g><circle cx='8' cy='16' r='3' fill='#FFD45C'/><circle cx='40' cy='24' r='3' fill='#FFD45C'/><circle cx='72' cy='16' r='3' fill='#FFD45C'/>",
    "fire": "<path d='M18 56 L62 46 M18 46 L62 56' stroke='#8B5E3C' stroke-width='7' stroke-linecap='round'/><path d='M40 6 C52 20 58 30 52 42 C48 48 32 48 28 42 C24 32 30 26 35 18 C36 27 42 27 40 6Z' fill='#F29A3A'/><path d='M40 22 C47 31 47 38 43 43 C38 46 33 42 35 36 C36 31 40 29 40 22Z' fill='#FFD45C'/>",
    "tent": "<path d='M10 56 L40 10 L70 56Z' fill='#F08A6C'/><path d='M40 10 L32 56 H48Z' fill='#FFF0DC'/><rect x='52' y='36' width='10' height='12' rx='1' fill='#FFFFFF' transform='rotate(8 57 42)'/><path d='M6 57 H74' stroke='#8B6A4A' stroke-width='3' stroke-linecap='round'/>",
    "hammock": "<path d='M10 8 V58 M70 8 V58' stroke='#8B6A4A' stroke-width='4' stroke-linecap='round'/><path d='M10 20 Q40 54 70 20' fill='none' stroke='#6F8FE0' stroke-width='7' stroke-linecap='round'/><path d='M13 23 Q40 46 67 23' fill='none' stroke='#A9BEF2' stroke-width='3'/>",
    "mat": "<path d='M4 54 L22 32 H76 L58 54Z' fill='#E86B4F'/><path d='M13 43 H67 M34 32 L16 54 M50 32 L32 54 M66 32 L48 54' stroke='#FFF8EC' stroke-width='3'/><rect x='44' y='16' width='22' height='15' rx='3' fill='#C98A4E'/><path d='M47 17 Q55 6 63 17' fill='none' stroke='#8B5E3C' stroke-width='2.5'/>",
    "easel": "<path d='M28 40 L20 60 M52 40 L60 60 M40 40 V58' stroke='#B88A5E' stroke-width='4' stroke-linecap='round'/><rect x='18' y='4' width='44' height='40' rx='3' fill='#FFFFFF'/><rect x='22' y='8' width='36' height='28' rx='2' fill='#F2BE66'/><circle cx='40' cy='22' r='7' fill='#E86B4F'/>",
    "lantern": "<path d='M40 2 V12' stroke='#3A2E22' stroke-width='2.5'/><rect x='30' y='11' width='20' height='6' rx='2' fill='#3A2E22'/><rect x='28' y='17' width='24' height='34' rx='8' fill='#FFD45C'/><rect x='32' y='21' width='16' height='26' rx='6' fill='#FFF3C4'/><rect x='30' y='51' width='20' height='6' rx='2' fill='#3A2E22'/>",
    "screen": "<rect x='14' y='6' width='52' height='36' rx='3' fill='#FFFDF8'/><rect x='18' y='10' width='44' height='28' fill='#D6ECF6'/><path d='M18 34 L32 22 L42 30 L50 24 L62 33 V38 H18Z' fill='#6FA45F'/><path d='M24 42 L18 60 M56 42 L62 60' stroke='#8B6A4A' stroke-width='3.5' stroke-linecap='round'/>",
    "mailbox": "<path d='M40 34 V60' stroke='#8B6A4A' stroke-width='5' stroke-linecap='round'/><path d='M22 36 V20 a12 12 0 0 1 12 -12 H48 a12 12 0 0 1 12 12 V36Z' fill='#6F8FE0'/><path d='M34 8 a12 12 0 0 1 12 12 V36' fill='none' stroke='#4F6FC4' stroke-width='2.5'/><path d='M58 14 V4 H66 V9 H58' fill='#E86B4F'/>",
    "lake": "<ellipse cx='40' cy='46' rx='35' ry='14' fill='#9ED0E8'/><ellipse cx='33' cy='43' rx='14' ry='4' fill='#C9E6F3'/><circle cx='15' cy='26' r='12' fill='#6FA45F'/><circle cx='64' cy='22' r='14' fill='#7BB06A'/>",
    "tree": "<rect x='36' y='34' width='8' height='28' fill='#8B5E3C'/><circle cx='40' cy='22' r='21' fill='#6FA45F'/><rect x='26' y='24' width='28' height='18' rx='2' fill='#C98A4E'/><path d='M22 26 L40 11 L58 26Z' fill='#E86B4F'/><rect x='36' y='31' width='8' height='11' fill='#3A2E22'/>",
    "flower": "<path d='M0 46 Q40 26 80 46 V64 H0Z' fill='#8CC475'/><circle cx='16' cy='48' r='4' fill='#F06B5B'/><circle cx='30' cy='42' r='4' fill='#FFD45C'/><circle cx='46' cy='44' r='4' fill='#D78AD0'/><circle cx='60' cy='50' r='4' fill='#FFFFFF'/><circle cx='24' cy='56' r='4' fill='#FFD45C'/><circle cx='52' cy='57' r='4' fill='#F06B5B'/>",
}
D = {
    "cup": "<path d='M22 22 H54 L50 54 H26Z' fill='#FFFFFF'/><path d='M22 22 H54 V29 H22Z' fill='#C98A4E'/><path d='M54 30 Q66 32 62 42 Q60 48 52 46' fill='none' stroke='#FFFFFF' stroke-width='5'/><path d='M32 15 Q36 9 32 3 M44 15 Q48 9 44 3' stroke='#D9CFBD' stroke-width='3' fill='none' stroke-linecap='round'/>",
    "ramen": "<path d='M8 28 H72 Q70 56 40 58 Q10 56 8 28Z' fill='#F29A3A'/><path d='M8 28 H72' stroke='#C2621A' stroke-width='4'/><path d='M20 26 Q28 16 36 26 Q44 16 52 26' fill='none' stroke='#FFE3A0' stroke-width='4'/><path d='M50 4 L60 26 M58 4 L66 26' stroke='#8B5E3C' stroke-width='3' stroke-linecap='round'/><path d='M22 41 H58' stroke='#FFFFFF' stroke-width='3' stroke-dasharray='4 5'/>",
    "leaf": "<path d='M40 60 C14 46 14 16 40 4 C66 16 66 46 40 60Z' fill='#6FA45F'/><path d='M40 60 V10 M40 30 L28 22 M40 40 L52 32' stroke='#A6D58C' stroke-width='3' stroke-linecap='round' fill='none'/>",
    "flower": "<g fill='#F06B5B'><circle cx='40' cy='16' r='11'/><circle cx='56' cy='30' r='11'/><circle cx='50' cy='48' r='11'/><circle cx='30' cy='48' r='11'/><circle cx='24' cy='30' r='11'/></g><circle cx='40' cy='33' r='9' fill='#FFD45C'/>",
    "cone": "<path d='M40 4 L58 52 H22Z' fill='#F29A3A'/><path d='M33 22 H47 L50 32 H30Z M27 40 H53 L55 46 H25Z' fill='#FFFFFF'/><rect x='14' y='52' width='52' height='8' rx='3' fill='#6F8FE0'/>",
    "party": "<path d='M40 4 L60 56 H20Z' fill='#D78AD0'/><path d='M30 30 L50 22 M25 44 L55 32' stroke='#FFFFFF' stroke-width='4'/><circle cx='40' cy='5' r='5' fill='#FFD45C'/><rect x='18' y='54' width='44' height='6' rx='3' fill='#B86AB0'/>",
    "band": "<path d='M8 46 Q40 18 72 46' fill='none' stroke='#E8C33A' stroke-width='12' stroke-linecap='round'/><path d='M10 45 Q40 20 70 45' fill='none' stroke='#FFFFFF' stroke-width='3' stroke-dasharray='6 6'/><circle cx='40' cy='31' r='8' fill='#E86B4F'/>",
    "star": "<path d='M40 4 l10 20 22 3 -16 15 4 22 -20 -11 -20 11 4 -22 -16 -15 22 -3Z' fill='#FFD45C' stroke='#D9A92C' stroke-width='3' stroke-linejoin='round'/>",
}


def egg(c, d):
    return ("<path d='M40 4 C57 4 68 30 68 44 C68 58 55 63 40 63 C25 63 12 58 12 44 C12 30 23 4 40 4Z' fill='" + c + "'/>"
            + d + "<ellipse cx='30' cy='22' rx='5' ry='8' fill='#FFFFFF' opacity='0.45'/>")


E = {
    "forest": egg("#A6D58C", "<circle cx='50' cy='30' r='5' fill='#6FA45F'/><circle cx='32' cy='46' r='6' fill='#6FA45F'/><circle cx='52' cy='50' r='4' fill='#6FA45F'/>"),
    "city": egg("#A9BEF2", "<path d='M14 36 H66 M12 46 H68' stroke='#6F8FE0' stroke-width='5'/>"),
    "star": egg("#C9A0E0", "<path d='M50 26 l2.5 5 5.5 .8 -4 3.9 1 5.5 -5 -2.6 -5 2.6 1 -5.5 -4 -3.9 5.5 -.8Z' fill='#FFD45C'/><circle cx='30' cy='46' r='3' fill='#FFD45C'/><circle cx='48' cy='52' r='2.5' fill='#FFD45C'/>"),
}
# 道具（設計稿沒有，照同樣畫風補畫）
I = {
    "potion": "<rect x='32' y='2' width='16' height='8' rx='2.5' fill='#C98A4E'/><rect x='34' y='9' width='12' height='12' fill='#DDEEF5'/>"
              "<circle cx='40' cy='40' r='22' fill='#E8F4FA'/>"
              "<path d='M18.5 38 Q29 33 40 38 T61.5 38 A21.5 21.5 0 0 1 18.5 38Z' fill='#F06B8B'/>"
              "<path d='M40 55 C31 49 29 44 33 41 C36 39 39 41 40 43 C41 41 44 39 47 41 C51 44 49 49 40 55Z' fill='#FFFFFF'/>"
              "<ellipse cx='30' cy='30' rx='4' ry='7' fill='#FFFFFF' opacity='0.7' transform='rotate(25 30 30)'/>"
              "<circle cx='40' cy='40' r='22' fill='none' stroke='#B9D3E0' stroke-width='2.5'/>",
}
COIN = "<circle cx='8' cy='8' r='7.5' fill='#FFD54F'/><circle cx='8' cy='8' r='4.6' fill='none' stroke='#E0A21E' stroke-width='1.6'/>"

# (輸出檔名, viewBox 寬, 高, SVG 內容)
SPRITES = []
for k in ["lights", "fire", "tent", "hammock", "mat", "easel", "lantern", "mailbox", "screen"]:
    SPRITES.append(("furn_" + k, 80, 64, A[k]))
for k in ["lake", "tree", "flower"]:
    SPRITES.append(("exp_" + k, 80, 64, A[k]))
for k in D:
    SPRITES.append(("dec_" + k, 80, 64, D[k]))
for k in E:
    SPRITES.append(("egg_" + k, 80, 66, E[k]))
for k in I:
    SPRITES.append(("item_" + k, 80, 64, I[k]))
SPRITES.append(("ui_coin", 16, 16, COIN))


def main():
    chrome = next((c for c in CHROME_CANDIDATES if os.path.exists(c)), None)
    if chrome is None:
        sys.exit("找不到 Google Chrome")
    os.makedirs(OUT, exist_ok=True)
    tmp = tempfile.mkdtemp(prefix="market_sprites_")
    only = set(sys.argv[1:])
    for name, w, h, body in SPRITES:
        if only and name not in only:
            continue
        html = ("<!doctype html><html><head><style>html,body{margin:0;background:transparent}</style></head><body>"
                f"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}' style='display:block'>"
                f"{body}</svg></body></html>")
        src = os.path.join(tmp, name + ".html")
        with open(src, "w", encoding="utf-8") as f:
            f.write(html)
        png = os.path.join(OUT, name + ".png")
        subprocess.run([chrome, "--headless=new", "--disable-gpu", "--hide-scrollbars", f"--force-device-scale-factor={SCALE}",
                        "--default-background-color=00000000", f"--window-size={w},{h}", f"--screenshot={png}",
                        "file://" + src], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print("ok", name)


if __name__ == "__main__":
    main()
