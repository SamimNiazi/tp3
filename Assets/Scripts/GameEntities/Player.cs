using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public struct InputData : INetworkSerializeByMemcpy
{
    public Vector2 input;
    public bool stunPressed;
    public float timestamp;
}

public struct PlayerState : INetworkSerializeByMemcpy
{
    public InputData input;
    public Vector2 position;
}

public class Player : NetworkBehaviour
{
    [SerializeField] private float m_Velocity = 5f;
    [SerializeField] private float m_Size = 1f;

    private GameState m_GameState;
    private PlayerState m_PredictedState;
    private float TickDelta => 1f / NetworkManager.NetworkTickSystem.TickRate;

    private NetworkVariable<PlayerState> m_ServerState = new NetworkVariable<PlayerState>();
    public Vector2 Position => (IsClient && IsOwner) ? m_PredictedState.position : m_ServerState.Value.position;

    private Queue<InputData> m_InputQueue = new Queue<InputData>();
    private List<PlayerState> m_StateHistory = new List<PlayerState>();

    private bool m_StunBuffered;
    private bool m_HasNewServerState;
    private PlayerState m_LatestServerState;

    private void Awake()
    {
        m_GameState = FindFirstObjectByType<GameState>();
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;

        m_ServerState.OnValueChanged += (oldVal, newVal) =>
        {
            if (IsClient && IsOwner)
            {
                m_LatestServerState = newVal;
                m_HasNewServerState = true;
            }
        };

        if (IsServer)
        {
            m_ServerState.Value = new PlayerState
            {
                position = transform.position,
                input = default
            };
        }

        if (IsOwner)
        {
            m_PredictedState.position = transform.position;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
    }

    private void Update()
    {
        if (IsClient && IsOwner && Input.GetKeyDown(KeyCode.Space))
            m_StunBuffered = true;

        transform.position = Position;
    }

    private void OnNetworkTick()
    {
        if (m_GameState == null)
            return;

        if (IsServer)
            ProcessServer();

        if (IsClient && IsOwner)
        {
            if (m_HasNewServerState)
                Reconciliate();

            ProcessClient();
        }
    }

    private void ProcessServer()
    {
        while (m_InputQueue.Count > 0)
        {
            var input = m_InputQueue.Dequeue();

            if (input.stunPressed)
                m_GameState.ApplyStun(input.timestamp);

            PlayerState state = m_ServerState.Value;

            if (!m_GameState.IsStunnedAtTime(input.timestamp))
                state.position += input.input * m_Velocity * TickDelta;

            state.input = input;
            state.position = ClampToGameArea(state.position);
            m_ServerState.Value = state;
        }
    }

    private void ProcessClient()
    {
        Vector2 dir = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;

        float actionTime = m_GameState.ServerTime.Value + m_GameState.CurrentRTT;

        InputData input = new InputData
        {
            input = dir,
            stunPressed = m_StunBuffered,
            timestamp = actionTime
        };

        m_StunBuffered = false;

        SendInputServerRpc(input);

        if (input.stunPressed)
            m_GameState.ApplyStun(input.timestamp);

        PredictPosition(input);
    }

    private void PredictPosition(InputData input)
    {
        if (!m_GameState.IsStunnedAtTime(input.timestamp))
            m_PredictedState.position += input.input * m_Velocity * TickDelta;

        m_PredictedState.input = input;
        m_PredictedState.position = ClampToGameArea(m_PredictedState.position);
        m_StateHistory.Add(m_PredictedState);
    }

    private void Reconciliate()
    {
        m_StateHistory.RemoveAll(s => s.input.timestamp <= m_LatestServerState.input.timestamp);

        m_PredictedState = m_LatestServerState;

        List<PlayerState> history = new List<PlayerState>(m_StateHistory);
        m_StateHistory.Clear();

        foreach (var state in history)
            PredictPosition(state.input);

        m_HasNewServerState = false;
    }

    private Vector2 ClampToGameArea(Vector2 pos)
    {
        var size = m_GameState.GameSize;
        pos.x = Mathf.Clamp(pos.x, -size.x + m_Size, size.x - m_Size);
        pos.y = Mathf.Clamp(pos.y, -size.y + m_Size, size.y - m_Size);
        return pos;
    }

    [ServerRpc]
    private void SendInputServerRpc(InputData input)
    {
        m_InputQueue.Enqueue(input);
    }
}