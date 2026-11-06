# BlockPet 功能地圖

最後掃描：2026-11-05（掃描 `Assets/` 的場景、Prefab、腳本引用）。
新增或搬移功能時同步更新這份文件。找不到或無法從專案確認的標「待確認」。

## 場景

| 場景 | 狀態 |
|---|---|
| `Assets/Scenes/Feed.unity` | **唯一放進 Build Settings 的場景**，所有遊戲畫面都在這裡 |
| `Assets/Scenes/Chat.unity` | 不在 Build Settings；舊版聊天／相簿／登入，用途待確認 |
| `Assets/Scenes/SampleScene.unity` | 不在 Build Settings；只掛 `FeedController`，用途待確認 |
| `Assets/Scenes/fishtest.unity` | 不在 Build Settings；只放 `PetBase.prefab`，用途待確認 |
| `Assets/Scenes/Treehouse.unity` | 不在 Build Settings；沒有掛任何專案腳本，用途待確認 |
| `Assets/Scenes/Expedition.unity` | 是空資料夾（不是場景檔），用途待確認 |

「執行時建立」：介面是程式在執行時用 uGUI 建出來的，場景裡只有掛腳本的物件，沒有對應的 Prefab。

## 功能

以下「Scene」欄都是 `Feed.unity`，除非另外註明。

### 登入與帳號
- Scene：Feed.unity（LoginPanel）
- 主要 Prefab：無（`AuthScreenController` 執行時建立；`LoginScreenController` 用 `Art/UI/Login_UI/LoginScreen.uxml`）
- 主要腳本：`Scripts/FirebaseManager.cs`（Google／Apple／訪客登入）、`Scripts/LoginUIHandler.cs`（登入後流程、自動回上次的空間）、`Scripts/UI/Auth/AuthScreenController.cs`、`Scripts/LoginScreenController.cs`

### 房間（空間）資料與選擇
- Scene：Feed.unity（RoomPage）
- 主要 Prefab：無（執行時建立）
- 主要腳本：`Scripts/UI/Room/RoomRingController.cs`（轉盤選空間、搖桿、彈幕）、`Scripts/UI/Room/RoomJoystick.cs`、`Scripts/UI/Room/RoomRingInput.cs`、`Scripts/UI/Room/RingSectorGraphic.cs`、`Scripts/FirebaseManager.cs`（建立／加入／離開／刪除空間）
- 舊版：`Scripts/UI/Room/RoomUIController.cs`、`Scripts/UI/Room/RoomSelectPageController.cs`、`Scripts/UI/Room/WebView/RoomWebViewBridge.cs`、`Art/UI/Room_UI/RoomScreen.uxml`；是否還在使用待確認

### 空間資訊（點寵物頭像：成員、邀請碼、營地代碼、改名、離開）
- Scene：Feed.unity（PetSettingPage）
- 主要 Prefab：無（執行時建立）
- 主要腳本：`Scripts/UI/Room/RoomInfoScreenController.cs`；營地代碼（藍色 6 碼，5.15 ③-2）由 `FirebaseManager.EnsureCampCode` 產生，存 `Rooms/{room}/meta/campCode`＋`CampCodes/{代碼}`＝空間 id
- 舊版：`Scripts/UI/PetSetting/PetSettingPageController.cs`（`Art/UI/PetSetting_UI/PetSettingScreen.uxml`），也還掛在場景裡，是否已停用待確認

### 首頁狀態欄（愛心、想吃、進化條、金幣、復活入口）
- Scene：Feed.unity
- 主要 Prefab：`Assets/Prefabs/UI/TopBar.prefab`
- 主要腳本：`Scripts/UI/TopBar/TopBarView.cs`、`Scripts/UI/TopBar/HeartsView.cs`、`Scripts/UI/TopBar/EvoBarView.cs`、`Scripts/UI/TopBar/MenuToggle.cs`、`Scripts/UI/TopBar/TopBarConfig.cs`
- 首頁 v9 的 Home 鍵、選單鈕（沒有底的線條圖示，設計文件 5.25 ②）：`Scripts/UI/Home/Home9TopIcons.cs`（`TopBarView.Awake` 執行時換樣式，不用重建場景）
- 舊版：`Scripts/UI/Home/HomeTopBar.cs`

