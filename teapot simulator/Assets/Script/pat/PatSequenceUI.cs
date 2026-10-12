using UnityEngine;
using UnityEngine.UI;
using System.Text;

public class PatSequenceUI : MonoBehaviour
{
    [Header("References")]
    public PatSequenceController sequence;
    public Text debugText;

    [Header("Options")]
    public bool showDebug = true;

    void Update()
    {
        if (!showDebug || debugText == null || sequence == null || sequence.modules == null)
            return;

        StringBuilder sb = new StringBuilder();

        sb.AppendLine($"[PatSequence] allComplete = {sequence.allComplete}");

        for (int i = 0; i < sequence.modules.Length; i++)
        {
            var m = sequence.modules[i];
            if (m == null)
            {
                sb.AppendLine($"[{i}] <null>");
                continue;
            }

            bool inTrans = m.inTransition;
            int hits = m.CurrentHits;

            string status = m.complete ? "DONE" :
                (m.active ? "ACTIVE" : "IDLE");

            sb.AppendLine(
                $"[{i}] {m.name} | {status} | hits={hits} | inTrans={inTrans} | goActive={m.gameObject.activeSelf}"
            );
        }

        debugText.text = sb.ToString();
    }
}