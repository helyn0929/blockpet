# Evolution Flow & Furniture Likes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The pet can evolve once at 100 photos (shared result for the whole camp, animation, dex record, 80% preview), and after evolution it likes up to 5 furniture pieces by main color and walks over to them; a debug menu simulates 80% and 100%.

**Architecture:** Pure rules (palette → type/main/sub, preview line, which furniture is liked, which one to visit) live in the `BlockPet.Economy` asmdef with EditMode tests. Firebase stores one evolution record per camp at `Rooms/{room}/evolution`, claimed by a transaction (first tapper decides). A runtime overlay plays the animation and tints the pet; a small component on the pet picks liked, placed furniture during idle and walks there with `AIController.GoTo`. Likes come from `Resources/Pets/furniture_likes.json`.

**Tech Stack:** Unity 2022.3 C#, uGUI built at runtime, Firebase Realtime Database, NUnit EditMode tests via `scripts/run-tests.sh` (Unity must be closed).

**Spec:** `docs/design/blockpet-design-doc.md` 5.17 (② types, ④ / ④-2 flow, ⑤ 80% preview, ⑥ after evolution) and 5.18 ⑤ (furniture likes, 2026-11-14).

## Global Constraints

- Evolve at exactly 100 photos (`PetCollectionManager.EvolveTarget`); counting starts at `startingPhotoCount`; uncategorized photos count as 美食 (index 0), same as `PetCollectionManager.EnergyFor`.
- Category order everywhere: 美食, 自然, 城市, 生活人物, 寵物 (`PetPreferences.Categories`).
- Type rule: reuse `DexRules.TypeFor(int[] palette)` (極致 > 50%, 彩虹 = all five present and main < 30%, else 進化型). Main = longest; tie → category of the last fed photo. Sub = second longest.
- Evolution happens once per pet; result stored at `Rooms/{room}/evolution` = `{type, main, sub, palette[5], at, by}`; an existing record is never overwritten.
- Heart decay is unchanged (no 20% slowdown).
- Placeholder art: evolved look = current pet sprite tinted by main color; rainbow = slow cycle through the five dot colors.
- Copy (【暫定】 in spec): ready bubble 「可以進化了！」 + 「點我進化」; result 「變成極致型了！」/「變成彩虹型了！」/「變成進化型了！」 + 「偏{main}」; button 「好」. Preview lines exactly: 極致 「全部都是{main}耶！好像要變成極致型…」, 彩虹 「顏色好平均，說不定會變彩虹…」, 進化型 「好像會變成{color}的…」.
- Color words for the 進化型 preview (my call, spec only gives 橘色 as the example): 美食 橘色, 自然 綠色, 城市 藍色, 生活人物 粉紫色, 寵物 黃色.
- Likes (【暫定】): max 5 per set; juvenile = 2 random, fixed per pet (seed = roomId + petIndex); evolved = by main category (彩虹型 uses key `rainbow`); placeholder table: 自然 → furn_hammock(睡), furn_fire(看); 美食 → furn_mat(坐), furn_lantern(看); juvenile pool = the 8 floor furniture except furn_lights (hang point), actions: furn_tent sleep, furn_hammock sleep, furn_mat sit, furn_fire/furn_easel/furn_lantern/furn_mailbox/furn_screen look. Visit chance 0.3 per idle end, stay 6 s, same item cooldown 60 s. Only placed furniture (`DecorationManager.CountOnCamp > 0`) counts.
- Shop red heart (`MarketEntry.petLikes`) = item is in the pet's current likes.

## Review Focus

- Two members tap 進化 at the same moment → both see the same type (transaction loser adopts the stored record, never writes its own). Test: rules-level `EvolutionRules.Decide` is deterministic for the same palette + last category (Task 1); manual: tap on two devices.
- Room switch while the animation is playing → overlay closes, no record written to the new room. Covered in Task 4 step (room id captured before the transaction, compared in callback).
- Pet already evolved when the app opens on another member's phone → replay once, then never again (PlayerPrefs key per room + `at`). Task 4.
- Liked furniture put away mid-visit → pet stops visiting it next idle (placed check each pick); current visit just ends. Task 5 test `PickVisit_IgnoresItemsNotPlaced`.
- Debug override active then cleared → evo bar returns to real photo progress. Task 6 manual check.

---

### Task 1: Evolution rules (pure)

**Files:**
- Create: `Assets/Scripts/Economy/EvolutionRules.cs`
- Test: `Assets/Tests/EditMode/EvolutionRulesTests.cs`

