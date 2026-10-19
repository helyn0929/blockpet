# BlockPet 首頁頂部狀態欄 — Unity 素材包

**依據：設計稿 artifact「Blockpet 簡化版遊戲畫面」→ 頁面「首頁 v8 照片分割」→ Camp8。**
每張圖都是直接用設計稿的 CSS 在瀏覽器裡以 3 倍解析度切出來的，所以顏色、圓角、邊框、陰影跟設計稿一模一樣。
尺寸是從設計稿量出來的（`blockpet_topbar_config.json`、`Preview/topbar_redline.png`）。
標「補充」的是設計稿沒畫、依設計文件規則補的狀態，風格沿用設計稿。

驗證方式：只用這些 PNG＋config 的座標重新拼一次狀態欄，和設計稿比對，差異只有毛玻璃模糊（見下面「毛玻璃」）。

## 放到專案

```
Assets/Art/UI/TopBar/Sprites/   ← Sprites/*.png
Assets/Data/blockpet_topbar_config.json
```
`Source/` 是每張圖的 HTML 原稿（改設計稿 CSS 後可用 `Tools/make_sprites.py` 重切，需要 Python＋Playwright）。不用匯入 Unity。

## 為什麼是 PNG

Unity UI 的 Image 不能直接吃 SVG（要另裝套件，且不支援 9-slice）。PNG＋9-slice 是 Unity 的標準做法。

## 匯入設定

- Texture Type：Sprite (2D and UI)；Pixels Per Unit：300（素材是 @3x）；Mesh Type：Full Rect
- Filter：Bilinear；Compression：None；Mip Maps：關
- Canvas Scaler：Scale With Screen Size，Reference 390 × 772（設計稿畫框尺寸），Match 0.5
- 9-slice（Sprite Editor → Border，單位 px），Image Type 設 Sliced：

| 檔案 | Border L,B,R,T | 用途 |
|---|---|---|
| ui_topbar_panel | 87,87,87,87 | 狀態欄底板 370×58 |
| ui_topbar_panel_shadow | 159,159,159,159 | 陰影，比底板四邊各大 24 |
| ui_lv_badge | 18,18,18,18 | Lv 徽章底，寬隨文字 |
| ui_pill_17 | 25,25,25,25 | 想吃膠囊（白色，用 Image.color 上色） |
| ui_evo_track | 12,12,12,12 | 進化條軌道 |
| ui_evo_mask | 12,12,12,12 | 進化條遮罩 |
| ui_evo_ready_glow | 54,54,54,54 | 補充：滿條金框，比條四邊各大 14 |
| ui_coin_pill | 45,45,45,45 | 金幣按鈕底 |

其餘（頭像底、愛心、金幣、＋鈕、選單鈕、圖示）用 Simple。

## 物件階層與座標（@1x，相對狀態欄左上角）

