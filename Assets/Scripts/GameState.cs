using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GameState : NetworkBehaviour
{
    [SerializeField] private GameObject m_GameArea;
    [SerializeField] private float m_StunDuration = 1.0f;
    [SerializeField] private Vector2 m_GameSize;

    public Vector2 GameSize => m_GameSize;

    public NetworkList<float> GlobalStunStarts;
    private List<float> m_LocalPredictedStuns = new List<float>();

    public NetworkVariable<float> ServerTime = new NetworkVariable<float>();

    public float CurrentRTT =>
        IsClient
            ? NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) / 1000f
            : 0f;

    private void Awake()
    {
        GlobalStunStarts = new NetworkList<float>();
    }

    private void Start()
    {
        if (m_GameArea != null)
            m_GameArea.transform.localScale = new Vector3(m_GameSize.x * 2, m_GameSize.y * 2, 1);
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;

        if (NetworkManager.NetworkTickSystem != null)
            NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;

            if (NetworkManager.NetworkTickSystem != null)
                NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
        }
    }

    private void OnNetworkTick()
    {
        if (IsServer)
        {
            ServerTime.Value = (float)NetworkManager.LocalTime.TimeAsFloat;
        }

        CleanUpOldStuns();
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (!IsServer)
            SceneManager.LoadScene("StartupScene");
    }

    public bool IsStunnedAtTime(float time)
    {
        foreach (float start in GlobalStunStarts)
        {
            if (time >= start && time <= start + m_StunDuration)
                return true;
        }

        if (IsClient)
        {
            foreach (float start in m_LocalPredictedStuns)
            {
                if (time >= start && time <= start + m_StunDuration)
                    return true;
            }
        }

        return false;
    }

    public void ApplyStun(float startTime)
    {
        if (IsServer && !GlobalStunStarts.Contains(startTime))
            GlobalStunStarts.Add(startTime);

        if (IsClient && !m_LocalPredictedStuns.Contains(startTime))
            m_LocalPredictedStuns.Add(startTime);
    }

    private void CleanUpOldStuns()
    {
        float currentTime = ServerTime.Value;
        float buffer = 3.0f;

        if (IsServer)
        {
            for (int i = GlobalStunStarts.Count - 1; i >= 0; i--)
            {
                if (currentTime > GlobalStunStarts[i] + m_StunDuration + buffer)
                    GlobalStunStarts.RemoveAt(i);
            }
        }

        if (IsClient)
        {
            for (int i = m_LocalPredictedStuns.Count - 1; i >= 0; i--)
            {
                if (currentTime > m_LocalPredictedStuns[i] + m_StunDuration + buffer)
                    m_LocalPredictedStuns.RemoveAt(i);
            }
        }
    }
}