using UnityEngine;

public class FitWidth : MonoBehaviour
{
    void Start()
    {
        var cam = Camera.main;

        float screenHeight = cam.orthographicSize * 2;
        float screenWidth = screenHeight * cam.aspect;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();

        float spriteWidth = sr.sprite.bounds.size.x;

        float scale = screenWidth / spriteWidth;

        transform.localScale = new Vector3(scale, scale, 1);
    }
}