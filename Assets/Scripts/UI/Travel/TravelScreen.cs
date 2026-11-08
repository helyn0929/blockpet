using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Travel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Travel
{
    /// <summary>
    /// 明信片地圖（設計文件 5.21，2026-11-08「寄明信片」取代「旅行」；地圖沿用設計稿 TravelGrow）：陸地、家、好友營地大頭針、路、
    /// 地形裝飾（湖、小山丘、樹、小花）、路邊小物、下一塊地的雲霧；頂部（返回、「明信片地圖」、黃色「抽好友」）與底部（護照、狀態卡）。
    /// 抽好友在 TravelScreen.Draw；選照片、寄出、護照（明信片收藏）在 TravelScreen.Postcard。
    /// 地圖用營地代碼當種子在執行時生成（<see cref="TravelMapGen"/>）。
    /// 介面以 390 × 772 設計座標建立，依螢幕寬度等比縮放、上下置中；海鋪滿整個畫面。
    /// 入口：首頁右上選單「旅行」（MenuToggle；選單的字維持「旅行」，2026-11-08 使用者決定）。
    /// </summary>
    public partial class TravelScreen : MonoBehaviour
    {
        const float DesignW = 390f;
        const float DesignH = 772f;

        // 第一步測試用（之後換成營地的營地代碼與 Firebase 的營地好友）
        public const string DemoCampCode = "K7R2PX";
        public const int DemoFriendCount = 3;
        const int DemoStage = 1; // 設計稿階段：1＝3 個好友

        static readonly Color Sea = Hex(0x9FCFD6);
        static readonly Color Shallow = Hex(0xB8E0E4);
        static readonly Color Sand = Hex(0xF3E7C9);
        static readonly Color Grass = Hex(0xBFDE9C);
        static readonly Color RoadEdge = Hex(0xDCEBC8);
        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Cream = Hex(0xFFFCF4);
        static readonly Color LabelBg = new Color(1f, 248f / 255f, 236f / 255f, 0.95f);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color SceneGreen = Hex(0x8FA868);
        static readonly Color Brown = Hex(0x8A6A4E);
        static readonly Color SubText = Hex(0x6B5D4E);
        static readonly Color LakeEdge = Hex(0xE8F4EC);
        static readonly Color Hill = Hex(0xA6CF82);
        static readonly Color HillLight = Hex(0xC9E6AA);
        static readonly Color Tree = Hex(0x6BAA55);
        static readonly Color TreeDark = Hex(0x5A9548);
        static readonly Color TreeLight = Hex(0x8CC56E);
        static readonly Color TreeShadow = new Color(0x4A / 255f, 0x7A / 255f, 0x3A / 255f, 0.22f);
        static readonly Color[] FlowerColors = { Hex(0xF6C945), Hex(0xF28FA6), Color.white };
        static readonly Color StampRed = Hex(0xC2452B);
        static readonly Color FogText = Hex(0x6B8A94);
        static readonly Color HeaderFill = new Color(1f, 248f / 255f, 236f / 255f, 0.94f);
        static readonly Color HeaderLine = Hex(0xEFE6D4);
        static readonly Color InviteBlue = Hex(0x6F8FE0);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color PassportBlue = Hex(0x3F5BA8);
        static readonly Color PassportGold = Hex(0xFFE9A8);
        // 第一步假資料：營地名字、好友邀請數
        const string DemoCampName = "家人營地";
        const int DemoInvites = 1;

        // 對方營地／寵物的上色（和風景明信片同一套，PostcardArt）
        static readonly Color[] FriendPetTint = PostcardArt.FriendPetTint;
        static readonly Color[] FriendSceneTint = PostcardArt.FriendSceneTint;

        static TravelScreen _instance;

        TravelSkin _skin;
        PostcardSkin _pc;
        TMP_FontAsset _font;
        Canvas _canvas;
        RectTransform _root, _content, _map, _wave, _headerFill, _header, _bottom, _fogText;
        bool _capture;
        string _code = DemoCampCode;

        /// <summary>目前營地的營地代碼；還沒有（剛建立、代碼產生中）時先用示範代碼。</summary>
        static string CampCode()
        {
            var fb = FirebaseManager.Instance;
            return fb != null && !string.IsNullOrEmpty(fb.CurrentCampCode) ? fb.CurrentCampCode : DemoCampCode;
        }
        RectTransform _homePet;
        float _lastW = -1f, _lastH = -1f;
        Rect _lastSafe;

        // ─── 入口 ───────────────────────────────────────────────────────

        public static void Open()
        {
            // 地圖用目前營地的營地代碼當種子；代碼換了（換空間）就重建
            string code = CampCode();
            if (_instance != null && _instance._code != code) { Destroy(_instance.gameObject); _instance = null; }
            if (_instance == null)
            {
                var go = new GameObject("TravelScreen (runtime)", typeof(RectTransform));
                _instance = go.AddComponent<TravelScreen>();
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 38;
                go.AddComponent<GraphicRaycaster>();
                _instance._code = code;
                _instance.Build(canvas);
            }
            _instance.gameObject.SetActive(true);
        }

        /// <summary>建在指定的畫布上（Editor 截圖用；不放返回鍵、不跟著安全區）。</summary>
        public static TravelScreen BuildForCapture(Canvas canvas)
        {
            var s = canvas.gameObject.AddComponent<TravelScreen>();
            s._capture = true;
            s.Build(canvas);
            s.Relayout();
            return s;
        }

        void OnDestroy()
        {
            FirebaseManager.OnCampFriendsChanged -= OnFriendsData;
            FirebaseManager.OnTravelChanged -= OnFriendsData;
            FirebaseManager.OnPostcardChanged -= OnPostcardData;
            FirebaseManager.OnRoomMetaChanged -= OnFriendsData;
            if (_instance == this) _instance = null;
        }

        void Close() => gameObject.SetActive(false);

        // ─── 版面 ──────────────────────────────────────────────────────

        void Update()
        {
            Relayout();
            // 家裡的寵物上下輕跳（2.4 秒）
            if (_homePet != null)
                _homePet.anchoredPosition = new Vector2(18f, -25f + 4f * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime / 2.4f * Mathf.PI * 2f)));
            AnimateSheet();
            AnimateOverlays();
            AnimateDraw();
            if (!_capture) TickTrip();
            // 「抽到好友後這裡會長出地」左右飄 6px（5 秒）
            if (_fogText != null)
                _fogText.anchoredPosition = new Vector2(_fogX + 6f * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime / 5f * Mathf.PI * 2f)), _fogText.anchoredPosition.y);
        }

        float _fogX;

        public void Relayout()
        {
            float w = _root.rect.width, h = _root.rect.height;
            var safe = Screen.safeArea;
            if (w <= 0f || h <= 0f) return;
            if (Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH) && safe == _lastSafe) return;
            _lastW = w; _lastH = h; _lastSafe = safe;
            float k = w / DesignW;
            _map.localScale = Vector3.one * k;
            // 海浪紋：跟地圖同一個座標，上下多鋪（20 的倍數，紋路才對得齊）
            float extra = Mathf.Ceil(Mathf.Max(0f, h / k - DesignH) / 2f / 20f) * 20f + 20f;
            _wave.localScale = Vector3.one * k;
            _wave.sizeDelta = new Vector2(DesignW, DesignH + extra * 2f);
            // 頂部、底部跟著安全區（截圖時沒有安全區）
            float topInset = 0f, bottomInset = 0f;
            if (!_capture)
            {
                float scale = _canvas.rootCanvas.scaleFactor;
                float px = scale > 0f ? 1f / scale : 1f;
                topInset = (Screen.height - safe.yMax) * px;
                bottomInset = safe.yMin * px;
            }
            _headerFill.sizeDelta = new Vector2(0f, topInset + 74f * k);
            _header.localScale = Vector3.one * k;
            _header.anchoredPosition = new Vector2(0f, -topInset);
            _bottom.localScale = Vector3.one * k;
            _bottom.anchoredPosition = new Vector2(0f, bottomInset);
        }

        // ─── 建立 ──────────────────────────────────────────────────────

        void Build(Canvas canvas)
        {
            _canvas = canvas;
            _root = (RectTransform)canvas.transform;
            _skin = TravelSkin.Load();
            if (_skin == null) Debug.LogWarning("[Travel] 找不到 Resources/UI/TravelSkin（執行 BlockPet/Travel/Build Travel Skin）");
            _font = _skin != null ? _skin.font : null;
            _pc = PostcardSkin.Load();
            if (_pc == null) Debug.LogWarning("[Postcard] 找不到 Resources/UI/PostcardSkin（執行 BlockPet/Postcard/Build Postcard Skin）");

            _content = Stretch(_root, "Content");
            BuildSheetLayer();
            BuildContent();
            if (!_capture)
            {
                var fb = FirebaseManager.Instance;
                fb?.StartCampFriends();
                fb?.StartTravelState();
                FirebaseManager.OnTravelChanged += OnFriendsData;
                FirebaseManager.OnCampFriendsChanged += OnFriendsData;
                FirebaseManager.OnPostcardChanged += OnPostcardData;
                fb?.PublishCampProfile();
                FirebaseManager.OnRoomMetaChanged += OnFriendsData;
            }
        }

        void OnFriendsData() { if (this != null) { CheckNewFriends(); RebuildContent(); } }

        int _shownMatchIn = -1, _shownPostcards = -1, _shownCost = -1;

        /// <summary>抽好友、明信片的資料變了：頂部紅點、價格、護照張數有變才重畫地圖；抽好友畫面跟著更新。</summary>
        void OnPostcardData()
        {
            if (this == null) return;
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            int mi = fb.MatchIn.Count, pcs = fb.VisiblePostcards().Count, cost = fb.DrawCostNow;
            if (mi != _shownMatchIn || pcs != _shownPostcards || cost != _shownCost) RebuildContent();
            OnPostcardDataDraw();
        }

        /// <summary>好友、邀請、名字變了：整張地圖和頂部底部重畫（面板留著）。</summary>
        void RebuildContent()
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _homePet = null; _fogText = null;
            BuildContent();
            _lastW = -1f;
            Relayout();
        }

        /// <summary>地圖資料：截圖或沒有 Firebase 時用示範假資料；遊戲裡用營地的營地好友（依格子）。</summary>
        void FriendsData(out List<int> slots, out List<string> names, out List<int> trips, out int invites)
        {
            slots = new List<int>(); names = new List<string>(); trips = new List<int>(); invites = 0;
            _friendRecords.Clear();
            var fb = FirebaseManager.Instance;
            if (_capture || fb == null || string.IsNullOrEmpty(fb.RoomId))
            {
                TravelMapGen.DemoFriends(_code, DemoFriendCount, DemoStage, out var dn, out var dt);
                for (int i = 0; i < dn.Length; i++) { slots.Add(i); names.Add(dn[i]); trips.Add(dt[i]); }
                invites = _capture ? DemoInvites : 0;
                return;
            }
            foreach (var f in fb.CampFriends)
            {
                if (f.slot < 0 || f.slot >= TravelMapGen.MaxFriends) continue; // 還沒分格子：等一下
                slots.Add(f.slot);
                names.Add(string.IsNullOrEmpty(f.name) ? "營地好友" : f.name);
                trips.Add(f.trips);
                _friendRecords.Add(f);
            }
            invites = fb.MatchIn.Count;
            _shownMatchIn = invites;
            _shownPostcards = fb.VisiblePostcards().Count;
            _shownCost = fb.DrawCostNow;
        }

        readonly List<FirebaseManager.CampFriend> _friendRecords = new List<FirebaseManager.CampFriend>();
        TravelMap _mapData;

        void BuildContent()
        {
            FriendsData(out var slots, out var names, out var trips, out int invites);

            // 海 #9FCFD6＋白色細波浪紋
            var sea = Stretch(_content, "Sea").gameObject.AddComponent<Image>();
            sea.color = Sea;
            _wave = Centered(_content, "Waves");
            if (_skin != null && _skin.seaWave != null)
            {
                var wave = _wave.gameObject.AddComponent<Image>();
                wave.sprite = _skin.seaWave;
                wave.type = Image.Type.Tiled;
                wave.raycastTarget = false;
            }

            _map = Centered(_content, "Map");
            _map.sizeDelta = new Vector2(DesignW, DesignH);

            var map = TravelMapGen.Generate(_code, slots, trips);
            _mapData = map;
            _shownPhase = Phase(Trip());
            BuildMap(map, names.ToArray());

            int stamps = 0; // 護照裡收到的明信片張數
            var fbS = FirebaseManager.Instance;
            if (!_capture && fbS != null && !string.IsNullOrEmpty(fbS.RoomId)) stamps = fbS.VisiblePostcards().Count;
            else foreach (var t in trips) if (t > 0) stamps++;
            BuildHeader(map.friends.Count, invites);
            BuildBottom(map.friends.Count, stamps);
        }

        // ─── 旅行中（流程 6、8）────────────────────────────────────────

        /// <summary>目前的旅行（截圖時沒有）。</summary>
        FirebaseManager.TravelTrip Trip()
        {
            if (_capture) return null;
            var fb = FirebaseManager.Instance;
            return fb != null ? fb.CurrentTrip : null;
        }

        static long Now()
        {
            var fb = FirebaseManager.Instance;
            return fb != null ? fb.ServerNowMs : System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        TripPhase Phase(FirebaseManager.TravelTrip t) => t == null ? TripPhase.None : TripRules.Phase(Now(), t.departAt, t.oneWayMin, t.stayMin);

        /// <summary>旅行目的地在地圖上的第幾個（好友清單裡），沒有是 -1。</summary>
        int TripIndex(FirebaseManager.TravelTrip t)
        {
            if (t == null) return -1;
            for (int i = 0; i < _friendRecords.Count; i++) if (_friendRecords[i].roomId == t.to) return i;
            return -1;
        }

        MapCurveGraphic _activeRoute;
        RectTransform _walker;
        MapRoad _walkRoad;
        TMP_Text _statusTitle, _statusSub;
        RectTransform _progressFill;
        float _progressW;
        TripPhase _shownPhase = TripPhase.None;
        float _nextTick;

        /// <summary>每一格：紅線流動、寵物在路上走、狀態卡文字與進度；階段換了就整張重畫。</summary>
        void TickTrip()
        {
            var t = Trip();
            var phase = Phase(t);
            if (phase != _shownPhase) { _shownPhase = phase; RebuildContent(); return; }
            if (t == null || phase == TripPhase.None) return;
            if (_activeRoute != null) _activeRoute.SetFlow(Time.unscaledTime / 0.8f * 12f); // 0.8 秒流 12（設計稿 24／2）
            long now = Now();
            if (_walker != null)
            {
                var p = _walkRoad.At(TripRules.RoadT(now, t.departAt, t.oneWayMin, t.stayMin));
                float w = Mathf.Sin(Time.unscaledTime / 0.8f * Mathf.PI * 2f);
                _walker.anchoredPosition = new Vector2((float)p.x - 28f, -((float)p.y - 44f) + 4f * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime / 0.8f * Mathf.PI * 4f)));
                _walker.localRotation = Quaternion.Euler(0f, 0f, -4f * w);
            }
            if (Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + 1f;
                StatusTexts(t, phase, out var title, out var sub);
                if (_statusTitle != null) _statusTitle.text = title;
                if (_statusSub != null) _statusSub.text = sub;
                if (_progressFill != null) _progressFill.sizeDelta = new Vector2(Mathf.Max(6f, _progressW * (float)TripRules.Progress(now, t.departAt, t.oneWayMin, t.stayMin)), 6f);
            }
        }

        void StatusTexts(FirebaseManager.TravelTrip t, TripPhase phase, out string title, out string sub)
        {
            string pet = PetName(), to = string.IsNullOrEmpty(t.toName) ? "好友營地" : t.toName;
            long now = Now();
            switch (phase)
            {
                case TripPhase.Going:
                    title = $"{pet} 正在送明信片去{to}";
                    sub = $"還要 {TripRules.FormatMinutes(TripRules.MinutesUntil(now, TripRules.ArriveAt(t.departAt, t.oneWayMin)))}到・照片照常餵";
                    break;
                case TripPhase.There:
                    title = $"{pet} 到{to}了！";
                    sub = $"點地圖上的營地看牠在那邊玩・再 {TripRules.FormatMinutes(TripRules.MinutesUntil(now, TripRules.LeaveAt(t.departAt, t.oneWayMin, t.stayMin)))}回家";
                    break;
                case TripPhase.Returning:
                    title = $"{pet} 正在回家";
                    sub = $"還要 {TripRules.FormatMinutes(TripRules.MinutesUntil(now, t.HomeAt))}到家・照片照常餵";
                    break;
                default:
                    title = $"{pet} 回家了";
                    sub = $"帶回一張{to}的風景明信片，收在護照";
                    break;
            }
        }

        // ─── 頂部、底部 ────────────────────────────────────────────────

        /// <summary>營地名字（空間名稱）；截圖或還沒載入時用示範的「家人營地」。</summary>
        string CampName()
        {
            var fb = FirebaseManager.Instance;
            string n = !_capture && fb != null ? fb.CurrentRoomName : null;
            return string.IsNullOrEmpty(n) ? DemoCampName : n;
        }

        string PetName()
        {
            var fb = FirebaseManager.Instance;
            string n = !_capture && fb != null ? fb.CurrentPetName : null;
            if (string.IsNullOrEmpty(n) && !_capture && PetCollectionManager.Instance != null) n = PetCollectionManager.Instance.GetPetDisplayName();
            return string.IsNullOrEmpty(n) ? "BowBow" : n;
        }

        void BuildHeader(int friends, int invites)
        {
            // 底 rgba(255,248,236,0.94)＋下緣線 #EFE6D4，延伸到瀏海後面
            _headerFill = new GameObject("HeaderFill", typeof(RectTransform)).GetComponent<RectTransform>();
            _headerFill.SetParent(_content, false);
            _headerFill.anchorMin = new Vector2(0f, 1f);
            _headerFill.anchorMax = new Vector2(1f, 1f);
            _headerFill.pivot = new Vector2(0.5f, 1f);
            var fill = _headerFill.gameObject.AddComponent<Image>();
            fill.color = HeaderFill;
            var line = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>();
            line.SetParent(_headerFill, false);
            line.anchorMin = Vector2.zero;
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.sizeDelta = new Vector2(0f, 1f);
            line.gameObject.AddComponent<Image>().color = HeaderLine;

            _header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
            _header.SetParent(_content, false);
            _header.anchorMin = _header.anchorMax = new Vector2(0.5f, 1f);
            _header.pivot = new Vector2(0.5f, 1f);
            _header.sizeDelta = new Vector2(DesignW, 74f);

            var back = Place(_header, "Back", 12, 14, 44, 44);
            var backImg = SkinImage(back, _skin?.back, Hex(0xF2E9D8), 22, false);
            if (_skin == null || _skin.back == null) Label(back, 0, 0, 44, 44, "‹", 22, Ink);
            Button(back, backImg, Close);

            var title = Label(_header, 68, 12, 160, 28, "明信片地圖", 24, Ink);
            title.alignment = TextAlignmentOptions.Left;
            var sub = Label(_header, 68, 41, 200, 18, $"{CampName()}・{friends} 個好友", 12, Brown);
            sub.alignment = TextAlignmentOptions.Left;
            BuildDrawButton(invites);
        }

        void BuildBottom(int friends, int stamps)
        {
            // 左右 14、下 22：左下護照 64 × 76，右邊狀態卡（至少 64 高，和護照底部對齊）
            _bottom = new GameObject("Bottom", typeof(RectTransform)).GetComponent<RectTransform>();
            _bottom.SetParent(_content, false);
            _bottom.anchorMin = _bottom.anchorMax = new Vector2(0.5f, 0f);
            _bottom.pivot = new Vector2(0.5f, 0f);
            _bottom.sizeDelta = new Vector2(DesignW, 98f);

            var passport = Place(_bottom, "Passport", 14, 0, 64, 76);
            UIShapes.AddShadow(passport, new Color(0f, 0f, 0f, 0.3f), 14, 14, 6);
            var pImg = SkinImage(passport, _skin?.passportButton, PassportBlue, 14, false);
            Label(passport, 6, 34, 58, 16, "護照", 11, PassportGold);
            Label(passport, 6, 50, 58, 14, $"{stamps} 張", 10, Color.white);
            Button(passport, pImg, OpenPassportView);

            var trip = Trip();
            var phase = Phase(trip);
            bool traveling = phase != TripPhase.None;
            float ch = traveling ? 74f : 64f;
            float cx = 14f + 64f + 10f, cw = DesignW - 14f - cx;
            var card = Place(_bottom, "Status", cx, 76f - ch, cw, ch);
            UIShapes.AddShadow(card, new Color(0f, 0f, 0f, 0.2f), 18, 14, 6);
            SkinImage(card, _skin?.statusCard, new Color(1f, 248f / 255f, 236f / 255f, 0.96f), 18, true);
            string title, sub;
            if (traveling) StatusTexts(trip, phase, out title, out sub);
            else
            {
                title = friends == 0 ? "地圖才剛開始" : $"{PetName()} 在家";
                sub = friends == 0 ? "抽好友，雲霧就會散開長出新的地" : "點好友營地，寄一張明信片過去";
            }
            _statusTitle = Label(card, 14, traveling ? 10 : 13, cw - 28f, 20, title, 14, Ink);
            _statusTitle.alignment = TextAlignmentOptions.Left;
            _statusSub = Label(card, 14, traveling ? 33 : 36, cw - 28f, 16, sub, 11, SubText);
            _statusSub.alignment = TextAlignmentOptions.Left;
            _progressFill = null;
            if (traveling)
            {
                _progressW = cw - 28f;
                var track = Place(card, "Track", 14, 55, _progressW, 6);
                UIShapes.AddRounded(track.gameObject, HeaderLine, 3).raycastTarget = false;
                _progressFill = Place(track, "Fill", 0, 0, 6, 6);
                UIShapes.AddRounded(_progressFill.gameObject, Yellow, 3).raycastTarget = false;
                long now = Now();
                _progressFill.sizeDelta = new Vector2(Mathf.Max(6f, _progressW * (float)TripRules.Progress(now, trip.departAt, trip.oneWayMin, trip.stayMin)), 6f);
            }
        }

        /// <summary>有素材用素材（sliced 時 9-slice），沒有用圓角方塊。</summary>
        Image SkinImage(RectTransform rt, Sprite sprite, Color fallback, float radius, bool sliced)
        {
            Image img;
            if (sprite != null)
            {
                img = rt.gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            }
            else img = UIShapes.AddRounded(rt.gameObject, fallback, radius);
            img.raycastTarget = false;
            return img;
        }

        static void Button(RectTransform rt, Image graphic, UnityEngine.Events.UnityAction onClick)
        {
            graphic.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = graphic;
            if (onClick != null) b.onClick.AddListener(onClick);
        }

        void BuildMap(TravelMap map, string[] names)
        {
            // 陸地：三層圓（淺水 r+20 → 沙灘 r+7 → 草地 r）
            var layers = new[] { ("Shallow", Shallow, 20.0), ("Sand", Sand, 7.0), ("Grass", Grass, 0.0) };
            foreach (var (layerName, col, add) in layers)
            {
                var layer = Stretch(_map, layerName);
                foreach (var c in map.land) Disc(layer, c.c, c.r + add, col);
            }

            // 湖：#9FCFD6、邊 3 #E8F4EC、白色小波紋
            var lakes = Stretch(_map, "Lakes");
            foreach (var l in map.lakes)
            {
                Ellipse(lakes, l.c, l.rx + 1.5, l.ry + 1.5, LakeEdge);
                Ellipse(lakes, l.c, l.rx - 1.5, l.ry - 1.5, Sea);
                // 'M x-0.5rx y q 0.25rx -3 0.5rx 0 t 0.5rx 0'
                var p0 = new MapPoint(l.c.x - l.rx * 0.5, l.c.y);
                var p1 = new MapPoint(p0.x + l.rx * 0.5, p0.y);
                var p2 = new MapPoint(p0.x + l.rx, p0.y);
                var wave = new Color(1f, 1f, 1f, 0.6f);
                CurveRaw(lakes, p0, new MapPoint(p0.x + l.rx * 0.25, p0.y - 3), p1, 1.6f, wave);
                CurveRaw(lakes, p1, new MapPoint(p0.x + l.rx * 0.75, p0.y + 3), p2, 1.6f, wave);
            }
            // 小山丘：橢圓 #A6CF82＋左上亮面 #C9E6AA
            var hills = Stretch(_map, "Hills");
            foreach (var hl in map.hills)
            {
                Ellipse(hills, hl.c, hl.rx, hl.ry, Hill);
                Ellipse(hills, new MapPoint(hl.c.x - hl.rx * 0.25, hl.c.y - hl.rx * 0.15), hl.rx * 0.45, hl.rx * 0.22, HillLight);
            }

            // 路：去過＝外框 #DCEBC8＋實線白路；沒去過＝白色虛線（3 8，透明 90%）
            var roads = Stretch(_map, "Roads");
            foreach (var r in map.roads)
            {
                if (r.Visited) Curve(roads, r, (float)r.EdgeWidth, RoadEdge, 0f, 0f);
                Curve(roads, r, (float)r.Width, r.Visited ? Color.white : new Color(1f, 1f, 1f, 0.9f), r.Visited ? 0f : 3f, r.Visited ? 0f : 8f);
            }

            // 小花 2.2
            var flowers = Stretch(_map, "Flowers");
            foreach (var f in map.flowers) Ellipse(flowers, f.c, 2.2, 2.2, FlowerColors[Mathf.Clamp(f.color, 0, 2)]);

            // 路邊小物（長椅、路牌、花圃、路燈）
            var props = Stretch(_map, "Props");
            foreach (var pr in map.props) Prop(props, pr);

            // 樹：淡影、樹冠、左上亮點（依 y 排好）
            var trees = Stretch(_map, "Trees");
            foreach (var t in map.trees)
            {
                Ellipse(trees, new MapPoint(t.c.x + 2, t.c.y + t.r * 0.8), t.r, t.r * 0.4, TreeShadow);
                Ellipse(trees, t.c, t.r, t.r, t.dark ? TreeDark : Tree);
                Ellipse(trees, new MapPoint(t.c.x - t.r * 0.3, t.c.y - t.r * 0.3), t.r * 0.45, t.r * 0.45, TreeLight);
            }

            // 下一塊地的雲霧＋小字
            var fog = Stretch(_map, "Fog");
            for (int i = 0; i < map.fog.Count; i++)
                Ellipse(fog, map.fog[i].c, map.fog[i].r, map.fog[i].r, new Color(1f, 1f, 1f, i == map.fog.Count - 1 ? 0.9f : 0.85f));

            // 旅途中：那條路變紅色流動虛線（#E86B4F，寬 5，2 10）
            var trip = Trip();
            var phase = Phase(trip);
            int ti = TripIndex(trip);
            _activeRoute = null;
            _walker = null;
            if (ti >= 0 && phase != TripPhase.None && phase != TripPhase.Done)
            {
                var r = map.roads[ti];
                var route = Stretch(_map, "ActiveRoute");
                Curve(route, r, 5f, Coral, 2f, 10f);
                _activeRoute = route.GetComponentInChildren<MapCurveGraphic>();
            }

            // 好友營地大頭針
            var pins = Stretch(_map, "Pins");
            if (map.hasNext)
            {
                _fogX = (float)map.next.x - 60f;
                var ft = Label(pins, _fogX, (float)map.next.y - 12f, 120, 34, "抽到好友後\n這裡會長出地", 11, FogText);
                ft.lineSpacing = -10f;
                _fogText = ft.rectTransform;
            }
            for (int i = 0; i < map.friends.Count; i++)
            {
                bool dest = i == ti && (phase == TripPhase.Going || phase == TripPhase.There);
                int idx = i;
                Pin(pins, map.friends[i], i < names.Length ? names[i] : "", map.slotIndices[i], map.roads[i].trips,
                    dest && phase == TripPhase.There ? $"{PetName()} 在這裡" : dest ? "目的地" : null,
                    dest && phase == TripPhase.There ? Coral : InviteBlue,
                    dest && phase == TripPhase.There,
                    () => OnPinTapped(idx));
            }

            // 走在路上的寵物（背小背包，左右搖擺 0.8 秒）
            if (ti >= 0 && (phase == TripPhase.Going || phase == TripPhase.Returning))
            {
                _walkRoad = map.roads[ti];
                _walker = Place(pins, "Walker", 0, 0, 56, 52);
                _walker.pivot = new Vector2(0.5f, 0.15f);
                if (_skin != null && _skin.pet != null)
                {
                    var pi = Place(_walker, "Pet", 0, 0, 56, 48).gameObject.AddComponent<Image>();
                    pi.sprite = _skin.pet;
                    pi.preserveAspect = true;
                    pi.raycastTarget = false;
                    if (phase == TripPhase.Returning) pi.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                }
                if (_skin != null && _skin.backpack != null)
                {
                    var bp = Place(_walker, "Backpack", 30, 14, 18, 20).gameObject.AddComponent<Image>();
                    bp.sprite = _skin.backpack;
                    bp.raycastTarget = false;
                }
            }

            // 家（最上層）
            Home(Stretch(_map, "Home"), map.home, CampName() + "（家）", phase != TripPhase.None && phase != TripPhase.Done);
        }

        void Disc(RectTransform parent, MapPoint c, double r, Color col)
        {
            float d = (float)r * 2f;
            var rt = Place(parent, "Land", (float)c.x - d / 2f, (float)c.y - d / 2f, d, d);
            if (_skin != null && _skin.landDisc != null)
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = _skin.landDisc;
                img.color = col;
                img.raycastTarget = false;
            }
            else UIShapes.AddRounded(rt.gameObject, col, (float)r).raycastTarget = false;
        }

        void Ellipse(RectTransform parent, MapPoint c, double rx, double ry, Color col)
        {
            float w = (float)rx * 2f, h = (float)ry * 2f;
            var rt = Place(parent, "E", (float)c.x - w / 2f, (float)c.y - h / 2f, w, h);
            if (_skin != null && _skin.landDisc != null)
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = _skin.landDisc;
                img.color = col;
                img.raycastTarget = false;
            }
            else UIShapes.AddRounded(rt.gameObject, col, Mathf.Min(w, h) / 2f).raycastTarget = false;
        }

        static void CurveRaw(RectTransform parent, MapPoint a, MapPoint c, MapPoint b, float w, Color col)
        {
            Curve(parent, new MapRoad { from = a, ctrl = c, to = b }, w, col, 0f, 0f);
        }

        /// <summary>路邊小物：圖的中心和設計稿 SVG 的原點對齊（各自的偏移照設計稿的圖形範圍）。</summary>
        void Prop(RectTransform parent, MapProp p)
        {
            Sprite sprite; float w, h, oy;
            switch (p.kind)
            {
                case 0: sprite = _skin?.propBench; w = 16; h = 12; oy = 1.5f; break;
                case 1: sprite = _skin?.propSign; w = 18; h = 20; oy = -2f; break;
                case 2: sprite = _skin?.propFlowerbed; w = 20; h = 12; oy = 0.5f; break;
                default: sprite = _skin?.propLamp; w = 10; h = 24; oy = -4.8f; break;
            }
            if (sprite == null) return;
            var rt = Place(parent, "Prop", (float)p.c.x - w / 2f, (float)p.c.y + oy - h / 2f, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
        }

        static void Curve(RectTransform parent, MapRoad r, float w, Color col, float dash, float gap)
        {
            var rt = Stretch(parent, "Road");
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(DesignW, DesignH);
            rt.anchoredPosition = Vector2.zero;
            rt.gameObject.AddComponent<MapCurveGraphic>().Set(r.from, r.ctrl, r.to, w, col, dash, gap);
        }

        /// <summary>好友營地：56 圓（邊 3 #FFFCF4，裡面是對方場景＋寵物），下方奶油色名牌 10 Black。</summary>
        void Pin(RectTransform parent, MapPoint p, string name, int index, int trips,
                 string tag = null, Color tagColor = default, bool redRing = false, UnityEngine.Events.UnityAction onTap = null)
        {
            float x = (float)p.x, y = (float)p.y;
            // 點的範圍：大頭針＋名牌（100 × 66）
            if (onTap != null)
            {
                var hit = Place(parent, "Hit " + name, x - 50f, y - 30f, 100, 66);
                var hitImg = hit.gameObject.AddComponent<Image>();
                hitImg.color = new Color(1f, 1f, 1f, 0f);
                Button(hit, hitImg, onTap);
            }
            var pin = Place(parent, "Pin " + name, x - 28f, y - 30f, 56, 56);
            UIShapes.AddShadow(pin, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.3f), 28, 10, 4);
            Portrait(pin, 56f, 3f, _skin?.pinMask, -26f, -102f, 110f, 217f, 10f, 15f, 34f, 30f,
                     FriendSceneTint[index % FriendSceneTint.Length], FriendPetTint[index % FriendPetTint.Length], out _);
            Frame(pin, _skin?.pinFrame, 0f, 56f, Cream, 3f);
            if (redRing) UIShapes.AddRing(Place(pin, "Red", 0, 0, 56, 56).gameObject, Coral, 28).raycastTarget = false; // 目的地紅框
            NameLabel(parent, x, y + 29f, name, 10, _skin?.labelBg, LabelBg, 16f, 8f, 0f);
            // 標籤：「目的地」藍／「BowBow 在這裡」紅（大頭針上方 -20）
            if (!string.IsNullOrEmpty(tag))
            {
                var probe = Label(parent, 0, 0, 10, 16, tag, 10, Color.white);
                float tw = Mathf.Ceil(TextWidth(probe, tag)) + 16f;
                var tr = Place(parent, "Tag", x - tw / 2f, y - 30f - 20f, tw, 17);
                if (_skin != null && _skin.tagPill != null) { var ti2 = tr.gameObject.AddComponent<Image>(); ti2.sprite = _skin.tagPill; ti2.type = Image.Type.Sliced; ti2.color = tagColor; ti2.raycastTarget = false; }
                else UIShapes.AddRounded(tr.gameObject, tagColor, 8.5f).raycastTarget = false;
                probe.rectTransform.SetParent(tr, false);
                probe.rectTransform.anchoredPosition = Vector2.zero;
                probe.rectTransform.sizeDelta = new Vector2(tw, 17f);
                probe.transform.SetAsLastSibling();
            }

            // 寄過明信片的營地：大頭針右上一張小郵票（pc_stamp 22，轉 8°），兩次以上旁邊「×2」（2026-11-08 取代紅章）
            if (trips > 0)
            {
                var stamp = Place(parent, "Stamp", x + 14f, y - 40f, 24, 24);
                stamp.pivot = new Vector2(0.5f, 0.5f);
                stamp.anchoredPosition = new Vector2(x + 14f + 12f, -(y - 40f + 12f));
                stamp.localRotation = Quaternion.Euler(0f, 0f, -8f);
                if (_pc != null && _pc.stamp != null)
                {
                    var img = stamp.gameObject.AddComponent<Image>();
                    img.sprite = _pc.stamp;
                    img.raycastTarget = false;
                }
                else UIShapes.AddRounded(stamp.gameObject, Hex(0xF7B6C2), 3).raycastTarget = false;
                string t = PostcardRules.StampLabel(trips);
                if (t.Length > 0)
                {
                    var times = Label(parent, x + 37f, y - 38f, 30, 16, t, 10, Ink);
                    times.alignment = TextAlignmentOptions.Left;
                }
            }
        }

        /// <summary>家：76 圓（邊 4 #F6C945＋外光圈），黃色名牌「家人營地（家）」11 Black。</summary>
        void Home(RectTransform parent, MapPoint p, string name, bool away)
        {
            float x = (float)p.x, y = (float)p.y;
            var home = Place(parent, "Home", x - 38f, y - 44f, 76, 76);
            // 外光圈 4（rgba(246,201,69,0.35)）＋陰影
            if (_skin == null || _skin.homeFrame == null)
                UIShapes.AddRounded(Place(home, "Glow", -4, -4, 84, 84).gameObject, new Color(Yellow.r, Yellow.g, Yellow.b, 0.35f), 42).raycastTarget = false;
            UIShapes.AddShadow(home, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.3f), 38, 14, 6);
            Portrait(home, 76f, 4f, _skin?.homeMask, -32f, -134f, 140f, 277f, 14f, 21f, 42f, 37f, Color.white, Color.white, out _homePet);
            if (away)
            {
                // 出門中：寵物不在，蓋一層奶油色＋「出門中」
                if (_homePet != null) _homePet.gameObject.SetActive(false);
                _homePet = null;
                var cover = Place(home, "Away", 0, 0, 76, 76);
                UIShapes.AddRounded(cover.gameObject, new Color(1f, 248f / 255f, 236f / 255f, 0.6f), 38).raycastTarget = false;
                Label(cover, 0, 0, 76, 76, "出門中", 11, SubText);
            }
            Frame(home, _skin?.homeFrame, -6f, 88f, Yellow, 4f);
            NameLabel(parent, x, y + 36f, name, 11, _skin?.homeLabel, Yellow, 20f, 10f, 2f);
        }

        /// <summary>
        /// 圓形照片：營地場景＋寵物（設計稿用 img 絕對位置，相對外框內側；border 是外框的粗細）。
        /// </summary>
        void Portrait(RectTransform circle, float size, float border, Sprite mask, float bgL, float bgT, float bgW, float bgH,
                      float petL, float petT, float petW, float petH, Color sceneTint, Color petTint, out RectTransform pet)
        {
            var holder = Stretch(circle, "Photo");
            Image maskImg;
            if (mask != null) { maskImg = holder.gameObject.AddComponent<Image>(); maskImg.sprite = mask; }
            else maskImg = UIShapes.AddRounded(holder.gameObject, Color.white, size / 2f);
            maskImg.raycastTarget = false;
            holder.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            maskImg.color = SceneGreen;

            var camp = _skin != null ? _skin.campBackground : null;
            if (camp != null)
            {
                // 底圖在外框內側的位置：left bgL、top bgT、bgW × bgH → 算出圓裡看得到的那一塊
                float ox = border + bgL, oy = border + bgT;
                var raw = Stretch(holder, "Scene").gameObject.AddComponent<RawImage>();
                raw.texture = camp;
                raw.color = sceneTint;
                raw.raycastTarget = false;
                raw.uvRect = new Rect(-ox / bgW, 1f - (size - oy) / bgH, size / bgW, size / bgH);
            }
            pet = Place(holder, "Pet", border + petL, border + petT, petW, petH);
            if (_skin != null && _skin.pet != null)
            {
                var img = pet.gameObject.AddComponent<Image>();
                img.sprite = _skin.pet;
                img.preserveAspect = true;
                img.color = petTint;
                img.raycastTarget = false;
            }
        }

        void Frame(RectTransform circle, Sprite sprite, float offset, float size, Color fallback, float width)
        {
            var rt = Place(circle, "Frame", offset, offset, size, size);
            if (sprite != null)
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
            }
            else
            {
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(circle.sizeDelta.x, circle.sizeDelta.y);
                UIShapes.AddRing(rt.gameObject, fallback, circle.sizeDelta.x / 2f).raycastTarget = false;
            }
        }

        /// <summary>名牌：以 (cx, top) 置中；左右內距 padX；含下緣 bottom（黃色名牌有 2px 下緣）。</summary>
        void NameLabel(RectTransform parent, float cx, float top, string text, float size, Sprite sprite, Color fallback, float h, float padX, float bottom)
        {
            var probe = Label(parent, 0, 0, 10, h - bottom, text, size, Ink);
            float w = Mathf.Ceil(TextWidth(probe, text)) + padX * 2f;
            var rt = Place(parent, "Label", cx - w / 2f, top, w, h);
            if (sprite != null)
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;
            }
            else
            {
                if (bottom > 0f) UIShapes.AddRounded(Place(rt, "Edge", 0, bottom, w, h - bottom).gameObject, YellowEdge, (h - bottom) / 2f).raycastTarget = false;
                UIShapes.AddRounded(Place(rt, "Bg", 0, 0, w, h - bottom).gameObject, fallback, (h - bottom) / 2f).raycastTarget = false;
            }
            probe.rectTransform.SetParent(rt, false);
            probe.rectTransform.anchoredPosition = Vector2.zero;
            probe.rectTransform.sizeDelta = new Vector2(w, h - bottom);
            probe.transform.SetAsLastSibling();
        }

        // ─── 面板（下方滑出）────────────────────────────────────────────

        enum SheetKind { None, Send }
        SheetKind _sheetKind;
        RectTransform _sheetLayer, _sheetPanel;
        Image _sheetDim;
        float _sheetT;
        bool _sheetOpen;
        static readonly Color Dim = new Color(20f / 255f, 24f / 255f, 14f / 255f, 1f);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Disabled = Hex(0xE2D6C0);
        static readonly Color BlueSoft = Hex(0xE2E8F8);
        static readonly Color BlueDeep = Hex(0x4F6FC4);
        static readonly Color ErrorRed = Hex(0xC2452B);

        void BuildSheetLayer()
        {
            _sheetLayer = Stretch(_root, "Sheet");
            _sheetDim = _sheetLayer.gameObject.AddComponent<Image>();
            _sheetDim.color = new Color(Dim.r, Dim.g, Dim.b, 0f);
            var b = _sheetLayer.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(CloseSheet);
            _sheetPanel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            _sheetPanel.SetParent(_sheetLayer, false);
            _sheetPanel.anchorMin = _sheetPanel.anchorMax = new Vector2(0.5f, 0f);
            _sheetPanel.pivot = new Vector2(0.5f, 0f);
            _sheetPanel.sizeDelta = new Vector2(DesignW, 300f);
            _sheetLayer.gameObject.SetActive(false);
        }

        void ShowSheet()
        {
            bool was = _sheetOpen && _sheetLayer.gameObject.activeSelf;
            _sheetLayer.gameObject.SetActive(true);
            _sheetOpen = true;
            if (!was) _sheetT = 0f;
        }

        void CloseSheet()
        {
            _sheetOpen = false;
            _sheetKind = SheetKind.None;
        }

        void AnimateSheet()
        {
            if (_sheetLayer == null || !_sheetLayer.gameObject.activeSelf) return;
            _sheetT = Mathf.MoveTowards(_sheetT, _sheetOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.3f);
            float e = 1f - Mathf.Pow(1f - _sheetT, 3f);
            _sheetDim.color = new Color(Dim.r, Dim.g, Dim.b, 0.45f * Mathf.Clamp01(_sheetT / 0.8f));
            float k = _root.rect.width / DesignW;
            _sheetPanel.localScale = Vector3.one * k;
            _sheetPanel.anchoredPosition = new Vector2(0f, -_sheetPanel.sizeDelta.y * k * (1f - e));
            if (!_sheetOpen && _sheetT <= 0f) _sheetLayer.gameObject.SetActive(false);
        }

        float BottomInsetDesign()
        {
            if (_capture) return 0f;
            float scale = _canvas.rootCanvas.scaleFactor;
            float px = scale > 0f ? 1f / scale : 1f;
            float k = _root.rect.width / DesignW;
            return k > 0f ? Screen.safeArea.yMin * px / k : 0f;
        }

        /// <summary>面板共用：底、把手、標題列（標題＋右邊 × 44）。回傳下一個元件的 y。</summary>
        float SheetFrame(string title)
        {
            float y = SheetBase();
            var t = Label(_sheetPanel, 18, y, 260, 44, title, 21, Ink);
            t.alignment = TextAlignmentOptions.Left;
            var close = Place(_sheetPanel, "Close", DesignW - 18f - 44f, y, 44, 44);
            var cImg = UIShapes.AddRounded(close.gameObject, Beige, 22);
            Label(close, 0, 0, 44, 44, "×", 20, Ink);
            Button(close, cImg, CloseSheet);
            return y + 44f + 12f;
        }

        /// <summary>面板的底與把手；回傳把手下面的 y。</summary>
        float SheetBase()
        {
            for (int i = _sheetPanel.childCount - 1; i >= 0; i--) Destroy(_sheetPanel.GetChild(i).gameObject);
            var bg = Stretch(_sheetPanel, "Bg");
            UIShapes.AddShadow(bg, new Color(0f, 0f, 0f, 0.25f), 26, 30, -10);
            var bgImg = UIShapes.AddRounded(Stretch(bg, "Fill").gameObject, Paper, 26);
            bgImg.raycastTarget = true; // 點面板不會關
            // 下面兩個角不要圓：多蓋一塊方的
            var sq = Place(bg, "Square", 0, 0, DesignW, 30);
            sq.anchorMin = new Vector2(0f, 0f); sq.anchorMax = new Vector2(1f, 0f); sq.pivot = new Vector2(0.5f, 0f);
            sq.anchoredPosition = Vector2.zero; sq.sizeDelta = new Vector2(0f, 30f);
            sq.gameObject.AddComponent<Image>().color = Paper;

            UIShapes.AddRounded(Place(_sheetPanel, "Grabber", (DesignW - 40f) / 2f, 12, 40, 5).gameObject, Line, 2.5f).raycastTarget = false;
            return 12f + 5f + 12f;
        }

        void FinishSheet(float y)
        {
            _sheetPanel.sizeDelta = new Vector2(DesignW, y + 26f + BottomInsetDesign());
        }

        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Line = Hex(0xE2D6C0);

        // ─── 寄明信片（5.21 ④-1：點好友營地 → 面板 → 選一張照片）──────────

        void OnPinTapped(int idx)
        {
            if (_capture || idx < 0 || idx >= _friendRecords.Count) return;
            var t = Trip();
            var phase = Phase(t);
            if (phase == TripPhase.None) { OpenSend(idx); return; }
            if (phase == TripPhase.There && TripIndex(t) == idx) { OpenThere(); return; }
            ShowToast(phase == TripPhase.Done ? "寵物剛回家，等一下再出門" : $"{PetName()} 正在送明信片，一次只能去一個地方");
        }

        int _sendIdx;
        bool _departing;

        void OpenSend(int idx)
        {
            _sendIdx = idx;
            _sheetKind = SheetKind.Send;
            ShowSheet();
            BuildSendSheet();
        }

        int OneWayFor(int idx) => _mapData != null && idx < _mapData.roads.Count
            ? TripRules.OneWayMinutes(_mapData.home, _mapData.roads[idx].to) : 30;

        void BuildSendSheet()
        {
            float y = SheetBase();
            const float W = DesignW - 36f;
            var f = _friendRecords[_sendIdx];
            int slot = Mathf.Max(0, f.slot);
            int oneWay = OneWayFor(_sendIdx);

            // 對方營地的場景＋寵物，左下營地名、「去過 N 次」
            var hero = Place(_sheetPanel, "Hero", 18, y, W, 130);
            var mask = UIShapes.AddRounded(hero.gameObject, SceneGreen, 18);
            mask.raycastTarget = false;
            hero.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var camp = _skin != null ? _skin.campBackground : null;
            if (camp != null)
            {
                var raw = Stretch(hero, "Scene").gameObject.AddComponent<RawImage>();
                raw.texture = camp;
                raw.color = FriendSceneTint[slot % FriendSceneTint.Length];
                raw.raycastTarget = false;
                raw.uvRect = new Rect(0f, 1f - 530f / 700f, 1f, 130f / 700f);
            }
            var shade = Place(hero, "Shade", 0, 70, W, 60).gameObject.AddComponent<RawImage>();
            shade.texture = GradientTex();
            shade.raycastTarget = false;
            if (_skin != null && _skin.pet != null)
            {
                var pet = Place(hero, "Pet", 230, 30, 90, 76).gameObject.AddComponent<Image>();
                pet.sprite = _skin.pet;
                pet.preserveAspect = true;
                pet.color = FriendPetTint[slot % FriendPetTint.Length];
                pet.raycastTarget = false;
            }
            var hn = Label(hero, 14, 82, W - 28f, 16, f.name, 12, Color.white);
            hn.alignment = TextAlignmentOptions.Left;
            var ht = Label(hero, 14, 96, W - 28f, 26, $"寄過 {f.trips} 次", 20, Color.white);
            ht.alignment = TextAlignmentOptions.Left;
            y += 130f + 12f;

            // 路程／在那邊／回到家
            float cw = (W - 16f) / 3f;
            InfoCell(18, y, cw, "路程", "單程 " + TripRules.FormatMinutes(oneWay));
            InfoCell(18 + cw + 8f, y, cw, "在那邊", TripRules.StayMinutes + " 分");
            InfoCell(18 + (cw + 8f) * 2f, y, cw, "回到家", "約 " + TripRules.FormatMinutes(oneWay * 2 + TripRules.StayMinutes));
            y += 52f + 12f;

            string pet2 = PetName();
            var desc = Label(_sheetPanel, 18, y, W, 20, $"選一張照片做成拍立得明信片，{pet2} 會帶過去。有人臉的照片不能寄；回家時會帶回一張那裡的風景明信片。", 12, SubText);
            desc.enableWordWrapping = true;
            desc.alignment = TextAlignmentOptions.TopLeft;
            desc.lineSpacing = 12f;
            float dh = Mathf.Ceil(TextHeight(desc, desc.text, W));
            desc.rectTransform.sizeDelta = new Vector2(W, dh);
            y += dh + 12f;

            var go = Place(_sheetPanel, "Go", 18, y, W, 58);
            UIShapes.AddRounded(Place(go, "Edge", 0, 4, W, 54).gameObject, YellowEdge, 18).raycastTarget = false;
            var goImg = UIShapes.AddRounded(Place(go, "Fill", 0, 0, W, 54).gameObject, Yellow, 18);
            string goText = _departing ? "出發中…" : "選一張照片";
            var gl = Label(go, 0, 0, W, 54, goText, 17, Ink);
            Button(go, goImg, () => { if (!_departing) OpenPick(PickMode.Send, _sendIdx, null); });
            y += 58f + 10f;
            // 封鎖・檢舉（好友營地的面板，5.21 ⑦）
            BlockReportLinks(_sheetPanel, y, W, f.roomId, f.name, null, "friend_sheet", SubText);
            FinishSheet(y + 24f);
        }

        void InfoCell(float x, float y, float w, string label, string value)
        {
            var rt = Place(_sheetPanel, "Info", x, y, w, 52);
            SkinImage(rt, _skin?.infoCell, Cream, 14, true);
            var l = Label(rt, 12, 9, w - 24f, 14, label, 10, Brown);
            l.alignment = TextAlignmentOptions.Left;
            var v = Label(rt, 12, 25, w - 24f, 20, value, 15, Ink);
            v.alignment = TextAlignmentOptions.Left;
            v.enableAutoSizing = true;
            v.fontSizeMin = 11;
            v.fontSizeMax = 15;
        }

        static Texture2D _gradient;

        /// <summary>上透明、下黑 50% 的漸層（大圖下方的字比較清楚）。</summary>
        static Texture2D GradientTex()
        {
            if (_gradient != null) return _gradient;
            _gradient = new Texture2D(1, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < 16; i++) _gradient.SetPixel(0, i, new Color(0f, 0f, 0f, 0.5f * (1f - i / 15f)));
            _gradient.Apply();
            return _gradient;
        }

        // ─── 在對方營地玩（流程 9，TravelMapThere）＋護照（流程 12）──────────

        RectTransform _thereLayer, _thereDesign, _passportLayer, _hostPet, _guestPet;
        TMP_Text _thereSub;
        RectTransform _toast;
        TMP_Text _toastText;
        float _toastUntil;

        void ShowToast(string msg)
        {
            if (_toast == null)
            {
                _toast = new GameObject("Toast", typeof(RectTransform)).GetComponent<RectTransform>();
                _toast.SetParent(_root, false);
                _toast.anchorMin = _toast.anchorMax = new Vector2(0.5f, 0.5f);
                _toast.sizeDelta = new Vector2(300f, 40f);
                UIShapes.AddRounded(_toast.gameObject, new Color(Ink.r, Ink.g, Ink.b, 0.88f), 20).raycastTarget = false;
                _toastText = Label(_toast, 0, 0, 300, 40, "", 13, Color.white);
            }
            float k = _root.rect.width / DesignW;
            _toast.localScale = Vector3.one * k;
            _toastText.text = msg;
            float w = Mathf.Min(360f, Mathf.Ceil(TextWidth(_toastText, msg)) + 32f);
            _toast.sizeDelta = new Vector2(w, 40f);
            _toastText.rectTransform.sizeDelta = new Vector2(w, 40f);
            _toast.SetAsLastSibling();
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 2.2f;
        }

        void AnimateOverlays()
        {
            if (_toast != null && _toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toast.gameObject.SetActive(false);
            if (_thereLayer != null && _thereLayer.gameObject.activeSelf)
            {
                float t = Time.unscaledTime;
                if (_hostPet != null) _hostPet.anchoredPosition = new Vector2(60f + 30f * (0.5f - 0.5f * Mathf.Cos(t / 7f * Mathf.PI * 2f)), -420f + 5f * (0.5f - 0.5f * Mathf.Cos(t / 2.6f * Mathf.PI * 2f)));
                if (_guestPet != null) _guestPet.anchoredPosition = new Vector2(210f, -440f + 5f * (0.5f - 0.5f * Mathf.Cos(t / 2f * Mathf.PI * 2f)));
                var trip = Trip();
                if (Phase(trip) != TripPhase.There) { _thereLayer.gameObject.SetActive(false); return; }
                if (_thereSub != null) _thereSub.text = $"再玩 {TripRules.FormatMinutes(TripRules.MinutesUntil(Now(), TripRules.LeaveAt(trip.departAt, trip.oneWayMin, trip.stayMin)))}就回家";
                float k = _root.rect.width / DesignW;
                _thereDesign.localScale = Vector3.one * k;
            }
        }

        void OpenThere()
        {
            var trip = Trip();
            if (trip == null) return;
            if (_thereLayer != null) Destroy(_thereLayer.gameObject);
            _thereLayer = Stretch(_root, "There");
            _thereLayer.SetSiblingIndex(_content.GetSiblingIndex() + 1);
            int slot = Mathf.Max(0, trip.toSlot);
            // 滿版對方營地
            var bg = Stretch(_thereLayer, "Camp").gameObject.AddComponent<RawImage>();
            bg.color = FriendSceneTint[slot % FriendSceneTint.Length];
            var camp = _skin != null ? _skin.campBackground : null;
            if (camp != null)
            {
                bg.texture = camp;
                var fit = bg.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = (float)camp.width / Mathf.Max(1, camp.height);
            }
            else bg.color = SceneGreen;

            _thereDesign = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _thereDesign.SetParent(_thereLayer, false);
            _thereDesign.anchorMin = _thereDesign.anchorMax = new Vector2(0.5f, 1f);
            _thereDesign.pivot = new Vector2(0.5f, 1f);
            _thereDesign.sizeDelta = new Vector2(DesignW, DesignH);
            float k = _root.rect.width / DesignW;
            _thereDesign.localScale = Vector3.one * k;
            float topInset = 0f;
            if (!_capture)
            {
                float sc = _canvas.rootCanvas.scaleFactor;
                topInset = (Screen.height - Screen.safeArea.yMax) * (sc > 0f ? 1f / sc : 1f);
            }
            _thereDesign.anchoredPosition = new Vector2(0f, -topInset);

            string pet = PetName();
            string host = string.IsNullOrEmpty(trip.toPet) ? "主人" : trip.toPet;
            // 主人寵物（左）＋客人（右，翻面、背背包）
            _hostPet = Place(_thereDesign, "Host", 60, 420, 130, 120);
            UIShapes.AddRounded(Place(_hostPet, "Shadow", 15, 104, 100, 16).gameObject, new Color(30f / 255f, 45f / 255f, 20f / 255f, 0.3f), 8).raycastTarget = false;
            PetImg(_hostPet, 0, 0, 130, 112, FriendPetTint[slot % FriendPetTint.Length], false);
            _guestPet = Place(_thereDesign, "Guest", 210, 440, 120, 112);
            UIShapes.AddRounded(Place(_guestPet, "Shadow", 14, 96, 92, 15).gameObject, new Color(30f / 255f, 45f / 255f, 20f / 255f, 0.3f), 7.5f).raycastTarget = false;
            PetImg(_guestPet, 0, 0, 120, 104, Color.white, true);
            if (_skin != null && _skin.backpack != null)
            {
                var bp = Place(_guestPet, "Backpack", 14, 40, 30, 34).gameObject.AddComponent<Image>();
                bp.sprite = _skin.backpack;
                bp.raycastTarget = false;
            }
            Bubble(_thereDesign, 92, 368, "歡迎你來！", true);
            Bubble(_thereDesign, 236, 392, "我來玩囉～", false);

            // 上方毛玻璃 bar：返回地圖、「BowBow 在○○」「再玩 N 分就回家」、藍色「旅行中」
            var bar = Place(_thereDesign, "Bar", 10, 10, DesignW - 20f, 52);
            SkinImage(bar, _skin?.glassBar, new Color(1f, 248f / 255f, 236f / 255f, 0.62f), 26, true);
            var back = Place(bar, "Back", 6, 6, 40, 40);
            var bImg = SkinImage(back, _skin?.backSmall, Paper, 20, false);
            if (_skin == null || _skin.backSmall == null) Label(back, 0, 0, 40, 40, "‹", 20, Ink);
            Button(back, bImg, () => _thereLayer.gameObject.SetActive(false));
            var t1 = Label(bar, 54, 8, 220, 20, $"{pet} 在{trip.toName}", 14, Ink);
            t1.alignment = TextAlignmentOptions.Left;
            t1.overflowMode = TextOverflowModes.Ellipsis;
            _thereSub = Label(bar, 54, 28, 220, 16, "", 11, SubText);
            _thereSub.alignment = TextAlignmentOptions.Left;
            var tag = Place(bar, "Traveling", DesignW - 20f - 12f - 56f, 16, 56, 20);
            SkinImage(tag, _skin?.tagTraveling, InviteBlue, 10, true);
            Label(tag, 0, 0, 56, 20, "送信中", 11, Color.white);

            // 底部：只看得到營地和寵物
            var bottom = new GameObject("Note", typeof(RectTransform)).GetComponent<RectTransform>();
            bottom.SetParent(_thereLayer, false);
            bottom.anchorMin = new Vector2(0f, 0f); bottom.anchorMax = new Vector2(1f, 0f); bottom.pivot = new Vector2(0.5f, 0f);
            bottom.sizeDelta = new Vector2(0f, 70f * k + BottomInsetDesign() * k);
            var fade = bottom.gameObject.AddComponent<RawImage>();
            fade.texture = CreamFadeTex();
            fade.raycastTarget = false;
            var note = Label(bottom, 0, 0, 10, 10, "只看得到營地和寵物・明信片收在對方的護照", 12, SubText);
            note.rectTransform.anchorMin = new Vector2(0f, 0f); note.rectTransform.anchorMax = new Vector2(1f, 0f);
            note.rectTransform.pivot = new Vector2(0.5f, 0f);
            note.rectTransform.anchoredPosition = new Vector2(0f, (26f + BottomInsetDesign()) * k);
            note.rectTransform.sizeDelta = new Vector2(0f, 18f * k);
            note.fontSize = 12f * k;
        }

        void PetImg(RectTransform parent, float x, float y, float w, float h, Color tint, bool flip)
        {
            if (_skin == null || _skin.pet == null) return;
            var img = Place(parent, "Pet", x, y, w, h).gameObject.AddComponent<Image>();
            img.sprite = _skin.pet;
            img.preserveAspect = true;
            img.color = tint;
            img.raycastTarget = false;
            if (flip) img.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
        }

        void Bubble(RectTransform parent, float x, float y, string text, bool tailRight)
        {
            var probe = Label(parent, 0, 0, 10, 26, text, 13, Ink);
            float w = Mathf.Ceil(TextWidth(probe, text)) + 22f;
            var rt = Place(parent, "Bubble", x, y, w, 30);
            var sprite = tailRight ? _skin?.bubbleRight : _skin?.bubbleLeft;
            UIShapes.AddShadow(rt, new Color(0f, 0f, 0f, 0.18f), 14, 8, 3);
            SkinImage(rt, sprite, Color.white, 14, true);
            probe.rectTransform.SetParent(rt, false);
            probe.rectTransform.anchoredPosition = Vector2.zero;
            probe.rectTransform.sizeDelta = new Vector2(w, 30f);
            probe.transform.SetAsLastSibling();
        }

        static Texture2D _creamFade;

        static Texture2D CreamFadeTex()
        {
            if (_creamFade != null) return _creamFade;
            _creamFade = new Texture2D(1, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < 16; i++) _creamFade.SetPixel(0, i, new Color(1f, 248f / 255f, 236f / 255f, 0.96f * Mathf.Clamp01((15f - i) / 10f)));
            _creamFade.Apply();
            return _creamFade;
        }

        static Color HexStr(string s) => ColorUtility.TryParseHtmlString(s, out var c) ? c : Color.white;

        /// <summary>黃色按鈕＋下緣 3 #D9A92C。回傳上面那層（按鈕的圖）。</summary>
        static Image YellowButton(RectTransform rt, float w, float h, float r)
        {
            UIShapes.AddRounded(Place(rt, "Edge", 0, 3, w, h).gameObject, YellowEdge, r).raycastTarget = false;
            return UIShapes.AddRounded(Place(rt, "Fill", 0, 0, w, h).gameObject, Yellow, r);
        }

        static string Ago(long ms)
        {
            if (ms <= 0) return "剛剛";
            var fb = FirebaseManager.Instance;
            long now = fb != null ? fb.ServerNowMs : System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var d = System.TimeSpan.FromMilliseconds(Mathf.Max(0, now - ms));
            if (d.TotalMinutes < 1) return "剛剛";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} 分鐘前";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours} 小時前";
            return $"{(int)d.TotalDays} 天前";
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

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size, Color color)
        {
            var t = Place(parent, "Text", left, top, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.richText = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static RectTransform Centered(RectTransform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(DesignW, DesignH);
            return rt;
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
