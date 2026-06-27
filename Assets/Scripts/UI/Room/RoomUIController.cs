using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class RoomUIController : MonoBehaviour
{
    [SerializeField] PageManager pageManager;
    [Tooltip("Pet sprites cycled across rooms: kirby, ibu, imagine, …")]
    [SerializeField] Sprite[] petSprites;
    [Tooltip("Pet names matching petSprites order: Kirby, Image, …")]
    [SerializeField] string[] petNames;

    UIDocument _doc;
    VisualElement _root;
    VisualElement _petWorld;
    VisualElement _joinPanel;
    VisualElement _emptyState;
    TextField _joinInput;

    bool _joinPanelOpen;
    VisualElement _codeOverlay;
    Label _generatedCodeLabel;
    List<FirebaseManager.RoomSummary> _rooms = new();
    List<PetActor> _actors = new();
    Firebase.Database.DatabaseReference _userRoomsRef;

    // Wandering constants
    const float PetSpeedPx   = 45f;   // pixels per second
    const float TargetRadius  = 8f;    // distance to consider target reached
    const float MarginTop     = 20f;   // small top margin
    const float MarginBottom  = 110f;  // clear action bar
    const float MarginSide    = 10f;
    const float PetHalfW      = 165f;  // half pet element width for clamping

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Awake()
    {
        _doc = GetComponent<UIDocument>();
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
    }

    void OnEnable()
    {
        _root      = _doc.rootVisualElement;
        _petWorld  = _root.Q<VisualElement>("pet-world");
        _joinPanel = _root.Q<VisualElement>("join-panel");
        _emptyState = _root.Q<VisualElement>("empty-state");
        _joinInput = _root.Q<TextField>("join-input");
        _codeOverlay = _root.Q<VisualElement>("code-overlay");
        _generatedCodeLabel = _root.Q<Label>("generated-code");

        WireButton("btn-settings",     OnClickSettings);
        WireButton("btn-create",       OnClickCreate);
        WireButton("btn-join-toggle",  OnClickJoinToggle);
        WireButton("btn-join-confirm", OnClickJoinConfirm);
        WireButton("btn-enter-room",   OnClickEnterRoom);

        if (_joinInput != null)
            _joinInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    OnClickJoinConfirm();
            });

        StartRoomsListener();
        RefreshPage();
    }

    void OnDisable()
    {
        StopRoomsListener();
        _actors.Clear();
    }

    void OnDestroy() => StopRoomsListener();

    void Update()
    {
        if (_petWorld == null || _actors.Count == 0) return;

        float worldW = _petWorld.resolvedStyle.width;
        float worldH = _petWorld.resolvedStyle.height;
        if (worldW <= 0 || worldH <= 0) return;

        float xMin = MarginSide;
        float xMax = worldW - PetHalfW * 2 - MarginSide;
        float yMin = MarginTop;
        float yMax = worldH - MarginBottom - PetHalfW * 2;

        float dt = Time.deltaTime;

        foreach (var actor in _actors)
        {
            Vector2 dir = actor.target - actor.pos;
            float dist = dir.magnitude;

            if (dist < TargetRadius)
            {
                actor.target = new Vector2(
                    UnityEngine.Random.Range(xMin, xMax),
                    UnityEngine.Random.Range(yMin, yMax));
            }
            else
            {
                actor.pos += dir.normalized * PetSpeedPx * dt;
            }

            // Clamp inside world
            actor.pos.x = Mathf.Clamp(actor.pos.x, xMin, xMax);
            actor.pos.y = Mathf.Clamp(actor.pos.y, yMin, yMax);

            // Flip horizontally based on movement direction
            if (dir.x < -1f)
                actor.el.style.scale = new StyleScale(new Scale(new Vector3(-1, 1, 1)));
            else if (dir.x > 1f)
                actor.el.style.scale = new StyleScale(new Scale(new Vector3(1, 1, 1)));

            actor.el.style.left = actor.pos.x;
            actor.el.style.top  = actor.pos.y;
        }
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    void StartRoomsListener()
    {
        if (_userRoomsRef != null) return;
        var fb = FirebaseManager.Instance;
        if (fb == null) return;
        _userRoomsRef = fb.GetUserRoomsRef();
        if (_userRoomsRef == null) return;
        _userRoomsRef.ValueChanged += OnRoomsChanged;
    }

    void StopRoomsListener()
    {
        if (_userRoomsRef != null)
            _userRoomsRef.ValueChanged -= OnRoomsChanged;
        _userRoomsRef = null;
    }

    void OnRoomsChanged(object sender, Firebase.Database.ValueChangedEventArgs args)
    {
        if (args?.DatabaseError != null) return;
        RefreshPage();
    }

    void RefreshPage()
    {
        var fb = FirebaseManager.Instance;
        if (fb == null) { SpawnPets(new List<FirebaseManager.RoomSummary>()); return; }

        fb.GetMyRoomSummaries(list =>
        {
            list ??= new List<FirebaseManager.RoomSummary>();
            string currentId = fb.RoomId;
            if (!string.IsNullOrEmpty(currentId) && !list.Exists(r => r.roomId == currentId))
                list.Insert(0, new FirebaseManager.RoomSummary { roomId = currentId, name = currentId });
            _rooms = list;
            SpawnPets(list);
        });
    }

    // ── Pet spawning ──────────────────────────────────────────────────────────

    void SpawnPets(List<FirebaseManager.RoomSummary> rooms)
    {
        _petWorld?.Clear();
        _actors.Clear();

        bool any = rooms.Count > 0;
        if (_emptyState != null)
        {
            if (any) _emptyState.RemoveFromClassList("empty-state--visible");
            else     _emptyState.AddToClassList("empty-state--visible");
        }

        float worldW = _petWorld?.resolvedStyle.width  ?? Screen.width;
        float worldH = _petWorld?.resolvedStyle.height ?? Screen.height;
        // Fallback if layout not ready yet
        if (worldW <= 1) worldW = Screen.width;
        if (worldH <= 1) worldH = Screen.height;

        string currentId = FirebaseManager.Instance?.RoomId ?? string.Empty;

        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            bool isCurrent = room.roomId == currentId;

            // Random spawn position
            float sx = UnityEngine.Random.Range(MarginSide, worldW - PetHalfW * 2 - MarginSide);
            float sy = UnityEngine.Random.Range(MarginTop,  worldH - MarginBottom - PetHalfW * 2);
            float tx = UnityEngine.Random.Range(MarginSide, worldW - PetHalfW * 2 - MarginSide);
            float ty = UnityEngine.Random.Range(MarginTop,  worldH - MarginBottom - PetHalfW * 2);

            var el = BuildPetElement(room, isCurrent, i);
            el.style.left = sx;
            el.style.top  = sy;
            _petWorld?.Add(el);

            _actors.Add(new PetActor
            {
                el     = el,
                pos    = new Vector2(sx, sy),
                target = new Vector2(tx, ty),
            });
        }
    }

    VisualElement BuildPetElement(FirebaseManager.RoomSummary room, bool isCurrent, int index)
    {
        var pet = new VisualElement();
        pet.AddToClassList("pet");

        // Photo chat bubble — only shown when a photo exists, floats above the sprite
        var latestPhoto = SaveManager.Instance?.GetLatestPhotoTextureForRoom(room.roomId);
        if (latestPhoto != null)
        {
            var bubble = new VisualElement();
            bubble.AddToClassList("pet-bubble");
            if (isCurrent) bubble.AddToClassList("pet-bubble--active");
            bubble.style.backgroundImage = new StyleBackground(latestPhoto);
            pet.Add(bubble);
        }

        // Pet sprite — always visible below the bubble
        var img = new VisualElement();
        img.AddToClassList("pet-sprite");
        if (isCurrent) img.AddToClassList("pet-sprite--active");
        if (petSprites != null && petSprites.Length > 0)
        {
            var sprite = petSprites[room.petIndex % petSprites.Length];
            if (sprite != null)
                img.style.backgroundImage = new StyleBackground(sprite);
        }
        pet.Add(img);

        // Pet name label — prefer custom name, fall back to species name
        string displayName = !string.IsNullOrEmpty(room.name)
            ? room.name
            : PetNameAt(room.petIndex);
        var nameLabel = new Label(displayName);
        nameLabel.AddToClassList("pet-name");
        if (isCurrent) nameLabel.AddToClassList("pet-name--active");
        pet.Add(nameLabel);

        // "目前" badge for active room
        if (isCurrent)
        {
            var badge = new Label("目前");
            badge.AddToClassList("pet-badge");
            pet.Add(badge);
        }

        // Tap to enter room — PointerDown fires immediately on touch regardless of drift
        string capturedId = room.roomId;
        pet.RegisterCallback<PointerDownEvent>(_ => EnterRoom(capturedId));

        return pet;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    void OnClickSettings() => pageManager?.ShowSettingsPage();

    void OnClickCreate()
    {
        string code = GenerateCode(6);
        FirebaseManager.Instance?.SetRoomId(code);
        GUIUtility.systemCopyBuffer = code;
        string defaultPetName = PetNameAt(0);
        FirebaseManager.Instance?.CreateRoom(code, defaultPetName, (ok, err) =>
        {
            if (!ok) Debug.LogWarning("[RoomUIController] CreateRoom failed: " + err);
        });
        if (_generatedCodeLabel != null) _generatedCodeLabel.text = code;
        _codeOverlay?.AddToClassList("code-overlay--visible");
    }

    void OnClickEnterRoom()
    {
        _codeOverlay?.RemoveFromClassList("code-overlay--visible");
        EnterGameAndGoHome();
    }

    void OnClickJoinToggle()
    {
        _joinPanelOpen = !_joinPanelOpen;
        if (_joinPanelOpen)
        {
            _joinPanel?.AddToClassList("join-panel--visible");
            _joinInput?.Focus();
        }
        else
        {
            _joinPanel?.RemoveFromClassList("join-panel--visible");
        }
    }

    void OnClickJoinConfirm()
    {
        string code = (_joinInput?.value ?? string.Empty).Trim().ToUpper();
        if (string.IsNullOrEmpty(code)) return;
        FirebaseManager.Instance?.SetRoomId(code);
        FirebaseManager.Instance?.JoinRoom(code, (ok, err) =>
        {
            if (!ok) Debug.LogWarning("[RoomUIController] JoinRoom failed: " + err);
        });
        if (_joinInput != null) _joinInput.value = string.Empty;
        _joinPanel?.RemoveFromClassList("join-panel--visible");
        _joinPanelOpen = false;
        EnterGameAndGoHome();
    }

    void EnterRoom(string roomId)
    {
        FirebaseManager.Instance?.SetRoomId(roomId);
        FirebaseManager.Instance?.JoinRoom(roomId, (ok, err) =>
        {
            if (!ok) Debug.LogWarning("[RoomUIController] JoinRoom failed: " + err);
        });
        EnterGameAndGoHome();
    }

    void EnterGameAndGoHome()
    {
        FindObjectOfType<LoginUIHandler>(true)?.EnterMainGame();
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        pageManager?.ShowHomePage();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void WireButton(string name, Action cb)
    {
        var btn = _root?.Q<Button>(name);
        if (btn != null) btn.clicked += cb;
    }

    string PetNameAt(int index)
    {
        if (petNames != null && petNames.Length > 0)
            return petNames[index % petNames.Length];
        // Hardcoded fallbacks matching default sprite order
        string[] defaults = { "Kirby", "Image" };
        return defaults[index % defaults.Length];
    }

    static string GenerateCode(int len)
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var chars = new char[len];
        for (int i = 0; i < len; i++)
            chars[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
        return new string(chars);
    }

    // ── PetActor ──────────────────────────────────────────────────────────────

    class PetActor
    {
        public VisualElement el;
        public Vector2       pos;
        public Vector2       target;
    }
}
