#if UNITY_EDITOR_WIN

using System.Windows.Forms;
using UnityEngine;

public class CopyIPButton : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Button button;
    private void Awake() => button.onClick.AddListener(() => Clipboard.SetText(Networker.GetPublicIPAddress()));
}

#endif