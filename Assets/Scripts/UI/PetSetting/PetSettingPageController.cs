using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PetSettingPageController : MonoBehaviour
{
    [SerializeField] PageManager pageManager;
    [SerializeField] Sprite[] petSprites;

    UIDocument _doc;
    VisualElement _root;

    VisualElement _petAvatar;
    TextField _nameInput;
    Label _nameHint;
    Label _roomIdLabel;
    Label _birthLabel;
    VisualElement _parentsList;

    void Awake()
    {
        _doc = GetComponent<UIDocument>();
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
    }

    void OnEnable()
    {
        _root = _doc.rootVisualElement;

        _petAvatar   = _root.Q<VisualElement>("pet-avatar");
        _nameInput   = _root.Q<TextField>("name-input");
        _nameHint    = _root.Q<Label>("name-hint");
        _roomIdLabel = _root.Q<Label>("room-id-label");
        _birthLabel  = _root.Q<Label>("birth-label");
        _parentsList = _root.Q<VisualElement>("parents-list");

        WireButton("btn-back",      OnClickBack);
        WireButton("btn-save-name", OnClickSaveName);
        WireButton("btn-copy-id",   OnClickCopyId);

        if (_nameInput != null)
            _nameInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    OnClickSaveName();
            });

        LoadData();
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    void LoadData()
    {
        var fb = FirebaseManager.Instance;
        string roomId = fb?.RoomId ?? string.Empty;

        // Pet avatar sprite
        if (_petAvatar != null && petSprites != null && petSprites.Length > 0)
        {
            int idx = PetCollectionManager.Instance != null
                ? PetCollectionManager.Instance.CurrentPetIndex % petSprites.Length
                : 0;
            if (petSprites[idx] != null)
                _petAvatar.style.backgroundImage = new StyleBackground(petSprites[idx]);
        }

        // Room ID display
        if (_roomIdLabel != null)
            _roomIdLabel.text = string.IsNullOrEmpty(roomId) ? "—" : roomId;

        if (string.IsNullOrEmpty(roomId)) return;

        // Fetch room meta for name + birth date
        fb.GetRoomMeta(roomId, meta =>
        {
            if (_nameInput != null)
                _nameInput.value = meta != null ? meta.name : roomId;

            if (_birthLabel != null)
                _birthLabel.text = FormatBirthDate(meta?.createdAt);
        });

        // Fetch parents (members)
        fb.GetRoomMembers(roomId, members => BuildParentsList(members, fb.GetUserId()));
    }

    // ── Parents list ──────────────────────────────────────────────────────────

    void BuildParentsList(List<FirebaseManager.RoomMemberInfo> members, string myUid)
    {
        _parentsList?.Clear();
        if (members == null || members.Count == 0) return;

        foreach (var m in members)
        {
            bool isMe = m.uid == myUid;
            string name = string.IsNullOrEmpty(m.nickname) ? m.uid : m.nickname;

            var row = new VisualElement();
            row.AddToClassList("ps-parent-row");

            var avatar = new VisualElement();
            avatar.AddToClassList("ps-parent-avatar");
            var initial = new Label(GetInitial(name));
            initial.AddToClassList("ps-parent-initial");
            avatar.Add(initial);
            row.Add(avatar);

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("ps-parent-name");
            row.Add(nameLabel);

            if (isMe)
            {
                var badge = new Label("你");
                badge.AddToClassList("ps-parent-badge");
                row.Add(badge);
            }

            _parentsList.Add(row);
        }
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    void OnClickBack()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        pageManager?.ShowHomePage();
    }

    void OnClickSaveName()
    {
        string newName = (_nameInput?.value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(newName)) return;

        string roomId = FirebaseManager.Instance?.RoomId ?? string.Empty;
        if (string.IsNullOrEmpty(roomId)) return;

        FirebaseManager.Instance.SetRoomName(roomId, newName, ok =>
        {
            if (_nameHint != null)
                _nameHint.text = ok ? "已儲存 ✓" : "儲存失敗，請重試";

            _root?.schedule.Execute(() =>
            {
                if (_nameHint != null) _nameHint.text = string.Empty;
            }).StartingIn(2000);
        });
    }

    void OnClickCopyId()
    {
        string roomId = FirebaseManager.Instance?.RoomId ?? string.Empty;
        if (string.IsNullOrEmpty(roomId)) return;
        GUIUtility.systemCopyBuffer = roomId;

        if (_roomIdLabel != null)
        {
            string original = _roomIdLabel.text;
            _roomIdLabel.text = "已複製！";
            _root?.schedule.Execute(() =>
            {
                if (_roomIdLabel != null) _roomIdLabel.text = original;
            }).StartingIn(1500);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void WireButton(string name, Action cb)
    {
        var btn = _root?.Q<Button>(name);
        if (btn != null) btn.clicked += cb;
    }

    static string GetInitial(string name) =>
        string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpper();

    static string FormatBirthDate(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "—";
        try
        {
            var dt = DateTimeOffset.Parse(iso).ToLocalTime();
            return dt.ToString("yyyy / MM / dd");
        }
        catch { return iso; }
    }
}