**Interfaces:**
- Produces (namespace `BlockPet.Feed`):
  - `struct EvolutionResult { string type; int main; int sub; int[] palette; }`
  - `static int[] EvolutionRules.Palette(IList<string> photoCategories, int start, int target = 100)` — counts of the first `target` photos from `start`; null/unknown → index 0.
  - `static int EvolutionRules.LastFedCategory(IList<string> photoCategories, int start, int target = 100)` — index of the last counted photo (−1 if none).
  - `static EvolutionResult EvolutionRules.Decide(int[] palette, int lastFed)`
  - `static bool EvolutionRules.CanEvolve(int fed)` (fed ≥ 100), `static bool EvolutionRules.ShowPreview(int fed)` (80 ≤ fed < 100)
  - `static string EvolutionRules.PreviewLine(int[] palette, int lastFed)`, `static string EvolutionRules.ResultTitle(string type)`, `static string EvolutionRules.ColorWord(int category)`

- [ ] **Step 1: Write failing tests** — `Palette_CountsFirst100FromStart` (110 photos, start 5 → sum 100, unknown counted as 美食); `Decide_ExtremeWhenMainOver50` (`{60,10,10,10,10}` → `extreme`, main 0, sub 1); `Decide_RainbowWhenAllPresentAndMainUnder30` (`{20,20,20,20,20}`, lastFed 3 → `rainbow`, main 3); `Decide_TieUsesLastFed` (`{40,40,10,5,5}`, lastFed 1 → main 1, sub 0); `Decide_EvolvedOtherwise` (`{45,40,15,0,0}` → `evolved`); `Preview_Thresholds` (79 false, 80 true, 100 false; CanEvolve 99 false 100 true); `PreviewLine_Texts` (extreme 「全部都是美食耶！好像要變成極致型…」, rainbow 「顏色好平均，說不定會變彩虹…」, evolved with main 自然 「好像會變成綠色的…」); `ResultTitle` (`extreme` → 「變成極致型了！」).
- [ ] **Step 2: Run** `scripts/run-tests.sh` → new tests fail (type missing).
- [ ] **Step 3: Implement** in `EvolutionRules.cs`; `Decide` uses `DexRules.TypeFor(palette)` for the type.
- [ ] **Step 4: Run** `scripts/run-tests.sh` → all pass.
- [ ] **Step 5: Commit** `feat: evolution rules (5.17)`.

### Task 2: Furniture-like rules + data file (pure)

**Files:**
- Create: `Assets/Scripts/Economy/FurnitureLikeRules.cs`
- Create: `Assets/Resources/Pets/furniture_likes.json`
- Test: `Assets/Tests/EditMode/FurnitureLikeRulesTests.cs`

**Interfaces:**
- Produces (namespace `BlockPet.Feed`), all `[Serializable]` for `JsonUtility`:
  - `class LikeEntry { string item; string action; float weight = 1; }` — actions: `look`, `sleep`, `sit` (more later).
  - `class LikeSet { string key; LikeEntry[] likes; }` — key = category name, `rainbow`, or `juvenile`.
  - `class BreedLikes { string breed; LikeSet[] sets; }`
  - `class FurnitureLikeTable { float visitChance = 0.3f; float staySeconds = 6f; float cooldownSeconds = 60f; int maxLikes = 5; int juvenileCount = 2; BreedLikes[] breeds; }`
  - `static List<LikeEntry> FurnitureLikeRules.LikesFor(FurnitureLikeTable t, string breed, string evoType, string mainCategory, int seed)` — evolved: set by key (`rainbow` when type is rainbow, else main category), unknown breed → `bowbow`, capped at `maxLikes`; not evolved (`evoType` null/empty): `juvenileCount` entries picked from the `juvenile` set with `new Random(seed)`, no duplicates.
  - `static LikeEntry FurnitureLikeRules.PickVisit(IList<LikeEntry> likes, Func<string,bool> isPlaced, IDictionary<string,float> lastVisit, float now, FurnitureLikeTable t, Random rnd)` — null if roll ≥ `visitChance`, nothing placed, or all placed ones in cooldown; otherwise weighted pick among eligible.
  - `static bool FurnitureLikeRules.Likes(IList<LikeEntry> likes, string itemId)`

- [ ] **Step 1: Write failing tests** — `LikesFor_EvolvedUsesMainColor` (自然 → furn_hammock sleep, furn_fire look); `LikesFor_RainbowKey`; `LikesFor_UnknownBreedFallsBackToBowbow`; `LikesFor_JuvenileTwoFixedBySeed` (same seed same 2 items, different seeds can differ, never duplicate); `LikesFor_CapsAtMax` (set with 7 entries → 5); `PickVisit_IgnoresItemsNotPlaced`; `PickVisit_RespectsCooldown` (visited at t=10, now 50 → null; now 71 → item); `PickVisit_RollAboveChanceReturnsNull` (seeded `Random` whose first `NextDouble` ≥ 0.3).
- [ ] **Step 2: Run** `scripts/run-tests.sh` → fail.
- [ ] **Step 3: Implement** rules; write `furniture_likes.json` with breed `bowbow`, sets `自然`, `美食`, `juvenile` per Global Constraints and the four tuning numbers.
- [ ] **Step 4: Run** `scripts/run-tests.sh` → pass. Add one test that loads the real JSON via `File.ReadAllText("Assets/Resources/Pets/furniture_likes.json")` + `JsonUtility` and asserts the 自然 set has 2 entries.
- [ ] **Step 5: Commit** `feat: furniture like rules and table (5.18)`.

