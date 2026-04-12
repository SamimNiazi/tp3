using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
public struct StunStatus : INetworkSerializeByMemcpy
{
    public bool IsStunned;
    public int endTick;
}


public class GameState : NetworkBehaviour
{
    [SerializeField]
    private GameObject m_GameArea;

    [SerializeField]
    private float m_StunDuration = 1.0f;

    [SerializeField]
    private Vector2 m_GameSize;

    public Vector2 GameSize { get => m_GameSize; }

    public NetworkVariable<StunStatus> m_IsStunned = new NetworkVariable<StunStatus>();

    public StunStatus local_Stunned;

    public StunStatus IsStunned { get => IsServer ? m_IsStunned.Value : local_Stunned ; }

    private Coroutine m_StunCoroutine;

    private float m_CurrentRtt;

    public float CurrentRTT { get => m_CurrentRtt / 1000f; }

    public NetworkVariable<float> ServerTime = new NetworkVariable<float>();

    private void Start()
    {
        m_GameArea.transform.localScale = new Vector3(m_GameSize.x * 2, m_GameSize.y * 2, 1);
    }

    private void FixedUpdate()
    {
        if (IsSpawned)
        {
            m_CurrentRtt = NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        }

        if (IsSpawned && IsServer)
        {
            ServerTime.Value = Time.time;
        }
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
    }

    public override void OnNetworkDespawn()
    {
        NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (!IsServer)
        {
            // si on est un client, retourner au menu principal
            SceneManager.LoadScene("StartupScene");
        }
    }

    public void Stun(int currentTick)
    {
        var tickRate = NetworkManager.NetworkConfig.TickRate;
        int durationInTicks = Mathf.CeilToInt(m_StunDuration * tickRate);
        if (IsServer)
        {
            m_IsStunned.Value = new StunStatus
            {
                IsStunned = true,
                endTick = currentTick + durationInTicks
            };
            //Debug.Log(m_IsStunned.Value.endTick);
        }
        else if (IsClient)
        {
            local_Stunned = new StunStatus
            {
                IsStunned = true,
                endTick = currentTick + durationInTicks
            };
        }
    }
}
