using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class LobbyCrowdPawnBounce : MonoBehaviour
{
    [SerializeField] private string pawnNamePrefix = "Lobby Crowd Pawn Ring";
    [SerializeField, Min(0.001f)] private float minimumHopHeight = 0.025f;
    [SerializeField, Min(0.001f)] private float maximumHopHeight = 0.065f;
    [SerializeField, Min(0.05f)] private float minimumHopDuration = 0.34f;
    [SerializeField, Min(0.05f)] private float maximumHopDuration = 0.58f;
    [SerializeField, Min(0f)] private float minimumRestDuration = 0.25f;
    [SerializeField, Min(0f)] private float maximumRestDuration = 1.9f;

    private readonly List<PawnMotion> pawnMotions = new();
    private readonly List<Material> sceneMaterialInstances = new();

    private sealed class PawnMotion
    {
        public Transform PawnTransform;
        public Vector3 RestPosition;
        public uint RandomState;
        public float NextHopTime;
        public float HopStartedAt;
        public float HopDuration;
        public float HopHeight;
        public bool IsHopping;
    }

    private void Awake()
    {
        CollectScenePawns();
    }

    private void Update()
    {
        float currentTime = Time.unscaledTime;

        foreach (PawnMotion motion in pawnMotions)
        {
            if (motion.PawnTransform == null)
            {
                continue;
            }

            if (!motion.IsHopping)
            {
                if (currentTime < motion.NextHopTime)
                {
                    continue;
                }

                motion.IsHopping = true;
                motion.HopStartedAt = currentTime;
                motion.HopDuration = RandomRange(
                    ref motion.RandomState,
                    minimumHopDuration,
                    maximumHopDuration);
                motion.HopHeight = RandomRange(
                    ref motion.RandomState,
                    minimumHopHeight,
                    maximumHopHeight);
            }

            float progress = Mathf.Clamp01(
                (currentTime - motion.HopStartedAt) / motion.HopDuration);
            float hop = Mathf.Sin(progress * Mathf.PI) * motion.HopHeight;
            motion.PawnTransform.position = motion.RestPosition + Vector3.up * hop;

            if (progress >= 1f)
            {
                motion.PawnTransform.position = motion.RestPosition;
                motion.IsHopping = false;
                motion.NextHopTime = currentTime + RandomRange(
                    ref motion.RandomState,
                    minimumRestDuration,
                    maximumRestDuration);
            }
        }
    }

    private void CollectScenePawns()
    {
        pawnMotions.Clear();
        float currentTime = Time.unscaledTime;

        foreach (GameObject rootObject in gameObject.scene.GetRootGameObjects())
        {
            foreach (Transform candidate in rootObject.GetComponentsInChildren<Transform>(true))
            {
                if (!candidate.name.StartsWith(
                        pawnNamePrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Renderer pawnRenderer in
                         candidate.GetComponentsInChildren<Renderer>(true))
                {
                    pawnRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    ReplaceWithOutlineFreeMaterials(pawnRenderer);
                }

                uint randomState = StableNameHash(candidate.name);
                float initialDelay = RandomRange(
                    ref randomState,
                    0.1f,
                    maximumRestDuration);
                pawnMotions.Add(new PawnMotion
                {
                    PawnTransform = candidate,
                    RestPosition = candidate.position,
                    RandomState = randomState,
                    NextHopTime = currentTime + initialDelay,
                });
            }
        }

        pawnMotions.Sort((left, right) => string.CompareOrdinal(
            left.PawnTransform.name,
            right.PawnTransform.name));
    }

    private void OnDisable()
    {
        foreach (PawnMotion motion in pawnMotions)
        {
            if (motion.PawnTransform != null)
            {
                motion.PawnTransform.position = motion.RestPosition;
            }
        }
    }

    private void OnDestroy()
    {
        foreach (Material material in sceneMaterialInstances)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }

    private void ReplaceWithOutlineFreeMaterials(Renderer targetRenderer)
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

    private static Material CreateOutlineFreeMaterial(Material sourceMaterial)
    {
        Material material = new(sourceMaterial);
        material.name = sourceMaterial.name + " (Lobby No Outline)";

        Color pieceColor = sourceMaterial.HasProperty("_BaseColor")
            ? sourceMaterial.GetColor("_BaseColor")
            : sourceMaterial.color;

        if (material.HasProperty("_SPRDefaultUnlitColorMask"))
        {
            material.SetFloat("_SPRDefaultUnlitColorMask", 0f);
        }

        material.SetShaderPassEnabled("Outline", false);
        material.SetShaderPassEnabled("SRPDefaultUnlit", false);
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

    private void OnValidate()
    {
        maximumHopHeight = Mathf.Max(minimumHopHeight, maximumHopHeight);
        maximumHopDuration = Mathf.Max(minimumHopDuration, maximumHopDuration);
        maximumRestDuration = Mathf.Max(minimumRestDuration, maximumRestDuration);
    }

    private static uint StableNameHash(string value)
    {
        uint hash = 2166136261u;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash == 0u ? 0x9E3779B9u : hash;
    }

    private static float RandomRange(ref uint state, float minimum, float maximum)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        float unitValue = (state & 0x00FFFFFFu) / 16777215f;
        return Mathf.Lerp(minimum, maximum, unitValue);
    }
}
