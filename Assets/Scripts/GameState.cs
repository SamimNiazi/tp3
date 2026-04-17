using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GameState : NetworkBehaviour
{
    [SerializeField]
    private GameObject m_GameArea;

    [SerializeField]
    private float m_StunDuration = 3.0f;

    [SerializeField]
    private Vector2 m_GameSize;

    public Vector2 GameSize => m_GameSize;

    // Track the EXACT start ticks of stuns for accurate historical replays
    public NetworkList<int> GlobalStunStarts;

    // Clients predict their own stuns before the server confirms them
    private List<int> m_LocalPredictedStuns = new List<int>();

    private void Awake()
    {
        // NetworkList MUST be initialized in Awake
        GlobalStunStarts = new NetworkList<int>();
    }

    private void Start()
    {
        m_GameArea.transform.localScale = new Vector3(m_GameSize.x * 2, m_GameSize.y * 2, 1);
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
        NetworkManager.NetworkTickSystem.Tick += CleanUpOldStuns;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
            if (NetworkManager.NetworkTickSystem != null)
            {
                NetworkManager.NetworkTickSystem.Tick -= CleanUpOldStuns;
            }
        }
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (!IsServer)
        {
            SceneManager.LoadScene("StartupScene");
        }
    }

    // Evaluates stun correctly no matter if the queried tick is past, present, or future
    public bool IsStunnedAtTick(int queryTick)
    {
        int durationTicks = Mathf.CeilToInt(5 * NetworkManager.NetworkTickSystem.TickRate);

        // 1. Check Server Confirmed Stuns
        foreach (int startTick in GlobalStunStarts)
        {
            if (queryTick >= startTick && queryTick < startTick + durationTicks)
                return true;
        }

        // 2. Check Locally Predicted Stuns
        foreach (int startTick in m_LocalPredictedStuns)
        {
            if (queryTick >= startTick && queryTick < startTick + durationTicks)
                return true;
        }

        return false;
    }

    public void ApplyStun(int startTick)
    {
        if (IsServer)
        {
            if (!GlobalStunStarts.Contains(startTick))
                GlobalStunStarts.Add(startTick);
        }

        if (IsClient)
        {
            if (!m_LocalPredictedStuns.Contains(startTick))
                m_LocalPredictedStuns.Add(startTick);
        }
    }

    // Prevents memory leaks by clearing out stuns that are way in the past
    private void CleanUpOldStuns()
    {
        int currentTick = NetworkManager.ServerTime.Tick;
        int durationTicks = Mathf.CeilToInt(m_StunDuration * NetworkManager.NetworkTickSystem.TickRate);
        int historyBuffer = (int)(NetworkManager.NetworkTickSystem.TickRate * 3f); // Keep in history for 3 seconds past end time

        if (IsServer)
        {
            for (int i = GlobalStunStarts.Count - 1; i >= 0; i--)
            {
                if (currentTick > GlobalStunStarts[i] + durationTicks + historyBuffer)
                {
                    GlobalStunStarts.RemoveAt(i);
                }
            }
        }

        if (IsClient)
        {
            for (int i = m_LocalPredictedStuns.Count - 1; i >= 0; i--)
            {
                if (currentTick > m_LocalPredictedStuns[i] + durationTicks + historyBuffer)
                {
                    m_LocalPredictedStuns.RemoveAt(i);
                }
            }
        }
    }
}