```
TopBar                       畫面 (10,10)，370×58
├─ Shadow                    ui_topbar_panel_shadow，(-24,-24) 418×106
├─ [Blur]                    可選，見「毛玻璃」
├─ Panel                     ui_topbar_panel，(0,0) 370×58
├─ Avatar                    (7,7) 44×44
│  ├─ Bg                     ui_avatar_bg
│  ├─ Mask                   ui_avatar_mask＋Mask
│  │  └─ PetImage            (2,5) 40×34，Preserve Aspect
│  └─ LvBadge                ui_lv_badge，高 15，寬 = 字寬＋13；水平置中、下緣比頭像低 5
│     └─ Text                「Lv10」9pt Black #FFF8EC
├─ Row1                      x 59，y 9.5，高 17（HorizontalLayout，間距 6，不撐開）
│  ├─ Name                   TMP 13 Black #3A2E22
│  ├─ Hearts                 HorizontalLayout 間距 1；5 × (15×14) ui_heart_full / half / empty
│  └─ WantChip               ui_pill_17（Image.color＝類別底色），左右內距 8
│     └─ Text                「想吃 美食」11 Black，類別字色
├─ Row2                      x 59，y 32.5，高 16（HorizontalLayout，間距 6）
│  ├─ EvoBar                 Flexible 寬，高 8（此例寬 160.5）
│  │  ├─ Track               ui_evo_track
│  │  ├─ Fill                ui_evo_mask＋Mask（不顯示遮罩圖）
│  │  │  └─ Seg × 5          ui_evo_segment，Image.color＝類別色，段與段間隔 1
│  │  └─ ReadyGlow           補充：ui_evo_ready_glow，平常關閉
│  └─ EvoText                「進化 68%」11 Black
└─ CoinButton                靠右 8，y 14，高 30，寬隨數字（此例 78.8）
   ├─ Bg                     ui_coin_pill
   ├─ Dot                    ui_coin_dot 14×14（左 7、上 8）
   ├─ Number                 TMP 13 Black，接在金幣後 4
   └─ Plus                   ui_coin_plus 22×24（右 4、上 4）

MenuToggle                   畫面右 10、y 80；46×32
├─ Bg                        ui_menu_btn_closed / ui_menu_btn_open（含陰影，四邊外擴 12 → 70×56）
└─ Chevron                   ui_icon_chevron 16×16，展開時旋轉 180°（0.25s）
MenuItems（展開時）          往下排，間距 8：旅行、圖鑑、設定
└─ Tile                      ui_menu_tile（46×46，含陰影外擴 12）＋ ui_icon_* 22×22＋下方標籤 11 Black 白字加陰影
```

中欄寬 = 370 − 6 − 44 − 8 − 8 − 金幣按鈕寬 − 8。

## 顏色

- 文字 #3A2E22；Lv 徽章底 #3A2E22、字與邊 #FFF8EC
- 愛心 滿 #F06B5B／空 #E6D9C3
- 色段（固定順序）：美食 #F29A3A、自然 #6FA45F、城市 #6F8FE0、生活人物 #D78AD0、運動戶外 #E8C33A
- 想吃膠囊：美食 底 #FFE3C7 字 #A4511B；生活人物 底 #F6E2F4 字 #8A3F84（設計稿）；自然、城市、運動戶外為補充色，見 config

## 進化條行為（設計稿）

- 條總長代表 100 能量；每段寬 = 該類能量 %。文字「進化 N%」，N = 總能量（最多 100）。
- 色段寬度變化用 0.6 秒 ease 動畫。
- 命中「今天想吃」：該類 +12、金幣 +5×愛心乘數，跳出「好好吃！ 命中喜好・進化 +12」（#E0662E）。
- 沒命中：照片自己的類別 +3、金幣 +1，跳出「嗯…？ 想吃美食・進化 +3」（#6A55A8）。
- 愛心：設計稿命中 +1 顆；設計文件規定每餵一張 +0.5 顆。以設計文件為準，等 Helyn 確認。

## 補充狀態（設計稿沒畫，見 Preview/topbar_states.png）

- 半顆心：ui_heart_half
- 愛心 ≤ 1：愛心列 1→1.18→1 每 1.8 秒（沿用設計稿的 c5Pulse）
- 滿條：ReadyGlow 開啟，文字改「可進化！」#C2621A；點進化條或寵物觸發
- 死亡：寵物頭像灰階、色段 #C9BFAF、想吃膠囊改「點我復活」（底 #E86B4F 白字）

## 毛玻璃

設計稿狀態欄和選單鈕有 `backdrop-filter: blur(8px)`，PNG 做不到（模糊的是後面的畫面）。
- 要一模一樣：在 Panel 下面加 UI 模糊（URP 可用 Full Screen Pass＋模糊 Shader，或現成的 UI Blur 套件），套用範圍裁成同樣的膠囊形。
- 不做模糊：只用底板 PNG，看起來會比設計稿清楚一點，其他都一樣。
