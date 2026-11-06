using System;
using System.Collections.Generic;
using BlockPet.Home;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.Hand
{
    /// <summary>
    /// 首頁 v9 的照片貼紙（設計文件 5.25 ④；規則在 <see cref="StickerRules"/>）：
    /// - 其他成員發的、我還沒撕掉的照片變成貼紙散在營地上（最新的 8 張，多的顯示 +N；大小 56～96，可以蓋到寵物）；點一張撕一張（記為看過）。
    ///   看過的記在手機上（PlayerPrefs，每個空間、每個帳號各自一份）；第一次用 v9 之前的照片都當作看過。
    /// - 全部撕完 → 寵物泡泡「乾淨了！」。
    /// - 營地最新一張（含自己的）一直黏著、撕不掉；點了開分割畫面；可以拖到任何地方，位置記在手機上。
    ///   有新照片時換掉並播「啪」的貼上動畫；被換下來的那張如果還沒看過，就變成一般貼紙。
    /// </summary>
    public partial class HandController
    {
        const string PrefsSeen = "BlockPet.Home9.Seen.";        // + 空間 + "." + uid
        const string PrefsSince = "BlockPet.Home9.Since.";      // 第一次用 v9 的時間（ticks）
        const string PrefsLatestSticker = "BlockPet.Home9.LatestPos";
        const int MaxSeenIds = 400;
        const float LatestW = 88f, LatestH = 92f;
        static readonly Vector2 LatestDefault = new Vector2(284f, 470f);
        static readonly Color MoreBg = Hex(0x3A2E22);
        static readonly Color[] AvatarColors = { Hex(0xE88AA8), Hex(0x6F8FE0), Hex(0x6FA45F), Hex(0xF29A3A) };

        class Sticker
        {
            public string id;
            public RectTransform holder;   // 位置＋歪的角度
            public RectTransform body;     // 動畫
            public CanvasGroup group;
            public RawImage photo;
            public Texture2D texture;
            public float time;
            public bool peeling;
        }

        RectTransform _stickerLayer;
        readonly Dictionary<string, Sticker> _stickers = new Dictionary<string, Sticker>();
        RectTransform _more;
        TMP_Text _moreText;
        float _nextStickerPoll;
        string _seenOwner;
        readonly List<string> _seenList = new List<string>();
        readonly HashSet<string> _seen = new HashSet<string>();
        long _since;

        // 最新照片貼紙
        RectTransform _latest;
        RectTransform _latestBody;
        CanvasGroup _latestGroup;
        RawImage _latestPhoto;
        Texture2D _latestTexture;
        string _latestId;
        RectTransform _latestAvatar;
        TMP_Text _latestInitial;
        Image _latestAvatarBg;
        AvatarPhoto _latestAvatarPhoto;
        float _latestStick = -1f;
        Vector2 _latestPos = LatestDefault;
        Vector2 _latestDragFrom;
        bool _latestDragging;
        PhotoSplitView _splitView;

        // ─── 建立 ──────────────────────────────────────────────────────

        void BuildStickers()
        {
            _stickerLayer = Stretch(_design, "Stickers");

            _more = Place(_design, "StickerMore", 0, 0, 36, 22);
            Fill(Stretch(_more, "Bg"), H9(s => s.tagMore), true, MoreBg, 11).raycastTarget = false;
            _moreText = Label(_more, 0, 0, 36, 22, "", 12, Color.white, TextAlignmentOptions.Center);
            _moreText.enableWordWrapping = false;
            _more.gameObject.SetActive(false);

            BuildLatestSticker();
            LoadLatestStickerPos();
        }

        void BuildLatestSticker()
        {
            // 88×92；貼紙邊 5、照片 78×78；金邊 3；頂端置中「最新」；頭像 26；歪 −6°；不透明度 85%；右下角翹起
            _latest = Place(_design, "LatestSticker", LatestDefault.x, LatestDefault.y, LatestW, LatestH);
            _latestBody = Stretch(_latest, "Body");
            _latestBody.localRotation = Quaternion.Euler(0f, 0f, 6f);
            _latestGroup = _latestBody.gameObject.AddComponent<CanvasGroup>();
            _latestGroup.alpha = 0.85f;

            var frame = Place(_latestBody, "Frame", 0, 0, 88, 88);
            UIShapes.AddShadow(frame, new Color(0.12f, 0.12f, 0.08f, 0.3f), 12, 8, 4);
            var frameSprite = H9(s => s.stickerLatestFrame);
            Image bg;
            if (frameSprite != null) bg = Fill(Stretch(frame, "Bg"), frameSprite, false, Color.white, 12);
            else
            {
                UIShapes.AddRounded(Place(frame, "Gold", -3, -3, 94, 94).gameObject, Yellow, 15).raycastTarget = false;
                bg = UIShapes.AddRounded(Stretch(frame, "Bg").gameObject, Color.white, 12);
            }
            _latestPhoto = MaskedPhoto(frame, 5, 5, 78, 78, H9(s => s.stickerLatestMask), 8, null);
            PeelCorner(frame, frameSprite, 88f, 68.64f);

            (_latestAvatar, _latestAvatarBg, _latestInitial) = AvatarCircle(_latestBody, -6, 64, 26, 11);
            _latestAvatarPhoto = AvatarPhoto.Attach(_latestAvatar, null);
            RingOver(_latestAvatar, 26);

            var tag = Place(_latestBody, "Tag", 26, -12, 36, 18);
            Fill(Stretch(tag, "Bg"), H9(s => s.tagLatest), true, Yellow, 8).raycastTarget = false;
            Label(tag, 0, 0, 36, 16, "最新", 10, Ink, TextAlignmentOptions.Center).enableWordWrapping = false;

            bg.raycastTarget = true;
            var btn = _latest.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnLatestClicked);

            // 手指拖到任何地方，放開就貼上（LatestCardDrag 拖曳開始後不會再觸發點擊）
            var drag = _latest.gameObject.AddComponent<LatestCardDrag>();
            drag.space = _design;
            drag.canDrag = () => !_dealt && _selected == null;
            drag.onBegin = () => { _latestDragging = true; _latestDragFrom = _latestPos; };
            drag.onDrag = d => SetLatestPos(_latestDragFrom + d, clamp: false);
            drag.onEnd = (d, v) =>
            {
                _latestDragging = false;
                SetLatestPos(_latestDragFrom + d, clamp: true);
                SaveLatestStickerPos();
            };

            _latest.gameObject.SetActive(false);
        }

        Sticker BuildSticker(string id, PhotoMeta meta, Texture2D tex, StickerSpot spot)
        {
            // 設計稿 66×66（白邊 4、圓角 10、陰影 0 3 6；照片 58×58 圓角 7；右下角翹起；左下頭像 22；不透明度 85%），
            // 依 spot.size 等比縮放（有大有小）
            float half = spot.size / 2f;
            var holder = Place(_stickerLayer, "Sticker", spot.x + half - 33f, spot.y + half - 33f, 66, 66);
            Rotate(holder, -spot.rot);
            holder.localScale = Vector3.one * (spot.size / 66f);
            var body = Stretch(holder, "Body");
            var group = body.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            UIShapes.AddShadow(body, new Color(0.12f, 0.12f, 0.08f, 0.25f), 10, 6, 3);
            var frameSprite = H9(s => s.stickerFrame);
            var bg = frameSprite != null ? Fill(Stretch(body, "Bg"), frameSprite, false, Color.white, 10)
                                         : UIShapes.AddRounded(Stretch(body, "Bg").gameObject, Color.white, 10);
            var photo = MaskedPhoto(body, 4, 4, 58, 58, H9(s => s.stickerPhotoMask), 7, tex);
            PeelCorner(body, frameSprite, 66f, 52.8f);

            var (circle, circleBg, initial) = AvatarCircle(body, -5, 46, 22, 10);
            Decorate(circleBg, initial, meta);
            AvatarPhoto.Attach(circle, meta.takerUserId);
            RingOver(circle, 22);

            var st = new Sticker { id = id, holder = holder, body = body, group = group, photo = photo, texture = tex };
            bg.raycastTarget = true;
            var btn = holder.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => Peel(st));
            return st;
        }

        /// <summary>
        /// 右下角翹起：素材的翹角在照片下面會被照片蓋住，所以只取素材右下角那一塊（RectMask2D 裁切）再疊一次在照片上面。
        /// </summary>
        static void PeelCorner(RectTransform frame, Sprite frameSprite, float size, float from)
        {
            if (frameSprite == null) return;
            var clip = Place(frame, "PeelCorner", from, from, size - from, size - from);
            clip.gameObject.AddComponent<RectMask2D>();
            var copy = Place(clip, "Frame", -from, -from, size, size);
            Fill(copy, frameSprite, false, Color.white, 0).raycastTarget = false;
        }

        /// <summary>頭像圓：底色＋名字第一個字（沒有頭貼時看到這個）；頭貼由 AvatarPhoto 蓋上去。</summary>
        (RectTransform, Image, TMP_Text) AvatarCircle(RectTransform parent, float left, float top, float size, float fontSize)
        {
            var circle = Place(parent, "Avatar", left, top, size, size);
            var bg = UIShapes.AddRounded(circle.gameObject, AvatarColors[0], size / 2f);
            bg.raycastTarget = false;
            var t = Label(circle, 0, 0, size, size, "", fontSize, Color.white, TextAlignmentOptions.Center);
            t.enableWordWrapping = false;
            return (circle, bg, t);
        }

        void RingOver(RectTransform circle, float size)
        {
            var rt = Place(circle.parent, "AvatarRing", 0, 0, size, size);
            rt.anchorMin = circle.anchorMin;
            rt.anchorMax = circle.anchorMax;
            rt.pivot = circle.pivot;
            rt.anchoredPosition = circle.anchoredPosition;
            rt.SetAsLastSibling();
            var ring = H9(s => s.avatarRing);
            if (ring != null) Fill(rt, ring, false, Color.white, size / 2f).raycastTarget = false;
            else UIShapes.AddThinRing(rt.gameObject, Color.white, size / 2f).raycastTarget = false;
        }

        static void Decorate(Image bg, TMP_Text initial, PhotoMeta meta)
        {
            string uid = meta != null ? meta.takerUserId : null;
            string name = meta != null ? meta.takerName : null;
            bg.color = AvatarColors[StickerRules.Hash(uid ?? name ?? "") % (uint)AvatarColors.Length];
            initial.text = string.IsNullOrEmpty(name) ? "營" : name.Substring(0, 1);
        }

        // ─── 資料 ──────────────────────────────────────────────────────

        void UpdateStickers()
        {
            AnimateStickers();
            AnimateLatestSticker();
            if (Time.unscaledTime < _nextStickerPoll) return;
            _nextStickerPoll = Time.unscaledTime + 0.5f;

            var fb = FirebaseManager.Instance;
            string room = fb != null ? fb.RoomId : null;
            string me = fb != null ? fb.GetUserId() : null;
            var photos = SaveManager.Instance?.data?.photos;
            if (string.IsNullOrEmpty(room) || photos == null)
            {
                ClearStickers();
                SetLatest(null);
                return;
            }
            LoadSeen(room, me);

            // 營地最新一張（相簿最後一張）
            PhotoMeta latest = null;
            for (int i = photos.Count - 1; i >= 0 && latest == null; i--)
                if (photos[i] != null && !string.IsNullOrEmpty(photos[i].fileName)) latest = photos[i];
            SetLatest(latest);

            var byId = new Dictionary<string, PhotoMeta>();
            var list = new List<StickerPhoto>();
            foreach (var p in photos)
            {
                if (p == null || string.IsNullOrEmpty(p.fileName)) continue;
                byId[p.fileName] = p;
                long t = DateTime.TryParse(p.timestamp, out var dt) ? dt.Ticks : 0L;
                list.Add(new StickerPhoto(p.fileName, p.takerUserId, t));
            }
            var want = StickerRules.Visible(list, me, _seen, _since, latest?.fileName, out int more);

            // 不該再貼的拿掉（例如換了空間）；撕到一半的讓動畫跑完
            var keep = new HashSet<string>();
            foreach (var p in want) keep.Add(p.id);
            var drop = new List<string>();
            foreach (var kv in _stickers) if (!keep.Contains(kv.Key) && !kv.Value.peeling) drop.Add(kv.Key);
            foreach (var id in drop) RemoveSticker(id);

            // 新的貼上去（照片還沒下載好就等下一次）
            Sticker last = null;
            foreach (var p in want)
            {
                if (!_stickers.TryGetValue(p.id, out var st))
                {
                    var tex = LoadStickerPhoto(byId[p.id]);
                    if (tex == null) continue;
                    st = BuildSticker(p.id, byId[p.id], tex, StickerRules.Spot(p.id));
                    st.photo.uvRect = AspectFillUv(tex.width, tex.height, 58f, 58f);
                    _stickers[p.id] = st;
                }
                if (!st.peeling) last = st;
            }

            // +N：貼在最後一張貼紙的右下旁邊
            bool showMore = more > 0 && last != null;
            if (_more.gameObject.activeSelf != showMore) _more.gameObject.SetActive(showMore);
            if (showMore)
            {
                string s = "+" + more;
                if (_moreText.text != s)
                {
                    _moreText.text = s;
                    float w = Mathf.Max(36f, Mathf.Ceil(TextW(_moreText, s)) + 20f);
                    _more.sizeDelta = new Vector2(w, 22f);
                    _moreText.rectTransform.sizeDelta = new Vector2(w, 22f);
                }
                var c = last.holder.anchoredPosition; // holder 以中心為軸
                float h = 33f * last.holder.localScale.x;
                _more.anchoredPosition = new Vector2(c.x + h - 15f, c.y - h + 9f);
            }
        }

        void LoadSeen(string room, string me)
        {
            string owner = room + "." + (me ?? "");
            if (owner == _seenOwner) return;
            _seenOwner = owner;
            ClearStickers();
            _latestId = null; // 換空間：最新貼紙直接換，不播「啪」
            _seen.Clear();
            _seenList.Clear();
            foreach (var id in PlayerPrefs.GetString(PrefsSeen + owner, "").Split('\n'))
                if (!string.IsNullOrEmpty(id) && _seen.Add(id)) _seenList.Add(id);

            // 第一次在這個空間用 v9：之前的照片都當作看過，只有之後的新照片變貼紙
            string since = PlayerPrefs.GetString(PrefsSince + owner, "");
            if (!long.TryParse(since, out _since))
            {
                _since = DateTime.Now.Ticks;
                PlayerPrefs.SetString(PrefsSince + owner, _since.ToString());
                PlayerPrefs.Save();
            }
        }

        void MarkSeen(string id)
        {
            if (string.IsNullOrEmpty(id) || !_seen.Add(id)) return;
            _seenList.Add(id);
            while (_seenList.Count > MaxSeenIds)
            {
                _seen.Remove(_seenList[0]);
                _seenList.RemoveAt(0);
            }
            PlayerPrefs.SetString(PrefsSeen + _seenOwner, string.Join("\n", _seenList));
            PlayerPrefs.Save();
        }

        readonly Dictionary<string, float> _photoRetry = new Dictionary<string, float>();

        /// <summary>讀營地照片並縮到最長邊 256（貼紙只有 66／78 pt）；檔案還沒下載好時 5 秒後再試，不要每次都讀。</summary>
        Texture2D LoadStickerPhoto(PhotoMeta meta)
        {
            if (meta == null || SaveManager.Instance == null) return null;
            if (_photoRetry.TryGetValue(meta.fileName, out float at) && Time.unscaledTime < at) return null;
            var tex = SaveManager.Instance.LoadPhoto(meta);
            if (tex == null)
            {
                _photoRetry[meta.fileName] = Time.unscaledTime + 5f;
                return null;
            }
            _photoRetry.Remove(meta.fileName);
            return Shrink(tex, 256);
        }

        static Texture2D Shrink(Texture2D src, int max)
        {
            if (Mathf.Max(src.width, src.height) <= max) return src;
            float k = max / (float)Mathf.Max(src.width, src.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k));
            int h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));
            var rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply(false, true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(src);
            return dst;
        }

        void RemoveSticker(string id)
        {
            if (!_stickers.TryGetValue(id, out var st)) return;
            _stickers.Remove(id);
            if (st.texture != null) Destroy(st.texture);
            if (st.holder != null) Destroy(st.holder.gameObject);
        }

        void ClearStickers()
        {
            foreach (var id in new List<string>(_stickers.Keys)) RemoveSticker(id);
            if (_more != null) _more.gameObject.SetActive(false);
        }

        void DestroyStickerTextures()
        {
            foreach (var st in _stickers.Values) if (st.texture != null) Destroy(st.texture);
            _stickers.Clear();
            if (_latestTexture != null) Destroy(_latestTexture);
        }

        // ─── 撕掉 ──────────────────────────────────────────────────────

        void Peel(Sticker st)
        {
            if (st.peeling) return;
            st.peeling = true;
            st.time = 0f;
            st.holder.SetAsLastSibling();
            MarkSeen(st.id);
            Haptics.Light();
        }

        void AnimateStickers()
        {
            float dt = Time.unscaledDeltaTime;
            List<string> done = null;
            foreach (var st in _stickers.Values)
            {
                st.time += dt;
                if (st.peeling)
                {
                    // hPeelOff 0.45 秒 ease-in：35% 往上 10、轉 −18°、1.08 倍 → 結束 右 70、上 160、轉 40°、0.6 倍、淡出
                    float t = Mathf.Clamp01(st.time / 0.45f);
                    Vector2 pos; float rot, scale, alpha;
                    if (t < 0.35f)
                    {
                        float u = CubicBezier(0.42f, 0f, 1f, 1f, t / 0.35f);
                        pos = Vector2.Lerp(Vector2.zero, new Vector2(4f, 10f), u);
                        rot = Mathf.Lerp(0f, 18f, u);
                        scale = Mathf.Lerp(1f, 1.08f, u);
                        alpha = Mathf.Lerp(0.85f, 0.9f, u);
                    }
                    else
                    {
                        float u = CubicBezier(0.42f, 0f, 1f, 1f, (t - 0.35f) / 0.65f);
                        pos = Vector2.Lerp(new Vector2(4f, 10f), new Vector2(70f, 160f), u);
                        rot = Mathf.Lerp(18f, -40f, u);
                        scale = Mathf.Lerp(1.08f, 0.6f, u);
                        alpha = Mathf.Lerp(0.9f, 0f, u);
                    }
                    // 設計稿的動畫在歪過的貼紙裡面跑，所以位移也跟著貼紙歪
                    st.body.anchoredPosition = pos;
                    st.body.localRotation = Quaternion.Euler(0f, 0f, rot);
                    st.body.localScale = Vector3.one * scale;
                    st.group.alpha = alpha;
                    if (t >= 1f) (done ??= new List<string>()).Add(st.id);
                }
                else if (st.time <= 0.45f)
                {
                    // hStickIn 0.4 秒 ease-out：1.4 倍、透明 → 70% 0.95 倍 → 1 倍、不透明度 85%
                    float t = Mathf.Clamp01(st.time / 0.4f);
                    float scale, alpha;
                    if (t < 0.7f)
                    {
                        float u = CubicBezier(0f, 0f, 0.58f, 1f, t / 0.7f);
                        scale = Mathf.Lerp(1.4f, 0.95f, u);
                        alpha = Mathf.Lerp(0f, 0.85f, u);
                    }
                    else
                    {
                        float u = CubicBezier(0f, 0f, 0.58f, 1f, (t - 0.7f) / 0.3f);
                        scale = Mathf.Lerp(0.95f, 1f, u);
                        alpha = 0.85f;
                    }
                    st.body.localScale = Vector3.one * scale;
                    st.group.alpha = alpha;
                }
            }
            if (done == null) return;
            foreach (var id in done) RemoveSticker(id);
            // 全部撕完：寵物說「乾淨了！」
            if (_stickers.Count == 0 && !_more.gameObject.activeSelf) SayClean();
            _nextStickerPoll = 0f; // 馬上補下一張
        }

        // ─── 最新照片貼紙 ──────────────────────────────────────────────

        void SetLatest(PhotoMeta meta)
        {
            string id = meta?.fileName;
            if (id == _latestId && (id == null || _latestTexture != null)) return;

            var tex = meta != null ? LoadStickerPhoto(meta) : null;
            if (meta != null && tex == null) return; // 還沒下載好，下次再試

            bool replaced = _latestId != null && id != null && id != _latestId;
            _latestId = id;
            if (_latestTexture != null) Destroy(_latestTexture);
            _latestTexture = tex;
            _latestPhoto.texture = tex;
            if (tex != null) _latestPhoto.uvRect = AspectFillUv(tex.width, tex.height, 78f, 78f);
            if (meta != null)
            {
                Decorate(_latestAvatarBg, _latestInitial, meta);
                if (_latestAvatarPhoto != null) _latestAvatarPhoto.SetUser(meta.takerUserId);
            }

            bool show = tex != null;
            _latest.gameObject.SetActive(show);
            // 有新照片進來：「啪」地貼上（第一次出現不播）
            if (show && replaced) _latestStick = 0f;
        }

        void AnimateLatestSticker()
        {
            if (!_latest.gameObject.activeSelf) return;
            if (_latestStick < 0f) return;
            // hStick 0.5 秒 cubic-bezier(.2,.8,.3,1)：1.35 倍、轉 −14°、透明 → 60% 0.94 倍、−4° → 1 倍、−6°（CSS 角度）
            _latestStick += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_latestStick / 0.5f);
            float scale, rot, alpha;
            if (t < 0.6f)
            {
                float u = DealEase(t / 0.6f);
                scale = Mathf.LerpUnclamped(1.35f, 0.94f, u);
                rot = Mathf.LerpUnclamped(-14f, -4f, u);
                alpha = Mathf.LerpUnclamped(0f, 0.85f, u);
            }
            else
            {
                float u = DealEase((t - 0.6f) / 0.4f);
                scale = Mathf.LerpUnclamped(0.94f, 1f, u);
                rot = Mathf.LerpUnclamped(-4f, -6f, u);
                alpha = 0.85f;
            }
            _latestBody.localScale = Vector3.one * scale;
            _latestBody.localRotation = Quaternion.Euler(0f, 0f, -rot);
            _latestGroup.alpha = alpha;
            if (t >= 1f) _latestStick = -1f;
        }

        void SetLatestPos(Vector2 p, bool clamp)
        {
            if (clamp)
            {
                // 整張留在畫面裡（上面留給狀態欄）
                float scale = _design.localScale.x > 0f ? _design.localScale.x : 1f;
                float visibleH = _overlay.rect.height > 0f ? _overlay.rect.height / scale : DesignH;
                float top = DesignH - visibleH + 110f;
                float bottom = DesignH - LatestH - 4f;
                p = new Vector2(Mathf.Clamp(p.x, 4f, DesignW - LatestW - 4f), Mathf.Clamp(p.y, Mathf.Min(top, bottom), bottom));
            }
            _latestPos = p;
            _latest.anchoredPosition = new Vector2(p.x, -p.y);
        }

        void LoadLatestStickerPos()
        {
            var parts = PlayerPrefs.GetString(PrefsLatestSticker, "").Split(',');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var p = LatestDefault;
            if (parts.Length == 2
                && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out float x)
                && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out float y))
                p = new Vector2(x, y);
            SetLatestPos(p, clamp: false);
        }

        void SaveLatestStickerPos()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(PrefsLatestSticker, _latestPos.x.ToString("0.#", inv) + "," + _latestPos.y.ToString("0.#", inv));
            PlayerPrefs.Save();
        }

        /// <summary>最新貼紙：打開分割畫面（大照片＋留言＋縮小的營地）；沒有掛 PhotoSplitView 時改開相簿頁。</summary>
        void OnLatestClicked()
        {
            if (_latestDragging || _dealt) return;
            if (_splitView == null) _splitView = GetComponent<PhotoSplitView>();
            if (_splitView == null) _splitView = FindObjectOfType<PhotoSplitView>(true);
            if (_splitView != null && _splitView.Open()) return;
            cameraUI?.OpenAlbumFromHand();
        }
    }
}
