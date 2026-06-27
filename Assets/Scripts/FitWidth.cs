using UnityEngine;

public class FitWidth : MonoBehaviour
{
    void Start() => Fit();

    void Fit()
    {
        var cam = Camera.main;
        if (cam == null) return;

        var sr = GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        float screenH = cam.orthographicSize * 2f;
        float screenW = screenH * cam.aspect;

        float spriteW = sr.sprite.bounds.size.x;
        float spriteH = sr.sprite.bounds.size.y;

        // Use the larger scale so the sprite covers the full screen on both axes
        float scale = Mathf.Max(screenW / spriteW, screenH / spriteH);

        transform.localScale = new Vector3(scale, scale, 1f);
    }
}
