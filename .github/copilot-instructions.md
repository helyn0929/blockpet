# Blockpet AI Coding Instructions

## Project Overview
**Blockpet** is a mobile pet collection game built in Unity with Firebase backend integration. Players feed pets, take photos, manage collections, and chat in real-time. The app supports iOS/Android with Google Sign-In authentication.

## Architecture Overview

### Key Components
- **FirebaseManager** (`Assets/Scripts/FirebaseManager.cs`) - Singleton managing Firebase auth, Realtime Database, and main-thread message queue
- **SaveManager** (`Assets/Scripts/Save/SaveManager.cs`) - Singleton for local persistence (game state, photos to disk)
- **PetHealthManager** - Tracks pet health decay over time using SaveManager
- **CameraController** - WebCam texture management for photo capture
- **AlbumUI** - Photo gallery with date-based grouping
- **ChatUIHandler** - Real-time chat UI bound to Firebase messages
- **EconomyManager** - Currency/reward system with animated count-up UI

### Data Flow
1. **Local Save** → SaveManager writes JSON to `Application.persistentDataPath/save.json`, photos to `photos/` folder
2. **Firebase Sync** → Chat messages, login events via `FirebaseDatabase.RootReference`
3. **UI Updates** → Event-driven via `SaveManager.OnSaveDataChanged` and `FirebaseManager.OnLoginSuccess`

## Critical Patterns & Conventions

### Singleton Pattern
All managers use singleton pattern with `Instance` static field and Awake() check. SaveManager additionally uses `DontDestroyOnLoad()`.

```csharp
public class FirebaseManager : MonoBehaviour {
    public static FirebaseManager Instance;
    void Awake() {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        // Initialize...
    }
}
```

**When adding new managers:** Always implement `OnDestroy()` to set `Instance = null` if not using DontDestroyOnLoad, so callbacks don't fire on destroyed objects.

### SaveManager Dependency
**CRITICAL:** SaveManager must execute before other game logic (use Script Execution Order: -100 in Project Settings). Many classes depend on it in their `Awake()`/`Start()`:
- Always guard with `if (SaveManager.Instance?.data != null)`
- Never assume it's ready on first frame in other scripts

### Firebase Threading
Firebase callbacks arrive on non-main threads. FirebaseManager queues actions via `_mainThreadQueue` to invoke them in Update(). When adding Firebase callbacks:
```csharp
// WRONG - crashes Unity
dbRef.Child("messages").OnChildAdded += (snapshot) => { UI.text = "..."; };

// RIGHT - queue for main thread
dbRef.Child("messages").OnChildAdded += (snapshot) => {
    _mainThreadQueue.Enqueue(() => { UI.text = "..."; });
};
```

### Event System
Use static Action events (not UnityEvents in UI) for loose coupling:
- `SaveManager.OnSaveDataChanged` - Fired when save.json updates
- `SaveManager.OnPhotoSaved` - Fired when photo saved successfully
- `FirebaseManager.OnLoginSuccess` - Fired after login completes

Subscribe in `OnEnable()`, unsubscribe in `OnDisable()` to prevent missing-object callbacks.

## High-Risk Patterns (Known Issues)

### 1. Null Safety on SaveManager Calls
See `POTENTIAL_BUGS_AND_ADVICE.md` - Multiple scripts call `SaveManager.Instance.data` without null checks. Always guard:
```csharp
if (SaveManager.Instance == null || SaveManager.Instance.data == null) return;
```

### 2. Timestamp Parsing in AlbumUI
`ReloadFromSave()` uses `p.timestamp.Substring(5, 5)` - crashes if timestamp is null or < 10 chars. Filter invalid timestamps first.

### 3. ChatUIHandler Singleton Lifecycle
Not using `DontDestroyOnLoad` - if chat panel is destroyed mid-game, FirebaseManager's callbacks reference a destroyed object. Either:
- Use `DontDestroyOnLoad` and manually clear state on scene load, OR
- Add null checks before calling `ChatUIHandler.Instance.DisplayMessage()`

### 4. WebCam Cleanup
CameraController doesn't stop WebCamTexture on disable - can leak resources. Add `OnDisable()` to stop and null the texture.

### 5. JSON Deserialization
FirebaseManager deserializes chat messages without null checks after `FromJson<>()`. Malformed JSON → null object → NRE accessing `msg.userName`.

## File Organization
- **Core Logic**: `Assets/Scripts/` (managers, controllers)
- **UI**: `Assets/Scripts/UI/` (handlers, helpers like ChatKeyboardAvoidance)
- **Persistence**: `Assets/Scripts/Save/SaveManager.cs`
- **Scenes**: `Assets/Scenes/` (Feed.unity, Album, MainGame, etc.)
- **Prefabs**: `Assets/Prefabs/PetBase.prefab` (main pet GameObject)