### 首頁 v9：點寵物出牌、照片貼紙、底部相簿／相機／聊天（設計文件 5.25）
- Scene：Feed.unity
- 主要 Prefab：無（`HandController` 執行時建立）；`Assets/Prefabs/VFX_PhotoFly.prefab` 只在 PetBase.prefab 底下，Feed.unity 已不使用
- 主要腳本：`Scripts/Hand/HandController.cs`（發牌／收回、舉起照片、餵食飛牌、底部固定三顆鈕）、`Scripts/Hand/HandController.Pet.cs`（點寵物、餓了輕晃、寵物泡泡：好餓／飽飽的／餵食反應／乾淨了）、`Scripts/Hand/HandController.Stickers.cs`（照片貼紙、最新照片貼紙、拖曳位置）、`Scripts/HomeRules/StickerRules.cs`（貼紙規則，獨立組件 `BlockPet.Home`）、`Scripts/Hand/PhotoLibrary.cs`（讀相簿）、`Scripts/Hand/LatestCardDrag.cs`（最新貼紙拖曳）、`Scripts/FeedController.cs`（餵食動畫）、`Scripts/PhotoCooldown.cs`（10 分鐘冷卻）
- 素材：`Assets/Art/UI/Home9/Sprites/`（匯入設定 `Editor/TopBar/TopBarSpriteImporter.cs`）→ `Resources/UI/Home9Skin.asset`（`Scripts/UI/Home/Home9Skin.cs`，由 `Editor/Home/Home9SkinBuilder.cs` 自動填）；規格 `docs/home9/`
- 看過的貼紙存在手機 PlayerPrefs（`BlockPet.Home9.Seen.{空間}.{uid}`、第一次使用時間 `BlockPet.Home9.Since.…`、最新貼紙位置 `BlockPet.Home9.LatestPos`）
- 舊版：`Scripts/Hand/HandSwipe.cs`（首頁不再用；照片檢視、分割畫面左右滑還在用）

### 餵食結算（辨識、命中、金幣、愛心）
- Scene：無（純程式，由餵食流程呼叫）
- 主要 Prefab：無
- 主要腳本：`Scripts/Feed/FeedSettlement.cs`（結算流程）、`Scripts/Economy/EconomyRules.cs`（**經濟規則唯一定義處**：愛心乘數表、回復量、金幣計算、未定暫用值；依據 `docs/design/blockpet-economy-spec.md`）、`Scripts/Feed/FeedGate.cs`（昏倒時擋住餵食入口）、`Scripts/Feed/TodayWant.cs`（現在想吃：取目前空間、寵物、伺服器時間）、`Scripts/Economy/WantSchedule.cs`（現在想吃的時段與抽選：每 2 小時、台灣時間偶數整點）、`Scripts/Economy/PetPreferences.cs`（五大類別、寵物偏愛；2026-11-05 從 Feed 搬到 Economy）、`Scripts/Vision/PhotoClassifier.cs`（照片辨識）、`Scripts/Feed/PetHop.cs`

### 愛心、死亡與復活
- Scene：Feed.unity
- 主要 Prefab：無（`ReviveDialog` 執行時建立）
- 主要腳本：`Scripts/PetHealthManager.cs`、`Scripts/Economy/PetHearts.cs`（掉心規則、星等；2026-11-05 從 Feed 搬到 Economy，獨立組件 `BlockPet.Economy` 讓測試可以引用）、`Scripts/Feed/ReviveDialog.cs`（復活視窗：直接付營地金幣，價格 `EconomyRules.RevivePrice`）、`Scripts/FirebaseManager.cs`（`petState.hearts`／`heartsAt`、`ReviveRoomPet` 扣金幣復活）

