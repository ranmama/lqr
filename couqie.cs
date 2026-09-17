using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// couqie：基于 NavMeshAgent 寻路 + Rigidbody 驱动的随机游走 AI 控制器。
/// 模型会在出生点周围随机挑选一个可达点走过去，到达后原地停留一段时间，然后继续选下一个点。
/// </summary>
/// <remarks>
/// 核心思路：NavMeshAgent 只负责"算路"，不接管位移；真正的位移完全由 Rigidbody 完成。
/// 为此必须关闭 agent.updatePosition 与 agent.updateRotation，否则 NavMeshAgent 会和 Rigidbody
/// 争抢 Transform 的控制权，表现为模型抖动、穿墙或瞬移。
/// 同时必须在每个物理帧把 agent.nextPosition 同步为 rb.position，否则 agent 的内部位置会停留在
/// 旧值，导致 remainingDistance 与路径计算结果全部失真。
/// </remarks>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NavMeshAgent))]
public class couqie : MonoBehaviour
{
    /// <summary>
    /// 默认的移动/待机动画参数名。0 表示待机，1 表示移动。
    /// 注意：本项目的 Animator Controller 里该参数实际名为 "New Float"（而非项目其它脚本用的 "Speed"），
    /// 故默认值与其对齐。若你的 Controller 用的是其它参数名，请在 Inspector 的「动画」分组里改成一致的名字。
    /// </summary>
    private const string DefaultMoveParamName = "New Float";

    /// <summary>
    /// 随机游走的内部状态枚举，用于描述当前所处的游走阶段。
    /// </summary>
    private enum WanderState
    {
        /// <summary>等待状态：选点失败或卡住后原地停留，等待计时结束后重新选点。</summary>
        Waiting,
        /// <summary>移动状态：正沿 NavMeshAgent 计算出的路径朝目标点移动。</summary>
        Moving,
        /// <summary>巡视状态：到达目标点后原地停留并随机转向张望，巡视结束后再寻找下一个目标点。</summary>
        Observing,
    }

    [Header("移动")]
    [Tooltip("移动速度（米/秒）。决定写入 Rigidbody 的水平速度大小，NavMeshAgent 自身速度仅用于寻路估算")]
    public float moveSpeed = 2f;
    [Tooltip("到达判定距离（米）。与目标点的剩余距离小于等于该值时视为已到达")]
    public float arriveDistance = 0.5f;

    [Header("随机游走")]
    [Tooltip("巡逻半径（米）。以出生点为圆心，在该圆内随机挑选目标点")]
    public float patrolRadius = 8f;
    [Tooltip("最小移动距离（米）。随机点离当前位置小于该值时重新随机，防止原地抽搐")]
    public float minMoveDistance = 2f;
    [Tooltip("NavMesh 采样半径（米）。把随机点吸附到可行走面时的搜索范围，不宜过大以免吸到墙另一侧")]
    public float sampleRadius = 2f;
    [Tooltip("单次选点的最大尝试次数。超过次数后回退到出生点，仍失败则原地等待下一轮")]
    public int maxSampleAttempts = 10;
    [Tooltip("到达目标后等待的最短时间（秒）")]
    public float minWaitTime = 1f;
    [Tooltip("到达目标后等待的最长时间（秒）。实际等待时间在该区间内随机")]
    public float maxWaitTime = 3f;
    [Tooltip("到达目标后原地巡视的时长（秒）。巡视期间原地随机转向张望，结束后再寻找下一个目标点。设 0 则跳过巡视直接找下一个点")]
    public float observeDuration = 2f;
    [Tooltip("巡视时单次张望停留的最短时间（秒）")]
    public float observeLookMinTime = 0.8f;
    [Tooltip("巡视时单次张望停留的最长时间（秒）")]
    public float observeLookMaxTime = 1.6f;
    [Tooltip("卡住判定的检测周期（秒）。每累计该时长检查一次实际位移")]
    public float stuckCheckInterval = 1f;
    [Tooltip("卡住判定的速度阈值（米/秒）。有路径但平均速度低于该值时判定为卡住，会重新选点")]
    public float stuckSpeedThreshold = 0.15f;

