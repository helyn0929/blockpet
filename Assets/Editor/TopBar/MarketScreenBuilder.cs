using System.Collections.Generic;
using System.IO;
using BlockPet.Decoration;
using BlockPet.Hand;
using BlockPet.UI.Home;
using BlockPet.UI.Market;
using BlockPet.UI.Room;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build Market Screen」：把設計稿「商店 A 營地試擺」接到場景的 MarketPage。
    /// 1. 重新匯入 Assets/Art/UI/Market/Sprites（docs/market/Tools/make_market_sprites.py 產生）
    /// 2. 每件商店家具建立 DecorationItem（Assets/Data/Market/{id}.asset），加進場景用的 DecorationDatabase，買了才能擺
    /// 3. MarketPage 加上 MarketScreenController 並接好素材；關掉舊的商店頁（MarketUIController／MarketPageController／UIDocument，不刪除）
    /// 家具放進營地的大小用 DecorationItem.worldScale 調（預設 2）。
    /// </summary>
    public static class MarketScreenBuilder
    {
        const string SkinPath = "Assets/Data/HandSkin.asset";
        const string BackgroundPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";
        const string ItemFolder = "Assets/Data/Market";
        const float DefaultWorldScale = 2f;

        [MenuItem("BlockPet/Build Market Screen")]
        public static void Build()
        {
            var pm = Object.FindObjectOfType<PageManager>(true);
            GameObject page = null;
            if (pm != null && new SerializedObject(pm).FindProperty("marketPage").objectReferenceValue is GameObject g) page = g;
            if (page == null)
            {
                EditorUtility.DisplayDialog("Build Market Screen", "目前場景找不到 MarketPage（PageManager 的 Market Page）。請先打開 Feed 場景。", "OK");
                return;
            }

            TopBarSpriteImporter.ReimportFolder(TopBarSpriteImporter.MarketFolder);
            var sprites = new List<Sprite>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { TopBarSpriteImporter.MarketFolder.TrimEnd('/') }))
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) sprites.Add(s);
            }
            if (sprites.Count == 0) Debug.LogWarning("[Market] 找不到商店素材，先執行 python3 docs/market/Tools/make_market_sprites.py");
            Sprite S(string n) => sprites.Find(s => s.name == n);

            // 商店家具 → DecorationItem，加進場景的 DecorationDatabase
            var db = FindDatabase();
            if (db == null) Debug.LogWarning("[Market] 場景裡找不到 DecorationDatabase，家具買了也無法擺放");
            Directory.CreateDirectory(ItemFolder);
            int added = 0;
            foreach (var e in MarketCatalog.InTab(MarketTab.Furniture))
            {
                string path = ItemFolder + "/" + e.id + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<DecorationItem>(path);
                if (item == null)
                {
                    item = ScriptableObject.CreateInstance<DecorationItem>();
                    item.worldScale = DefaultWorldScale;
                    AssetDatabase.CreateAsset(item, path);
                }
                item.itemId = e.id;
                item.displayName = e.name;
                item.icon = S(e.sprite);
                item.worldSprite = S(e.sprite);
                item.price = e.price;
                item.category = DecorationCategory.Furniture;
                EditorUtility.SetDirty(item);

                if (db != null && db.GetById(e.id) == null)
                {
                    var dso = new SerializedObject(db);
                    var list = dso.FindProperty("items");
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = item;
                    dso.ApplyModifiedPropertiesWithoutUndo();
                    added++;
                }
            }
            if (db != null) EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            var screen = page.GetComponent<MarketScreenController>();
            if (screen == null) screen = Undo.AddComponent<MarketScreenController>(page);
            var so = new SerializedObject(screen);
            so.FindProperty("skin").objectReferenceValue = AssetDatabase.LoadAssetAtPath<HandSkin>(SkinPath);
            so.FindProperty("background").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            so.FindProperty("pageManager").objectReferenceValue = pm;
            so.FindProperty("decorationDatabase").objectReferenceValue = db;
            so.FindProperty("roomEditor").objectReferenceValue = Object.FindObjectOfType<RoomEditorController>(true);
            var sp = so.FindProperty("sprites");
            sp.arraySize = sprites.Count;
            for (int i = 0; i < sprites.Count; i++) sp.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];

            SerializedProperty pets = null;
            var ring = Object.FindObjectOfType<RoomRingController>(true);
            if (ring != null) pets = new SerializedObject(ring).FindProperty("petSprites");
            if (pets == null || pets.arraySize == 0)
            {
                var roomUi = Object.FindObjectOfType<RoomUIController>(true);
                if (roomUi != null) pets = new SerializedObject(roomUi).FindProperty("petSprites");
            }
            if (pets != null)
            {
                var dst = so.FindProperty("petSprites");
                dst.arraySize = pets.arraySize;
                for (int i = 0; i < pets.arraySize; i++)
                    dst.GetArrayElementAtIndex(i).objectReferenceValue = pets.GetArrayElementAtIndex(i).objectReferenceValue;
            }
            so.ApplyModifiedProperties();

            // 關掉舊的商店頁
            foreach (var old in new Behaviour[] { page.GetComponent<MarketUIController>(), page.GetComponent<MarketPageController>(), page.GetComponent<UIDocument>() })
            {
                if (old == null) continue;
                Undo.RecordObject(old, "Disable old market");
                old.enabled = false;
            }

            EditorSceneManager.MarkSceneDirty(page.scene);
            Selection.activeGameObject = page;
            EditorGUIUtility.PingObject(page);
            Debug.Log($"[Market] 完成：{page.name} 已改用新版商店；{sprites.Count} 張素材、DecorationDatabase 新增 {added} 件家具（{ItemFolder}）。記得存檔場景。");
        }

        static DecorationDatabase FindDatabase()
        {
            foreach (var picker in Object.FindObjectsOfType<DecorationPickerController>(true))
                if (new SerializedObject(picker).FindProperty("database").objectReferenceValue is DecorationDatabase d) return d;
            var guids = AssetDatabase.FindAssets("t:DecorationDatabase");
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<DecorationDatabase>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }
    }
}
