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
- 主要腳本：`Scripts/FirebaseManager.cs`（Google／Apple／訪客登入）、`Scripts/LoginUIHandler.cs`（登入後流程、自動回上次的空間；開 App 自動登入時 LoginPanel 是關著的，由 `Scripts/UI/Navigation/PageManager.cs` 收登入事件後呼叫 `ResumeLastRoomOnLaunch`）、`Scripts/UI/Auth/AuthScreenController.cs`、`Scripts/LoginScreenController.cs`

### 房間（空間）資料與選擇
- Scene：Feed.unity（RoomPage）
- 主要 Prefab：無（執行時建立）
- 主要腳本：`Scripts/UI/Room/RoomRingController.cs`（轉盤選空間：點格子直接進去、搖桿推過去也能進去；彈幕）、`Scripts/UI/Room/RoomJoystick.cs`、`Scripts/UI/Room/RoomRingInput.cs`、`Scripts/UI/Room/RingSectorGraphic.cs`、`Scripts/FirebaseManager.cs`（建立／加入／離開／刪除空間）
- 舊版：`Scripts/UI/Room/RoomUIController.cs`、`Scripts/UI/Room/RoomSelectPageController.cs`、`Scripts/UI/Room/WebView/RoomWebViewBridge.cs`、`Art/UI/Room_UI/RoomScreen.uxml`；是否還在使用待確認

### 空間資訊（點寵物頭像：成員、邀請碼、營地代碼、改名、離開）
- Scene：Feed.unity（PetSettingPage）
- 主要 Prefab：無（執行時建立）
- 主要腳本：`Scripts/UI/Room/RoomInfoScreenController.cs`；營地代碼由 `FirebaseManager.EnsureCampCode` 產生（只當明信片地圖的種子，2026-11-08 起不顯示）
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
- 主要腳本：`Scripts/Hand/HandController.cs`（發牌／收回、舉起照片、餵食飛牌、底部固定三顆鈕）、`Scripts/Hand/HandController.Pet.cs`（點寵物、餓了輕晃、寵物泡泡：好餓／飽飽的／餵食反應／乾淨了；餵食反應只有一行＝類別色點＋「是○○！好好吃！」／「是○○耶…」，沒命中歪頭）、`Scripts/Hand/HandController.Stickers.cs`（照片貼紙 85% 半透明、最新照片貼紙：不透明＋細黃邊 2、沒有「最新」標籤、拖曳位置）、`Scripts/HomeRules/StickerRules.cs`（貼紙規則，獨立組件 `BlockPet.Home`）、`Scripts/Hand/PhotoLibrary.cs`（讀相簿）、`Scripts/Hand/LatestCardDrag.cs`（最新貼紙拖曳）、`Scripts/FeedController.cs`（餵食動畫）、`Scripts/PhotoCooldown.cs`（10 分鐘冷卻）
- 素材：`Assets/Art/UI/Home9/Sprites/`（由 `docs/home9/Tools/make_home9.py` 產生；`tag_latest` 已不使用）（匯入設定 `Editor/TopBar/TopBarSpriteImporter.cs`）→ `Resources/UI/Home9Skin.asset`（`Scripts/UI/Home/Home9Skin.cs`，由 `Editor/Home/Home9SkinBuilder.cs` 自動填）；規格 `docs/home9/`
- 截圖：Editor 選單 BlockPet/Debug/寵物泡泡截圖（`Assets/Editor/Home/PetBubbleSnapshot.cs`，可 batchmode；輸出 Logs/pet_bubble_hit／miss／unknown.png）
- 看過的貼紙存在手機 PlayerPrefs（`BlockPet.Home9.Seen.{空間}.{uid}`、第一次使用時間 `BlockPet.Home9.Since.…`、最新貼紙位置 `BlockPet.Home9.LatestPos`）
- 舊版：`Scripts/Hand/HandSwipe.cs`（首頁不再用；拍立得分割畫面左右滑換張還在用）

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
- 主要腳本：`Scripts/CameraController.cs`（WebCamTexture 拍照；數位縮放 1×～5×（裁切中間，預覽和照片一樣，兩指縮放，翻轉鏡頭回 1×）、魚眼開關（PlayerPrefs 記住））、`Scripts/CameraLensBar.cs`（快門上方的鏡頭列：魚眼｜1× 2× 3×，執行時建立，設計文件 5.27）、`Scripts/CameraUIManager.cs`（首頁／相機／相簿／聊天模式切換）
- 魚眼（5.27 ③；11-08 改成全片幅魚眼，沒有圓形暗角）：shader `Assets/Resources/Shaders/UIFisheye.shader`＋`Scripts/UI/FisheyeImage.cs`（套在 RawImage 上）；照片存原圖＋`PhotoMeta.fisheye` 標記（同步到營地），辨識用原圖；照片貼紙、最新貼紙、相簿縮圖、拍立得（先裁正方形、套顏色濾鏡，再套魚眼；下載的圖也有）顯示魚眼版
- 相機模式的按鈕：沿用首頁舊按鈕物件 HomePage / Button(album)、Button(chat)，由 `CameraUIManager` 換圖示和功能：Button(album)＝關閉相機（拍完是重拍），Button(chat)＝翻轉鏡頭。使用中（2026-11-05 確認）；首頁模式下這兩顆不再使用

