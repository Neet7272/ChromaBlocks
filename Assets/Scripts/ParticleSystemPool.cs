using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>ParticleSystem için basit havuz — Instantiate/Destroy yerine SetActive döngüsü.</summary>
public sealed class ParticleSystemPool : MonoBehaviour
{
    [SerializeField, Min(1)] int prewarmCount = 6;

    ParticleSystem _prefab;
    readonly List<ParticleSystem> _inactive = new();

    public void Initialize(ParticleSystem prefab, int warmCount = -1, Transform poolParent = null)
    {
        if (prefab == null)
            return;

        _prefab = prefab;
        var parent = poolParent != null ? poolParent : transform;
        var count = warmCount >= 0 ? warmCount : prewarmCount;

        while (_inactive.Count < count)
            ReturnToPool(CreateInstance(parent));
    }

    public void PlayAt(Vector3 position, Quaternion rotation, Action<ParticleSystem> configure = null)
    {
        if (_prefab == null)
            return;

        var ps = Rent();
        ps.transform.SetParent(null);
        ps.transform.SetPositionAndRotation(position, rotation);
        ps.transform.localScale = Vector3.one;

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        configure?.Invoke(ps);

        ps.gameObject.SetActive(true);
        ps.Play(true);

        var lifetime = Mathf.Max(main.duration, main.startLifetime.constantMax, 0.05f);
        StartCoroutine(ReturnAfter(ps, lifetime));
    }

    ParticleSystem Rent()
    {
        if (_inactive.Count > 0)
        {
            var ps = _inactive[_inactive.Count - 1];
            _inactive.RemoveAt(_inactive.Count - 1);
            return ps;
        }

        return CreateInstance(transform);
    }

    ParticleSystem CreateInstance(Transform parent)
    {
        var ps = Instantiate(_prefab, parent);
        ps.gameObject.SetActive(false);
        return ps;
    }

    void ReturnToPool(ParticleSystem ps)
    {
        if (ps == null)
            return;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.gameObject.SetActive(false);
        ps.transform.SetParent(transform);
        _inactive.Add(ps);
    }

    IEnumerator ReturnAfter(ParticleSystem ps, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (ps != null && ps.gameObject.activeSelf)
            ReturnToPool(ps);
    }
}
