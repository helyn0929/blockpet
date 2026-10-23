"""聊天室按的 emoji（設計稿 Chat.dc.html 的 ❤️ 😂 😋 😮 👍 🐾）用 headless Chrome 的系統彩色 emoji 字型
以 3 倍解析度、透明背景存成 Unity sprite（TextMeshPro 的字型沒有彩色 emoji）。
用法：python3 docs/chat/Tools/make_emoji_sprites.py
輸出：Assets/Art/UI/Chat/Sprites/emoji_{key}.png（key 與 Firebase 的 reactions key 相同）
"""
import os, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Art", "UI", "Chat", "Sprites")
CHROME_CANDIDATES = [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Google Chrome 2.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
]
SIZE = 32
SCALE = 3
EMOJI = [("heart", "❤️"), ("laugh", "\U0001F602"), ("yum", "\U0001F60B"),
         ("wow", "\U0001F62E"), ("like", "\U0001F44D"), ("paw", "\U0001F43E")]


def main():
    chrome = next((c for c in CHROME_CANDIDATES if os.path.exists(c)), None)
    if chrome is None:
        sys.exit("找不到 Google Chrome")
    os.makedirs(OUT, exist_ok=True)
    tmp = tempfile.mkdtemp(prefix="emoji_sprites_")
    for key, ch in EMOJI:
        html = ("<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;background:transparent}"
                f"div{{width:{SIZE}px;height:{SIZE}px;display:flex;align-items:center;justify-content:center;"
                f"font-size:{SIZE - 4}px;line-height:1;font-family:'Apple Color Emoji','Noto Color Emoji',sans-serif}}"
                f"</style></head><body><div>{ch}</div></body></html>")
        src = os.path.join(tmp, key + ".html")
        with open(src, "w", encoding="utf-8") as f:
            f.write(html)
        png = os.path.join(OUT, "emoji_" + key + ".png")
        subprocess.run([chrome, "--headless=new", "--disable-gpu", "--hide-scrollbars", f"--force-device-scale-factor={SCALE}",
                        "--default-background-color=00000000", f"--window-size={SIZE},{SIZE}", f"--screenshot={png}",
                        "file://" + src], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print("ok", key)


if __name__ == "__main__":
    main()