### 寵物（移動、動畫、收集）
- Scene：Feed.unity
- 主要 Prefab：無；寵物是場景物件 `Zapup`（掛 `AIController`）。`Assets/Prefabs/PetBase.prefab`（狗，舊餵食動畫用）2026-11-05 已從 Feed.unity 移除，檔案保留；`shiba.prefab` 沒有被任何場景引用
- 主要腳本：`Scripts/AIController.cs`（待機移動；速度＝基本 1.5 × `EconomyRules` 愛心乘數，昏倒時停住）、`Scripts/PetAnimationEvents.cs`、`Scripts/PetCollectionManager.cs`（寵物資料、進化能量）、`Scripts/PetEatTest.cs`（測試用，待確認）

### 拍照
- Scene：Feed.unity
- 主要 Prefab：無
- 主要腳本：`Scripts/CameraController.cs`（WebCamTexture 拍照）、`Scripts/CameraUIManager.cs`（首頁／相機／相簿／聊天模式切換）
- 相機模式的按鈕：沿用首頁舊按鈕物件 HomePage / Button(album)、Button(chat)，由 `CameraUIManager` 換圖示和功能：Button(album)＝關閉相機（拍完是重拍），Button(chat)＝翻轉鏡頭。使用中（2026-11-05 確認）；首頁模式下這兩顆不再使用

### 營地分割畫面（大照片、留言、內嵌相機）
- Scene：Feed.unity
- 主要 Prefab：無（執行時建立）
- 主要腳本：`Scripts/Hand/PhotoSplitView.cs`
- 舊版：`Scripts/BulletinManager.cs`（7 張照片看板，已不使用；2026-11-05 已在場景裡把 BulletinPhoto 物件和腳本都關掉）

### 相簿與照片檢視（拍立得、儲存／分享）
- Scene：Feed.unity（AlbumPage）
- 主要 Prefab：`Assets/Prefabs/PhotoItem.prefab`、`Assets/Prefabs/DateHeader 1.prefab`（舊版相簿用，新版是否還用待確認）；`PhotoItem 1.prefab`、`DateHeader.prefab` 沒有被引用，待確認
- 主要腳本：`Scripts/UI/Album/AlbumScreenController.cs`、`Scripts/UI/Album/PhotoViewScreen.cs`（拍立得檢視）、`Scripts/UI/Album/PhotoExport.cs`（浮水印、存相簿、分享）、`Scripts/UI/Album/PhotoViewSkin.cs`
- 舊版：`Scripts/AlbumUI.cs`、`Scripts/AlbumUI_Legacy.cs`、`Scripts/UI/Album/AlbumUIManager.cs`、`Scripts/UI/Album/PhotoDetailUIPage.cs`（是否還在使用待確認）、`Scripts/AlbumPanelToggle.cs`（已不使用：首頁相簿按鈕已拿掉，`CameraUIManager` 執行時會把它關掉）

### 聊天室
- Scene：Feed.unity（ChatPage）
- 主要 Prefab：`Assets/Prefabs/MessageRow.prefab` 只在 Chat.unity 裡被引用，Feed.unity 是否還用待確認
- 主要腳本：`Scripts/UI/Chat/ChatScreenController.cs`、`Scripts/ChatEntry.cs`、`Scripts/ChatMassage.cs`
- 舊版：`Scripts/ChatPanelToggle.cs`（已不使用：首頁聊天按鈕已拿掉，`CameraUIManager` 執行時會把它關掉）、`Scripts/UI/Chat/ChatUIController.cs`（場景裡已停用）、`Scripts/ChatUIHandler.cs`（是否還在使用待確認）、`Art/UI/Chat_UI/ChatScreen.uxml`

### 頭貼
- Scene：Feed.unity
- 主要 Prefab：無
- 主要腳本：`Scripts/AvatarManager.cs`（自己的頭貼、存檔、上傳）、`Scripts/UI/AvatarPhoto.cs`（成員頭貼快取、圓形頭像）、`Scripts/UI/Settings/SettingsAvatarButton.cs`（沒有被場景引用，待確認）

