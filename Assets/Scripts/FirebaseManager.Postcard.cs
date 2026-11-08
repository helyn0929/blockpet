using System;
using System.Collections.Generic;
using BlockPet.Travel;
using Firebase.Database;
using UnityEngine;

/// <summary>
/// 寄明信片（設計文件 5.21，2026-11-08；取代旅行的加好友與蓋章）：
/// - 抽好友：CampProfiles/{營地}＝公開的營地卡（名稱、寵物、品種、成員數、最常拍的類別、國家；沒有真名、帳號、照片）。
///   Rooms/{我}/matchOut/{對方}＝我們按了加好友、等對方；Rooms/{對方}/matchIn/{我}＝對方看到「有營地想跟你們當好友！」。
///   雙方都按「加好友」才寫 friends（兩邊）。略過不通知對方；超過 confirmExpireHours 就不顯示。
///   Rooms/{我}/postcardMeta＝{drawDay, draws, unlocks}：今天抽了幾次（台灣時間換日）、好友位置解鎖幾次。
/// - 封鎖：Rooms/{我}/blocked/{對方}＋Rooms/{對方}/blockedBy/{我}，刪掉兩邊的好友（地圖那塊地消失），以後不會再配到。
/// - 檢舉：Reports/{id}（先存資料庫，審核方式待定）。
/// - 明信片：Rooms/{收件營地}/postcards/{id}＝資料；Rooms/{收件營地}/postcardImg/{id}＝JPG base64（分開放，打開護照才讀圖）。
///   寄件營地 Rooms/{我}/sent/{id}＝寄出的那張，replied＝""／"scenery"（寵物回到家帶回對方營地的風景明信片後標記，交易只做一次）。
///   2026-11-08 使用者決定拿掉回寄、各寄各的：想收到對方的照片，要等對方用自己的寵物寄過來。
/// 推播還沒做（2026-11-08 使用者決定先做 App 內通知）：對方打開 App 時看到紅點、明信片彈出。
/// 規則與數字在 PostcardRules／Resources/Postcard/postcard_settings.json。
/// </summary>
public partial class FirebaseManager
{
    public const string PostcardKindPhoto = "photo", PostcardKindReply = "reply", PostcardKindScenery = "scenery";
    const string CampProfilesNode = "CampProfiles";
    const string ReportsNode = "Reports";
    /// <summary>品種：還沒有品種系統，現在的寵物都算 BowBow（DexRules.DefaultSeries，2026-11-05 定）。</summary>
    const string DefaultBreed = "BowBow";

    [Serializable]
    public class MatchRequest
    {
        public string roomId;
        public string name, pet, breed, top, country;
        public int members;
        public long at;

        public CampProfile ToProfile() => new CampProfile { roomId = roomId, name = name, pet = pet, breed = breed, top = top, country = country, members = members, at = at };
    }

    [Serializable]
    public class Postcard
    {
        public string id;
        public string from, fromName, fromPet;
        public int fromSlot = -1;
        /// <summary>photo＝對方用他們的寵物寄來的；scenery＝我們的寵物回家帶回的對方營地風景明信片；reply＝舊資料（回寄已拿掉）。</summary>
        public string kind;
        public string caption;
        public long at;
        /// <summary>什麼時候看得到（寵物送到／帶回來的時間）。</summary>
        public long visibleAt;
        /// <summary>寄來的那隻寵物送到、回到家的時間。</summary>
        public long arriveAt, homeAt;
        public bool replied;
        public string replyTo;
    }

    [Serializable]
    public class SentPostcard
    {
        public string id;
        public string to, toName;
        public int toSlot = -1;
        public long arriveAt, homeAt;
        public string replied = "";
    }

    [Serializable]
    class PostcardMeta
    {
        public string drawDay = "";
        public int draws;
        public int unlocks;
    }

    /// <summary>抽好友、明信片、封鎖的資料變了（主執行緒）。</summary>
    public static event Action OnPostcardChanged;

    static PostcardSettings _postcardSettings;

    /// <summary>寄明信片的設定值（Resources/Postcard/postcard_settings.json；讀不到用預設值）。</summary>
    public static PostcardSettings PostcardConfig
    {
        get
        {
            if (_postcardSettings != null) return _postcardSettings;
            var ta = Resources.Load<TextAsset>("Postcard/postcard_settings");
            try { _postcardSettings = ta != null ? JsonUtility.FromJson<PostcardSettings>(ta.text) : null; } catch { }
            return _postcardSettings ??= new PostcardSettings();
        }
    }

