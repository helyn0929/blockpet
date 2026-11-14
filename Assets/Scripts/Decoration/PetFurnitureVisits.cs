using System.Collections;
using System.Collections.Generic;
using BlockPet.Feed;
using UnityEngine;

namespace BlockPet.Decoration
{
    /// <summary>
    /// 寵物走去喜歡的家具（設計文件 5.18 ⑤，2026-11-14）：每次閒晃結束時，照 Resources/Pets/furniture_likes.json 的機率，
    /// 從「有擺出來」的喜歡家具裡挑一件走過去，停一下做那件的互動（動畫待美術：先用泡泡「♥」看、「Zz…」睡、「♪」坐）。
    /// 喜歡哪些：進化後依主色、進化前隨機 2 件（出生時決定，同一隻寵物不會變）。掉心速度不變。
    /// 佈置中、出牌中、寵物出門（看不到）時不去。由 EvolutionFlow 掛到寵物上。
    /// </summary>
    [RequireComponent(typeof(AIController))]
    public class PetFurnitureVisits : MonoBehaviour
    {
        static FurnitureLikeTable _table;
        const float ArriveTimeout = 15f;
        const float FrontOffset = 0.3f; // 站在家具前面一點（往下＝往前）

        AIController _ai;
        SpriteRenderer _sr;
        readonly Dictionary<string, float> _lastVisit = new Dictionary<string, float>();
        readonly System.Random _rnd = new System.Random();
        bool _visiting;

        public static FurnitureLikeTable Table
        {
            get
            {
                if (_table != null) return _table;
                var ta = Resources.Load<TextAsset>("Pets/furniture_likes");
                try { _table = ta != null ? JsonUtility.FromJson<FurnitureLikeTable>(ta.text) : null; } catch { }
                return _table ??= new FurnitureLikeTable();
            }
        }

        /// <summary>目前這隻寵物喜歡的家具（還沒有品種系統：都算 bowbow）。</summary>
        public static List<LikeEntry> CurrentLikes()
        {
            var fb = FirebaseManager.Instance;
            var rec = fb != null ? fb.CurrentEvolution : null;
            string main = rec != null ? PetPreferences.Categories[Mathf.Clamp(rec.main, 0, PetPreferences.Categories.Length - 1)] : null;
            int petIndex = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0;
            int seed = (int)StableHash((fb != null ? fb.RoomId : "") + "#" + petIndex);
            return FurnitureLikeRules.LikesFor(Table, FurnitureLikeRules.DefaultBreed, rec?.type, main, seed);
        }

        /// <summary>每台手機、每次啟動都一樣的雜湊（string.GetHashCode 每次啟動會變）。</summary>
        static uint StableHash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") { h ^= c; h *= 16777619; }
            return h;
        }

        void Awake()
        {
            _ai = GetComponent<AIController>();
            _sr = GetComponentInChildren<SpriteRenderer>();
            _ai.TryDirectedIdle = TryVisit;
        }

        void OnDestroy()
        {
            if (_ai != null && _ai.TryDirectedIdle == TryVisit) _ai.TryDirectedIdle = null;
        }

        bool TryVisit()
        {
            if (_visiting || BlockPet.UI.TopBar.DecorModeSettings.IsDecorating) return false;
            if (_sr != null && !_sr.enabled) return false; // 出門寄明信片：寵物藏起來了
            if (BlockPet.UI.Home.EvolutionFlow.Playing) return false;
            var hand = FindObjectOfType<BlockPet.Hand.HandController>();
            if (hand != null && hand.IsDealt) return false;
            var dm = DecorationManager.Instance;
            if (dm == null) return false;
            var pick = FurnitureLikeRules.PickVisit(CurrentLikes(), id => dm.CountOnCamp(id) > 0, _lastVisit, Time.time, Table, _rnd);
            if (pick == null) return false;
            DecorationObject target = null;
            foreach (var kv in dm.Spawned)
                if (kv.Value != null && kv.Value.HasGrid && kv.Value.ItemId == pick.item && kv.Value.gameObject.activeInHierarchy) { target = kv.Value; break; }
            if (target == null) return false;
            _visiting = true;
            StartCoroutine(Visit(pick, target, hand));
            return true;
        }

        IEnumerator Visit(LikeEntry like, DecorationObject obj, BlockPet.Hand.HandController hand)
        {
            yield return null; // 等 AIController 的閒晃結束，再接手
            var dm = DecorationManager.Instance;
            var item = dm != null ? dm.Database.GetById(obj.ItemId) : null;
            Vector3 spot = dm != null && dm.Grid != null && item != null
                ? DecorationLayout.FootWorld(item, obj.Anchor, dm.Grid)
                : obj.transform.position;
            spot += new Vector3(0f, -FrontOffset, 0f);
            bool arrived = false;
            if (!_ai.GoTo(spot, () => arrived = true)) { _visiting = false; yield break; }
            float until = Time.time + ArriveTimeout;
            while (!arrived && Time.time < until && obj != null) yield return null;
            if (!arrived || obj == null) { _ai.ReleaseBusy(); _visiting = false; yield break; }

            // 面向家具，做互動（暫用：泡泡＋小動作）
            if (_sr != null) _sr.flipX = obj.transform.position.x < transform.position.x;
            float stay = Mathf.Max(1f, Table.staySeconds);
            hand?.SayLike(like.action, stay);
            Vector3 scale = transform.localScale;
            if (like.action == "sleep") transform.localScale = scale * 0.9f;
            else PetHop.Play(gameObject, like.action == "sit" ? 1 : 2);
            yield return new WaitForSeconds(stay);
            transform.localScale = scale;
            _lastVisit[like.item] = Time.time;
            _ai.ReleaseBusy();
            _visiting = false;
        }
    }
}
