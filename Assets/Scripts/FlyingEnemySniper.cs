using UnityEngine;

public class FlyingEnemySniper : NetworkEnemyBase
{
    protected override void TickAI()
    {
        // slower movement, longer range, bigger damage
        base.TickAI();
    }
}