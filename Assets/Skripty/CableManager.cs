using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CableManager : NetworkBehaviour
{
    [Header("Cable Settings")]
    [SerializeField] private Material cableMaterial;
    [SerializeField] private Material energyShortageCableMaterial;
    [SerializeField] private float cableWidth = 0.05f;
    private List<GameObject> cables = new List<GameObject>();

    [ClientRpc]
    public void CreateCableClientRpc(Vector3 position1, Vector3 position2, bool destinationLacksEnergy)
    {
        Debug.Log($"[CableManager] Creating cable from {position1} to {position2}");
        GameObject cableObject = new GameObject("Cable");

        cableObject.transform.SetParent(transform);

        LineRenderer line = cableObject.AddComponent<LineRenderer>();

        line.positionCount = 2;
        line.SetPosition(0, position1);
        line.SetPosition(1, position2);

        line.startWidth = cableWidth;
        line.endWidth = cableWidth;

        Material selectedCableMaterial = destinationLacksEnergy && energyShortageCableMaterial != null
            ? energyShortageCableMaterial
            : cableMaterial;
        line.material = selectedCableMaterial;

        line.useWorldSpace = true;

        // Černá čára
        if (selectedCableMaterial == null)
        {
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = Color.black;
            line.endColor = Color.black;
        }

        cables.Add(cableObject);
    }

    [ClientRpc]
    public void ClearAllCablesClientRpc()
    {
        foreach (GameObject cable in cables)
        {
            if (cable != null)
                Destroy(cable);
        }

        cables.Clear();
    }
}