### 拍立得分割畫面（設計文件 5.26，2026-11-07，11-08 改版；取代 5.16 分割畫面與 5.14 ⑥ 照片檢視）
- Scene：無（執行時自動建立，sortingOrder 37）
- 主要 Prefab：無；素材 `Assets/Art/UI/Polaroid/Sprites/`（匯入設定 `Editor/TopBar/TopBarSpriteImporter.cs`）→ `Resources/UI/PolaroidSkin.asset`（`Scripts/UI/Polaroid/PolaroidSkin.cs`，由 `Editor/Polaroid/PolaroidSkinBuilder.cs` 自動填）；規格 `docs/polaroid/`
- 主要腳本：`Scripts/UI/Polaroid/PolaroidScreen.cs`（入口 `Open`／`OpenToday`／`OpenLatest`／`OpenPhoto`、上方一排（誰拍的、類別小標籤、分享、下載）、卡片與右下編輯鈕、照片左下留言、留言框與標題輸入、鍵盤）、`PolaroidScreen.Panel.cs`（下方面板兩頁：寵物頁（營地、愛心、進化條、跳／歪頭）／裝飾頁（貼紙、畫框、濾鏡小分頁），左右滑換頁、頁數小點、下載與分享）、`PolaroidScreen.Stickers.cs`（卡片上的貼紙：貼、選取、拖、撕、寵物貼紙輕晃）、`PolaroidCard.cs`（卡片與貼紙的樣子，畫面和下載共用）、`PolaroidPhoto.cs`（裁正方形、套濾鏡）、`PolaroidExport.cs`（下載圖 1062 × 1293）、`PhotoComments.cs`（照片留言＝聊天室回覆這張照片，原本在 PhotoSplitView）
- 規則：`Scripts/Polaroid/PolaroidRules.cs`（獨立組件 `BlockPet.Polaroid`：每人貼紙上限、誰能換畫框／濾鏡／標題、貼上位置、標題字數、畫框清單、3 種濾鏡（萊卡暗角、富士底片、Y2K 日期戳）、再點一次取消、CSS 濾鏡算法）；待決 35 的暫定值都在這裡
- 資料：`Scripts/FirebaseManager.Polaroid.cs`，`Rooms/{room}/polaroid/{照片鍵}/style`（畫框、濾鏡、標題）與 `/stickers/{id}`
- 入口：首頁最新照片貼紙（`HandController.Stickers`）、相簿點照片（`AlbumScreenController`；舊相簿格子 `PhotoItemClick` → `AlbumUIManager`）、聊天室右上「營地照片」與餵食照片下方「到營地看這張 ›」（`ChatScreenController`）；手機桌面小工具、推播還沒有點照片的入口
- 截圖：Editor 選單 BlockPet/Debug/拍立得截圖（`Assets/Editor/Polaroid/PolaroidSnapshot.cs`，可 batchmode；輸出 Logs/polaroid_*.png 與下載圖 polaroid_export.jpg）
- 2026-11-07 刪除：`Scripts/Hand/PhotoSplitView.cs`、`Editor/TopBar/SplitViewBuilder.cs`、`Scripts/UI/Album/PhotoViewScreen.cs`、`PhotoViewSkin.cs`（含 `Resources/UI/PhotoViewSkin.asset`）、`Editor/Album/PhotoViewSkinBuilder.cs`、`Scripts/UI/Album/PhotoDetailUIPage.cs`（場景裡的 PhotoDetailPage 物件一起刪）
- 舊版：`Scripts/BulletinManager.cs`（7 張照片看板，已不使用；2026-11-05 已在場景裡把 BulletinPhoto 物件和腳本都關掉）

