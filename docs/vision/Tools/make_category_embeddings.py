"""照片五大類（美食、自然、城市、生活人物、運動戶外）的 MobileCLIP 文字向量，事先在電腦上算好給遊戲用。
遊戲只帶影像模型（Assets/Models/mobileclip_s0_vision.onnx），照片向量和這些文字向量比相似度就知道是哪一類。

用法（需要 onnxruntime、numpy、tokenizers、pillow）：
  python3 docs/vision/Tools/make_category_embeddings.py MODEL_DIR [照片資料夾…]
  MODEL_DIR = 從 https://huggingface.co/Xenova/mobileclip_s0 下載的資料夾（含 onnx/text_model.onnx、onnx/vision_model.onnx、tokenizer.json）
輸出：Assets/Data/Vision/photo_categories.json（每類一個 512 維單位向量＋分數倍率）
給了照片資料夾時，會用影像模型把裡面的照片分類並印出結果，方便檢查句子寫得好不好。
"""
import glob, json, os, sys
import numpy as np
import onnxruntime as ort
from tokenizers import Tokenizer
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Data", "Vision", "photo_categories.json")

# 每類幾句英文描述（CLIP 是用英文訓練的），向量取平均。要調整分類就改這裡再重跑。
CATEGORIES = [
    ("美食", ["a photo of food", "a photo of a meal on a plate", "a photo of a drink or a cup of coffee",
             "a photo of dessert, cake or snacks", "a photo of a dish at a restaurant", "a photo of noodles or rice"]),
    ("自然", ["a photo of nature", "a photo of mountains or a forest", "a photo of the sea or a beach",
             "a photo of flowers or plants", "a photo of the sky, clouds or a sunset", "a photo of a park with trees"]),
    ("城市", ["a photo of a city street", "a photo of buildings", "a photo of a city at night",
             "a photo of a shop front or a train station", "a photo of cars and roads", "a photo of an interior of a mall or office"]),
    ("生活人物", ["a photo of a person", "a selfie", "a photo of friends or family together",
               "a photo of a pet cat or dog", "a photo of everyday life at home", "a photo of a baby or children"]),
    ("運動戶外", ["a photo of people playing sports", "a photo of running or hiking", "a photo of a gym workout",
               "a photo of cycling or swimming", "a photo of camping outdoors", "a photo of a ball game or a sports field"]),
]
LOGIT_SCALE = 100.0  # CLIP 的溫度：相似度 × 100 再取 softmax
SEQ_LEN = 77


def l2(v):
    return v / np.linalg.norm(v, axis=-1, keepdims=True)


def text_embeds(model_dir, sentences):
    tok = Tokenizer.from_file(os.path.join(model_dir, "tokenizer.json"))
    tok.enable_padding(length=SEQ_LEN, pad_id=0, pad_token="!")
    tok.enable_truncation(max_length=SEQ_LEN)
    ids = np.array([e.ids for e in tok.encode_batch(sentences)], dtype=np.int64)
    sess = ort.InferenceSession(os.path.join(model_dir, "onnx", "text_model.onnx"), providers=["CPUExecutionProvider"])
    return l2(sess.run(None, {"input_ids": ids})[0])


def preprocess(path):
    # 與遊戲相同：短邊縮到 256、置中裁 256×256、0～1、不做 normalize（preprocessor_config.json）
    img = Image.open(path).convert("RGB")
    w, h = img.size
    s = 256 / min(w, h)
    img = img.resize((max(256, round(w * s)), max(256, round(h * s))), Image.BICUBIC)
    w, h = img.size
    l, t = (w - 256) // 2, (h - 256) // 2
    img = img.crop((l, t, l + 256, t + 256))
    return (np.asarray(img, dtype=np.float32) / 255.0).transpose(2, 0, 1)[None]


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    model_dir = sys.argv[1]
    cats = []
    for name, sentences in CATEGORIES:
        v = l2(text_embeds(model_dir, sentences).mean(axis=0))
        cats.append({"name": name, "prompts": sentences, "embedding": [round(float(x), 6) for x in v]})
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump({"model": "mobileclip_s0", "logitScale": LOGIT_SCALE, "categories": cats}, f, ensure_ascii=False)
    print("寫入", OUT)

    folders = sys.argv[2:]
    if not folders:
        return
    sess = ort.InferenceSession(os.path.join(model_dir, "onnx", "vision_model.onnx"), providers=["CPUExecutionProvider"])
    mat = np.array([c["embedding"] for c in cats], dtype=np.float32)
    counts = {c["name"]: 0 for c in cats}
    for folder in folders:
        for p in sorted(glob.glob(os.path.join(folder, "*"))):
            if not p.lower().endswith((".jpg", ".jpeg", ".png")):
                continue
            emb = l2(sess.run(None, {"pixel_values": preprocess(p)})[0])[0]
            logits = LOGIT_SCALE * mat @ emb
            prob = np.exp(logits - logits.max())
            prob /= prob.sum()
            k = int(prob.argmax())
            counts[cats[k]["name"]] += 1
            print(f"{cats[k]['name']:<6} {prob[k]:.2f}  {os.path.basename(p)}")
    print(counts)


if __name__ == "__main__":
    main()
