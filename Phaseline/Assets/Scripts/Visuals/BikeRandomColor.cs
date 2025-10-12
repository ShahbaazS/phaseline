using UnityEngine;
using System.Linq;

/// Attach this to the root of PlayerBike and BotBike.
/// On Awake, it picks a color (blue or red by default) and tints ALL child renderers,
/// including your trail renderer, so body and trail match.
public class BikeRandomColor : MonoBehaviour
{
    [Header("Palette")]
    public Color[] palette = new Color[]
    {
        new Color(0.23f, 0.76f, 1f),   // Blue
        new Color(1f, 0.28f, 0.28f),   // Red
        new Color(0.56f, 0.94f, 0.33f), // Green
        new Color(1f, 0.84f, 0.22f),   // Yellow
    };

    [Header("Emission")]
    public bool setEmission = true;
    [Range(0f, 3f)] public float emissionIntensity = 1.5f;

    [Header("Renderer Search")]
    public bool includeInactiveChildren = true;
    [Tooltip("Any child tagged with this will be skipped (e.g., decals).")]
    public string skipTag = "NoTint";

    // Common color property names across shaders
    static readonly int _BaseColorID   = Shader.PropertyToID("_BaseColor");
    static readonly int _ColorID       = Shader.PropertyToID("_Color");
    static readonly int _EmissionColor = Shader.PropertyToID("_EmissionColor");

    MaterialPropertyBlock mpb;

    Color chosenBaseColor;
    Color chosenEmissionColor;

    void Awake()
    {
        if (palette == null || palette.Length == 0)
            palette = new[] { Color.blue, Color.red };

        // pick one color for BOTH bike + trail
        chosenBaseColor = palette[Random.Range(0, palette.Length)];
        chosenEmissionColor = setEmission ? chosenBaseColor * Mathf.LinearToGammaSpace(emissionIntensity) : Color.black;

        ApplyToAllRenderers(chosenBaseColor, chosenEmissionColor);
        ApplyToAllLineRenderers(chosenBaseColor, chosenEmissionColor);
    }

    void ApplyToAllRenderers(Color baseColor, Color emissionColor)
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();

        var renderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);
        foreach (var r in renderers)
        {
            if (!r || (skipTag.Length > 0 && r.CompareTag(skipTag))) continue;

            r.GetPropertyBlock(mpb);

            var mat = r.sharedMaterial;
            if (mat)
            {
                if (mat.HasProperty(_BaseColorID)) mpb.SetColor(_BaseColorID, baseColor);
                if (mat.HasProperty(_ColorID)) mpb.SetColor(_ColorID, baseColor);

                if (setEmission && mat.HasProperty(_EmissionColor))
                {
                    mpb.SetColor(_EmissionColor, emissionColor);
                    // Ensure emission is enabled on the material keyword if needed
                    if (!mat.IsKeywordEnabled("_EMISSION")) mat.EnableKeyword("_EMISSION");
                }
            }

            r.SetPropertyBlock(mpb);
        }
    }

    public void ApplyToRenderer(Renderer r)
    {
        if (!r) return;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);

        var mat = r.sharedMaterial;
        if (mat)
        {
            if (mat.HasProperty(_BaseColorID)) mpb.SetColor(_BaseColorID, chosenBaseColor);
            if (mat.HasProperty(_ColorID))     mpb.SetColor(_ColorID, chosenBaseColor);
            if (setEmission && mat.HasProperty(_EmissionColor))
            {
                mpb.SetColor(_EmissionColor, chosenEmissionColor);
                if (!mat.IsKeywordEnabled("_EMISSION")) mat.EnableKeyword("_EMISSION");
            }
        }
        r.SetPropertyBlock(mpb);
    }

    void ApplyToAllLineRenderers(Color baseColor, Color emissionColor)
    {
        // LineRenderer doesn't use MPB for color; set start/end directly
        var lines = GetComponentsInChildren<LineRenderer>(includeInactiveChildren);
        foreach (var lr in lines)
        {
            if (!lr || (skipTag.Length > 0 && lr.CompareTag(skipTag))) continue;
            lr.startColor = lr.endColor = baseColor;
            // If your line shader supports emission via material, MPB above will handle it.
        }
    }
}
