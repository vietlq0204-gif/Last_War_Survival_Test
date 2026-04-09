using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public sealed class EnemyHealthBarWorld : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Image fillImage;
    [SerializeField] private Slider fillSlider;
    [SerializeField] private Scrollbar fillScrollbar;
    [SerializeField] private TMP_Text healthText;

    [Header("Behaviour")]
    [SerializeField] private bool faceMainCamera = true;
    [SerializeField] private string healthTextFormat = "{0}";

    private Transform _visualRoot;
    private Transform _targetRoot;
    private Transform _cameraTransform;
    private Vector3 _worldOffset;

    private Renderer[] _cachedRenderers;
    private Collider[] _cachedColliders;

    public void Bind(Enemy enemy)
    {
        if (enemy == null)
            return;

        _targetRoot = enemy.transform;
        _cachedRenderers = enemy.GetComponentsInChildren<Renderer>(true);
        _cachedColliders = enemy.GetComponentsInChildren<Collider>(true);
        ResolveReferences();
    }

    public void Configure(bool isVisible, Vector3 worldOffset)
    {
        _worldOffset = worldOffset;
        ResolveReferences();
        SetVisible(isVisible);
        UpdatePlacement();
    }

    public void Refresh(int currentHealth, int maxHealth)
    {
        float healthRatio = maxHealth > 0
            ? Mathf.Clamp01(currentHealth / (float)maxHealth)
            : 0f;

        if (fillImage != null)
            fillImage.fillAmount = healthRatio;

        if (fillSlider != null)
            fillSlider.normalizedValue = healthRatio;

        if (fillScrollbar != null)
        {
            fillScrollbar.size = healthRatio;
            fillScrollbar.value = 0f;
        }

        if (healthText != null)
            healthText.SetText(healthTextFormat, Mathf.Max(0, currentHealth), Mathf.Max(0, maxHealth));
    }

    public void SetVisible(bool isVisible)
    {
        if (_visualRoot == null)
            return;

        if (_visualRoot.gameObject.activeSelf != isVisible)
            _visualRoot.gameObject.SetActive(isVisible);
    }

    private void LateUpdate()
    {
        if (_visualRoot == null || !_visualRoot.gameObject.activeSelf)
            return;

        UpdatePlacement();

        if (faceMainCamera)
            FaceCamera();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private void UpdatePlacement()
    {
        if (_visualRoot == null)
            return;

        Vector3 anchorPosition = (_targetRoot != null ? _targetRoot.position : transform.position) + _worldOffset;

        if (TryGetWorldBounds(out Bounds bounds))
            anchorPosition = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) + _worldOffset;

        _visualRoot.position = anchorPosition;
    }

    private void FaceCamera()
    {
        if (!TryResolveCameraTransform(out Transform resolvedCameraTransform))
            return;

        _visualRoot.rotation = resolvedCameraTransform.rotation;
    }

    private bool TryResolveCameraTransform(out Transform resolvedCameraTransform)
    {
        if (_cameraTransform != null && _cameraTransform.gameObject.activeInHierarchy)
        {
            resolvedCameraTransform = _cameraTransform;
            return true;
        }

        Camera mainCamera = Camera.main;
        _cameraTransform = mainCamera != null ? mainCamera.transform : null;
        resolvedCameraTransform = _cameraTransform;
        return resolvedCameraTransform != null;
    }

    private bool TryGetWorldBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        if (_cachedRenderers != null)
        {
            for (int i = 0; i < _cachedRenderers.Length; i++)
            {
                Renderer cachedRenderer = _cachedRenderers[i];
                if (cachedRenderer == null || !cachedRenderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = cachedRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(cachedRenderer.bounds);
                }
            }
        }

        if (hasBounds)
            return true;

        if (_cachedColliders == null)
            return false;

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            Collider cachedCollider = _cachedColliders[i];
            if (cachedCollider == null || !cachedCollider.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = cachedCollider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(cachedCollider.bounds);
            }
        }

        return hasBounds;
    }

    private void ResolveReferences()
    {
        if (visualRoot == null && TryGetComponent(out RectTransform _))
            visualRoot = gameObject;

        _visualRoot = visualRoot != null ? visualRoot.transform : null;
    }
}
