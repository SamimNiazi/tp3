using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public struct CircleState : INetworkSerializeByMemcpy
{
    public Vector2 position;
    public Vector2 velocity;
    public int tick;
}

public class MovingCircle : NetworkBehaviour
{
    [SerializeField] private float m_Radius = 1f;
    [SerializeField] private float m_PositionReconcileEpsilon = 0.0001f;
    [SerializeField] private float m_VelocityReconcileEpsilon = 0.0001f;
    [SerializeField] private int m_MaxHistorySize = 512;

    public Vector2 InitialPosition;
    public Vector2 InitialVelocity;

    private NetworkVariable<CircleState> m_ServerState = new NetworkVariable<CircleState>();

    private GameState m_GameState;

    private CircleState m_PredictedState;
    private CircleState m_LatestServerState;
    private bool m_HasNewServerState;

    private readonly List<CircleState> m_StateHistory = new List<CircleState>();

    public Vector2 Position => IsServer ? m_ServerState.Value.position : m_PredictedState.position;
    public Vector2 Velocity => IsServer ? m_ServerState.Value.velocity : m_PredictedState.velocity;

    private float TickDelta => 1f / NetworkManager.NetworkTickSystem.TickRate;

    private void Awake()
    {
        m_GameState = FindFirstObjectByType<GameState>();
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;

        if (IsServer)
        {
            m_ServerState.Value = new CircleState
            {
                position = InitialPosition,
                velocity = InitialVelocity,
                tick = NetworkUtility.GetServerTick()
            };
        }
        else
        {
            m_PredictedState = m_ServerState.Value;
            m_LatestServerState = m_ServerState.Value;

            m_StateHistory.Clear();
            m_StateHistory.Add(m_PredictedState);

            m_ServerState.OnValueChanged += OnServerStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;

        if (!IsServer)
            m_ServerState.OnValueChanged -= OnServerStateChanged;
    }

    private void OnServerStateChanged(CircleState oldState, CircleState newState)
    {
        m_LatestServerState = newState;
        m_HasNewServerState = true;
    }

    private void OnNetworkTick()
    {
        if (m_GameState == null)
            return;

        if (IsServer)
        {
            UpdateServer();
            return;
        }

        int predictedTick = GetPredictedTick();
        int currentServerTickEstimate = NetworkUtility.GetServerTick();

        if (m_HasNewServerState)
        {
            TryReconciliate(predictedTick, currentServerTickEstimate);
            m_HasNewServerState = false;
        }

        while (m_PredictedState.tick < predictedTick)
        {
            int nextSimulatedTick = m_PredictedState.tick + 1;

            // IMPORTANT :
            // le ghost peut être avancé visuellement,
            // mais le stun reste évalué au temps courant serveur estimé.
            m_PredictedState = SimulateOneTick(
                m_PredictedState,
                nextSimulatedTick,
                currentServerTickEstimate
            );

            AddStateToHistory(m_PredictedState);
        }

        CleanupHistory();
    }

    private void UpdateServer()
    {
        int currentServerTick = NetworkUtility.GetServerTick();
        CircleState state = m_ServerState.Value;

        while (state.tick < currentServerTick)
        {
            state = SimulateOneTick(state, state.tick + 1, state.tick + 1);
        }

        m_ServerState.Value = state;
    }

    private int GetPredictedTick()
    {
        int localTick = NetworkUtility.GetLocalTick();
        ulong rttMs = NetworkUtility.GetCurrentRtt(NetworkManager.ServerClientId);

        float tickRate = NetworkManager.NetworkTickSystem.TickRate;
        int halfRttTicks = Mathf.RoundToInt((rttMs / 1000f) * 0.5f * tickRate);

        return localTick + halfRttTicks;
    }

    private void TryReconciliate(int predictedTick, int currentServerTickEstimate)
    {
        CircleState? predictedAtSameTick = FindHistoryState(m_LatestServerState.tick);
        bool mustReconcile = true;

        if (predictedAtSameTick.HasValue)
        {
            mustReconcile = StatesDiffer(predictedAtSameTick.Value, m_LatestServerState);
        }

        if (!mustReconcile)
        {
            RemoveHistoryUpToTick(m_LatestServerState.tick - 1);
            return;
        }

        m_PredictedState = m_LatestServerState;

        m_StateHistory.Clear();
        m_StateHistory.Add(m_PredictedState);

        while (m_PredictedState.tick < predictedTick)
        {
            int nextSimulatedTick = m_PredictedState.tick + 1;

            m_PredictedState = SimulateOneTick(
                m_PredictedState,
                nextSimulatedTick,
                currentServerTickEstimate
            );

            AddStateToHistory(m_PredictedState);
        }
    }

    private CircleState? FindHistoryState(int tick)
    {
        for (int i = 0; i < m_StateHistory.Count; i++)
        {
            if (m_StateHistory[i].tick == tick)
                return m_StateHistory[i];
        }

        return null;
    }

    private bool StatesDiffer(CircleState a, CircleState b)
    {
        bool positionDiffers =
            (a.position - b.position).sqrMagnitude >
            m_PositionReconcileEpsilon * m_PositionReconcileEpsilon;

        bool velocityDiffers =
            (a.velocity - b.velocity).sqrMagnitude >
            m_VelocityReconcileEpsilon * m_VelocityReconcileEpsilon;

        return positionDiffers || velocityDiffers;
    }

    private CircleState SimulateOneTick(CircleState state, int simulatedTick, int stunQueryTick)
    {
        state.tick = simulatedTick;

        if (m_GameState.IsStunnedAtTick(stunQueryTick))
            return state;

        state.position += state.velocity * TickDelta;

        Vector2 size = m_GameState.GameSize;

        if (state.position.x - m_Radius < -size.x)
        {
            state.position = new Vector2(-size.x + m_Radius, state.position.y);
            state.velocity = new Vector2(-state.velocity.x, state.velocity.y);
        }
        else if (state.position.x + m_Radius > size.x)
        {
            state.position = new Vector2(size.x - m_Radius, state.position.y);
            state.velocity = new Vector2(-state.velocity.x, state.velocity.y);
        }

        if (state.position.y - m_Radius < -size.y)
        {
            state.position = new Vector2(state.position.x, -size.y + m_Radius);
            state.velocity = new Vector2(state.velocity.x, -state.velocity.y);
        }
        else if (state.position.y + m_Radius > size.y)
        {
            state.position = new Vector2(state.position.x, size.y - m_Radius);
            state.velocity = new Vector2(state.velocity.x, -state.velocity.y);
        }

        return state;
    }

    private void AddStateToHistory(CircleState state)
    {
        m_StateHistory.Add(state);

        if (m_StateHistory.Count > m_MaxHistorySize)
        {
            int extra = m_StateHistory.Count - m_MaxHistorySize;
            m_StateHistory.RemoveRange(0, extra);
        }
    }

    private void RemoveHistoryUpToTick(int tick)
    {
        m_StateHistory.RemoveAll(s => s.tick <= tick);
    }

    private void CleanupHistory()
    {
        int minUsefulTick = m_PredictedState.tick - m_MaxHistorySize;
        m_StateHistory.RemoveAll(s => s.tick < minUsefulTick);
    }
}