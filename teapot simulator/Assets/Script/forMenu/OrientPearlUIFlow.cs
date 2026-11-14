using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// First-pass UI navigation for Orient Pearl: cover -> initial settings -> workspace.
/// Attach to a persistent UI root. Connect all panels and button OnClick actions in Inspector.
/// Gameplay scene/Archive/Résumé actions are exposed as UnityEvents so your existing
/// Yarn/scene progression systems remain the source of truth.
/// </summary>
public sealed class OrientPearlUIFlow : MonoBehaviour
{
    private const string SetupKey = "OP.UI.FirstRunConfigured";

    [Header("Panels - one active at a time")]
    [SerializeField] private GameObject coverPanel;
    [SerializeField] private GameObject workspacePanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject archivePanel;
    [SerializeField] private GameObject resumePanel;

    [Header("Modal dialogs")]
    [SerializeField] private GameObject unfinishedWorkflowDialog;
    [SerializeField] private GameObject quitConfirmationDialog;

    [Header("Other Components")]
    [SerializeField] private OrientPearlSettingsUI settingsUI;

    [Header("Prototype ONLY: replace with actual save/progression manager")]
    [SerializeField] private bool unfinishedWorkflow = true;

    [Header("Connect to your gameplay / Yarn systems")]
    public UnityEvent onStartNewProject;
    public UnityEvent onResumeExistingWorkflow;

    private enum Page { Cover, Workspace, Settings, Archive, Resume }
    private Page returnPageAfterSettings = Page.Workspace;
    private Page currentPage = Page.Cover;
    private bool firstTimeSetup;

    private void Start()
    {
        SetPage(Page.Cover);
    }

    // Cover Start button
    public void StartFromCover()
    {
        firstTimeSetup = PlayerPrefs.GetInt(SetupKey, 0) == 0;
        if (firstTimeSetup)
        {
            returnPageAfterSettings = Page.Workspace;
            OpenSettingsInternal();
        }
        else
        {
            SetPage(Page.Workspace);
        }
    }

    // Workspace Settings button
    public void OpenSettings()
    {
        returnPageAfterSettings = currentPage;
        firstTimeSetup = false;
        OpenSettingsInternal();
    }

    private void OpenSettingsInternal()
    {
        settingsUI?.LoadSavedValues();
        settingsUI?.SetFirstRun(firstTimeSetup);
        SetPage(Page.Settings);
    }

    // Settings Confirm button
    public void ConfirmSettings()
    {
        settingsUI?.SaveCurrentValues();
        if (firstTimeSetup)
        {
            PlayerPrefs.SetInt(SetupKey, 1);
            PlayerPrefs.Save();
            firstTimeSetup = false;
            SetPage(Page.Workspace);
        }
        else
        {
            SetPage(returnPageAfterSettings);
        }
    }

    // Settings Back button (hide it during first-run configuration)
    public void CancelSettings()
    {
        if (firstTimeSetup) return;
        settingsUI?.LoadSavedValues(); // restore preview, including volume/brightness
        SetPage(returnPageAfterSettings);
    }

    public void OpenArchive() => SetPage(Page.Archive);
    public void OpenResumeFromArchive() => SetPage(Page.Resume);
    public void BackToArchive() => SetPage(Page.Archive);
    public void BackToWorkspace() => SetPage(Page.Workspace);

    // Workspace "Initiate New Business Project"
    public void RequestNewBusinessProject()
    {
        if (unfinishedWorkflow)
        {
            if (unfinishedWorkflowDialog != null)
                unfinishedWorkflowDialog.SetActive(true);
            return;
        }
        onStartNewProject?.Invoke();
    }

    public void DismissUnfinishedWorkflowDialog()
    {
        if (unfinishedWorkflowDialog != null)
            unfinishedWorkflowDialog.SetActive(false);
    }

    public void ContinueCurrentWorkflow()
    {
        DismissUnfinishedWorkflowDialog();
        onResumeExistingWorkflow?.Invoke();
    }

    public void SetUnfinishedWorkflow(bool value) => unfinishedWorkflow = value;

    public void AskToExit()
    {
        if (quitConfirmationDialog != null)
            quitConfirmationDialog.SetActive(true);
    }

    public void CancelExit()
    {
        if (quitConfirmationDialog != null)
            quitConfirmationDialog.SetActive(false);
    }

    public void ConfirmExit() => Application.Quit();

    private void SetPage(Page page)
    {
        currentPage = page;
        SetActive(coverPanel, page == Page.Cover);
        SetActive(workspacePanel, page == Page.Workspace);
        SetActive(settingsPanel, page == Page.Settings);
        SetActive(archivePanel, page == Page.Archive);
        SetActive(resumePanel, page == Page.Resume);
        SetActive(unfinishedWorkflowDialog, false);
        SetActive(quitConfirmationDialog, false);
    }

    private static void SetActive(GameObject item, bool active)
    {
        if (item != null) item.SetActive(active);
    }
}
