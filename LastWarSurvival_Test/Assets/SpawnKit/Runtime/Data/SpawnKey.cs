using System;
using UnityEngine;

namespace Vit.SpawnKit.Data
{
/// <summary>
/// Stable identifier for a spawnable.
/// </summary>
[Serializable]
public struct SpawnKey : IEquatable<SpawnKey>
{
    [SerializeField] private string _id;
    [SerializeField] private int _hash;

    public string Id => _id;
    public int Hash => _hash;

    public SpawnKey(string id)
    {
        _id = id;
        _hash = string.IsNullOrEmpty(id) ? 0 : Animator.StringToHash(id);
    }

    public void RecomputeHash()
    {
        _hash = string.IsNullOrEmpty(_id) ? 0 : Animator.StringToHash(_id);
    }

    public bool Equals(SpawnKey other) => _hash == other._hash;
    public override bool Equals(object obj) => obj is SpawnKey other && Equals(other);
    public override int GetHashCode() => _hash;

    public static bool operator ==(SpawnKey a, SpawnKey b) => a.Equals(b);
    public static bool operator !=(SpawnKey a, SpawnKey b) => !a.Equals(b);

    public override string ToString() => $"{_id}({_hash})";
}
}