### Task 3: Firebase evolution record

**Files:**
- Create: `Assets/Scripts/FirebaseManager.Evolution.cs` (partial `FirebaseManager`)
- Modify: `Assets/Scripts/FirebaseManager.cs` — start/stop the listener where `petState` listening starts/stops (`StartMetaListening` / `StopPetListening`).

**Interfaces:**
- Consumes: `EvolutionResult` (Task 1).
- Produces:
  - `[Serializable] class EvolutionRecord { string type; int main; int sub; int[] palette; long at; string by; }`
  - `EvolutionRecord CurrentEvolution { get; }` (null = not evolved), `bool EvolutionLoaded`
  - `static event Action OnEvolutionChanged` (main thread)
  - `void ClaimEvolution(EvolutionResult r, Action<EvolutionRecord, bool> done)` — transaction on `Rooms/{room}/evolution`: abort if a value exists; `done(record, claimedByMe)` on main thread; if aborted, `done` gets the stored record and `false`; on failure `done(null,false)`.
  - `void DebugClearEvolution()` — removes the node.

- [ ] **Step 1: Implement** listener (`ValueChanged` → parse with `JsonUtility`, room check like `PostcardEvent`) and `ClaimEvolution`; write the record with explicit dictionary (`palette` as list of longs).
- [ ] **Step 2: Run** `scripts/run-tests.sh` → compiles, all pass.
- [ ] **Step 3: Commit** `feat: shared evolution record (5.17 ④-2)`.

### Task 4: Evolution flow on the home screen

**Files:**
- Create: `Assets/Scripts/UI/Home/EvolutionFlow.cs` (runtime-bootstrapped overlay, like `TravelHomeOverlay`: `RuntimeInitializeOnLoadMethod`, ScreenSpaceOverlay sortingOrder 13, home-only visibility).
- Create: `Assets/Scripts/UI/Home/EvolvedLook.cs` (on the pet: tints its SpriteRenderers by main color; rainbow cycles the five dot colors over 4 s; restores on null).
- Modify: `Assets/Scripts/Hand/HandController.Pet.cs` — new `Say.Evolve` (ready, not evolved: 「可以進化了！」/「點我進化」, highest priority after Reaction); `Say.Preview` (80–99: title = `EvolutionRules.PreviewLine`, sub = hungry hint when hungry); `OnPetTapped` calls `EvolutionFlow.Request()` when ready instead of dealing.
- Modify: `Assets/Scripts/UI/TopBar/EvoBarView.cs` subscriber hookup (in `EvolutionFlow`) to `OnEvolveRequested`; after evolution the bar shows 「成長紀錄」 and never reports ready.
- Modify: `Assets/Scripts/PetCollectionManager.cs` — `int FedCount` (progress photos, capped 100), `int[] Palette()`, `int LastFed()` using Task 1 helpers; `static int[] DebugPalette` + `static int DebugLastFed` (when set, `FedCount`/`Palette()`/`LastFed()`/`EnergyByCategory` use it; local only, not saved); `bool IsEvolved` = `FirebaseManager.CurrentEvolution != null`.

**Interfaces:**
- Consumes: Tasks 1, 3; `DexRules.DefaultSeries`, `FirebaseManager.RecordDex`, `DexScreen.ShowNew`, `PetHop.Play`.
- Produces: `static void EvolutionFlow.Request()`; `static bool EvolutionFlow.Playing`.

