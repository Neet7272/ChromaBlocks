using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] GameObject settingsPanel;
    [SerializeField] GameObject leaderboardPanel;

    void Awake()
    {
        // Not: SettingsPanel inactive başlıyorsa GameObject.Find bulamaz.
        // Bu yüzden Canvas altından Transform.Find ile buluyoruz (inactive olsa da çalışır).
        if (settingsPanel == null)
            settingsPanel = FindSettingsPanelUnderCanvas();

        if (leaderboardPanel == null)
            leaderboardPanel = FindLeaderboardPanelUnderCanvas();
    }

    public void PlayButton()
    {
        Debug.Log("Play button clicked.");
        SceneManager.LoadScene("GameScene");
    }

    public void OpenSettings()
    {
        if (settingsPanel == null)
            settingsPanel = FindSettingsPanelUnderCanvas();

        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        if (settingsPanel == null)
            settingsPanel = FindSettingsPanelUnderCanvas();

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    /// <summary>Menüdeki High Score / Leaderboard butonuna bağlayın.</summary>
    public void OpenLeaderboard()
    {
        if (leaderboardPanel == null)
            leaderboardPanel = FindLeaderboardPanelUnderCanvas();

        if (leaderboardPanel != null)
            leaderboardPanel.SetActive(true);
    }

    /// <summary>İstersen başka yerden de çağırabilirsiniz (aynı panel referansı).</summary>
    public void CloseLeaderboard()
    {
        if (leaderboardPanel == null)
            leaderboardPanel = FindLeaderboardPanelUnderCanvas();

        if (leaderboardPanel != null)
            leaderboardPanel.SetActive(false);
    }

    static GameObject FindSettingsPanelUnderCanvas()
    {
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return null;

        // Canvas altındaki inactive objeler de burada bulunur.
        var t = canvas.transform.Find("SettingsPanel");
        return t != null ? t.gameObject : null;
    }

    static GameObject FindLeaderboardPanelUnderCanvas()
    {
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return null;

        var t = canvas.transform.Find("LeaderboardPanel");
        return t != null ? t.gameObject : null;
    }
}
