using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class ChargerEnemy : NetworkEnemyBase
{
    [Header("Charge")]
    public float detectionRadius = 20f;
    public float windupTime = 0.6f;
    public float chargeSpeed = 20f;
    public float chargeDuration = 1.2f;
    public float chargeCooldown = 2.5f;

    [Header("Animation")]
    public Animator animator;

    enum ChargeState
    {
        Idle,
        Chasing,
        Windup,
        Charging,
        Recover
    }

    ChargeState _state = ChargeState.Idle;
    Transform _targetPlayer;
    float _stateTimer;
    Vector3 _chargeDir;

    void Reset()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (!IsServer) return;

        AcquireTargetIfNeeded();

        switch (_state)
        {
            case ChargeState.Idle:
                TickIdle();
                break;
            case ChargeState.Chasing:
                TickChase();
                break;
            case ChargeState.Windup:
                TickWindup();
                break;
            case ChargeState.Charging:
                TickCharge();
                break;
            case ChargeState.Recover:
                TickRecover();
                break;
        }

        UpdateAnim();
    }

    void AcquireTargetIfNeeded()
    {
        if (_targetPlayer != null) return;

        float bestDist = Mathf.Infinity;
        Transform best = null;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (!client.PlayerObject) continue;
            float d = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = client.PlayerObject.transform;
            }
        }

        if (best != null && bestDist <= detectionRadius)
        {
            _targetPlayer = best;
            _state = ChargeState.Chasing;
        }
    }

    void TickIdle()
    {
        // just wait for target
        if (_targetPlayer == null)
            AcquireTargetIfNeeded();
    }

    void TickChase()
    {
        if (_targetPlayer == null)
        {
            _state = ChargeState.Idle;
            return;
        }

        Vector3 flatTarget = new Vector3(_targetPlayer.position.x, transform.position.y, _targetPlayer.position.z);
        Vector3 dir = (flatTarget - transform.position);
        float dist = dir.magnitude;
        dir.Normalize();

        if (dist > detectionRadius * 1.5f)
        {
            // lost them
            _targetPlayer = null;
            _state = ChargeState.Idle;
            return;
        }

        // move toward player
        transform.position += dir * moveSpeed * Time.deltaTime;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);

        // ready to charge once close enough
        if (dist <= attackRange * 2f)
        {
            _state = ChargeState.Windup;
            _stateTimer = windupTime;
            _chargeDir = dir;
        }
    }

    void TickWindup()
    {
        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            _state = ChargeState.Charging;
            _stateTimer = chargeDuration;
        }
    }

    void TickCharge()
    {
        transform.position += _chargeDir * chargeSpeed * Time.deltaTime;
        if (_chargeDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(_chargeDir);

        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            _state = ChargeState.Recover;
            _stateTimer = chargeCooldown;
        }
    }

    void TickRecover()
    {
        _stateTimer -= Time.deltaTime;
        if (_stateTimer <= 0f)
        {
            _state = ChargeState.Idle;
            _targetPlayer = null;
        }
    }

    void UpdateAnim()
    {
        if (!animator) return;

        animator.SetBool("IsMoving",
            _state == ChargeState.Chasing || _state == ChargeState.Charging);
        animator.SetBool("IsCharging", _state == ChargeState.Charging);
    }
}