### 相簿
- Scene：Feed.unity（AlbumPage）
- 主要 Prefab：`Assets/Prefabs/PhotoItem.prefab`、`Assets/Prefabs/DateHeader 1.prefab`（舊版相簿用，新版是否還用待確認）；`PhotoItem 1.prefab`、`DateHeader.prefab` 沒有被引用，待確認
- 主要腳本：`Scripts/UI/Album/AlbumScreenController.cs`（點照片開拍立得分割畫面）、`Scripts/UI/Album/PhotoExport.cs`（圖片存相簿、分享；拍立得下載用）
- 舊版：`Scripts/AlbumUI.cs`、`Scripts/AlbumUI_Legacy.cs`、`Scripts/UI/Album/AlbumUIManager.cs`、`Scripts/UI/Album/PhotoItemClick.cs`（舊相簿格子點照片，改開拍立得）、`Scripts/AlbumPanelToggle.cs`（已不使用：首頁相簿按鈕已拿掉，`CameraUIManager` 執行時會把它關掉）；`Resources/UI/watermark_blockpet.bytes`（舊照片檢視的浮水印，已不使用）

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
- 分頁：家具／環境／穿搭／寵物蛋（2026-11-06：裝飾、道具分頁刪除；11-07 新增穿搭）
- 主要腳本：`Scripts/UI/Market/MarketScreenController.cs`、`Scripts/UI/Market/MarketCatalog.cs`（商品與價格；穿搭從設定檔讀）、`Scripts/UI/Market/MarketInventoryStore.cs`（舊版配件／裝備，新版商店沒用到）；舊版相關：`MarketPageController.cs`、`MarketUIController.cs`、`ShopGridController.cs`、`Scripts/UI/Market/WebView/MarketWebViewBridge.cs`、`Art/UI/Market_UI/MarketScreen.uxml`，哪些還在用待確認

### 寵物穿搭（設計文件 5.11 ⑧，2026-11-07）
- Scene：無（商店分頁、首頁寵物身上的穿搭都是執行時建立）
- 品項與價格：`Assets/Resources/Shop/outfits.json`（設定檔，暫定價格改這裡；部位 head／face／neck／side、months＝限定月份）
- 規則：`Scripts/Outfit/OutfitRules.cs`（獨立組件 `BlockPet.Outfit`：部位、同部位只能穿一件、限定月份、商店狀態；只是打扮，沒有任何數值）
- 資料：買過的記在 `Rooms/{room}/shop`（同家具）；正在穿的 `Rooms/{room}/outfit/{部位}`（`Scripts/FirebaseManager.Outfit.cs`，待決 36 暫定營地共用）
- 畫面：商店穿搭分頁（`MarketScreenController`：試穿預覽、穿上／脫下來）；首頁寵物身上 `Scripts/UI/Home/OutfitHomeOverlay.cs`（執行時自動建立，跟著寵物位置、翻面）；佔位圖 `Scripts/UI/Market/OutfitArt.cs`（正式美術還沒出）
- 和 11-06 刪除的「從照片撿裝飾」無關

