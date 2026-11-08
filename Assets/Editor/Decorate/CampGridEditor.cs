using System.Collections.Generic;
using System.IO;
using BlockPet.Decoration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockPet.EditorTools.Decorate
{
    /// <summary>
    /// 格子表編輯器（設計文件 5.28 ②）：在 Project 選到 CampGrid_*.asset，打開 Feed.unity 的 Scene 畫面就會畫出格子。
    /// - 上方選「標記模式」，在 Scene 畫面點格子：存在（加入／拿掉）、不能放（標／取消）、擴建（加到某一塊擴建）。
    /// - 「對齊營地底圖」：照設計稿 OX 205、OY 370、一格 60（畫框寬 390）從營地底圖（MainRoomBackground_world）算出來。
    /// - 「依設計稿範圍鋪滿草地」：中心點在 x 15～378、y 335～625 的格子都加進來（之後再把小路、樹叢、石頭標成不能放）。
    /// 白框＝存在、紅框＝不能放、藍框＝擴建、淡灰＝還沒加的格子。
    /// </summary>
    [CustomEditor(typeof(CampGrid))]
    public class CampGridEditor : Editor
    {
        enum Mode { Exists, Blocked, Extension }
        const int Range = 26;           // 畫 −Range～Range 的格子讓人點
        const string AssetPath = "Assets/Resources/Camp/CampGrid_forest.asset";
        const string BackgroundName = "MainRoomBackground_world";
        static readonly Vector2 MockOrigin = new Vector2(205, 370);
        static readonly Rect MockArea = Rect.MinMaxRect(15, 335, 378, 625);

        static Mode _mode = Mode.Exists;
        static int _extIndex;

        void OnEnable() => SceneView.duringSceneGui += OnScene;
        void OnDisable() => SceneView.duringSceneGui -= OnScene;

        public override void OnInspectorGUI()
        {
            var g = (CampGrid)target;
            EditorGUILayout.HelpBox("在 Scene 畫面點格子標記。白＝存在、紅＝不能放、藍＝擴建、淡灰＝還沒加。", MessageType.Info);
            _mode = (Mode)GUILayout.Toolbar((int)_mode, new[] { "存在", "不能放", "擴建" });
            if (_mode == Mode.Extension)
            {
                if (g.extensions.Count == 0) EditorGUILayout.HelpBox("先在下面 extensions 加一塊擴建（填 unlockItemId，例 exp_lake）", MessageType.Warning);
                else _extIndex = EditorGUILayout.IntSlider("第幾塊擴建", _extIndex, 0, g.extensions.Count - 1);
            }
            var owned = new List<string>();
            EditorGUILayout.LabelField($"存在 {g.cells.Count} 格・不能放 {g.blocked.Count} 格・可以放 {g.PlaceableCount(owned)} 格");
            EditorGUILayout.Space();
            if (GUILayout.Button("對齊營地底圖（設計稿 OX 205、OY 370）")) AlignToBackground(g);
            if (GUILayout.Button("依設計稿範圍鋪滿草地")) FillMockArea(g);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        void OnScene(SceneView view)
        {
            var g = (CampGrid)target;
            if (g == null || g.tileWorldWidth <= 0f) return;
            var exists = new HashSet<Cell>(g.cells);
            var blocked = new HashSet<Cell>(g.blocked);
            var ext = new Dictionary<Cell, int>();
            for (int k = 0; k < g.extensions.Count; k++)
                if (g.extensions[k] != null) foreach (var c in g.extensions[k].cells) ext[c] = k;

            for (int i = -Range; i <= Range; i++)
                for (int j = -Range; j <= Range; j++)
                {
                    var c = new Cell(i, j);
                    Color col;
                    if (blocked.Contains(c)) col = new Color(1f, 0.4f, 0.3f, 0.9f);
                    else if (exists.Contains(c)) col = new Color(1f, 1f, 1f, 0.9f);
                    else if (ext.ContainsKey(c)) col = new Color(0.4f, 0.6f, 1f, 0.9f);
                    else
                    {
                        // 只畫靠近已有格子的候選格，畫面不會太亂
                        if (!Near(c, exists)) continue;
                        col = new Color(1f, 1f, 1f, 0.15f);
                    }
                    DrawDiamond(g, c, col, blocked.Contains(c) || exists.Contains(c) || ext.ContainsKey(c));
                }

            // 點一下標記
            var e = Event.current;
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                float t = Mathf.Abs(ray.direction.z) > 1e-5f ? -ray.origin.z / ray.direction.z : 0f;
                var world = ray.origin + ray.direction * t;
                var cell = g.CellAtWorld(world);
                Undo.RecordObject(g, "標記格子");
                Toggle(g, cell);
                EditorUtility.SetDirty(g);
                e.Use();
                Repaint();
            }
        }

        static bool Near(Cell c, HashSet<Cell> exists)
        {
            if (exists.Count == 0) return Mathf.Abs(c.i) < 14 && Mathf.Abs(c.j) < 14;
            for (int di = -2; di <= 2; di++)
                for (int dj = -2; dj <= 2; dj++)
                    if (exists.Contains(new Cell(c.i + di, c.j + dj))) return true;
            return false;
        }

        void Toggle(CampGrid g, Cell c)
        {
            switch (_mode)
            {
                case Mode.Exists:
                    if (!g.cells.Remove(c)) g.cells.Add(c);
                    else g.blocked.Remove(c);
                    break;
                case Mode.Blocked:
                    if (!g.blocked.Remove(c)) { g.blocked.Add(c); if (!g.cells.Contains(c)) g.cells.Add(c); }
                    break;
                case Mode.Extension:
                    if (g.extensions.Count == 0) return;
                    var ext = g.extensions[Mathf.Clamp(_extIndex, 0, g.extensions.Count - 1)];
                    if (!ext.cells.Remove(c)) ext.cells.Add(c);
                    break;
            }
        }

        static void DrawDiamond(CampGrid g, Cell c, Color col, bool filled)
        {
            var center = g.CellCenterWorld(c);
            float hw = g.tileWorldWidth / 2f, hh = g.tileWorldWidth / 4f;
            var pts = new[]
            {
                center + new Vector3(0, hh), center + new Vector3(hw, 0), center + new Vector3(0, -hh), center + new Vector3(-hw, 0),
            };
            if (filled) Handles.DrawSolidRectangleWithOutline(pts, new Color(col.r, col.g, col.b, 0.12f), col);
            else { Handles.color = col; Handles.DrawPolyLine(pts[0], pts[1], pts[2], pts[3], pts[0]); }
        }

        // ─── 對齊、鋪滿 ────────────────────────────────────────────────

        static SpriteRenderer FindBackground()
        {
            var go = GameObject.Find(BackgroundName);
            if (go == null)
                foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
                    if (sr.name == BackgroundName && sr.gameObject.scene.IsValid()) return sr;
            return go != null ? go.GetComponent<SpriteRenderer>() : null;
        }

        /// <summary>設計稿畫框寬 390 對到營地底圖的寬；OX 205、OY 370 是從底圖左上角量的。</summary>
        public static bool AlignToBackground(CampGrid g)
        {
            var bg = FindBackground();
            if (bg == null) { Debug.LogWarning("[CampGrid] 找不到營地底圖 " + BackgroundName + "（先打開 Feed.unity）"); return false; }
            var b = bg.bounds;
            float k = b.size.x / 390f;
            Undo.RecordObject(g, "對齊營地底圖");
            g.worldOrigin = new Vector2(b.min.x + MockOrigin.x * k, b.max.y - MockOrigin.y * k);
            g.tileWorldWidth = GridMath.TileW * k;
            EditorUtility.SetDirty(g);
            Debug.Log($"[CampGrid] 已對齊營地底圖：origin {g.worldOrigin}、一格寬 {g.tileWorldWidth:0.###}");
            return true;
        }

        /// <summary>設計稿：中心點落在 x 15～378、y 335～625 的格子（約 120 格）。</summary>
        public static void FillMockArea(CampGrid g)
        {
            Undo.RecordObject(g, "鋪滿草地");
            for (int i = -Range; i <= Range; i++)
                for (int j = -Range; j <= Range; j++)
                {
                    var c = new Cell(i, j);
                    var p = GridMath.Center(c, MockOrigin);
                    if (MockArea.Contains(p) && !g.cells.Contains(c)) g.cells.Add(c);
                }
            EditorUtility.SetDirty(g);
            Debug.Log($"[CampGrid] 鋪滿草地：{g.cells.Count} 格");
        }

        /// <summary>建立森林營地的格子表（已經有就不動）：對齊底圖、鋪滿設計稿範圍。batchmode 也能跑。</summary>
        [MenuItem("BlockPet/Decorate/Create Forest Camp Grid")]
        public static void CreateForest()
        {
            var g = AssetDatabase.LoadAssetAtPath<CampGrid>(AssetPath);
            if (g != null) { Selection.activeObject = g; Debug.Log("[CampGrid] 已經有了：" + AssetPath); return; }
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Camp")) AssetDatabase.CreateFolder("Assets/Resources", "Camp");
            // 先開場景再建：開場景會把還沒存成檔案的物件清掉
            if (FindBackground() == null && File.Exists("Assets/Scenes/Feed.unity"))
                EditorSceneManager.OpenScene("Assets/Scenes/Feed.unity", OpenSceneMode.Single);
            g = CreateInstance<CampGrid>();
            g.sceneId = "forest";
            g.extensions.Add(new GridExtension { unlockItemId = "exp_flower" });
            g.extensions.Add(new GridExtension { unlockItemId = "exp_lake" });
            AlignToBackground(g);
            FillMockArea(g);
            AssetDatabase.CreateAsset(g, AssetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = g;
            Debug.Log("[CampGrid] 建好了：" + AssetPath);
        }
    }
}
