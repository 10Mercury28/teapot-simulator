using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Functional prototype for UI settings. Assign Slider values and overlay in Inspector.
/// Language is saved but needs a Localization/String Table binding to change actual game text.
/// Brightness uses a non-interactable black overlay; it does not change monitor hardware brightness.
/// </summary>
public sealed class OrientPearlSettingsUI : MonoBehaviour
{
    private const string LanguageKey = "OP.UI.Language";
    private const string BrightnessKey = "OP.UI.Brightness";
    private const string VolumeKey = "OP.UI.MasterVolume";
    private const string SubtitlesKey = "OP.UI.Subtitles";

    [Header("UI fields")]
    [SerializeField] private TMP_Dropdown languageDropdown; // 0 = 简体中文; 1 = English
    [SerializeField] private Slider brightnessSlider;       // min 0.30, max 1.00
    [SerializeField] private Slider volumeSlider;           // min 0.00, max 1.00
    [SerializeField] private Toggle subtitlesToggle;
    [SerializeField] private TMP_Text brightnessPercent;
    [SerializeField] private TMP_Text volumePercent;
    [SerializeField] private GameObject firstRunOnlyText;
    [SerializeField] private GameObject backButton;

    [Header("Runtime effects")]
    [SerializeField] private Image brightnessOverlay;       // full-screen black, raycastTarget = false
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string masterVolumeParameter = "MasterVolume";

    private bool initialized;

    private void Awake()
    {
        if (languageDropdown != null) languageDropdown.onValueChanged.AddListener(OrientPearlLanguage.Select);
        if (brightnessSlider != null) brightnessSlider.onValueChanged.AddListener(_ => Preview());
        if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(_ => Preview());
        if (subtitlesToggle != null) subtitlesToggle.onValueChanged.AddListener(_ => Preview());
    }

    private void Start()
    {
        // Initialize audio mixer after Awake; official Unity docs advise against SetFloat from Awake.
        LoadSavedValues();
    }

    public void SetFirstRun(bool firstRun)
    {
        if (firstRunOnlyText != null) firstRunOnlyText.SetActive(firstRun);
        if (backButton != null) backButton.SetActive(!firstRun);
    }

    public void LoadSavedValues()
    {
        if (languageDropdown != null)
            languageDropdown.SetValueWithoutNotify(PlayerPrefs.GetInt(LanguageKey, 0));
        OrientPearlLanguage.Select(PlayerPrefs.GetInt(LanguageKey, 0));
        if (brightnessSlider != null)
        {
            brightnessSlider.minValue = 0.3f;
            brightnessSlider.maxValue = 1f;
            brightnessSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(BrightnessKey, 0.7f));
        }
        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(VolumeKey, 0.8f));
        }
        if (subtitlesToggle != null)
            subtitlesToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(SubtitlesKey, 1) != 0);

        initialized = true;
        Preview();
    }

    public void SaveCurrentValues()
    {
        if (languageDropdown != null) PlayerPrefs.SetInt(LanguageKey, languageDropdown.value);
        if (brightnessSlider != null) PlayerPrefs.SetFloat(BrightnessKey, brightnessSlider.value);
        if (volumeSlider != null) PlayerPrefs.SetFloat(VolumeKey, volumeSlider.value);
        if (subtitlesToggle != null) PlayerPrefs.SetInt(SubtitlesKey, subtitlesToggle.isOn ? 1 : 0);
        PlayerPrefs.Save();
        Preview();
    }

    private void Preview()
    {
        if (!initialized) return;
        if (brightnessSlider != null)
        {
            float brightness = brightnessSlider.value;
            if (brightnessPercent != null)
                brightnessPercent.text = Mathf.RoundToInt(brightness * 100f) + "%";
            if (brightnessOverlay != null)
            {
                Color c = Color.black;
                c.a = Mathf.Lerp(0.55f, 0f, Mathf.InverseLerp(0.3f, 1f, brightness));
                brightnessOverlay.color = c;
                brightnessOverlay.raycastTarget = false;
            }
        }
        if (volumeSlider != null)
        {
            float value = volumeSlider.value;
            if (volumePercent != null)
                volumePercent.text = Mathf.RoundToInt(value * 100f) + "%";
            if (audioMixer != null)
            {
                float dB = Mathf.Clamp(20f * Mathf.Log10(Mathf.Max(0.0001f, value)), -80f, 0f);
                audioMixer.SetFloat(masterVolumeParameter, dB);
            }
        }
    }
}
