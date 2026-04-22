using UnityEngine;

/// <summary>
/// Leaderboard UI kapatma: butonun OnClick'ine <see cref="CloseLeaderboardPanel"/> bağlayın.
/// Script panel kökünde ise <see cref="panelRoot"/> boş bırakılabilir (kendi objesini kapatır).
/// </summary>
public sealed class LeaderboardPanelController : MonoBehaviour
{
    [Tooltip("Kapatılacak panel. Boşsa bu GameObject (scriptin olduğu obje) kapatılır.")]
    [SerializeField] GameObject panelRoot;

    public void CloseLeaderboardPanel()
    {
        var target = panelRoot != null ? panelRoot : gameObject;
        if (target != null)
            target.SetActive(false);
    }
}
