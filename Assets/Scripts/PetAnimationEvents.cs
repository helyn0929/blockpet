using System.Collections;
using UnityEngine;

public class PetAnimationEvents : MonoBehaviour
{
    public ParticleSystem hearts;

    [Header("直接把 Hierarchy 裡的 FlyingPhoto 拖進來")]
    public SpriteRenderer flyingPhotoSR; 

    public void OnEatBite(string note)
    {
        if (hearts != null) hearts.Play();

        // 核心邏輯：只針對拖進來的 FlyingPhoto 進行淡出隱藏
        if (flyingPhotoSR != null)
        {
            StartCoroutine(FadeOutPhoto(flyingPhotoSR));
        }
    }

    IEnumerator FadeOutPhoto(SpriteRenderer sr)
    {
        float t = 0;
        float duration = 0.2f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = t / duration;
            // 漸變透明度
            sr.color = new Color(1, 1, 1, 1 - p);
            yield return null;
        }
        sr.gameObject.SetActive(false);
        // 重置顏色透明度以便下次使用
        sr.color = new Color(1, 1, 1, 1);
    }
}