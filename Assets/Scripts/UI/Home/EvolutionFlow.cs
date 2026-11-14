using System;
using BlockPet.Feed;
using BlockPet.Hand;
using BlockPet.UI.TopBar;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Home
{
    /// <summary>
    /// 進化流程（設計文件 5.17 ④-2、⑤，2026-11-14）：
    /// - 滿 100 張：點寵物（HandController）或進化條（EvoBarView）→ Request()：用營地照片算出型／主色／副色，
    ///   Firebase 交易寫 Rooms/{營地}/evolution（別人先點了就用那個結果）→ 播進化動畫；點的人那台記圖鑑。
    /// - 其他成員：第一次看到進化後的寵物時補播一次（每人一次，記在手機上）。
    /// - 80～99 張：寵物旁邊發光的剪影（預告的一句話在寵物泡泡，HandController）。
    /// - 進化後寵物上主色（EvolvedLook，暫用，待美術）。
    /// 執行時自動建立，只在首頁顯示。
    /// </summary>
    public class EvolutionFlow : MonoBehaviour
    {
        const float DesignW = 390f, DesignH = 772f;
        const string PrefsSeen = "BlockPet.Evo.Seen.";
        const float AnimSeconds = 2.5f;

        static readonly Color Ink = new Color(0x3A / 255f, 0x2E / 255f, 0x22 / 255f);
        static readonly Color Paper = new Color(1f, 248f / 255f, 236f / 255f);
        static readonly Color Yellow = new Color(0xF6 / 255f, 0xC9 / 255f, 0x45 / 255f);
        static readonly Color YellowEdge = new Color(0xD9 / 255f, 0xA9 / 255f, 0x2C / 255f);

        static EvolutionFlow _instance;

        /// <summary>進化動畫正在播。</summary>
        public static bool Playing => _instance != null && _instance._playing;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _root, _anim, _design, _petRt, _glowRt, _textRt, _okRt;
        Image _dim, _pet, _glow, _flash, _silhouette;
        TMP_Text _title, _sub;
        TMP_FontAsset _font;
        bool _playing, _claiming;
        float _t;
        string _playRoom;
        FirebaseManager.EvolutionRecord _rec;
        EvoBarView _hookedBar;
        PageManager _pages;
        CameraUIManager _cameraUI;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("EvolutionFlow (runtime)", typeof(RectTransform));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<EvolutionFlow>();
        }

        /// <summary>點寵物／進化條：可以進化就開始（已經進化、還不夠 100 張、正在處理中都不做）。</summary>
        public static void Request() => _instance?.DoRequest();

        void Awake()
        {
            _font = BlockPet.UI.Decorate.DecorateSkin.Load()?.font;
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 13; // 首頁旅行彈出（12）之上，相簿／拍立得等全頁畫面之下
            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();
            _root = (RectTransform)transform;
            EvoBarView.IsEvolved = () => PetCollectionManager.IsEvolved;
            FirebaseManager.OnEvolutionChanged += OnEvolutionChanged;
            BuildSilhouette();
            BuildAnim();
        }

        void OnDestroy()
        {
            FirebaseManager.OnEvolutionChanged -= OnEvolutionChanged;
            if (_hookedBar != null) _hookedBar.OnEvolveRequested -= Request;
            if (_instance == this) _instance = null;
        }

        void OnEvolutionChanged()
        {
            if (_hookedBar != null) _hookedBar.Refresh();
        }

        bool IsHome()
        {
            if (_pages == null) _pages = FindObjectOfType<PageManager>(true);
            if (_cameraUI == null) _cameraUI = FindObjectOfType<CameraUIManager>(true);
            return (_cameraUI == null || _cameraUI.CurrentMode == UIMode.Home) && _pages != null && _pages.IsHomePageActive;
        }

        // ─── 每一格 ─────────────────────────────────────────────────────

        void Update()
        {
            var fb = FirebaseManager.Instance;
            HookEvoBar();
            bool home = IsHome();
            _group.alpha = home || _playing ? 1f : 0f;
            _group.blocksRaycasts = _playing;

            var rec = fb != null ? fb.CurrentEvolution : null;
            ApplyLook(rec);

            if (_playing)
            {
                if (fb == null || fb.RoomId != _playRoom) { Finish(false); return; } // 換空間：不播了
                Animate();
                return;
            }

            // 別人進化的：第一次看到時補播（每人一次）
            if (rec != null && home && fb != null && !string.IsNullOrEmpty(fb.RoomId)
                && PlayerPrefs.GetString(PrefsSeen + fb.RoomId, "") != rec.at.ToString())
                Play(rec);

            UpdateSilhouette(home && rec == null);
        }

        void HookEvoBar()
        {
            if (_hookedBar != null) return;
            var top = FindObjectOfType<TopBarView>(true);
            if (top == null || top.EvoBar == null) return;
            _hookedBar = top.EvoBar;
            _hookedBar.OnEvolveRequested += Request;
            _hookedBar.Refresh();
        }

        void ApplyLook(FirebaseManager.EvolutionRecord rec)
        {
            var pet = AIController.Instance;
            if (pet == null) return;
            if (pet.GetComponent<BlockPet.Decoration.PetFurnitureVisits>() == null) pet.gameObject.AddComponent<BlockPet.Decoration.PetFurnitureVisits>();
            var look = pet.GetComponent<EvolvedLook>();
            if (look == null) look = pet.gameObject.AddComponent<EvolvedLook>();
            // 動畫播完才換樣子（播的時候寵物還是原本的樣子）
            look.Record = _playing ? null : rec;
        }

        // ─── 進化 ──────────────────────────────────────────────────────

        void DoRequest()
        {
            var fb = FirebaseManager.Instance;
            var pcm = PetCollectionManager.Instance;
            if (fb == null || pcm == null || _claiming || _playing || PetCollectionManager.IsEvolved) return;
            if (!EvolutionRules.CanEvolve(pcm.FedCount)) return;
            _claiming = true;
            string room = fb.RoomId;
            var result = EvolutionRules.Decide(pcm.Palette(), pcm.LastFed());
            int days = DaysRaised(pcm);
            fb.ClaimEvolution(result, (rec, mine) =>
            {
                _claiming = false;
                if (rec == null || fb.RoomId != room) { Debug.LogWarning("[Evolution] 進化沒有寫進營地（網路或 Firebase 規則）"); return; }
                Play(rec);
                if (!mine) return;
                var entry = new DexEntry { evolvedAt = rec.at, days = days, palette = (int[])rec.palette.Clone() };
                fb.RecordDex(DexRules.DefaultSeries, rec.type, entry, r =>
                {
                    if (r.isNewCell) BlockPet.UI.Dex.DexScreen.ShowNew(DexRules.DefaultSeries, rec.type, r);
                });
            });
        }

        /// <summary>養了幾天：這隻寵物的第一張照片到現在。</summary>
        static int DaysRaised(PetCollectionManager pcm)
        {
            var photos = SaveManager.Instance != null ? SaveManager.Instance.data?.photos : null;
            int i = Mathf.Max(0, pcm.StartingPhotoCount);
            if (photos == null || i >= photos.Count || !DateTime.TryParse(photos[i]?.timestamp, out var t)) return 0;
            return Mathf.Max(0, (int)(DateTime.Now - t).TotalDays);
        }

        // ─── 動畫（約 2.5 秒，【暫定，待美術】）────────────────────────

        void Play(FirebaseManager.EvolutionRecord rec)
        {
            var fb = FirebaseManager.Instance;
            _rec = rec;
            _playRoom = fb != null ? fb.RoomId : null;
            _playing = true;
            _t = 0f;
            var sr = AIController.Instance != null ? AIController.Instance.GetComponentInChildren<SpriteRenderer>() : null;
            _pet.sprite = sr != null ? sr.sprite : null;
            _glow.sprite = _pet.sprite;
            _pet.enabled = _glow.enabled = _pet.sprite != null;
            _pet.color = Color.white;
            _title.text = EvolutionRules.ResultTitle(rec.type);
            _sub.text = rec.type == DexRules.Rainbow ? "五種顏色都有" : $"偏{PetPreferences.Categories[Mathf.Clamp(rec.main, 0, 4)]}";
            _silhouette.enabled = false;
            _anim.gameObject.SetActive(true);
            Animate();
        }

        void Animate()
        {
            _t += Time.unscaledDeltaTime;
            float k = _root.rect.width / DesignW;
            float top = (Screen.height - Screen.safeArea.yMax) * (_canvas.scaleFactor > 0f ? 1f / _canvas.scaleFactor : 1f);
            _design.localScale = Vector3.one * k;
            _design.anchoredPosition = new Vector2(0f, -top);

            _dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.6f * Mathf.Clamp01(_t / 0.3f));
            // 0.3～1.3 秒：發光、彈跳兩下
            float bounce = _t > 0.3f && _t < 1.3f ? Mathf.Abs(Mathf.Sin((_t - 0.3f) * Mathf.PI * 2f)) * 18f : 0f;
            _petRt.anchoredPosition = new Vector2(0f, -280f + bounce);
            float glow = _t < 1.3f ? 0.35f + 0.35f * Mathf.Sin(_t * 9f) : 0f;
            _glow.color = new Color(1f, 1f, 0.85f, Mathf.Clamp01(glow) * Mathf.Clamp01(_t / 0.3f));
            // 1.3～1.5 白光一閃；1.4 換成新樣子
            float flash = _t < 1.3f ? 0f : _t < 1.4f ? (_t - 1.3f) / 0.1f : Mathf.Clamp01(1f - (_t - 1.4f) / 0.3f);
            _flash.color = new Color(1f, 1f, 1f, flash);
            _pet.color = _t >= 1.4f ? EvolvedLook.TintFor(_rec, Time.unscaledTime) : Color.white;
            // 1.5～2.0 字淡入；2.0 後出現「好」
            float textA = Mathf.Clamp01((_t - 1.5f) / 0.5f);
            _title.alpha = _sub.alpha = textA;
            _okRt.gameObject.SetActive(_t >= AnimSeconds - 0.5f);
        }

        void Finish(bool seen)
        {
            var fb = FirebaseManager.Instance;
            if (seen && _rec != null && !string.IsNullOrEmpty(_playRoom))
            {
                PlayerPrefs.SetString(PrefsSeen + _playRoom, _rec.at.ToString());
                PlayerPrefs.Save();
            }
            _playing = false;
            _anim.gameObject.SetActive(false);
            if (seen && AIController.Instance != null) PetHop.Play(AIController.Instance.gameObject, 2);
            if (fb != null && fb.RoomId != _playRoom) _rec = null;
        }

        // ─── 80% 預告：發光的剪影 ──────────────────────────────────────

        void UpdateSilhouette(bool show)
        {
            var pcm = PetCollectionManager.Instance;
            var pet = AIController.Instance;
            var cam = Camera.main;
            var sr = pet != null ? pet.GetComponentInChildren<SpriteRenderer>() : null;
            bool on = show && pcm != null && sr != null && sr.enabled && sr.sprite != null && cam != null
                      && EvolutionRules.ShowPreview(pcm.FedCount) && !BlockPet.UI.TopBar.DecorModeSettings.IsDecorating;
            _silhouette.enabled = on;
            if (!on) return;
            var r = EvolutionRules.Decide(pcm.Palette(), pcm.LastFed());
            var color = r.type == DexRules.Rainbow
                ? EvolvedLook.TintFor(new FirebaseManager.EvolutionRecord { type = DexRules.Rainbow }, Time.unscaledTime)
                : Color.Lerp(Color.white, EvolvedLook.CategoryColors[r.main], 0.6f);
            float pulse = 0.4f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime / 1.6f * Mathf.PI * 2f));
            _silhouette.sprite = sr.sprite;
            _silhouette.color = new Color(color.r, color.g, color.b, pulse);
            // 放在寵物左邊，和寵物一樣大
            var b = sr.bounds;
            Vector2 a = cam.WorldToScreenPoint(b.min), z = cam.WorldToScreenPoint(b.max);
            float px = _canvas.scaleFactor > 0f ? 1f / _canvas.scaleFactor : 1f;
            var size = new Vector2(Mathf.Abs(z.x - a.x), Mathf.Abs(z.y - a.y)) * px;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, cam.WorldToScreenPoint(b.center), null, out var local);
            var rt = _silhouette.rectTransform;
            rt.sizeDelta = size;
            rt.anchoredPosition = local - _root.rect.min - new Vector2(size.x * 0.95f, 0f);
            rt.localScale = new Vector3(sr.flipX ? -1f : 1f, 1f, 1f);
        }

        // ─── 建立 ──────────────────────────────────────────────────────

        void BuildSilhouette()
        {
            var rt = NewRect(_root, "Silhouette");
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            _silhouette = rt.gameObject.AddComponent<Image>();
            _silhouette.preserveAspect = true;
            _silhouette.raycastTarget = false;
            _silhouette.material = WhiteMaterial();
            _silhouette.enabled = false;
        }

        void BuildAnim()
        {
            _anim = NewRect(_root, "Evolve");
            Stretch(_anim);
            _dim = _anim.gameObject.AddComponent<Image>();
            _dim.raycastTarget = true; // 播的時候擋住後面
            _design = NewRect(_anim, "Design");
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 1f);
            _design.pivot = new Vector2(0.5f, 1f);
            _design.sizeDelta = new Vector2(DesignW, DesignH);

            _glowRt = Place(_design, "Glow", 0, 0, 240, 210);
            _glowRt.anchorMin = _glowRt.anchorMax = new Vector2(0.5f, 1f);
            _glowRt.pivot = new Vector2(0.5f, 0.5f);
            _glowRt.anchoredPosition = new Vector2(0f, -280f);
            _glow = _glowRt.gameObject.AddComponent<Image>();
            _glow.preserveAspect = true;
            _glow.raycastTarget = false;
            _glow.material = WhiteMaterial();

            _petRt = Place(_design, "Pet", 0, 0, 200, 175);
            _petRt.anchorMin = _petRt.anchorMax = new Vector2(0.5f, 1f);
            _petRt.pivot = new Vector2(0.5f, 0.5f);
            _pet = _petRt.gameObject.AddComponent<Image>();
            _pet.preserveAspect = true;
            _pet.raycastTarget = false;

            _textRt = Place(_design, "Text", 0, 400, DesignW, 70);
            _title = Label(_textRt, 0, 0, DesignW, 34, "", 26, Color.white);
            _sub = Label(_textRt, 0, 38, DesignW, 22, "", 15, new Color(1f, 1f, 1f, 0.85f));

            _okRt = Place(_design, "OK", (DesignW - 200f) / 2f, 490, 200, 53);
            UIShapes.AddRounded(Place(_okRt, "Edge", 0, 3, 200, 50).gameObject, YellowEdge, 15).raycastTarget = false;
            var fill = UIShapes.AddRounded(Place(_okRt, "Fill", 0, 0, 200, 50).gameObject, Yellow, 15);
            Label(_okRt, 0, 0, 200, 50, "好", 16, Ink);
            var b = _okRt.gameObject.AddComponent<Button>();
            b.targetGraphic = fill;
            b.onClick.AddListener(() => Finish(true));

            var flashRt = NewRect(_anim, "Flash");
            Stretch(flashRt);
            _flash = flashRt.gameObject.AddComponent<Image>();
            _flash.raycastTarget = false;
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _anim.gameObject.SetActive(false);
        }

        static Material _white;

        /// <summary>把圖畫成純白（剪影、光暈）：UI/Default 改不了，用頂點色乘在白色上——這裡用 Sprites 的 "GUI/Text Shader" 只取 alpha。</summary>
        static Material WhiteMaterial()
        {
            if (_white != null) return _white;
            var sh = Shader.Find("GUI/Text Shader");
            if (sh == null) return null;
            _white = new Material(sh) { hideFlags = HideFlags.DontSave };
            return _white;
        }

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size, Color color)
        {
            var t = Place(parent, "Text", left, top, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            return t;
        }

        static RectTransform NewRect(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
        {
            var rt = NewRect(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }
    }
}
