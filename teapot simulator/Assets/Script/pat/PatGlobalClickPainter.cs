using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class PatGlobalClickPainter : MonoBehaviour
{
    [Header("Stamp Settings")]
    public Sprite patSprite;                // The pat.png image
    public float stampDuration = 1f;        // Fade-out time
    public Vector2 stampSize = new Vector2(200, 200);

    [Header("Clickable Areas (UI Buttons OR BoxColliders)")]
    public List<Collider2D> boxAreas = new List<Collider2D>();
    public List<Button> uiButtons = new List<Button>();

    [Header("References")]
    public Canvas rootCanvas;               // This must be the TOP canvas
    public RectTransform stampParent;       // Usually same as Canvas

    void Start()
    {
        // Hook up UI Buttons
        foreach (var btn in uiButtons)
        {
            btn.onClick.AddListener(() => SpawnStampUI());
        }
    }

    void Update()
    {
        if (Time.timeScale <= 0f || BeforeStartController.IsAnyBeforeStartBlocking)
            return;

        if (PopupManager.ExistingInstance != null && PopupManager.ExistingInstance.HasActivePopup)
            return;

        // Check module hit areas (UI Frame or fallback BoxCollider2D)
        if (Input.GetMouseButtonDown(0))
        {
            foreach (var col in boxAreas)
            {
                if (col == null) continue;

                PatModule pm = col.GetComponent<PatModule>();
                if (pm != null)
                {
                    if (pm.active && pm.IsPointerInsideModule(Input.mousePosition))
                    {
                        SpawnStampWorldPosition(Input.mousePosition);
                        break;
                    }
                }
                else if (Camera.main != null)
                {
                    Vector3 sp = Input.mousePosition;
                    sp.z = Mathf.Abs(Camera.main.transform.position.z);
                    Vector3 mouse = Camera.main.ScreenToWorldPoint(sp);
                    Vector2 pos = new Vector2(mouse.x, mouse.y);
                    if (col.OverlapPoint(pos))
                    {
                        SpawnStampWorldPosition(Input.mousePosition);
                        break;
                    }
                }
            }
        }
    }

    // Stamp for UI Buttons (centered on button)
    void SpawnStampUI()
    {
        Vector2 screenPos = Input.mousePosition;
        SpawnStampWorldPosition(screenPos);
    }

    // Spawn stamp at cursor position
    void SpawnStampWorldPosition(Vector2 screenPos)
    {
        GameObject go = new GameObject("PatStamp", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        go.transform.SetParent(stampParent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = stampSize;
        rt.position = screenPos;

        Image img = go.GetComponent<Image>();
        img.sprite = patSprite;
        img.raycastTarget = false;

        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        cg.alpha = 1f;

        StartCoroutine(FadeAndDestroy(go, cg));
    }

    IEnumerator FadeAndDestroy(GameObject obj, CanvasGroup cg)
    {
        float t = 0f;

        while (t < stampDuration)
        {
            t += Time.deltaTime;
            cg.alpha = 1f - (t / stampDuration);
            yield return null;
        }

        Destroy(obj);
    }
}
