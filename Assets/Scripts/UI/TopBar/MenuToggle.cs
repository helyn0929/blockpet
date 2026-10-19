using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BlockPet.UI.TopBar
{
    /// <summary>
    /// 右上收合選單鈕（預設收起）：點了換底圖（收合 / 展開）、箭頭 0.25 秒轉 180°，
    /// 下方彈出「佈置／旅行／圖鑑／設定」（0.22 秒 0.6→1，設計稿 c5Pop；佈置見設計文件 5.7）。
    /// </summary>
    public class MenuToggle : MonoBehaviour
    {
        [SerializeField] TopBarConfig config;
        [SerializeField] Button toggleButton;
        [SerializeField] Image background;
        [SerializeField] RectTransform chevron;
        [SerializeField] RectTransform items;
        [SerializeField] CanvasGroup itemsGroup;
        [SerializeField] Button decorButton;
        [SerializeField] Button travelButton;
        [SerializeField] Button dexButton;
        [SerializeField] Button settingsButton;

        [Header("Events")]
        public UnityEvent onDecor = new UnityEvent();
        public UnityEvent onTravel = new UnityEvent();
        public UnityEvent onDex = new UnityEvent();
        public UnityEvent onSettings = new UnityEvent();

        PageManager _pageManager;
        bool _open;
        float _chevronAngle;
        float _popTime = -1f;

        public bool IsOpen => _open;

        void Awake()
        {
            _pageManager = FindObjectOfType<PageManager>(true);
            if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
            if (decorButton != null) decorButton.onClick.AddListener(() => Fire(onDecor, "佈置", EnterDecorMode));
            if (travelButton != null) travelButton.onClick.AddListener(() => Fire(onTravel, "旅行"));
            if (dexButton != null) dexButton.onClick.AddListener(() => Fire(onDex, "圖鑑"));
            if (settingsButton != null) settingsButton.onClick.AddListener(() => Fire(onSettings, "設定", () => _pageManager?.ShowSettingsPage()));
            SetOpen(false, instant: true);
        }

        public void Toggle() => SetOpen(!_open);

        public void SetOpen(bool open, bool instant = false)
        {
            _open = open;
            if (config != null && background != null)
                background.sprite = open ? config.menuOpen : config.menuClosed;
            if (items != null) items.gameObject.SetActive(open);
            _popTime = open && !instant ? 0f : -1f;
            if (open && instant) ApplyPop(1f);
            if (instant)
            {
                _chevronAngle = open ? 180f : 0f;
                if (chevron != null) chevron.localRotation = Quaternion.Euler(0f, 0f, _chevronAngle);
            }
        }

        void Update()
        {
            if (config == null) return;

            float target = _open ? 180f : 0f;
            if (!Mathf.Approximately(_chevronAngle, target))
            {
                float speed = 180f / Mathf.Max(0.01f, config.chevronRotateSec);
                _chevronAngle = Mathf.MoveTowards(_chevronAngle, target, speed * Time.unscaledDeltaTime);
                if (chevron != null) chevron.localRotation = Quaternion.Euler(0f, 0f, _chevronAngle);
            }

            if (_popTime >= 0f)
            {
                _popTime += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_popTime / Mathf.Max(0.01f, config.menuPopSec));
                ApplyPop(1f - (1f - t) * (1f - t));
                if (t >= 1f) _popTime = -1f;
            }
        }

        void ApplyPop(float k)
        {
            if (items != null) items.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, k);
            if (itemsGroup != null) itemsGroup.alpha = k;
        }

        /// <summary>佈置：進入佈置模式（取代舊版左上的床圖示鈕）。</summary>
        static void EnterDecorMode()
        {
            var editor = FindObjectOfType<BlockPet.UI.Home.RoomEditorController>(true);
            if (editor != null) editor.EnterEditMode();
            else Debug.LogWarning("[MenuToggle] 場景裡找不到 RoomEditorController，無法進入佈置模式");
        }

        /// <summary>點選單項目：先收起選單；沒有另外接事件時用預設動作（佈置 → 佈置模式、設定 → 設定頁），沒有預設動作就記 log。</summary>
        void Fire(UnityEvent evt, string label, System.Action fallback = null)
        {
            SetOpen(false);
            if (evt.GetPersistentEventCount() == 0)
            {
                if (fallback != null) fallback();
                else Debug.Log($"[MenuToggle] {label}（尚未接上功能）");
            }
            evt.Invoke();
        }
    }
}