    DatabaseReference _matchInRef, _matchOutRef, _blockedRef, _blockedByRef, _pcMetaRef, _postcardsRef, _sentRef;
    string _postcardRoom;
    PostcardMeta _pcMeta = new PostcardMeta();
    readonly List<MatchRequest> _matchIn = new List<MatchRequest>(), _matchOut = new List<MatchRequest>();
    readonly HashSet<string> _blocked = new HashSet<string>(), _blockedBy = new HashSet<string>();
    readonly List<Postcard> _postcards = new List<Postcard>();
    readonly List<SentPostcard> _sent = new List<SentPostcard>();
    readonly Dictionary<string, Texture2D> _postcardImages = new Dictionary<string, Texture2D>();

    /// <summary>想跟我們當好友的營地（還沒失效的，新的在前）。</summary>
    public IReadOnlyList<MatchRequest> MatchIn => _matchIn;
    /// <summary>我們按了加好友、等對方確認的營地。</summary>
    public IReadOnlyList<MatchRequest> MatchOut => _matchOut;
    /// <summary>收到的明信片（包含還沒送到的；顯示前用 visibleAt 篩）。</summary>
    public IReadOnlyList<Postcard> Postcards => _postcards;
    public IReadOnlyList<SentPostcard> SentPostcards => _sent;
    public int FriendUnlocks => _pcMeta.unlocks;
    public int FriendCapacity => PostcardRules.FriendCapacity(_pcMeta.unlocks, PostcardConfig);
    public int DrawsToday => PostcardRules.DrawsToday(_pcMeta.drawDay, _pcMeta.draws, PostcardRules.DayKey(ServerNowMs));
    public int DrawCostNow => PostcardRules.DrawCost(DrawsToday, PostcardConfig);
    public bool IsBlocked(string roomId) => roomId != null && (_blocked.Contains(roomId) || _blockedBy.Contains(roomId));

    /// <summary>現在看得到的收到明信片（新的在前）。</summary>
    public List<Postcard> VisiblePostcards()
    {
        long now = ServerNowMs;
        var list = _postcards.FindAll(p => p.visibleAt <= now && !IsBlocked(p.from));
        list.Sort((a, b) => b.visibleAt.CompareTo(a.visibleAt));
        return list;
    }

    // ─── 監聽 ──────────────────────────────────────────────────────

