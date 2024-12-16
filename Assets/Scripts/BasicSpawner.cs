using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
// using UnityEngine.Windows;
using static Unity.Collections.Unicode;
using static UnityEngine.UI.CanvasScaler;

public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{

    // Singleton Instance
    public static BasicSpawner Instance { get; private set; }

    private NetworkRunner _NetRunner;
    public NetworkRunner NetRunner => _NetRunner; // Giving access to runner from other scripts

    [SerializeField] public int unitCountPerPlayer = 5;
    [SerializeField] public int unitAllowedOffset = 3;

    [SerializeField] private NetworkPrefabRef _UnitPrefab;
    [SerializeField] private GameObject _DestinationMarkerPrefab;
    [SerializeField] private SelectionManager _selectionManager;
    [SerializeField] private GameObject _HostManagerPrefab;
    // to call RPCs from HostManager
    public HostManager HostManagerLink => _HostManagerPrefab.GetComponent<HostManager>();
    public SelectionManager SelectionManagerLink => _selectionManager;  // to access from Unit when need to get prediction on selected units center

    // Client request to send destination point to the host
    private Vector3 _pendingTargetPosition = Vector3.zero;
    // ���� ������� ����� ����������
    private bool _hasPendingTarget = false;
    public bool HasPendingTarget => _hasPendingTarget;

    // ��� ������ ������ Host
    private bool _isFreezeSimulated = false; // ���� �����

    private Dictionary<PlayerRef, List<NetworkObject>> _spawnedPlayers = new();

    // List of commands came to Host
    private Queue<Command> _commandQueue = new Queue<Command>();

    private void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // Destroy duplicate object
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // Preserve between scenes
    }

    async void StartGame(GameMode mode)
    {
        // Test if _UnitPrefab initialized
        if (_UnitPrefab == null)
        {
            Debug.LogError("Unit prefab is not set.");
            return;
        }

        // Create the Fusion runner and let it know that we will be providing user input
        _NetRunner = gameObject.AddComponent<NetworkRunner>();
        _NetRunner.ProvideInput = true;

        // Create the NetworkSceneInfo from the current scene
        var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
        var sceneInfo = new NetworkSceneInfo();
        if (scene.IsValid)
        {
            sceneInfo.AddSceneRef(scene, LoadSceneMode.Additive);
        }

        // Start or join (depends on gamemode) a session with a specific name
        await _NetRunner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = "TestRoom",
            Scene = scene,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        // running host manager
        if (_NetRunner.IsServer)
        {
            _NetRunner.Spawn(_HostManagerPrefab, Vector3.zero, Quaternion.identity, null);
        }
    }

    public void Update()
    {
        // �������� ��� ��������� ����� �� ������
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _isFreezeSimulated = !_isFreezeSimulated;
            Debug.Log(_isFreezeSimulated ? "Host paused." : "Host resumed.");
        }
    }

    private void OnGUI()
    {
        if (_NetRunner == null)
        {
            if (GUI.Button(new Rect(0, 0, 200, 40), "Host"))
            {
                StartGame(GameMode.Host);
            }
            if (GUI.Button(new Rect(0, 40, 200, 40), "Join"))
            {
                StartGame(GameMode.Client);
            }
        }
    }
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player joined: {player}");
        if (runner.IsServer)
        {
            SpawnPlayerUnits(runner, player);
            // �������������� ������ �� ������ ����� �������� (�����, ���������). ���������, ����� ������ ������������ � ����
            StartCoroutine(SyncUnitsData());
        }
    }

    private IEnumerator SyncUnitsData()
    {
        // ���� 2 �������
        yield return new WaitForSeconds(2f);

        // ����� �� ���� ������ � ����� � ���������� ������ � ��� ����� RPC
        foreach (var unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            // ���������� ������ � �����
            unit.RPC_SendSpawnedUnitInfo(unit.GetComponent<NetworkObject>().Id, unit.name, unit.materialIndex);
        }
    }

    // ����� �������������� �������
    private int spawnedPlayersCount;

    private void SpawnPlayerUnits(NetworkRunner runner, PlayerRef player)
    {
        // Create a unique center position for each player
        var playerSpawnCenterPosition = new Vector3((player.RawEncoded % runner.Config.Simulation.PlayerCount) * 3, 1, 0);

        var unitList = new List<NetworkObject>();

        spawnedPlayersCount += 1;

        string materialName = $"Materials/UnitPayer{spawnedPlayersCount}_Material"; // ���� ��� ����������
        Material material = Resources.Load<Material>(materialName);

        // Spawn Player units on the field
        for (int i = 0; i < unitCountPerPlayer; i++)
        {
            // place units in circle around player center
            Vector3 spawnPosition = playerSpawnCenterPosition + new Vector3(Mathf.Cos(i * Mathf.PI * 2 / unitCountPerPlayer), 0, Mathf.Sin(i * Mathf.PI * 2 / unitCountPerPlayer));
            // spawn unit
            var networkUnitObject = runner.Spawn(_UnitPrefab, spawnPosition, Quaternion.identity, player);

            if (networkUnitObject == null)
            {
                Debug.LogError("Failed to spawn network unit object.");
                return;
            }

            var unit = networkUnitObject.GetComponent<Unit>();
            unit.SetOwner(player); // Set unit owner directly (in case it may be given to other player) 
            // ������� ��� ��� �����, � �������� �������� ������ � ������ �����
            unit.name = $"Unit_{player.RawEncoded}_{i}";
            // ������� ������ ���������, ����� ������ ������� ���� �����
            unit.materialIndex = spawnedPlayersCount;

            // ������� ��������, ��������������� ������ (�� ������� ������) (UnitPayer1_Material, UnitPayer2_Material, UnitPayer3_Material, UnitPayer4_Material)
            if (material != null)
            {
                unit.GetComponentInChildren<MeshRenderer>().material = material;
                Debug.Log("Material successfully loaded and applied.");
            }
            else
            {
                Debug.LogError("Failed to load material: " + materialName);
            }

            unitList.Add(networkUnitObject);

        }
        // Keep track of the player avatars for easy access
        _spawnedPlayers.Add(player, unitList);
        //   Debug.Log($"Spawned {unitList.Count} units for player: {player}  material name: {materialName}");
        Debug.Log($"Spawned {unitList.Count} units for player: {player}");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)  // not in Phoron turorial somehow, but looks like it needed
        {
            if (_spawnedPlayers.TryGetValue(player, out var networkObjects))
            {
                // Despawn all player units
                foreach (var networkObject in networkObjects)
                {
                    runner.Despawn(networkObject);
                }

                // Delete the player from the list
                _spawnedPlayers.Remove(player);
            }
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        // ������������ ����������� (��� ���������� ������� �������)
        if (Input.GetKey(KeyCode.W))
            data.direction += Vector3.forward;
        if (Input.GetKey(KeyCode.S))
            data.direction += Vector3.back;
        if (Input.GetKey(KeyCode.A))
            data.direction += Vector3.left;
        if (Input.GetKey(KeyCode.D))
            data.direction += Vector3.right;

        // ���� ���� ����� ����
        if (HasPendingTarget)
        {
            data.targetPosition = _pendingTargetPosition;
            data.timestamp = Time.time;

            data.unitCount = Mathf.Min(_selectionManager.SelectedUnits.Count, UnitIdList.MaxUnits);
            for (int i = 0; i < data.unitCount; i++)
            {
                var networkObject = _selectionManager.SelectedUnits[i].GetComponent<NetworkObject>();
                if (networkObject != null)
                {
                    data.unitIds[i] = networkObject.Id.Raw;
                }
                else
                {
                    Debug.LogError($"Unit {_selectionManager.SelectedUnits[i].name} is missing a NetworkObject!");
                }
            }
            _hasPendingTarget = false;
        }
        input.Set(data);
    }



    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void HostProcessCommandsFromNetwork()
    {

        // �������� ������ �� ���� �������� �������
        foreach (var player in NetRunner.ActivePlayers)
        {
            if (NetRunner.TryGetInputForPlayer<NetworkInputData>(player, out var input))
            {
                HostReceiveCommand(player, input); // ��������� ������� � �������
            }
        }

        // ������� freeze ����
        if (_isFreezeSimulated)
        {
            Debug.LogWarning("Host is Freezed. Skipping HostProcessCommands.");
            return; // ���� ���� "���������", �� ������������ �������
        }

        // ������������ ������� ������
        HostProcessCommands();
    }

    private void HostProcessCommands()
    {
        // ��������� ������� �� �������
        var sortedCommands = _commandQueue.OrderBy(c => c.Input.timestamp).ToList();

        foreach (var command in sortedCommands)
        {
            // ������� ������ ���� ������ ��� ������� ���� ��������� � ������� input (��� ���������� ����������� bearing point)
            var changedUnits = Unit.GetUnitsInInput(command.Input);
            // ������ ����� ��������� ������ - ��� bearing point
            var center = GetCenterOfUnits(changedUnits);

            // for (int i = 0; i < command.Input.unitIds.Length; i++)
            for (int i = 0; i < command.Input.unitCount; i++)
            {
                var unitId = command.Input.unitIds[i];
                var unit = FindUnitById(unitId);
                if (unit != null)
                {
                    // ���������� ���������� �������
                    if (command.Input.timestamp > unit.LastCommandTimestamp)
                    {
                        // ������ ������ ����� ��� ������� ����� (�������� ����� ��� ������)
                        var unitTargetPosition = unit.GetUnitTargetPosition(center, command.Input.targetPosition);

                        // unit.HostSetTarget(command.Input.targetPosition, command.Input.timestamp);
                        unit.HostSetTarget(unitTargetPosition, command.Input.timestamp);
                    }
                    else
                    {
                        Debug.LogWarning($"Ignored outdated command for unit {unit.name} at {command.Input.timestamp}");
                    }
                }
            }
        }

        _commandQueue.Clear(); // ������� ������� ����� ���������
    }

    public void HostReceiveCommand(PlayerRef player, NetworkInputData input)
    {
        var command = new Command
        {
            Player = player,
            Input = input
        };

        _commandQueue.Enqueue(command); // ��������� ������� � �������
    }


    private Unit FindUnitById(uint unitId)
    {
        // ������������, ��� ��� ����� ����� NetworkObject
        foreach (var unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var networkObject = unit.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.Id.Raw == unitId)
            {
                return unit;
            }
        }
        return null;
    }


    public void HandleDestinationInput(Vector3 targetPosition)
    {
        // ���� ���� ���� �� ���� ���������� ������
        if (_selectionManager.SelectedUnits.Count == 0)
            return;

        // ������ instantiate _DestinationMarkerPrefab ������� � ���� �����, � ������� ��� ����� 2 �������
        var marker = Instantiate(_DestinationMarkerPrefab, targetPosition, Quaternion.identity);
        Destroy(marker, 2);

        Debug.Log($"Received destination input: {targetPosition}");
        _pendingTargetPosition = targetPosition; // ��������� ����� ����������
        _hasPendingTarget = true; // ������������� ����
    }

    // ������� ���������� ����� ������ ������
    public Vector3 GetCenterOfUnits(List<Unit> units)
    {
        var center = Vector3.zero;
        foreach (var unit in units)
        {
            center += unit.transform.position;
        }
        center /= units.Count;
        return center;
    }
}

public class Command
{
    public PlayerRef Player;         // ID �������, �� �������� ������ �������
    public NetworkInputData Input;  // ������ ��������� ������ �� NetworkInputData
}
