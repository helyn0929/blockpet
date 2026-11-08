using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Polaroid;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlockPet.UI.Polaroid
{
    /// <summary>
    /// 拍立得上的貼紙（5.26 ④）：大家都能貼，每人每張最多 3 個（暫定）；自己的可以點選、拖動、按右上 × 撕掉；
    /// 別人的點了顯示「小美貼的」1.5 秒、不能動。寵物貼紙在 App 裡輕輕左右晃（±3°、2.2 秒）。
    /// 拍照的人長按撕掉任何人的貼紙之後再畫（待決 35），現在還沒有。
    /// </summary>
    public partial class PolaroidScreen
    {
        class StickerView
        {
            public PolaroidSticker data;
            public RectTransform rt, body;
            public float size, phase;
        }

        readonly Dictionary<string, StickerView> _views = new Dictionary<string, StickerView>();
        string _selectedId, _popId;
        float _popT = 1f;
        bool _draggingSticker;
        RectTransform _selection, _ownerTag;
        TMP_Text _ownerTagText;
        float _ownerTagUntil;

        void RebuildStickers()
        {
            _card.ClearStickers();
            _views.Clear();
            _selection = null;
            foreach (var s in _stickers)
            {
                var rt = _card.AddSticker(s, out var body);
                var v = new StickerView { data = s, rt = rt, body = body, size = PolaroidRules.StickerSize(s.kind), phase = (Mathf.Abs((s.id ?? "").GetHashCode()) % 100) / 100f };
                _views[s.id ?? ""] = v;
                var hit = rt.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                var handle = rt.gameObject.AddComponent<StickerHandle>();
                handle.screen = this;
                handle.id = s.id;
            }
            if (_selectedId != null && _views.ContainsKey(_selectedId)) ShowSelection(_selectedId);
            else _selectedId = null;
            if (_ownerTag == null) BuildOwnerTag();
            _ownerTag.SetAsLastSibling();
        }

        /// <summary>點貼紙列的一張：貼到拍立得上（依序放在三個位置、左右各轉 8°），0.35 秒彈出。</summary>
        void AddSticker(string kind)
        {
            var m = Current;
            string me = Me;
            if (m == null) return;
            if (string.IsNullOrEmpty(me)) { ShowHint("登入之後才能貼貼紙", 1.6f); return; }
            if (!PolaroidRules.CanAddSticker(_stickers, me)) { ShowHint($"每人最多貼 {PolaroidRules.StickerLimit} 個", 1.6f); return; }
            var spot = PolaroidRules.SpawnSpot(PolaroidRules.MyCount(_stickers, me));
            var s = new PolaroidSticker { kind = kind, x = spot.pos.x, y = spot.pos.y, rot = spot.rot, by = me };
            var fb = FirebaseManager.Instance;
            s.id = fb != null ? fb.AddPolaroidSticker(m.fileName, s) : null;
            if (string.IsNullOrEmpty(s.id)) return;
            if (!_stickers.Exists(x => x.id == s.id)) _stickers.Add(s); // Firebase 的事件通常會先到
            _popId = s.id;
            _popT = 0f;
            RebuildStickers();
            BuildTray();
        }

        // ─── 選取、撕掉 ────────────────────────────────────────────────

        internal void OnStickerClicked(string id)
        {
            if (!_views.TryGetValue(id ?? "", out var v)) return;
            if (_input.isFocused) { EndTyping(); return; }
            if (PolaroidRules.CanEditSticker(v.data, Me))
            {
                if (_selectedId == id) Deselect();
                else ShowSelection(id);
                return;
            }
            // 別人的：「小美貼的」1.5 秒
            Deselect();
            string name = string.IsNullOrEmpty(v.data.byName) ? "營地" : v.data.byName;
            ShowOwnerTag(v, name + "貼的", 1.5f);
        }

        void ShowSelection(string id)
        {
            Deselect();
            if (!_views.TryGetValue(id, out var v)) return;
            _selectedId = id;
            float s = v.size;
            // 黃色虛線框 2 #F6C945，外擴 6；右上紅色 ×（26 圓、白邊 2）；下方「我貼的・可以拖、可以撕」
            _selection = PUI.Place(v.rt, "Selection", -6, -6, s + 12f, s + 12f);
            if (_skin != null && _skin.selDash != null)
            {
                var d = _selection.gameObject.AddComponent<Image>();
                d.sprite = _skin.selDash; d.type = Image.Type.Sliced; d.raycastTarget = false;
            }
            else UIShapes.AddThinRing(_selection.gameObject, Yellow, 8).raycastTarget = false;
            var peel = PUI.Place(_selection, "Peel", s + 12f - 26f + 10f, -10f, 26, 26);
            var pb = PUI.Fill(peel, _skin?.btnPeel, false, Coral, 13);
            PUI.Icon(peel, _skin?.iconX, 8, 8, 10, Color.white);
            PUI.MakeButton(peel.gameObject, pb, () => Peel(id));
            ShowOwnerTag(v, "我貼的・可以拖、可以撕", 0f);
        }

        void Deselect()
        {
            if (_selection != null) Kill(_selection.gameObject);
            _selection = null;
            _selectedId = null;
            if (_ownerTag != null && _ownerTagUntil <= 0f) _ownerTag.gameObject.SetActive(false);
        }

        void Peel(string id)
        {
            var m = Current;
            if (m == null || !_views.TryGetValue(id, out var v) || !PolaroidRules.CanEditSticker(v.data, Me)) return;
            FirebaseManager.Instance?.RemovePolaroidSticker(m.fileName, id);
            _stickers.RemoveAll(x => x.id == id);
            _selectedId = null;
            if (_ownerTag != null) _ownerTag.gameObject.SetActive(false);
            RebuildStickers();
            BuildTray();
        }

        // ─── 「小美貼的」小標 ──────────────────────────────────────────

        void BuildOwnerTag()
        {
            _ownerTag = PUI.Place(_card.root, "OwnerTag", 0, 0, 100, 24);
            PUI.Fill(PUI.Stretch(_ownerTag, "Bg"), _skin?.tagDark, true, new Color(Ink.r, Ink.g, Ink.b, 0.9f), 10).raycastTarget = false;
            _ownerTagText = PUI.Label(_font, _ownerTag, 9, 0, 80, 24, "", 11, PUI.Hex(0xFFF8EC), TextAlignmentOptions.Center);
            _ownerTag.gameObject.SetActive(false);
        }

        /// <summary>貼紙下方的深色小標；seconds＝0 表示一直顯示到取消選取。</summary>
        void ShowOwnerTag(StickerView v, string text, float seconds)
        {
            if (_ownerTag == null) BuildOwnerTag();
            _ownerTagText.text = text;
            float tw = Mathf.Ceil(_ownerTagText.GetPreferredValues(text).x);
            _ownerTagText.rectTransform.sizeDelta = new Vector2(tw, 24f);
            _ownerTag.sizeDelta = new Vector2(tw + 18f, 24f);
            _ownerTag.anchoredPosition = new Vector2(v.data.x + v.size / 2f - (tw + 18f) / 2f, -(v.data.y + v.size + 10f));
            _ownerTag.gameObject.SetActive(true);
            _ownerTag.SetAsLastSibling();
            _ownerTagUntil = seconds > 0f ? Time.unscaledTime + seconds : 0f;
        }

        // ─── 拖動 ──────────────────────────────────────────────────────

        Vector2 _dragStartLocal, _dragStartPos;
        string _dragId;

        Vector2 CardLocal(PointerEventData e, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_card.root, screen, e.pressEventCamera, out var p);
            return new Vector2(p.x, -p.y); // 卡片座標：左上原點、y 向下
        }

        internal bool BeginStickerDrag(string id, PointerEventData e)
        {
            if (!_views.TryGetValue(id ?? "", out var v) || !PolaroidRules.CanEditSticker(v.data, Me)) return false;
            if (_selectedId != id) ShowSelection(id);
            _draggingSticker = true;
            _dragId = id;
            _dragStartLocal = CardLocal(e, e.pressPosition);
            _dragStartPos = new Vector2(v.data.x, v.data.y);
            return true;
        }

        internal void DragSticker(PointerEventData e)
        {
            if (!_draggingSticker || !_views.TryGetValue(_dragId ?? "", out var v)) return;
            var d = CardLocal(e, e.position) - _dragStartLocal;
            // 可以超出卡片一點（下載時切掉），但中心要留在卡片上
            float x = Mathf.Clamp(_dragStartPos.x + d.x, -v.size / 2f, PolaroidRules.CardW - v.size / 2f);
            float y = Mathf.Clamp(_dragStartPos.y + d.y, -v.size / 2f, PolaroidRules.CardH - v.size / 2f);
            v.data.x = x;
            v.data.y = y;
            PolaroidCard.PlaceSticker(v.rt, x, y, v.data.rot, v.size);
            if (_ownerTag != null && _ownerTag.gameObject.activeSelf)
                _ownerTag.anchoredPosition = new Vector2(x + v.size / 2f - _ownerTag.sizeDelta.x / 2f, -(y + v.size + 10f));
        }

        internal void EndStickerDrag()
        {
            if (!_draggingSticker) return;
            _draggingSticker = false;
            var m = Current;
            if (m != null && _views.TryGetValue(_dragId ?? "", out var v))
                FirebaseManager.Instance?.MovePolaroidSticker(m.fileName, _dragId, v.data.x, v.data.y);
            _dragId = null;
        }

        /// <summary>別人的貼紙拖了就當成在拍立得上左右滑（換張）。</summary>
        internal HandSwipe CardSwipe => _card.root.GetComponent<HandSwipe>();

        // ─── 每幀 ──────────────────────────────────────────────────────

        void UpdateStickers(float dt)
        {
            float now = Time.unscaledTime;
            foreach (var v in _views.Values)
            {
                // 寵物貼紙輕輕左右晃：±3°、2.2 秒
                if (v.body != null) v.body.localRotation = Quaternion.Euler(0f, 0f, 3f * Mathf.Sin((now / 2.2f + v.phase) * Mathf.PI * 2f));
            }
            // 剛貼上：0.35 秒彈出（0.6 → 1.08 → 1）
            if (_popT < 1f && _popId != null && _views.TryGetValue(_popId, out var p))
            {
                _popT = Mathf.MoveTowards(_popT, 1f, dt / 0.35f);
                float s = _popT < 0.7f ? Mathf.Lerp(0.6f, 1.08f, _popT / 0.7f) : Mathf.Lerp(1.08f, 1f, (_popT - 0.7f) / 0.3f);
                p.rt.localScale = Vector3.one * s;
            }
            if (_ownerTag != null && _ownerTagUntil > 0f && now >= _ownerTagUntil)
            {
                _ownerTagUntil = 0f;
                if (_selectedId == null) _ownerTag.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>掛在拍立得上每一張貼紙：點選、拖動自己的；別人的拖曳交給卡片的左右滑。</summary>
    public class StickerHandle : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal PolaroidScreen screen;
        internal string id;
        bool _mine;
        HandSwipe _forward;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return;
            screen?.OnStickerClicked(id);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _mine = screen != null && screen.BeginStickerDrag(id, e);
            if (_mine) return;
            _forward = screen?.CardSwipe;
            if (_forward == null) return;
            e.pointerDrag = _forward.gameObject;
            _forward.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_mine) screen.DragSticker(e);
            else _forward?.OnDrag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_mine) screen.EndStickerDrag();
            else _forward?.OnEndDrag(e);
            _mine = false;
            _forward = null;
        }
    }
}
