using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;

/// <summary>
/// NetworkManager compatível com Photon Fusion 2.0.8
/// </summary>
public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Network Configuration")]
    [SerializeField] private NetworkRunner _runnerPrefab;

    private NetworkRunner _runner;
    private bool _isShuttingDown = false;

    // Eventos para outros sistemas se inscreverem
    public event Action<NetworkRunner> OnServerConnected;
    public event Action<NetworkRunner, NetDisconnectReason> OnServerDisconnected;

    #region Unity Lifecycle

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_runner != null)
        {
            _runner.RemoveCallbacks(this);
        }
    }

    #endregion

    #region Public API

    public async void StartHost(string sessionName)
    {
        _runner = Instantiate(_runnerPrefab);
        _runner.name = "NetworkRunner_Host";
        _runner.AddCallbacks(this);

        var startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Host,
            SessionName = sessionName,
            Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await _runner.StartGame(startGameArgs);

        if (result.Ok)
        {
            Debug.Log($"Host iniciado com sucesso: {sessionName}");
        }
        else
        {
            Debug.LogError($"Falha ao iniciar host: {result.ShutdownReason}");
        }
    }

    public async void JoinSession(string sessionName)
    {
        _runner = Instantiate(_runnerPrefab);
        _runner.name = "NetworkRunner_Client";
        _runner.AddCallbacks(this);

        var startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = sessionName,
            Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await _runner.StartGame(startGameArgs);

        if (result.Ok)
        {
            Debug.Log($"Conectado à sessão: {sessionName}");
        }
        else
        {
            Debug.LogError($"Falha ao conectar: {result.ShutdownReason}");
        }
    }

    #endregion

    #region INetworkRunnerCallbacks - Conexão

    /// <summary>
    /// Callback quando conecta com sucesso ao servidor
    /// MUDANÇA FUSION 2.0: Agora recebe NetworkRunner como parâmetro
    /// </summary>
    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log($"? Conectado ao servidor! Mode: {runner.GameMode}");
        OnServerConnected?.Invoke(runner);
    }

    /// <summary>
    /// Callback quando desconecta do servidor
    /// MUDANÇA FUSION 2.0: Agora recebe NetDisconnectReason como segundo parâmetro
    /// </summary>
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"? Desconectado do servidor! Razão: {reason}");

        // Tratamento baseado na razão da desconexão
        HandleDisconnect(runner, reason);

        OnServerDisconnected?.Invoke(runner, reason);
    }

    /// <summary>
    /// Lógica centralizada de tratamento de desconexão
    /// </summary>
    private void HandleDisconnect(NetworkRunner runner, NetDisconnectReason reason)
    {
        // MIGRAÇÃO: Substituição de PluginDisconnect/DisconnectedByPluginLogic
        switch (reason)
        {
            case NetDisconnectReason.GameClosed:
                // Equivalente ao antigo "DisconnectedByPluginLogic"
                Debug.Log("Sessão encerrada pelo servidor");
                ReturnToMainMenu();
                break;

            case NetDisconnectReason.Timeout:
                Debug.LogWarning("Timeout - Tentando reconectar...");
                if (!_isShuttingDown)
                {
                    StartCoroutine(AttemptReconnect(runner));
                }
                break;

            case NetDisconnectReason.Error:
                Debug.LogError("Erro de conexão detectado");
                ReturnToMainMenu();
                break;

            case NetDisconnectReason.ServerFull:
                Debug.LogWarning("Servidor está cheio");
                ShowErrorMessage("Servidor lotado. Tente novamente mais tarde.");
                break;

            case NetDisconnectReason.GameNotFound:
                Debug.LogWarning("Sessão não encontrada");
                ShowErrorMessage("Sessão não existe mais.");
                break;

            default:
                Debug.Log($"Desconexão: {reason}");
                ReturnToMainMenu();
                break;
        }
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"Falha na conexão: {reason}");
        ShowErrorMessage($"Falha ao conectar: {reason}");
    }

    #endregion

    #region INetworkRunnerCallbacks - Jogadores

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Jogador entrou: {player.PlayerId}");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Jogador saiu: {player.PlayerId}");
    }

    #endregion

    #region INetworkRunnerCallbacks - Outros (Implementação Mínima)

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"Runner desligado: {shutdownReason}");
        _isShuttingDown = true;
    }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }

    // NOVOS CALLBACKS FUSION 2.0 - AOI (Area of Interest)
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    // NOVOS CALLBACKS FUSION 2.0 - Reliable Data Streams
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    #endregion

    #region Helper Methods

    private IEnumerator AttemptReconnect(NetworkRunner runner)
    {
        yield return new WaitForSeconds(2f);

        if (!_isShuttingDown && runner != null)
        {
            Debug.Log("Tentando reconectar...");
            // Lógica de reconexão aqui
        }
    }

    private void ReturnToMainMenu()
    {
        Debug.Log("Retornando ao menu principal...");
        // Implementar lógica de retorno ao menu
    }

    private void ShowErrorMessage(string message)
    {
        Debug.LogWarning($"UI Error: {message}");
        // Implementar exibição de mensagem na UI
    }

    #endregion
}