### 佈置家具（設計文件 5.28 佈置營地：斜角格子）
- Scene：Feed.unity（`RoomEditorController` 掛在 HomePage；畫面執行時建立，sortingOrder 12）；入口是右上選單「佈置」（`MenuToggle`）和商店「去擺放」
- 操作：按住家具拖（面板卡片往上拖到營地），吸附格子，白框放得下、紅框放不下；也能點家具再點綠格；場上家具往下拖到面板＝收起來（`DecorationManager.PutAway`）；家具效果要用 `DecorationManager.CountOnCamp`（收起來的不算）
- 佈置時收起狀態欄、選單、照片貼紙、底部按鈕：`Assets/Data/DecorModeSettings.asset`
- 主要 Prefab：`Assets/Art/UI/Mainroom_UI/DecorationObject.prefab`
- 主要腳本：`Scripts/Decoration/DecorationManager.cs`（`PlaceOnGrid`／`MoveOnGrid`、編輯鎖）、`Scripts/Decoration/DecorationObject.cs`、`Scripts/Decoration/DecorationDatabase.cs`、`Scripts/Decoration/DecorationLayout.cs`（家具在格子上的位置、大小、前後、影子）、`Scripts/UI/Home/RoomEditorController.cs`（佈置畫面、拖曳與點選流程）、`Scripts/UI/TopBar/DecorModeSettings.cs`
- 格子規則：`Scripts/Decoration/Grid/CampGridRules.cs`（獨立組件 `BlockPet.CampGrid`：`GridMath` 座標、底座、放得下、前後、圖片位置；測試 `Tests/EditMode/CampGridRulesTests.cs`）
- 格子表：`Scripts/Decoration/Grid/CampGrid.cs`，檔案 `Assets/Resources/Camp/CampGrid_forest.asset`；在 Unity 選到它，在 Scene 畫面用滑鼠點格子標「存在／不能放／擴建」（`Assets/Editor/Decorate/CampGridEditor.cs`，選單 BlockPet/Decorate/Create Forest Camp Grid）
- 家具底座：`DecorationItem` 的 `footprintW`／`footprintD`／`hangPoint`（`Assets/Data/Market/furn_*.asset`）
- 素材：`Assets/Resources/UI/DecorateSkin.asset`（`Assets/Editor/Decorate/DecorateSkinBuilder.cs` 從 `Assets/Art/UI/Decorate/Sprites/` 自動填）
- 資料：`Rooms/{room}/decorations/{id}` 多了 `gi`、`gj`（底座最上面那格）；沒有格子座標的舊家具不顯示，佈置時列在「還沒擺出來的家具」
- 寵物和家具前後用同一套：`AIController.SortOrderForY`
- 舊的自由擺放（`DecorationPickerController`、`DecorationPicker.uxml/.uss`）2026-11-08 已刪除
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

### 寄明信片（設計文件 5.21，2026-11-08 取代旅行）
- Scene：無（執行時自動建立，sortingOrder 38）；入口 `MenuToggle` 的「旅行」按鈕（選單的字維持「旅行」）
- 素材：`Assets/Resources/UI/TravelSkin.asset`（地圖，`Assets/Editor/Travel/TravelSkinBuilder.cs`）＋`Assets/Resources/UI/PostcardSkin.asset`（抽好友、選照片、護照，`Assets/Editor/Postcard/PostcardSkinBuilder.cs` 從 `Assets/Art/UI/Postcard/Sprites/` 自動填）
- 地圖：`Scripts/Travel/TravelMapGen.cs`（照設計稿 TravelGrow 移植；獨立組件 `BlockPet.Travel`）、`Scripts/UI/Travel/TravelScreen.cs`（地圖＋頂部「明信片地圖」、底部護照與狀態卡、好友營地面板）、`Scripts/UI/Travel/MapCurveGraphic.cs`；地圖種子＝營地代碼（`FirebaseManager.EnsureCampCode`，不再顯示）
- 規則：`Scripts/Travel/PostcardRules.cs`（抽好友次數與價格、台灣時間換日、好友上限與解鎖、配對、等確認／回寄期限、郵票郵戳）、`Scripts/Travel/TripRules.cs`（路程時間）、`Scripts/Travel/CampFriendRules.cs`（地圖格子）；數字在 `Assets/Resources/Postcard/postcard_settings.json`
- 抽好友：`Scripts/UI/Travel/TravelScreen.Draw.cs`（抽好友按鈕、配對卡、等對方確認、有營地想加你們、解鎖、封鎖・檢舉）；資料 `CampProfiles/{營地}`、`Rooms/{room}/matchIn`、`matchOut`、`postcardMeta`、`blocked`、`blockedBy`、`Reports`（`Scripts/FirebaseManager.Postcard.cs`）
- 寄明信片：`Scripts/UI/Travel/TravelScreen.Postcard.cs`（選照片、人臉、裝飾過的拍立得、寄出、護照、放大看）；拍立得寄出模式 `Scripts/UI/Polaroid/PolaroidScreen.Send.cs`；資料 `Rooms/{收件}/postcards/{id}`＋`postcardImg/{id}`（JPG base64）、`Rooms/{寄件}/sent/{id}`；寵物出門沿用 `SendPetOnTrip`（`Rooms/{room}/trip`、`guests`）
- 人臉偵測：`Scripts/Postcard/FaceDetector.cs`＋`Assets/Plugins/iOS/BlockPetFaceDetect.mm`（Vision）＋`Assets/Plugins/Android/BlockPetFaceDetect.java`（ML Kit，依賴 `Assets/Editor/Postcard/PostcardDependencies.xml`）；國家 `Scripts/Postcard/DeviceRegion.cs`
- 首頁：`Scripts/UI/Travel/TravelHomeOverlay.cs`（出門木牌、客人、收到明信片／寵物回家帶回風景明信片的彈出，風景明信片 `PostcardArt.RenderScenery`）
- 推播還沒做（App 內通知）；測試用選單 BlockPet/Debug/寄明信片；地圖截圖 BlockPet/Travel/Snapshot Map

