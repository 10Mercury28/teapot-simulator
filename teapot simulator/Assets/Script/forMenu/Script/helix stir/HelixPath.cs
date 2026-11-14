using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class HelixPath : MonoBehaviour
{
    [Header("Helix Settings")]
    public float a = 0.1f;          // 起始半径
    public float b = 0.15f;         // 螺距
    public float turns = 3f;        // 旋转圈数
    public int resolution = 300;    // 路径精度

    [Header("Gizmo Settings")]
    public Color gizmoColor = Color.yellow;
    public float gizmoPointSize = 0.02f;

    private List<Vector2> cachedPoints = new();

    public List<Vector2> GetPoints()
    {
        if (cachedPoints == null || cachedPoints.Count != resolution)
            Rebuild();
        return cachedPoints;
    }

    void OnValidate()
    {
        Rebuild();
    }

    public void Rebuild()
    {
        cachedPoints = new List<Vector2>();

        float maxTheta = turns * Mathf.PI * 2f;

        for (int i = 0; i < resolution; i++)
        {
            float t = (float)i / (resolution - 1);
            float theta = t * maxTheta;
            float r = a + b * theta;

            float x = r * Mathf.Cos(theta);
            float y = r * Mathf.Sin(theta);

            Vector2 worldPos = (Vector2)transform.position + new Vector2(x, y);
            cachedPoints.Add(worldPos);
        }
    }
}
