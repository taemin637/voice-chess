using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LobbyCreateServerKingDrop : MonoBehaviour
{
    [SerializeField] private GameObject whiteKingPrefab;
    [SerializeField] private GameObject blackKingPrefab;
    [SerializeField] private GameObject menuRoot;
    [SerializeField] private Vector3 whiteLandingPosition = new(0.12f, 0.641f, 0.338f);
    [SerializeField] private Vector3 blackLandingPosition = new(0.12f, 0.641f, -0.36f);
    [SerializeField] private float visualBaseYOffset;
    [SerializeField, Min(1f)] private float spawnHeight = 5.5f;
    [SerializeField, Min(0f)] private float whiteDelay = 0.2f;
    [SerializeField, Min(0f)] private float blackDelay = 0.48f;
    [SerializeField, Min(0.1f)] private float fallDuration = 0.95f;
    [SerializeField, Min(0f)] private float bounceHeight = 0.16f;
    [SerializeField, Min(0.1f)] private float scaleMultiplier = 1.1f;
    [SerializeField, Range(0f, 1f)] private float impactVolume = 0.78f;

    private AudioClip impactClip;
    private readonly List<Material> sceneMaterialInstances = new();
    private readonly List<GameObject> spawnedKings = new();
    private bool hasDropped;
    private bool whiteDropStarted;
    private bool blackDropStarted;
    private int expectedKingCount;
    private int settledKingCount;

    public bool KingsHaveSettled =>
        hasDropped && expectedKingCount > 0 && settledKingCount >= expectedKingCount;

    private void Awake()
    {
    }

    public void DropKings()
    {
        DropWhiteKingOnly();
        DropBlackKing();
    }

    public void DropWhiteKingOnly()
    {
        PrepareDropSequence();

        if (!whiteDropStarted && whiteKingPrefab != null)
        {
            whiteDropStarted = true;
            expectedKingCount++;
            StartCoroutine(DropKing(
                whiteKingPrefab,
                whiteLandingPosition,
                whiteDelay,
                "Lobby White King Drop",
                1.04f));
        }
    }

    public void DropBlackKing()
    {
        PrepareDropSequence();

        if (!blackDropStarted && blackKingPrefab != null)
        {
            blackDropStarted = true;
            expectedKingCount++;
            StartCoroutine(DropKing(
                blackKingPrefab,
                blackLandingPosition,
                blackDelay,
                "Lobby Black King Drop",
                0.92f));
        }
    }

    private void PrepareDropSequence()
    {
        if (hasDropped)
        {
            return;
        }

        hasDropped = true;
        expectedKingCount = 0;
        settledKingCount = 0;
    }

    private IEnumerator DropKing(
        GameObject prefab,
        Vector3 landingPosition,
        float delay,
        string instanceName,
        float impactPitch)
    {
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        Vector3 startPosition = landingPosition + Vector3.up * spawnHeight;
        GameObject instance = new(instanceName);
        instance.transform.SetPositionAndRotation(startPosition, Quaternion.identity);
        instance.name = instanceName;
        spawnedKings.Add(instance);

        GameObject visual = Instantiate(prefab, instance.transform, false);
        visual.name = prefab.name + " Visual (Lobby Copy)";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = prefab.transform.localRotation;
        visual.transform.localScale = prefab.transform.localScale;

        Transform kingTransform = instance.transform;
        Vector3 settledScale = kingTransform.localScale * scaleMultiplier;
        kingTransform.localScale = settledScale;
        AlignVisualToLandingPivot(
            visual,
            kingTransform,
            visualBaseYOffset);
        ReplaceWithOutlineFreeMaterials(instance);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
        }

        float elapsed = 0f;
        while (elapsed < fallDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / fallDuration);
            float gravityProgress = progress * progress;
            kingTransform.position = Vector3.LerpUnclamped(
                startPosition,
                landingPosition,
                gravityProgress);
            yield return null;
        }

        kingTransform.position = landingPosition;
        PlayImpact(instance, impactPitch);

        const float settleDuration = 0.28f;
        elapsed = 0f;

        while (elapsed < settleDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / settleDuration);
            float bounce = Mathf.Sin(progress * Mathf.PI) *
                bounceHeight * (1f - progress);
            float squash = Mathf.Sin(progress * Mathf.PI) * (1f - progress);

            kingTransform.position = landingPosition + Vector3.up * bounce;
            kingTransform.localScale = new Vector3(
                settledScale.x * (1f + squash * 0.055f),
                settledScale.y * (1f - squash * 0.1f),
                settledScale.z * (1f + squash * 0.055f));
            yield return null;
        }

        kingTransform.position = landingPosition;
        kingTransform.localScale = settledScale;
        settledKingCount++;
    }

    private static void AlignVisualToLandingPivot(
        GameObject visual,
        Transform landingRoot,
        float baseYOffset)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds visualBounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            visualBounds.Encapsulate(renderers[index].bounds);
        }

        Vector3 desiredVisualBase = landingRoot.position +
            Vector3.up * baseYOffset;
        Vector3 currentVisualBase = new(
            visualBounds.center.x,
            visualBounds.min.y,
            visualBounds.center.z);
        visual.transform.position += desiredVisualBase - currentVisualBase;
    }

    public void ResetKingsForMainMenu()
    {
        StopAllCoroutines();

        foreach (GameObject king in spawnedKings)
        {
            if (king != null)
            {
                Destroy(king);
            }
        }

        spawnedKings.Clear();
        expectedKingCount = 0;
        settledKingCount = 0;
        hasDropped = false;
        whiteDropStarted = false;
        blackDropStarted = false;
    }

    private void PlayImpact(GameObject king, float pitch)
    {
        if (impactClip == null)
        {
            impactClip = CreateImpactClip();
        }

        AudioSource source = king.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0.45f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = 18f;
        source.volume = impactVolume;
        source.pitch = pitch;
        source.PlayOneShot(impactClip);
    }

    private static AudioClip CreateImpactClip()
    {
        const int sampleRate = 44100;
        const float clipDuration = 0.24f;
        int sampleCount = Mathf.CeilToInt(sampleRate * clipDuration);
        float[] samples = new float[sampleCount];
        uint noiseState = 0x71A53C9Du;

        for (int index = 0; index < sampleCount; index++)
        {
            float time = index / (float)sampleRate;
            float bodyEnvelope = Mathf.Exp(-17f * time);
            float transientEnvelope = Mathf.Exp(-58f * time);
            float body = Mathf.Sin(Mathf.PI * 2f * 67f * time) * 0.58f +
                Mathf.Sin(Mathf.PI * 2f * 43f * time) * 0.28f;

            noiseState = noiseState * 1664525u + 1013904223u;
            float noise = ((noiseState >> 8) / 8388607.5f) - 1f;
            samples[index] = Mathf.Clamp(
                body * bodyEnvelope + noise * transientEnvelope * 0.32f,
                -1f,
                1f);
        }

        AudioClip clip = AudioClip.Create(
            "Lobby King Landing Thud",
            sampleCount,
            1,
            sampleRate,
            false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void ReplaceWithOutlineFreeMaterials(GameObject instance)
    {
        foreach (Renderer targetRenderer in
                 instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] sourceMaterials = targetRenderer.sharedMaterials;
            Material[] sceneMaterials = new Material[sourceMaterials.Length];

            for (int index = 0; index < sourceMaterials.Length; index++)
            {
                Material sourceMaterial = sourceMaterials[index];
                if (sourceMaterial == null ||
                    !sourceMaterial.HasProperty("_Outline_Width"))
                {
                    sceneMaterials[index] = sourceMaterial;
                    continue;
                }

                Material sceneMaterial = CreateOutlineFreeMaterial(sourceMaterial);
                sceneMaterialInstances.Add(sceneMaterial);
                sceneMaterials[index] = sceneMaterial;
            }

            targetRenderer.sharedMaterials = sceneMaterials;
        }
    }

    private static Material CreateOutlineFreeMaterial(Material sourceMaterial)
    {
        Material material = new(sourceMaterial);
        material.name = sourceMaterial.name + " (Lobby No Outline)";
        bool isKingTestMaterial = sourceMaterial.name.Contains("KingTest");

        Color pieceColor = sourceMaterial.HasProperty("_BaseColor")
            ? sourceMaterial.GetColor("_BaseColor")
            : sourceMaterial.color;

        if (!isKingTestMaterial)
        {
            if (material.HasProperty("_SPRDefaultUnlitColorMask"))
            {
                material.SetFloat("_SPRDefaultUnlitColorMask", 0f);
            }

            material.SetShaderPassEnabled("Outline", false);
            material.SetShaderPassEnabled("SRPDefaultUnlit", false);
        }

        if (material.HasProperty("_Outline_Width"))
        {
            material.SetFloat("_Outline_Width", 0f);
        }

        if (material.HasProperty("_OutlineVisible"))
        {
            material.SetFloat("_OutlineVisible", 0f);
        }

        if (material.HasProperty("_Outline_Color"))
        {
            material.SetColor("_Outline_Color", pieceColor);
        }

        material.DisableKeyword("_OUTLINE_NML");
        material.DisableKeyword("_OUTLINE_POS");
        return material;
    }

    private void OnDestroy()
    {
        if (impactClip != null)
        {
            Destroy(impactClip);
        }

        foreach (Material material in sceneMaterialInstances)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
