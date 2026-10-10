using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Matchmaker.Models;
using Unity.VisualScripting;
using UnityEngine;

public class ResearchUiController : MonoBehaviour
{
    [SerializeField] private Transform uiPanel;
    [SerializeField] private Transform ResearchParent;
    [SerializeField] private GameObject doctrinePrefab;
    [SerializeField] private GameObject researchPrefab;
    [SerializeField] private Vector2 gridSpacing = new Vector2(200, 200);
    [SerializeField] private GameObject chooseParent;
    [SerializeField] private GameObject researchParent;
    private Vector3 position;
    private bool isVisible = false;
    private PlayerTechnology playerTechnology;
    private readonly List<ResearchPopup> researchPopups = new List<ResearchPopup>();
    
    private void Start()
    {
        playerTechnology = GetComponent<PlayerTechnology>();
        SpawnDoctrinePrefabs();
    }

    public void SpawnDoctrinePrefabs()
    {
        int cycle = 0;

foreach (DoctrineDefinition doctrine in playerTechnology.Doctrines)
{
    if (doctrine.DoctrineType == DoctrineType.None)
        continue;

    GameObject spawnedDoctrine = Instantiate(doctrinePrefab, uiPanel);

    RectTransform rect = spawnedDoctrine.GetComponent<RectTransform>();

    int column = cycle % 4;
    int row = cycle / 4;

    rect.anchoredPosition = new Vector2(
        column * gridSpacing.x,
        -row * gridSpacing.y
    );

    DoctrinePopup doctrinePopup = spawnedDoctrine.GetComponent<DoctrinePopup>();
    doctrinePopup.Fill(doctrine);

    cycle++;

    doctrinePopup.OnDecisionMade += (selectedDoctrine, accepted) =>
    {
        if (accepted)
        {
            playerTechnology.SetSelectedDoctrineServerRpc(selectedDoctrine.DoctrineType);
        }
    };
}
    }
    [ClientRpc]
    public void SpawnResearchPrefabsClientRpc(DoctrineType Doctrinetype)
    {
        DoctrineDefinition selectedDoctrine = null;
        foreach (DoctrineDefinition doctrine in playerTechnology.Doctrines)
        {
            if(doctrine.DoctrineType == Doctrinetype)
            {
                selectedDoctrine = doctrine;
            }
        }
        if(selectedDoctrine == null)
            return;
        chooseParent.SetActive(false);
        researchParent.SetActive(true);

        int cycle = 0;
        foreach (ResearchDefinition research in selectedDoctrine.Researches)
        {
            if (research == null)
                continue;

            GameObject spawnedResearch = Instantiate(researchPrefab, ResearchParent);

            RectTransform rect = spawnedResearch.GetComponent<RectTransform>();

            int column = cycle % 4;
            int row = cycle / 4;

            rect.anchoredPosition = new Vector2(
                column * gridSpacing.x,
                -row * gridSpacing.y
            );

            ResearchPopup researchPopup = spawnedResearch.GetComponent<ResearchPopup>();
            researchPopup.Fill(research);
            researchPopup.OnResearchClicked += HidePopups;
            researchPopup.OnPopupClosed += ShowPopups;
            researchPopup.OnAcceptClicked = researchId =>
            playerTechnology.SelectResearchServerRpc(researchId);
            researchPopups.Add(researchPopup);

            cycle++;
        }
    }

    private void HidePopups(ResearchPopup notThisOne, ResearchDefinition ignoredResearch)
    {
        if (notThisOne == null)
            return;

        for (int i = researchPopups.Count - 1; i >= 0; i--)
        {
            ResearchPopup popup = researchPopups[i];
            if (popup == null || popup == notThisOne)
                continue;

            popup.GameObject().SetActive(false);
        }
    }
    private void ShowPopups()
    {
        for (int i = researchPopups.Count - 1; i >= 0; i--)
        {
            ResearchPopup popup = researchPopups[i];

            popup.GameObject().SetActive(true);
        }
    }
}