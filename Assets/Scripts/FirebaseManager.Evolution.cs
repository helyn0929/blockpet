using System;
using System.Collections.Generic;
using BlockPet.Feed;
using Firebase.Database;
using UnityEngine;

/// <summary>
/// 寵物進化結果（設計文件 5.17 ④-2，2026-11-14）：Rooms/{營地}/evolution＝{type, main, sub, palette[5], at, by}。
/// 誰先點「進化」誰判定：交易寫入，已經有結果就不覆蓋，所以營地每個人看到同一個結果。
/// 不放在 petState 底下：petState 每次餵食會整份重寫，會把進化結果蓋掉。
/// </summary>
public partial class FirebaseManager
{
    [Serializable]
    public class EvolutionRecord
    {
        public string type;
        public int main;
        public int sub;
        public int[] palette;
        public long at;
        public string by;
    }

    /// <summary>進化結果變了（主執行緒）：別的成員進化了、換空間、清掉測試。</summary>
    public static event Action OnEvolutionChanged;

    DatabaseReference _evoRef;
    string _evoRoom;

    /// <summary>目前寵物的進化結果；null＝還沒進化。</summary>
    public EvolutionRecord CurrentEvolution { get; private set; }
    /// <summary>進化資料讀到了沒（還沒讀到前不要顯示「可以進化了」以外的判斷）。</summary>
    public bool EvolutionLoaded { get; private set; }

    void StartEvolutionListening()
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId)) return;
        if (_evoRef != null && _evoRoom == _roomId) return;
        StopEvolutionListening();
        _evoRoom = _roomId;
        _evoRef = RoomRoot().Child("evolution");
        _evoRef.ValueChanged += HandleEvolutionChanged;
    }

    void StopEvolutionListening()
    {
        if (_evoRef != null) _evoRef.ValueChanged -= HandleEvolutionChanged;
        _evoRef = null;
        _evoRoom = null;
        bool had = CurrentEvolution != null;
        CurrentEvolution = null;
        EvolutionLoaded = false;
        if (had) OnEvolutionChanged?.Invoke();
    }

    void HandleEvolutionChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogWarning("[FirebaseManager] 讀進化結果失敗：" + e.DatabaseError.Message); return; }
        EvolutionRecord rec = null;
        if (e.Snapshot != null && e.Snapshot.Exists) rec = ParseEvolution(e.Snapshot.Value as Dictionary<string, object>);
        string room = _evoRoom;
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (room != _evoRoom) return;
            CurrentEvolution = rec;
            EvolutionLoaded = true;
            OnEvolutionChanged?.Invoke();
        });
    }

    static EvolutionRecord ParseEvolution(Dictionary<string, object> d)
    {
        if (d == null || !d.ContainsKey("type")) return null;
        var rec = new EvolutionRecord
        {
            type = d["type"]?.ToString(),
            main = (int)ReadLongObj(d.TryGetValue("main", out var m) ? m : null),
            sub = (int)ReadLongObj(d.TryGetValue("sub", out var sb) ? sb : null),
            at = ReadLongObj(d.TryGetValue("at", out var a) ? a : null),
            by = d.TryGetValue("by", out var b) ? b?.ToString() : null,
            palette = new int[5],
        };
        if (d.TryGetValue("palette", out var p) && p is List<object> list)
            for (int i = 0; i < list.Count && i < 5; i++) rec.palette[i] = (int)ReadLongObj(list[i]);
        return rec;
    }

    static Dictionary<string, object> EvolutionToDict(EvolutionRecord r)
    {
        var pal = new List<object>();
        for (int i = 0; i < 5; i++) pal.Add((long)(r.palette != null && i < r.palette.Length ? r.palette[i] : 0));
        return new Dictionary<string, object>
        {
            { "type", r.type ?? DexRules.Evolved }, { "main", (long)r.main }, { "sub", (long)r.sub },
            { "palette", pal }, { "at", r.at }, { "by", r.by ?? "" },
        };
    }

    /// <summary>
    /// 進化：交易寫入 Rooms/{營地}/evolution。已經有結果（別人先點了）就不寫，回傳那個結果。
    /// done(結果, 是不是我判定的)，主執行緒；失敗是 (null, false)。
    /// </summary>
    public void ClaimEvolution(EvolutionResult r, Action<EvolutionRecord, bool> done)
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId)) { done?.Invoke(null, false); return; }
        var mine = new EvolutionRecord { type = r.type, main = r.main, sub = r.sub, palette = r.palette, at = ServerNowMs, by = GetUserId() };
        var dict = EvolutionToDict(mine);
        bool claimed = false;
        EvolutionRecord existing = null;
        string room = _roomId;
        RoomRoot().Child("evolution").RunTransaction(data =>
        {
            if (data.Value != null)
            {
                claimed = false;
                existing = ParseEvolution(data.Value as Dictionary<string, object>);
                return TransactionResult.Abort();
            }
            claimed = true;
            data.Value = dict;
            return TransactionResult.Success(data);
        }).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] 進化寫入失敗（Firebase 規則要允許 evolution）：" + t.Exception?.GetBaseException()?.Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
            {
                if (room != _roomId) { done?.Invoke(null, false); return; }
                if (claimed && ok) { CurrentEvolution = mine; EvolutionLoaded = true; done?.Invoke(mine, true); return; }
                if (existing != null) { done?.Invoke(existing, false); return; }
                done?.Invoke(null, false);
            });
        });
    }

    /// <summary>（測試用）清掉進化結果，寵物回到可以進化的狀態。</summary>
    public void DebugClearEvolution()
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId)) return;
        RoomRoot().Child("evolution").RemoveValueAsync();
    }
}
