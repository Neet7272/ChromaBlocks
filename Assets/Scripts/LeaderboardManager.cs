using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Exceptions;
using Unity.Services.Leaderboards.Models;

public class LeaderboardManager : MonoBehaviour
{
    [Serializable]
    public class LeaderboardSlot
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text rankText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text scoreText;

        public void SetActive(bool value)
        {
            if (root != null) root.SetActive(value);
        }

        public void SetData(string rank, string name, string score)
        {
            if (rankText != null) rankText.text = rank;
            if (nameText != null) nameText.text = name;
            if (scoreText != null) scoreText.text = score;
        }
    }

    [Header("Refs")]
    [SerializeField] private TabController tabController;
    [SerializeField] private GameObject loadingRoot;
    [SerializeField] private TMP_Text loadingText;

    [Header("Leaderboard IDs")]
    [SerializeField] private string weeklyLeaderboardId = "WeeklyLeaderboard";
    [SerializeField] private string globalLeaderboardId = "GlobalLeaderboard";

    [Header("UI Slots")]
    [SerializeField] private List<LeaderboardSlot> topSlots = new(); // 10 slot
    [SerializeField] private LeaderboardSlot localPlayerSlot;

    CancellationTokenSource _cts;
    int _lastTab = -1;

    [SerializeField, Tooltip("Sekme: 0=Weekly, 1=Global (TabController yokken fallback)")]
    int _defaultTab = 0;

    private int testSkorum = 50000;

    void OnEnable()
    {
        if (tabController != null)
            tabController.OnTabChanged += HandleTabChanged;

        // Sahne a��l�r a��lmaz ilk tabloyu �ek.
        var initialTab = tabController != null ? tabController.CurrentTabIndex : 0;
        HandleTabChanged(initialTab);
    }

    void OnDisable()
    {
        if (tabController != null)
            tabController.OnTabChanged -= HandleTabChanged;

        CancelInFlight();
    }

    void HandleTabChanged(int tabIndex)
    {
        _lastTab = tabIndex;
        CancelInFlight();
        _cts = new CancellationTokenSource();
        _ = RefreshAsync(tabIndex, _cts.Token);
    }

    /// <summary>Weekly ve Global tablolara ayn? skoru g�nder; sonra a�?k sekmeyi yenile.</summary>
    public async Task SubmitScoreAsync(int newScore)
    {
        if (newScore < 0)
        {
            Debug.LogWarning("[Leaderboard] SubmitScore: negative score ignored.");
            return;
        }

        try
        {
            // TabController/Leaderboard ayn? frame'de iptal edilmesin diye ayr? token.
            using var cts = new CancellationTokenSource();
            await EnsureUgsReadyAsync(cts.Token);

            var score = (double)newScore;
            var w = LeaderboardsService.Instance.AddPlayerScoreAsync(weeklyLeaderboardId, score, null);
            var g = LeaderboardsService.Instance.AddPlayerScoreAsync(globalLeaderboardId, score, null);
            await Task.WhenAll(w, g);
            Debug.Log("[Leaderboard] Scores submitted to weekly & global. value=" + newScore);

            var active = tabController != null ? tabController.CurrentTabIndex : _defaultTab;
            active = Mathf.Clamp(active, 0, 1);
            HandleTabChanged(active);
        }
        catch (Exception e)
        {
            Debug.LogError("[Leaderboard] SubmitScore failed: " + e);
            throw;
        }
    }

    /// <summary>Inspector OnClick i�in: Task d�nen metotlar listelenmez; bu void sarmalay?c?y? kullan.</summary>
    public async void SubmitTestScoreFromButton()
    {
        testSkorum += 1500; // Her butona bastığında skoru 1500 artırır (51500, 53000, 54500...)
        await SubmitScoreAsync(testSkorum);
    }

    void CancelInFlight()
    {
        if (_cts == null) return;
        try { _cts.Cancel(); } catch { /* ignore */ }
        _cts.Dispose();
        _cts = null;
    }

    async Task RefreshAsync(int tabIndex, CancellationToken ct)
    {
        try
        {
            SetLoading(true, "Loading...");

            await EnsureUgsReadyAsync(ct);

            var leaderboardId = tabIndex == 0 ? weeklyLeaderboardId : globalLeaderboardId;

            // Sorgu 1: Top 10
            var topTask = FetchTop10Async(leaderboardId, ct);
            // Sorgu 2: Local player rank
            var meTask = FetchLocalPlayerAsync(leaderboardId, ct);

            await Task.WhenAll(topTask, meTask);
        }
        catch (OperationCanceledException)
        {
            // sekme h�zl� de�i�tiyse normal
        }
        catch (Exception e)
        {
            Debug.LogError("[Leaderboard] Refresh failed: " + e);
        }
        finally
        {
            // Bu istek eski kald�ysa loading'i kapatma (yeni istek a�m�� olabilir)
            if (!ct.IsCancellationRequested && tabIndex == _lastTab)
                SetLoading(false, null);
        }
    }

    async Task EnsureUgsReadyAsync(CancellationToken ct)
    {
        await AuthManager.EnsureAnonymousSignedInAsync(ct);
    }

    async Task FetchTop10Async(string leaderboardId, CancellationToken ct)
    {
        // offset=0, limit=10
        var scores = await LeaderboardsService.Instance.GetScoresAsync(
            leaderboardId,
            new GetScoresOptions { Offset = 0, Limit = 10 }
        );

        ct.ThrowIfCancellationRequested();

        var results = scores.Results ?? new List<LeaderboardEntry>();

        for (int i = 0; i < topSlots.Count; i++)
        {
            var slot = topSlots[i];
            if (slot == null) continue;

            if (i >= results.Count)
            {
                slot.SetActive(false);
                continue;
            }

            var entry = results[i];
            slot.SetActive(true);

            // Rank: UGS rank 1-based
            var rank = entry.Rank > 0 ? entry.Rank.ToString() : (i + 1).ToString();
            var name = !string.IsNullOrWhiteSpace(entry.PlayerName) ? entry.PlayerName : "Player";
            var score = entry.Score.ToString();

            slot.SetData(rank, name, score);
        }
    }

    async Task FetchLocalPlayerAsync(string leaderboardId, CancellationToken ct)
    {
        if (localPlayerSlot == null)
            return;

        try
        {
            var me = await LeaderboardsService.Instance.GetPlayerScoreAsync(leaderboardId);
            ct.ThrowIfCancellationRequested();

            // Local player rank/score
            var rank = me.Rank > 0 ? me.Rank.ToString() : "Unranked";
            var name = !string.IsNullOrWhiteSpace(me.PlayerName) ? me.PlayerName : "You";
            var score = me.Score.ToString();

            localPlayerSlot.SetActive(true);
            localPlayerSlot.SetData(rank, name, score);
        }
        catch (LeaderboardsException)
        {
            // Oyuncu listede yoksa genelde buraya d��er; UI'da Unranked g�ster.
            localPlayerSlot.SetActive(true);
            localPlayerSlot.SetData("Unranked", "You", "-");
        }
    }

    void SetLoading(bool value, string message)
    {
        if (loadingRoot != null)
            loadingRoot.SetActive(value);

        if (!string.IsNullOrEmpty(message) && loadingText != null)
            loadingText.text = message;
    }
}


