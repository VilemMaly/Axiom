
using UnityEngine;

public class ConvertSkinnedMesh : MonoBehaviour
{
    [ContextMenu("Convert To Mesh Renderer")]
    void Convert()
    {
        SkinnedMeshRenderer skinned =
            GetComponent<SkinnedMeshRenderer>();

        if (skinned == null)
            return;

        Mesh bakedMesh = new Mesh();
        skinned.BakeMesh(bakedMesh);

        MeshFilter meshFilter =
            gameObject.AddComponent<MeshFilter>();

        meshFilter.sharedMesh = bakedMesh;

        MeshRenderer meshRenderer =
            gameObject.AddComponent<MeshRenderer>();

        meshRenderer.sharedMaterials = skinned.sharedMaterials;

        DestroyImmediate(skinned);
    }
}
