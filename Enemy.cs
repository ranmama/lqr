using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 敌人状态枚举，用于描述敌人当前所处的人工智能行为状态。
/// </summary>
public enum EnemyState
{
    /// <summary>待机状态：敌人在原地停留，等待触发下一步行为。</summary>
    idle,
    /// <summary>巡逻状态：敌人在指定路径或范围内来回移动。</summary>
    patrol,
    /// <summary>追击状态：敌人发现目标后朝目标方向移动。</summary>
    pursuit,
    /// <summary>攻击状态：敌人对目标发起攻击。</summary>
    attack,
    /// <summary>跳扑攻击状态：敌人对中距离目标发起跳跃扑击。</summary>
    leapAttack,
    /// <summary>受击状态：敌人受到伤害后进入的硬直或反馈状态。</summary>
    gethit,
    /// <summary>死亡状态：敌人生命值耗尽，停止所有行为。</summary>
    dead,
    /// <summary>返回状态：离开出生地过久后返回出生地。</summary>
    returning,
    /// <summary>观察状态：到达目的地后在原地四处观察。</summary>
    observing,
}

/// <summary>
/// 敌人类：基于简单状态机控制敌人的行为切换与生命周期。
/// 每个状态都对应 Enter（进入）、Update（更新）和 Exit（退出）三个回调，
/// 便于在状态切换时分别执行一次性逻辑与每帧逻辑。
/// </summary>
public class Enemy : MonoBehaviour
{
    /// <summary>
    /// 当前敌人状态，默认进入待机状态。
    /// </summary>
    public EnemyState state = EnemyState.idle;

    [Header("生命值")]
    [Tooltip("最大生命值")]
    public float maxHealth = 100f;
    [Tooltip("当前生命值（运行时由脚本管理，Inspector 中的初始值仅在 Start 时生效）")]
    public float currentHealth = 100f;
    [Tooltip("受伤后的无敌时间（秒），防止连续受伤")]
    public float invincibilityDuration = 0.3f;
    [Tooltip("受击硬直持续时间（秒），期间无法行动")]
    public float getHitDuration = 0.3f;
    [Tooltip("死亡动画 Trigger 名称，需与 Animator Controller 中的 Trigger 参数名一致")]
    public string deathTrigger = "Death";
    [Tooltip("死亡后延迟销毁时间（秒），0 表示不自动销毁")]
    public float destroyAfterDeath = 2f;
    [Tooltip("死亡音效")]
    public AudioClip deathSound;
    [Tooltip("受击音效")]
    public AudioClip hitSound;
    [Tooltip("受击粒子特效列表。可添加多个特效，受击时同时播放。可引用场景中已存在的 ParticleSystem 直接播放；若引用预制体，会自动实例化并自动销毁。")]
    public ParticleSystem[] hitEffects;
    [Tooltip("受击特效的生成位置，留空则使用怪物自身位置")]
    public Transform hitEffectPoint;
    [Tooltip("受击特效预制体实例化后的存活时间（秒）")]
    public float hitEffectLifetime = 2f;

    [Header("动画")]
    [SerializeField] private Animator animator;
    private float idleTimer = 0f;
    private float invincibilityTimer;
    private bool isDead;
    private int deathTriggerHash;
    private float getHitTimer;
    private float destroyTimer;

    /// <summary>
    /// 生命值发生变化时触发。参数：当前生命值、最大生命值。
    /// </summary>
    public event System.Action<float, float> OnHealthChanged;

    /// <summary>
    /// 敌人死亡时触发。
    /// </summary>
    public event System.Action OnDeath;

    /// <summary>
    /// 当前生命值百分比（0-1）。
    /// </summary>
    public float HealthPercent => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    /// <summary>
    /// 敌人是否已死亡。
    /// </summary>
    public bool IsDead => isDead;

    [Header("巡逻")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField, Tooltip("以出生地为圆心的巡逻半径")] private float patrolRadius = 10f;
    [SerializeField, Tooltip("待机多少秒后再次巡逻")] private float idleWaitTime = 10f;
    [SerializeField, Tooltip("随机目标点采样半径，避免穿墙应设较小值")] private float sampleRadius = 3f;
    [SerializeField, Tooltip("随机采样最大尝试次数")] private int maxSampleAttempts = 10;
    [SerializeField, Tooltip("巡逻目标点与当前位置的最小距离，防止目标点过近")] private float minPatrolDistance = 5f;
    [SerializeField, Tooltip("墙体/障碍物所在的 Layer，用于物理同步时检测阻挡")] private LayerMask obstacleLayers = ~0;
    [SerializeField, Tooltip("每帧最大移动步长，防止高速时穿墙")] private float maxMoveStepPerFrame = 0.5f;

    [Header("到达目的地观察")]
    [SerializeField, Tooltip("每个观察方向停留的时长")] private float observeDurationPerAngle = 1f;
    [SerializeField, Tooltip("每次观察随机选取几个方向")] private int observeCount = 3;
    [SerializeField, Tooltip("每次随机朝向相对于初始朝向的最大角度（不能超过90度）")] private float observeRandomAngleRange = 90f;
    [SerializeField, Tooltip("转向观察方向的速度")] private float observeRotationSpeed = 120f;
    [SerializeField, Tooltip("返回出生地是否也执行观察")] private bool observeAfterReturning = false;
    private int currentObserveIndex = 0;
    private float observeTimer = 0f;
    private Quaternion observeStartRotation;
    private Quaternion observeTargetRotation;
    private bool observeRotationInProgress = false;
    private float observeBaseYRotation = 0f;
    /// <summary>本次观察实际生成的随机角度列表。</summary>
    private List<float> runtimeObserveAngles = new List<float>();

    [Header("追击玩家")]
    [SerializeField, Tooltip("发现玩家的检测半径")] private float detectRadius = 8f;
    [SerializeField, Tooltip("近距离强制检测半径（绕到身后/贴脸时也能发现）")] private float proximityDetectRadius = 3f;
    [SerializeField, Tooltip("返回途中重新发现玩家的检测半径（独立于巡逻检测，可单独调整）")] private float returnDetectRadius = 5f;
    [SerializeField, Tooltip("追击时的移动速度")] private float chaseSpeed = 5f;
    [SerializeField, Tooltip("丢失仇恨的距离，必须大于检测半径")] private float loseAggroRadius = 12f;
    [SerializeField, Tooltip("丢失仇恨后的记忆时间（防止绕身后/快速转向时立刻脱战）")] private float chaseMemoryTime = 2f;
    [SerializeField, Tooltip("追击时的转向速度")] private float chaseAngularSpeed = 180f;
    [SerializeField, Tooltip("追击时的加速度（越大转向越不容易绕弯）")] private float chaseAcceleration = 20f;
    [SerializeField, Tooltip("追击离开出生点的最大距离（拴绳），超过后放弃追击/攻击并返回出生点")] private float maxChaseDistanceFromSpawn = 15f;
    [SerializeField, Tooltip("攻击距离，进入该范围后切换到攻击状态")] private float attackRange = 2.5f;
    [SerializeField, Tooltip("攻击冷却时间（秒）")] private float attackCooldown = 3f;
    [SerializeField, Tooltip("攻击动画播放时长（秒），动画结束后自动回到待机/移动")] private float attackAnimationDuration = 1f;
    [SerializeField, Tooltip("攻击动画结束后的硬直时间（秒），期间不会切换状态或再次攻击")] private float postAttackDuration = 0.5f;

    [Header("跳扑攻击")]
    [SerializeField, Tooltip("跳扑攻击的最大触发距离（超出普通攻击范围、在此距离内可触发跳扑）")] private float leapAttackRange = 6f;
    [SerializeField, Tooltip("跳扑攻击冷却时间（秒）")] private float leapAttackCooldown = 30f;
    [SerializeField, Tooltip("跳扑动画移动时长（秒）")] private float leapAttackDuration = 0.8f;
    [SerializeField, Tooltip("跳扑移动速度")] private float leapAttackMoveSpeed = 12f;
    [SerializeField, Tooltip("跳扑落点与玩家的距离偏移（落点=玩家位置+朝向怪物的方向×偏移）")] private float leapAttackLandingOffset = 1.5f;
    [SerializeField, Tooltip("跳扑前方墙体检测距离，检测到墙体则取消跳扑")] private float leapAttackWallCheckDistance = 4f;
    [SerializeField, Tooltip("发现玩家后多久才能触发跳扑（秒），防止追击初始化的抽搐")] private float leapAttackTriggerDelay = 2f;

