using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

/// <summary>
/// Matte Structural Game Over paneli + tek seferlik Revive.
/// </summary>
public sealed class GameOverManager : MonoBehaviour
{
    const string PrefBestScore = "ChromaBlocks_BestScore";

    [Header("Panel")]
    public GameObject gameOverPanel;

    [Header("Skor (slot — 5 ayrı rakam TMP)")]
    public TextMeshProUGUI[] scoreDigits;
    public TextMeshProUGUI bestScoreText;

    [Header("Butonlar")]
    public Button btnRevive;
    public Button btnReplay;
    public Button btnHome;
    public Button btnLeaderboard;

    [Header("Bağlantılar")]
    [SerializeField] GridManager gridManager;
    [SerializeField] ShapeSpawner shapeSpawner;
    [SerializeField] UIManager uiManager;

    [Header("Animasyon")]
    [SerializeField] float panelIntroDuration = 0.5f;
    [SerializeField] float panelStartScale = 0.8f;
    [SerializeField] float scoreCountDelay = 0.2f;
    [SerializeField] float scoreCountDuration = 1f;
    [SerializeField] float replayPulseScale = 1.06f;
    [SerializeField] float replayPulseDuration = 0.55f;

    [Header("Etkileşim")]
    [SerializeField] Physics2DRaycaster[] disablePhysicsRaycastersOnGameOver;

    Physics2DRaycaster _fallbackPhysicsRaycaster;

    bool hasRevivedThisGame;

    CanvasGroup _panelCanvasGroup;
    RectTransform _panelRect;
    RectTransform _replayPulseTarget;

    Sequence _introSequence;
    Tween _scoreTween;
    Tween _replayPulseTween;

    void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (shapeSpawner == null)
            shapeSpawner = FindAnyObjectByType<ShapeSpawner>();
        if (uiManager == null)
            uiManager = FindAnyObjectByType<UIManager>();

