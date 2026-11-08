using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 舊相簿格子（AlbumUI → PhotoItemClick）點照片的入口：2026-11-07 起改成打開拍立得分割畫面（設計文件 5.26），
/// 舊的全螢幕照片頁（PhotoDetailUIPage）拿掉。照片順序同舊相簿（新到舊）。
/// </summary>
public class AlbumUIManager : MonoBehaviour
{
    public static AlbumUIManager Instance { get; private set; }

    /// <summary>
    /// Use this from UI callbacks. <see cref="Instance"/> is only set after <c>Awake</c>;
    /// if this component sits on an inactive GameObject, <c>Awake</c> has not run yet and <c>Instance</c> is null.
    /// </summary>
    public static AlbumUIManager Resolve()
    {
        if (Instance != null)
            return Instance;
        return FindObjectOfType<AlbumUIManager>(true);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void OpenPhotoDetail(PhotoMeta meta)
    {
        if (meta == null || SaveManager.Instance == null)
            return;

        // 舊相簿的順序：新到舊
        List<PhotoMeta> allPhotos = SaveManager.Instance.data?.photos
            ?.Where(p => p != null && !string.IsNullOrEmpty(p.fileName))
            .OrderByDescending(p => p.timestamp ?? "")
            .ToList() ?? new List<PhotoMeta>();

        int index = allPhotos.FindIndex(p => p != null && p.fileName == meta.fileName);
        BlockPet.UI.Polaroid.PolaroidScreen.Open(allPhotos, Mathf.Max(0, index));
    }
}
