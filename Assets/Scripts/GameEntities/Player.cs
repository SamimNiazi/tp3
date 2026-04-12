using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public struct StateTick : INetworkSerializeByMemcpy
{
    public InputTick input;
    public Vector2 position;
}
public struct InputTick : INetworkSerializeByMemcpy
{
    public Vector2 input;
    public int tick;
}

public class Player : NetworkBehaviour
{
    [SerializeField]
    private float m_Velocity;

    [SerializeField]
    private float m_Size = 1;

    private GameState m_GameState;

    private StateTick m_PredictedPosition;

    private float TickDelta => 1f / NetworkManager.Singleton.NetworkConfig.TickRate;

    private GameState GameState
    {
        get
        {
            if (m_GameState == null)
                m_GameState = FindFirstObjectByType<GameState>();
            return m_GameState;
        }
    }

    private NetworkVariable<StateTick> m_Position = new NetworkVariable<StateTick>();

    public Vector2 Position => (IsClient && IsOwner) ? m_PredictedPosition.position : m_Position.Value.position;

    private Queue<InputTick> m_InputQueue = new Queue<InputTick>();
    private Queue<StateTick> m_StateTickQueue = new Queue<StateTick>();

    private void Awake()
    {
        m_GameState = FindFirstObjectByType<GameState>();
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;
        m_Position.OnValueChanged += Reconciliate;

        //if (IsClient && !IsServer)
            //GameState.m_IsStunned.OnValueChanged += ReconciliateStunTick;

        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;

        m_Position.OnValueChanged -= Reconciliate;

        //if (IsClient && !IsServer)
            //GameState.m_IsStunned.OnValueChanged -= ReconciliateStunTick;

        base.OnNetworkDespawn();
    }

    private void ReconciliateStunTick(StunStatus oldStatus, StunStatus newStatus)
    {
        GameState.local_Stunned = newStatus;
    }
    private void OnNetworkTick()
    {
        if (GameState == null) return;

        //Debug.Log(GameState.local_Stunned.endTick);
        //Debug.Log(GameState.m_IsStunned.Value.endTick);
        if (IsServer)
            UpdatePositionServer();

        if (IsClient && IsOwner)
        {
            if (!IsStunnedAtTick(NetworkManager.NetworkTickSystem.LocalTime.Tick))
                UpdateInputClient();
        }
    }

    private bool IsStunnedAtTick(int tick)
    {
        var status = GameState.IsStunned;
        //Debug.Log(status.endTick);
        return status.IsStunned && tick < status.endTick;
    }

    private void UpdatePositionServer()
    {
        if (m_InputQueue.Count > 0)
        {
            var input = m_InputQueue.Dequeue();
            StateTick position = m_Position.Value;

            if (!IsStunnedAtTick(input.tick))
                position.position += input.input * m_Velocity * TickDelta;

            position.input = input;
            position.position = ClampToGameArea(position.position);
            m_Position.Value = position;
        }
    }

    private void PredictPosition(InputTick inputTick)
    {
        var position = m_PredictedPosition;

        if (!IsStunnedAtTick(inputTick.tick))
            position.position += inputTick.input * m_Velocity * TickDelta;

        position.input = inputTick;
        position.position = ClampToGameArea(position.position);
        m_PredictedPosition = position;
    }

    private void Reconciliate(StateTick oldState, StateTick state)
    {
        if (IsServer) return;

        if (!IsOwner)
        {
            m_PredictedPosition = state;
            return;
        }

        while (m_StateTickQueue.Count > 0)
        {
            if (m_StateTickQueue.Peek().input.tick < state.input.tick)
                m_StateTickQueue.Dequeue();
            else
                break;
        }

        if (m_StateTickQueue.Count > 0 &&
            Vector2.Distance(m_StateTickQueue.Peek().position, state.position) > 0.001f)
        {
            m_StateTickQueue.Dequeue();
            //LogStateQueue();
            m_PredictedPosition = state;
            Queue<StateTick> correctedQueue = new Queue<StateTick>();

            foreach (var s in m_StateTickQueue)
            {
                PredictPosition(s.input);
                correctedQueue.Enqueue(m_PredictedPosition);
            }
            //Debug.Log($"(Tick:{state.input.tick}, Pos:{state.position}) -> ");
            m_StateTickQueue = correctedQueue;
        }
    }

    private void UpdateInputClient()
    {
        Vector2 inputDirection = Vector2.zero;
        if (Input.GetKey(KeyCode.W)) inputDirection += Vector2.up;
        if (Input.GetKey(KeyCode.A)) inputDirection += Vector2.left;
        if (Input.GetKey(KeyCode.S)) inputDirection += Vector2.down;
        if (Input.GetKey(KeyCode.D)) inputDirection += Vector2.right;
        inputDirection = inputDirection.normalized;

        InputTick input = new InputTick
        {
            input = inputDirection,
            tick = NetworkUtility.GetLocalTick()
        };

        SendInputServerRpc(input);
        PredictPosition(input);
        m_StateTickQueue.Enqueue(m_PredictedPosition);
    }

    private Vector2 ClampToGameArea(Vector2 pos)
    {
        var size = GameState.GameSize;
        pos.x = Mathf.Clamp(pos.x, -size.x + m_Size, size.x - m_Size);
        pos.y = Mathf.Clamp(pos.y, -size.y + m_Size, size.y - m_Size);
        return pos;
    }

    [ServerRpc]
    private void SendInputServerRpc(InputTick input)
    {
        m_InputQueue.Enqueue(input);
    }

    private void LogStateQueue(string label = "")
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[StateQueue {label}] Count={m_StateTickQueue.Count} | ");
        foreach (var s in m_StateTickQueue)
            sb.Append($"(Tick:{s.input.tick}, Pos:{s.position}) -> ");
        Debug.Log(sb.ToString());
    }
}