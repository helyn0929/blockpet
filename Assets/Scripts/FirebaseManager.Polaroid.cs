using System;
using System.Collections.Generic;
using BlockPet.Polaroid;
using Firebase.Database;
using UnityEngine;

/// <summary>
/// 拍立得分割畫面的資料（設計文件 5.26）：照片上的貼紙、拍照的人設定的畫框／濾鏡／標題，營地成員都看得到。
/// Rooms/{room}/polaroid/{照片鍵}/style＝{frame, filter, caption, by, at}；/stickers/{id}＝PolaroidSticker。
/// 照片鍵＝PolaroidRules.PhotoKey(檔名)。誰能改的規則在 PolaroidRules（畫面上先擋），這裡只負責讀寫。
/// </summary>
public partial class FirebaseManager
{
    /// <summary>正在看的那張照片的拍立得資料變了（參數＝照片檔名）。主執行緒。</summary>
    public static event Action<string> OnPolaroidChanged;

    DatabaseReference _polaroidRef;
    string _polaroidFile, _polaroidRoom;
    readonly List<PolaroidSticker> _polaroidStickers = new List<PolaroidSticker>();

    public PolaroidStyle PolaroidStyleNow { get; private set; } = new PolaroidStyle();
    public IReadOnlyList<PolaroidSticker> PolaroidStickersNow => _polaroidStickers;
    public string PolaroidFileNow => _polaroidFile;

    DatabaseReference PolaroidNode(string fileName) =>
        dbRef == null || string.IsNullOrEmpty(_roomId) || string.IsNullOrEmpty(fileName)
            ? null : RoomRoot().Child("polaroid").Child(PolaroidRules.PhotoKey(fileName));

    /// <summary>開始看某張照片的拍立得資料（換張就換監聽；同一張不重複）。</summary>
    public void WatchPolaroid(string fileName)
    {
        if (fileName == _polaroidFile && _polaroidRoom == _roomId && _polaroidRef != null) return;
        StopPolaroid();
        var node = PolaroidNode(fileName);
        if (node == null || auth?.CurrentUser == null) return;
        _polaroidFile = fileName;
        _polaroidRoom = _roomId;
        _polaroidRef = node;
        _polaroidRef.ValueChanged += HandlePolaroidChanged;
    }

    public void StopPolaroid()
    {
        if (_polaroidRef != null) _polaroidRef.ValueChanged -= HandlePolaroidChanged;
        _polaroidRef = null;
        _polaroidFile = null;
        _polaroidRoom = null;
        _polaroidStickers.Clear();
        PolaroidStyleNow = new PolaroidStyle();
    }

    void HandlePolaroidChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogWarning("[FirebaseManager] 讀拍立得失敗：" + e.DatabaseError.Message); return; }
        var style = new PolaroidStyle();
        var stickers = new List<PolaroidSticker>();
        var snap = e.Snapshot;
        if (snap != null && snap.Exists)
        {
            var st = snap.Child("style");
            if (st.Exists) try { style = JsonUtility.FromJson<PolaroidStyle>(st.GetRawJsonValue()) ?? style; } catch { }
            foreach (var c in snap.Child("stickers").Children)
            {
                try
                {
                    var s = JsonUtility.FromJson<PolaroidSticker>(c.GetRawJsonValue());
                    if (s == null || string.IsNullOrEmpty(s.kind)) continue;
                    s.id = c.Key;
                    stickers.Add(s);
                }
                catch { }
            }
            stickers.Sort((a, b) => a.at != b.at ? a.at.CompareTo(b.at) : string.CompareOrdinal(a.id, b.id));
        }
        var file = _polaroidFile;
        var room = _polaroidRoom;
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (file != _polaroidFile || room != _polaroidRoom) return; // 已經換張
            PolaroidStyleNow = style;
            _polaroidStickers.Clear();
            _polaroidStickers.AddRange(stickers);
            OnPolaroidChanged?.Invoke(file);
        });
    }

    /// <summary>拍照的人換畫框／濾鏡／標題（整份 style 一起寫）。</summary>
    public void SetPolaroidStyle(string fileName, PolaroidStyle style, Action<bool> done = null)
    {
        var node = PolaroidNode(fileName);
        if (node == null || style == null) { done?.Invoke(false); return; }
        style.by = GetUserId();
        style.at = ServerNowMs;
        node.Child("style").SetRawJsonValueAsync(JsonUtility.ToJson(style)).ContinueWith(t =>
        {
            bool ok = !t.IsFaulted && !t.IsCanceled;
            if (!ok) Debug.LogWarning("[FirebaseManager] 存拍立得樣式失敗：" + t.Exception?.GetBaseException().Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    /// <summary>貼一張貼紙；回傳新的 id（失敗是 null）。</summary>
    public string AddPolaroidSticker(string fileName, PolaroidSticker s)
    {
        var node = PolaroidNode(fileName);
        if (node == null || s == null) return null;
        var r = node.Child("stickers").Push();
        s.id = r.Key;
        s.by = GetUserId();
        s.byName = GetDisplayName();
        s.at = ServerNowMs;
        r.SetRawJsonValueAsync(JsonUtility.ToJson(s)).ContinueWith(t =>
        {
            if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] 貼貼紙失敗：" + t.Exception?.GetBaseException().Message);
        });
        return s.id;
    }

    /// <summary>拖完自己的貼紙：寫回新位置。</summary>
    public void MovePolaroidSticker(string fileName, string id, float x, float y)
    {
        var node = PolaroidNode(fileName);
        if (node == null || string.IsNullOrEmpty(id)) return;
        node.Child("stickers").Child(id).UpdateChildrenAsync(new Dictionary<string, object> { { "x", (double)x }, { "y", (double)y } });
    }

    /// <summary>撕掉貼紙。</summary>
    public void RemovePolaroidSticker(string fileName, string id)
    {
        var node = PolaroidNode(fileName);
        if (node == null || string.IsNullOrEmpty(id)) return;
        node.Child("stickers").Child(id).RemoveValueAsync();
    }
}
