using System;
using BlockPet.Hand;
using BlockPet.Travel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Travel
{
    /// <summary>
    /// 營地首頁上的旅行畫面（設計文件 5.21 ③；設計稿 TravelTrip 的 away／host／back）：
    /// - 自己的寵物出門中（流程 7）：寵物的位置換成「去旅行了！」木牌。
    /// - 別的營地的寵物來玩（流程 10）：營地裡多一隻背背包的客人（只顯示最早到的）。
    /// - 2026-11-06 使用者決定：首頁上方的出門卡片、客人來玩白卡都拿掉（會蓋住 v9 的 Home 鍵和選單），旅行資訊去旅行頁面看。
    /// - 寄明信片（5.21，2026-11-08）：好友寄來的明信片送到時彈出「BowBow 送來一張明信片！」；
    ///   我們的寵物寄完回到家時暗下來「BowBow 帶回一張明信片！」（對方營地的風景明信片，自動產生），明信片彈出、蓋郵戳。
    ///   兩種都是「收進護照」。回寄已拿掉（2026-11-08 使用者決定，各寄各的）。
    ///   每個成員各看一次（記在手機上）。推播還沒做，打開 App 時才看到。
    /// - 回家結算：時間到了誰先打開 App 誰結算一次（好友路 +1 趟＝地圖上郵票的 ×N）。
    /// 執行時自動建立；只在首頁顯示（相機、相簿、聊天、其他頁面時藏起來）。
    /// </summary>
    public class TravelHomeOverlay : MonoBehaviour
    {
        const float DesignW = 390f;
        const string PrefsSeenPostcards = "BlockPet.Postcard.Seen.";
        const long PopupMaxAgeMs = 3L * 24 * 3600 * 1000; // 太久以前的明信片就不補彈了

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color SubText = Hex(0x6B5D4E);
        static readonly Color Brown = Hex(0x8A6A4E);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color Blue = Hex(0x6F8FE0);
        static readonly Color Dim = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.55f);

        static TravelHomeOverlay _instance;

        TravelSkin _skin;
        TMP_FontAsset _font;
        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _root, _sign, _guest, _backLayer;
        PageManager _pages;
        CameraUIManager _cameraUI;
        SpriteRenderer[] _hiddenPet;
        /// <summary>寵物出門那一刻的位置：木牌固定插在這裡（寵物藏起來後還會在原地亂走，不能跟著牠）。</summary>
        Vector3? _signWorld;
        float _nextCheck, _backT, _nextScenic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("TravelHomeOverlay (runtime)", typeof(RectTransform));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TravelHomeOverlay>();
        }

        void Awake()
        {
            _skin = TravelSkin.Load();
            _font = _skin != null ? _skin.font : null;
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 12; // 首頁之上，圖鑑／照片檢視／旅行地圖之下
            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();
            _root = (RectTransform)transform;

            _sign = BuildSign();
            _guest = BuildGuest();
            _backLayer = Stretch(_root, "Back");
            _backLayer.gameObject.SetActive(false);
            HideAll();
        }

        void OnDestroy()
        {
            ShowPet(true);
            if (_instance == this) _instance = null;
        }

        // ─── 每一格 ─────────────────────────────────────────────────────

        void Update()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || string.IsNullOrEmpty(fb.RoomId) || string.IsNullOrEmpty(fb.GetUserId())) { HideAll(); ShowPet(true); return; }
            fb.StartTravelState();
            fb.StartCampFriends(); // 也一起聽明信片、抽好友

            long now = fb.ServerNowMs;
            var trip = fb.CurrentTrip;
            var phase = trip == null ? TripPhase.None : TripRules.Phase(now, trip.departAt, trip.oneWayMin, trip.stayMin);

            // 時間到了：回家蓋章（交易保證只結算一次）
            if (phase == TripPhase.Done && Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 5f;
                fb.FinishTripIfHome(CountPhotos);
            }

            bool home = IsHome();
            _group.alpha = home ? 1f : 0f;
            _group.blocksRaycasts = home;

            bool away = phase == TripPhase.Going || phase == TripPhase.There || phase == TripPhase.Returning;
            ShowPet(!away);
            float k = _root.rect.width / DesignW;
            _sign.gameObject.SetActive(away);
            if (away)
            {
                if (_signWorld == null && AIController.Instance != null) _signWorld = AIController.Instance.transform.position;
                FollowPet(_sign, k, -55f, -120f, _signWorld);
            }
            else _signWorld = null;

            // 客人（只顯示最早到、現在在這裡的那隻）
            FirebaseManager.TravelGuest guest = null;
            foreach (var g in fb.Guests)
            {
                if (now < g.arriveAt || now >= g.leaveAt) continue;
                if (guest == null) guest = g;
            }
            _guest.gameObject.SetActive(guest != null);
            if (guest != null)
            {
                _guestTag.text = guest.fromName;
                float tw = Mathf.Ceil(TextWidth(_guestTag, guest.fromName)) + 16f;
                ((RectTransform)_guestTag.transform.parent).sizeDelta = new Vector2(tw, 17f);
                ((RectTransform)_guestTag.transform.parent).anchoredPosition = new Vector2(60f - tw / 2f, 24f);
                _guestTag.rectTransform.sizeDelta = new Vector2(tw, 17f);
                FollowPet(_guest, k, 60f, -104f);
                _guest.GetChild(0).localPosition = new Vector3(0f, 4f * Mathf.Sin(Time.unscaledTime / 2f * Mathf.PI * 2f), 0f);
            }

            if (Time.unscaledTime >= _nextScenic)
            {
                _nextScenic = Time.unscaledTime + 10f;
                fb.CheckScenicPostcards(PostcardArt.RenderScenery); // 寵物回到家：帶回對方營地的風景明信片
            }
            UpdatePopup(fb);
        }

        bool IsHome()
        {
            if (_pages == null) _pages = FindObjectOfType<PageManager>(true);
            if (_cameraUI == null) _cameraUI = FindObjectOfType<CameraUIManager>(true);
            return (_cameraUI == null || _cameraUI.CurrentMode == UIMode.Home) && _pages != null && _pages.IsHomePageActive;
        }

        float TopInset()
        {
            float sc = _canvas.scaleFactor;
            return (Screen.height - Screen.safeArea.yMax) * (sc > 0f ? 1f / sc : 1f);
        }

        void HideAll()
        {
            if (_group != null) { _group.alpha = 0f; _group.blocksRaycasts = false; }
        }

        /// <summary>寵物出門時把首頁那隻藏起來，回家再放回來。</summary>
        void ShowPet(bool show)
        {
            var pet = AIController.Instance;
            if (!show)
            {
                if (pet == null || _hiddenPet != null) return;
                // 只關原本開著的，回家時只開回這些
                var list = new System.Collections.Generic.List<SpriteRenderer>();
                foreach (var r in pet.GetComponentsInChildren<SpriteRenderer>(true)) if (r != null && r.enabled) { r.enabled = false; list.Add(r); }
                _hiddenPet = list.ToArray();
                return;
            }
            if (_hiddenPet == null) return;
            foreach (var r in _hiddenPet) if (r != null) r.enabled = true;
            _hiddenPet = null;
        }

        /// <summary>把 UI 放在首頁寵物的位置（腳下）＋位移（設計座標）。找不到寵物就放在畫面中間偏下。</summary>
        void FollowPet(RectTransform rt, float k, float dx, float dy, Vector3? fixedWorld = null)
        {
            var pet = AIController.Instance;
            var cam = Camera.main;
            Vector2 local;
            if ((fixedWorld.HasValue || pet != null) && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(fixedWorld ?? pet.transform.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, null, out local);
            }
            else local = new Vector2(0f, -_root.rect.height * 0.1f);
            rt.localScale = Vector3.one * k;
            rt.anchoredPosition = local + new Vector2(dx, -dy) * k;
        }

        int CountPhotos(long fromMs, long toMs)
        {
            var photos = SaveManager.Instance != null ? SaveManager.Instance.data?.photos : null;
            if (photos == null) return 0;
            var from = DateTimeOffset.FromUnixTimeMilliseconds(fromMs).LocalDateTime;
            var to = DateTimeOffset.FromUnixTimeMilliseconds(toMs).LocalDateTime;
            int n = 0;
            foreach (var p in photos)
                if (p != null && DateTime.TryParse(p.timestamp, out var t) && t >= from && t <= to) n++;
            return n;
        }

        // ─── 出門中（流程 7）──────────────────────────────────────────

        TMP_Text _guestTag;

        RectTransform BuildSign()
        {
            // 木牌 110 × 120＋「去旅行了！」Huninn 14（設計稿寬 126 的框）
            var rt = Stretch(_root, "AwaySign");
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(110f, 120f);
            if (_skin != null && _skin.awaySign != null)
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = _skin.awaySign;
                img.raycastTarget = false;
            }
            else UIShapes.AddRounded(Place(rt, "Board", 10, 20, 90, 32).gameObject, Paper, 4).raycastTarget = false;
            Label(rt, 0, 24, 110, 22, "去寄明信片了！", 13, Ink, TextAlignmentOptions.Center);
            rt.gameObject.SetActive(false);
            return rt;
        }

        // ─── 客人來玩（流程 10）───────────────────────────────────────

        RectTransform BuildGuest()
        {
            // 營地裡的客人：寵物（翻面）＋背包＋頭上藍色營地名
            var rt = Stretch(_root, "Guest");
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(120f, 112f);
            var body = Place(rt, "Body", 0, 0, 120, 112);
            UIShapes.AddRounded(Place(body, "Shadow", 14, 96, 92, 15).gameObject, new Color(30f / 255f, 45f / 255f, 20f / 255f, 0.3f), 7.5f).raycastTarget = false;
            if (_skin != null && _skin.pet != null)
            {
                var pi = Place(body, "Pet", 0, 0, 120, 104).gameObject.AddComponent<Image>();
                pi.sprite = _skin.pet;
                pi.preserveAspect = true;
                pi.raycastTarget = false;
                pi.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            }
            if (_skin != null && _skin.backpack != null)
            {
                var bp = Place(body, "Backpack", 14, 40, 30, 34).gameObject.AddComponent<Image>();
                bp.sprite = _skin.backpack;
                bp.raycastTarget = false;
            }
            var tag = Place(rt, "Tag", 20, -24, 80, 17);
            if (_skin != null && _skin.tagPill != null)
            {
                var ti = tag.gameObject.AddComponent<Image>();
                ti.sprite = _skin.tagPill; ti.type = Image.Type.Sliced; ti.color = Blue; ti.raycastTarget = false;
            }
            else UIShapes.AddRounded(tag.gameObject, Blue, 8.5f).raycastTarget = false;
            _guestTag = Label(tag, 0, 0, 80, 17, "", 10, Color.white, TextAlignmentOptions.Center);
            rt.gameObject.SetActive(false);
            return rt;
        }

        // ─── 明信片彈出（5.21 ④-5、④-7）─────────────────────────────

        RectTransform _popCard, _postmark;
        string _popId;
        PostcardSkin _pc;

        string SeenKey(FirebaseManager fb) => PrefsSeenPostcards + fb.RoomId + "." + fb.GetUserId();

        bool Seen(FirebaseManager fb, string id) => ("," + PlayerPrefs.GetString(SeenKey(fb), "") + ",").Contains("," + id + ",");

        void MarkSeen(FirebaseManager fb, string id)
        {
            string key = SeenKey(fb), cur = PlayerPrefs.GetString(key, "");
            var parts = new System.Collections.Generic.List<string>(cur.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            parts.Add(id);
            if (parts.Count > 200) parts.RemoveRange(0, parts.Count - 200);
            PlayerPrefs.SetString(key, string.Join(",", parts));
            PlayerPrefs.Save();
        }

        void UpdatePopup(FirebaseManager fb)
        {
            if (_backLayer.gameObject.activeSelf)
            {
                AnimatePopup();
                return;
            }
            if (!IsHome()) return;
            long now = fb.ServerNowMs;
            foreach (var p in fb.VisiblePostcards())
            {
                if (now - p.visibleAt > PopupMaxAgeMs || Seen(fb, p.id)) continue;
                BuildPopup(fb, p);
                return;
            }
        }

        void AnimatePopup()
        {
            if (_popCard == null) return;
            // 明信片彈出 0.6 秒（0.6 倍→1.04→1、轉到 −6°）；郵戳 0.5 秒後蓋下（1.5 倍→1）
            _backT += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_backT / 0.6f);
            float sc = t < 0.7f ? Mathf.Lerp(0.6f, 1.04f, t / 0.7f) : Mathf.Lerp(1.04f, 1f, (t - 0.7f) / 0.3f);
            _popCard.localScale = Vector3.one * sc;
            _popCard.localRotation = Quaternion.Euler(0f, 0f, 6f * t); // CSS rotate(-6deg)：逆時針
            if (_postmark != null)
            {
                float m = Mathf.Clamp01((_backT - 0.5f) / 0.25f);
                _postmark.localScale = Vector3.one * (m <= 0f ? 0.01f : Mathf.Lerp(1.5f, 1f, m));
            }
        }

        /// <summary>
        /// 彈出一張明信片（back_card：266 × 324 在 (62,168)，照片 (14,18) 238、標題 y 262、右上郵票、郵戳）。
        /// 對方寄來的、寵物帶回的風景明信片，都是「收進護照」。
        /// </summary>
        void BuildPopup(FirebaseManager fb, FirebaseManager.Postcard p)
        {
            if (_pc == null) _pc = PostcardSkin.Load();
            _backLayer.gameObject.SetActive(true);
            for (int i = _backLayer.childCount - 1; i >= 0; i--) Destroy(_backLayer.GetChild(i).gameObject);
            var dim = _backLayer.gameObject.GetComponent<Image>();
            if (dim == null) dim = _backLayer.gameObject.AddComponent<Image>();
            dim.color = Dim;
            float k = _root.rect.width / DesignW;
            var design = Design(_backLayer, "Design");
            design.localScale = Vector3.one * k;
            design.anchoredPosition = new Vector2(0f, -TopInset());
            _popId = p.id;
            bool incoming = p.kind == FirebaseManager.PostcardKindPhoto;
            string myPet = !string.IsNullOrEmpty(fb.CurrentPetName) ? fb.CurrentPetName : "寵物";
            string theirPet = string.IsNullOrEmpty(p.fromPet) ? "好友的寵物" : p.fromPet;
            Label(design, 20, 110, DesignW - 40f, 30, incoming ? $"{theirPet} 送來一張明信片！" : $"{myPet} 帶回一張明信片！", 20, Color.white, TextAlignmentOptions.Center);
            Label(design, 20, 140, DesignW - 40f, 18, incoming ? $"從{p.fromName}寄來" : $"{p.fromName}的風景", 12, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center);

            _popCard = Place(design, "Card", 62, 168, 266, 324);
            _popCard.pivot = new Vector2(0.5f, 0.5f);
            _popCard.anchoredPosition = new Vector2(62f + 133f, -(168f + 162f));
            UIShapes.AddShadow(_popCard, new Color(0f, 0f, 0f, 0.35f), 4, 30, 12);
            UIShapes.AddRounded(Stretch(_popCard, "Bg").gameObject, Color.white, 3).raycastTarget = true;
            var photo = Place(_popCard, "Photo", 14, 18, 238, 238).gameObject.AddComponent<RawImage>();
            photo.color = Hex(0xE8DFCF);
            photo.uvRect = PostcardArt.PhotoUv();
            photo.raycastTarget = false;
            fb.LoadPostcardImage(p.id, tex => { if (photo != null && tex != null) { photo.texture = tex; photo.color = Color.white; } });
            string cap = string.IsNullOrEmpty(p.caption) ? p.fromName : p.caption;
            Label(_popCard, 14, 262, 238, 24, cap, 15, Ink, TextAlignmentOptions.Center);
            Label(_popCard, 14, 288, 238, 16, BlockPet.Travel.PostcardRules.PostmarkDate(p.visibleAt), 11, Brown, TextAlignmentOptions.Center);
            // 右上郵票（58，轉 8°）
            var stamp = Place(_popCard, "Stamp", 186, -14, 58, 58);
            stamp.pivot = new Vector2(0.5f, 0.5f);
            stamp.anchoredPosition = new Vector2(186f + 29f, -(-14f + 29f));
            stamp.localRotation = Quaternion.Euler(0f, 0f, -8f);
            if (_pc != null && _pc.stamp != null) { var si = stamp.gameObject.AddComponent<Image>(); si.sprite = _pc.stamp; si.raycastTarget = false; }
            else UIShapes.AddRounded(stamp.gameObject, Hex(0xF7B6C2), 3).raycastTarget = false;
            // 郵戳（營地名＋日期，0.5 秒後蓋下）
            _postmark = PostcardArt.Postmark(_popCard, _pc, _font, 150, 6, 92, p.fromName, BlockPet.Travel.PostcardRules.PostmarkDate(p.visibleAt));
            _postmark.localScale = Vector3.one * 0.01f;
            _popCard.localScale = Vector3.one * 0.6f;
            _backT = 0f;

            float y = 168f + 324f + 26f;
            const float W = 266f;
            string id = p.id;
            Btn(design, 62, y, W, true, "收進護照", () =>
            {
                MarkSeen(fb, id);
                _backLayer.gameObject.SetActive(false);
                TravelScreen.OpenPassport();
            });
            if (incoming)
            {
                y += 53f + 12f;
                var note = Place(design, "Note", 40, y, DesignW - 80f, 30);
                if (_pc != null && _pc.note != null) { var ni = note.gameObject.AddComponent<Image>(); ni.sprite = _pc.note; ni.type = Image.Type.Sliced; ni.raycastTarget = false; }
                else UIShapes.AddRounded(note.gameObject, new Color(Paper.r, Paper.g, Paper.b, 0.92f), 14).raycastTarget = false;
                Label(note, 0, 0, DesignW - 80f, 30, "收到的明信片放在護照・也可以寄一張過去", 11, SubText, TextAlignmentOptions.Center);
            }
            _backLayer.gameObject.SetActive(true);
        }

        void Btn(RectTransform parent, float x, float y, float w, bool yellow, string text, UnityEngine.Events.UnityAction onClick)
        {
            var rt = Place(parent, text, x, y, w, 53);
            Image img;
            if (yellow)
            {
                UIShapes.AddRounded(Place(rt, "Edge", 0, 3, w, 50).gameObject, YellowEdge, 15).raycastTarget = false;
                var fill = Place(rt, "Fill", 0, 0, w, 50);
                if (_pc != null && _pc.btnYes != null) { img = fill.gameObject.AddComponent<Image>(); img.sprite = _pc.btnYes; img.type = Image.Type.Sliced; }
                else img = UIShapes.AddRounded(fill.gameObject, Yellow, 15);
            }
            else
            {
                var fill = Place(rt, "Fill", 0, 0, w, 50);
                if (_pc != null && _pc.btnNo != null) { img = fill.gameObject.AddComponent<Image>(); img.sprite = _pc.btnNo; img.type = Image.Type.Sliced; }
                else img = UIShapes.AddRounded(fill.gameObject, Color.white, 15);
            }
            Label(rt, 0, 0, w, 50, text, 15, Ink, TextAlignmentOptions.Center);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
        }

        /// <summary>
        /// 量字寬（名牌、按鈕、標籤的底依字寬算）：字型是動態的，沒出現過的字要先加進字型，不然量出來太窄、字超出底。
        /// </summary>
        static float TextWidth(TMP_Text t, string s)
        {
            if (t.font != null && !string.IsNullOrEmpty(s)) t.font.TryAddCharacters(s);
            return t.GetPreferredValues(s).x;
        }

        /// <summary>
        /// 多行文字的高度：設定寬度、開換行，讓 TextMeshPro 實際排一次版，量第一行頂到最後一行底。
        /// 文字所在的物件一定要已經啟用（未啟用時 TextMeshPro 量不出來，會只剩一行高）。
        /// （2026-11-08 修：原本面板「先建內容、再打開」，量到的都是一行高，下面的東西就壓到第二行以後；圖鑑說明則是沒開換行）
        /// </summary>
        static float TextHeight(TMP_Text t, string s, float width)
        {
            if (t.font != null && !string.IsNullOrEmpty(s)) t.font.TryAddCharacters(s);
            if (!t.gameObject.activeInHierarchy) Debug.LogWarning("[UI] 文字還沒啟用就量高度，會量不準（先 SetActive 再建）：" + s);
            var rt = t.rectTransform;
            var old = rt.sizeDelta;
            t.enableWordWrapping = true;
            t.text = s;
            rt.sizeDelta = new Vector2(width, 4000f);
            t.ForceMeshUpdate(true, true);
            var info = t.textInfo;
            float h = info != null && info.lineCount > 0
                ? info.lineInfo[0].ascender - info.lineInfo[info.lineCount - 1].descender
                : 0f;
            rt.sizeDelta = old;
            if (h <= 0f) h = t.fontSize * 1.3f; // 排不出來（例如字型還沒載入）：至少一行
            return h + 2f;
        }

        // ─── UI helpers ────────────────────────────────────────────────

        RectTransform Design(RectTransform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(DesignW, 772f);
            return rt;
        }

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size, Color color, TextAlignmentOptions align)
        {
            var t = Place(parent, "Text", left, top, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
