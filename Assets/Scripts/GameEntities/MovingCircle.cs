using Unity.Netcode;
using UnityEngine;

public struct CircleState : INetworkSerializeByMemcpy
{
    public Vector2 position;
    public Vector2 velocity;
    public float timestamp;
}

public class MovingCircle : NetworkBehaviour
{
    [SerializeField] private float m_Radius = 1f;
    public Vector2 InitialPosition, InitialVelocity;

    private NetworkVariable<CircleState> m_ServerState = new NetworkVariable<CircleState>();
    private GameState m_GameState;

    private Vector2 m_PredictedPos, m_PredictedVel;
    private bool m_HasNewState;
    private CircleState m_LatestState;

    private float TickDelta => 1f / NetworkManager.NetworkTickSystem.TickRate;
    public Vector2 Position => IsServer ? m_ServerState.Value.position : m_PredictedPos;

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
                timestamp = (float)NetworkManager.LocalTime.TimeAsFloat
            };
        }
        else
        {
            var initial = m_ServerState.Value;
            m_PredictedPos = initial.position;
            m_PredictedVel = initial.velocity;

            m_ServerState.OnValueChanged += (oldV, newV) =>
            {
                m_LatestState = newV;
                m_HasNewState = true;
            };
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
    }

    private void OnNetworkTick()
    {
        if (m_GameState == null)
            return;

        if (IsServer)
        {
            var state = m_ServerState.Value;
            float simTime = m_GameState.ServerTime.Value;

            if (!m_GameState.IsStunnedAtTime(simTime))
                state = SimulateStep(state, TickDelta);

            state.timestamp = simTime;
            m_ServerState.Value = state;
        }
        else
        {
            if (m_HasNewState)
            {
                Reconciliate(TickDelta);
                m_HasNewState = false;
            }

            float predictedWorldTime = m_GameState.ServerTime.Value + m_GameState.CurrentRTT;

            if (!m_GameState.IsStunnedAtTime(predictedWorldTime))
            {
                var next = SimulateStep(
                    new CircleState
                    {
                        position = m_PredictedPos,
                        velocity = m_PredictedVel,
                        timestamp = predictedWorldTime
                    },
                    TickDelta
                );

                m_PredictedPos = next.position;
                m_PredictedVel = next.velocity;
            }
        }
    }

    private void Reconciliate(float delta)
    {
        CircleState state = m_LatestState;

        float targetTime = m_GameState.ServerTime.Value + m_GameState.CurrentRTT;
        float timeToFastForward = targetTime - state.timestamp;

        int steps = Mathf.Max(0, Mathf.RoundToInt(timeToFastForward / delta));

        for (int i = 0; i < steps; i++)
        {
            float simTime = state.timestamp + (i * delta);

            if (!m_GameState.IsStunnedAtTime(simTime))
                state = SimulateStep(state, delta);
        }

        m_PredictedPos = state.position;
        m_PredictedVel = state.velocity;
    }

    private CircleState SimulateStep(CircleState state, float dt)
    {
        state.position += state.velocity * dt;

        var s = m_GameState.GameSize;

        if (Mathf.Abs(state.position.x) + m_Radius > s.x)
        {
            state.velocity.x *= -1;
            state.position.x = Mathf.Sign(state.position.x) * (s.x - m_Radius);
        }

        if (Mathf.Abs(state.position.y) + m_Radius > s.y)
        {
            state.velocity.y *= -1;
            state.position.y = Mathf.Sign(state.position.y) * (s.y - m_Radius);
        }

        return state;
    }

    private void Update()
    {
        transform.position = Position;
    }
}