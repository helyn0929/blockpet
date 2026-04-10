using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small strip above the input bar showing the active reply target.
/// </summary>
public class GroupChatReplyPreview : MonoBehaviour
{
    [SerializeField] GameObject root;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text previewText;
    [SerializeField] Button cancelButton;

    Action _onCancel;

    void Awake()
    {
        if (cancelButton != null)
            cancelButton.onClick.AddListener(Cancel);
    }

    void OnDestroy()
    {
        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(Cancel);
    }

    public void Set(
        string replyToDisplayName,
        string replyToPreview,
        Action onCancel)
    {
        _onCancel = onCancel;

        if (titleText != null)
            titleText.text = string.IsNullOrEmpty(replyToDisplayName) ? "Reply" : $"Replying to {replyToDisplayName}";
        if (previewText != null)
            previewText.text = replyToPreview ?? string.Empty;

        if (root != null)
            root.SetActive(true);
        else
            gameObject.SetActive(true);
    }

    public void Clear()
    {
        _onCancel = null;
        if (root != null)
            root.SetActive(false);
        else
            gameObject.SetActive(false);
    }

    void Cancel()
    {
        _onCancel?.Invoke();
    }
}

