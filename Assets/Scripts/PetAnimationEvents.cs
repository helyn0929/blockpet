using System.Collections;
using UnityEngine;

public class PetAnimationEvents : MonoBehaviour
{
    [Header("VFX")]
    public ParticleSystem hearts;

    [Header("Photo fade settings")]
    public float photoFadeTime = 0.15f;
    public float photoShrinkMultiplier = 0.2f;

    [Header("Find FlyingPhoto (recommended path first)")]
    public string vfxRootName = "VFX_PhotoFly";         // in your Hierarchy
    public string flyingPhotoName = "FlyingPhoto";      // child under VFX_PhotoFly

    [Header("Debug / Test")]
    public bool enableTestKey = true;
    public KeyCode testKey = KeyCode.T;                 // press T to simulate bite
    public string expectedEventString = "bite";         // Animation Event String

    private SpriteRenderer activePhoto;
    private Coroutine running;

    void Start()
    {
        Debug.LogWarning($"[PetAnimationEvents] START on: {GetPath(transform)}");
    }

    void Update()
    {
        // Manual test without relying on engineer flow / animation event
        if (enableTestKey && Input.GetKeyDown(testKey))
        {
            Debug.LogWarning("[PetAnimationEvents] TEST KEY pressed -> simulate bite");
            OnEatBite(expectedEventString);
        }
    }

    // Animation Event calls this: OnEatBite(string)
    public void OnEatBite(string note)
    {
        Debug.LogWarning($"[PetAnimationEvents] OnEatBite called. note='{note}' on {GetPath(transform)}");

        if (!string.Equals(note, expectedEventString))
        {
            Debug.LogWarning($"[PetAnimationEvents] note != '{expectedEventString}', ignored.");
            return;
        }

        // 1) hearts
        if (hearts != null)
        {
            hearts.Play();
            Debug.LogWarning("[PetAnimationEvents] hearts.Play()");
        }
        else
        {
            Debug.LogWarning("[PetAnimationEvents] hearts is NULL (ok if you haven't assigned)");
        }

        // 2) Find the FlyingPhoto SR
        activePhoto = FindFlyingPhotoSpriteRenderer();

        if (activePhoto == null)
        {
            Debug.LogWarning("[PetAnimationEvents] FlyingPhoto NOT found.");
            return;
        }

        Debug.LogWarning($"[PetAnimationEvents] FlyingPhoto FOUND: name='{activePhoto.gameObject.name}' path='{GetPath(activePhoto.transform)}' active={activePhoto.gameObject.activeInHierarchy}");

        // Stop any previous fade coroutine
        if (running != null) StopCoroutine(running);

        // IMPORTANT: some engineer scripts may keep writing scale/alpha every frame.
        // To guarantee "photo disappears" first, we disable it immediately.
        // If you later want smooth fade, comment this out after we confirm the correct object is targeted.
        // ----
        // Quick verify:
        //activePhoto.gameObject.SetActive(false);
        //Debug.LogWarning("[PetAnimationEvents] FlyingPhoto SetActive(false) (quick verify mode)");
        //return;
        // ----

        // Smooth fade & shrink, then disable
        running = StartCoroutine(FadeAndShrinkThenDisable(activePhoto, photoFadeTime, photoShrinkMultiplier));
    }

    SpriteRenderer FindFlyingPhotoSpriteRenderer()
    {
        // A) Preferred: find "VFX_PhotoFly/FlyingPhoto" under scene root
        var vfx = GameObject.Find(vfxRootName);
        if (vfx != null)
        {
            var t = vfx.transform.Find(flyingPhotoName);
            if (t != null)
            {
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr != null) return sr;

                // maybe on children
                sr = t.GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null) return sr;
            }
        }

        // B) Fallback: scan any SpriteRenderer whose name contains "FlyingPhoto"
        var srs = Object.FindObjectsOfType<SpriteRenderer>(true);
        foreach (var sr in srs)
        {
            if (sr != null && sr.gameObject.name.Contains(flyingPhotoName))
                return sr;
        }

        return null;
    }

    IEnumerator FadeAndShrinkThenDisable(SpriteRenderer sr, float t, float endScaleMul)
    {
        if (sr == null) yield break;

        Transform tr = sr.transform;

        // cache
        Vector3 startScale = tr.localScale;
        Vector3 endScale = startScale * endScaleMul;

        Color c = sr.color;
        float startA = c.a;

        Debug.LogWarning($"[PetAnimationEvents] Fade start. scale={startScale}, alpha={startA}, time={t}");

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

        // final
        if (sr != null)
        {
            c.a = 0f;
            sr.color = c;
            tr.localScale = endScale;

            sr.gameObject.SetActive(false);
            Debug.LogWarning("[PetAnimationEvents] Fade done -> FlyingPhoto disabled");
        }

        running = null;
        activePhoto = null;
    }

    string GetPath(Transform t)
    {
        if (t == null) return "(null)";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