    [Header("动画")]
    [Tooltip("模型上的 Animator。留空时自动从自身及子物体查找，仍为空则跳过动画驱动")]
    [SerializeField] private Animator animator;
    [Tooltip("Animator 中控制移动/待机切换的浮点参数名。0=待机，1=移动。默认 \"New Float\" 对齐本项目 Controller，请与 Animator 窗口里的参数名保持一致")]
    [SerializeField] private string moveParamName = DefaultMoveParamName;

    [Header("朝向")]
    [Tooltip("模型正面所对应的前向轴。标准模型为 +Z，MMD 导入的模型通常为 +Y。若角色以背面/侧面行走，请调整此项")]
    [SerializeField] private Vector3 modelForward = Vector3.forward;
    [Tooltip("转向插值速度。越大转身越快，0 表示瞬间转向")]
    public float rotationSpeed = 8f;

    [Header("生命值")]
    [Tooltip("最大生命值")]
    public float maxHealth = 100f;
    [Tooltip("当前生命值（运行时由脚本管理，Inspector 初始值仅在 Start 时生效）")]
    public float currentHealth = 100f;
    [Tooltip("受伤后的无敌时间（秒），防止连续受伤")]
    public float invincibilityDuration = 0.3f;
    [Tooltip("受击动画 Trigger 名称，需与 Animator Controller 中的 Trigger 参数名一致。留空则不播放受击动画")]
    public string getHitTrigger = "GetHit";
    [Tooltip("受击音效（可选）")]
    public AudioClip hitSound;
    [Tooltip("死亡后延迟销毁时间（秒）。到时间后自动 Destroy 整个 GameObject。设 0 表示不自动销毁")]
    public float destroyAfterDeath = 2f;

    [Header("调试")]
    [Tooltip("选中该物体时在 Scene 视图绘制巡逻圆与当前目标点")]
    public bool showDebugGizmos = true;
    [Tooltip("输出选点与卡住的详细日志，便于排查寻路问题")]
    public bool verboseLog = false;

    /// <summary>自身挂載的刚体组件，负责真实位移。</summary>
    private Rigidbody rb;
    /// <summary>自身挂載的寻路组件，只负责计算路径。</summary>
    private NavMeshAgent agent;
    /// <summary>出生点，作为随机取点的圆心。</summary>
    private Vector3 spawnPosition = Vector3.zero;
    /// <summary>当前正在前往的目标点。</summary>
    private Vector3 targetPosition = Vector3.zero;
    /// <summary>当前游走状态。</summary>
    private WanderState currentState = WanderState.Waiting;
    /// <summary>等待状态剩余倒计时（秒）。</summary>
    private float waitTimer;
    /// <summary>巡视状态剩余时长（秒）。</summary>
    private float observeTimer;
    /// <summary>巡视时当前张望方向的剩余停留时间（秒）。</summary>
    private float observeLookTimer;
    /// <summary>巡视时当前张望方向的目标旋转（绕 Y 轴）。</summary>
    private Quaternion observeTargetRotation = Quaternion.identity;
    /// <summary>卡住检测累计计时（秒）。</summary>
    private float stuckTimer;
    /// <summary>上一轮卡住检测时记录的位置，用于计算实际位移。</summary>
    private Vector3 lastStuckCheckPosition = Vector3.zero;
    /// <summary>是否已经输出过"不在 NavMesh 上"的警告，避免每帧刷屏。</summary>
    private bool hasWarnedOffNavMesh;
    /// <summary>
    /// 复用的寻路结果缓存，避免每次选点时反复分配 NavMeshPath。
    /// 注意：不能写成字段初始化器 new NavMeshPath()，Unity 禁止在 MonoBehaviour 构造阶段
    /// 调用 InitializeNavMeshPath，会直接抛 UnityException 并导致实例为 null。改为在 Awake 中创建。
    /// </summary>
    private NavMeshPath wanderPath;

    /// <summary>动画移动参数的哈希缓存，由 <see cref="moveParamName"/> 计算得到，运行时重算开销极小。</summary>
    private int moveParamHash;
    /// <summary>受击动画 Trigger 的哈希缓存。</summary>
    private int getHitTriggerHash;
    /// <summary>是否已死亡。</summary>
    private bool isDead;
    /// <summary>无敌时间剩余倒计时（秒），用于防止连续受伤。</summary>
    private float invincibilityTimer;
    /// <summary>受击音效的 AudioSource，惰性创建。</summary>
    private AudioSource audioSource;