### 經濟（房間金幣）
- Scene：Feed.unity
- 主要 Prefab：無
- 主要腳本：`Scripts/EconomyManager.cs`、`Scripts/UI/Market/MarketWallet.cs`

### 商城
- Scene：Feed.unity（MarketPage）
- 主要 Prefab：`Assets/Art/Test/ShopItemCard.prefab`
- 分頁：家具／環境／寵物蛋（2026-11-06：裝飾、道具分頁刪除）
- 主要腳本：`Scripts/UI/Market/MarketScreenController.cs`、`Scripts/UI/Market/MarketCatalog.cs`（商品與價格）、`Scripts/UI/Market/MarketInventoryStore.cs`；舊版相關：`MarketPageController.cs`、`MarketUIController.cs`、`ShopGridController.cs`、`Scripts/UI/Market/WebView/MarketWebViewBridge.cs`、`Art/UI/Market_UI/MarketScreen.uxml`，哪些還在用待確認

### 佈置家具
- Scene：Feed.unity
- 主要 Prefab：`Assets/Art/UI/Mainroom_UI/DecorationObject.prefab`
- 主要腳本：`Scripts/Decoration/DecorationManager.cs`、`Scripts/Decoration/DecorationObject.cs`、`Scripts/Decoration/DecorationDatabase.cs`、`Scripts/UI/Home/RoomEditorController.cs`、`Scripts/UI/Home/DecorationPickerController.cs`（`Art/UI/Mainroom_UI/DecorationPicker.uxml`）、`Scripts/UI/TopBar/DecorModeSettings.cs`
- `Assets/Prefabs/Livingroom.prefab` 沒有被引用，待確認

### 設定
- Scene：Feed.unity（SettingsPage）
- 主要 Prefab：無（執行時建立）
- 主要腳本：`Scripts/UI/Settings/SettingsScreenController.cs`
- 舊版：`Scripts/UI/Settings/SettingsUIController.cs`（`Art/UI/Settings_UI/SettingsScreen.uxml`）

### 圖鑑（設計文件 5.23）
- Scene：無（執行時自動建立，sortingOrder 38）
- 主要 Prefab：無；素材 `Assets/Resources/UI/DexSkin.asset`（由 `Assets/Editor/Dex/DexSkinBuilder.cs` 從 `Assets/Art/UI/Dex/Sprites/` 自動填）
- 主要腳本：`Scripts/UI/Dex/DexScreen.cs`（主頁、詳細、還沒收到、圖鑑新增）、`Scripts/Economy/DexRules.cs`（系列與三種型目錄、圖鑑規則、進化型判斷 `TypeFor`）、`Scripts/FirebaseManager.cs`（`LoadRoomDex`、`RecordDex`：`Rooms/{room}/dex`）；入口 `Scripts/UI/TopBar/MenuToggle.cs` 的圖鑑按鈕
- 測試用：Editor 選單 BlockPet/Debug/圖鑑（`Assets/Editor/Dex/DexDebugMenu.cs`）
- 待接：進化系統（進化那一刻呼叫 `RecordDex`＋`DexScreen.ShowNew`）、收滿送蛋（抽蛋系統）、其他成員補看「圖鑑新增」；各外型寵物圖待美術（暫用 shiba.png 上色）

