using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Network : NetworkBehaviour
{
    [SerializeField] TMP_InputField ipInput;

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    public void Host()
    {
        NetworkManager.Singleton.StartHost();
    }

    public void Join()
    {
        UnityTransport transport =
            NetworkManager.Singleton.GetComponent<UnityTransport>();

        transport.SetConnectionData(
            ipInput.text,
            7777
        );

        NetworkManager.Singleton.StartClient();
    }

        private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;
            
        // ignoruj hosta
        if (clientId == NetworkManager.Singleton.LocalClientId)
            return;

        NetworkManager.Singleton.SceneManager.LoadScene(
            "Mapa1",
            LoadSceneMode.Single
        );
    }
}