### 進化與家具愛好（設計文件 5.17、5.18 ⑤，2026-11-14）
- 規則：`Scripts/Economy/EvolutionRules.cs`（調色盤、三種型、主色副色、80% 預告句）、`Scripts/Economy/FurnitureLikeRules.cs`（喜歡哪些家具、什麼時候走過去）；測試 `EvolutionRulesTests.cs`、`FurnitureLikeRulesTests.cs`
- 規則表：`Assets/Resources/Pets/furniture_likes.json`（品種 → 主色／rainbow／juvenile → 家具、互動方式、比重；走過去的機率、停留、冷卻）
- 資料：`Rooms/{room}/evolution`（`Scripts/FirebaseManager.Evolution.cs`，交易寫入：誰先點誰決定）
- 畫面：`Scripts/UI/Home/EvolutionFlow.cs`（點寵物／進化條進化、動畫、補播、80% 剪影；執行時自動建立）、`Scripts/UI/Home/EvolvedLook.cs`（進化後上主色，暫用）、`Scripts/Hand/HandController.Pet.cs`（「可以進化了！」、預告句、家具旁的泡泡）、`Scripts/UI/TopBar/EvoBarView.cs`（進化後「成長紀錄」）
- 走去喜歡的家具：`Scripts/Decoration/PetFurnitureVisits.cs`（掛在寵物上，用 `AIController.TryDirectedIdle`）；商店紅愛心在 `MarketScreenController`
- 測試用：Editor 選單 BlockPet/Debug/進化（`Assets/Editor/Pets/EvolutionDebugMenu.cs`：模擬 80%、滿 100 張三種型、清掉進化結果）

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
- 主要腳本：`Assets/Tests/EditMode/`（`BlockPet.Tests.EditMode.asmdef`，引用 `BlockPet.Economy`、`BlockPet.Travel`、`BlockPet.Home`、`BlockPet.Polaroid`、`BlockPet.Outfit`）：`SampleTest.cs`、`EconomyRulesTests.cs`（經濟規格第 2 節驗收條件）、`WantScheduleTests.cs`（現在想吃時段）、`DexRulesTests.cs`（圖鑑規則）、`TravelMapGenTests.cs`（旅行地圖生成，期望值用設計稿原程式算）、`CampFriendRulesTests.cs`（地圖格子）、`PostcardRulesTests.cs`（寄明信片）、`TripRulesTests.cs`（旅行時間與階段）、`StickerRulesTests.cs`（首頁照片貼紙）、`PolaroidRulesTests.cs`（拍立得規則與濾鏡）、`OutfitRulesTests.cs`（穿搭部位、限定月份、商店狀態）；執行 `scripts/run-tests.sh`

## 規劃中、專案裡找不到程式的功能

| 功能 | 狀態 |
|---|---|
| 進化分支 | 待確認（找不到；`PetCollectionManager` 只有進化能量） |
| 抽蛋 | 待確認（找不到） |
| 推播 | 有 `Editor/com.unity.mobile.notifications` 設定資料夾；遊戲腳本待確認（找不到） |
