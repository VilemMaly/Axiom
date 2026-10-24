using Unity.Netcode;
using UnityEngine;

public class BuildingMaterial : NetworkBehaviour
{
    // zelená = AI, modrá = lokální hráč,
    // červená = protihráč, žlutá = další hráč

    [SerializeField] GameObject model;
    [SerializeField] Material red;
    [SerializeField] Material green;
    [SerializeField] Material orange;
    [SerializeField] Material blue;

    private Renderer[] modelRenderers;

    public override void OnNetworkSpawn()
    {
        ApplyMaterial(IsOwner, (int)OwnerClientId);
    }

    void Start()
    {
        if (model != null)
        {
            modelRenderers = model.GetComponentsInChildren<Renderer>(true);

            if (modelRenderers.Length == 0)
            {
                Renderer mainRenderer = model.GetComponent<Renderer>();

                if (mainRenderer != null)
                {
                    modelRenderers = new Renderer[] { mainRenderer };
                }
            }
        }
    }

    // Ghost není síťový objekt, proto ho inicializuje placement controller přímo.
    public void ApplyMaterial(bool isOwner, int id)
    {
        if (model == null)
        {
            Debug.LogError(
                "BuildingModelSpawn: Není přiřazen model!",
                this
            );
            return;
        }

        if (modelRenderers == null || modelRenderers.Length == 0)
        {
            modelRenderers = model.GetComponentsInChildren<Renderer>(true);

            if (modelRenderers.Length == 0)
            {
                Renderer mainRenderer = model.GetComponent<Renderer>();

                if (mainRenderer != null)
                {
                    modelRenderers = new Renderer[] { mainRenderer };
                }
            }
        }

        if (modelRenderers.Length == 0)
        {
            Debug.LogError(
                "BuildingModelSpawn: Model nemá žádný Renderer!",
                model
            );
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
                case 1:
                    selectedMaterial = red;
                    break;

                case 0:
                    selectedMaterial = green;
                    break;

                case 2:
                    selectedMaterial = orange;
                    break;
            }
        }

        if (selectedMaterial == null)
        {
            Debug.LogWarning(
                $"BuildingModelSpawn: Pro hráče s ID {id} není přiřazen materiál.",
                this
            );
            return;
        }

        foreach (Renderer modelRenderer in modelRenderers)
        {
            if (modelRenderer != null)
            {
                Material[] materials = modelRenderer.materials;

                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = selectedMaterial;
                }

                modelRenderer.materials = materials;
            }
        }
    }

    void Update()
    {

    }
}
