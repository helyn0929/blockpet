using System.Collections.Generic;
using BlockPet.UI.Market;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Home
{
    /// <summary>
    /// 營地首頁的寵物穿著穿搭（設計文件 5.11 ⑧）：營地共用（待決 36 暫定），所以每個成員看到的寵物都穿著同一套
    /// （FirebaseManager.WornOutfit，Rooms/{room}/outfit）。
    /// 寵物是場景裡的 2D 圖，這裡在畫面上跟著寵物的位置畫穿搭；寵物走路左右翻面時穿搭也翻。
    /// Canvas 排在首頁 UI（order 0）下面、場景上面，所以貼紙、手牌、狀態欄會蓋在穿搭上。
    /// 寵物出去旅行（圖被藏起來）、不在首頁時不畫。執行時自動建立。
    /// </summary>
    public class OutfitHomeOverlay : MonoBehaviour
    {
        static OutfitHomeOverlay _instance;

        Canvas _canvas;
        RectTransform _root, _layer;
        PageManager _pages;
        CameraUIManager _cameraUI;
        readonly Dictionary<string, string> _shown = new Dictionary<string, string>();
        bool _shownFlip;
        Vector2 _shownSize;
        bool _dirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("OutfitHomeOverlay (runtime)", typeof(RectTransform));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<OutfitHomeOverlay>();
        }

        void Awake()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = -1; // 首頁 UI（0）下面
            _root = (RectTransform)transform;
            _layer = new GameObject("Wear", typeof(RectTransform)).GetComponent<RectTransform>();
            _layer.SetParent(_root, false);
            _layer.anchorMin = _layer.anchorMax = Vector2.zero;
            _layer.pivot = new Vector2(0f, 1f);
            FirebaseManager.OnOutfitChanged += MarkDirty;
        }

        void OnDestroy()
        {
            FirebaseManager.OnOutfitChanged -= MarkDirty;
            if (_instance == this) _instance = null;
        }

        void MarkDirty() => _dirty = true;

        void LateUpdate()
        {
            var fb = FirebaseManager.Instance;
            if (fb != null && !string.IsNullOrEmpty(fb.RoomId)) fb.StartOutfit();
            var worn = fb != null ? fb.WornOutfit : null;

            var pet = AIController.Instance;
            var cam = Camera.main;
            var sr = pet != null ? pet.GetComponentInChildren<SpriteRenderer>() : null;
            bool show = worn != null && worn.Count > 0 && pet != null && cam != null && sr != null
                        && pet.gameObject.activeInHierarchy && sr.enabled && IsHome();
            _layer.gameObject.SetActive(show);
            if (!show) return;

            // 寵物圖在畫面上的範圍（畫布單位）
            var b = sr.bounds;
            Vector3 s0 = cam.WorldToScreenPoint(b.min), s1 = cam.WorldToScreenPoint(b.max);
            float k = _canvas.scaleFactor > 0f ? 1f / _canvas.scaleFactor : 1f;
            float x0 = Mathf.Min(s0.x, s1.x) * k, x1 = Mathf.Max(s0.x, s1.x) * k;
            float y0 = Mathf.Min(s0.y, s1.y) * k, y1 = Mathf.Max(s0.y, s1.y) * k;
            var size = new Vector2(x1 - x0, y1 - y0);
            _layer.anchoredPosition = new Vector2(x0, y1);
            _layer.sizeDelta = size;

            bool flip = sr.flipX;
            if (_dirty || flip != _shownFlip || (size - _shownSize).sqrMagnitude > 1f || !Same(worn))
            {
                _dirty = false;
                _shownFlip = flip;
                _shownSize = size;
                _shown.Clear();
                foreach (var kv in worn) _shown[kv.Key] = kv.Value;
                OutfitArt.Dress(_layer, worn, size.x, size.y, flip);
            }
        }

        bool Same(IReadOnlyDictionary<string, string> worn)
        {
            if (worn.Count != _shown.Count) return false;
            foreach (var kv in worn) if (!_shown.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }

        bool IsHome()
        {
            if (_pages == null) _pages = FindObjectOfType<PageManager>(true);
            if (_cameraUI == null) _cameraUI = FindObjectOfType<CameraUIManager>(true);
            return (_cameraUI == null || _cameraUI.CurrentMode == UIMode.Home) && _pages != null && _pages.IsHomePageActive
                   && !BlockPet.UI.TopBar.DecorModeSettings.IsDecorating;
        }
    }
}
