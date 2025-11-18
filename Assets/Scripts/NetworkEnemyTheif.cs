using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class NetworkEnemyTheif : NetworkEnemyBase
{
    public Transform homePoint;
    [Tooltip("Lift or orb-bank this thief steals from.")]
    public NetworkBehaviour liftOrbBank;   // drag your lift script here

    public float detectionRadius = 25f;
    public float stopDistance = 2.5f;
    public int stealAmountPerTrip = 5;
    public float stealDuration = 1.5f;

    [Header("Animation")]
    public Animator animator;   // use with NetworkAnimator

    enum ThiefState
    {
        Idle,
        GoingToLift,
        Stealing,
        ReturningHome
    }

    ThiefState _state = ThiefState.Idle;
    float _stealTimer;

    void Reset()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (!IsServer) return;

        switch (_state)
        {
            case ThiefState.Idle:
                ThinkIdle();
                break;
            case ThiefState.GoingToLift:
                TickGoToTarget(liftOrbBank ? liftOrbBank.transform.position : transform.position);
                break;
            case ThiefState.Stealing:
                TickSteal();
                break;
            case ThiefState.ReturningHome:
                if (homePoint)
                    TickGoToTarget(homePoint.position, true);
                else
                    _state = ThiefState.Idle;
                break;
        }

        UpdateAnim();
    }

    void ThinkIdle()
    {
        if (!liftOrbBank) return;

        float dist = Vector3.Distance(transform.position, liftOrbBank.transform.position);
        if (dist < detectionRadius)
        {
            _state = ThiefState.GoingToLift;
        }
    }

    void TickGoToTarget(Vector3 target, bool endAtHome = false)
    {
        Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
        Vector3 dir = flatTarget - transform.position;
        float dist = dir.magnitude;

        if (dist <= stopDistance)
        {
            if (!endAtHome)
            {
                // Reached lift
                BeginSteal();
            }
            else
            {
                // Reached home – drop loot / vanish / idle
                _state = ThiefState.Idle;
            }
            return;
        }

        dir.Normalize();
        transform.position += dir * moveSpeed * Time.deltaTime;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }

    void BeginSteal()
    {
        _state = ThiefState.Stealing;
        _stealTimer = stealDuration;

        if (!liftOrbBank) return;

        int stolen = 0;

        // Try to call `StealOrbs` if it exists on the lift script
        var method = liftOrbBank.GetType().GetMethod("StealOrbs");
        if (method != null)
        {
            object result = method.Invoke(liftOrbBank, new object[] { stealAmountPerTrip });
            if (result is int value) stolen = value;
        }

        // Optional: store stolen amount in a NetworkVariable if you want
        // to show it visually, etc. For now we just "have loot".
    }

    void TickSteal()
    {
        _stealTimer -= Time.deltaTime;
        if (_stealTimer <= 0f)
        {
            _state = ThiefState.ReturningHome;
        }
    }

    void UpdateAnim()
    {
        if (!animator) return;

        bool moving =
            _state == ThiefState.GoingToLift ||
            _state == ThiefState.ReturningHome;

        animator.SetBool("IsMoving", moving);
        animator.SetBool("IsStealing", _state == ThiefState.Stealing);
    }
}
