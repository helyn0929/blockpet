using UnityEngine;
using BlockPet.UI.Home;

namespace BlockPet.Decoration
{
    /// <summary>
    /// Attached at runtime (via FermentationJarManager, in response to
    /// DecorationManager.OnDecorationSpawned) to any placed decoration whose itemId matches
    /// the fermentation jar item. Purely visual + tap-to-upload; all state and timing logic
    /// lives in FermentationJarManager, which calls into this component's Set* methods.
    /// </summary>
    [RequireComponent(typeof(DecorationObject))]
    public class FermentationJarObject : MonoBehaviour
    {
        public enum JarVisualState { Empty, Digesting, Ready }

        DecorationObject _decor;
        SpriteRenderer _sr;
        DecorationItem _item;
        FermentationJarManager _manager;
        TextMesh _countdownText;

        public string DecorId => _decor != null ? _decor.DecorId : null;
        public JarVisualState CurrentState { get; private set; } = JarVisualState.Empty;

        public void Configure(DecorationItem item, FermentationJarManager manager)
        {
            _decor   = GetComponent<DecorationObject>();
            _sr      = GetComponent<SpriteRenderer>();
            _item    = item;
            _manager = manager;

            if (_countdownText == null)
            {
                var go = new GameObject("CountdownText");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                _countdownText = go.AddComponent<TextMesh>();
                _countdownText.characterSize = 0.12f;
                _countdownText.fontSize = 48;
                _countdownText.anchor = TextAnchor.MiddleCenter;
                _countdownText.alignment = TextAlignment.Center;
                _countdownText.color = Color.white;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) { mr.sortingLayerName = "Pet"; mr.sortingOrder = 999; }
            }

            SetEmpty();
        }

        void OnMouseDown()
        {
            if (RoomEditorController.AnyEditModeActive) return;
            if (_manager == null || string.IsNullOrEmpty(DecorId)) return;

            if (CurrentState == JarVisualState.Empty)
                _manager.RequestUploadPhoto(DecorId, transform.position);
        }

        // ─── Visual states (driven by FermentationJarManager) ────────────

        public void SetEmpty()
        {
            CurrentState = JarVisualState.Empty;
            if (_sr != null && _item != null) { _sr.sprite = _item.worldSprite; _sr.color = Color.white; }
            SetCountdownText(null);
        }

        public void SetDigesting(float remainingSeconds)
        {
            CurrentState = JarVisualState.Digesting;
            if (_sr != null && _item != null)
            {
                _sr.sprite = _item.digestingSprite != null ? _item.digestingSprite : _item.worldSprite;
                _sr.color  = _item.digestingSprite != null ? Color.white : new Color(0.75f, 0.85f, 1f, 1f);
            }
            int s = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            SetCountdownText(string.Format("{0:D2}:{1:D2}", s / 60, s % 60));
        }

        public void SetReady()
        {
            CurrentState = JarVisualState.Ready;
            if (_sr != null && _item != null)
            {
                _sr.sprite = _item.readySprite != null ? _item.readySprite
                           : _item.digestingSprite != null ? _item.digestingSprite : _item.worldSprite;
                _sr.color  = Color.white;
            }
            SetCountdownText(null);
        }

        void SetCountdownText(string text)
        {
            if (_countdownText == null) return;
            _countdownText.text = text ?? "";
            _countdownText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}
