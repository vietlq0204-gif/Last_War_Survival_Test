using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class CameraFollowTaggedTarget : MonoBehaviour
{
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private bool preserveInitialOffset = true;
    [SerializeField, Min(0f)] private float followSmoothness = 10f;
    [SerializeField, Min(0.05f)] private float reacquireInterval = 0.25f;

    private Transform _target;
    private Vector3 _followOffset;
    private Quaternion _lockedRotation;
    private bool _hasOffset;
    private float _nextReacquireTime;

    private void Awake()
    {
        _lockedRotation = transform.rotation;
        TryResolveTarget(resetOffset: true);
    }

    private void LateUpdate()
    {
        if (!TryEnsureTarget())
            return;

        Vector3 desiredPosition = _target.position + _followOffset;
        if (followSmoothness <= 0f)
            transform.position = desiredPosition;
        else
            transform.position = Vector3.Lerp(
                transform.position,
                desiredPosition,
                1f - Mathf.Exp(-followSmoothness * Time.deltaTime));

        transform.rotation = _lockedRotation;
    }

    private bool TryEnsureTarget()
    {
        if (IsTargetValid(_target))
            return true;

        if (Time.unscaledTime < _nextReacquireTime)
            return false;

        _nextReacquireTime = Time.unscaledTime + Mathf.Max(0.05f, reacquireInterval);
        return TryResolveTarget(resetOffset: false);
    }

    private bool TryResolveTarget(bool resetOffset)
    {
        if (string.IsNullOrWhiteSpace(targetTag))
            return false;

        GameObject targetObject = GameObject.FindGameObjectWithTag(targetTag);
        if (targetObject == null)
            return false;

        _target = targetObject.transform;
        if (resetOffset || !_hasOffset)
        {
            _followOffset = preserveInitialOffset
                ? transform.position - _target.position
                : Vector3.zero;
            _hasOffset = true;
        }

        return true;
    }

    private static bool IsTargetValid(Transform candidate)
    {
        return candidate != null && candidate.gameObject.activeInHierarchy;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureMainCameraHasFollow()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        if (mainCamera.GetComponent<CameraFollowTaggedTarget>() != null)
            return;

        if (GameObject.FindGameObjectWithTag("Player") == null)
            return;

        mainCamera.gameObject.AddComponent<CameraFollowTaggedTarget>();
    }
}
