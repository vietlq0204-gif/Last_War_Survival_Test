using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BulletSpawnerHomeRelay : MonoBehaviour
{
    private readonly List<RelayRegistration> _registrations = new List<RelayRegistration>(2);

    private struct RelayRegistration
    {
        public BulletSpawner owner;
        public Collider sourceCollider;
    }

    public void Register(BulletSpawner owner, Collider sourceCollider)
    {
        if (owner == null || sourceCollider == null)
            return;

        for (int i = 0; i < _registrations.Count; i++)
        {
            RelayRegistration registration = _registrations[i];
            if (registration.owner == owner && registration.sourceCollider == sourceCollider)
                return;
        }

        _registrations.Add(new RelayRegistration
        {
            owner = owner,
            sourceCollider = sourceCollider,
        });
    }

    public void Unregister(BulletSpawner owner, Collider sourceCollider)
    {
        for (int i = _registrations.Count - 1; i >= 0; i--)
        {
            RelayRegistration registration = _registrations[i];
            if (registration.owner != owner || registration.sourceCollider != sourceCollider)
                continue;

            _registrations.RemoveAt(i);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Dispatch(other, isEnter: true);
    }

    private void OnTriggerExit(Collider other)
    {
        Dispatch(other, isEnter: false);
    }

    private void Dispatch(Collider other, bool isEnter)
    {
        for (int i = _registrations.Count - 1; i >= 0; i--)
        {
            RelayRegistration registration = _registrations[i];
            if (registration.owner == null || registration.sourceCollider == null)
            {
                _registrations.RemoveAt(i);
                continue;
            }

            if (isEnter)
                registration.owner.HandleHomeTriggerEnterFromRelay(registration.sourceCollider, other);
            else
                registration.owner.HandleHomeTriggerExitFromRelay(registration.sourceCollider, other);
        }
    }
}
