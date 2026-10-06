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
using UnityEngine.UI;

public class Network : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField ipInput;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_Text joinCodeText;

    [Header("Network Mode")]
    [Tooltip("TRUE = normal IP/LAN connection. FALSE = Unity Relay.")]
    [SerializeField] private bool localhostMode = true;

    [Header("Relay")]
    [Tooltip("Number of clients allowed to connect to the host.")]
    [SerializeField] private int maxConnections = 1;

    private bool servicesInitialized = false;

    [SerializeField] private Toggle localhostToggle;

    public void SetLocalhostMode(bool value)
    {
        localhostMode = value;

        Debug.Log($"Network mode: {(localhostMode ? "LOCAL/IP" : "RELAY")}");
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
        localhostMode = localhostToggle != null && localhostToggle.isOn;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    // =========================================================
    // INITIALIZE UNITY SERVICES
    // =========================================================

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

    // =========================================================
    // HOST
    // =========================================================

    public async void Host()
    {
        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("Network is already running.");
            return;
        }

        if (localhostMode)
        {
            StartLocalHost();
        }
        else
        {
            await StartRelayHost();
        }
    }

    // =========================================================
    // LOCAL HOST
    // =========================================================

    private void StartLocalHost()
    {
        UnityTransport transport =
            NetworkManager.Singleton.GetComponent<UnityTransport>();

        // 0.0.0.0 znamená, že server naslouchá na všech
        // dostupných síťových rozhraních.
        transport.SetConnectionData(
            "0.0.0.0",
            7777
        );

        bool started = NetworkManager.Singleton.StartHost();

        if (!started)
        {
            Debug.LogError("Failed to start local Host.");
            return;
        }

        Debug.Log("Local Host started on port 7777.");
    }

    // =========================================================
    // RELAY HOST
    // =========================================================

    private async Task StartRelayHost()
    {
        try
        {
            Debug.Log("Initializing Unity Services...");

            await InitializeUnityServices();

            Debug.Log("Creating Relay allocation...");

            Allocation allocation =
                await RelayService.Instance.CreateAllocationAsync(
                    maxConnections
                );

            string joinCode =
                await RelayService.Instance.GetJoinCodeAsync(
                    allocation.AllocationId
                );

            Debug.Log($"Relay Join Code: {joinCode}");

            if (joinCodeText != null)
            {
                joinCodeText.text = joinCode;
            }

            UnityTransport transport =
                NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                AllocationUtils.ToRelayServerData(
                    allocation,
                    "dtls"
                )
            );

            bool started = NetworkManager.Singleton.StartHost();

            if (!started)
            {
                Debug.LogError("Failed to start Relay Host.");
                return;
            }

            Debug.Log($"Relay Host started. Join Code: {joinCode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Relay Host failed: {e}");
        }
    }

    // =========================================================
    // JOIN
    // =========================================================

    public async void Join()
    {
        if (NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("Network is already running.");
            return;
        }

        if (localhostMode)
        {
            StartLocalClient();
        }
        else
        {
            await StartRelayClient();
        }
    }

    // =========================================================
    // LOCAL CLIENT
    // =========================================================

    private void StartLocalClient()
    {
        UnityTransport transport =
            NetworkManager.Singleton.GetComponent<UnityTransport>();

        string ip = "127.0.0.1";

        if (ipInput != null && !string.IsNullOrWhiteSpace(ipInput.text))
        {
            ip = ipInput.text.Trim();
        }

        transport.SetConnectionData(
            ip,
            7777
        );

        bool started = NetworkManager.Singleton.StartClient();

        if (!started)
        {
            Debug.LogError("Failed to start local Client.");
            return;
        }

        Debug.Log($"Local Client connecting to {ip}:7777");
    }

    // =========================================================
    // RELAY CLIENT
    // =========================================================

    private async Task StartRelayClient()
    {
        string joinCode = "";

        if (joinCodeInput != null)
        {
            joinCode = joinCodeInput.text.Trim();
        }

        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogError("Join Code is empty.");
            return;
        }

        try
        {
            Debug.Log($"Joining Relay with code: {joinCode}");

            await InitializeUnityServices();

            JoinAllocation allocation =
                await RelayService.Instance.JoinAllocationAsync(
                    joinCode
                );

            UnityTransport transport =
                NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                AllocationUtils.ToRelayServerData(
                    allocation,
                    "dtls"
                )
            );

            bool started = NetworkManager.Singleton.StartClient();

            if (!started)
            {
                Debug.LogError("Failed to start Relay Client.");
                return;
            }

            Debug.Log("Relay Client started successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Relay Join failed: {e}");
        }
    }

    // =========================================================
    // CLIENT CONNECTED
    // =========================================================

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        // Ignoruj samotného hosta.
        if (clientId == NetworkManager.Singleton.LocalClientId)
            return;

        Debug.Log($"Client {clientId} connected.");

        NetworkManager.Singleton.SceneManager.LoadScene(
            "Mapa1",
            LoadSceneMode.Single
        );
    }
}