## Build & Platform Notes
- **Android**: `com.linnTech.blockpet` - Firebase config in `Assets/google-services.json`
- **iOS**: `com.linnTech.blockpet` - Firebase config in `Assets/GoogleService-Info.plist`
- **Dependencies**: External Dependency Manager (EDM4U) handles Android/iOS libs; use `-gvh_disable` flag when building plugins
- **Google Sign-In**: Requires `GOOGLE_SIGN_IN` scripting define and Web Client ID: `39782728703-9kf4c6p1lggr12b4t78nblohavu226td.apps.googleusercontent.com`
- **Firebase Project**: ID `blockpet-fc23b`, Database URL: `https://blockpet-fc23b-default-rtdb.firebaseio.com`
- **iOS Gotchas**: Provisioning profile IDs empty (must configure before signing); Apple Sign-In stubbed (not implemented)

## Build & Run Commands

### Android
1. **Setup**: Edit → Project Settings → Player → Android:
   - Set `overrideDefaultApplicationIdentifier = true`
   - Set Bundle Identifier to `com.linnTech.blockpet` (currently defaults to wrong value)
   - Set Scripting Define: `GOOGLE_SIGN_IN`
   - Min SDK: 23, Target: 33+
2. **Build**: File → Build Settings → Select Android → Build (generates `.apk`)
3. **Firebase**: Verify `Assets/google-services.json` is present
4. **Plugin build**: Use `-gvh_disable` flag to avoid EDM4U conflicts

### iOS
1. **Setup**: Edit → Project Settings → Player → iOS:
   - Set Bundle Identifier to `com.linnTech.blockpet`
   - Min OS: 15.0
   - Configure Provisioning Profile IDs (currently empty)
2. **Build**: File → Build Settings → Select iOS → Build (generates `.xcodeproj`)
3. **Firebase**: Verify `Assets/GoogleService-Info.plist` is present
4. **Dependencies**: Cocoapods auto-configured; requires macOS with Xcode
5. **Apple Sign-In**: Not yet implemented (TODO in FirebaseManager line 135)

### Script Execution Order (MUST DO)
Set SaveManager execution order first: Edit → Project Settings → Script Execution Order → Add `SaveManager` with order **-100**. This prevents null-reference errors in other managers that depend on SaveManager in Awake().

## Common Development Tasks

### Adding a New Persistent Feature
1. Add field to `SaveData` class in [Assets/Scripts/Save/SaveData.cs](Assets/Scripts/Save/SaveData.cs)
2. Modify the manager that owns the feature to call `SaveManager.Instance.SaveData()`
3. Subscribe to `SaveManager.OnSaveDataChanged` in UI to reflect changes
4. Test: Run in editor, check `Application.persistentDataPath/save.json` for your field

### Adding Firebase Sync (e.g., new chat messages)
1. Register listener in `FirebaseManager.cs` using database reference
2. **CRITICAL**: Wrap all UI/manager updates in `_mainThreadQueue`:
   ```csharp
   lock (_mainThreadQueue) {
       _mainThreadQueue.Enqueue(() => {
           // Update UI here (safe on main thread)
       });
   }
   ```
3. Test with offline mode: disconnect network, verify UI updates still work when reconnected

### Debugging Pet States
- **Photo save failure**: Check `SaveManager.Instance?.data != null` guard in `FeedController` (line 18)
- **Pet health decay incorrect**: Verify `SaveManager` initialized; check `PetHealthManager.CalculateOfflineDecay()` for `lastUpdateTime` parsing
- **Album crashes on scroll**: See AlbumUI `ReloadFromSave()` - check for timestamp null/length < 10

## Quick Reference: Common Errors

| Error | Likely Cause | Solution |
|-------|--------------|----------|
| NullReferenceException in PetHealth/FeedController startup | SaveManager not initialized first | Set SaveManager script order to -100 |
| Chat messages not displaying | Firebase callback not queued properly | Check `_mainThreadQueue.Enqueue()` wrapping |
| Album crashes: "Substring error" | `p.timestamp` is null or short | Add length check in AlbumUI.ReloadFromSave() |
| Chat displays but then crashes | ChatUIHandler destroyed mid-session | Add null check before `Instance.DisplayMessage()` call |
| WebCam keeps running after scene change | WebCamTexture not stopped | Verify `OnDisable()` calls Stop() and nulls texture |
| Photos not syncing to persistent storage | Photos folder doesn't exist | SaveManager creates it; check disk space |

## Testing & Debugging
- **Save data location**: Run `Debug.Log(Application.persistentDataPath)` to find save.json during play mode
- **Firebase connection**: Check FirebaseManager's Awake() logs for dependency status (look for "Firebase initialized" message)
- **Offline pet decay**: See `PetHealthManager.CalculateOfflineDecay()` - tests `DateTime` parsing of `lastUpdateTime`
- **Check existing bugs**: See `Assets/Scripts/POTENTIAL_BUGS_AND_ADVICE.md` for documented issues and fixes
