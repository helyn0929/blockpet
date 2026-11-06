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
    /// - 自己的寵物出門中（流程 7）：寵物的位置換成「去旅行了！」木牌＋上方卡片（還要幾分、幾點回家、進度條）。
    /// - 別的營地的寵物來玩（流程 10）：上方白卡「BowBow 從○○來玩了！」＋營地裡多一隻背背包的客人（只顯示最早到的）。
    /// - 回家蓋章（流程 11）：時間到了誰先打開 App 誰結算一次；每個成員各看一次蓋章卡，「看護照」打開護照。
    /// 執行時自動建立；只在首頁顯示（相機、相簿、聊天、其他頁面時藏起來）。
    /// </summary>
    public class TravelHomeOverlay : MonoBehaviour
    {
        const float DesignW = 390f;
        const string PrefsSeenReturn = "BlockPet.Travel.SeenReturn.";

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color SubText = Hex(0x6B5D4E);
        static readonly Color Brown = Hex(0x8A6A4E);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color Track = Hex(0xEFE6D4);
        static readonly Color Blue = Hex(0x6F8FE0);
        static readonly Color Dim = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.55f);

        static TravelHomeOverlay _instance;

        TravelSkin _skin;
        TMP_FontAsset _font;
        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _root, _top, _sign, _guest, _backLayer, _stamp;
        RectTransform _awayCard, _hostCard;
        TMP_Text _awayTitle, _awaySub, _hostTitle, _hostSub;
        RectTransform _awayFill;
        PageManager _pages;
        CameraUIManager _cameraUI;
        SpriteRenderer[] _hiddenPet;
        string _hostDismissedFor;
        float _nextCheck, _backT;

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

            _top = Design(_root, "Top");
            _awayCard = BuildAwayCard();
            _hostCard = BuildHostCard();
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
            float topInset = TopInset();
            _top.localScale = Vector3.one * k;
            _top.anchoredPosition = new Vector2(0f, -topInset);

            float cardY = 90f;
            _awayCard.gameObject.SetActive(away);
            if (away)
            {
                UpdateAway(trip, phase, now);
                _awayCard.anchoredPosition = new Vector2(14f, -cardY);
                cardY += _awayCard.sizeDelta.y + 10f;
            }
            _sign.gameObject.SetActive(away);
            if (away) FollowPet(_sign, k, -55f, -120f);

            // 客人（只顯示最早到、現在在這裡的那隻）
            FirebaseManager.TravelGuest guest = null;
            int more = 0;
            foreach (var g in fb.Guests)
            {
                if (now < g.arriveAt || now >= g.leaveAt) continue;
                if (guest == null) guest = g; else more++;
            }
            bool showHost = guest != null && _hostDismissedFor != guest.from + guest.arriveAt;
            _hostCard.gameObject.SetActive(showHost);
            if (showHost)
            {
                string pet = string.IsNullOrEmpty(guest.petName) ? "寵物" : guest.petName;
                _hostTitle.text = $"{pet} 從{guest.fromName}來玩了！";
                string ours = fb.CurrentPetName;
                string until = DateTimeOffset.FromUnixTimeMilliseconds(guest.leaveAt).ToLocalTime().ToString("HH:mm");
                _hostSub.text = $"會在這裡玩到 {until}" + (!string.IsNullOrEmpty(ours) && !away ? $"，跟{ours}一起" : "") + (more > 0 ? $"・還有 {more} 隻" : "");
                _hostCard.anchoredPosition = new Vector2(14f, -cardY);
            }
            _guest.gameObject.SetActive(guest != null);
            if (guest != null)
            {
                _guestTag.text = guest.fromName;
                float tw = Mathf.Ceil(_guestTag.GetPreferredValues(guest.fromName).x) + 16f;
                ((RectTransform)_guestTag.transform.parent).sizeDelta = new Vector2(tw, 17f);
                ((RectTransform)_guestTag.transform.parent).anchoredPosition = new Vector2(60f - tw / 2f, 24f);
                _guestTag.rectTransform.sizeDelta = new Vector2(tw, 17f);
                FollowPet(_guest, k, 60f, -104f);
                _guest.GetChild(0).localPosition = new Vector3(0f, 4f * Mathf.Sin(Time.unscaledTime / 2f * Mathf.PI * 2f), 0f);
            }

            UpdateBack(fb);
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
        void FollowPet(RectTransform rt, float k, float dx, float dy)
        {
            var pet = AIController.Instance;
            var cam = Camera.main;
            Vector2 local;
            if (pet != null && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(pet.transform.position);
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

        RectTransform BuildAwayCard()
        {
            var card = Place(_top, "AwayCard", 14, 90, DesignW - 28f, 104);
            UIShapes.AddShadow(card, new Color(0f, 0f, 0f, 0.22f), 20, 20, 8);
            UIShapes.AddRounded(Stretch(card, "Bg").gameObject, new Color(Paper.r, Paper.g, Paper.b, 0.95f), 20).raycastTarget = false;
            float w = DesignW - 28f - 28f;
            _awayTitle = Label(card, 14, 12, w, 20, "", 15, Ink, TextAlignmentOptions.Left);
            _awaySub = Label(card, 14, 34, w, 16, "", 12, SubText, TextAlignmentOptions.Left);
            var track = Place(card, "Track", 14, 56, w, 6);
            UIShapes.AddRounded(track.gameObject, Track, 3).raycastTarget = false;
            _awayFill = Place(track, "Fill", 0, 0, 6, 6);
            UIShapes.AddRounded(_awayFill.gameObject, Yellow, 3).raycastTarget = false;
            _awayNote = Label(card, 14, 70, w, 16, "", 11, Brown, TextAlignmentOptions.Left);
            card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
            return card;
        }

        TMP_Text _awayNote, _guestTag;

        void UpdateAway(FirebaseManager.TravelTrip t, TripPhase phase, long now)
        {
            var fb = FirebaseManager.Instance;
            string pet = fb != null && !string.IsNullOrEmpty(fb.CurrentPetName) ? fb.CurrentPetName : "寵物";
            string to = string.IsNullOrEmpty(t.toName) ? "好友營地" : t.toName;
            string homeAt = DateTimeOffset.FromUnixTimeMilliseconds(t.HomeAt).ToLocalTime().ToString("HH:mm");
            _awayTitle.text = $"{pet} 去{to}旅行了";
            _awaySub.text = phase == TripPhase.Going
                ? $"還要 {TripRules.FormatMinutes(TripRules.MinutesUntil(now, TripRules.ArriveAt(t.departAt, t.oneWayMin)))}到，{homeAt} 回家"
                : phase == TripPhase.There ? $"正在{to}玩，{homeAt} 回家" : $"正在回家，{homeAt} 到家";
            _awayNote.text = $"照片照常餵，會寄給旅行中的 {pet}（進化、金幣照算）";
            float w = DesignW - 28f - 28f;
            _awayFill.sizeDelta = new Vector2(Mathf.Max(6f, w * (float)TripRules.Progress(now, t.departAt, t.oneWayMin, t.stayMin)), 6f);
        }

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
            Label(rt, 0, 24, 110, 22, "去旅行了！", 14, Ink, TextAlignmentOptions.Center);
            rt.gameObject.SetActive(false);
            return rt;
        }

        // ─── 客人來玩（流程 10）───────────────────────────────────────

        RectTransform BuildHostCard()
        {
            var card = Place(_top, "HostCard", 14, 90, DesignW - 28f, 72);
            UIShapes.AddShadow(card, new Color(0f, 0f, 0f, 0.25f), 20, 24, 10);
            var bg = UIShapes.AddRounded(Stretch(card, "Bg").gameObject, Paper, 20);
            var av = Place(card, "Avatar", 14, 12, 48, 48);
            UIShapes.AddRounded(av.gameObject, Hex(0xFCE7A6), 24).raycastTarget = false;
            av.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            if (_skin != null && _skin.pet != null)
            {
                var pi = Place(av, "Pet", 3, 6, 42, 36).gameObject.AddComponent<Image>();
                pi.sprite = _skin.pet;
                pi.preserveAspect = true;
                pi.raycastTarget = false;
            }
            float w = DesignW - 28f - 74f - 14f;
            _hostTitle = Label(card, 74, 16, w, 20, "", 15, Ink, TextAlignmentOptions.Left);
            _hostTitle.overflowMode = TextOverflowModes.Ellipsis;
            _hostSub = Label(card, 74, 38, w, 16, "", 11, SubText, TextAlignmentOptions.Left);
            var b = card.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(() =>
            {
                // 點一下收起來（同一隻客人這次不再跳）
                var fb = FirebaseManager.Instance;
                if (fb == null) return;
                long now = fb.ServerNowMs;
                foreach (var g in fb.Guests) if (now >= g.arriveAt && now < g.leaveAt) { _hostDismissedFor = g.from + g.arriveAt; break; }
            });
            card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
            return card;
        }

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

        // ─── 回家蓋章（流程 11）───────────────────────────────────────

        void UpdateBack(FirebaseManager fb)
        {
            var last = fb.LastTripReturn;
            string key = PrefsSeenReturn + fb.RoomId + "." + fb.GetUserId();
            bool want = last != null && last.at > 0 && last.at > long.Parse(PlayerPrefs.GetString(key, "0"))
                        && fb.ServerNowMs - last.at < 3L * 24 * 3600 * 1000 && IsHome(); // 太久以前的就不補了
            if (want && !_backLayer.gameObject.activeSelf) BuildBack(fb, last, key);
            if (_backLayer.gameObject.activeSelf && _stamp != null)
            {
                // 蓋章 0.6 秒（延遲 0.2）：0.5 倍→1.12→1，轉 -20°→-6°→-8°
                _backT += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01((_backT - 0.2f) / 0.6f);
                float sc = t < 0.7f ? Mathf.Lerp(0.5f, 1.12f, t / 0.7f) : Mathf.Lerp(1.12f, 1f, (t - 0.7f) / 0.3f);
                float rot = t < 0.7f ? Mathf.Lerp(-20f, -6f, t / 0.7f) : Mathf.Lerp(-6f, -8f, (t - 0.7f) / 0.3f);
                _stamp.localScale = Vector3.one * (t <= 0f ? 0.01f : sc);
                _stamp.localRotation = Quaternion.Euler(0f, 0f, -rot); // CSS 角度＝順時針
            }
        }

        void BuildBack(FirebaseManager fb, FirebaseManager.TripReturn last, string key)
        {
            for (int i = _backLayer.childCount - 1; i >= 0; i--) Destroy(_backLayer.GetChild(i).gameObject);
            var dim = _backLayer.gameObject.GetComponent<Image>();
            if (dim == null) dim = _backLayer.gameObject.AddComponent<Image>();
            dim.color = Dim;
            float k = _root.rect.width / DesignW;
            var design = Design(_backLayer, "Design");
            design.localScale = Vector3.one * k;
            design.anchoredPosition = new Vector2(0f, -TopInset());

            string pet = !string.IsNullOrEmpty(fb.CurrentPetName) ? fb.CurrentPetName : "寵物";
            const float W = DesignW - 60f, Inner = W - 36f;
            var card = Place(design, "Card", 30, 150, W, 420);
            UIShapes.AddShadow(card, new Color(0f, 0f, 0f, 0.35f), 24, 40, 16);
            if (_skin != null && _skin.popupCard != null) { var ci = Stretch(card, "Bg").gameObject.AddComponent<Image>(); ci.sprite = _skin.popupCard; ci.type = Image.Type.Sliced; }
            else UIShapes.AddRounded(Stretch(card, "Bg").gameObject, Paper, 24);

            float y = 18f;
            Label(card, 18, y, Inner, 17, $"{pet} 回家了", 12, Brown, TextAlignmentOptions.Center); y += 17f + 8f;
            Label(card, 18, y, Inner, 30, $"去了{last.toName}", 22, Ink, TextAlignmentOptions.Center); y += 30f + 12f;

            // 護照內頁 220 × 170：中間摺線、左邊淡淡的舊章、右邊新章蓋下去
            var page = Place(card, "Page", (W - 220f) / 2f, y, 220, 170);
            if (_skin != null && _skin.passportPage != null) { var pi = page.gameObject.AddComponent<Image>(); pi.sprite = _skin.passportPage; pi.type = Image.Type.Sliced; pi.raycastTarget = false; }
            else UIShapes.AddRounded(page.gameObject, Hex(0xF2E6CB), 8).raycastTarget = false;
            var fold = Place(page, "Fold", 110, 10, 1, 150).gameObject.AddComponent<Image>();
            fold.color = Hex(0xD9C79A);
            fold.raycastTarget = false;
            var old = Place(page, "Old", 16, 22, 76, 76);
            old.pivot = new Vector2(0.5f, 0.5f);
            old.anchoredPosition = new Vector2(16f + 38f, -(22f + 38f));
            old.localRotation = Quaternion.Euler(0f, 0f, -10f);
            UIShapes.AddRing(old.gameObject, new Color(0x4F / 255f, 0x8F / 255f, 0x45 / 255f, 0.55f), 38).raycastTarget = false;

            Color col = ColorUtility.TryParseHtmlString(TripRules.StampColor(last.to), out var c) ? c : Hex(0xC2452B);
            _stamp = Place(page, "Stamp", 118, 26, 92, 92);
            _stamp.pivot = new Vector2(0.5f, 0.5f);
            _stamp.anchoredPosition = new Vector2(118f + 46f, -(26f + 46f));
            if (_skin != null && _skin.stampRing != null) { var si = Stretch(_stamp, "Ring").gameObject.AddComponent<Image>(); si.sprite = _skin.stampRing; si.color = col; si.raycastTarget = false; }
            else UIShapes.AddRing(Stretch(_stamp, "Ring").gameObject, col, 46).raycastTarget = false;
            string shortName = last.toName != null && last.toName.Length > 5 ? last.toName.Substring(0, 5) : last.toName;
            Label(_stamp, 0, 22, 92, 12, shortName, 9, col, TextAlignmentOptions.Center);
            if (_skin != null && _skin.pet != null)
            {
                var pi2 = Place(_stamp, "Pet", 27, 32, 38, 33).gameObject.AddComponent<Image>();
                pi2.sprite = _skin.pet; pi2.preserveAspect = true; pi2.raycastTarget = false;
            }
            Label(_stamp, 0, 68, 92, 12, DateTimeOffset.FromUnixTimeMilliseconds(last.at).ToLocalTime().ToString("MM.dd"), 8, col, TextAlignmentOptions.Center);
            _stamp.localScale = Vector3.one * 0.01f;
            _backT = 0f;
            Label(page, 0, 146, 220, 14, $"旅行護照 第 {Mathf.Max(1, last.stampNo)} 個章", 10, Brown, TextAlignmentOptions.Center);
            y += 170f + 10f;

            string photos = last.photos > 0 ? $"旅行時大家餵了 {last.photos} 張照片，{pet} 都收到了" : "旅行時沒有人餵照片，下次記得寄照片給牠";
            var pl = Label(card, 18, y, Inner, 34, photos, 12, SubText, TextAlignmentOptions.Center);
            pl.enableWordWrapping = true;
            float ph = Mathf.Ceil(pl.GetPreferredValues(photos, Inner, 0f).y);
            pl.rectTransform.sizeDelta = new Vector2(Inner, ph);
            y += ph + 14f;

            var btn = Place(card, "Passport", 18, y, Inner, 53);
            UIShapes.AddRounded(Place(btn, "Edge", 0, 3, Inner, 50).gameObject, YellowEdge, 15).raycastTarget = false;
            var bimg = UIShapes.AddRounded(Place(btn, "Fill", 0, 0, Inner, 50).gameObject, Yellow, 15);
            Label(btn, 0, 0, Inner, 50, "看護照", 15, Ink, TextAlignmentOptions.Center);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = bimg;
            b.onClick.AddListener(() =>
            {
                PlayerPrefs.SetString(key, last.at.ToString());
                PlayerPrefs.Save();
                _backLayer.gameObject.SetActive(false);
                TravelScreen.OpenPassport();
            });
            y += 53f + 18f;
            card.sizeDelta = new Vector2(W, y);
            _backLayer.gameObject.SetActive(true);
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
