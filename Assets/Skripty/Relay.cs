using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Relay : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_Text joinCodeText;

    [Header("Relay")]
    [SerializeField] private int maxConnections = 1;

    private bool servicesInitialized = false;

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private async Task InitializeUnityServices()
    {
        if (!servicesInitialized)
        {
            await UnityServices.InitializeAsync();

            servicesInitialized = true;
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    public async void Host()
    {
        try
        {
            Debug.Log("Initializing Unity Services...");

            await InitializeUnityServices();

            Debug.Log("Creating Relay allocation...");

            // 1 klient + host = 2 hráči
            Allocation allocation =
                await RelayService.Instance.CreateAllocationAsync(maxConnections);

            // Získání Join Code
            string joinCode =
                await RelayService.Instance.GetJoinCodeAsync(
                    allocation.AllocationId
                );

            Debug.Log($"Relay Join Code: {joinCode}");

            // Zobrazení kódu v UI
            if (joinCodeText != null)
            {
                joinCodeText.text = joinCode;
            }

            // Nastavení Relay dat do UnityTransport
            UnityTransport transport =
                NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                AllocationUtils.ToRelayServerData(
                    allocation,
                    "dtls"
                )
            );

            // Spuštění hosta
            bool started = NetworkManager.Singleton.StartHost();

            if (!started)
            {
                Debug.LogError("Failed to start Host.");
                return;
            }

            Debug.Log("Relay Host started successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Host creation failed: {e}");
        }
    }

    public async void Join()
    {
        string joinCode = joinCodeInput.text.Trim();

        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogError("Join Code is empty.");
            return;
        }

        try
        {
            Debug.Log($"Joining Relay with code: {joinCode}");

            await InitializeUnityServices();

            // Najde Relay allocation podle kódu
            JoinAllocation allocation =
                await RelayService.Instance.JoinAllocationAsync(joinCode);

            // Nastavení Relay dat do transportu
            UnityTransport transport =
                NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                AllocationUtils.ToRelayServerData(
                    allocation,
                    "dtls"
                )
            );

            // Spuštění klienta
            bool started = NetworkManager.Singleton.StartClient();

            if (!started)
            {
                Debug.LogError("Failed to start Client.");
                return;
            }

            Debug.Log("Client started successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Join failed: {e}");
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        // Ignoruj hosta
        if (clientId == NetworkManager.Singleton.LocalClientId)
            return;

        Debug.Log($"Client {clientId} connected.");

        // Host načte mapu.
        // NGO SceneManager ji synchronizuje připojenému klientovi.
        NetworkManager.Singleton.SceneManager.LoadScene(
            "Mapa1",
            LoadSceneMode.Single
        );
    }
}