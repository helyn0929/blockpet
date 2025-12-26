using System.Collections;
using UnityEngine;

public class PetAnimationEvents : MonoBehaviour
{
    [Header("VFX")]
    public ParticleSystem hearts;

    [Header("Photo fade settings")]
    public float photoFadeTime = 0.15f;
    public float photoShrinkMultiplier = 0.2f;

    [Header("Find FlyingPhoto (path first)")]
    public string vfxRootName = "VFX_PhotoFly";
    public string flyingPhotoName = "FlyingPhoto";

    private Coroutine running;

    // Animation Event calls this: OnEatBite(string)
    public void OnEatBite(string note)
    {
        // 不要擋掉，先確保「動畫事件有進來」
        Debug.LogWarning($"[PetAnimationEvents] OnEatBite called. note='{note}'");

        // hearts
        if (hearts != null) hearts.Play();

        // 找到照片
        var sr = FindFlyingPhotoSpriteRenderer();
        if (sr == null)
        {
            Debug.LogWarning("[PetAnimationEvents] FlyingPhoto NOT found.");
            return;
        }

        // 停止上一個 coroutine
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FadeAndShrinkThenDisable(sr, photoFadeTime, photoShrinkMultiplier));
    }

    private SpriteRenderer FindFlyingPhotoSpriteRenderer()
    {
        // A) VFX_PhotoFly/FlyingPhoto
        var vfx = GameObject.Find(vfxRootName);
        if (vfx != null)
        {
            var t = vfx.transform.Find(flyingPhotoName);
            if (t != null)
            {
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr != null) return sr;
                sr = t.GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null) return sr;
            }
        }

        // B) fallback: name contains FlyingPhoto
        var srs = Object.FindObjectsOfType<SpriteRenderer>(true);
        foreach (var sr in srs)
        {
            if (sr != null && sr.gameObject.name.Contains(flyingPhotoName))
                return sr;
        }

        return null;
    }

    private IEnumerator FadeAndShrinkThenDisable(SpriteRenderer sr, float t, float endScaleMul)
    {
        if (sr == null) yield break;

        Transform tr = sr.transform;

        Vector3 startScale = tr.localScale;
        Vector3 endScale = startScale * endScaleMul;

        Color c = sr.color;
        float startA = c.a;

        float time = 0f;
        while (time < t)
        {
            if (sr == null) yield break;

            time += Time.deltaTime;
            float k = Mathf.Clamp01(time / t);

            tr.localScale = Vector3.Lerp(startScale, endScale, k);
            c.a = Mathf.Lerp(startA, 0f, k);
            sr.color = c;

            yield return null;
        }

        c.a = 0f;
        sr.color = c;
        tr.localScale = endScale;

        sr.gameObject.SetActive(false);

        running = null;
    }
}
