using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

/// <summary>
/// İki aşamalı Game Over: ilk kayıp (revive) + revive sonrası kayıp (ayrı görsel).
/// </summary>
public sealed class GameOverManager : MonoBehaviour
{
    const string PrefBestScore = "ChromaBlocks_BestScore";

    [Header("Paneller")]
    [Tooltip("İlk game over — Revive butonu burada.")]
    public GameObject gameOverPanelFirst;
    [Tooltip("Revive kullanıldıktan sonraki game over — yeni görselin.")]
    public GameObject gameOverPanelFinal;
    [Tooltip("Eski alan: boşsa gameOverPanelFirst ile aynı kabul edilir.")]
    public GameObject gameOverPanel;

    [Header("Skor — First panel")]
    public TextMeshProUGUI[] scoreDigits;
    public TextMeshProUGUI bestScoreText;

    [Header("Skor — Final panel (boşsa Final kökünden otomatik bulunur)")]
    public TextMeshProUGUI[] scoreDigitsFinal;
    public TextMeshProUGUI bestScoreTextFinal;

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

    GameObject _activePanelRoot;
    CanvasGroup _panelCanvasGroup;
    RectTransform _panelRect;
    RectTransform _replayPulseTarget;
    TextMeshProUGUI[] _activeScoreDigits;
    TextMeshProUGUI _activeBestScoreText;

    Sequence _introSequence;
    Tween _scoreTween;
    Tween _replayPulseTween;

    void Awake()
    {
        DisableDuplicateManagersOnOtherObjects();

        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (shapeSpawner == null)
            shapeSpawner = FindAnyObjectByType<ShapeSpawner>();
        if (uiManager == null)
            uiManager = FindAnyObjectByType<UIManager>();

        ResolvePanelReferences();
        _activeScoreDigits = scoreDigits;
        _activeBestScoreText = bestScoreText;
        PreparePanelHidden();
    }