    void StartPostcardState()
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId)) return;
        if (_postcardRoom == _roomId && _matchInRef != null) return;
        StopPostcardState();
        _postcardRoom = _roomId;
        var root = RoomRoot();
        _matchInRef = root.Child("matchIn");
        _matchOutRef = root.Child("matchOut");
        _blockedRef = root.Child("blocked");
        _blockedByRef = root.Child("blockedBy");
        _pcMetaRef = root.Child("postcardMeta");
        _postcardsRef = root.Child("postcards");
        _sentRef = root.Child("sent");
        _matchInRef.ValueChanged += HandleMatchIn;
        _matchOutRef.ValueChanged += HandleMatchOut;
        _blockedRef.ValueChanged += HandleBlocked;
        _blockedByRef.ValueChanged += HandleBlockedBy;
        _pcMetaRef.ValueChanged += HandlePcMeta;
        _postcardsRef.ValueChanged += HandlePostcards;
        _sentRef.ValueChanged += HandleSent;
        PublishCampProfile();
    }

    void StopPostcardState()
    {
        if (_matchInRef != null) _matchInRef.ValueChanged -= HandleMatchIn;
        if (_matchOutRef != null) _matchOutRef.ValueChanged -= HandleMatchOut;
        if (_blockedRef != null) _blockedRef.ValueChanged -= HandleBlocked;
        if (_blockedByRef != null) _blockedByRef.ValueChanged -= HandleBlockedBy;
        if (_pcMetaRef != null) _pcMetaRef.ValueChanged -= HandlePcMeta;
        if (_postcardsRef != null) _postcardsRef.ValueChanged -= HandlePostcards;
        if (_sentRef != null) _sentRef.ValueChanged -= HandleSent;
        _matchInRef = _matchOutRef = _blockedRef = _blockedByRef = _pcMetaRef = _postcardsRef = _sentRef = null;
        _postcardRoom = null;
        _pcMeta = new PostcardMeta();
        _matchIn.Clear(); _matchOut.Clear(); _blocked.Clear(); _blockedBy.Clear(); _postcards.Clear(); _sent.Clear();
        foreach (var t in _postcardImages.Values) if (t != null) UnityEngine.Object.Destroy(t);
        _postcardImages.Clear();
    }

    void PostcardEvent(string room, Action apply)
    {
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (room != _postcardRoom) return;
            apply();
            OnPostcardChanged?.Invoke();
        });
    }

    static List<T> ReadChildren<T>(ValueChangedEventArgs e, Action<T, string> setId) where T : class
    {
        var list = new List<T>();
        if (e.DatabaseError != null || e.Snapshot == null || !e.Snapshot.Exists) return list;
        foreach (var c in e.Snapshot.Children)
        {
            T v = null;
            try { v = JsonUtility.FromJson<T>(c.GetRawJsonValue()); } catch { }
            if (v == null) continue;
            setId(v, c.Key);
            list.Add(v);
        }
        return list;
    }

    static HashSet<string> ReadKeys(ValueChangedEventArgs e)
    {
        var set = new HashSet<string>();
        if (e.DatabaseError == null && e.Snapshot != null && e.Snapshot.Exists) foreach (var c in e.Snapshot.Children) set.Add(c.Key);
        return set;
    }

    void HandleMatchIn(object s, ValueChangedEventArgs e)
    {
        var list = ReadChildren<MatchRequest>(e, (m, k) => m.roomId = k);
        list.Sort((a, b) => b.at.CompareTo(a.at));
        PostcardEvent(_postcardRoom, () => { _matchIn.Clear(); _matchIn.AddRange(list.FindAll(m => !PostcardRules.MatchExpired(m.at, ServerNowMs, PostcardConfig))); });
    }

    void HandleMatchOut(object s, ValueChangedEventArgs e)
    {
        var list = ReadChildren<MatchRequest>(e, (m, k) => m.roomId = k);
        PostcardEvent(_postcardRoom, () => { _matchOut.Clear(); _matchOut.AddRange(list.FindAll(m => !PostcardRules.MatchExpired(m.at, ServerNowMs, PostcardConfig))); });
    }

    void HandleBlocked(object s, ValueChangedEventArgs e) { var set = ReadKeys(e); PostcardEvent(_postcardRoom, () => { _blocked.Clear(); _blocked.UnionWith(set); }); }
    void HandleBlockedBy(object s, ValueChangedEventArgs e) { var set = ReadKeys(e); PostcardEvent(_postcardRoom, () => { _blockedBy.Clear(); _blockedBy.UnionWith(set); }); }

    void HandlePcMeta(object s, ValueChangedEventArgs e)
    {
        PostcardMeta m = null;
        if (e.DatabaseError == null && e.Snapshot != null && e.Snapshot.Exists) try { m = JsonUtility.FromJson<PostcardMeta>(e.Snapshot.GetRawJsonValue()); } catch { }
        PostcardEvent(_postcardRoom, () => _pcMeta = m ?? new PostcardMeta());
    }

    void HandlePostcards(object s, ValueChangedEventArgs e)
    {
        var list = ReadChildren<Postcard>(e, (p, k) => p.id = k);
        PostcardEvent(_postcardRoom, () => { _postcards.Clear(); _postcards.AddRange(list); });
    }

    void HandleSent(object s, ValueChangedEventArgs e)
    {
        var list = ReadChildren<SentPostcard>(e, (p, k) => p.id = k);
        PostcardEvent(_postcardRoom, () => { _sent.Clear(); _sent.AddRange(list); });
    }

    // ─── 營地卡（抽好友看得到的） ─────────────────────────────────────

    /// <summary>目前營地最常拍的類別（相簿裡辨識過的照片）。</summary>
    public static string MyTopCategory()
    {
        var counts = new Dictionary<string, int>();
        var photos = SaveManager.Instance != null ? SaveManager.Instance.data?.photos : null;
        if (photos != null)
            foreach (var p in photos)
                if (p != null && !string.IsNullOrEmpty(p.category)) counts[p.category] = (counts.TryGetValue(p.category, out int n) ? n : 0) + 1;
        return PostcardRules.TopCategory(counts);
    }

    /// <summary>更新自己營地的公開營地卡（打開明信片地圖時；成員數、最常拍的類別、國家會變）。</summary>
    public void PublishCampProfile()
    {
        string me = _roomId;
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(me)) return;
        GetRoomMembers(me, members =>
        {
            if (me != _roomId) return;
            var p = MyProfile(members != null ? members.Count : 1);
            dbRef.Child(CampProfilesNode).Child(me).SetRawJsonValueAsync(JsonUtility.ToJson(p)).ContinueWith(t =>
            {
                if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] 更新營地卡失敗（Firebase 規則要允許 CampProfiles）：" + t.Exception?.GetBaseException()?.Message);
            });
        });
    }

    CampProfile MyProfile(int members) => new CampProfile
    {
        roomId = _roomId,
        name = CurrentRoomName ?? "營地",
        pet = CurrentPetName ?? (PetCollectionManager.Instance != null ? PetCollectionManager.Instance.GetPetDisplayName() : ""),
        breed = DefaultBreed,
        members = Mathf.Max(1, members),
        top = MyTopCategory(),
        country = BlockPet.Postcard.DeviceRegion.CountryCode(),
        at = ServerNowMs,
    };

    // ─── 抽好友 ────────────────────────────────────────────────────

    /// <summary>
    /// 抽一個營地：先找得到可以抽的才扣次數／金幣（沒抽到不算）。done(營地, 錯誤)；錯誤是要顯示的字。主執行緒。
    /// </summary>
    public void DrawFriend(Action<CampProfile, string> done)
    {
        string me = _roomId;
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(me)) { done?.Invoke(null, "還沒登入或沒有進空間"); return; }
        if (PostcardRules.IsFull(_campFriends.Count, _pcMeta.unlocks, PostcardConfig)) { done?.Invoke(null, "full"); return; }
        string myTop = MyTopCategory();
        var exclude = new HashSet<string>(_blocked);
        exclude.UnionWith(_blockedBy);
        foreach (var f in _campFriends) exclude.Add(f.roomId);
        foreach (var m in _matchOut) exclude.Add(m.roomId);
        foreach (var m in _matchIn) exclude.Add(m.roomId);

        // 最近活動的營地（最多 200 個）＋同類別的營地；在手機上挑
        var recent = dbRef.Child(CampProfilesNode).OrderByChild("at").LimitToLast(200).GetValueAsync();
        recent.ContinueWith(t =>
        {
            var list = new List<CampProfile>();
            if (t.IsCompleted && !t.IsFaulted && t.Result != null)
                foreach (var c in t.Result.Children)
                {
                    try { var p = JsonUtility.FromJson<CampProfile>(c.GetRawJsonValue()); if (p != null) { p.roomId = c.Key; list.Add(p); } } catch { }
                }
            else Debug.LogWarning("[FirebaseManager] 讀營地卡失敗（Firebase 規則要允許讀 CampProfiles、.indexOn at）：" + t.Exception?.GetBaseException()?.Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
            {
                if (me != _roomId) return;
                var pick = PostcardRules.Pick(list, me, myTop, exclude, new System.Random());
                if (pick == null) { done?.Invoke(null, "現在沒有可以抽的營地，晚點再試"); return; }
                int cost = DrawCostNow;
                Action consume = () => CountDraw(me, () => done?.Invoke(pick, null));
                if (cost <= 0) consume();
                else SpendRoomCoins(cost, "抽好友", ok => { if (ok) consume(); else done?.Invoke(null, "營地金幣不夠"); });
            });
        });
    }

    /// <summary>今天抽的次數 +1（換日就從 1 開始）。</summary>
    void CountDraw(string me, Action then)
    {
        string today = PostcardRules.DayKey(ServerNowMs);
        dbRef.Child("Rooms").Child(me).Child("postcardMeta").RunTransaction(d =>
        {
            var dict = d.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
            string day = dict.TryGetValue("drawDay", out var dv) ? dv?.ToString() : "";
            long draws = dict.TryGetValue("draws", out var nv) ? ReadLongObj(nv) : 0;
            dict["draws"] = (day == today ? draws : 0) + 1;
            dict["drawDay"] = today;
            d.Value = dict;
            return TransactionResult.Success(d);
        }).ContinueWith(t => { lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => then?.Invoke()); });
    }

    /// <summary>按「加好友」：對方已經也按過（我們的 matchIn 有它）就直接成為好友；否則留在對方的 matchIn 等確認。done(錯誤) 主執行緒。</summary>
    public void SendMatch(CampProfile target, Action<string> done)
    {
        string me = _roomId;
        if (dbRef == null || target == null || string.IsNullOrEmpty(me)) { done?.Invoke("還沒登入或沒有進空間"); return; }
        var already = _matchIn.Find(m => m.roomId == target.roomId);
        if (already != null) { AcceptMatch(already, done); return; }
        GetRoomMembers(me, members =>
        {
            var mine = MyProfile(members != null ? members.Count : 1);
            long now = ServerNowMs;
            var toThem = new MatchRequest { name = mine.name, pet = mine.pet, breed = mine.breed, top = mine.top, country = mine.country, members = mine.members, at = now };
            var mineOut = new MatchRequest { name = target.name, pet = target.pet, breed = target.breed, top = target.top, country = target.country, members = target.members, at = now };
            var updates = new Dictionary<string, object>
            {
                [$"Rooms/{me}/matchOut/{target.roomId}"] = ToDict(mineOut),
                [$"Rooms/{target.roomId}/matchIn/{me}"] = ToDict(toThem),
            };
            dbRef.UpdateChildrenAsync(updates).ContinueWith(t =>
            {
                bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
                if (!ok) Debug.LogWarning("[FirebaseManager] 送出加好友失敗（Firebase 規則要允許 matchIn／matchOut）：" + t.Exception?.GetBaseException()?.Message);
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok ? null : "送出失敗，請再試一次"));
            });
        });
    }

    static Dictionary<string, object> ToDict(MatchRequest m) => new Dictionary<string, object>
    {
        { "name", m.name ?? "" }, { "pet", m.pet ?? "" }, { "breed", m.breed ?? "" }, { "top", m.top ?? "" },
        { "country", m.country ?? "" }, { "members", (long)m.members }, { "at", m.at },
    };

    /// <summary>對方想加我們、我們也按了「加好友」：兩邊寫好友（我們分格子，對方 -1 自己分），清掉兩邊的等待。</summary>
    public void AcceptMatch(MatchRequest r, Action<string> done)
    {
        string me = _roomId;
        if (dbRef == null || r == null || string.IsNullOrEmpty(me)) { done?.Invoke("還沒登入或沒有進空間"); return; }
        if (PostcardRules.IsFull(_campFriends.Count, _pcMeta.unlocks, PostcardConfig)) { done?.Invoke("full"); return; }
        var used = new List<int>();
        foreach (var f in _campFriends) used.Add(f.slot);
        int slot = CampFriendRules.NextFreeSlot(used, FriendCapacity);
        long now = ServerNowMs;
        var updates = new Dictionary<string, object>
        {
            [$"Rooms/{me}/friends/{r.roomId}"] = new Dictionary<string, object> { { "slot", (long)slot }, { "since", now }, { "name", r.name ?? "" }, { "trips", 0L } },
            [$"Rooms/{r.roomId}/friends/{me}"] = new Dictionary<string, object> { { "slot", -1L }, { "since", now }, { "name", CurrentRoomName ?? me }, { "trips", 0L } },
            [$"Rooms/{me}/matchIn/{r.roomId}"] = null,
            [$"Rooms/{me}/matchOut/{r.roomId}"] = null,
            [$"Rooms/{r.roomId}/matchOut/{me}"] = null,
            [$"Rooms/{r.roomId}/matchIn/{me}"] = null,
        };
        dbRef.UpdateChildrenAsync(updates).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            if (!ok) Debug.LogWarning("[FirebaseManager] 成為好友失敗（Firebase 規則要允許 friends）：" + t.Exception?.GetBaseException()?.Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok ? null : "加好友失敗，請再試一次"));
        });
    }

    /// <summary>略過對方的邀請（不通知對方）。</summary>
    public void SkipMatch(MatchRequest r)
    {
        if (dbRef == null || r == null || string.IsNullOrEmpty(_roomId)) return;
        RoomRoot().Child("matchIn").Child(r.roomId).RemoveValueAsync();
    }

    /// <summary>好友位置解鎖：扣金幣後解鎖次數 +1。done(錯誤) 主執行緒。</summary>
    public void UnlockFriendSlot(Action<string> done)
    {
        string me = _roomId;
        if (dbRef == null || string.IsNullOrEmpty(me)) { done?.Invoke("還沒登入或沒有進空間"); return; }
        SpendRoomCoins(PostcardConfig.unlockPrice, "好友位置解鎖", ok =>
        {
            if (!ok) { done?.Invoke("營地金幣不夠"); return; }
            dbRef.Child("Rooms").Child(me).Child("postcardMeta").Child("unlocks").RunTransaction(d =>
            {
                d.Value = ReadLongObj(d.Value) + 1;
                return TransactionResult.Success(d);
            }).ContinueWith(t => { lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(null)); });
        });
    }

    // ─── 封鎖、檢舉 ────────────────────────────────────────────────

    /// <summary>封鎖：刪掉兩邊的好友（地圖那塊地消失）、清掉等待中的加好友，以後不會再配到（兩邊都排除）。</summary>
    public void BlockCamp(string other, Action<bool> done = null)
    {
        string me = _roomId;
        if (dbRef == null || string.IsNullOrEmpty(me) || string.IsNullOrEmpty(other)) { done?.Invoke(false); return; }
        long now = ServerNowMs;
        var updates = new Dictionary<string, object>
        {
            [$"Rooms/{me}/blocked/{other}"] = now,
            [$"Rooms/{me}/friends/{other}"] = null,
            [$"Rooms/{me}/matchIn/{other}"] = null,
            [$"Rooms/{me}/matchOut/{other}"] = null,
        };
        if (!other.StartsWith("DEBUG_"))
        {
            updates[$"Rooms/{other}/blockedBy/{me}"] = now;
            updates[$"Rooms/{other}/friends/{me}"] = null;
            updates[$"Rooms/{other}/matchIn/{me}"] = null;
            updates[$"Rooms/{other}/matchOut/{me}"] = null;
        }
        dbRef.UpdateChildrenAsync(updates).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            if (!ok) Debug.LogWarning("[FirebaseManager] 封鎖失敗：" + t.Exception?.GetBaseException()?.Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    /// <summary>檢舉：把對方營地（和明信片內容，有的話）存進 Reports 等審核（審核方式待定）。</summary>
    public void ReportCamp(string other, string otherName, string postcardId, string where, Action<bool> done = null)
    {
        string me = _roomId;
        if (dbRef == null || string.IsNullOrEmpty(me) || string.IsNullOrEmpty(other)) { done?.Invoke(false); return; }
        Action<string> write = img =>
        {
            var rec = new Dictionary<string, object>
            {
                { "fromRoom", me }, { "uid", GetUserId() ?? "" }, { "target", other }, { "targetName", otherName ?? "" },
                { "postcardId", postcardId ?? "" }, { "where", where ?? "" }, { "at", ServerNowMs }, { "status", "new" },
            };
            if (!string.IsNullOrEmpty(img)) rec["img"] = img;
            dbRef.Child(ReportsNode).Push().SetValueAsync(rec).ContinueWith(t =>
            {
                bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
                if (!ok) Debug.LogWarning("[FirebaseManager] 檢舉送出失敗（Firebase 規則要允許寫 Reports）：" + t.Exception?.GetBaseException()?.Message);
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
            });
        };
        if (string.IsNullOrEmpty(postcardId)) { write(null); return; }
        RoomRoot().Child("postcardImg").Child(postcardId).GetValueAsync().ContinueWith(t =>
        {
            string img = t.IsCompleted && !t.IsFaulted && t.Result != null && t.Result.Exists ? t.Result.Value?.ToString() : null;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => write(img));
        });
    }

    // ─── 寄明信片、寵物帶回的風景明信片 ────────────────────────────────

    /// <summary>
    /// 寄一張明信片：派寵物出門（路程照地圖），寵物送到時對方才看得到。jpg＝寄出當下那一版拍立得（之後營地再改也不會變）。
    /// done(錯誤) 主執行緒，成功是 null。
    /// </summary>
    public void SendPostcard(CampFriend friend, int oneWayMin, byte[] jpg, string caption, Action<string> done)
    {
        string me = _roomId;
        if (dbRef == null || friend == null || jpg == null || jpg.Length == 0) { done?.Invoke("明信片還沒做好"); return; }
        SendPetOnTrip(friend, oneWayMin, done, trip =>
        {
            string id = dbRef.Child("Rooms").Child(me).Child("sent").Push().Key;
            long arrive = TripRules.ArriveAt(trip.departAt, trip.oneWayMin);
            var card = new Postcard
            {
                from = me, fromName = CurrentRoomName ?? "營地", fromPet = trip.petName, kind = PostcardKindPhoto, caption = caption ?? "",
                at = trip.departAt, visibleAt = arrive, arriveAt = arrive, homeAt = trip.HomeAt,
            };
            var sent = new SentPostcard { to = friend.roomId, toName = friend.name, toSlot = friend.slot, arriveAt = arrive, homeAt = trip.HomeAt, replied = "" };
            var updates = new Dictionary<string, object> { [$"Rooms/{me}/sent/{id}"] = SentToDict(sent) };
            if (!friend.roomId.StartsWith("DEBUG_"))
            {
                updates[$"Rooms/{friend.roomId}/postcards/{id}"] = PostcardToDict(card);
                updates[$"Rooms/{friend.roomId}/postcardImg/{id}"] = Convert.ToBase64String(jpg);
            }
            dbRef.UpdateChildrenAsync(updates).ContinueWith(t =>
            {
                if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] 寄明信片失敗（Firebase 規則要允許 postcards／postcardImg／sent）：" + t.Exception?.GetBaseException()?.Message);
            });
        });
    }

    bool _checkingScenic;

    /// <summary>
    /// 寵物寄完明信片回到家：帶回一張對方營地的風景明信片當紀念（render＝畫風景明信片的 JPG；不用對方的私人照片）。
    /// 交易佔住 sent.replied＝scenery，只會做一次。首頁定時呼叫。
    /// </summary>
    public void CheckScenicPostcards(Func<SentPostcard, byte[]> render)
    {
        if (_checkingScenic || dbRef == null || string.IsNullOrEmpty(_roomId) || render == null) return;
        long now = ServerNowMs;
        var due = _sent.Find(s => PostcardRules.SceneryDue(s.homeAt, now, !string.IsNullOrEmpty(s.replied)));
        if (due == null) return;
        _checkingScenic = true;
        string me = _roomId;
        bool claimed = false;
        RoomRoot().Child("sent").Child(due.id).Child("replied").RunTransaction(d =>
        {
            string cur = d.Value?.ToString() ?? "";
            claimed = string.IsNullOrEmpty(cur);
            if (!claimed) return TransactionResult.Abort();
            d.Value = "scenery";
            return TransactionResult.Success(d);
        }).ContinueWith(t =>
        {
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
            {
                _checkingScenic = false;
                if (t.IsFaulted || !claimed || me != _roomId) return;
                byte[] jpg = null;
                try { jpg = render(due); } catch (Exception ex) { Debug.LogWarning("[FirebaseManager] 畫風景明信片失敗：" + ex.Message); }
                if (jpg == null) return;
                string id = RoomRoot().Child("postcards").Push().Key;
                var card = new Postcard
                {
                    from = due.to, fromName = due.toName, fromSlot = due.toSlot, kind = PostcardKindScenery, caption = $"{due.toName}的風景",
                    at = ServerNowMs, visibleAt = ServerNowMs, replyTo = due.id, replied = true,
                };
                var updates = new Dictionary<string, object>
                {
                    [$"Rooms/{me}/postcards/{id}"] = PostcardToDict(card),
                    [$"Rooms/{me}/postcardImg/{id}"] = Convert.ToBase64String(jpg),
                };
                dbRef.UpdateChildrenAsync(updates);
            });
        });
    }

    static Dictionary<string, object> PostcardToDict(Postcard p) => new Dictionary<string, object>
    {
        { "from", p.from ?? "" }, { "fromName", p.fromName ?? "" }, { "fromPet", p.fromPet ?? "" }, { "fromSlot", (long)p.fromSlot },
        { "kind", p.kind ?? PostcardKindPhoto }, { "caption", p.caption ?? "" }, { "at", p.at }, { "visibleAt", p.visibleAt },
        { "arriveAt", p.arriveAt }, { "homeAt", p.homeAt }, { "replied", p.replied }, { "replyTo", p.replyTo ?? "" },
    };

    static Dictionary<string, object> SentToDict(SentPostcard s) => new Dictionary<string, object>
    {
        { "to", s.to ?? "" }, { "toName", s.toName ?? "" }, { "toSlot", (long)s.toSlot },
        { "arriveAt", s.arriveAt }, { "homeAt", s.homeAt }, { "replied", s.replied ?? "" },
    };

    /// <summary>讀一張收到的明信片的圖（快取在記憶體；只能讀自己營地收到的）。callback 主執行緒，讀不到是 null。</summary>
    public void LoadPostcardImage(string id, Action<Texture2D> done)
    {
        if (string.IsNullOrEmpty(id) || dbRef == null || string.IsNullOrEmpty(_roomId)) { done?.Invoke(null); return; }
        if (_postcardImages.TryGetValue(id, out var cached) && cached != null) { done?.Invoke(cached); return; }
        string room = _roomId;
        RoomRoot().Child("postcardImg").Child(id).GetValueAsync().ContinueWith(t =>
        {
            string b64 = t.IsCompleted && !t.IsFaulted && t.Result != null && t.Result.Exists ? t.Result.Value?.ToString() : null;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
            {
                if (room != _roomId || string.IsNullOrEmpty(b64)) { done?.Invoke(null); return; }
                Texture2D tex = null;
                try
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (!tex.LoadImage(Convert.FromBase64String(b64))) { UnityEngine.Object.Destroy(tex); tex = null; }
                }
                catch { tex = null; }
                if (tex != null) _postcardImages[id] = tex;
                done?.Invoke(tex);
            });
        });
    }

    /// <summary>讀明信片原始 JPG（下載用）。</summary>
    public void LoadPostcardJpg(string id, Action<byte[]> done)
    {
        if (string.IsNullOrEmpty(id) || dbRef == null || string.IsNullOrEmpty(_roomId)) { done?.Invoke(null); return; }
        RoomRoot().Child("postcardImg").Child(id).GetValueAsync().ContinueWith(t =>
        {
            byte[] bytes = null;
            try { if (t.IsCompleted && !t.IsFaulted && t.Result != null && t.Result.Exists) bytes = Convert.FromBase64String(t.Result.Value.ToString()); } catch { }
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(bytes));
        });
    }

    // ─── 測試用（BlockPet/Debug/明信片）────────────────────────────

    /// <summary>（測試用）假裝有營地想跟我們當好友（id 以 DEBUG_ 開頭）。</summary>
    public void DebugMatchIn()
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId)) return;
        var r = new MatchRequest { name = "（測試）海邊小屋", pet = "小黑", breed = DefaultBreed, top = MyTopCategory(), country = "JP", members = 3, at = ServerNowMs };
        RoomRoot().Child("matchIn").Child("DEBUG_MATCH_" + UnityEngine.Random.Range(100, 999)).SetValueAsync(ToDict(r));
    }

    /// <summary>（測試用）假裝好友寄來一張明信片（kind photo），或寵物帶回一張風景明信片（kind scenery）；馬上看得到。</summary>
    public void DebugPostcardIn(byte[] jpg, string kind)
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId) || jpg == null) return;
        long now = ServerNowMs;
        string from = _campFriends.Count > 0 ? _campFriends[0].roomId : "DEBUG_FRIEND_0";
        string name = _campFriends.Count > 0 ? _campFriends[0].name : "（測試）營地";
        string id = RoomRoot().Child("postcards").Push().Key;
        var card = new Postcard
        {
            from = from, fromName = name, fromPet = "小黑", fromSlot = _campFriends.Count > 0 ? _campFriends[0].slot : 0, kind = kind,
            caption = kind == PostcardKindScenery ? $"{name}的風景" : "測試明信片", at = now, visibleAt = now, arriveAt = now, homeAt = now,
        };
        var updates = new Dictionary<string, object>
        {
            [$"Rooms/{_roomId}/postcards/{id}"] = PostcardToDict(card),
            [$"Rooms/{_roomId}/postcardImg/{id}"] = Convert.ToBase64String(jpg),
        };
        dbRef.UpdateChildrenAsync(updates);
    }
}
