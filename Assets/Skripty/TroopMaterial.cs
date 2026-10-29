using Unity.Netcode;
using UnityEngine;

public class TroopMaterial : NetworkBehaviour
{
    // Zelená = AI, modrá = lokální hráč, červená = protihráč,
    // oranžová = další hráč.
    [SerializeField] private GameObject model;
    [SerializeField] private Material red;
    [SerializeField] private Material green;
    [SerializeField] private Material orange;
    [SerializeField] private Material blue;

    private Renderer[] modelRenderers;

    public override void OnNetworkSpawn()
    {
        ApplyMaterial(IsOwner, (int)OwnerClientId);
    }

    /// <summary>
    /// Nastaví barvu jednotky podle jejího vlastníka. Metodu lze použít i
    /// pro lokální, dosud nesíťové preview jednotky.
    /// </summary>
    public void ApplyMaterial(bool isOwner, int id)
    {
        if (model == null)
        {
            Debug.LogError("TroopMaterial: Není přiřazen model!", this);
            return;
        }

        if (modelRenderers == null || modelRenderers.Length == 0)
        {
            modelRenderers = model.GetComponentsInChildren<Renderer>(true);

            if (modelRenderers.Length == 0)
            {
                Renderer mainRenderer = model.GetComponent<Renderer>();
                if (mainRenderer != null)
                    modelRenderers = new[] { mainRenderer };
            }
        }

        if (modelRenderers.Length == 0)
        {
            Debug.LogError("TroopMaterial: Model nemá žádný Renderer!", model);
            return;
        }

        Material selectedMaterial = null;

        if (isOwner)
        {
            selectedMaterial = blue;
        }
        else
        {
            switch (id)
            {
                case 0:
                    selectedMaterial = green;
                    break;
                case 1:
                    selectedMaterial = red;
                    break;
                case 2:
                    selectedMaterial = orange;
                    break;
            }
        }

        if (selectedMaterial == null)
        {
            Debug.LogWarning(
                $"TroopMaterial: Pro hráče s ID {id} není přiřazen materiál.",
                this);
            return;
        }

        foreach (Renderer modelRenderer in modelRenderers)
        {
            if (modelRenderer == null)
                continue;

            Material[] materials = modelRenderer.materials;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = selectedMaterial;

            modelRenderer.materials = materials;
        }
    }
}