### 旅行地圖（設計文件 5.21）
- Scene：無（執行時自動建立，sortingOrder 38）
- 主要 Prefab：無；素材 `Assets/Resources/UI/TravelSkin.asset`（由 `Assets/Editor/Travel/TravelSkinBuilder.cs` 從 `Assets/Art/UI/Travel/Sprites/` 自動填）
- 主要腳本：`Scripts/Travel/TravelMapGen.cs`（地圖生成：陸地、好友位置、路、湖、小山丘、樹、小花、路邊小物、雲霧，照設計稿 TravelGrow 移植；獨立組件 `BlockPet.Travel`）、`Scripts/UI/Travel/TravelScreen.cs`（地圖＋頂部、底部）、`Scripts/UI/Travel/MapCurveGraphic.cs`（路的曲線）；入口 `MenuToggle` 的旅行按鈕
- 截圖：Editor 選單 BlockPet/Travel/Snapshot Map（`Assets/Editor/Travel/TravelMapSnapshot.cs`，可 batchmode）
- 派寵物旅行（5.21 流程 5～12）：`FirebaseManager` 的 `SendPetOnTrip`／`FinishTripIfHome`，資料 `Rooms/{room}/trip`、`guests/{對方}`、`passport/{對方}`、`tripLast`；規則 `Scripts/Travel/TripRules.cs`；地圖上的派出發面板、旅途中、抵達、在對方營地、護照在 `TravelScreen`；首頁上的出門木牌、客人、回家蓋章卡在 `Scripts/UI/Travel/TravelHomeOverlay.cs`（執行時自動建立）；測試用選單 BlockPet/Debug/旅行
- 營地好友（5.21 流程 1～3）：`FirebaseManager` 的 `SendFriendInvite`／`AcceptFriendInvite`／`DeclineFriendInvite`，資料 `Rooms/{room}/friendInvites/{對方}`、`Rooms/{room}/friends/{對方}`（slot＝地圖格子，放好就固定）；規則在 `Scripts/Travel/CampFriendRules.cs`；面板「加營地好友」「好友邀請」在 `TravelScreen`
- 地圖種子＝目前營地的營地代碼（還沒有時用 K7R2PX）；好友用真的營地好友（截圖時用示範假資料）；之後接營地代碼（5.15）、Firebase 營地好友、加好友與派寵物旅行流程、護照

### 頁面切換
- Scene：Feed.unity
- 主要 Prefab：無
- 主要腳本：`Scripts/UI/Navigation/PageManager.cs`、`Scripts/UI/Navigation/HomeShopButton.cs`

### 存檔
- Scene：Feed.unity
- 主要 Prefab：無
- 主要腳本：`Scripts/Save/SaveManager.cs`（每個空間一份 save_{room}.json、全域 save_global.json）、`Scripts/Save/SaveData.cs`、`Scripts/Save/PhotoMeta.cs`

### 天氣
- Scene：無（執行時自動建立）
- 主要 Prefab：無
- 主要腳本：`Scripts/Weather/WeatherService.cs`、`Scripts/Weather/WeatherEffects.cs`；Editor 選單 `Editor/Weather/WeatherDebugMenu.cs`

### iOS 桌面小工具
- Scene：無（執行時自動建立，只在 iOS 實機）
- 主要 Prefab：無
- 主要腳本：`Scripts/Widget/WidgetSync.cs`；小工具本體 `ios-widget/BlockPetWidget/`；加進 Xcode：`Assets/Editor/iOSWidgetPostBuild.cs`

### 震動、分享等原生功能
- Scene：無
- 主要 Prefab：無
- 主要腳本：`Scripts/Haptics.cs`、`Scripts/ShareText.cs`；原生程式在 `Assets/Plugins/iOS/`

### 測試
- Scene：無
- 主要腳本：`Assets/Tests/EditMode/`（`BlockPet.Tests.EditMode.asmdef`，引用 `BlockPet.Economy`、`BlockPet.Travel`、`BlockPet.Home`）：`SampleTest.cs`、`EconomyRulesTests.cs`（經濟規格第 2 節驗收條件）、`WantScheduleTests.cs`（現在想吃時段）、`DexRulesTests.cs`（圖鑑規則）、`TravelMapGenTests.cs`（旅行地圖生成，期望值用設計稿原程式算）、`CampFriendRulesTests.cs`（營地好友規則）、`TripRulesTests.cs`（旅行時間與階段）、`StickerRulesTests.cs`（首頁照片貼紙）；執行 `scripts/run-tests.sh`

## 規劃中、專案裡找不到程式的功能

| 功能 | 狀態 |
|---|---|
| 進化分支 | 待確認（找不到；`PetCollectionManager` 只有進化能量） |
| 抽蛋 | 待確認（找不到） |
| 推播 | 有 `Editor/com.unity.mobile.notifications` 設定資料夾；遊戲腳本待確認（找不到） |
