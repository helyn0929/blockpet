using System.Collections.Generic;
using UnityEngine;

namespace BlockPet.Decoration
{
    [CreateAssetMenu(menuName = "BlockPet/Decoration Database")]
    public class DecorationDatabase : ScriptableObject
    {
        [SerializeField] List<DecorationItem> items = new List<DecorationItem>();

        Dictionary<string, DecorationItem> _lookup;

        void OnEnable() => BuildLookup();

        void BuildLookup()
        {
            _lookup = new Dictionary<string, DecorationItem>(items.Count);
            foreach (var item in items)
                if (item != null && !string.IsNullOrEmpty(item.itemId))
                    _lookup[item.itemId] = item;
        }

        public DecorationItem GetById(string itemId)
        {
            if (_lookup == null) BuildLookup();
            return _lookup.TryGetValue(itemId, out var item) ? item : null;
        }

        public IReadOnlyList<DecorationItem> GetAll() => items;

        public IReadOnlyList<DecorationItem> GetByCategory(DecorationCategory cat)
        {
            var result = new List<DecorationItem>();
            foreach (var item in items)
                if (item != null && item.category == cat) result.Add(item);
            return result;
        }
    }
}
