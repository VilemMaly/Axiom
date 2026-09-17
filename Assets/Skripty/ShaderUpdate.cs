using UnityEngine;

public class ShaderUpdate : MonoBehaviour
{
    public RenderTexture currentVisibility;   // z tvé vision kamery
    public RenderTexture exploredA;
    public RenderTexture exploredB;
    public Material accumulateMaterial;       // materiál z FogAccumulateShader
    public Material fogDisplayMaterial;       // tvůj hlavní fog shader na rovině

    private bool flip;

    void UpdateExplored()
    {
        RenderTexture prev = flip ? exploredB : exploredA;
        RenderTexture next = flip ? exploredA : exploredB;

        accumulateMaterial.SetTexture("_PrevExplored", prev);
        Graphics.Blit(currentVisibility, next, accumulateMaterial);

        fogDisplayMaterial.SetTexture("_VisibleMap", next);

        flip = !flip;
    }

    private int frameCount = 0;
    // Update is called once per frame
    void Update()
    {
        frameCount++;
        if (frameCount >= 60)
        {
            UpdateExplored();
            frameCount = 0;
        }
        
    }
}
