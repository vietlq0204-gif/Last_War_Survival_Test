using UnityEngine;

[DisallowMultipleComponent]
public sealed class DamageShakeFeedback : MonoBehaviour
{
    [SerializeField] private Transform targetTransform;
    [SerializeField] private bool useLocalSpace = true;
    [SerializeField] private bool useUnscaledTime;
    [SerializeField, Range(0f, 1f)] private float traumaPerHit = 0.35f;
    [SerializeField, Min(0.01f)] private float traumaDecayPerSecond = 2.5f;
    [SerializeField, Min(0.01f)] private float shakeFrequency = 35f;
    [SerializeField] private Vector3 maxOffset = new Vector3(0.1f, 0.1f, 0f);

    private float _trauma;
    private float _noiseTime;
    private float _noiseSeedX;
    private float _noiseSeedY;
    private float _noiseSeedZ;
    private Vector3 _currentOffset;

    private void Awake()
    {
        if (targetTransform == null)
            targetTransform = transform;

        float baseSeed = Random.value * 1000f;
        _noiseSeedX = baseSeed + 11.1f;
        _noiseSeedY = baseSeed + 23.7f;
        _noiseSeedZ = baseSeed + 41.3f;
    }

    private void OnDisable()
    {
        RestoreRestPosition();
        _trauma = 0f;
        _noiseTime = 0f;
    }

    private void LateUpdate()
    {
        Transform target = ResolveTargetTransform();
        if (target == null)
            return;

        Vector3 basePosition = GetCurrentPosition(target) - _currentOffset;
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (_trauma <= 0f || deltaTime <= 0f)
        {
            if (_currentOffset != Vector3.zero)
                ApplyPosition(target, basePosition);

            _currentOffset = Vector3.zero;
            return;
        }

        _noiseTime += deltaTime * shakeFrequency;
        float amplitude = _trauma * _trauma;
        Vector3 nextOffset = new Vector3(
            SampleNoise(_noiseSeedX) * maxOffset.x * amplitude,
            SampleNoise(_noiseSeedY) * maxOffset.y * amplitude,
            SampleNoise(_noiseSeedZ) * maxOffset.z * amplitude);

        ApplyPosition(target, basePosition + nextOffset);
        _currentOffset = nextOffset;
        _trauma = Mathf.Max(0f, _trauma - traumaDecayPerSecond * deltaTime);

        if (_trauma <= 0f)
        {
            ApplyPosition(target, basePosition);
            _currentOffset = Vector3.zero;
        }
    }

    public void PlayShake()
    {
        AddTrauma(traumaPerHit);
    }

    public void PlayShake(float traumaAmount)
    {
        AddTrauma(traumaAmount > 0f ? traumaAmount : traumaPerHit);
    }

    private void AddTrauma(float traumaAmount)
    {
        _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, traumaAmount));
    }

    private Transform ResolveTargetTransform()
    {
        if (targetTransform == null)
            targetTransform = transform;

        return targetTransform;
    }

    private void RestoreRestPosition()
    {
        Transform target = ResolveTargetTransform();
        if (target == null || _currentOffset == Vector3.zero)
            return;

        ApplyPosition(target, GetCurrentPosition(target) - _currentOffset);
        _currentOffset = Vector3.zero;
    }

    private Vector3 GetCurrentPosition(Transform target)
    {
        return useLocalSpace ? target.localPosition : target.position;
    }

    private void ApplyPosition(Transform target, Vector3 position)
    {
        if (useLocalSpace)
            target.localPosition = position;
        else
            target.position = position;
    }

    private float SampleNoise(float seed)
    {
        return (Mathf.PerlinNoise(seed, _noiseTime) - 0.5f) * 2f;
    }
}