        CachePanelRefs();
        PreparePanelHidden();
    }

    void CachePanelRefs()
    {
        if (gameOverPanel == null)
            return;

        _panelCanvasGroup = gameOverPanel.GetComponent<CanvasGroup>();
        if (_panelCanvasGroup == null)
            _panelCanvasGroup = gameOverPanel.AddComponent<CanvasGroup>();

        _panelRect = gameOverPanel.transform as RectTransform;
        _replayPulseTarget = btnReplay != null ? btnReplay.transform as RectTransform : null;
    }

    void OnDisable()
    {
        KillTweens();
    }

    /// <summary>ShapeSpawner.EnterGameOver → buradan çağrılır.</summary>
    public void EnterGameOver()
    {
        CachePanelRefs();

        if (gameOverPanel == null)
        {
            Debug.LogError("[GameOverManager] gameOverPanel atanmamış.", this);
            return;
        }

        ClearSaveDataBeforeSceneChange();

        KillTweens();
        SetPhysicsRaycastersEnabled(false);

        if (!gameOverPanel.activeSelf)
            gameOverPanel.SetActive(true);

        if (_panelCanvasGroup != null)
        {
            _panelCanvasGroup.blocksRaycasts = true;
            _panelCanvasGroup.interactable = true;
            _panelCanvasGroup.alpha = 0f;
        }

        if (_panelRect != null)
            _panelRect.localScale = Vector3.one * panelStartScale;

        if (_replayPulseTarget != null)
            _replayPulseTarget.localScale = Vector3.one;

        UpdateReviveButtonVisibility();
        RefreshBestScoreLabel();
        AnimateScoreCount();
        PlayPanelIntro();
        TrySubmitScoreToLeaderboard();

        Debug.Log("[GameOverManager] Panel gösterildi.");
    }

    /// <summary>Başlangıçta paneli gizle — manager aynı objedeyse yalnızca alpha ile.</summary>
    void PreparePanelHidden()
    {
        KillTweens();

        if (gameOverPanel == null)
            return;

        if (gameOverPanel != gameObject)
            gameOverPanel.SetActive(false);

        if (_panelCanvasGroup != null)
        {
            _panelCanvasGroup.alpha = 0f;
            _panelCanvasGroup.blocksRaycasts = false;
            _panelCanvasGroup.interactable = false;
        }

        if (_panelRect != null)
            _panelRect.localScale = Vector3.one * panelStartScale;

        if (_replayPulseTarget != null)
            _replayPulseTarget.localScale = Vector3.one;
    }

    void UpdateReviveButtonVisibility()
    {
        if (btnRevive == null)
            return;

        btnRevive.gameObject.SetActive(!hasRevivedThisGame);
    }

    void RefreshBestScoreLabel()
    {
        int current = gridManager != null ? gridManager.CurrentScore : 0;
        int best = PlayerPrefs.GetInt(PrefBestScore, 0);

        if (current > best)
        {
            best = current;
            PlayerPrefs.SetInt(PrefBestScore, best);
            PlayerPrefs.Save();
        }

        if (bestScoreText != null)
            bestScoreText.text = best.ToString();
    }

    void AnimateScoreCount()
    {
        if (scoreDigits == null || scoreDigits.Length < 5)
        {
            Debug.LogWarning("[GameOverManager] scoreDigits atanmalı ve en az 5 elemanlı olmalı.", this);
            return;
        }

        int targetScore = gridManager != null ? gridManager.CurrentScore : 0;

        KillScoreDigitTweens();
        ApplySlotDigits(0);

        int tempScore = 0;
        _scoreTween = DOTween.To(() => tempScore, v => tempScore = v, targetScore, scoreCountDuration)
            .SetDelay(scoreCountDelay)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .OnUpdate(() =>
            {
                var scoreString = tempScore.ToString("D5");
                for (int i = 0; i < 5; i++)
                {
                    if (scoreDigits[i] != null)
                        scoreDigits[i].text = scoreString[i].ToString();
                }
            })
            .OnComplete(() =>
            {
                tempScore = targetScore;
                var scoreString = tempScore.ToString("D5");
                for (int i = 0; i < 5; i++)
                {
                    if (scoreDigits[i] != null)
                        scoreDigits[i].text = scoreString[i].ToString();
                }

                var punch = new Vector3(0.2f, 0.2f, 0f);
                for (int i = 0; i < scoreDigits.Length; i++)
                {
                    var d = scoreDigits[i];
                    if (d == null)
                        continue;
                    d.transform.DOKill(true);
                    d.transform.DOPunchScale(punch, 0.2f, 5, 0.5f).SetUpdate(true);
                }
            });
    }

    void ApplySlotDigits(int value)
    {
        var scoreString = Mathf.Max(0, value).ToString("D5");
        for (int i = 0; i < 5 && i < scoreDigits.Length; i++)
        {
            if (scoreDigits[i] != null)
                scoreDigits[i].text = scoreString[i].ToString();
        }
    }

    void KillScoreDigitTweens()
    {
        if (scoreDigits == null)
            return;

        for (int i = 0; i < scoreDigits.Length; i++)
        {
            var d = scoreDigits[i];
            if (d == null)
                continue;
            d.DOKill();
            d.transform.DOKill(true);
        }
    }

    void PlayPanelIntro()
    {
        _introSequence = DOTween.Sequence().SetUpdate(true);

        if (_panelCanvasGroup != null)
            _introSequence.Join(_panelCanvasGroup.DOFade(1f, panelIntroDuration));

        if (_panelRect != null)
        {
            _introSequence.Join(
                _panelRect
                    .DOScale(1f, panelIntroDuration)
                    .SetEase(Ease.OutBack));
        }

        _introSequence.OnComplete(StartReplayPulse);
    }

    void StartReplayPulse()
    {
        if (_replayPulseTarget == null)
            return;

        _replayPulseTarget.DOKill();
        _replayPulseTarget.localScale = Vector3.one;
        _replayPulseTween = _replayPulseTarget
            .DOScale(replayPulseScale, replayPulseDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    /// <summary>Reklam SDK başarı callback'inden çağır.</summary>
    public void OnReviveButtonClicked()
    {
        if (hasRevivedThisGame)
            return;

        hasRevivedThisGame = true;
        KillTweens();

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        SetPhysicsRaycastersEnabled(true);

        if (shapeSpawner != null)
        {
            shapeSpawner.ClearGameOverState();
            shapeSpawner.ReviveTray();
        }
    }

    public void OnReplayButtonClicked()
    {
        PlayUiClick();
        ClearSaveDataBeforeSceneChange();
        if (uiManager != null)
            uiManager.RestartGame();
    }

    public void OnHomeButtonClicked()
    {
        PlayUiClick();
        ClearSaveDataBeforeSceneChange();
        if (uiManager != null)
            uiManager.GoToMainMenu();
    }

    public void OnLeaderboardButtonClicked()
    {
        PlayUiClick();
        if (uiManager != null)
            uiManager.OpenLeaderboard();
    }

    void SetPhysicsRaycastersEnabled(bool enabled)
    {
        if (disablePhysicsRaycastersOnGameOver != null)
        {
            foreach (var r in disablePhysicsRaycastersOnGameOver)
            {
                if (r != null)
                    r.enabled = enabled;
            }
        }

        if (_fallbackPhysicsRaycaster == null)
            _fallbackPhysicsRaycaster = FindAnyObjectByType<Physics2DRaycaster>();

        if (_fallbackPhysicsRaycaster != null)
            _fallbackPhysicsRaycaster.enabled = enabled;
    }

    async void TrySubmitScoreToLeaderboard()
    {
        int score = gridManager != null ? gridManager.CurrentScore : 0;
        if (score <= 0)
            return;

        var leaderboard = FindAnyObjectByType<LeaderboardManager>(FindObjectsInactive.Include);
        if (leaderboard == null)
            return;

        try
        {
            await leaderboard.SubmitScoreAsync(score);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[GameOverManager] Leaderboard skor gönderimi başarısız: " + e.Message);
        }
    }

    static void PlayUiClick()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
    }

    /// <summary>Oyun bittiğinde veya sahne değişmeden önce: bitmiş tahta tekrar yüklenmesin.</summary>
    static void ClearSaveDataBeforeSceneChange()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.ClearSaveData();
        else
            SaveManager.DeleteSaveFile();
    }

    void KillTweens()
    {
        _introSequence?.Kill();
        _introSequence = null;
        _scoreTween?.Kill();
        _scoreTween = null;
        _replayPulseTween?.Kill();
        _replayPulseTween = null;

        KillScoreDigitTweens();

        if (_panelCanvasGroup != null)
            _panelCanvasGroup.DOKill();
        if (_panelRect != null)
            _panelRect.DOKill();
        if (_replayPulseTarget != null)
            _replayPulseTarget.DOKill();
    }
}
