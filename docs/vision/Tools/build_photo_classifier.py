"""照片辨識：把 TinyCLIP（Microsoft，MIT 授權）的影像模型轉成 ONNX 給 Unity Sentis 用，並算好五大類的文字向量。

做法（看圖說話）：照片 → 影像模型 → 向量；和每一句描述的文字向量比相似度，每類取最像的那一句當分數，最高的那類就是結果。
遊戲只帶影像模型，文字向量事先在這裡算好（改分類只要改 CATEGORIES 再重跑，模型不用動）。

需要：python3.13 venv，pip install torch transformers onnx onnxruntime pillow numpy
模型：https://huggingface.co/wkcn/<MODEL>（config.json、model.safetensors、tokenizer.json、preprocessor_config.json）

用法：
  python3 docs/vision/Tools/build_photo_classifier.py MODEL_DIR                 # 輸出模型與類別向量到 Unity 專案
  python3 docs/vision/Tools/build_photo_classifier.py MODEL_DIR --json-only     # 只重算類別向量（改了 CATEGORIES 時）
  python3 docs/vision/Tools/build_photo_classifier.py MODEL_DIR --eval DIR…    # 只用測試照片看分類結果（不寫入專案）
輸出：
  Assets/Resources/Vision/photo_classifier_vision.onnx   影像模型（輸入 pixel_values [1,3,224,224]，輸出 image_embeds [1,D]）
  Assets/Resources/Vision/photo_categories.json          每類每句一個單位向量、分數倍率、前處理參數（遊戲執行時自動載入）
"""
import glob, json, os, sys
import numpy as np
import torch
from PIL import Image, ImageDraw
from transformers import CLIPModel, CLIPTokenizerFast

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
ONNX_OUT = os.path.join(ROOT, "Assets", "Resources", "Vision", "photo_classifier_vision.onnx")
JSON_OUT = os.path.join(ROOT, "Assets", "Resources", "Vision", "photo_categories.json")

# 每類幾句英文描述（CLIP 是用英文訓練的），向量取平均。分錯就在這裡加句子。
# 2026-10-29：運動戶外併入自然，多一類「寵物」（家裡養的貓狗等；野生動物仍算自然）。
# 改了類別或句子要把 VERSION 加一：遊戲會把舊版本辨識過的照片重新辨識。
VERSION = "2"
CATEGORIES = [
    ("美食", ["a photo of food", "a photo of a meal on a plate", "a photo of a drink or a cup of coffee",
             "a photo of dessert, cake or snacks", "a photo of a dish at a restaurant", "a photo of noodles or rice",
             "a photo of fruit or vegetables"]),
    ("自然", ["a photo of nature", "a photo of mountains or a forest", "a photo of the sea or a beach",
             "a photo of flowers or plants", "a photo of the sky, clouds or a sunset", "a photo of a lake or a river",
             "a photo of a landscape",
             "a photo of wild animals in nature", "a photo of a zebra, a bear or a bird in the wild",
             # 原「運動戶外」
             "a photo of people playing sports", "a photo of running or hiking", "a photo of a gym workout",
             "a photo of cycling or swimming", "a photo of camping outdoors", "a photo of a ball game or a sports field",
             "a photo of skiing or surfing"]),
    ("城市", ["a photo of a city street", "a photo of buildings", "a photo of a city at night",
             "a photo of a shop front or a train station", "a photo of cars and roads", "a photo of a bridge in a city",
             "a photo of architecture"]),
    ("生活人物", ["a photo of a person", "a selfie", "a photo of friends or family together",
               "a photo of everyday life at home", "a photo of a baby or children",
               "a photo of a room interior"]),
    ("寵物", ["a photo of a pet", "a photo of a pet dog", "a photo of a pet cat", "a photo of a puppy or a kitten",
             "a photo of a person holding a cat or a dog", "a photo of a pet rabbit or hamster",
             "a photo of a pet bird or fish"]),
]


class VisionEmbeds(torch.nn.Module):
    """影像塔＋投影層：pixel_values → image_embeds（還沒正規化，遊戲端再除以長度）。"""

    def __init__(self, clip):
        super().__init__()
        self.vision = clip.vision_model
        self.proj = clip.visual_projection

    def forward(self, pixel_values):
        return self.proj(self.vision(pixel_values=pixel_values).pooler_output)


def preprocess(path, size, mean, std):
    # 與遊戲相同：短邊縮到 size、置中裁成正方形、除以 255、減 mean 除 std
    img = Image.open(path).convert("RGB")
    w, h = img.size
    s = size / min(w, h)
    img = img.resize((max(size, round(w * s)), max(size, round(h * s))), Image.BICUBIC)
    w, h = img.size
    l, t = (w - size) // 2, (h - size) // 2
    img = img.crop((l, t, l + size, t + size))
    x = np.asarray(img, dtype=np.float32) / 255.0
    x = (x - np.array(mean, np.float32)) / np.array(std, np.float32)
    return x.transpose(2, 0, 1)[None]


