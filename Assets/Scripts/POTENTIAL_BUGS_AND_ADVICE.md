# Potential Bugs & Structure Advice (Blockpet)

## Critical / Likely Bugs

### 1. **AlbumUI – crash when `timestamp` is null or short**
- **Where:** `ReloadFromSave()` uses `p.timestamp.Substring(5, 5)` for every photo.
- **Risk:** If any `PhotoMeta` has `timestamp == null` or length < 10 (e.g. old save or bad data), `Substring` throws and the album screen can crash.
- **Fix:** Only group photos with valid timestamp (e.g. `timestamp != null && timestamp.Length >= 10`), or use a safe substring helper.

### 2. **PetHealthManager – NullReferenceException if SaveManager is missing or late**
- **Where:** `CalculateOfflineDecay()` starts with `SaveManager.Instance.data.lastUpdateTime` with no check on `Instance` or `data`. `AddHealth()` and `UpdateUI()` use `SaveManager.Instance.data` without null checks.
- **Risk:** If SaveManager is disabled, not in the scene, or runs after PetHealthManager, you get NRE.
- **Fix:** Guard all use: `if (SaveManager.Instance == null || SaveManager.Instance.data == null) return;` at the start of each method that touches them.

### 3. **FeedController – no check for SaveManager before save**
- **Where:** `FeedWithPhoto()` calls `SaveManager.Instance.SavePhoto(photo)` with no null check.
- **Risk:** If SaveManager isn’t ready (e.g. script order or missing from scene), NRE when opening camera/feeding.
- **Fix:** Add `if (SaveManager.Instance == null) { Debug.LogWarning(...); return; }` before `SavePhoto`.

### 4. **FirebaseManager – null message from Firebase**
- **Where:** `HandleChildAdded` does `JsonUtility.FromJson<ChatMessage>(json)` and then `DisplayMessage(msg)` without checking `msg` for null.
- **Risk:** Malformed or empty JSON can make `msg` null; `DisplayMessage` then accesses `msg.userName` and crashes.
- **Fix:** After `FromJson`, if `msg == null` skip calling `DisplayMessage` (and optionally log).

### 5. **ChatUIHandler – singleton and scene lifecycle**
- **Where:** `Instance = this` in Awake with no `DontDestroyOnLoad` and no “single instance” guard.
- **Risk:** Two scenes with ChatUIHandler overwrite `Instance`. If the object with ChatUIHandler is destroyed (e.g. panel closed/destroyed), `FirebaseManager` can still call `ChatUIHandler.Instance.DisplayMessage` and hit a destroyed object → NRE or missing UI.
- **Fix:** Either make ChatUIHandler a true singleton on a DontDestroyOnLoad object, or have FirebaseManager check `Instance != null` and that the GameObject is still active/destroyed before calling. Prefer clearing `Instance` in `OnDestroy()` when the handler is destroyed.

---

## Moderate / Edge Cases

### 6. **CameraController – WebCamTexture not stopped on disable**
- **Where:** No `OnDisable` / `OnDestroy` to stop and null `webcamTexture`.
- **Risk:** If the scene unloads or the component is disabled while the camera is open, the WebCam can keep running and cause leaks or errors.
- **Fix:** In `OnDisable` or `OnDestroy`, call the same cleanup as `ResetFlow()` (stop and null `webcamTexture`).

### 7. **EconomyManager – coroutine and destroyed UI**
- **Where:** `CountUpMoneyRoutine` updates `moneyText` every frame. If the object with `moneyText` is destroyed (e.g. scene unload), you may be updating a destroyed component.
- **Risk:** “MissingReferenceException” or similar when the referenced object is destroyed mid-coroutine.
- **Fix:** In the coroutine, check `moneyText != null` (and optionally that the GameObject is still active) before setting `moneyText.text`; if null, break out of the loop.

### 8. **LoginUIHandler – Start() and panel state**
- **Where:** Start() sets `loginPanel.alpha = 1` and `mainGameUI.SetActive(false)`. If LoginUIHandler’s GameObject is disabled at start, Start() doesn’t run.
- **Risk:** Only a problem if you ever enable the login flow after load; then the initial state might be wrong. Usually low risk if the login object is always active at boot.

### 9. **SaveManager – LastSavedPath not set**
- **Where:** `SavePhoto()` doesn’t set a `LastSavedPath` (or similar) property.
- **Risk:** If you later add logic that “verifies the last saved file” or “uploads last saved photo” (e.g. in FeedController), you’ll need this path. Not a bug today but easy to forget later.
- **Fix:** When you need it, set a `public string LastSavedPath { get; private set; }` (or similar) inside `SavePhoto()` after a successful write.

### 10. **PetCollectionManager – SaveManager timing**
- **Where:** In Awake you use `CurrentPhotoCount` (which depends on `SaveManager.Instance`). If SaveManager runs after PetCollectionManager, count is 0 at first.
- **Risk:** You already re-run `EnsureValidState()` in Start(), which helps. Remaining risk is if SaveManager is never present; then count is always 0 and progress stays 0.
- **Fix:** Optional: in Start or a later frame, if `CurrentPhotoCount` was 0 in Awake and is now > 0, run `EnsureValidState()` again so progress isn’t stuck.

---

## Structure / Design Advice

### Script execution order
- **SaveManager** is used by FeedController, CameraController, PetCollectionManager, EconomyManager, AlbumUI, PetHealthManager. It should run first.
- **Recommendation:** In Unity, set **Script Execution Order** so that `SaveManager` runs before default time (e.g. -100). That reduces “Instance is null” issues on first frame.

### Singleton cleanup
- When a singleton’s GameObject is destroyed, set `Instance = null` in `OnDestroy()` so other scripts don’t keep a reference to a destroyed object. This matters for ChatUIHandler (and any other singleton that isn’t DontDestroyOnLoad).

### Event subscriptions
- You already use OnEnable/OnDisable for `SaveManager.OnPhotoSaved` and `FirebaseManager.OnLoginSuccess`. Good. Just ensure every subscription is unsubscribed in OnDisable so you don’t get “missing object” callbacks after scene unload.

### InputField null in ChatUIHandler
- **Where:** `OnSendMessage()` uses `inputField.text` without checking `inputField != null`.
- **Risk:** If the reference is missing in the Inspector, NRE when sending.
- **Fix:** Add `if (inputField == null) return;` at the top of `OnSendMessage()`.

---

## Summary Table

| Area              | Issue                          | Severity | Fix location        |
|-------------------|--------------------------------|----------|---------------------|
| AlbumUI           | timestamp null/short Substring | High     | ReloadFromSave()    |
| PetHealthManager  | SaveManager null               | High     | All methods         |
| FeedController    | SaveManager null               | High     | FeedWithPhoto()     |
| FirebaseManager   | null msg to DisplayMessage     | High     | HandleChildAdded    |
| ChatUIHandler     | Instance lifecycle/destroyed    | Medium   | Awake/OnDestroy     |
| CameraController  | WebCam cleanup on disable      | Medium   | OnDisable/OnDestroy |
| EconomyManager    | moneyText destroyed in coroutine| Medium   | CountUpMoneyRoutine |
| ChatUIHandler     | inputField null                | Low      | OnSendMessage       |

Applying the high-severity fixes in code will make the app much more robust; the rest can be done gradually.
