#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Use when Unity reports SourceAssetDB / import code 4 or stale prefab state after external edits.
/// </summary>
public static class BlockpetPrefabMaintenance
{
    const string MessageRowPrefab = "Assets/Prefabs/MessageRow.prefab";

    [MenuItem("Blockpet/Force Reimport MessageRow Prefab")]
    public static void ReimportMessageRow()
    {
        AssetDatabase.ImportAsset(MessageRowPrefab, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("[Blockpet] Reimported " + MessageRowPrefab);
    }
}
#endif