def main():
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__)
    model_dir = args[0]
    eval_dirs = args[args.index("--eval") + 1:] if "--eval" in args else []
    json_only = "--json-only" in args

    clip = CLIPModel.from_pretrained(model_dir).eval()
    tok = CLIPTokenizerFast.from_pretrained(model_dir)
    pp = json.load(open(os.path.join(model_dir, "preprocessor_config.json")))
    size = pp["crop_size"] if isinstance(pp["crop_size"], int) else pp["crop_size"]["height"]
    mean, std = pp["image_mean"], pp["image_std"]
    scale = float(clip.logit_scale.exp().item())

    with torch.no_grad():
        cats = []
        for name, sentences in CATEGORIES:
            t = tok(sentences, padding=True, return_tensors="pt")
            f = clip.text_projection(clip.text_model(**t).pooler_output)
            f = f / f.norm(dim=-1, keepdim=True)
            cats.append((name, sentences, f.numpy()))  # 每句一個單位向量

    if eval_dirs:
        evaluate(clip, cats, scale, size, mean, std, eval_dirs)
        return

    # 輸出 ONNX（opset 15，Sentis 支援）；只改類別句子時加 --json-only，模型檔不用重寫
    if not json_only:
        os.makedirs(os.path.dirname(ONNX_OUT), exist_ok=True)
        dummy = torch.zeros(1, 3, size, size)
        torch.onnx.export(VisionEmbeds(clip).eval(), dummy, ONNX_OUT, input_names=["pixel_values"], output_names=["image_embeds"],
                          opset_version=15, do_constant_folding=True, dynamo=False)
    os.makedirs(os.path.dirname(JSON_OUT), exist_ok=True)
    with open(JSON_OUT, "w", encoding="utf-8") as f:
        json.dump({
            "model": os.path.basename(os.path.normpath(model_dir)),
            "version": VERSION,
            "inputSize": size, "mean": mean, "std": std, "logitScale": round(scale, 4),
            # 每類每句一個向量（扁平：句數 × dim），遊戲端每類取最像的一句
            "dim": int(cats[0][2].shape[1]),
            "categories": [{"name": n, "prompts": p, "embeddings": [round(float(x), 6) for x in v.reshape(-1)]} for n, p, v in cats],
        }, f, ensure_ascii=False)
    if not json_only:
        print("寫入", ONNX_OUT, os.path.getsize(ONNX_OUT) // 1024, "KB")
    print("寫入", JSON_OUT)


def category_probs(e, cats, scale):
    """每類取最像的一句（最大餘弦相似度），再乘倍率取 softmax。"""
    logits = np.array([scale * float((v @ e).max()) for _, _, v in cats])
    prob = np.exp(logits - logits.max())
    return prob / prob.sum()


def evaluate(clip, cats, scale, size, mean, std, dirs):
    paths = [p for d in dirs for p in sorted(glob.glob(os.path.join(d, "*"))) if p.lower().endswith((".jpg", ".jpeg", ".png"))]
    labels = ["food", "nature", "city", "people", "pet"]
    tiles = []
    counts = {c[0]: 0 for c in cats}
    with torch.no_grad():
        for p in paths:
            x = torch.from_numpy(preprocess(p, size, mean, std))
            e = VisionEmbeds(clip)(x)[0].numpy()
            e = e / np.linalg.norm(e)
            prob = category_probs(e, cats, scale)
            k = int(prob.argmax())
            counts[cats[k][0]] += 1
            print(f"{cats[k][0]:<6} {prob[k]:.2f}  {os.path.basename(p)}")
            tiles.append((p, f"{labels[k]} {prob[k]:.2f}"))
    print(counts)
    # 縮圖總覽（方便人工檢查）
    cols, tw, th = 8, 160, 140
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * tw, rows * th), "white")
    draw = ImageDraw.Draw(sheet)
    for i, (p, label) in enumerate(tiles):
        im = Image.open(p).convert("RGB")
        im.thumbnail((tw - 6, th - 22))
        x, y = (i % cols) * tw, (i // cols) * th
        sheet.paste(im, (x + 3, y + 3))
        draw.text((x + 4, y + th - 18), label, fill="black")
    out = os.path.join(os.path.dirname(dirs[0].rstrip("/")), "eval_sheet.jpg")
    sheet.save(out, quality=85)
    print("總覽", out)


if __name__ == "__main__":
    main()
