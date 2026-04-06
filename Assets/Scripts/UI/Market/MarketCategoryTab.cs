using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One category tab in the market. Assign <see cref="category"/> per instance in the Inspector.
/// </summary>
public class MarketCategoryTab : MonoBehaviour
{
    [SerializeField] MarketCategory category;
    [SerializeField] GameObject selectedHighlight;
    [Tooltip("Optional: tint when selected.")]
    [SerializeField] Graphic[] selectedTintTargets;
    [SerializeField] Color normalColor = Color.white;
    [SerializeField] Color selectedColor = new Color(1f, 0.92f, 0.8f, 1f);

    public MarketCategory Category => category;

    public void SetSelected(bool selected)
    {
        if (selectedHighlight != null)
            selectedHighlight.SetActive(selected);

        foreach (Graphic g in selectedTintTargets)
        {
            if (g != null)
                g.color = selected ? selectedColor : normalColor;
        }
    }
}
