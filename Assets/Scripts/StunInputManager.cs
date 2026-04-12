using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class StunInputManager : NetworkBehaviour
{
    [SerializeField]
    private GameState m_GameState;

    private void Update()
    {
        // Seuls les clients peuvent envoyer des inputs.
        if (IsClient)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                m_GameState.Stun(NetworkUtility.GetLocalTick());
                ActivateStunServerRpc();
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ActivateStunServerRpc()
    {
        m_GameState.Stun(NetworkUtility.GetLocalTick());
    }
}
