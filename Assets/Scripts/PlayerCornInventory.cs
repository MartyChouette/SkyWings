using Unity.Netcode;
using Unity;

public class PlayerCornInventory : NetworkBehaviour
{
    public NetworkVariable<int> CurrentCorn = new NetworkVariable<int>();

    public void ServerAddCorn(int amount)
    {
        if (!IsServer) return;
        CurrentCorn.Value += amount;
    }

    public bool ServerConsumeCorn(int amount)
    {
        if (!IsServer) return false;
        if (CurrentCorn.Value < amount) return false;
        CurrentCorn.Value -= amount;
        return true;
    }
}