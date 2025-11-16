using UnityEngine;

public class FlyingEnemySniper : NetworkEnemyBase
{
    protected  void TickAI()
    {
        // slower movement, longer range, bigger damage
        base.TickAI();
    }
}