using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;

public sealed class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    public bool IsReady { get; private set; }

    Task _initTask;

    static readonly SemaphoreSlim s_AnonymousSignInGate = new(1, 1);
    static Task s_AnonymousSignInTask;

    static readonly object s_UnityServicesInitLock = new object();
    static Task s_UnityServicesInitTask;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Bir kez başlat.
        _initTask ??= InitializeAndSignInAsync();
    }

    /// <summary>
    /// Dışarıdan beklemek isteyenler için (opsiyonel).
    /// </summary>
    public Task EnsureReadyAsync()
    {
        _initTask ??= InitializeAndSignInAsync();
        return _initTask;
    }

    /// <summary>
    /// UGS Core'u başlatır (Editor'da birden fazla bileşen aynı anda çağırınca tek init).
    /// </summary>
    public static async Task EnsureUnityServicesInitializedAsync(CancellationToken ct = default)
    {
        if (UnityServices.State == ServicesInitializationState.Initialized)
        {
            ct.ThrowIfCancellationRequested();
            return;
        }

        Task initTask;
        lock (s_UnityServicesInitLock)
        {
            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                ct.ThrowIfCancellationRequested();
                return;
            }

            s_UnityServicesInitTask ??= InitializeUnityServicesCoreAsync();
            initTask = s_UnityServicesInitTask;
        }

        await initTask.ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
    }

    static async Task InitializeUnityServicesCoreAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            // Hiçbir ayar zorlaması yapmadan, Unity'nin kendi doğal ortam ayarıyla başlatıyoruz.
            await UnityServices.InitializeAsync();
        }
    }

    /// <summary>
    /// UGS çağrılarından önce: Core init + tekilleştirilmiş anonim oturum.
    /// Birden fazla yerden aynı anda SignInAnonymouslyAsync çağrılmasını engeller.
    /// </summary>
    public static async Task EnsureAnonymousSignedInAsync(CancellationToken ct = default)
    {
        await EnsureUnityServicesInitializedAsync(ct);

        await s_AnonymousSignInGate.WaitAsync(ct);
        try
        {
            if (AuthenticationService.Instance.IsSignedIn)
                return;

            if (s_AnonymousSignInTask != null)
            {
                var existing = s_AnonymousSignInTask;
                s_AnonymousSignInGate.Release();
                try
                {
                    await existing.ConfigureAwait(true);
                }
                finally
                {
                    await s_AnonymousSignInGate.WaitAsync(ct);
                }

                ct.ThrowIfCancellationRequested();
                return;
            }

            s_AnonymousSignInTask = AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        finally
        {
            s_AnonymousSignInGate.Release();
        }

        try
        {
            await s_AnonymousSignInTask.ConfigureAwait(true);
        }
        finally
        {
            await s_AnonymousSignInGate.WaitAsync(ct);
            try
            {
                s_AnonymousSignInTask = null;
            }
            finally
            {
                s_AnonymousSignInGate.Release();
            }
        }
    }

    async Task InitializeAndSignInAsync()
    {
        try
        {
            await EnsureAnonymousSignedInAsync();

            IsReady = AuthenticationService.Instance.IsSignedIn;
            if (IsReady)
                Debug.Log("[Auth] Signed in. PlayerId=" + AuthenticationService.Instance.PlayerId);
            else
                Debug.LogError("[Auth] Init finished but player is not signed in.");
        }
        catch (Exception e)
        {
            IsReady = false;
            Debug.LogError("[Auth] Init/SignIn failed: " + e);
        }
    }
}

