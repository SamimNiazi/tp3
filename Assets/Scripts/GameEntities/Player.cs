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


    // GameState peut etre nul si l'entite joueur est instanciee avant de charger MainScene
    private GameState GameState
    {
        get
        {
            if (m_GameState == null)
            {
                m_GameState = FindFirstObjectByType<GameState>();
            }
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
        m_Position.OnValueChanged += Reconciliate;
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        m_Position.OnValueChanged -= Reconciliate;
        base.OnNetworkDespawn();
    }
    private void FixedUpdate()
    {
        // Si le stun est active, rien n'est mis a jour.
        if (GameState == null || GameState.IsStunned)
        {
            return;
        }

        // Seul le serveur met à jour la position de l'entite.
        if (IsServer)
        {
            UpdatePositionServer();
        }

        // Seul le client qui possede cette entite peut envoyer ses inputs. 
        if (IsClient && IsOwner)
        {
            UpdateInputClient();
        }
    }

    private void UpdatePositionServer()
    {
        // Mise a jour de la position selon dernier input reçu, puis consommation de l'input
        if (m_InputQueue.Count > 0)
        {
            var input = m_InputQueue.Dequeue();
            StateTick position = m_Position.Value;
            position.position += input.input * m_Velocity * Time.fixedDeltaTime;
            position.input = input;

            // Gestion des collisions avec l'exterieur de la zone de simulation
            var size = GameState.GameSize;
            if (position.position.x - m_Size < -size.x)
            {
                position.position = new Vector2(-size.x + m_Size, position.position.y);
            }
            else if (position.position.x + m_Size > size.x)
            {
                position.position = new Vector2(size.x - m_Size, position.position.y);
            }

            if (position.position.y + m_Size > size.y)
            {
                position.position = new Vector2(position.position.x, size.y - m_Size);
            }
            else if (position.position.y - m_Size < -size.y)
            {
                position.position = new Vector2(position.position.x, -size.y + m_Size);
            }
            m_Position.Value = position;
        }
    }

    private void PredictPosition(InputTick inputTick)
    {
        var position = m_PredictedPosition;
        position.position += inputTick.input * m_Velocity * Time.fixedDeltaTime;
        position.input = inputTick;

        // Gestion des collisions avec l'exterieur de la zone de simulation
        var size = GameState.GameSize;
        if (position.position.x - m_Size < -size.x)
        {
            position.position = new Vector2(-size.x + m_Size, position.position.y);
        }
        else if (position.position.x + m_Size > size.x)
        {
            position.position = new Vector2(size.x - m_Size, position.position.y);
        }

        if (position.position.y + m_Size > size.y)
        {
            position.position = new Vector2(position.position.x, size.y - m_Size);
        }
        else if (position.position.y - m_Size < -size.y)
        {
            position.position = new Vector2(position.position.x, -size.y + m_Size);
        }
        m_PredictedPosition = position;
    }

    private void Reconciliate(StateTick oldState, StateTick state)
    {
        if (IsServer) { return; }

        if (!IsOwner) 
        {
            m_PredictedPosition = state;
            return;
        }

        while (m_StateTickQueue.Count > 0)
        {
            if (m_StateTickQueue.Peek().input.tick < state.input.tick) { m_StateTickQueue.Dequeue(); }
            else { break; }
        }

        if (m_StateTickQueue.Count > 0 && Vector2.Distance(m_StateTickQueue.Dequeue().position, state.position) > 0.001f)
        {
            LogStateQueue("After Enqueue");
            Debug.Log($"(Tick:{state.input.tick}, Pos:{state.position}) -> ");
            Queue<StateTick> correctedQueue = new Queue<StateTick>();
            m_PredictedPosition = state;
            foreach (var states in m_StateTickQueue)
            {
                PredictPosition(states.input);
                correctedQueue.Enqueue(m_PredictedPosition);
            }
            m_StateTickQueue = correctedQueue;
        }
    }

    private void UpdateInputClient()
    {
        Vector2 inputDirection = new Vector2(0, 0);
        if (Input.GetKey(KeyCode.W))
        {
            inputDirection += Vector2.up;
        }
        if (Input.GetKey(KeyCode.A))
        {
            inputDirection += Vector2.left;
        }
        if (Input.GetKey(KeyCode.S))
        {
            inputDirection += Vector2.down;
        }
        if (Input.GetKey(KeyCode.D))
        {
            inputDirection += Vector2.right;
        }
        inputDirection = inputDirection.normalized;
        InputTick input = new InputTick { input = inputDirection, tick = NetworkUtility.GetLocalTick()};
        SendInputServerRpc(input);
        PredictPosition(input);
        m_StateTickQueue.Enqueue(m_PredictedPosition);
    }


    [ServerRpc]
    private void SendInputServerRpc(InputTick input)
    {
        // On utilise une file pour les inputs pour les cas ou on en recoit plusieurs en meme temps.
        m_InputQueue.Enqueue(input);
    }

    private void LogStateQueue(string label = "")
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        sb.Append($"[StateQueue {label}] Count={m_StateTickQueue.Count} | ");

        foreach (var state in m_StateTickQueue)
        {
            sb.Append($"(Tick:{state.input.tick}, Pos:{state.position}) -> ");
        }

        Debug.Log(sb.ToString());
    }


}
