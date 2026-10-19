using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BlockPet.UI.TopBar;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build TopBar」：
    /// 1. 重新匯入狀態欄素材（套用 TopBarSpriteImporter 的設定）
    /// 2. 讀 Assets/Data/blockpet_topbar_config.json → Assets/Data/TopBarConfig.asset
    /// 3. 用 NotoSansCJKtc-Black.otf 產生 TMP 字型資產（＋選單標籤用的陰影材質）
    /// 4. 在目前場景建立獨立的 TopBarCanvas（Scale With Screen Size 390×772、Match 0.5），
    ///    照 docs/topbar/README_給ClaudeCode.md 的物件階層與座標建出狀態欄與選單鈕
    /// 5. 存成 Assets/Prefabs/UI/TopBar.prefab
    /// 狀態欄用自己的 Canvas，不改既有 Canvas（它是 1080×1920，改了其他畫面會全部變形）。
    /// </summary>
    public static class TopBarBuilder
    {
        const string JsonPath = "Assets/Data/blockpet_topbar_config.json";
        const string ConfigPath = "Assets/Data/TopBarConfig.asset";
        const string DecorModePath = "Assets/Data/DecorModeSettings.asset";
        const string FontPath = "Assets/Art/Fonts/NotoSansCJKtc-Black.otf";
        const string FontAssetPath = "Assets/Art/Fonts/NotoSansCJKtc-Black SDF.asset";
        const string LabelMaterialPath = "Assets/Art/Fonts/NotoSansCJKtc-Black SDF - Label Shadow.mat";
        const string PrefabPath = "Assets/Prefabs/UI/TopBar.prefab";
        const string PlaceholderPetPath = "Assets/Art/Characters/Pets/shiba.png";
        const string CanvasName = "TopBarCanvas";
        const string HudName = "TopBarHUD";

        // 先放進字型的字（其他字在執行時動態補）
        const string PrewarmChars = "BowLv0123456789+%！？…・想吃美食自然城市生活人物運動戶外進化可點我復活旅行圖鑑設定 ";

        [MenuItem("BlockPet/Build TopBar")]
        public static void Build()
        {
            TopBarSpriteImporter.ReimportAll();

            var config = LoadOrCreateConfig();
            if (!ImportJson(config)) return;
            AssignSprites(config);
            config.font = LoadOrCreateFont();
            config.labelShadowMaterial = LoadOrCreateLabelMaterial(config.font);
            config.decorMode = LoadOrCreateDecorMode();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            if (config.font == null)
            {
                EditorUtility.DisplayDialog("Build TopBar", "找不到字型 " + FontPath, "OK");
                return;
            }

            var canvas = FindOrCreateCanvas(config);
            var old = canvas.transform.Find(HudName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var hud = BuildHud(canvas.transform, config);
            RemoveReplacedButtons();
            AssignDecorModeToHands(config.decorMode);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAssetAndConnect(hud, PrefabPath, InteractionMode.UserAction);
            EditorSceneManager.MarkSceneDirty(hud.scene);
            Selection.activeGameObject = hud;
            EditorGUIUtility.PingObject(hud);
            Debug.Log($"[TopBar] 完成：場景 {CanvasName}/{HudName}，prefab {PrefabPath}，設定 {ConfigPath}");
        }

        // ─── 設計文件 5.7：Home 鍵取代 Back、選單「佈置」取代床圖示 ─────

        /// <summary>佈置模式隱藏設定（預設全部不隱藏，尚未定案）。已存在就沿用使用者的勾選。</summary>
        static DecorModeSettings LoadOrCreateDecorMode()
        {
            var s = AssetDatabase.LoadAssetAtPath<DecorModeSettings>(DecorModePath);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<DecorModeSettings>();
            AssetDatabase.CreateAsset(s, DecorModePath);
            return s;
        }

        /// <summary>刪除被 Home 鍵、選單「佈置」取代的舊按鈕（可用 Undo 復原）。</summary>
        static void RemoveReplacedButtons()
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != "Canvas") continue;
                foreach (var path in new[] { "MainGame/HomePage/Button(back)", "MainGame/HomePage/Button(editmode)" })
                {
                    var t = root.transform.Find(path);
                    if (t == null) continue;
                    Undo.DestroyObjectImmediate(t.gameObject);
                    Debug.Log("[TopBar] 已刪除舊按鈕 Canvas/" + path);
                }
            }
        }

        static void AssignDecorModeToHands(DecorModeSettings settings)
        {
            foreach (var hand in Object.FindObjectsOfType<BlockPet.Hand.HandController>(true))
            {
                var so = new SerializedObject(hand);
                var p = so.FindProperty("decorMode");
                if (p == null) continue;
                p.objectReferenceValue = settings;
                so.ApplyModifiedProperties();
            }
        }

        // ─── Config ────────────────────────────────────────────────────

        static TopBarConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<TopBarConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<TopBarConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        static bool ImportJson(TopBarConfig c)
        {
            if (!File.Exists(JsonPath))
            {
                EditorUtility.DisplayDialog("Build TopBar", "找不到 " + JsonPath, "OK");
                return false;
            }
            var root = MiniJson.Parse(File.ReadAllText(JsonPath)) as Dictionary<string, object>;
            if (root == null)
            {
                EditorUtility.DisplayDialog("Build TopBar", JsonPath + " 格式錯誤", "OK");
                return false;
            }

            c.referenceResolution = Vec2(Get(root, "referenceResolution"), c.referenceResolution);
            c.textColor = Col(Get(root, "font", "textColor"), c.textColor);

            var panel = Obj(root, "layout_1x_relativeToPanel", "panel");
            if (panel != null)
            {
                c.panelScreenPos = new Vector2(Num(panel, "screenX", c.panelScreenPos.x), Num(panel, "screenY", c.panelScreenPos.y));
                c.panelSize = new Vector2(Num(panel, "w", c.panelSize.x), Num(panel, "h", c.panelSize.y));
                c.panelGap = Num(panel, "gap", c.panelGap);
            }

            Rect R(string key, Rect fallback) => RectOf(Get(root, "layout_1x_relativeToPanel", key), fallback);
            var avatar = R("avatar", c.avatar);
            c.avatar = avatar;
            var img = R("avatarImage", new Rect(9, 12, 40, 34));
            c.avatarImage = new Rect(img.x - avatar.x, img.y - avatar.y, img.width, img.height);
            var lv = R("lvBadge", new Rect(12.38f, 41, 33.23f, 15));
            c.lvBadgeHeight = lv.height;
            c.lvBadgeBelowAvatar = lv.yMax - avatar.yMax;

            var name = R("name", new Rect(59, 9.5f, 57.67f, 17));
            c.rowX = name.x;
            c.row1Y = name.y;
            c.row1Height = name.height;
            var hearts = R("hearts", new Rect(122.67f, 11, 79, 14));
            c.rowGap = hearts.x - name.xMax;
            var evoBar = R("evoBar", new Rect(59, 36.5f, 160.47f, 8));
            c.evoHeight = evoBar.height;
            var evoText = R("evoText", new Rect(225.47f, 32.5f, 48.77f, 16));
            c.row2Y = evoText.y;
            c.row2Height = evoText.height;

            var coin = R("coinButton", new Rect(282.23f, 14, 78.77f, 30));
            c.coinY = coin.y;
            c.coinHeight = coin.height;
            c.coinRight = c.panelSize.x - coin.xMax;
            var dot = R("coinDot", new Rect(289.23f, 22, 14, 14));
            c.coinDot = new Rect(dot.x - coin.x, dot.y - coin.y, dot.width, dot.height);
            var num = R("coinNumber", new Rect(307.23f, 19.5f, 23.77f, 19));
            c.coinNumber = new Vector3(num.x - coin.x, num.y - coin.y, num.height);
            var plus = R("coinPlus", new Rect(335, 18, 22, 22));
            c.coinNumberToPlus = plus.x - num.xMax;
            c.coinPlusTop = plus.y - coin.y;
            c.coinPlusRight = coin.xMax - plus.xMax;

            var menu = Obj(root, "menuButton");
            if (menu != null)
            {
                c.menuRightMargin = Num(menu, "screenRightMargin", c.menuRightMargin);
                c.menuY = Num(menu, "screenY", c.menuY);
                c.menuSize = new Vector2(Num(menu, "w", c.menuSize.x), Num(menu, "h", c.menuSize.y));
                c.chevronRotateSec = Num(menu, "chevronRotateSec", c.chevronRotateSec);
                var items = Obj(menu, "items");
                if (items != null)
                {
                    c.menuTile = Num(items, "tile", c.menuTile);
                    c.menuGap = Num(items, "gap", c.menuGap);
                    if (items.TryGetValue("order", out var order) && order is List<object> list)
                    {
                        c.menuOrder = new List<string>();
                        foreach (var o in list) c.menuOrder.Add(o as string);
                    }
                }
            }

            var home = Obj(root, "homeButton");
            if (home != null)
            {
                c.homeLeftMargin = Num(home, "screenLeftMargin", c.homeLeftMargin);
                c.homeY = Num(home, "screenY", c.homeY);
                c.homeSize = new Vector2(Num(home, "w", c.homeSize.x), Num(home, "h", c.homeSize.y));
                c.homeIconSize = Num(home, "iconSize", c.homeIconSize);
            }
            var theme = Obj(root, "todayTheme");
            if (theme != null) c.todayThemeStartY = Num(theme, "startY", c.todayThemeStartY);

            var sizes = Obj(root, "fontSizes");
            if (sizes != null)
            {
                c.nameSize = Num(sizes, "name", c.nameSize);
                c.lvSize = Num(sizes, "lv", c.lvSize);
                c.wantSize = Num(sizes, "want", c.wantSize);
                c.evoTextSize = Num(sizes, "evoText", c.evoTextSize);
                c.coinSize = Num(sizes, "coin", c.coinSize);
            }

            var h = Obj(root, "hearts");
            if (h != null)
            {
                c.heartSize = Vec2(Get(h, "size"), c.heartSize);
                c.heartGap = Num(h, "gap", c.heartGap);
            }

            var evo = Obj(root, "evoBar");
            if (evo != null)
            {
                c.evoHeight = Num(evo, "height", c.evoHeight);
                c.evoSegmentGap = Num(evo, "segmentGap", c.evoSegmentGap);
                c.evoTransitionSec = Num(evo, "widthTransitionSec", c.evoTransitionSec);
                if (Get(evo, "text") is string fmt) c.evoTextFormat = fmt;
                var colors = Obj(evo, "segmentColors");
                if (Get(evo, "segmentOrder") is List<object> segOrder && colors != null)
                {
                    c.segments = new List<TopBarConfig.CategoryColor>();
                    foreach (var o in segOrder)
                    {
                        string cat = o as string;
                        c.segments.Add(new TopBarConfig.CategoryColor { category = cat, color = Col(Get(colors, cat), Color.gray) });
                    }
                }
            }

            var want = Obj(root, "wantChip");
            if (want != null)
            {
                c.wantHeight = Num(want, "height", c.wantHeight);
                c.wantPaddingX = Num(want, "paddingX", c.wantPaddingX);
                var colors = Obj(want, "colors");
                if (colors != null)
                {
                    c.wantColors = new List<TopBarConfig.ChipColor>();
                    foreach (var kv in colors)
                    {
                        if (!(kv.Value is Dictionary<string, object> cc)) continue;
                        c.wantColors.Add(new TopBarConfig.ChipColor
                        {
                            category = kv.Key,
                            bg = Col(Get(cc, "bg"), Color.white),
                            fg = Col(Get(cc, "fg"), c.textColor),
                        });
                    }
                }
            }

            // 補充狀態：文字規格，抓出其中的數字與顏色
            if (Get(root, "supplementary", "ready", "text") is string ready)
            {
                c.readyText = Regex.Replace(ready, @"#[0-9A-Fa-f]{6}", "").Trim();
                c.readyTextColor = Col(FirstHex(ready), c.readyTextColor);
            }
            if (Get(root, "supplementary", "lowHearts", "when") is string when)
                c.lowHeartsThreshold = FirstNumber(when, c.lowHeartsThreshold);
            if (Get(root, "supplementary", "lowHearts", "anim") is string anim)
            {
                var m = Regex.Match(anim, @"1\s*→\s*([\d.]+)\s*→\s*1");
                if (m.Success) c.lowHeartsPulseScale = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var p = Regex.Match(anim, @"([\d.]+)\s*s");
                if (p.Success) c.lowHeartsPulsePeriod = float.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            if (Get(root, "supplementary", "dead", "avatar") is string deadAvatar)
                c.deadAvatarGray = FirstNumber(deadAvatar, c.deadAvatarGray * 100f) / 100f;
            if (Get(root, "supplementary", "dead", "segments") is string deadSeg)
                c.deadSegmentColor = Col(FirstHex(deadSeg), c.deadSegmentColor);
            if (Get(root, "supplementary", "dead", "wantChip") is string deadChip)
            {
                var t = Regex.Match(deadChip, "「(.+?)」");
                if (t.Success) c.deadChipText = t.Groups[1].Value;
                c.deadChipBg = Col(FirstHex(deadChip), c.deadChipBg);
                c.deadChipFg = deadChip.Contains("白字") ? Color.white : c.textColor;
            }
            return true;
        }

        static void AssignSprites(TopBarConfig c)
        {
            Sprite S(string n)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(TopBarSpriteImporter.Folder + n + ".png");
                if (s == null) Debug.LogWarning("[TopBar] 找不到素材 " + n);
                return s;
            }
            c.panel = S("ui_topbar_panel");
            c.panelShadow = S("ui_topbar_panel_shadow");
            c.avatarBg = S("ui_avatar_bg");
            c.avatarMask = S("ui_avatar_mask");
            c.lvBadge = S("ui_lv_badge");
            c.heartFull = S("ui_heart_full");
            c.heartHalf = S("ui_heart_half");
            c.heartEmpty = S("ui_heart_empty");
            c.pill17 = S("ui_pill_17");
            c.evoTrack = S("ui_evo_track");
            c.evoMask = S("ui_evo_mask");
            c.evoSegment = S("ui_evo_segment");
            c.evoReadyGlow = S("ui_evo_ready_glow");
            c.coinPill = S("ui_coin_pill");
            c.coinDotSprite = S("ui_coin_dot");
            c.coinPlus = S("ui_coin_plus");
            c.menuClosed = S("ui_menu_btn_closed");
            c.menuOpen = S("ui_menu_btn_open");
            c.chevron = S("ui_icon_chevron");
            c.menuTileSprite = S("ui_menu_tile");
            c.iconTravel = S("ui_icon_travel");
            c.iconDex = S("ui_icon_dex");
            c.iconSettings = S("ui_icon_settings");
            c.iconDecor = AssetDatabase.LoadAssetAtPath<Sprite>(TopBarSpriteImporter.IconsFolder + "icon_bed@3x.png");
            c.iconHome = AssetDatabase.LoadAssetAtPath<Sprite>(TopBarSpriteImporter.IconsFolder + "icon_home@3x.png");
            if (c.iconDecor == null) Debug.LogWarning("[TopBar] 找不到 " + TopBarSpriteImporter.IconsFolder + "icon_bed@3x.png");
            if (c.iconHome == null) Debug.LogWarning("[TopBar] 找不到 " + TopBarSpriteImporter.IconsFolder + "icon_home@3x.png");
        }

        // ─── Font ──────────────────────────────────────────────────────

        public static TMP_FontAsset LoadOrCreateFont()
        {
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fa == null)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
                if (font == null) return null;
                fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                fa.name = Path.GetFileNameWithoutExtension(FontAssetPath);
                AssetDatabase.CreateAsset(fa, FontAssetPath);
                fa.atlasTextures[0].name = fa.name + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = fa.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            fa.TryAddCharacters(PrewarmChars);
            EditorUtility.SetDirty(fa);
            return fa;
        }

        /// <summary>選單標籤「白字加陰影」：複製字型材質，開 Underlay（對應 CSS text-shadow 0 1px 3px rgba(0,0,0,.75)）。</summary>
        public static Material LoadOrCreateLabelMaterial(TMP_FontAsset font)
        {
            if (font == null) return null;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(LabelMaterialPath);
            if (mat == null)
            {
                mat = new Material(font.material) { name = Path.GetFileNameWithoutExtension(LabelMaterialPath) };
                AssetDatabase.CreateAsset(mat, LabelMaterialPath);
            }
            mat.CopyPropertiesFromMaterial(font.material);
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.75f));
            mat.SetFloat("_UnderlayOffsetX", 0f);
            mat.SetFloat("_UnderlayOffsetY", -0.5f);
            mat.SetFloat("_UnderlayDilate", 0.1f);
            mat.SetFloat("_UnderlaySoftness", 0.6f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ─── Canvas ────────────────────────────────────────────────────

        static Canvas FindOrCreateCanvas(TopBarConfig c)
        {
            var scene = EditorSceneManager.GetActiveScene();
            Canvas canvas = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == CanvasName) { canvas = go.GetComponent<Canvas>(); break; }

            if (canvas == null)
            {
                var go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                go.layer = LayerMask.NameToLayer("UI");
                Undo.RegisterCreatedObjectUndo(go, "Build TopBar");
                canvas = go.GetComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // 畫在既有 Canvas（首頁、手牌）上面
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = c.referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = c.matchWidthOrHeight;
            scaler.referencePixelsPerUnit = 100;
            return canvas;
        }

        // ─── Hierarchy（README「物件階層與座標」，@1x，相對狀態欄左上角）────

        static GameObject BuildHud(Transform canvas, TopBarConfig c)
        {
            var hud = Stretch(canvas, HudName);
            var hudGroup = hud.gameObject.AddComponent<CanvasGroup>();
            var view = hud.gameObject.AddComponent<TopBarView>();

            // TopBar：畫面 (10,10)，370×58；左右各留 10 隨螢幕寬度伸縮（中欄吸收差異）
            var bar = NewRect(hud, "TopBar");
            var barGroup = bar.gameObject.AddComponent<CanvasGroup>();
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0f, 1f);
            bar.offsetMin = new Vector2(c.panelScreenPos.x, -c.panelScreenPos.y - c.panelSize.y);
            bar.offsetMax = new Vector2(-c.panelScreenPos.x, -c.panelScreenPos.y);

            var shadow = Stretch(bar, "Shadow", c.panelShadowOutset);
            AddImage(shadow, c.panelShadow, sliced: true, raycast: false);
            var panel = Stretch(bar, "Panel");
            AddImage(panel, c.panel, sliced: true, raycast: true); // 擋住點到後面的營地

            // Avatar
            var avatar = Node(bar, "Avatar", c.avatar);
            AddImage(Stretch(avatar, "Bg"), c.avatarBg, false, false);
            var mask = Stretch(avatar, "Mask");
            AddImage(mask, c.avatarMask, false, false);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var avatarBtn = avatar.gameObject.AddComponent<Button>();
            avatarBtn.targetGraphic = avatar.GetChild(0).GetComponent<Image>();
            avatar.GetChild(0).GetComponent<Image>().raycastTarget = true;
            UnityEventTools.AddPersistentListener(avatarBtn.onClick, view.ClickAvatar);
            var pet = Node(mask, "PetImage", c.avatarImage);
            var petImg = AddImage(pet, AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderPetPath), false, false);
            petImg.preserveAspect = true;

            var lvBadge = NewRect(avatar, "LvBadge");
            lvBadge.anchorMin = lvBadge.anchorMax = new Vector2(0.5f, 0f);
            lvBadge.pivot = new Vector2(0.5f, 0f);
            lvBadge.anchoredPosition = new Vector2(0f, -c.lvBadgeBelowAvatar);
            lvBadge.sizeDelta = new Vector2(33.23f, c.lvBadgeHeight);
            AddImage(lvBadge, c.lvBadge, true, false);
            var lvText = Text(Stretch(lvBadge, "Text"), c, "Lv10", c.lvSize, c.lvTextColor, TextAlignmentOptions.Center);

            // Row1：名字、愛心、想吃膠囊（間距 6）
            var row1 = Node(bar, "Row1", new Rect(c.rowX, c.row1Y, 200, c.row1Height));
            var row1Layout = row1.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureRow(row1Layout, c.rowGap);

            var name = NewRect(row1, "Name");
            name.sizeDelta = new Vector2(60, c.row1Height);
            var nameText = Text(name, c, "BowBow", c.nameSize, c.textColor, TextAlignmentOptions.Left);

            var heartsRt = NewRect(row1, "Hearts");
            heartsRt.sizeDelta = new Vector2(c.maxHearts * c.heartSize.x + (c.maxHearts - 1) * c.heartGap, c.heartSize.y);
            heartsRt.pivot = new Vector2(0.5f, 0.5f);
            var heartsLayout = heartsRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            heartsLayout.spacing = c.heartGap;
            heartsLayout.childAlignment = TextAnchor.MiddleLeft;
            heartsLayout.childControlWidth = heartsLayout.childControlHeight = false;
            heartsLayout.childForceExpandWidth = heartsLayout.childForceExpandHeight = false;
            var heartImages = new Image[c.maxHearts];
            for (int i = 0; i < c.maxHearts; i++)
            {
                var hrt = NewRect(heartsRt, "Heart" + (i + 1));
                hrt.sizeDelta = c.heartSize;
                heartImages[i] = AddImage(hrt, c.heartFull, false, false);
            }
            var heartsView = heartsRt.gameObject.AddComponent<HeartsView>();

            var chip = NewRect(row1, "WantChip");
            chip.sizeDelta = new Vector2(60, c.wantHeight);
            var chipImg = AddImage(chip, c.pill17, true, true);
            var chipLayout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipLayout.padding = new RectOffset(Mathf.RoundToInt(c.wantPaddingX), Mathf.RoundToInt(c.wantPaddingX), 0, 0);
            chipLayout.childAlignment = TextAnchor.MiddleCenter;
            chipLayout.childControlWidth = chipLayout.childControlHeight = true;
            chipLayout.childForceExpandWidth = false;
            chipLayout.childForceExpandHeight = true;
            var wantText = Text(NewRect(chip, "Text"), c, "想吃 美食", c.wantSize, c.textColor, TextAlignmentOptions.Center);
            var chipBtn = chip.gameObject.AddComponent<Button>();
            chipBtn.targetGraphic = chipImg;
            UnityEventTools.AddPersistentListener(chipBtn.onClick, view.ClickWantChip);

            // Row2：進化條（彈性寬）＋「進化 N%」
            var row2 = Node(bar, "Row2", new Rect(c.rowX, c.row2Y, 200, c.row2Height));
            var row2Layout = row2.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureRow(row2Layout, c.rowGap);

            var evo = NewRect(row2, "EvoBar");
            evo.sizeDelta = new Vector2(100, c.evoHeight);
            var evoLe = evo.gameObject.AddComponent<LayoutElement>();
            evoLe.minWidth = 0;
            evoLe.preferredWidth = 0;
            evoLe.flexibleWidth = 1;
            var trackImg = AddImage(Stretch(evo, "Track"), c.evoTrack, true, true);
            var fill = Stretch(evo, "Fill");
            AddImage(fill, c.evoMask, true, false);
            fill.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var segs = new Image[c.segments.Count];
            for (int i = 0; i < segs.Length; i++)
            {
                var seg = NewRect(fill, "Seg" + (i + 1) + "_" + c.segments[i].category);
                segs[i] = AddImage(seg, c.evoSegment, false, false);
                segs[i].color = c.segments[i].color;
            }
            var glow = Stretch(evo, "ReadyGlow", c.readyGlowOutset);
            AddImage(glow, c.evoReadyGlow, true, false);
            glow.gameObject.SetActive(false);
            var evoText = Text(NewRect(row2, "EvoText"), c, "進化 68%", c.evoTextSize, c.textColor, TextAlignmentOptions.Left);
            ((RectTransform)evoText.transform).sizeDelta = new Vector2(50, c.row2Height);
            var evoView = evo.gameObject.AddComponent<EvoBarView>();
            var evoBtn = evo.gameObject.AddComponent<Button>();
            evoBtn.targetGraphic = trackImg;
            evoBtn.transition = Selectable.Transition.None;
            UnityEventTools.AddPersistentListener(evoBtn.onClick, evoView.Tap);

            // CoinButton：靠右，寬隨數字
            var coin = NewRect(bar, "CoinButton");
            coin.anchorMin = coin.anchorMax = new Vector2(1f, 1f);
            coin.pivot = new Vector2(1f, 1f);
            coin.anchoredPosition = new Vector2(-c.coinRight, -c.coinY);
            coin.sizeDelta = new Vector2(78.77f, c.coinHeight);
            var coinBg = AddImage(Stretch(coin, "Bg"), c.coinPill, true, true);
            AddImage(Node(coin, "Dot", c.coinDot), c.coinDotSprite, false, false);
            var numberRt = Node(coin, "Number", new Rect(c.coinNumber.x, c.coinNumber.y, 24, c.coinNumber.z));
            var numberText = Text(numberRt, c, "500", c.coinSize, c.textColor, TextAlignmentOptions.Left);
            var plusRt = NewRect(coin, "Plus");
            plusRt.anchorMin = plusRt.anchorMax = new Vector2(1f, 1f);
            plusRt.pivot = new Vector2(1f, 1f);
            plusRt.anchoredPosition = new Vector2(-c.coinPlusRight, -c.coinPlusTop);
            plusRt.sizeDelta = c.coinPlusSize;
            AddImage(plusRt, c.coinPlus, false, false);
            var coinBtn = coin.gameObject.AddComponent<Button>();
            coinBtn.targetGraphic = coinBg;
            UnityEventTools.AddPersistentListener(coinBtn.onClick, view.ClickCoin);

            var menuRt = BuildMenu(hud, c);
            var homeRt = BuildHomeButton(hud, c, view);

            // 接上參照
            Set(heartsView, "config", c);
            SetArray(heartsView, "hearts", heartImages);
            Set(evoView, "config", c);
            Set(evoView, "fill", fill);
            SetArray(evoView, "segments", segs);
            Set(evoView, "readyGlow", glow.gameObject);
            Set(evoView, "evoText", evoText);
            Set(view, "config", c);
            Set(view, "rootGroup", hudGroup);
            Set(view, "bar", bar);
            Set(view, "petImage", petImg);
            Set(view, "lvBadge", lvBadge);
            Set(view, "lvText", lvText);
            Set(view, "nameText", nameText);
            Set(view, "hearts", heartsView);
            Set(view, "wantChip", chipImg);
            Set(view, "wantText", wantText);
            Set(view, "row1", row1);
            Set(view, "row2", row2);
            Set(view, "evoBar", evoView);
            Set(view, "coinButton", coin);
            Set(view, "coinNumberRect", numberRt);
            Set(view, "coinNumber", numberText);
            SetArray(view, "legacyHud", FindLegacyHud());
            Set(view, "barGroup", barGroup);
            Set(view, "homeButtonGroup", homeRt.gameObject.AddComponent<CanvasGroup>());
            Set(view, "menuGroup", menuRt.gameObject.AddComponent<CanvasGroup>());

            view.ApplyPreview();
            return hud.gameObject;
        }

        /// <summary>舊版首頁狀態欄裡和新狀態欄重複的顯示物件（路徑相對場景裡的 Canvas）。</summary>
        static readonly string[] LegacyHudPaths =
        {
            "MainGame/HomePage/PetAvatarButtom",                     // 舊頭像
            "MainGame/HomePage/Button(shop)",                        // 購物車（改點金幣）
            "MainGame/HomePage/PetCollectionManager/PetProgressBar", // 舊進度條（物件本身有寵物進度程式，只藏子物件）
            "MainGame/HomePage/PetCollectionManager/PercentText",
            "MainGame/HomePage/PetCollectionManager/CountText",
            "MainGame/HomePage/PetCollectionManager/PetNameText",
            "MainGame/HomePage/EconomyWallet/WalletText",            // 舊金幣文字（物件本身有金幣程式，只藏文字）
        };

        static GameObject[] FindLegacyHud()
        {
            var found = new List<GameObject>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != "Canvas") continue;
                foreach (var p in LegacyHudPaths)
                {
                    var t = root.transform.Find(p);
                    if (t != null) found.Add(t.gameObject);
                    else Debug.LogWarning("[TopBar] 找不到舊版狀態欄物件 Canvas/" + p + "（略過）");
                }
            }
            return found.ToArray();
        }

        /// <summary>左上 Home 鍵：畫面左 10、y 80，46×32，同選單鈕的毛玻璃膠囊＋小屋圖示 20×20，不加文字。</summary>
        static RectTransform BuildHomeButton(RectTransform hud, TopBarConfig c, TopBarView view)
        {
            var home = NewRect(hud, "HomeButton");
            home.anchorMin = home.anchorMax = new Vector2(0f, 1f);
            home.pivot = new Vector2(0f, 1f);
            home.anchoredPosition = new Vector2(c.homeLeftMargin, -c.homeY);
            home.sizeDelta = c.homeSize;
            var hit = home.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            AddImage(Stretch(home, "Bg", c.menuSpriteOutset), c.menuClosed, false, false);
            var icon = NewRect(home, "Icon");
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(c.homeIconSize, c.homeIconSize);
            AddImage(icon, c.iconHome, false, false);

            var btn = home.gameObject.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            UnityEventTools.AddPersistentListener(btn.onClick, view.ClickHome);
            return home;
        }

        static RectTransform BuildMenu(RectTransform hud, TopBarConfig c)
        {
            // MenuToggle：畫面右 10、y 80；46×32
            var menu = NewRect(hud, "MenuToggle");
            menu.anchorMin = menu.anchorMax = new Vector2(1f, 1f);
            menu.pivot = new Vector2(1f, 1f);
            menu.anchoredPosition = new Vector2(-c.menuRightMargin, -c.menuY);
            menu.sizeDelta = c.menuSize;
            var hit = menu.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var toggleBtn = menu.gameObject.AddComponent<Button>();
            toggleBtn.targetGraphic = hit;
            toggleBtn.transition = Selectable.Transition.None;

            var bg = AddImage(Stretch(menu, "Bg", c.menuSpriteOutset), c.menuClosed, false, false);
            var chevron = NewRect(menu, "Chevron");
            chevron.anchorMin = chevron.anchorMax = chevron.pivot = new Vector2(0.5f, 0.5f);
            chevron.sizeDelta = new Vector2(c.chevronSize, c.chevronSize);
            AddImage(chevron, c.chevron, false, false);

            // MenuItems：往下排，間距 8
            float itemH = c.menuTile + c.menuLabelGap + 16f;
            int n = c.menuOrder.Count;
            var items = NewRect(menu, "MenuItems");
            items.anchorMin = items.anchorMax = new Vector2(0.5f, 0f);
            items.pivot = new Vector2(0.5f, 1f);
            items.anchoredPosition = new Vector2(0f, -c.menuGap);
            items.sizeDelta = new Vector2(c.menuItemWidth, n * itemH + (n - 1) * c.menuGap);
            var itemsGroup = items.gameObject.AddComponent<CanvasGroup>();

            var buttons = new Dictionary<string, Button>();
            for (int i = 0; i < n; i++)
            {
                string label = c.menuOrder[i];
                var item = NewRect(items, "Item_" + label);
                item.anchorMin = item.anchorMax = new Vector2(0.5f, 1f);
                item.pivot = new Vector2(0.5f, 1f);
                item.anchoredPosition = new Vector2(0f, -i * (itemH + c.menuGap));
                item.sizeDelta = new Vector2(c.menuItemWidth, itemH);
                var itemHit = item.gameObject.AddComponent<Image>();
                itemHit.color = Color.clear;

                var tile = NewRect(item, "Tile");
                tile.anchorMin = tile.anchorMax = new Vector2(0.5f, 1f);
                tile.pivot = new Vector2(0.5f, 1f);
                tile.anchoredPosition = Vector2.zero;
                tile.sizeDelta = new Vector2(c.menuTile, c.menuTile);
                AddImage(Stretch(tile, "Bg", c.menuSpriteOutset), c.menuTileSprite, false, false);
                var icon = NewRect(tile, "Icon");
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
                icon.sizeDelta = new Vector2(c.menuIconSize, c.menuIconSize);
                AddImage(icon, c.MenuIcon(label), false, false);

                var labelRt = Node(item, "Label", new Rect(0, c.menuTile + c.menuLabelGap, c.menuItemWidth, 16));
                var t = Text(labelRt, c, label, c.menuLabelSize, Color.white, TextAlignmentOptions.Center);
                if (c.labelShadowMaterial != null) t.fontSharedMaterial = c.labelShadowMaterial;

                var b = item.gameObject.AddComponent<Button>();
                b.targetGraphic = itemHit;
                b.transition = Selectable.Transition.None;
                buttons[label] = b;
            }
            items.gameObject.SetActive(false);

            var toggle = menu.gameObject.AddComponent<MenuToggle>();
            Set(toggle, "config", c);
            Set(toggle, "toggleButton", toggleBtn);
            Set(toggle, "background", bg);
            Set(toggle, "chevron", chevron);
            Set(toggle, "items", items);
            Set(toggle, "itemsGroup", itemsGroup);
            if (buttons.TryGetValue("佈置", out var decor)) Set(toggle, "decorButton", decor);
            if (buttons.TryGetValue("旅行", out var travel)) Set(toggle, "travelButton", travel);
            if (buttons.TryGetValue("圖鑑", out var dex)) Set(toggle, "dexButton", dex);
            if (buttons.TryGetValue("設定", out var settings)) Set(toggle, "settingsButton", settings);
            return menu;
        }

        static void ConfigureRow(HorizontalLayoutGroup g, float spacing)
        {
            g.spacing = spacing;
            g.childAlignment = TextAnchor.MiddleLeft;
            g.childControlWidth = true;
            g.childControlHeight = false;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
        }

        // ─── UI helpers ────────────────────────────────────────────────

        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>以父物件左上為原點放置（README 座標）。</summary>
        static RectTransform Node(Transform parent, string name, Rect r)
        {
            var rt = NewRect(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(r.x, -r.y);
            rt.sizeDelta = new Vector2(r.width, r.height);
            return rt;
        }

        /// <summary>撐滿父物件，四邊各外擴 outset。</summary>
        static RectTransform Stretch(Transform parent, string name, float outset = 0f)
        {
            var rt = NewRect(parent, name);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-outset, -outset);
            rt.offsetMax = new Vector2(outset, outset);
            return rt;
        }

        static Image AddImage(RectTransform rt, Sprite sprite, bool sliced, bool raycast)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = raycast;
            return img;
        }

        static TMP_Text Text(RectTransform rt, TopBarConfig c, string value, float size, Color color, TextAlignmentOptions align)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = c.font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.margin = Vector4.zero;
            return t;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[TopBar] {target.GetType().Name} 沒有欄位 {field}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray<T>(Object target, string field, T[] values) where T : Object
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ─── JSON helpers ──────────────────────────────────────────────

        static object Get(object node, params string[] path)
        {
            foreach (var key in path)
            {
                if (!(node is Dictionary<string, object> d) || !d.TryGetValue(key, out node)) return null;
            }
            return node;
        }

        static Dictionary<string, object> Obj(object node, params string[] path) => Get(node, path) as Dictionary<string, object>;

        static float Num(Dictionary<string, object> d, string key, float fallback) =>
            d.TryGetValue(key, out var v) && v is double n ? (float)n : fallback;

        static Vector2 Vec2(object node, Vector2 fallback) =>
            node is List<object> l && l.Count >= 2 && l[0] is double a && l[1] is double b ? new Vector2((float)a, (float)b) : fallback;

        static Rect RectOf(object node, Rect fallback)
        {
            if (!(node is List<object> l) || l.Count < 4) return fallback;
            return new Rect((float)(double)l[0], (float)(double)l[1], (float)(double)l[2], (float)(double)l[3]);
        }

        static Color Col(object node, Color fallback)
        {
            if (!(node is string s)) return fallback;
            s = s.Trim();
            var rgba = Regex.Match(s, @"rgba\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\)");
            if (rgba.Success)
            {
                float F(int i) => float.Parse(rgba.Groups[i].Value, CultureInfo.InvariantCulture);
                return new Color(F(1) / 255f, F(2) / 255f, F(3) / 255f, F(4));
            }
            return ColorUtility.TryParseHtmlString(s, out var c) ? c : fallback;
        }

        static string FirstHex(string s)
        {
            var m = Regex.Match(s, @"#[0-9A-Fa-f]{6}");
            return m.Success ? m.Value : null;
        }

        static float FirstNumber(string s, float fallback)
        {
            var m = Regex.Match(s, @"[\d.]+");
            return m.Success ? float.Parse(m.Value, CultureInfo.InvariantCulture) : fallback;
        }
    }
}
