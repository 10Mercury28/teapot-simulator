using TMPro;
using UnityEngine;

/// <summary>
/// Add to a TMP label to react to the prototype language dropdown.
/// Use for interface text only; story dialogue should eventually use Localization tables.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class OrientPearlLocalizedText : MonoBehaviour
{
    [TextArea(1, 4)] [SerializeField] private string simplifiedChinese;
    [TextArea(1, 4)] [SerializeField] private string english;
    private TMP_Text label;

    private void Awake() => label = GetComponent<TMP_Text>();
    private void OnEnable()
    {
        OrientPearlLanguage.LanguageChanged += Refresh;
        Refresh();
    }
    private void OnDisable() => OrientPearlLanguage.LanguageChanged -= Refresh;

    public void Refresh()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        label.text = OrientPearlLanguage.Current == 1 ? english : simplifiedChinese;
    }
}
