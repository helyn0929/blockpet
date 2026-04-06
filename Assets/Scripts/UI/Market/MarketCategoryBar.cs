using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Row of <see cref="MarketCategoryTab"/> buttons; notifies <see cref="MarketPageController"/> when the category changes.
/// </summary>
public class MarketCategoryBar : MonoBehaviour
{
    [SerializeField] MarketCategoryTab[] tabs;
    [SerializeField] MarketCategory defaultCategory = MarketCategory.Pets;

    MarketPageController _page;
    bool _wired;

    public void Initialize(MarketPageController page)
    {
        _page = page;
        if (_wired)
            return;
        _wired = true;

        if (tabs == null)
            return;

        foreach (MarketCategoryTab tab in tabs)
        {
            if (tab == null) continue;
            Button b = tab.GetComponent<Button>();
            if (b == null)
                b = tab.gameObject.AddComponent<Button>();
            MarketCategoryTab captured = tab;
            b.onClick.AddListener(() => Select(captured.Category));
        }

        Select(defaultCategory);
    }

    void Select(MarketCategory cat)
    {
        if (tabs != null)
        {
            foreach (MarketCategoryTab t in tabs)
            {
                if (t != null)
                    t.SetSelected(t.Category == cat);
            }
        }

        _page?.OnCategorySelected(cat);
    }
}