    [Header("动画事件")]
    [SerializeField, Tooltip("攻击盒子的预制体，由动画事件触发生成")]
    private GameObject attackBoxPrefab;
    [SerializeField, Tooltip("攻击盒子生成的挂点 Transform（场景中怪物子物体）")]
    private Transform attackBoxSpawnPoint;
    [SerializeField, Tooltip("攻击盒子存活时间（秒），超时自动销毁")]
    private float attackBoxLifetime = 0.5f;
    [SerializeField, Tooltip("攻击伤害值")]
    private float attackDamage = 10f;
    /// <summary>当前攻击盒子的 AttackTrigger 组件引用，供动画事件激活伤害。</summary>
    private AttackTrigger currentAttackTrigger;

    [SerializeField, Tooltip("手动指定玩家 Transform；留空则按标签查找")] private Transform targetPlayer;
    [SerializeField, Tooltip("玩家对象的标签")] private string playerTag = "Player";
    /// <summary>进入追击前缓存的 NavMeshAgent 速度，退出追击后恢复。</summary>
    private float cachedAgentSpeed;
    /// <summary>进入追击前缓存的 NavMeshAgent 转向速度，退出追击后恢复。</summary>
    private float cachedAgentAngularSpeed;
    /// <summary>进入追击前缓存的 NavMeshAgent 加速度，退出追击后恢复。</summary>
    private float cachedAgentAcceleration;
    /// <summary>进入追击前缓存的 NavMeshAgent 自动刹车设置，退出追击后恢复。</summary>
    private bool cachedAgentAutoBraking;
    /// <summary>进入追击前缓存的 NavMeshAgent 停止距离，退出追击后恢复。</summary>
    private float cachedAgentStoppingDistance;
    /// <summary>丢失目标后的累计记忆时间。</summary>
    private float chaseMemoryTimer = 0f;
    /// <summary>攻击冷却倒计时。</summary>
    private float attackCooldownTimer = 0f;
    /// <summary>当前攻击动画剩余播放时间。</summary>
    private float attackAnimationTimer = 0f;
    /// <summary>攻击动画结束后的硬直倒计时。</summary>
    private float postAttackTimer = 0f;
    /// <summary>Animator 中控制攻击状态的 Bool 参数哈希。</summary>
    private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
    /// <summary>Animator 中控制跳扑攻击状态的 Bool 参数哈希。</summary>
    private static readonly int IsLeapAttackingHash = Animator.StringToHash("IsLeapAttacking");
    /// <summary>跳扑攻击冷却倒计时。</summary>
    private float leapAttackCooldownTimer = 0f;
    /// <summary>跳扑攻击动画剩余时间。</summary>
    private float leapAttackTimer = 0f;
    /// <summary>跳扑攻击的目标落点。</summary>
    private Vector3 leapTargetPosition;
    /// <summary>最近一次进入追击状态的时间，用于防止状态反复抖动。</summary>
    private float pursuitStateEnterTime;
    /// <summary>状态最小持续时间，防止攻击/追击反复切换导致瞬移。</summary>
    private const float MinStateDuration = 0.5f;

    /// <summary>卡墙检测：上一帧追击时的位置，用于判断是否卡住。</summary>
    private Vector3 lastPursuitPosition;
    /// <summary>卡墙检测：累计在同一位置附近停留的时间。</summary>
    private float pursuitStuckTimer = 0f;
    /// <summary>卡墙检测：连续多少秒移动距离小于阈值则判定为卡墙。</summary>
    private const float PursuitStuckTimeLimit = 1.5f;
    /// <summary>卡墙检测：每秒移动距离低于此值视为卡住。</summary>
    private const float PursuitStuckSpeedThreshold = 0.3f;
    /// <summary>卡墙后重算路径时，在玩家位置附近采样的最大半径。</summary>
    private const float PursuitRepathSampleRadius = 5f;

    [Header("返回出生地")]
    [SerializeField, Tooltip("离开出生地超过该时间后触发返回")] private float maxAwayFromHomeTime = 60f;
    [SerializeField, Tooltip("距离出生地多远视为已离开")] private float homeRadius = 1.5f;
    /// <summary>累计离开出生地的时间。</summary>
    private float awayFromHomeTimer = 0f;
    /// <summary>最近一次进入返回状态的时间，用于防止返回↔追击反复切换。</summary>
    private float returningStateEnterTime;
    /// <summary>返回途中重新触发追击的距离系数：必须在拴绳范围的此比例内才能切追击，防止边界乒乓。</summary>
    private const float ReturnReenterPursuitRatio = 0.7f;
    /// <summary>是否处于待返回状态（巡逻完成后触发）。</summary>
    private bool pendingReturnHome = false;

    /// <summary>敌人出生地，作为巡逻圆心。</summary>
    private Vector3 spawnPosition;

    void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    /// <summary>
    /// 敌人移动完全由 NavMeshAgent 控制，这里空实现，彻底禁用攻击动画根运动带来的位移，
    /// 防止怪物穿过玩家或瞬移。
    /// </summary>
    private void OnAnimatorMove()
    {
    }

    /// <summary>
    /// Inspector 参数校验：确保观察随机角度范围合法。
    /// </summary>
    void OnValidate()
    {
        observeRandomAngleRange = Mathf.Clamp(observeRandomAngleRange, 0f, 90f);
        observeCount = Mathf.Max(1, observeCount);
        minPatrolDistance = Mathf.Max(0f, minPatrolDistance);

        detectRadius = Mathf.Max(0f, detectRadius);
        proximityDetectRadius = Mathf.Max(0f, proximityDetectRadius);
        returnDetectRadius = Mathf.Max(0f, returnDetectRadius);
        chaseMemoryTime = Mathf.Max(0f, chaseMemoryTime);
        chaseAngularSpeed = Mathf.Max(0f, chaseAngularSpeed);
        chaseAcceleration = Mathf.Max(0f, chaseAcceleration);
        maxChaseDistanceFromSpawn = Mathf.Max(1f, maxChaseDistanceFromSpawn);
        attackRange = Mathf.Max(0f, attackRange);
        attackCooldown = Mathf.Max(0f, attackCooldown);
        attackAnimationDuration = Mathf.Max(0.01f, attackAnimationDuration);
        postAttackDuration = Mathf.Max(0f, postAttackDuration);
        leapAttackRange = Mathf.Max(attackRange + 0.1f, leapAttackRange);
        leapAttackCooldown = Mathf.Max(0f, leapAttackCooldown);
        leapAttackDuration = Mathf.Max(0.01f, leapAttackDuration);
        leapAttackMoveSpeed = Mathf.Max(0.1f, leapAttackMoveSpeed);
        leapAttackLandingOffset = Mathf.Max(0f, leapAttackLandingOffset);
        leapAttackWallCheckDistance = Mathf.Max(0.1f, leapAttackWallCheckDistance);
        leapAttackTriggerDelay = Mathf.Max(0f, leapAttackTriggerDelay);
        loseAggroRadius = Mathf.Max(detectRadius + 0.1f, loseAggroRadius);

        // 生命值参数校验
        maxHealth = Mathf.Max(1f, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        invincibilityDuration = Mathf.Max(0f, invincibilityDuration);
        getHitDuration = Mathf.Max(0f, getHitDuration);
        destroyAfterDeath = Mathf.Max(0f, destroyAfterDeath);
    }

    /// <summary>
    /// 初始化：在脚本启动时调用一次，可用于初始化敌人数据或进入默认状态。
    /// </summary>
    void Start()
    {
        // 如果未在 Inspector 中指定，则自动获取组件
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // 禁用动画根运动，防止攻击动画自带位移导致穿过玩家或瞬移
        if (animator != null)
            animator.applyRootMotion = false;

        // 初始化死亡动画 Trigger 哈希与生命值
        deathTriggerHash = Animator.StringToHash(deathTrigger);
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        // 强制重置 NavMeshAgent，清除上一轮可能残留的路径和状态
        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            // 关闭自动位置同步，改为在 PatrolUpdate 中手动用物理检测后同步
            agent.updatePosition = false;
        }

        // 记录出生地，作为巡逻圆心
        spawnPosition = transform.position;

        // 重置离开出生地计时器
        awayFromHomeTimer = 0f;
        pendingReturnHome = false;

        // 如果怪物身上有 Rigidbody，设为运动学，避免物理与 NavMeshAgent 冲突导致瞬移/抖动
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        // 查找玩家目标
        FindPlayerTarget();

        // 始终保持与玩家的物理碰撞，让怪物在任何状态下（巡逻/追击/攻击）都正常阻挡玩家，
        // 不再根据状态动态切换碰撞，避免攻击时突然恢复碰撞导致推动玩家
        SetCollisionWithPlayer(false);
    }

