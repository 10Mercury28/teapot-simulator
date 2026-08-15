using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections.Generic;

public class GameMenuController : MonoBehaviour
{
    [Header("Menu Panel")]
    public CanvasGroup menuPanel;

    [Header("Toggle Buttons")]
    public GameObject openButton;
    public Button closeButton;

    [Header("Skip Button")]
    public Button skipButton;

    [Header("References")]
    public BeforeStartController beforeStartController;
    public List<VideoPlayer> videoPlayers = new List<VideoPlayer>();

    [Header("Interaction Blocking")]
    [Tooltip("所有可交互物体所在的 Layer")]
    public string interactableLayerName = "GameplayInteractable";

    private int interactableLayer = -1;
    private bool menuOpen = false;
    private HashSet<VideoPlayer> pausedByMenu = new HashSet<VideoPlayer>();

    // ================= LIFE CYCLE =================

    void Start()
    {
        interactableLayer = LayerMask.NameToLayer(interactableLayerName);
        if (interactableLayer == -1)
        {
            Debug.LogError($"❌ Layer '{interactableLayerName}' 不存在！");
        }

        SetMenuState(false);

        if (openButton != null)
            openButton.GetComponent<Button>().onClick.AddListener(OpenMenu);

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseMenu);

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipClicked);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetMenuState(!menuOpen);
        }

        UpdateSkipVisibility();
    }

    // ================= MENU CORE =================

    void OpenMenu() => SetMenuState(true);
    void CloseMenu() => SetMenuState(false);

    void SetMenuState(bool open)
    {
        if (menuPanel == null)
        {
            Debug.LogError("❌ GameMenuController: menuPanel 未绑定！");
            return;
        }

        menuOpen = open;

        menuPanel.alpha = open ? 1f : 0f;
        menuPanel.interactable = open;
        menuPanel.blocksRaycasts = open;

        if (openButton != null)
            openButton.SetActive(!open);

        if (closeButton != null)
            closeButton.gameObject.SetActive(open);

        if (open)
            PauseEverything();
        else
            ResumeEverything();
    }

    // ================= PAUSE SYSTEM =================

    void PauseEverything()
    {
        Time.timeScale = 0f;
        pausedByMenu.Clear();

        foreach (var vp in videoPlayers)
        {
            if (vp != null && vp.isPlaying)
            {
                vp.Pause();
                pausedByMenu.Add(vp);
            }
        }

        DisableGameplayInteraction();
    }

    void ResumeEverything()
    {
        Time.timeScale = 1f;

        foreach (var vp in pausedByMenu)
        {
            if (vp != null)
                vp.Play();
        }

        pausedByMenu.Clear();

        EnableGameplayInteraction();
    }

    // ================= INTERACTION BLOCK =================

    void DisableGameplayInteraction()
    {
        if (interactableLayer == -1) return;

        foreach (var col in FindObjectsOfType<Collider2D>())
        {
            if (col.gameObject.layer == interactableLayer)
                col.enabled = false;
        }

        foreach (var col in FindObjectsOfType<Collider>())
        {
            if (col.gameObject.layer == interactableLayer)
                col.enabled = false;
        }
    }

    void EnableGameplayInteraction()
    {
        if (interactableLayer == -1) return;

        foreach (var col in FindObjectsOfType<Collider2D>())
        {
            if (col.gameObject.layer == interactableLayer)
                col.enabled = true;
        }

        foreach (var col in FindObjectsOfType<Collider>())
        {
            if (col.gameObject.layer == interactableLayer)
                col.enabled = true;
        }
    }

    // ================= SKIP =================

    void UpdateSkipVisibility()
    {
        if (skipButton == null) return;

        bool show =
            beforeStartController != null &&
            beforeStartController.IsBeforeStartActive();

        skipButton.gameObject.SetActive(show);
    }

    void OnSkipClicked()
    {
        if (beforeStartController == null) return;

        beforeStartController.ForceShowButtonWithFade(2f);
        SetMenuState(false);
    }
}
