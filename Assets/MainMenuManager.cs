using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    const string PrefOpenLeaderboardOnMenu = "ChromaBlocks_OpenLeaderboardOnMenuLoad";

    [SerializeField] GameObject settingsPanel;
    [SerializeField] GameObject leaderboardPanel;
    [SerializeField] GameObject creditsPanel;

    [Header("Global dokunma partikül (ana menü bootstrap)")]
    [SerializeField] ParticleSystem touchParticlePrefab;
    [SerializeField] Camera touchParticleWorldCamera;

    void Awake()
    {
        GamePerformanceSettings.Apply();

        // Not: SettingsPanel inactive başlıyorsa GameObject.Find bulamaz.
        // Bu yüzden Canvas altından Transform.Find ile buluyoruz (inactive olsa da çalışır).
        if (settingsPanel == null)
            settingsPanel = FindSettingsPanelUnderCanvas();

        if (leaderboardPanel == null)
            leaderboardPanel = FindLeaderboardPanelUnderCanvas();

        if (creditsPanel == null)
            creditsPanel = FindCreditsPanelUnderCanvas();

        GlobalTouchManager.EnsureExists(touchParticlePrefab, touchParticleWorldCamera);
    }

    void Start()
    {
        if (PlayerPrefs.GetInt(PrefOpenLeaderboardOnMenu, 0) != 1)
            return;

        PlayerPrefs.DeleteKey(PrefOpenLeaderboardOnMenu);
        PlayerPrefs.Save();
        OpenLeaderboard();
    }

    public void PlayButton()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
        Debug.Log("Play button clicked.");
        SceneManager.LoadScene("GameScene");
    }

    public void OpenSettings()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
        if (settingsPanel == null)
            settingsPanel = FindSettingsPanelUnderCanvas();

        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
        if (settingsPanel == null)
            settingsPanel = FindSettingsPanelUnderCanvas();

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    /// <summary>Menüdeki High Score / Leaderboard butonuna bağlayın.</summary>
    public void OpenLeaderboard()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
        if (leaderboardPanel == null)
            leaderboardPanel = FindLeaderboardPanelUnderCanvas();

        if (leaderboardPanel != null)
            leaderboardPanel.SetActive(true);
    }

    /// <summary>İstersen başka yerden de çağırabilirsiniz (aynı panel referansı).</summary>
    public void CloseLeaderboard()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
        if (leaderboardPanel == null)
            leaderboardPanel = FindLeaderboardPanelUnderCanvas();

        if (leaderboardPanel != null)
            leaderboardPanel.SetActive(false);
    }

    /// <summary>Menüdeki Credits butonuna bağlayın.</summary>
    public void OpenCredits()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
        if (creditsPanel == null)
            creditsPanel = FindCreditsPanelUnderCanvas();

        if (creditsPanel != null)
            creditsPanel.SetActive(true);
    }

    public void CloseCredits()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
        if (creditsPanel == null)
            creditsPanel = FindCreditsPanelUnderCanvas();

        if (creditsPanel != null)
            creditsPanel.SetActive(false);
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

    static GameObject FindCreditsPanelUnderCanvas()
    {
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return null;

        var t = canvas.transform.Find("Creditspanel");
        if (t == null)
            t = canvas.transform.Find("CreditsPanel");
        return t != null ? t.gameObject : null;
    }
}
