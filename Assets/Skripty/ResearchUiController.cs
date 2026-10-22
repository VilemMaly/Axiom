using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Matchmaker.Models;
using Unity.VisualScripting;
using UnityEngine;

public class ResearchUiController : NetworkBehaviour
{
    [SerializeField] private Transform uiPanel;
    [SerializeField] private Transform ResearchParent;
    [SerializeField] private GameObject doctrinePrefab;
    [SerializeField] private GameObject researchPrefab;
    [SerializeField] private Vector2 gridSpacing = new Vector2(200, 200);
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

    int column = cycle / 4;
    int row = cycle % 4;

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

    private void hideIcons(bool wasOpened, DoctrinePopup notThisOne)
    {
        if(!wasOpened)
        {
            showIcons();
            return;
        }
        foreach (Transform child in uiPanel)
        {
            DoctrinePopup popupIcon = child.GetComponent<DoctrinePopup>();

            if (popupIcon != null && popupIcon != notThisOne)
            {
                child.GameObject().SetActive(false);
            }
        }
    }
    private void showIcons()
    {
        foreach (Transform child in uiPanel)
        {
            child.GameObject().SetActive(true);
        }
    }

    [ClientRpc]
    public void SpawnResearchPrefabsClientRpc(DoctrineType Doctrinetype)
    {
        DoctrineDefinition selectedDoctrine = null;
        foreach (DoctrineDefinition doctrine in playerTechnology.Doctrines)
        {
            if (doctrine.DoctrineType == Doctrinetype)
            {
                selectedDoctrine = doctrine;
            }
        }
        if (selectedDoctrine == null)
            return;
        researchParent.SetActive(true);
        Debug.Log("Client rpc spawn research");
        int cycle = 0;
        // Počet výzkumů v každém řádku
        // Musí být mimo foreach, aby se hodnoty neresetovaly.
        int[] radky = new int[10];

        foreach (ResearchDefinition research in selectedDoctrine.Researches)
        {
            if (research == null)
                continue;

            // Určí řádek podle RequiredResearches.
            int row = GetResearchRow(research);

            // Pole radky podporuje maximálně 10 řádků.
            if (row >= radky.Length)
            {
                Debug.LogWarning($"Výzkum {research.name} je mimo povolený počet řádků.");
                continue;
            }

            // Sloupec odpovídá počtu výzkumů v tomto řádku.
            int column = radky[row];
            radky[row]++;

            GameObject spawnedResearch = Instantiate(researchPrefab, ResearchParent);

            RectTransform rect = spawnedResearch.GetComponent<RectTransform>();

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

        private int GetResearchRow(ResearchDefinition research)
    {
        // Výzkum bez požadavků patří do řádku 0.
        if (research.RequiredResearches == null ||
            research.RequiredResearches.Count == 0)
        {
            return 0;
        }

        int row = 0;

        // Najde nejnižší řádek ze všech požadovaných výzkumů
        // a umístí tento výzkum o řádek níže.
        foreach (ResearchDefinition required in research.RequiredResearches)
        {
            if (required == null)
                continue;

            int requiredRow = GetResearchRow(required);

            row = Mathf.Max(row, requiredRow + 1);
        }

        return row;
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