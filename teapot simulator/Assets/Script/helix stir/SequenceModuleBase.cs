using UnityEngine;

public abstract class SequenceModuleBase : MonoBehaviour
{
    public bool complete = false;

    public abstract void Initialize(GeneralSequenceController controller);

    // 当一个 module 完成时，必须调用 controller.OnModuleCompleted(this)
}