    /// <summary>是否已死亡（供 AttackTrigger 等外部调用方判断，避免对死亡目标重复结算伤害）。</summary>
    public bool IsDead => isDead;

    /// <summary>
    /// 当前是否正处于移动状态（即已持有有效目标点并沿路径前进）。
    /// 该值由 <see cref="currentState"/> 派生，不单独维护布尔字段，避免两处状态手工同步造成漂移。
    /// </summary>
    private bool IsMoving => currentState == WanderState.Moving;

    /// <summary>
    /// 初始化：缓存组件引用并配置 Rigidbody 与 NavMeshAgent 的工作方式。
    /// </summary>
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        agent = GetComponent<NavMeshAgent>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        // NavMeshPath 必须在 Awake 及之后创建，字段初始化器阶段会被 Unity 拒绝。
        if (wanderPath == null)
        {
            wanderPath = new NavMeshPath();
        }

        // 缓存动画参数哈希，供 UpdateAnimator 使用。
        moveParamHash = Animator.StringToHash(moveParamName);
        getHitTriggerHash = Animator.StringToHash(getHitTrigger);

        spawnPosition = transform.position;
        SetupRigidbody();
        SetupNavMeshAgent();
    }

    /// <summary>
    /// 启用时调用：重置寻路与游走状态，避免编辑器重复运行或反复启停时残留旧路径。
    /// </summary>
    private void OnEnable()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        SetupRigidbody();
        SetupNavMeshAgent();
        ResetWanderState();
    }

    /// <summary>
    /// 启动时调用：修正出生点为实例化后的最终位置，并立即尝试选取第一个目标点。
    /// </summary>
    private void Start()
    {
        spawnPosition = transform.position;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        isDead = false;
        TryStartNewWander();
    }

    /// <summary>
    /// 每帧更新：驱动状态机，处理到达判定、等待计时、卡住检测与动画参数。
    /// </summary>
    private void Update()
    {
        if (invincibilityTimer > 0f)
        {
            invincibilityTimer -= Time.deltaTime;
        }

        // 死亡后不再驱动任何游走/动画逻辑，只保留物理系统接管（见 FixedUpdate）。
        if (isDead)
        {
            return;
        }

        if (!IsAgentReady())
        {
            // 不在 NavMesh 上时不做任何寻路操作，同时刹停水平速度。
            StopHorizontalVelocity();
            UpdateAnimator();
            return;
        }

        UpdateArriveCheck();
        UpdateWaitTimer();
        UpdateObserve();
        UpdateStuckCheck();
        UpdateAnimator();
    }

    /// <summary>
    /// 固定物理帧更新：把 NavMeshAgent 算出的期望方向转换为 Rigidbody 速度，由物理系统完成位移。
    /// </summary>
    private void FixedUpdate()
    {
        if (rb == null)
        {
            return;
        }

        // 死亡后：不再主动写入速度或转向，让刚体自然静止并保留重力与物理碰撞，
        // 以便被玩家推动或受其它物理影响。移动逻辑在死亡后全部跳过。
        if (isDead)
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            return;
        }

        if (!IsAgentReady())
        {
            StopHorizontalVelocity();
            return;
        }

        // 关键：每帧把刚体的真实位置回写给 agent，
        // 否则 agent 内部位置停留在旧值，路径与剩余距离全部失真。
        agent.nextPosition = rb.position;

        // 巡视状态：原地刹停，只做平滑转向张望，不写移动速度。
        if (currentState == WanderState.Observing)
        {
            StopHorizontalVelocity();
            RotateTowards(observeTargetRotation);
            return;
        }

        if (currentState != WanderState.Moving || agent.pathPending || !agent.hasPath)
        {
            StopHorizontalVelocity();
            return;
        }

        Vector3 desiredDirection = agent.desiredVelocity;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            StopHorizontalVelocity();
            return;
        }

        desiredDirection.Normalize();

        // 保留 rb.velocity.y，重力与下落才能正常工作。
        rb.velocity = new Vector3(desiredDirection.x * moveSpeed, rb.velocity.y, desiredDirection.z * moveSpeed);

        // 转向：让模型的正面（modelForward）对准移动方向。
        // 标准模型用 LookRotation 即可（正面 +Z 朝前）；MMD 导入的模型正面通常是 +Y，
        // 需要先把移动方向绕 Y 轴旋转到模型正面所对应的角度。
        Quaternion targetRotation = LookRotationForForward(desiredDirection);
        RotateTowards(targetRotation);
    }

    /// <summary>
    /// 根据模型正面轴（<see cref="modelForward"/>）计算「面向指定水平方向」的目标旋转。
    /// 标准模型正面为 +Z 时等价于 LookRotation；MMD 模型正面为 +Y 时做相应的绕 Y 轴补偿。
    /// </summary>
    /// <param name="direction">目标水平方向（应在 XZ 平面上，Y 分量会被忽略）。</param>
    /// <returns>使模型正面朝向该方向的目标旋转。</returns>
    private Quaternion LookRotationForForward(Vector3 direction)
    {
        direction.y = 0f;
        float forwardYaw = Mathf.Atan2(modelForward.x, modelForward.z) * Mathf.Rad2Deg;
        return Quaternion.LookRotation(direction) * Quaternion.Euler(0f, -forwardYaw, 0f);
    }

    /// <summary>
    /// 将刚体平滑转向指定目标旋转。转向速度由 <see cref="rotationSpeed"/> 决定，0 表示瞬间转向。
    /// </summary>
    /// <param name="targetRotation">目标旋转。</param>
    private void RotateTowards(Quaternion targetRotation)
    {
        if (rotationSpeed <= 0f)
        {
            // 转向速度为 0 时直接吸附到目标朝向，实现瞬间转向。
            // 注意不能依赖 Slerp(t=0)，那会返回起始旋转，导致模型永不转向。
            rb.rotation = targetRotation;
        }
        else
        {
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }
    }

    /// <summary>
    /// 配置 Rigidbody：冻结旋转防止被撞倒，开启重力，明确使用非运动学刚体。
    /// </summary>
    private void SetupRigidbody()
    {
        if (rb == null)
        {
            return;
        }

        rb.freezeRotation = true;
        rb.useGravity = true;
        rb.isKinematic = false;
    }

    /// <summary>
    /// 配置 NavMeshAgent：关闭自动位置与旋转同步，位移交由 Rigidbody 处理；
    /// 同时同步速度与停止距离，保证剩余距离估算与实际移动匹配。
    /// </summary>
    private void SetupNavMeshAgent()
    {
        if (agent == null)
        {
            return;
        }

        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.isStopped = false;
        agent.speed = Mathf.Max(moveSpeed, 0.01f);
        agent.stoppingDistance = arriveDistance;
        agent.ResetPath();
    }

    /// <summary>
    /// 重置游走状态：清空路径与目标，进入等待状态以便下一帧重新选点。
    /// </summary>
    private void ResetWanderState()
    {
        targetPosition = transform.position;
        currentState = WanderState.Waiting;
        waitTimer = 0f;
        stuckTimer = 0f;
        observeTimer = 0f;
        observeLookTimer = 0f;
        lastStuckCheckPosition = rb != null ? rb.position : transform.position;

        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    /// <summary>
    /// 判断 NavMeshAgent 是否处于可操作状态。
    /// </summary>
    /// <returns>组件存在且已贴合到 NavMesh 上时返回 true，否则返回 false。</returns>
    private bool IsAgentReady()
    {
        if (agent == null)
        {
            return false;
        }

        if (agent.isOnNavMesh)
        {
            hasWarnedOffNavMesh = false;
            return true;
        }

        if (!hasWarnedOffNavMesh)
        {
            hasWarnedOffNavMesh = true;
            Debug.LogWarning(
                $"[couqie] {name} 的 NavMeshAgent 不在 NavMesh 上，已暂停随机游走。" +
                "请确认场景已烘焙 NavMesh，且物体的出生点位于可行走面附近。", this);
        }

        return false;
    }

    /// <summary>
    /// 在出生点为圆心的巡逻圆内随机选取一个可达目标点，并设置为当前路径。
    /// 使用 CalculatePath 校验路径完整后再 SetPath，确保验证的路径与实际行走的路径一致。
    /// </summary>
    private void TryStartNewWander()
    {
        if (!IsAgentReady())
        {
            return;
        }
        // 兜底：正常情况下 Awake 已创建，防止异常生命周期下为 null 导致 CalculatePath 空引用。
        if (wanderPath == null)
        {
            wanderPath = new NavMeshPath();
        }

        Vector3 currentPosition = rb != null ? rb.position : transform.position;

        for (int attempt = 0; attempt < maxSampleAttempts; attempt++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = spawnPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            // 目标点离当前位置太近时重新随机，避免只挪一步就停下造成原地抽搐。
            Vector3 toCandidate = hit.position - currentPosition;
            toCandidate.y = 0f;
            if (toCandidate.magnitude < minMoveDistance)
            {
                continue;
            }

            // 复用同一个 NavMeshPath 实例：SetPath 内部会拷贝数据，复用是安全的。
            if (!agent.CalculatePath(hit.position, wanderPath))
            {
                continue;
            }
            if (wanderPath.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            agent.ResetPath();
            agent.SetPath(wanderPath);
            ApplyNewTarget(hit.position);
            return;
        }

        // 多次尝试都失败，回退到出生点。
        if (agent.CalculatePath(spawnPosition, wanderPath) &&
            wanderPath.status == NavMeshPathStatus.PathComplete)
        {
            Vector3 toSpawn = spawnPosition - currentPosition;
            toSpawn.y = 0f;
            if (toSpawn.magnitude >= minMoveDistance)
            {
                agent.ResetPath();
                agent.SetPath(wanderPath);
                ApplyNewTarget(spawnPosition);
                if (verboseLog)
                {
                    Debug.Log($"[couqie] {name} 随机选点失败，回退到出生点 {spawnPosition}。", this);
                }
                return;
            }
        }

        // 连出生点都走不过去，则原地等待下一轮再试。
        if (verboseLog)
        {
            Debug.LogWarning($"[couqie] {name} 未能找到可达目标点，原地等待后重试。", this);
        }
        EnterWaitState();
    }

    /// <summary>
    /// 记录新目标点并切换到移动状态，同时重置卡住检测数据。
    /// </summary>
    /// <param name="newTarget">已通过路径校验的目标点世界坐标。</param>
    private void ApplyNewTarget(Vector3 newTarget)
    {
        targetPosition = newTarget;
        currentState = WanderState.Moving;
        stuckTimer = 0f;
        lastStuckCheckPosition = rb != null ? rb.position : transform.position;

        if (verboseLog)
        {
            Debug.Log($"[couqie] {name} 新目标点：{newTarget}（巡逻半径 {patrolRadius}）。", this);
        }
    }

    /// <summary>
    /// 进入等待状态：清空路径、刹停水平速度，并随机一个等待时长。
    /// </summary>
    private void EnterWaitState()
    {
        currentState = WanderState.Waiting;
        waitTimer = Random.Range(minWaitTime, Mathf.Max(maxWaitTime, minWaitTime));
        stuckTimer = 0f;

        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }

        StopHorizontalVelocity();
    }

    /// <summary>
    /// 进入巡视状态：清空路径、刹停水平速度，并初始化巡视时长与第一个张望方向。
    /// 若巡视时长设为 0，则跳过巡视直接寻找下一个目标点。
    /// </summary>
    private void EnterObserveState()
    {
        if (observeDuration <= 0f)
        {
            // 不巡视，直接找下一个点。
            TryStartNewWander();
            return;
        }

        currentState = WanderState.Observing;
        observeTimer = observeDuration;
        stuckTimer = 0f;

        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }

        StopHorizontalVelocity();
        PickNewObserveDirection();
    }

    /// <summary>
    /// 随机选取一个新的张望方向（绕 Y 轴随机角度），并重置该方向的停留计时。
    /// </summary>
    private void PickNewObserveDirection()
    {
        // 在世界 XZ 平面上随机一个朝向角。
        float randomYaw = Random.Range(0f, 360f);
        Vector3 lookDirection = Quaternion.Euler(0f, randomYaw, 0f) * Vector3.forward;
        observeTargetRotation = LookRotationForForward(lookDirection);
        observeLookTimer = Random.Range(observeLookMinTime, Mathf.Max(observeLookMaxTime, observeLookMinTime));
    }

    /// <summary>
    /// 更新巡视状态：倒计时结束后随机转向新的张望方向；巡视总时长耗尽后寻找下一个目标点。
    /// 实际转向由 FixedUpdate 完成（见 FixedUpdate 中的巡视分支），这里只负责计时与方向切换。
    /// </summary>
    private void UpdateObserve()
    {
        if (currentState != WanderState.Observing)
        {
            return;
        }

        observeLookTimer -= Time.deltaTime;
        if (observeLookTimer <= 0f)
        {
            PickNewObserveDirection();
        }

        observeTimer -= Time.deltaTime;
        if (observeTimer <= 0f)
        {
            TryStartNewWander();
        }
    }

    /// <summary>
    /// 判断是否已经抵达当前目标点。
    /// 优先使用 agent.remainingDistance，不可用时退化为水平距离判定，避免 Infinity 导致的误判。
    /// </summary>
    /// <returns>已到达返回 true，否则返回 false。</returns>
    private bool HasReachedTarget()
    {
        if (agent == null || !IsMoving)
        {
            return false;
        }
        if (agent.pathPending || !agent.hasPath)
        {
            return false;
        }

        float remaining = agent.remainingDistance;
        if (!float.IsNaN(remaining) && !float.IsInfinity(remaining) && remaining <= arriveDistance)
        {
            return true;
        }

        Vector3 toTarget = targetPosition - (rb != null ? rb.position : transform.position);
        toTarget.y = 0f;
        return toTarget.magnitude <= arriveDistance;
    }

    /// <summary>
    /// 更新到达判定：处于移动状态且已抵达目标点时，进入巡视状态（若巡视时长设为 0 则直接找下一个点）。
    /// </summary>
    private void UpdateArriveCheck()
    {
        if (currentState != WanderState.Moving)
        {
            return;
        }

        if (HasReachedTarget())
        {
            EnterObserveState();
        }
    }

    /// <summary>
    /// 更新等待计时：倒计时结束后重新选取下一个目标点。
    /// </summary>
    private void UpdateWaitTimer()
    {
        if (currentState != WanderState.Waiting)
        {
            return;
        }

        waitTimer -= Time.deltaTime;
        if (waitTimer <= 0f)
        {
            TryStartNewWander();
        }
    }

    /// <summary>
    /// 更新卡住检测：有路径但实际位移几乎为零并持续一段时间后，判定为卡住并重新选点。
    /// </summary>
    private void UpdateStuckCheck()
    {
        if (agent == null || currentState != WanderState.Moving)
        {
            stuckTimer = 0f;
            return;
        }
        if (agent.pathPending || !agent.hasPath)
        {
            stuckTimer = 0f;
            return;
        }

        stuckTimer += Time.deltaTime;
        if (stuckTimer < stuckCheckInterval)
        {
            return;
        }

        Vector3 currentPosition = rb != null ? rb.position : transform.position;
        float travelled = Vector3.Distance(lastStuckCheckPosition, currentPosition);
        float averageSpeed = travelled / Mathf.Max(stuckTimer, 0.0001f);

        lastStuckCheckPosition = currentPosition;
        stuckTimer = 0f;

        if (averageSpeed >= stuckSpeedThreshold)
        {
            return;
        }

        if (verboseLog)
        {
            Debug.LogWarning(
                $"[couqie] {name} 疑似卡住（平均速度 {averageSpeed:F3} 米/秒），正在重新选点。", this);
        }

        EnterWaitState();
    }

    /// <summary>
    /// 更新动画参数：根据刚体水平速度在待机与移动之间切换 Animator 的移动参数（0=待机 1=移动）。
    /// </summary>
    private void UpdateAnimator()
    {
        if (animator == null)
        {
            return;
        }

        float horizontalSpeed = 0f;
        if (rb != null)
        {
            horizontalSpeed = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
        }

        animator.SetFloat(moveParamHash, horizontalSpeed > 0.1f ? 1f : 0f);
    }

    /// <summary>
    /// 刹停水平速度，保留竖直速度以保证重力与落地正常。
    /// </summary>
    private void StopHorizontalVelocity()
    {
        if (rb == null)
        {
            return;
        }

        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
    }

    /// <summary>
    /// 受到伤害：扣减生命值、进入短暂无敌、播放受击音效与受击动画。
    /// 已死亡、处于无敌时间或伤害值无效时忽略。
    /// </summary>
    /// <param name="damage">伤害值，需大于 0。</param>
    public void TakeDamage(float damage)
    {
        if (isDead || invincibilityTimer > 0f || damage <= 0f)
        {
            return;
        }

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        // 未死亡：进入无敌时间并播放受击反馈。
        invincibilityTimer = invincibilityDuration;

        if (hitSound != null)
        {
            PlayOneShot(hitSound);
        }

        if (animator != null && getHitTriggerHash != 0)
        {
            animator.SetTrigger(getHitTriggerHash);
        }
    }

    /// <summary>
    /// 处理死亡：标记死亡状态、刹停并停止随机游走，随后在 <see cref="destroyAfterDeath"/>
    /// 指定的秒数后自动销毁整个 GameObject。死亡后到销毁前，企鹅会在原地保持不动，
    /// 仅保留最基础的物理碰撞（可被推动、受重力影响），不再执行任何移动或动画逻辑。
    /// </summary>
    private void Die()
    {
        isDead = true;
        StopHorizontalVelocity();
        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
        currentState = WanderState.Waiting;
        UpdateAnimator();

        if (destroyAfterDeath > 0f)
        {
            Destroy(gameObject, destroyAfterDeath);
        }
    }

    /// <summary>
    /// 用自身的 AudioSource 播放一次性音效（懒创建，避免无 AudioSource 时反复 GetComponent）。
    /// </summary>
    /// <param name="clip">要播放的音效。</param>
    private void PlayOneShot(AudioClip clip)
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        audioSource.PlayOneShot(clip);
    }

    /// <summary>
    /// 编辑器参数校验：把所有可调参数钳制到合法范围，避免非法值导致行为异常。
    /// </summary>
    private void OnValidate()
    {
        moveSpeed = Mathf.Max(moveSpeed, 0f);
        rotationSpeed = Mathf.Max(rotationSpeed, 0f);
        arriveDistance = Mathf.Max(arriveDistance, 0.05f);

        // 动画参数名不能为空，否则 StringToHash("") 会得到 0，动画无法驱动。
        if (string.IsNullOrEmpty(moveParamName))
        {
            moveParamName = DefaultMoveParamName;
        }

        // 前向轴不能为零向量，否则无法确定朝向，回退到默认 +Z。
        if (modelForward.sqrMagnitude < 0.0001f)
        {
            modelForward = Vector3.forward;
        }
        modelForward.Normalize();

        patrolRadius = Mathf.Max(patrolRadius, 0.1f);
        minMoveDistance = Mathf.Clamp(minMoveDistance, 0f, patrolRadius);
        sampleRadius = Mathf.Max(sampleRadius, 0.1f);
        maxSampleAttempts = Mathf.Max(maxSampleAttempts, 1);

        minWaitTime = Mathf.Max(minWaitTime, 0f);
        maxWaitTime = Mathf.Max(maxWaitTime, minWaitTime);
        observeDuration = Mathf.Max(observeDuration, 0f);
        observeLookMinTime = Mathf.Max(observeLookMinTime, 0.05f);
        observeLookMaxTime = Mathf.Max(observeLookMaxTime, observeLookMinTime);

        stuckCheckInterval = Mathf.Max(stuckCheckInterval, 0.1f);
        stuckSpeedThreshold = Mathf.Max(stuckSpeedThreshold, 0f);

        maxHealth = Mathf.Max(maxHealth, 1f);
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        invincibilityDuration = Mathf.Max(invincibilityDuration, 0f);
        destroyAfterDeath = Mathf.Max(destroyAfterDeath, 0f);
    }

    /// <summary>
    /// 选中该物体时在 Scene 视图绘制调试图形：巡逻圆、出生点与当前目标点。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos)
        {
            return;
        }

        Vector3 center = Application.isPlaying ? spawnPosition : transform.position;

        Gizmos.color = Color.cyan;
        DrawGizmosCircle(center, patrolRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(center, 0.2f);

        if (!IsMoving)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(targetPosition, 0.25f);
        Gizmos.DrawLine(center, targetPosition);
    }

    /// <summary>
    /// 在 XZ 平面上绘制一个线框圆，用于可视化巡逻范围。
    /// </summary>
    /// <param name="center">圆心世界坐标。</param>
    /// <param name="radius">圆半径（米）。</param>
    private void DrawGizmosCircle(Vector3 center, float radius)
    {
        const int SegmentCount = 48;
        Vector3 previousPoint = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= SegmentCount; i++)
        {
            float angle = (float)i / SegmentCount * Mathf.PI * 2f;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }
    }
}