    void OnEnable()
    {
        // 对象被启用/重新启用时再次清理 agent 状态，防止 Editor 重新播放时状态残留
        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.updatePosition = false;
        }
    }

    /// <summary>
    /// 每帧更新：根据当前状态调用对应状态的每帧更新逻辑。
    /// </summary>
    void Update()
    {
        // 根据当前状态分发到对应的状态更新函数
        if (state == EnemyState.idle) IdleUpdate();
        else if (state == EnemyState.patrol) PatrolUpdate();
        else if (state == EnemyState.pursuit) PursuitUpdate();
        else if (state == EnemyState.attack) AttackUpdate();
        else if (state == EnemyState.leapAttack) LeapAttackUpdate();
        else if (state == EnemyState.gethit) GetHitUpdate();
        else if (state == EnemyState.dead) DeadUpdate();
        else if (state == EnemyState.returning) ReturningUpdate();
        else if (state == EnemyState.observing) ObservingUpdate();

        // 每帧更新离开出生地计时器（所有状态都需要计算）
        UpdateAwayFromHomeTimer();

        // 全局冷却倒计时（跳扑攻击冷却独立于普通攻击，始终倒计时）
        if (leapAttackCooldownTimer > 0f)
            leapAttackCooldownTimer -= Time.deltaTime;

        // 无敌时间倒计时
        if (invincibilityTimer > 0f)
            invincibilityTimer -= Time.deltaTime;
    }

    /// <summary>
    /// 切换到新状态：先执行旧状态的退出逻辑，再更新当前状态，最后执行新状态的进入逻辑。
    /// </summary>
    /// <param name="newState">要切换到的目标状态。</param>
    public void ChangeToNewState(EnemyState newState)
    {
        // 受击状态锁定：受击硬直未结束前，禁止切换到任何其他状态（死亡除外），
        // 确保受击期间不能移动、不能攻击，只能等待硬直结束。
        if (state == EnemyState.gethit && getHitTimer > 0f &&
            newState != EnemyState.gethit && newState != EnemyState.dead)
        {
            return;
        }

        // 步骤1：执行当前状态的退出逻辑，用于清理旧状态的资源或行为
        if (state == EnemyState.idle) IdleExit();
        else if (state == EnemyState.patrol) PatrolExit();
        else if (state == EnemyState.pursuit) PursuitExit();
        else if (state == EnemyState.attack) AttackExit();
        else if (state == EnemyState.leapAttack) LeapAttackExit();
        else if (state == EnemyState.gethit) GetHitExit();
        else if (state == EnemyState.dead) DeadExit();
        else if (state == EnemyState.returning) ReturningExit();
        else if (state == EnemyState.observing) ObservingExit();

        // 步骤2：更新当前状态为目标状态
        state = newState;

        // 步骤3：执行新状态的进入逻辑，用于初始化新状态的数据或行为
        if (newState == EnemyState.idle) IdleEnter();
        else if (newState == EnemyState.patrol) PatrolEnter();
        else if (newState == EnemyState.pursuit) PursuitEnter();
        else if (newState == EnemyState.attack) AttackEnter();
        else if (newState == EnemyState.leapAttack) LeapAttackEnter();
        else if (newState == EnemyState.gethit) GetHitEnter();
        else if (newState == EnemyState.dead) DeadEnter();
        else if (newState == EnemyState.returning) ReturningEnter();
        else if (newState == EnemyState.observing) ObservingEnter();

    }

    /// <summary>
    /// 进入待机状态：可在此播放待机动画、重置计时器等。
    /// </summary>
    public void IdleEnter()
    {
        if (agent != null)
        {
            agent.updatePosition = false;
            agent.updateRotation = true;
        }
        animator.SetFloat("Speed", 0);
        idleTimer = 0;
    }

    /// <summary>
    /// 待机状态每帧更新：可在此检测是否切换为巡逻或追击状态。
    /// </summary>
    public void IdleUpdate()
    {
        // 如果已经触发返回出生地，优先执行返回，不再进入巡逻
        if (pendingReturnHome)
        {
            ChangeToNewState(EnemyState.returning);
            return;
        }

        // 检测到玩家则切换到追击状态
        if (IsPlayerInDetectRange())
        {
            ChangeToNewState(EnemyState.pursuit);
            return;
        }

        idleTimer += Time.deltaTime;
        if (idleTimer > idleWaitTime)
        {
            ChangeToNewState(EnemyState.patrol);
        }
    }

    /// <summary>
    /// 退出待机状态：可在此清理待机相关逻辑。
    /// </summary>
    public void IdleExit()
    {

    }

    /// <summary>
    /// 进入巡逻状态：可在此设置巡逻路径或目标点。
    /// </summary>
	public void PatrolEnter()
    {
        if (agent != null)
        {
            agent.updatePosition = false;
            agent.updateRotation = true;
        }
        animator.SetFloat("Speed", 1);
        // 进入巡逻时随机选一个目标，避免在原地等待
        SetRandomPatrolDestination();
    }

    /// <summary>
    /// 巡逻状态每帧更新：检测玩家是否在视野内，并根据 NavMeshAgent 状态决定是否切换回待机。
    /// </summary>
    public void PatrolUpdate()
    {
        // 如果已经触发返回出生地，优先执行返回，不再继续巡逻
        if (pendingReturnHome)
        {
            ChangeToNewState(EnemyState.returning);
            return;
        }

        // 检测到玩家则切换到追击状态
        if (IsPlayerInDetectRange())
        {
            ChangeToNewState(EnemyState.pursuit);
            return;
        }

        // 使用物理分段同步代替直接读取 agent.nextPosition，防止穿墙
        SyncPositionWithPhysics();

        if (!agent.isOnNavMesh)
        {
            agent.isStopped = true;
            ChangeToNewState(EnemyState.idle);
            return;
        }

        // 到达巡逻目标后切换回待机
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            ChangeToNewState(EnemyState.idle);
        }
    }

    /// <summary>
    /// 物理同步移动：将 agent.nextPosition 的移动量分段检测，防止穿墙。
    /// 在巡逻和返回状态下使用，追击状态由 NavMeshAgent 自行处理。
    /// </summary>
    private void SyncPositionWithPhysics()
    {
        if (agent == null || !agent.isOnNavMesh || agent.isStopped) return;
        if (agent.pathPending || !agent.hasPath) return;

        Vector3 agentPos = agent.nextPosition;
        Vector3 moveDir = agentPos - transform.position;
        moveDir.y = 0f;

        float remainingDistance = moveDir.magnitude;
        if (remainingDistance <= 0.001f) return;

        moveDir /= remainingDistance;
        Vector3 stepStart = transform.position;

        while (remainingDistance > 0.001f)
        {
            float stepDistance = Mathf.Min(remainingDistance, maxMoveStepPerFrame);
            Vector3 rayOrigin = stepStart + Vector3.up * 0.5f;

            if (Physics.Raycast(rayOrigin, moveDir, out RaycastHit hit, stepDistance + 0.1f, obstacleLayers))
            {
                // 命中障碍物，停在此位置并重新选目标
                transform.position = stepStart;
                agent.nextPosition = stepStart;
                // 根据当前状态决定重新寻路的目标
                if (state == EnemyState.patrol)
                {
                    SetRandomPatrolDestination();
                }
                else if (state == EnemyState.returning)
                {
                    SetReturnPathToSpawn();
                }
                return;
            }

            // 当前小段安全，移动一步
            stepStart += moveDir * stepDistance;
            remainingDistance -= stepDistance;
        }

        // 所有小段都安全，移动到最终位置
        transform.position = stepStart;
        agent.nextPosition = stepStart;
    }

    /// <summary>
    /// 退出巡逻状态：可在此停止移动或清理巡逻数据。
    /// </summary>
    public void PatrolExit()
    {
        // 停止当前寻路
        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    /// <summary>
    /// 更新离开出生地计时器。当怪物距离出生地超过 homeRadius 时累计时间，
    /// 超过 maxAwayFromHomeTime 后设置 pendingReturnHome 标志。
    /// 处于返回状态或已回到出生地范围内时重置计时器。
    /// </summary>
    private void UpdateAwayFromHomeTimer()
    {
        float distanceToHome = Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z),
                                                new Vector3(spawnPosition.x, 0f, spawnPosition.z));

        // 已回到出生地范围内，重置计时和待返回标志
        if (distanceToHome <= homeRadius)
        {
            awayFromHomeTimer = 0f;
            pendingReturnHome = false;
            return;
        }

        // 正在返回途中，不继续累计时间，但也不重置
        if (state == EnemyState.returning) return;

        // 离开范围且不在返回途中，累计时间
        awayFromHomeTimer += Time.deltaTime;

        if (awayFromHomeTimer >= maxAwayFromHomeTime && !pendingReturnHome)
        {
            pendingReturnHome = true;
            Debug.Log($"[Home] 怪物离开出生地超过 {maxAwayFromHomeTime} 秒，标记为待返回。");
        }
    }

    /// <summary>
    /// 查找玩家目标：优先使用 Inspector 中指定的 targetPlayer，否则按标签查找。
    /// 如果 Inspector 中误填成了敌人自己，也会自动重新查找。
    /// </summary>
    private void FindPlayerTarget()
    {
        // 防止把敌人自己或其子物体设为追击目标
        if (targetPlayer != null && (targetPlayer == transform || targetPlayer.IsChildOf(transform)))
        {
            Debug.LogWarning($"[Enemy] targetPlayer 被设置成了敌人自己或其子物体，将尝试按标签 {playerTag} 重新查找玩家。");
            targetPlayer = null;
        }

        if (targetPlayer != null) return;

        if (!string.IsNullOrEmpty(playerTag))
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag(playerTag);
            if (playerGO != null)
            {
                targetPlayer = playerGO.transform;
                Debug.Log($"[Enemy] 通过标签 {playerTag} 找到玩家：{targetPlayer.name}");
            }
            else
            {
                Debug.LogWarning($"[Enemy] 未找到标签为 {playerTag} 的玩家对象。");
            }
        }
    }

    /// <summary>
    /// 设置怪物自身与玩家所有碰撞体之间的物理碰撞是否忽略。
    /// </summary>
    /// <param name="ignore">true 为忽略碰撞，false 为恢复碰撞。</param>
    private void SetCollisionWithPlayer(bool ignore)
    {
        if (targetPlayer == null) return;

        Collider enemyCollider = GetComponent<Collider>();
        if (enemyCollider == null) return;

        Collider[] playerColliders = targetPlayer.GetComponentsInChildren<Collider>();
        foreach (var playerCollider in playerColliders)
        {
            if (playerCollider == null) continue;
            Physics.IgnoreCollision(enemyCollider, playerCollider, ignore);
        }
    }

    /// <summary>
    /// 计算与玩家的水平距离（忽略 Y 轴差异）。
    /// </summary>
    private float GetHorizontalDistanceToPlayer()
    {
        if (targetPlayer == null) return float.MaxValue;
        Vector3 a = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 b = new Vector3(targetPlayer.position.x, 0f, targetPlayer.position.z);
        return Vector3.Distance(a, b);
    }

    /// <summary>
    /// 计算与出生点的水平距离（忽略 Y 轴差异），用于"拴绳"限制追击范围。
    /// </summary>
    private float GetHorizontalDistanceToSpawn()
    {
        Vector3 a = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 b = new Vector3(spawnPosition.x, 0f, spawnPosition.z);
        return Vector3.Distance(a, b);
    }

    /// <summary>
    /// 检测玩家是否在检测范围内。
    /// </summary>
    private bool IsPlayerInDetectRange()
    {
        FindPlayerTarget();
        if (targetPlayer == null || targetPlayer == transform || targetPlayer.IsChildOf(transform) || !targetPlayer.gameObject.activeInHierarchy) return false;
        float distanceToPlayer = GetHorizontalDistanceToPlayer();
        // 普通检测半径 或 近距离强制检测半径（贴脸/绕后也能发现）
        return distanceToPlayer <= detectRadius || distanceToPlayer <= proximityDetectRadius;
    }

    /// <summary>
    /// 返回途中专用的玩家检测：使用独立的 returnDetectRadius，与巡逻/追击检测半径分开，
    /// 避免返回途中因为检测半径过大而被远距离玩家反复拉回追击。
    /// </summary>
    private bool IsPlayerInReturnDetectRange()
    {
        FindPlayerTarget();
        if (targetPlayer == null || targetPlayer == transform || targetPlayer.IsChildOf(transform) || !targetPlayer.gameObject.activeInHierarchy) return false;
        float distanceToPlayer = GetHorizontalDistanceToPlayer();
        return distanceToPlayer <= returnDetectRadius || distanceToPlayer <= proximityDetectRadius;
    }

    /// <summary>
    /// 攻击距离检测系统：判断玩家是否在攻击范围内。
    /// 进入攻击使用严格判断（attackRange），离开攻击使用缓冲判断（attackRange * 1.1f），
    /// 防止边界浮点抖动导致攻击↔追击反复切换。
    /// </summary>
    /// <param name="useBuffer">true 使用离开缓冲（更宽松），false 使用严格进入判断。</param>
    private bool IsPlayerInAttackRange(bool useBuffer = false)
    {
        if (targetPlayer == null || targetPlayer == transform || targetPlayer.IsChildOf(transform) || !targetPlayer.gameObject.activeInHierarchy)
            return false;
        float distanceToPlayer = GetHorizontalDistanceToPlayer();
        float threshold = useBuffer ? attackRange * 1.1f : attackRange;
        return distanceToPlayer <= threshold;
    }

    /// <summary>
    /// 进入返回状态：设置目标为出生地并播放奔跑动画。
    /// </summary>
    public void ReturningEnter()
    {
        animator.SetFloat("Speed", 1);
        pendingReturnHome = false;
        awayFromHomeTimer = 0f;
        returningStateEnterTime = Time.time;

        if (agent != null)
        {
            // 先 Warp 对齐内部位置到当前 Transform，防止追击残留的 nextPosition 导致额外前移
            agent.Warp(transform.position);
            agent.updatePosition = false;
            agent.updateRotation = true;
            agent.isStopped = false;
            agent.velocity = Vector3.zero;
        }
        SetReturnPathToSpawn();
    }

    /// <summary>
    /// 设置返回出生地的寻路路径；路径不完整时清除路径。
    /// </summary>
    private void SetReturnPathToSpawn()
    {
        if (agent == null) return;
        NavMeshPath path = new NavMeshPath();
        if (agent.CalculatePath(spawnPosition, path) && path.status == NavMeshPathStatus.PathComplete)
        {
            agent.SetPath(path);
        }
        else
        {
            agent.ResetPath();
        }
    }

    /// <summary>
    /// 返回状态每帧更新：向出生地移动，到达后切回待机。
    /// </summary>
    public void ReturningUpdate()
    {
        // 复用物理同步，防止返回途中穿墙
        SyncPositionWithPhysics();

        if (!agent.isOnNavMesh)
        {
            agent.isStopped = true;
            ChangeToNewState(EnemyState.idle);
            return;
        }

        // 返回途中重新触发追击的条件（迟滞判定）：
        // 1. 必须在拴绳范围的 70% 以内（不是边界），防止追击↔返回乒乓切换
        // 2. 必须检测到玩家
        // 3. 返回状态已持续超过最小持续时间，防止刚进入返回就切追击
        if (GetHorizontalDistanceToSpawn() <= maxChaseDistanceFromSpawn * ReturnReenterPursuitRatio
            && IsPlayerInReturnDetectRange()
            && Time.time - returningStateEnterTime >= MinStateDuration)
        {
            ChangeToNewState(EnemyState.pursuit);
            return;
        }

        // 到达出生地附近，切回待机或进入观察状态
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (observeAfterReturning)
                ChangeToNewState(EnemyState.observing);
            else
                ChangeToNewState(EnemyState.idle);
        }
    }

    /// <summary>
    /// 退出返回状态：清理返回相关数据。
    /// </summary>
    public void ReturningExit()
    {
        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    /// <summary>
    /// 进入观察状态：停止移动，准备按角度列表依次观察四周。
    /// </summary>
    public void ObservingEnter()
    {
        animator.SetFloat("Speed", 0);
        currentObserveIndex = 0;
        observeTimer = 0f;
        observeRotationInProgress = false;
        observeBaseYRotation = transform.eulerAngles.y;

        // 生成本次观察的随机角度列表，范围限制在 [-observeRandomAngleRange, observeRandomAngleRange]
        runtimeObserveAngles.Clear();
        for (int i = 0; i < observeCount; i++)
        {
            runtimeObserveAngles.Add(Random.Range(-observeRandomAngleRange, observeRandomAngleRange));
        }

        // 确保 agent 停止，并关闭自动同步，改由观察状态手动转向
        if (agent != null)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    /// <summary>
    /// 观察状态每帧更新：依次转向各个观察方向，每个方向停留指定时长。
    /// </summary>
    public void ObservingUpdate()
    {
        // 观察到玩家时中断观察并追击
        if (IsPlayerInDetectRange())
        {
            ChangeToNewState(EnemyState.pursuit);
            return;
        }

        if (currentObserveIndex >= runtimeObserveAngles.Count)
        {
            // 所有方向观察完毕，切回待机
            ChangeToNewState(EnemyState.idle);
            return;
        }

        float targetAngle = runtimeObserveAngles[currentObserveIndex];

        if (!observeRotationInProgress)
        {
            // 开始转向新的观察方向，所有角度都相对于进入观察状态时的朝向
            observeStartRotation = transform.rotation;
            observeTargetRotation = Quaternion.Euler(0f, observeBaseYRotation + targetAngle, 0f);
            observeRotationInProgress = true;
            observeTimer = 0f;
        }

        // 先完成转向
        if (Quaternion.Angle(transform.rotation, observeTargetRotation) > 1f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, observeTargetRotation, observeRotationSpeed * Time.deltaTime);
            return;
        }

        // 转向完成，开始计时停留
        observeTimer += Time.deltaTime;
        if (observeTimer >= observeDurationPerAngle)
        {
            currentObserveIndex++;
            observeRotationInProgress = false;
        }
    }

    /// <summary>
    /// 退出观察状态：清理观察相关数据。
    /// </summary>
    public void ObservingExit()
    {
        observeRotationInProgress = false;
        currentObserveIndex = 0;
        observeTimer = 0f;
        runtimeObserveAngles.Clear();
    }

    /// <summary>
    /// 在出生地为圆心、patrolRadius 为半径的圆内随机选取一个可达点，
    /// 并设置为 NavMeshAgent 的下一个目标点。
    /// 通过小范围采样和路径完整性检查，避免目标点落在墙后导致穿墙。
    /// 使用 SetPath 而非 SetDestination，确保验证路径与实际行走路径一致。
    /// </summary>
    private void SetRandomPatrolDestination()
    {
        if (agent == null) return;

        agent.isStopped = false;

        for (int i = 0; i < maxSampleAttempts; i++)
        {
            // 在巡逻圆内随机一个方向与距离
            Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
            Vector3 target = spawnPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);

            // 小范围采样，防止大半径采样到墙另一侧的可行走区域
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                // 目标点离当前位置太近，跳过重新随机，防止只走几步就停下
                float distanceToTarget = Vector3.Distance(transform.position, hit.position);
                if (distanceToTarget < minPatrolDistance)
                    continue;

                // 检查路径是否完整，避免目标点虽然在 NavMesh 上但无法到达
                NavMeshPath path = new NavMeshPath();
                if (agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    agent.SetPath(path);
                    return;
                }
            }
        }

        // 多次尝试都失败，则回到出生地，避免设到非法位置
        NavMeshPath fallbackPath = new NavMeshPath();
        if (agent.CalculatePath(spawnPosition, fallbackPath) && fallbackPath.status == NavMeshPathStatus.PathComplete)
        {
            agent.SetPath(fallbackPath);
        }
        else
        {
            agent.ResetPath();
        }
    }

    /// <summary>
    /// 进入追击状态：锁定玩家目标、切换到追击速度并设置寻路目标。
    /// </summary>
    public void PursuitEnter()
    {
        FindPlayerTarget();

        // 不在 NavMesh 上时先退回待机，避免 Warp/SetDestination 触发瞬移到玩家身上
        if (agent != null && !agent.isOnNavMesh)
        {
            Debug.LogWarning($"[Enemy] 追击时不在 NavMesh 上，退回待机以避免瞬移。pos={transform.position}");
            ChangeToNewState(EnemyState.idle);
            return;
        }

        // 攻击距离守卫：如果进入追击时玩家仍在攻击范围内，不启动追击，直接切回攻击，
        // 防止攻击→追击→攻击的短暂状态切换导致 NavMeshAgent 推动玩家
        if (IsPlayerInAttackRange(useBuffer: true))
        {
            ChangeToNewState(EnemyState.attack);
            return;
        }

        // 进入追击时玩家是否已经在攻击范围内（严格判断）：在范围内则无需奔跑，直接停下等待冷却后攻击。
        bool alreadyInRange = IsPlayerInAttackRange(useBuffer: false);

        if (agent != null)
        {
            cachedAgentSpeed = agent.speed;
            cachedAgentAngularSpeed = agent.angularSpeed;
            cachedAgentAcceleration = agent.acceleration;
            cachedAgentAutoBraking = agent.autoBraking;
            cachedAgentStoppingDistance = agent.stoppingDistance;

            agent.speed = chaseSpeed;
            agent.angularSpeed = chaseAngularSpeed;
            agent.acceleration = chaseAcceleration;
            // 开启自动刹车，让怪物在接近目标时自然减速
            agent.autoBraking = true;
            // 在攻击范围边缘停下，避免走到玩家脚下/另一侧造成"瞬移/穿过玩家"
            agent.stoppingDistance = attackRange * 0.7f;
            // 先把 Agent 内部位置同步到当前 transform，再开启自动位置同步，
            // 防止 nextPosition 残留导致切回自动同步时瞬移。
            agent.Warp(transform.position);
            agent.updatePosition = true;
            // 关闭自动旋转，改由代码平滑转向，避免玩家转向时出现瞬移/抽搐
            agent.updateRotation = false;

            if (alreadyInRange)
            {
                // 已在攻击范围内：原地停下、不设寻路目标
                agent.isStopped = true;
                agent.ResetPath();
            }
            else
            {
                agent.isStopped = false;

                if (targetPlayer != null)
                {
                    Vector3 targetPos = targetPlayer.position;
                    // 如果玩家位置不在 NavMesh 上（例如跳跃中），在附近采样一个有效点，防止寻路失败
                    if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                        targetPos = hit.position;
                    agent.SetDestination(targetPos);
                }
            }
        }

        if (animator != null)
        {
            // 整个 AI 中怪物的移动由 NavMeshAgent 控制，动画不驱动根运动，避免与寻路/攻击状态冲突导致瞬移或穿人
            animator.applyRootMotion = false;
            // 已在范围内则保持静止；真正追击时才根据冷却决定是否奔跑
            animator.SetFloat("Speed", alreadyInRange ? 0f : (attackCooldownTimer > 0f ? 0f : 1f));
        }

        chaseMemoryTimer = 0f;
        attackAnimationTimer = 0f;
        postAttackTimer = 0f;
        pursuitStateEnterTime = Time.time;
        lastPursuitPosition = transform.position;
        pursuitStuckTimer = 0f;
    }

    /// <summary>
    /// 追击状态每帧更新：持续朝玩家移动，靠近后停止移动并面朝玩家，
    /// 玩家脱离丢失仇恨距离后回到待机状态。
    /// </summary>
    public void PursuitUpdate()
    {
        if (agent == null || !agent.isOnNavMesh)
        {
            ChangeToNewState(EnemyState.idle);
            return;
        }

        // 拴绳或超时：追击离开出生点超过最大距离，或离开出生地过久，放弃追击返回出生点
        if (GetHorizontalDistanceToSpawn() > maxChaseDistanceFromSpawn || pendingReturnHome)
        {
            ChangeToNewState(EnemyState.returning);
            return;
        }

        FindPlayerTarget();
        if (targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy)
        {
            ChangeToNewState(EnemyState.idle);
            return;
        }

        float distanceToPlayer = GetHorizontalDistanceToPlayer();

        // 丢失仇恨后进入记忆倒计时，防止玩家绕身后/快速转向时立刻脱战
        if (distanceToPlayer > loseAggroRadius)
        {
            chaseMemoryTimer += Time.deltaTime;
            if (chaseMemoryTimer >= chaseMemoryTime)
            {
                ChangeToNewState(EnemyState.idle);
                return;
            }
        }
        else
        {
            chaseMemoryTimer = 0f;
        }

        // 跳扑攻击触发检测：玩家在普通攻击范围外、跳扑范围内，冷却就绪，已过触发延迟，前方无墙体阻挡
        if (!IsPlayerInAttackRange(useBuffer: false)
            && distanceToPlayer <= leapAttackRange
            && distanceToPlayer <= detectRadius
            && leapAttackCooldownTimer <= 0f
            && Time.time - pursuitStateEnterTime >= leapAttackTriggerDelay
            && !IsWallBlockingLeap())
        {
            ChangeToNewState(EnemyState.leapAttack);
            return;
        }

        // 玩家在攻击范围内：不奔跑，原地停下并面朝玩家；冷却结束后直接攻击。
        // 无论刚进入追击还是攻击后的冷却期，只要在范围内就不再奔跑。
        if (IsPlayerInAttackRange(useBuffer: false))
        {
            if (!agent.isStopped)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            FaceTargetPlayer();
            if (animator != null)
                animator.SetFloat("Speed", 0);

            // 冷却结束且距离上次进入追击已超过最小持续时间，才切换到攻击状态，避免攻击/追击在边界反复切换导致瞬移
            if (attackCooldownTimer <= 0f &&
                Time.time - pursuitStateEnterTime >= MinStateDuration)
            {
                ChangeToNewState(EnemyState.attack);
            }
            return;
        }

        // 如果玩家位置不在 NavMesh 上，在附近采样一个有效点，避免寻路失败或路径突变
        Vector3 rawTargetPos = targetPlayer.position;
        if (!NavMesh.SamplePosition(rawTargetPos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            // 玩家可能在跳跃/楼梯侧面，2f 采样失败，扩大搜索半径再试
            if (!NavMesh.SamplePosition(rawTargetPos, out hit, PursuitRepathSampleRadius, NavMesh.AllAreas))
            {
                // 玩家彻底不在 NavMesh 附近，保持上一帧目标不变，等待玩家回到可行走区域
                rawTargetPos = agent.destination;
            }
            else
            {
                rawTargetPos = hit.position;
            }
        }
        else
        {
            rawTargetPos = hit.position;
        }

        if (agent.isStopped)
        {
            agent.isStopped = false;
            agent.SetDestination(rawTargetPos);
        }
        else
        {
            // 避免每帧重置路径：只在目标移动足够距离或没有有效路径时才重新设目标
            // 距离越近阈值越小，让玩家贴脸绕身时目标更新更及时，减少绕弯
            Vector3 agentDestFlat = new Vector3(agent.destination.x, 0f, agent.destination.z);
            Vector3 targetFlat = new Vector3(rawTargetPos.x, 0f, rawTargetPos.z);
            float destDelta = Vector3.Distance(agentDestFlat, targetFlat);
            float destUpdateThreshold = Mathf.Clamp(distanceToPlayer * 0.15f, 0.1f, 0.5f);
            if (!agent.pathPending && (!agent.hasPath || destDelta > destUpdateThreshold))
            {
                agent.SetDestination(rawTargetPos);
            }
        }

        // 手动平滑转向玩家，避免 NavMeshAgent 自动转向导致的抽搐/瞬移
        FaceTargetPlayer();

        animator.SetFloat("Speed", 1);

        // 追击时启用 agent.updatePosition，由 NavMeshAgent 自身处理移动与避障，
        // 不再调用用于巡逻的手动物理同步，避免路径被反复重置。

        // 卡墙检测：如果 Agent 有路径且未停止，但实际移动速度极低，判定为卡在楼梯/墙角等复杂几何体上
        if (!agent.isStopped && agent.hasPath && !agent.pathPending)
        {
            float moveDelta = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(lastPursuitPosition.x, 0f, lastPursuitPosition.z));
            float moveSpeed = moveDelta / Time.deltaTime;

            if (moveSpeed < PursuitStuckSpeedThreshold)
            {
                pursuitStuckTimer += Time.deltaTime;
                if (pursuitStuckTimer >= PursuitStuckTimeLimit)
                {
                    // 判定卡墙：用更大半径在玩家位置附近重新采样 NavMesh 有效点，
                    // 避免上一轮路径穿过楼梯侧面等不可行走区域
                    Vector3 repathTarget = rawTargetPos;
                    if (NavMesh.SamplePosition(rawTargetPos, out NavMeshHit repathHit, PursuitRepathSampleRadius, NavMesh.AllAreas))
                    {
                        repathTarget = repathHit.position;
                        // 验证新路径完整性
                        NavMeshPath repath = new NavMeshPath();
                        if (agent.CalculatePath(repathTarget, repath) && repath.status == NavMeshPathStatus.PathComplete)
                        {
                            agent.ResetPath();
                            agent.SetPath(repath);
                            Debug.Log($"[Enemy] 追击卡墙检测触发，已重算路径。旧目标={rawTargetPos}，新目标={repathTarget}");
                        }
                    }
                    pursuitStuckTimer = 0f;
                }
            }
            else
            {
                pursuitStuckTimer = 0f;
            }
        }
        lastPursuitPosition = transform.position;
    }

    /// <summary>
    /// 退出追击状态：停止寻路并恢复原始移动速度，同时关闭自动位置同步。
    /// </summary>
    public void PursuitExit()
    {
        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.updatePosition = false;
            agent.speed = cachedAgentSpeed;
            agent.angularSpeed = cachedAgentAngularSpeed;
            agent.acceleration = cachedAgentAcceleration;
            agent.autoBraking = cachedAgentAutoBraking;
            agent.stoppingDistance = cachedAgentStoppingDistance;
        }

        chaseMemoryTimer = 0f;
        if (animator != null)
            animator.SetFloat("Speed", 0);
    }

    /// <summary>
    /// 进入攻击状态：立即停止移动并立刻发动一次攻击。
    /// 修复：进入攻击时重置冷却计时器，防止上一次冷却残留导致连击过快。
    /// </summary>
    public void AttackEnter()
    {
        FindPlayerTarget();
        if (targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy)
        {
            ChangeToNewState(EnemyState.idle);
            return;
        }

        if (agent != null)
        {
            agent.isStopped = true;
            // 攻击期间完全冻结 NavMeshAgent：关闭自动位置/旋转同步、清空速度、清除残留路径，
            // 避免 NavMeshAgent 在攻击动画播放期间推动敌人，导致穿过玩家或瞬移到玩家另一侧。
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.velocity = Vector3.zero;
            agent.ResetPath();
        }

        if (animator != null)
        {
            // 攻击期间禁用根运动，完全由代码控制位置，避免攻击动画自带位移把怪物推过玩家或瞬移到奇怪位置
            animator.applyRootMotion = false;
            if (animator.applyRootMotion)
                Debug.LogWarning("[Enemy] applyRootMotion 意外为 true，攻击动画根运动可能仍在推动怪物。请检查 Animator 组件与 Controller 设置。", this);
            animator.SetBool(IsAttackingHash, true);
            animator.SetFloat("Speed", 0);
        }

        // 进入攻击时先面向玩家，让攻击朝向正确
        FaceTargetPlayer();

        attackAnimationTimer = attackAnimationDuration;
        postAttackTimer = 0f;
        // 修复：每次进入攻击时立即启动冷却，防止上一次攻击冷却残留导致连续攻击
        attackCooldownTimer = attackCooldown;

        // 攻击盒子由动画事件驱动生成，不再在代码中直接调用
        // 在攻击动画的关键帧上添加 Animation Event，调用 SpawnAttackBox() 生成盒子
        // 再在后续帧添加 Animation Event，调用 ActivateAttackBox() 激活伤害
        Debug.Log("[Enemy] AttackEnter 触发，等待动画事件驱动攻击盒子");
    }

    /// <summary>
    /// 攻击状态每帧更新：当前攻击动画播放期间不退出，动画完整结束并经过硬直后再回到追击。
    /// 修复：冷却计时器由 AttackUpdate 自己管理，不再依赖全局 Update 倒计时，避免动画期间冷却偷跑。
    /// </summary>
    public void AttackUpdate()
    {
        // 拴绳或超时：攻击期间离开出生点超过最大距离，或离开出生地过久，放弃攻击返回出生点
        if (GetHorizontalDistanceToSpawn() > maxChaseDistanceFromSpawn || pendingReturnHome)
        {
            ChangeToNewState(EnemyState.returning);
            return;
        }

        FindPlayerTarget();
        if (targetPlayer == null || targetPlayer == transform || targetPlayer.IsChildOf(transform) || !targetPlayer.gameObject.activeInHierarchy)
        {
            ChangeToNewState(EnemyState.idle);
            return;
        }

        // 当前攻击动画播放计时
        if (attackAnimationTimer > 0f)
        {
            attackAnimationTimer -= Time.deltaTime;

            // 攻击期间同步 NavMeshAgent 内部位置，避免攻击动画/物理导致的位置偏移在切回追击时引发瞬移
            if (agent != null)
                agent.nextPosition = transform.position;

            if (attackAnimationTimer <= 0f)
            {
                // 动画结束进入硬直/后摇，同时启动冷却，防止连击过快
                postAttackTimer = postAttackDuration;
                attackCooldownTimer = attackCooldown;
                if (animator != null)
                    animator.SetBool(IsAttackingHash, false);
            }

            if (animator != null)
                animator.SetFloat("Speed", 0);
            return;
        }

        // 后摇期间不切换状态、不再次攻击，只保持面朝玩家
        if (postAttackTimer > 0f)
        {
            postAttackTimer -= Time.deltaTime;
            // 后摇期间同步倒计时冷却
            if (attackCooldownTimer > 0f)
                attackCooldownTimer -= Time.deltaTime;
            FaceTargetPlayer();
            if (animator != null)
                animator.SetFloat("Speed", 0);
            return;
        }

        // 后摇结束但冷却未结束：继续倒计时冷却，等待冷却完成后才能再次攻击
        if (attackCooldownTimer > 0f)
        {
            attackCooldownTimer -= Time.deltaTime;
            // 冷却等待期间同步 agent 内部位置，防止切追击时 nextPosition 残留导致瞬移
            if (agent != null)
                agent.nextPosition = transform.position;
            FaceTargetPlayer();
            if (animator != null)
                animator.SetFloat("Speed", 0);
            return;
        }

        // 动画、硬直与冷却都已结束：使用攻击距离检测系统判断玩家是否还在攻击范围内
        // 离开攻击范围使用缓冲（attackRange * 1.1f），防止边界浮点抖动导致误切追击
        if (IsPlayerInAttackRange(useBuffer: true))
        {
            // 冷却等待期间同步 agent 内部位置，防止切追击时 nextPosition 残留导致瞬移
            if (agent != null)
                agent.nextPosition = transform.position;

            // 玩家在攻击范围内且冷却结束：发起新一轮攻击
            if (animator != null)
                animator.SetBool(IsAttackingHash, true);
            FaceTargetPlayer();
            attackAnimationTimer = attackAnimationDuration;
            postAttackTimer = 0f;
            attackCooldownTimer = attackCooldown;
            return;
        }
        else
        {
            // 玩家确实离开了攻击范围（含缓冲），切换到追击状态
            ChangeToNewState(EnemyState.pursuit);
        }
    }

    /// <summary>
    /// 退出攻击状态：清理攻击状态与冷却时间。
    /// </summary>
    public void AttackExit()
    {
        attackAnimationTimer = 0f;
        postAttackTimer = 0f;

        if (animator != null)
        {
            animator.SetBool(IsAttackingHash, false);
        }

        // 退出攻击时启动冷却，防止立刻再次攻击
        attackCooldownTimer = attackCooldown;
    }

    /// <summary>
    /// 检测怪物前方是否有墙体阻挡跳扑路径。
    /// 从怪物位置向玩家方向发射射线，检测距离为 leapAttackWallCheckDistance。
    /// </summary>
    private bool IsWallBlockingLeap()
    {
        if (targetPlayer == null) return true;

        Vector3 direction = (targetPlayer.position - transform.position).normalized;
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        float checkDistance = Mathf.Min(leapAttackWallCheckDistance, GetHorizontalDistanceToPlayer());

        if (Physics.Raycast(origin, direction, out RaycastHit hit, checkDistance, obstacleLayers))
        {
            // 命中玩家或玩家子物体不算墙体
            if (hit.collider.transform == targetPlayer || hit.collider.transform.IsChildOf(targetPlayer))
                return false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 进入跳扑攻击状态：计算落点、停止 Agent、播放跳扑动画。
    /// </summary>
    public void LeapAttackEnter()
    {
        FindPlayerTarget();
        if (targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy)
        {
            ChangeToNewState(EnemyState.idle);
            return;
        }

        // 计算落点：玩家位置 + 朝向怪物的方向 × 偏移量，让怪物落在玩家面前而非身上
        Vector3 dirToEnemy = (transform.position - targetPlayer.position).normalized;
        dirToEnemy.y = 0f;
        if (dirToEnemy.sqrMagnitude < 0.0001f)
            dirToEnemy = -targetPlayer.forward;
        leapTargetPosition = targetPlayer.position + dirToEnemy * leapAttackLandingOffset;

        // 确保落点在 NavMesh 上
        if (NavMesh.SamplePosition(leapTargetPosition, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
            leapTargetPosition = navHit.position;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.velocity = Vector3.zero;
            agent.ResetPath();
        }

        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.SetBool(IsLeapAttackingHash, true);
            animator.SetFloat("Speed", 0);
        }

        // 跳扑前先面朝落点方向
        Vector3 faceDir = (leapTargetPosition - transform.position).normalized;
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(faceDir);

        leapAttackTimer = leapAttackDuration;
    }

    /// <summary>
    /// 跳扑攻击状态每帧更新：向落点移动，到达后或超时后切回攻击/追击。
    /// </summary>
    public void LeapAttackUpdate()
    {
        // 拴绳检查
        if (GetHorizontalDistanceToSpawn() > maxChaseDistanceFromSpawn || pendingReturnHome)
        {
            ChangeToNewState(EnemyState.returning);
            return;
        }

        FindPlayerTarget();
        if (targetPlayer == null || !targetPlayer.gameObject.activeInHierarchy)
        {
            ChangeToNewState(EnemyState.idle);
            return;
        }

        if (leapAttackTimer > 0f)
        {
            leapAttackTimer -= Time.deltaTime;

            // 向落点移动
            Vector3 moveTarget = new Vector3(leapTargetPosition.x, transform.position.y, leapTargetPosition.z);
            float step = leapAttackMoveSpeed * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, moveTarget, step);

            // 同步 Agent 内部位置
            if (agent != null)
                agent.nextPosition = transform.position;

            if (animator != null)
                animator.SetFloat("Speed", 0);

            // 到达落点或动画时间结束
            float distToTarget = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(leapTargetPosition.x, 0f, leapTargetPosition.z));
            if (leapAttackTimer <= 0f || distToTarget <= 0.3f)
            {
                // 跳扑结束，根据玩家距离决定切攻击还是追击
                if (animator != null)
                    animator.SetBool(IsLeapAttackingHash, false);

                if (IsPlayerInAttackRange(useBuffer: false))
                    ChangeToNewState(EnemyState.attack);
                else
                    ChangeToNewState(EnemyState.pursuit);
            }
            return;
        }

        // 兜底：跳扑结束后切追击
        if (animator != null)
            animator.SetBool(IsLeapAttackingHash, false);
        ChangeToNewState(EnemyState.pursuit);
    }

    /// <summary>
    /// 退出跳扑攻击状态：清理动画状态并启动冷却。
    /// </summary>
    public void LeapAttackExit()
    {
        leapAttackTimer = 0f;

        if (animator != null)
        {
            animator.SetBool(IsLeapAttackingHash, false);
        }

        leapAttackCooldownTimer = leapAttackCooldown;
    }

    /// <summary>
    /// 进入受击状态：停止移动、播放受击动画并进入硬直。
    /// </summary>
    public void GetHitEnter()
    {
        if (agent != null)
        {
            agent.isStopped = true;
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.velocity = Vector3.zero;
            agent.ResetPath();
        }

        if (animator != null)
        {
            animator.SetFloat("Speed", 0);
            // 中断可能正在播放的攻击/跳扑动画，确保受击期间不能发起攻击
            animator.SetBool(IsAttackingHash, false);
            animator.SetBool(IsLeapAttackingHash, false);
            animator.SetTrigger("GetHit");
        }

        // 播放受击特效与音效
        PlayHitEffect();

        getHitTimer = getHitDuration;
    }

    /// <summary>
    /// 受击状态每帧更新：硬直倒计时结束后切回追击或待机。
    /// </summary>
    public void GetHitUpdate()
    {
        if (getHitTimer > 0f)
        {
            getHitTimer -= Time.deltaTime;
            return;
        }

        // 硬直结束，根据玩家是否在范围内决定切追击还是待机
        if (IsPlayerInDetectRange())
            ChangeToNewState(EnemyState.pursuit);
        else
            ChangeToNewState(EnemyState.idle);
    }

    /// <summary>
    /// 退出受击状态：清理受击相关数据。
    /// </summary>
    public void GetHitExit()
    {
        getHitTimer = 0f;
    }

    /// <summary>
    /// 播放受击特效与音效。
    /// 特效支持多个同时播放：可引用场景中已存在的 ParticleSystem 直接播放；引用预制体时实例化后播放并自动销毁。
    /// </summary>
    private void PlayHitEffect()
    {
        Vector3 position = hitEffectPoint != null ? hitEffectPoint.position : transform.position;

        // 播放受击音效
        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, position);

        if (hitEffects == null || hitEffects.Length == 0)
            return;

        foreach (ParticleSystem effect in hitEffects)
        {
            if (effect == null)
                continue;

            // scene.IsValid() 为 true 表示是场景中已存在的对象，直接移动并播放即可复用
            if (effect.gameObject.scene.IsValid())
            {
                effect.transform.position = position;
                effect.Play();
                continue;
            }

            // 引用的是预制体：实例化一份到受击点，播放后自动销毁
            ParticleSystem instance = Instantiate(effect, position, Quaternion.identity);
            instance.Play();
            float lifetime = hitEffectLifetime > 0f ? hitEffectLifetime : 2f;
            Destroy(instance.gameObject, lifetime);
        }
    }

    /// <summary>
    /// 进入死亡状态：停止移动、播放死亡动画并触发死亡事件。
    /// </summary>
    public void DeadEnter()
    {
        isDead = true;
        destroyTimer = destroyAfterDeath;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.velocity = Vector3.zero;
            agent.ResetPath();
        }

        if (animator != null)
        {
            animator.SetFloat("Speed", 0);
            if (HasAnimatorParameter(deathTriggerHash))
            {
                animator.ResetTrigger(deathTriggerHash);
                animator.SetTrigger(deathTriggerHash);
            }
        }

        if (deathSound != null)
            AudioSource.PlayClipAtPoint(deathSound, transform.position);

        OnDeath?.Invoke();
    }

    /// <summary>
    /// 死亡状态每帧更新：等待销毁计时器。
    /// </summary>
    public void DeadUpdate()
    {
        if (destroyAfterDeath > 0f)
        {
            destroyTimer -= Time.deltaTime;
            if (destroyTimer <= 0f)
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// 退出死亡状态：一般不会调用，但保留清理逻辑。
    /// </summary>
    public void DeadExit()
    {
    }

    /// <summary>
    /// 检查 Animator 中是否存在指定哈希的参数。
    /// </summary>
    private bool HasAnimatorParameter(int hash)
    {
        if (animator == null) return false;
        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.nameHash == hash) return true;
        }
        return false;
    }

    /// <summary>
    /// 面朝玩家方向平滑旋转。
    /// </summary>
    private void FaceTargetPlayer()
    {
        if (targetPlayer == null) return;
        Vector3 direction = (targetPlayer.position - transform.position).normalized;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
        }
    }

    /// <summary>
    /// 受到伤害。已死亡或处于无敌时间时忽略。
    /// </summary>
    /// <param name="damage">伤害值</param>
    public void TakeDamage(float damage)
    {
        if (isDead || invincibilityTimer > 0f || damage <= 0f) return;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0f)
        {
            ChangeToNewState(EnemyState.dead);
        }
        else
        {
            invincibilityTimer = invincibilityDuration;
            // 受到伤害即进入受击状态，任何状态（攻击、跳扑、追击、巡逻等）都会被硬直打断，
            // 由 ChangeToNewState 内的受击锁定保证硬直结束前不会再切换其他状态。
            ChangeToNewState(EnemyState.gethit);
        }
    }

    /// <summary>
    /// 恢复生命值。
    /// </summary>
    /// <param name="amount">恢复量</param>
    public void Heal(float amount)
    {
        if (isDead || amount <= 0f) return;
        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>
    /// 生成攻击盒子的公共逻辑。由各动画事件入口方法调用，传入对应伤害值。
    /// </summary>
    private void SpawnAttackBoxInternal(float damage)
    {
        if (attackBoxPrefab == null) return;
        if (attackBoxSpawnPoint == null) return;

        GameObject box = Instantiate(attackBoxPrefab, attackBoxSpawnPoint.position, attackBoxSpawnPoint.rotation, attackBoxSpawnPoint);
        currentAttackTrigger = box.GetComponent<AttackTrigger>();
        if (currentAttackTrigger != null)
        {
            currentAttackTrigger.DisableDamage();
            currentAttackTrigger.damage = damage;
        }

        if (attackBoxLifetime > 0f)
            Destroy(box, attackBoxLifetime);
    }

    /// <summary>
    /// 动画事件：生成攻击盒子。
    /// </summary>
    public void SpawnAttackBox()
    {
        SpawnAttackBoxInternal(attackDamage);
    }

    /// <summary>
    /// 动画事件：激活当前攻击盒子的伤害判定。
    /// </summary>
    public void ActivateAttackBox()
    {
        if (currentAttackTrigger != null)
            currentAttackTrigger.EnableDamage();
    }
}