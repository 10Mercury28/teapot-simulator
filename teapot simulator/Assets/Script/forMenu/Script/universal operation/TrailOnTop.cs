using UnityEngine;

[RequireComponent(typeof(TrailRenderer))]
public class TrailOnTop : MonoBehaviour
{
    public float zOffset = -500f;
    private Camera cam;

    void Start()
    {
        cam = Camera.main;
        var tr = GetComponent<TrailRenderer>();
        tr.sortingLayerName = "Default";
        tr.sortingOrder = 9999;
    }

    void LateUpdate()
    {
        if (cam)
        {
            var pos = transform.position;
            pos.z = cam.transform.position.z + zOffset;
            transform.position = pos;
        }
    }
}