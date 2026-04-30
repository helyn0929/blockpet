using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen settings overlay. Assign all UI references in the Inspector.
///
/// Recommended hierarchy under a Canvas:
///   SettingsPanelRoot (this script, starts inactive)
///     Backdrop  — full-screen semi-transparent Image (blocks touches)
///     Card      — white rounded panel
///       AvatarImage         — RawImage (circular mask)
///       ChangeAvatarButton  — Button
///       NicknameText        — TMP_Text  (displays current name)
///       EditNicknameButton  — Button
///       NicknameInputRow    — GameObject (starts inactive)
///         NicknameInputField — TMP_InputField
///         SaveNicknameButton — Button
///       DeleteAccountButton — Button
///       CloseButton         — Button
///   ConfirmDeleteOverlay  — child overlay, starts inactive
///     ConfirmText         — TMP_Text
///     ConfirmYesButton    — Button
///     ConfirmNoButton     — Button
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Avatar")]
    [SerializeField] RawImage avatarImage;
    [SerializeField] Texture defaultAvatar;
    [SerializeField] Button changeAvatarButton;

    [Header("Nickname")]
    [SerializeField] TMP_Text nicknameText;
    [SerializeField] Button editNicknameButton;
    [SerializeField] GameObject nicknameInputRow;
    [SerializeField] TMP_InputField nicknameInputField;
    [SerializeField] Button saveNicknameButton;
    [SerializeField] TMP_Text statusText;

    [Header("Account")]
    [SerializeField] Button deleteAccountButton;
    [SerializeField] Button closeButton;

    [Header("Confirm Delete Overlay")]
    [SerializeField] GameObject confirmDeleteOverlay;
    [SerializeField] Button confirmYesButton;
    [SerializeField] Button confirmNoButton;

    [Header("Navigation")]
    [SerializeField] PageManager pageManager;

    void Awake()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
    }

    void OnEnable()
    {
        Wire(changeAvatarButton,  OnClickChangeAvatar);
        Wire(editNicknameButton,  OnClickEditNickname);
        Wire(saveNicknameButton,  OnClickSaveNickname);
        Wire(deleteAccountButton, OnClickDeleteAccount);
        Wire(closeButton,         OnClickClose);
        Wire(confirmYesButton,    OnClickConfirmDelete);
        Wire(confirmNoButton,     OnClickCancelDelete);

        AvatarManager.OnAvatarChanged += RefreshAvatar;
        RefreshAll();
    }

    void OnDisable()
    {
        Unwire(changeAvatarButton,  OnClickChangeAvatar);
        Unwire(editNicknameButton,  OnClickEditNickname);
        Unwire(saveNicknameButton,  OnClickSaveNickname);
        Unwire(deleteAccountButton, OnClickDeleteAccount);
        Unwire(closeButton,         OnClickClose);
        Unwire(confirmYesButton,    OnClickConfirmDelete);
        Unwire(confirmNoButton,     OnClickCancelDelete);

        AvatarManager.OnAvatarChanged -= RefreshAvatar;
    }

    public void Open()
    {
        gameObject.SetActive(true);
        if (confirmDeleteOverlay != null) confirmDeleteOverlay.SetActive(false);
        if (nicknameInputRow != null)     nicknameInputRow.SetActive(false);
        SetStatus("");
        RefreshAll();
    }

    void OnClickClose()
    {
        gameObject.SetActive(false);
    }

    // ── Avatar ───────────────────────────────────────────────────────────────

    void RefreshAvatar()
    {
        if (avatarImage == null) return;
        Texture tex = AvatarManager.Instance != null ? AvatarManager.Instance.CurrentAvatar : null;
        avatarImage.texture = tex != null ? tex : defaultAvatar;
    }

    void OnClickChangeAvatar()
    {
        if (AvatarManager.Instance == null) return;
        AvatarManager.Instance.PickAvatarFromGallery(ok =>
        {
            SetStatus(ok ? "大頭貼已更新！" : "取消更換");
        });
    }

    // ── Nickname ─────────────────────────────────────────────────────────────

    void RefreshNickname()
    {
        string name = FirebaseManager.Instance != null
            ? FirebaseManager.Instance.GetDisplayName()
            : "Guest";
        if (nicknameText != null)
            nicknameText.text = name;
    }

    void OnClickEditNickname()
    {
        if (nicknameInputRow == null) return;
        nicknameInputRow.SetActive(true);
        if (nicknameInputField != null)
        {
            nicknameInputField.text = FirebaseManager.Instance != null
                ? FirebaseManager.Instance.GetDisplayName()
                : "";
            nicknameInputField.Select();
        }
        SetStatus("");
    }

    void OnClickSaveNickname()
    {
        string name = nicknameInputField != null ? nicknameInputField.text.Trim() : "";
        if (string.IsNullOrEmpty(name)) { SetStatus("暱稱不能為空"); return; }

        SetStatus("儲存中…");
        FirebaseManager.Instance?.SetDisplayName(name, ok =>
        {
            if (ok)
            {
                RefreshNickname();
                if (nicknameInputRow != null) nicknameInputRow.SetActive(false);
                SetStatus("暱稱已更新！");
            }
            else
            {
                SetStatus("儲存失敗，請重試");
            }
        });
    }

    // ── Delete account ───────────────────────────────────────────────────────

    void OnClickDeleteAccount()
    {
        if (confirmDeleteOverlay != null) confirmDeleteOverlay.SetActive(true);
    }

    void OnClickCancelDelete()
    {
        if (confirmDeleteOverlay != null) confirmDeleteOverlay.SetActive(false);
    }

    void OnClickConfirmDelete()
    {
        SetStatus("刪除中…");
        if (confirmDeleteOverlay != null) confirmDeleteOverlay.SetActive(false);

        FirebaseManager.Instance?.DeleteAccount((ok, err) =>
        {
            if (ok)
            {
                // Clear local data and return to login screen.
                AvatarManager.Instance?.ClearAvatar();
                if (SaveManager.Instance != null)
                {
                    SaveManager.Instance.data = new SaveData();
                    SaveManager.Instance.Save();
                }
                gameObject.SetActive(false);

                // Reload the login scene / reactivate login screen.
                var loginScreen = FindObjectOfType<LoginScreenController>(true);
                if (loginScreen != null) loginScreen.gameObject.SetActive(true);
                else UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
            else
            {
                SetStatus($"刪除失敗：{err}");
            }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    void RefreshAll()
    {
        RefreshAvatar();
        RefreshNickname();
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    static void Wire(Button b, UnityEngine.Events.UnityAction a)
    {
        if (b != null) { b.onClick.RemoveListener(a); b.onClick.AddListener(a); }
    }

    static void Unwire(Button b, UnityEngine.Events.UnityAction a)
    {
        if (b != null) b.onClick.RemoveListener(a);
    }
}