    /// <summary>Final panelde yanlışlıkla ikinci GameOverManager varsa kapat.</summary>
    void DisableDuplicateManagersOnOtherObjects()
    {
        var all = FindObjectsByType<GameOverManager>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            var other = all[i];
            if (other == null || other == this)
                continue;

            if (other.enabled)
            {
                DevelopmentDiagnostics.LogWarning(
                    "[GameOverManager] Yinelenen bileşen kapatıldı: " + other.gameObject.name, other);
                other.enabled = false;
            }
        }
    }

    void ResolvePanelReferences()
    {
        if (gameOverPanelFirst == null && gameOverPanel != null)
            gameOverPanelFirst = gameOverPanel;

        AutoFindPanelsUnderCanvas();
    }

    /// <summary>Inspector boşsa Canvas altında isimle bul (GameOverPanel_First / _Final).</summary>
    void AutoFindPanelsUnderCanvas()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        if (gameOverPanelFirst == null)
            gameOverPanelFirst = FindPanelByName(canvas.transform, "GameOverPanel_First");

        if (gameOverPanelFinal == null)
            gameOverPanelFinal = FindPanelByName(canvas.transform, "GameOverPanel_Final");
    }

    static GameObject FindPanelByName(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrEmpty(objectName))
            return null;

        if (root.name == objectName)
            return root.gameObject;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindPanelByName(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    GameObject SelectPanelForCurrentGameOver()
    {
        ResolvePanelReferences();
        if (hasRevivedThisGame && gameOverPanelFinal != null)
            return gameOverPanelFinal;
        return gameOverPanelFirst;
    }

    void CachePanelRefs(GameObject panelRoot)
    {
        _activePanelRoot = panelRoot;
        if (panelRoot == null)
            return;

        _panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
        if (_panelCanvasGroup == null)
            _panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();

        _panelRect = panelRoot.transform as RectTransform;
        _replayPulseTarget = btnReplay != null ? btnReplay.transform as RectTransform : null;
        CacheScoreUiForPanel(panelRoot);
    }

    void CacheScoreUiForPanel(GameObject panelRoot)
    {
        _activeScoreDigits = scoreDigits;
        _activeBestScoreText = bestScoreText;

        if (panelRoot != gameOverPanelFinal)
            return;

        if (scoreDigitsFinal != null && scoreDigitsFinal.Length >= 5)
            _activeScoreDigits = scoreDigitsFinal;

        if (TryFindScoreDigitsInPanel(panelRoot, out var autoDigits))
            _activeScoreDigits = autoDigits;

        if (bestScoreTextFinal != null)
            _activeBestScoreText = bestScoreTextFinal;
        else if (TryFindBestScoreLabelInPanel(panelRoot, out var autoBest))
            _activeBestScoreText = autoBest;
    }

    static bool TryFindScoreDigitsInPanel(GameObject panelRoot, out TextMeshProUGUI[] digits)
    {
        digits = null;
        if (panelRoot == null)
            return false;

        var tmps = panelRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
        var slots = new List<TextMeshProUGUI>(5);
        for (int i = 0; i < tmps.Length; i++)
        {
            var t = tmps[i];
            if (t == null)
                continue;

            var n = t.name;
            if (n.IndexOf("best", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (n.StartsWith("Score", System.StringComparison.OrdinalIgnoreCase))
                slots.Add(t);
        }

        if (slots.Count < 5)
            return false;

        slots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        digits = new TextMeshProUGUI[5];
        for (int i = 0; i < 5; i++)
            digits[i] = slots[i];
        return true;
    }

    static bool TryFindBestScoreLabelInPanel(GameObject panelRoot, out TextMeshProUGUI bestLabel)
    {
        bestLabel = null;
        if (panelRoot == null)
            return false;

        var tmps = panelRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            var t = tmps[i];
            if (t != null && t.name.IndexOf("best", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bestLabel = t;
                return true;
            }
        }

        return false;
    }

    /// <summary>Manager panelin üzerindeyse objeyi kapatma; yalnızca alpha ile gizle.</summary>
    void HidePanelVisual(GameObject panel)
    {
        if (panel == null)
            return;

        if (panel == gameObject)
        {
            var cg = panel.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = panel.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;
            return;
        }

        panel.SetActive(false);
    }

    void ShowPanelVisual(GameObject panel)
    {
        if (panel == null)
            return;

        if (!panel.activeSelf)
            panel.SetActive(true);

        if (panel == gameObject)
        {
            var cg = panel.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = panel.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = true;
            cg.interactable = true;
        }
    }

    void HideAllGameOverPanels()
    {
        HidePanelVisual(gameOverPanelFirst);
        HidePanelVisual(gameOverPanelFinal);
    }

    void OnDisable()
    {
        KillTweens();
    }

    /// <summary>ShapeSpawner.EnterGameOver → buradan çağrılır.</summary>
    public void EnterGameOver()
    {
        ResolvePanelReferences();
        HideAllGameOverPanels();

        var panel = SelectPanelForCurrentGameOver();
        if (panel == null)
        {
            DevelopmentDiagnostics.LogError(
                "[GameOverManager] gameOverPanelFirst / gameOverPanelFinal atanmamış.", this);
            return;
        }

        if (hasRevivedThisGame && gameOverPanelFinal == null)
            DevelopmentDiagnostics.LogWarning(
                "[GameOverManager] Revive sonrası kayıp: gameOverPanelFinal Inspector'da atanmalı.", this);

        CachePanelRefs(panel);

        ClearSaveDataBeforeSceneChange();

        KillTweens();
        SetPhysicsRaycastersEnabled(false);

        ShowPanelVisual(panel);

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

        DevelopmentDiagnostics.Log(hasRevivedThisGame
            ? "[GameOverManager] Final panel (revive tükendi)."
            : "[GameOverManager] İlk panel (revive mümkün).");
    }

    /// <summary>Başlangıçta paneli gizle — manager aynı objedeyse yalnızca alpha ile.</summary>
    void PreparePanelHidden()
    {
        KillTweens();
        HideAllGameOverPanels();

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

        // Revive yalnızca ilk game over panelinde; final panelde asla gösterme.
        var showRevive = !hasRevivedThisGame && _activePanelRoot == gameOverPanelFirst;
        btnRevive.gameObject.SetActive(showRevive);
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

        if (_activeBestScoreText != null)
            _activeBestScoreText.text = best.ToString();
        else if (bestScoreText != null)
            bestScoreText.text = best.ToString();
    }

    void AnimateScoreCount()
    {
        if (_activeScoreDigits == null || _activeScoreDigits.Length < 5)
        {
            DevelopmentDiagnostics.LogWarning("[GameOverManager] Aktif panelde scoreDigits (5 slot) bulunamadı.", this);
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
                    if (_activeScoreDigits[i] != null)
                        _activeScoreDigits[i].text = scoreString[i].ToString();
                }
            })
            .OnComplete(() =>
            {
                tempScore = targetScore;
                var scoreString = tempScore.ToString("D5");
                for (int i = 0; i < 5; i++)
                {
                    if (_activeScoreDigits[i] != null)
                        _activeScoreDigits[i].text = scoreString[i].ToString();
                }

                var punch = new Vector3(0.2f, 0.2f, 0f);
                for (int i = 0; i < _activeScoreDigits.Length; i++)
                {
                    var d = _activeScoreDigits[i];
                    if (d == null)
                        continue;
                    d.transform.DOKill(true);
                    d.transform.DOPunchScale(punch, 0.2f, 5, 0.5f).SetUpdate(true);
                }
            });
    }

    void ApplySlotDigits(int value)
    {
        if (_activeScoreDigits == null)
            return;

        var scoreString = Mathf.Max(0, value).ToString("D5");
        for (int i = 0; i < 5 && i < _activeScoreDigits.Length; i++)
        {
            if (_activeScoreDigits[i] != null)
                _activeScoreDigits[i].text = scoreString[i].ToString();
        }
    }

    void KillScoreDigitTweens()
    {
        if (_activeScoreDigits == null)
            return;

        for (int i = 0; i < _activeScoreDigits.Length; i++)
        {
            var d = _activeScoreDigits[i];
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
        HideAllGameOverPanels();

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
        ResetReviveSession();
        ClearSaveDataBeforeSceneChange();
        if (uiManager != null)
            uiManager.RestartGame();
    }

    public void OnHomeButtonClicked()
    {
        PlayUiClick();
        ResetReviveSession();
        ClearSaveDataBeforeSceneChange();
        if (uiManager != null)
            uiManager.GoToMainMenu();
    }

    /// <summary>Yeni oyun / sahne yenileme: revive hakkı sıfırlanır.</summary>
    public void ResetReviveSession()
    {
        hasRevivedThisGame = false;
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
            DevelopmentDiagnostics.LogWarning("[GameOverManager] Leaderboard skor gönderimi başarısız: " + e.Message);
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
