using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class LobbyCreateServerDepthOfField : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private GameObject menuRoot;
    [SerializeField, Min(0f)] private float startDelay = 0.35f;
    [SerializeField, Min(0.05f)] private float fadeDuration = 1f;
    [SerializeField, Min(0f)] private float blurStartDistance = 1.2f;
    [SerializeField, Min(0f)] private float blurEndDistance = 2.4f;
    [SerializeField, Range(0.5f, 1.5f)] private float maximumBlurRadius = 0.5f;

    private Volume depthOfFieldVolume;
    private VolumeProfile runtimeProfile;
    private bool menuWasVisible;
    private bool blurIsActive;
    private float activationTime;

    private void Awake()
    {
        menuWasVisible = menuRoot != null && menuRoot.activeInHierarchy;
        CreateDepthOfFieldVolume();
    }

    private void Update()
    {
        if (!blurIsActive && menuRoot != null)
        {
            bool menuIsVisible = menuRoot.activeInHierarchy;
            if (menuWasVisible && !menuIsVisible)
            {
                ActivateBackgroundBlur();
            }

            menuWasVisible = menuIsVisible;
        }

        if (!blurIsActive || depthOfFieldVolume == null)
        {
            return;
        }

        float elapsed = Time.unscaledTime - activationTime - startDelay;
        float progress = Mathf.Clamp01(elapsed / fadeDuration);
        depthOfFieldVolume.weight = Mathf.SmoothStep(0f, 1f, progress);
    }

    public void ActivateBackgroundBlur()
    {
        if (blurIsActive)
        {
            return;
        }

        blurIsActive = true;
        activationTime = Time.unscaledTime;

        if (targetCamera != null)
        {
            UniversalAdditionalCameraData cameraData =
                targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = true;
            }
        }
    }

    public void DeactivateBackgroundBlur()
    {
        blurIsActive = false;
        menuWasVisible = menuRoot != null && menuRoot.activeInHierarchy;

        if (depthOfFieldVolume != null)
        {
            depthOfFieldVolume.weight = 0f;
        }
    }

    private void CreateDepthOfFieldVolume()
    {
        depthOfFieldVolume = gameObject.AddComponent<Volume>();
        depthOfFieldVolume.isGlobal = true;
        depthOfFieldVolume.priority = 100f;
        depthOfFieldVolume.weight = 0f;

        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.hideFlags = HideFlags.DontSave;

        DepthOfField depthOfField = runtimeProfile.Add<DepthOfField>(true);
        depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
        depthOfField.gaussianStart.Override(blurStartDistance);
        depthOfField.gaussianEnd.Override(blurEndDistance);
        depthOfField.gaussianMaxRadius.Override(maximumBlurRadius);
        depthOfField.highQualitySampling.Override(true);

        depthOfFieldVolume.profile = runtimeProfile;
    }

    private void OnDestroy()
    {
        if (runtimeProfile != null)
        {
            Destroy(runtimeProfile);
        }
    }

    private void OnValidate()
    {
        fadeDuration = Mathf.Max(0.05f, fadeDuration);
        blurStartDistance = Mathf.Max(0f, blurStartDistance);
        blurEndDistance = Mathf.Max(blurStartDistance + 0.01f, blurEndDistance);
    }
}
