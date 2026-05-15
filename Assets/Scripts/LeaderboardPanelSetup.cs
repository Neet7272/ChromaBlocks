using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LeaderboardPanel prefab kökünde: GameScene'de ayrı LeaderboardManager objesi gerekmez;
/// panel spawn olunca manager + kapatma davranışını kurar.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class LeaderboardPanelSetup : MonoBehaviour
{
    void Awake()
    {
        if (GetComponent<LeaderboardPanelController>() == null)
            gameObject.AddComponent<LeaderboardPanelController>();

        var manager = GetComponent<LeaderboardManager>();
        if (manager == null)
            manager = gameObject.AddComponent<LeaderboardManager>();

        manager.TryAutoBindReferences();
        WireCloseButton();
    }

    void WireCloseButton()
    {
        var close = FindDeepChild(transform, "CloseButton");
        if (close == null)
            return;

        if (!close.TryGetComponent<Button>(out var btn))
            return;

        var controller = GetComponent<LeaderboardPanelController>();
        if (controller == null)
            return;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(controller.CloseLeaderboardPanel);
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindDeepChild(parent.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
