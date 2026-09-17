using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BlockPet.Decoration
{
    /// <summary>
    /// Coordinates every placed fermentation jar: uploading a photo into an empty jar,
    /// counting down its 10-minute digestion, sending the shared room pet to eat it once
    /// ready, and finally handing the photo over to SaveManager so it lands in the album.
    ///
    /// Placement/position of the jar itself is handled entirely by the existing decoration
    /// system (DecorationManager + Firebase, synced across the room). The digesting photo
    /// and its timer are local-only (per device) — see FermentationJarState.
    /// </summary>
    public class FermentationJarManager : MonoBehaviour
    {
        public static FermentationJarManager Instance;

        /// <summary>Must match the itemId of the DecorationItem asset created for the fermentation jar.</summary>
        public const string JarItemId = "fermentation_jar";

        [Header("Timing")]
        [Tooltip("How long a photo takes to digest before the pet goes to eat it.")]
        [SerializeField] float digestSeconds = 600f; // 10 minutes
        [Tooltip("Delay after the pet arrives (Eat animation playing) before the photo is moved into the album.")]
        [SerializeField] float eatAnimSeconds = 1.5f;

        readonly Dictionary<string, FermentationJarObject> _jars = new Dictionary<string, FermentationJarObject>();
        bool _subscribed;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            SaveManager.OnRoomSwitched += HandleRoomSwitched;
            TrySubscribeToDecorationEvents();
        }

        void OnDestroy()
        {
            SaveManager.OnRoomSwitched -= HandleRoomSwitched;
            if (DecorationManager.Instance != null)
            {
                DecorationManager.Instance.OnDecorationSpawned -= HandleSpawned;
                DecorationManager.Instance.OnDecorationRemoved -= HandleRemoved;
            }
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_subscribed) TrySubscribeToDecorationEvents();
            TickJars();
        }

        void TrySubscribeToDecorationEvents()
        {
            if (DecorationManager.Instance == null) return;
            DecorationManager.Instance.OnDecorationSpawned += HandleSpawned;
            DecorationManager.Instance.OnDecorationRemoved += HandleRemoved;
            _subscribed = true;
        }

        void HandleRoomSwitched()
        {
            // Decoration GameObjects for the previous room may still be mid-teardown;
            // drop our references so we don't act on stale jars.
            _jars.Clear();
        }

        // ─── Decoration spawn/remove hooks ─────────────────────────────

        void HandleSpawned(DecorationObject obj, DecorationItem item)
        {
            if (item == null || item.itemId != JarItemId || obj == null) return;

            var jarComp = obj.GetComponent<FermentationJarObject>();
            if (jarComp == null) jarComp = obj.gameObject.AddComponent<FermentationJarObject>();
            jarComp.Configure(item, this);
            _jars[obj.DecorId] = jarComp;

            // If this jar was already digesting (e.g. app relaunched, or another player placed
            // it earlier this session), reflect that immediately instead of waiting a frame.
            RefreshVisual(obj.DecorId);
        }

        void HandleRemoved(string decorId)
        {
            _jars.Remove(decorId);

            var data = SaveManager.Instance?.data;
            var state = data?.fermentationJars?.Find(s => s.decorId == decorId);
            if (state == null) return;

            DeletePendingFile(state.pendingPhotoFileName);
            data.fermentationJars.Remove(state);
            SaveManager.Instance.Save();
        }

        // ─── Upload flow (tap an empty jar) ────────────────────────────

        /// <summary>Called by FermentationJarObject when the player taps an empty jar.</summary>
        public void RequestUploadPhoto(string decorId, Vector3 jarWorldPos)
        {
            var data = SaveManager.Instance?.data;
            if (data == null || string.IsNullOrEmpty(decorId)) return;
            if (data.fermentationJars.Exists(s => s.decorId == decorId)) return; // already digesting

#if (UNITY_IOS || UNITY_ANDROID) && NATIVE_GALLERY
            NativeGallery.GetImageFromGallery(path =>
            {
                if (string.IsNullOrEmpty(path))
                {
                    Debug.Log("[FermentationJarManager] Photo pick cancelled.");
                    return;
                }
                StartCoroutine(LoadAndStartDigestion(decorId, path));
            }, "選擇要發酵的照片");
#else
            Debug.LogWarning("[FermentationJarManager] Uploading requires the NativeGallery plugin and the NATIVE_GALLERY scripting define (iOS/Android only).");
#endif
        }

        IEnumerator LoadAndStartDigestion(string decorId, string path)
        {
            yield return null; // let the native picker UI close first

            Texture2D tex = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var loaded = new Texture2D(2, 2);
                if (loaded.LoadImage(bytes)) tex = loaded;
                else Debug.LogError("[FermentationJarManager] Failed to decode picked image.");
            }
            catch (Exception e)
            {
                Debug.LogError("[FermentationJarManager] LoadAndStartDigestion failed: " + e.Message);
            }

            if (tex == null) yield break;

            string fileName = decorId + ".png"; // one pending photo per jar, keyed by decorId
            File.WriteAllBytes(Path.Combine(PendingFolder, fileName), tex.EncodeToPNG());
            Destroy(tex);

            var data = SaveManager.Instance.data;
            data.fermentationJars.Add(new FermentationJarState(decorId, fileName));
            SaveManager.Instance.Save();

            RefreshVisual(decorId);
        }

        // ─── Digestion countdown + pet dispatch ────────────────────────

        void TickJars()
        {
            var data = SaveManager.Instance?.data;
            if (data?.fermentationJars == null || data.fermentationJars.Count == 0) return;

            string readyDecorId = null;

            // Snapshot the count — FinishDigestion (later) mutates this list, but that
            // happens in a deferred coroutine, so the list is stable for the duration of this loop.
            for (int i = 0; i < data.fermentationJars.Count; i++)
            {
                var state = data.fermentationJars[i];
                if (state == null || string.IsNullOrEmpty(state.decorId)) continue;
                if (!_jars.TryGetValue(state.decorId, out var jarObj)) continue; // not spawned/registered yet

                float remaining = GetRemainingSeconds(state);
                if (remaining > 0f)
                {
                    jarObj.SetDigesting(remaining);
                }
                else
                {
                    jarObj.SetReady();
                    if (readyDecorId == null) readyDecorId = state.decorId;
                }
            }

            if (readyDecorId != null && AIController.Instance != null && !AIController.Instance.IsBusy &&
                _jars.TryGetValue(readyDecorId, out var readyJar))
            {
                string decorId = readyDecorId;
                AIController.Instance.GoTo(readyJar.transform.position, () => OnPetArrived(decorId));
            }
        }

        float GetRemainingSeconds(FermentationJarState state)
        {
            if (!DateTime.TryParse(state.digestStartTime, out DateTime start)) return 0f;
            return digestSeconds - (float)(DateTime.Now - start).TotalSeconds;
        }

        void RefreshVisual(string decorId)
        {
            var data = SaveManager.Instance?.data;
            var state = data?.fermentationJars?.Find(s => s.decorId == decorId);
            if (!_jars.TryGetValue(decorId, out var jarObj)) return;

            if (state == null) jarObj.SetEmpty();
            else
            {
                float remaining = GetRemainingSeconds(state);
                if (remaining > 0f) jarObj.SetDigesting(remaining);
                else jarObj.SetReady();
            }
        }

        // ─── Pet eats, photo goes to the album ─────────────────────────

        void OnPetArrived(string decorId)
        {
            var anim = AIController.Instance != null ? AIController.Instance.GetComponentInChildren<Animator>() : null;
            if (anim != null) anim.SetTrigger("Eat");
            StartCoroutine(FinishAfterEatAnim(decorId));
        }

        IEnumerator FinishAfterEatAnim(string decorId)
        {
            yield return new WaitForSeconds(eatAnimSeconds);

            var data = SaveManager.Instance?.data;
            var state = data?.fermentationJars?.Find(s => s.decorId == decorId);
            if (state != null)
            {
                string fullPath = Path.Combine(PendingFolder, state.pendingPhotoFileName);
                if (File.Exists(fullPath))
                {
                    byte[] bytes = File.ReadAllBytes(fullPath);
                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(bytes))
                        SaveManager.Instance.SavePhoto(tex); // → fires OnPhotoSaved, album UI updates itself
                    Destroy(tex);
                    File.Delete(fullPath);
                }
                data.fermentationJars.Remove(state);
                SaveManager.Instance.Save();

                if (_jars.TryGetValue(decorId, out var jarObj)) jarObj.SetEmpty();
                FindObjectOfType<PetHealthManager>()?.AddHealth();
            }
            // state == null means the jar was deleted while the pet was walking over —
            // nothing to eat, so skip the photo save and health bonus.

            AIController.Instance?.ReleaseBusy();
        }

        // ─── Pending photo storage ──────────────────────────────────────

        string PendingFolder
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "ferment_pending");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        void DeletePendingFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            string path = Path.Combine(PendingFolder, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