- [ ] **Step 1: Implement `Request()`**: guard not evolved, `CanEvolve(FedCount)`, not already claiming; capture room id; `Decide(Palette(), LastFed())` → `ClaimEvolution`; in callback ignore if room changed; play animation with the returned record; if `claimedByMe` → `RecordDex(DefaultSeries, record.type, new DexEntry{ palette = percent palette, evolvedAt = at, days = days since room createdAt or 0 })` and `ShowNew` when `isNewCell`.
- [ ] **Step 2: Implement animation** (≈2.5 s): dim 0.6 → pet copy (UI Image of the pet sprite) glow pulse + 2 hops → white flash 0.2 s → tinted pet → title 19 + sub 12 「偏美食」 → yellow 「好」 closes; mark seen `PlayerPrefs["BlockPet.Evo.Seen.{room}"] = at`.
- [ ] **Step 3: Implement replay**: on `OnEvolutionChanged` / home visible, if record exists and seen key ≠ `at` → play once (no claim, no dex).
- [ ] **Step 4: Implement 80% preview silhouette**: when `ShowPreview(FedCount)` and not evolved, a glowing white-tinted copy of the pet sprite at 70% alpha next to the pet (follow pet like `TravelHomeOverlay.FollowPet`), pulsing alpha 0.4–0.8 over 1.6 s, tinted with `ColorWord` color (rainbow: cycle).
- [ ] **Step 5: Run** `scripts/run-tests.sh` → pass.
- [ ] **Step 6: Commit** `feat: evolution flow, animation, preview (5.17)`.

### Task 5: Pet walks to liked furniture + shop heart

**Files:**
- Create: `Assets/Scripts/Decoration/PetFurnitureVisits.cs` (added to the pet at runtime by `EvolutionFlow` bootstrap or on `AIController.Instance`).
- Modify: `Assets/Scripts/AIController.cs` — `public System.Func<bool> TryDirectedIdle;` called in `RunIdle` after the wait; if it returns true, `yield break` (the handler starts `GoTo` next frame).
- Modify: `Assets/Scripts/UI/Market/MarketScreenController.cs` — before building cards, set `e.petLikes = FurnitureLikeRules.Likes(currentLikes, e.id)` for furniture entries (environment entries keep their catalog value).
- Modify: `Assets/Scripts/Hand/HandController.Pet.cs` — `public void SayLike(string action)` shows a short bubble: look 「♥」, sleep 「Zz…」, sit 「♪」 for `staySeconds`.

**Interfaces:**
- Consumes: Task 2 rules/table, Task 3 `CurrentEvolution`, `DecorationManager.CountOnCamp`, `DecorationManager.Spawned`, `DecorationLayout.FootWorld`, `AIController.GoTo/ReleaseBusy`, `PetHop.Play`.
- Produces: `static List<LikeEntry> PetFurnitureVisits.CurrentLikes()` (breed `bowbow`, type/main from `CurrentEvolution`, seed = hash(roomId + petIndex)).

- [ ] **Step 1: Implement visit**: `TryDirectedIdle` → `PickVisit`; target = foot point of the first placed `DecorationObject` of that item, offset 0.3 world units in front (−y); `GoTo` → on arrive: face the item, `SayLike(action)`, look = one `PetHop`, sleep = scale pet to 0.9 and lower alpha 0.85 for the stay, sit = small hop; after `staySeconds` restore and `ReleaseBusy()`; record `lastVisit[item] = Time.time`. Skip while decorating (`DecorModeSettings.IsDecorating`), while traveling (pet hidden), or when `HandController` is dealt.
- [ ] **Step 2: Shop heart** as listed.
- [ ] **Step 3: Run** `scripts/run-tests.sh` → pass.
- [ ] **Step 4: Commit** `feat: pet visits liked furniture, shop heart (5.18 ⑤)`.

### Task 6: Debug menu + docs

**Files:**
- Create: `Assets/Editor/Pets/EvolutionDebugMenu.cs` (menu `BlockPet/Debug/進化/…`, Play mode only).
- Modify: `docs/feature_map.md`, `Assets/CLAUDE.md`.

**Interfaces:**
- Consumes: Tasks 1–5.

- [ ] **Step 1: Menu items**: 「模擬 80%（偏美食）」 `{60,10,5,5,0}`; 「模擬 80%（很平均）」 `{16,16,16,16,16}`; 「模擬滿 100 張・極致型」 `{70,10,10,5,5}`; 「模擬滿 100 張・彩虹型」 `{20,20,20,20,20}`; 「模擬滿 100 張・進化型」 `{45,35,10,10,0}`; 「清掉模擬」; 「清掉進化結果（重來）」 → `DebugClearEvolution` + clear seen key.
- [ ] **Step 2: Docs**: feature_map 進化 / 家具愛好 entries; CLAUDE.md game-rule lines (100 photos, no heart slowdown, likes table location, debug menu).
- [ ] **Step 3: Run** `scripts/run-tests.sh` → pass.
- [ ] **Step 4: Manual check in Play**: 80% shows silhouette + line; 100% shows 「可以進化了！」, tap → animation → tinted pet → dex 「圖鑑新增！」; reopening the camp doesn't replay; 「清掉進化結果」 returns to ready; place 吊床 after 自然 evolution → pet walks over within a few idles.
- [ ] **Step 5: Commit** `feat: evolution debug menu; docs`.
