using System;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class SettingsUIController : MonoBehaviour
{
    [SerializeField] PageManager pageManager;
    [SerializeField] LoginUIHandler loginUIHandler;

    const string PrivacyPolicyUrl = "https://blockpet.app/privacy";

    UIDocument _doc;
    VisualElement _root;

    Label _displayName;
    VisualElement _avatarImage;

    VisualElement _nameOverlay;
    TextField _nicknameField;
    Label _nicknameStatus;

    VisualElement _passwordOverlay;
    TextField _newPasswordField;
    TextField _confirmPasswordField;
    Label _passwordStatus;

    VisualElement _deleteOverlay;

    void Awake()
    {
        _doc = GetComponent<UIDocument>();
        if (pageManager == null)    pageManager    = FindObjectOfType<PageManager>(true);
        if (loginUIHandler == null) loginUIHandler = FindObjectOfType<LoginUIHandler>(true);
    }

    float _dragStartY;
    bool  _dragging;
    const float DismissThreshold = 120f;

    void OnEnable()
    {
        _root = _doc.rootVisualElement;
        _root.style.top = 0;

        // Drag handle — swipe down to close with visual slide
        var handle = _root.Q<VisualElement>("drag-handle-area");
        if (handle != null)
        {
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                _dragStartY = e.position.y;
                _dragging   = true;
                handle.CapturePointer(e.pointerId);
            });

            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging) return;
                float delta = e.position.y - _dragStartY;
                _root.style.top = Mathf.Max(0, delta);
            });

            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_dragging) return;
                _dragging = false;
                handle.ReleasePointer(e.pointerId);

                float delta = e.position.y - _dragStartY;
                if (delta > DismissThreshold)
                    AnimateOutAndClose();
                else
                    AnimateSnapBack();
            });
        }

        WireButton("btn-change-avatar",    OnClickChangeAvatar);
        WireButton("btn-change-name",      OnClickOpenChangeName);
        WireButton("btn-save-nickname",    OnClickSaveNickname);
        WireButton("btn-cancel-name",      OnClickCancelName);
        WireButton("btn-change-password",  OnClickOpenChangePassword);
        WireButton("btn-save-password",    OnClickSavePassword);
        WireButton("btn-cancel-password",  OnClickCancelPassword);
        WireButton("btn-logout",           OnClickLogout);
        WireButton("btn-delete-account",   OnClickDeleteAccount);
        WireButton("btn-confirm-delete",   OnClickConfirmDelete);
        WireButton("btn-cancel-delete",    OnClickCancelDelete);
        WireButton("btn-privacy",          OnClickPrivacy);

        _displayName         = _root.Q<Label>("display-name");
        _avatarImage         = _root.Q<VisualElement>("avatar-image");
        _nameOverlay         = _root.Q<VisualElement>("name-overlay");
        _nicknameField       = _root.Q<TextField>("nickname-field");
        _nicknameStatus      = _root.Q<Label>("nickname-status");
        _passwordOverlay     = _root.Q<VisualElement>("password-overlay");
        _newPasswordField    = _root.Q<TextField>("new-password-field");
        _confirmPasswordField = _root.Q<TextField>("confirm-password-field");
        _passwordStatus      = _root.Q<Label>("password-status");
        _deleteOverlay       = _root.Q<VisualElement>("delete-overlay");

        var versionLabel = _root.Q<Label>("app-version");
        if (versionLabel != null)
            versionLabel.text = $"Version {Application.version}";

        RefreshUI();
    }

    void RefreshUI()
    {
        string name = FirebaseManager.Instance?.GetDisplayName() ?? "";
        if (_displayName != null)
            _displayName.text = string.IsNullOrEmpty(name) ? "Username" : name;

        var tex = AvatarManager.Instance?.CurrentAvatar;
        if (_avatarImage != null && tex != null)
            _avatarImage.style.backgroundImage = new StyleBackground(tex);
    }

    void WireButton(string name, Action callback)
    {
        var btn = _root.Q<Button>(name);
        if (btn != null) btn.clicked += callback;
        else Debug.LogWarning($"[SettingsUIController] Button '{name}' not found.");
    }

    void OnClickClose()
    {
        _root.style.top = 0;
        if (pageManager != null) pageManager.ShowRoomPage();
    }

    void AnimateOutAndClose()
    {
        float screenH = _root.resolvedStyle.height;
        if (screenH <= 0) screenH = Screen.height;
        float start = _root.resolvedStyle.top;
        float elapsed = 0f;
        const float duration = 0.18f;

        _root.schedule.Execute(() =>
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _root.style.top = Mathf.Lerp(start, screenH, t);
            if (t >= 1f) OnClickClose();
        }).Every(16).Until(() => false);
    }

    void AnimateSnapBack()
    {
        float start = _root.resolvedStyle.top;
        float elapsed = 0f;
        const float duration = 0.12f;

        _root.schedule.Execute(() =>
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _root.style.top = Mathf.Lerp(start, 0, t);
        }).Every(16).Until(() => elapsed >= duration);
    }

    void OnClickChangeAvatar()
    {
        if (AvatarManager.Instance == null) return;
        AvatarManager.Instance.PickAvatarFromGallery(ok =>
        {
            if (!ok || _avatarImage == null) return;
            var tex = AvatarManager.Instance?.CurrentAvatar;
            if (tex != null)
                _avatarImage.style.backgroundImage = new StyleBackground(tex);
        });
    }

    void OnClickOpenChangeName()
    {
        if (_nicknameField != null)
            _nicknameField.value = FirebaseManager.Instance?.GetDisplayName() ?? "";
        if (_nicknameStatus != null)
            _nicknameStatus.text = "";
        _nameOverlay?.AddToClassList("overlay--visible");
    }

    void OnClickSaveNickname()
    {
        string name = _nicknameField?.value?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            SetStatus(_nicknameStatus, "請輸入暱稱", danger: true);
            return;
        }

        FirebaseManager.Instance?.SetDisplayName(name, ok =>
        {
            if (ok)
            {
                if (_displayName != null) _displayName.text = name;
                SetStatus(_nicknameStatus, "已儲存！", danger: false);
                _nameOverlay?.RemoveFromClassList("overlay--visible");
            }
            else
            {
                SetStatus(_nicknameStatus, "儲存失敗，請重試", danger: true);
            }
        });
    }

    void OnClickCancelName()
    {
        _nameOverlay?.RemoveFromClassList("overlay--visible");
    }

    // ---- Change Password ----

    void OnClickOpenChangePassword()
    {
        if (_newPasswordField != null)    _newPasswordField.value = "";
        if (_confirmPasswordField != null) _confirmPasswordField.value = "";
        if (_passwordStatus != null)      _passwordStatus.text = "";
        _passwordOverlay?.AddToClassList("overlay--visible");
    }

    void OnClickSavePassword()
    {
        string newPw  = _newPasswordField?.value ?? "";
        string confPw = _confirmPasswordField?.value ?? "";

        if (newPw.Length < 6)
        {
            SetStatus(_passwordStatus, "密碼至少需要 6 位字元", danger: true);
            return;
        }
        if (newPw != confPw)
        {
            SetStatus(_passwordStatus, "兩次輸入的密碼不一致", danger: true);
            return;
        }

        FirebaseManager.Instance?.UpdatePassword(newPw, (ok, err) =>
        {
            if (ok)
            {
                SetStatus(_passwordStatus, "密碼已更新！", danger: false);
                _passwordOverlay?.RemoveFromClassList("overlay--visible");
            }
            else
            {
                SetStatus(_passwordStatus, string.IsNullOrEmpty(err) ? "更新失敗，請重試" : err, danger: true);
            }
        });
    }

    void OnClickCancelPassword()
    {
        _passwordOverlay?.RemoveFromClassList("overlay--visible");
    }

    // ---- Logout / Delete ----

    void OnClickLogout()
    {
        gameObject.SetActive(false);
        if (loginUIHandler != null)
            loginUIHandler.Logout();
        else
        {
            AvatarManager.Instance?.ClearAvatar();
            FirebaseManager.Instance?.SignOut();
        }
    }

    void OnClickDeleteAccount()
    {
        _deleteOverlay?.AddToClassList("overlay--visible");
    }

    void OnClickCancelDelete()
    {
        _deleteOverlay?.RemoveFromClassList("overlay--visible");
    }

    void OnClickConfirmDelete()
    {
        FirebaseManager.Instance?.DeleteAccount((ok, err) =>
        {
            if (ok)
            {
                gameObject.SetActive(false);
                if (loginUIHandler != null)
                    loginUIHandler.Logout();
                else
                {
                    AvatarManager.Instance?.ClearAvatar();
                    FirebaseManager.Instance?.SignOut();
                }
            }
            else
            {
                Debug.LogWarning("[SettingsUIController] deleteAccount failed: " + err);
                _deleteOverlay?.RemoveFromClassList("overlay--visible");
            }
        });
    }

    // ---- Privacy Policy ----

    void OnClickPrivacy()
    {
        Application.OpenURL(PrivacyPolicyUrl);
    }

    // ---- Helpers ----

    void SetStatus(Label label, string message, bool danger)
    {
        if (label == null) return;
        label.text = message;
        label.style.color = danger
            ? new StyleColor(new Color(1f, 0.23f, 0.19f))
            : new StyleColor(new Color(0.2f, 0.78f, 0.35f));
    }
}
