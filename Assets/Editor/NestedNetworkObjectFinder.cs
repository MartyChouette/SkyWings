using UnityEngine;
using UnityEditor;
using Unity.Netcode;

public static class NestedNetworkObjectFinder
{
    [MenuItem("Tools/Netcode/Find Nested NetworkObjects In Scene")]
    public static void FindNestedInScene()
    {
        var allNOs = Object.FindObjectsOfType<NetworkObject>(true);
        int count = 0;

        foreach (var no in allNOs)
        {
            if (no == null) continue;

            // walk up the parent chain and see if any ancestor also has a NetworkObject
            Transform t = no.transform.parent;
            while (t != null)
            {
                if (t.GetComponent<NetworkObject>() != null)
                {
                    Debug.LogError(
                        $"Nested NetworkObject found: {GetPath(no.transform)} " +
                        $"(parent NO: {GetPath(t)})",
                        no
                    );
                    count++;
                    break;
                }
                t = t.parent;
            }
        }

        if (count == 0)
            Debug.Log("[Netcode] No nested NetworkObjects found in this scene.");
        else
            Debug.Log($"[Netcode] Finished scan. Found {count} nested NetworkObject(s).");
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
