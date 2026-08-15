using UnityEngine;
using System.Collections.Generic;

public class CutTrailDrawer3D : MonoBehaviour
{
    [Header("References")]
    public LineRenderer lineRenderer;
    public Transform areaA;
    public Transform areaB;
    public Transform areaC;
    public Camera mainCamera;

    [Header("Settings")]
    public float lineWidth = 0.02f;
    public float fadeDuration = 0.5f;
    public LayerMask areaLayer; // 给a/b/c指定Layer方便检测

    private bool drawing = false;
    private List<Vector3> points = new List<Vector3>();

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 0;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
    }

    void Update()
    {
        // 鼠标射线
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        bool inArea = Physics.Raycast(ray, out hit, 100f, areaLayer);

        if (inArea)
        {
            if (Input.GetMouseButtonDown(0))
            {
                drawing = true;
                lineRenderer.positionCount = 0;
                points.Clear();
            }

            if (drawing && Input.GetMouseButton(0))
            {
                Vector3 hitPos = hit.point;
                points.Add(hitPos);
                lineRenderer.positionCount = points.Count;
                for (int i = 0; i < points.Count; i++)
                    lineRenderer.SetPosition(i, points[i]);
            }

            if (Input.GetMouseButtonUp(0))
            {
                drawing = false;
                StartCoroutine(FadeTrail());
            }
        }
    }

    private System.Collections.IEnumerator FadeTrail()
    {
        float t = fadeDuration;
        var colorGradient = lineRenderer.colorGradient;
        while (t > 0)
        {
            t -= Time.deltaTime;
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[]
            {
                new GradientAlphaKey(Mathf.Clamp01(t / fadeDuration), 0),
                new GradientAlphaKey(0, 1)
            };
            colorGradient.alphaKeys = alphaKeys;
            lineRenderer.colorGradient = colorGradient;
            yield return null;
        }
        lineRenderer.positionCount = 0;
        points.Clear();
    }
}
