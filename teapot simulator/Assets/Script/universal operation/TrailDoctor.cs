using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways, RequireComponent(typeof(TrailRenderer))]
public class TrailDoctor : MonoBehaviour
{
    public float forceZ = -5f;        // 每帧把 Trail 推到相机前面
    public bool pinToCameraZ = true;  // 绑定相机Z平面
    public bool replaceMaterial = true;

    private TrailRenderer tr;
    private Camera cam;

    void OnEnable()
    {
        tr = GetComponent<TrailRenderer>();
        cam = Camera.main;

        if (replaceMaterial)
        {
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = Color.white;
            tr.material = mat;
        }

        tr.time = Mathf.Max(tr.time, 1.0f);
        tr.minVertexDistance = Mathf.Min(tr.minVertexDistance, 0.02f);
        tr.alignment = LineAlignment.View;
        tr.textureMode = LineTextureMode.Stretch;

        Debug.Log($"[TrailDoctor] ▶️ Start\n" +
                  $"- Shader: {tr.material?.shader?.name}\n" +
                  $"- Color: {tr.material?.color}\n" +
                  $"- Time: {tr.time}, MinVertexDist: {tr.minVertexDistance}\n" +
                  $"- SortingLayerID: {tr.sortingLayerID}, Order: {tr.sortingOrder}\n" +
                  $"- Trail Layer: {LayerMask.LayerToName(gameObject.layer)}\n" +
                  $"- Camera: {cam?.name}, CullingMaskHasTrail: {CameraHasLayer(cam, gameObject.layer)}");
    }

    void LateUpdate()
    {
        if (!cam) cam = Camera.main;
        if (pinToCameraZ && cam)
        {
            var p = transform.position;
            p.z = cam.transform.position.z + forceZ; // 比 UI 更靠前
            transform.position = p;
        }
    }

    private bool CameraHasLayer(Camera c, int layer)
    {
        if (!c) return false;
        int mask = c.cullingMask;
        int bit = 1 << layer;
        return (mask & bit) != 0;
    }
}