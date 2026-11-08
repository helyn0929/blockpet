using System;
using System.Collections.Generic;
using Firebase.Database;
using UnityEngine;

/// <summary>
/// 營地寵物正在穿的穿搭（設計文件 5.11 ⑧）：Rooms/{room}/outfit/{部位}＝穿搭 id（head／face／neck／side）。
/// 待決 36 暫定營地共用（和家具一樣），所以營地每個成員看到的寵物都穿著同一套。買過的穿搭記在 Rooms/{room}/shop（同家具）。
/// </summary>
public partial class FirebaseManager
{
    /// <summary>穿著變了（主執行緒）。</summary>
    public static event Action OnOutfitChanged;

    DatabaseReference _outfitRef;
    string _outfitRoom;
    readonly Dictionary<string, string> _worn = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> WornOutfit => _worn;

    /// <summary>開始聽目前空間的穿搭（重複呼叫沒關係；換空間會自動換）。</summary>
    public void StartOutfit()
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId)) return;
        if (_outfitRoom == _roomId && _outfitRef != null) return;
        StopOutfit();
        _outfitRoom = _roomId;
        _outfitRef = RoomRoot().Child("outfit");
        _outfitRef.ValueChanged += HandleOutfitChanged;
    }

    void StopOutfit()
    {
        if (_outfitRef != null) _outfitRef.ValueChanged -= HandleOutfitChanged;
        _outfitRef = null;
        _outfitRoom = null;
        if (_worn.Count > 0) { _worn.Clear(); OnOutfitChanged?.Invoke(); }
    }

    void HandleOutfitChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogWarning("[FirebaseManager] 讀穿搭失敗：" + e.DatabaseError.Message); return; }
        var next = new Dictionary<string, string>();
        if (e.Snapshot != null && e.Snapshot.Exists)
            foreach (var c in e.Snapshot.Children)
                if (c.Value is string id && !string.IsNullOrEmpty(id)) next[c.Key] = id;
        string room = _outfitRoom;
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (room != _outfitRoom) return;
            _worn.Clear();
            foreach (var kv in next) _worn[kv.Key] = kv.Value;
            OnOutfitChanged?.Invoke();
        });
    }

    /// <summary>把穿著寫成這一套（整份寫入：同部位只能一件由 OutfitRules 先算好）。</summary>
    public void SetWornOutfit(IReadOnlyDictionary<string, string> worn, Action<bool> done = null)
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId)) { done?.Invoke(false); return; }
        var data = new Dictionary<string, object>();
        if (worn != null) foreach (var kv in worn) data[kv.Key] = kv.Value;
        // 先在本機更新（畫面馬上換），Firebase 回來會再同步一次
        _worn.Clear();
        if (worn != null) foreach (var kv in worn) _worn[kv.Key] = kv.Value;
        OnOutfitChanged?.Invoke();
        RoomRoot().Child("outfit").SetValueAsync(data.Count > 0 ? data : null).ContinueWith(t =>
        {
            bool ok = !t.IsFaulted && !t.IsCanceled;
            if (!ok) Debug.LogWarning("[FirebaseManager] 存穿搭失敗：" + t.Exception?.GetBaseException().Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }
}
