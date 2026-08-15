using UnityEngine;
using UnityEditor;

/*[CustomEditor(typeof(HelixPath))]
public class HelixPathEditor : Editor
{
    void OnSceneGUI()
    {
        HelixPath helix = (HelixPath)target;
        var pts = helix.GetPoints();

        Handles.color = helix.gizmoColor;

        // 画线
        for (int i = 0; i < pts.Count - 1; i++)
            Handles.DrawLine(pts[i], pts[i + 1]);

        // 可选绘制点
        foreach (var p in pts)
            Handles.DrawSolidDisc(p, Vector3.forward, helix.gizmoPointSize);
    }
}*/