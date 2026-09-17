using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class Player : MonoBehaviour
{
    [Header("移动")]
    public float moveSpeed = 6f;
    [Tooltip("跳跃过程中的水平移动速度，仅在离地到落地期间生效")]
    public float jumpMovementSpeed = 9f;
    [Tooltip("奔跑速度倍率，按住 Shift 时应用。1.5=快走，2.0=快跑")]
    [Range(1.5f, 2f)]
    public float runMultiplier = 1.75f;
    [Tooltip("奔跑按键，可改为右 Shift 或其他按键以避免冲突")]
    public KeyCode runKey = KeyCode.LeftShift;
    public float rotationSpeed = 12f;

    [Header("跳跃")]
    [Tooltip("跳跃按键，默认空格。使用显式按键可避免 Input Manager 在不同 Play 模式下行为不一致")]
    public KeyCode jumpKey = KeyCode.Space;
    [Tooltip("目标跳跃高度，角色最终稳定到达的最高高度")]
    public float jumpHeight = 2f;
    [Tooltip("跳跃速度倍率，默认 1.0。仅影响上升阶段达到目标高度的快慢，不改变最终高度")]
    [Range(0.5f, 2f)]
    public float jumpSpeed = 1f;
    [Tooltip("上升辅助推进强度，JumpSpeed > 1 时生效。值越大，高速跳跃越快接近目标高度")]
    public float ascentBoostMultiplier = 20f;
    [Tooltip("起跳后必须等待这段时间才能再次跳跃（秒）。期间无视地面检测，彻底防止多段跳")]
    public float jumpCooldown = 0.3f;
    [Tooltip("下落速度倍率，1=正常重力，越大下落越快")]
    public float fallMultiplier = 1f;
    [Tooltip("跳跃动画触发器名称，需与 Animator 中的 Trigger 参数名一致")]
    public string jumpTrigger = "Jump";

    [Header("地面")]
    [Tooltip("地面检测层级")]
    public LayerMask groundLayer = ~0;
    [Tooltip("地面检测半径，建议小一点避免墙面/斜面误判")]
    public float groundCheckRadius = 0.05f;
    [Tooltip("地面检测点相对于胶囊体底部的向上偏移")]
    public float groundCheckOffsetY = 0.05f;

    [Header("攻击")]
    [Tooltip("普通攻击按键，默认鼠标左键")]
    public KeyCode attackKey = KeyCode.Mouse0;
    [Tooltip("攻击冷却时间，控制攻击动画播放间隔（秒）")]
    [Range(0.5f, 1f)]
    public float attackCooldown = 0.6f;
    [Tooltip("攻击动画锁定时间，期间不更新移动动画参数，避免动画互相干扰")]
    [Range(0.3f, 1f)]
    public float attackLockDuration = 0.5f;
    [Tooltip("攻击逻辑锁定最大允许时长（秒）。超过此时长仍被锁定时，将强制解除锁定，防止动画异常导致玩家永久无法移动。")]
    public float maxAttackLockTime = 2f;
    [Tooltip("轻击动画状态名，用于确认轻击动画真正开始播放后才触发视角晃动等效果。当前配置：attack_02 为轻击动画。")]
    public string lightAttackStateName = "attack_02";
    [Tooltip("是否启用轻击动画状态验证。关闭后视角晃动会在按下轻击键时立即触发，便于排查验证逻辑问题。")]
    public bool verifyLightAttackAnimation = true;
    [Tooltip("第一段轻击音效列表，触发时随机播放其中一段。可只放一段，也可放多段随机。")]
    public AudioClip[] lightAttackSounds;

    [Header("轻击连击")]
    [Tooltip("第二段轻击输入窗口时间（秒）。在第一段轻击开始后这段时间内再次按轻击键，可衔接第二段动画。默认 2 秒。")]
    public float lightComboWindow = 2f;
    [Tooltip("第二段轻击动画状态名。当前配置：attack_05 为第二段轻击动画。")]
    public string lightAttackCombo2StateName = "attack_05";
    [Tooltip("第二段轻击触发进度阈值（0-1）。第一段轻击动画播放到该比例后才允许衔接第二段。默认 0.53。")]
    [Range(0f, 1f)]
    public float combo2ProgressThreshold = 0.53f;
    [Tooltip("第二段轻击音效列表，触发连击第二段时随机播放其中一段。")]
    public AudioClip[] lightCombo2Sounds;

    [Header("重击")]
    [Tooltip("重击按键，默认鼠标右键")]
    public KeyCode heavyAttackKey = KeyCode.Mouse1;
    [Tooltip("重击冷却时间（秒）")]
    public float heavyAttackCooldown = 1.5f;
    [Tooltip("重击位移距离，0-2米，步长0.1米。可在配置界面精确调整")]
    [Range(0f, 2f)]
    public float heavyAttackDashDistance = 1.5f;
    [Tooltip("重击位移持续时间，建议与重击动画时长相等")]
    public float heavyAttackDashDuration = 0.6f;
    [Tooltip("重击位移时的障碍物检测层级")]
    public LayerMask obstacleLayer = ~0;
    [Tooltip("重击音效")]
    public AudioClip heavyAttackSound;
    [Tooltip("重击粒子特效")]
    public ParticleSystem heavyAttackEffect;
    [Tooltip("重击动画状态名，用于确认重击动画真正开始播放后才触发视角晃动等效果。当前配置：attack_01 为重击动画。")]
    public string heavyAttackStateName = "attack_01";
    [Tooltip("是否启用重击动画状态验证。关闭后视角晃动会在按下重击键时立即触发，便于排查验证逻辑问题。")]
    public bool verifyHeavyAttackAnimation = true;
    [Tooltip("重击逻辑锁定最大允许时长（秒）。超过此时长仍被锁定时，将强制解除锁定。")]
    public float maxHeavyAttackLockTime = 2.5f;

    [Header("攻击盒子")]
    [Tooltip("攻击盒子的预制体，由动画事件触发生成。Faction 需设为 Player")]
    public GameObject attackBoxPrefab;
    [Tooltip("攻击盒子生成的挂点 Transform（玩家子物体，如武器尖端）")]
    public Transform attackBoxSpawnPoint;
    [Tooltip("攻击盒子存活时间（秒），超时自动销毁")]
    public float attackBoxLifetime = 0.5f;
    [Tooltip("第一段轻击伤害值")]
    public float lightAttack1Damage = 20f;
    [Tooltip("第二段轻击（连击）伤害值")]
    public float lightAttack2Damage = 35f;
    [Tooltip("重击伤害值")]
    public float heavyAttackDamage = 50f;
    /// <summary>当前攻击盒子的 AttackTrigger 组件引用，供动画事件激活伤害。</summary>
    private AttackTrigger currentAttackTrigger;

    [Header("生命值")]
    [Tooltip("最大生命值")]
    public float maxHealth = 100f;
    [Tooltip("当前生命值（运行时由脚本管理，Inspector 中的初始值仅在 Start 时生效）")]
    public float currentHealth = 100f;
    [Tooltip("受伤后的无敌时间（秒），防止连续受伤")]
    public float invincibilityDuration = 0.5f;
    [Tooltip("是否在调试 UI 中显示生命值")]
    public bool showHealthUI = true;

    [Header("受击")]
    [Tooltip("受击动画 Trigger 名称，需与 Animator Controller 中的 Trigger 参数名一致")]
    public string getHitTrigger = "GetHit";
    [Tooltip("受击硬直时长（秒），期间无法移动、无法攻击，只保留物理碰撞")]
    public float getHitDuration = 0.4f;
    [Tooltip("受击特效的生成挂点（玩家子物体，如身体中心）。特效会在此位置生成。")]
    public Transform getHitEffectSpawnPoint;
    [Tooltip("受击特效预制体列表，进入受击状态时会在挂点处同时生成全部。可留空。")]
    public GameObject[] getHitEffectPrefabs;
    [Tooltip("受击特效自动销毁时间（秒），0 表示不自动销毁。")]
    public float getHitEffectLifetime = 1.5f;

    [Header("血条 UI")]
    [Tooltip("血条 Slider，用于实时显示当前血量。拖入场景中的 Slider 组件即可。")]
    public Slider healthSlider;

    [Header("死亡")]
    [Tooltip("死亡动画 Trigger 名称，需与 Animator Controller 中的 Trigger 参数名一致")]
    public string deathTrigger = "Death";
    [Tooltip("死亡音效")]
    public AudioClip deathSound;

    [Header("视角")]
    public Transform cameraTransform;

    [Header("调试")]
    [Tooltip("在 Console 输出跳跃状态")]
    public bool debugJump = true;
    [Tooltip("在屏幕左上角显示当前速度和运动状态")]
    public bool showSpeedUI = true;
    [Tooltip("在 Console 输出攻击触发信息，便于排查按键与动画绑定问题")]
    public bool debugAttack = true;

    private Rigidbody rb;
    private CapsuleCollider capsuleCollider;
    private Animator animator;
    private Vector3 moveInput;
    private bool isGrounded;
    private float jumpCooldownTimer;
    private bool isAscending;
    private float jumpStartY;
    private float attackCooldownTimer;
    private float attackLockTimer;
    private bool isAttacking;
    private float attackStartTime;
    private float heavyAttackCooldownTimer;
    private bool isHeavyAttacking;
    private float heavyAttackStartTime;
    private bool isJumpAnimating;
    private bool jumpKeyWasPressed;
    private bool hasTriggeredLightAttackShake;
    private bool hasTriggeredHeavyAttackShake;
    private bool wasMovementLocked;
    private bool wasLightAttackAnimationPlaying;
    private bool wasHeavyAttackAnimationPlaying;
    private float lightComboTimer;
    private bool isInLightComboWindow;
    private bool hasPerformedLightCombo2;
    private int lightComboStartFrame;
    private float invincibilityTimer;
    private bool isDead;
    private float getHitTimer;
    private bool isGettingHit;
    private int getHitTriggerHash;
    private Coroutine heavyAttackDashCoroutine;
    private PlayerSkill[] skills;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int HeavyAttackHash = Animator.StringToHash("HeavyAttack");
    private static readonly int ComboHash = Animator.StringToHash("Combo");
    private int jumpTriggerHash;
    private int lightAttackStateHash;
    private int heavyAttackStateHash;
    private int lightAttackCombo2StateHash;
    private int deathTriggerHash;

    /// <summary>
    /// 轻击动画确认开始播放后触发的事件，供相机、音效、特效等系统订阅。
    /// </summary>
    public static event System.Action OnLightAttack;

    /// <summary>
    /// 重击动画确认开始播放后触发的事件，供相机、音效、特效等系统订阅。
    /// </summary>
    public static event System.Action OnHeavyAttack;

    /// <summary>
    /// 技能开始释放时触发的事件，供相机、音效、特效等系统订阅。
    /// </summary>
    public static event System.Action OnSkill;

    /// <summary>
    /// 角色受到伤害（未被无敌时间或死亡拦截）时触发，供相机、音效、特效等系统订阅。
    /// </summary>
    public static event System.Action OnTakeDamage;

    /// <summary>
    /// 生命值发生变化时触发。参数：当前生命值、最大生命值。
    /// </summary>
    public event System.Action<float, float> OnHealthChanged;

    /// <summary>
    /// 角色死亡时触发。
    /// </summary>
    public event System.Action OnDeath;

    /// <summary>
    /// 当前是否处于攻击动画播放期间，外部系统可据此禁用移动、AI、技能等。
    /// </summary>
    public bool IsMovementLocked => isGettingHit || isAttacking || isHeavyAttacking || IsAnySkillActive() || IsLightAttackAnimationPlaying() || IsHeavyAttackAnimationPlaying();

    /// <summary>
    /// 当前生命值百分比（0-1）。
    /// </summary>
    public float HealthPercent => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    /// <summary>
    /// 角色是否已死亡。
    /// </summary>
    public bool IsDead => isDead;

    /// <summary>
    /// 供 PlayerSkill 技能模块访问的 Animator 引用。
    /// </summary>
    public Animator PlayerAnimator => animator;

    /// <summary>
    /// 供 PlayerSkill 技能模块判断当前角色状态是否允许释放技能。
    /// 任一技能激活、或受击/攻击/重击期间均不允许。
    /// </summary>
    public bool CanCastSkill()
    {
        return !isDead
            && !isGettingHit
            && !isAttacking
            && !isHeavyAttacking
            && !IsLightAttackAnimationPlaying()
            && !IsHeavyAttackAnimationPlaying()
            && !IsAnySkillActive();
    }

    /// <summary>
    /// 供 PlayerSkill 技能模块生成技能伤害盒子（立即激活伤害判定）。
    /// 注意：方法名不能与 PlayerSkill 的动画事件方法 SpawnSkillAttackBox(int) 同名，
    /// 否则 Unity 动画事件会因「跨组件同名方法」冲突而隐藏后者，故此处命名为 SpawnSkillHit。
    /// </summary>
    public void SpawnSkillHit(float damage)
    {
        SpawnAttackBoxInternal(damage, true);
    }

    /// <summary>
    /// 供 PlayerSkill 技能模块在技能释放时通知订阅者（相机、音效、特效等）。
    /// 静态事件 OnSkill 只能在 Player 内部 Invoke，故提供此转发入口。
    /// </summary>
    public void NotifySkillTriggered()
    {
        OnSkill?.Invoke();
    }

    private void InitializeSkills()
    {
        skills = GetComponentsInChildren<PlayerSkill>(true);
        foreach (PlayerSkill skill in skills)
        {
            if (skill != null)
            {
                skill.Initialize(this);
            }
        }
    }

    private void UpdateSkills()
    {
        foreach (PlayerSkill skill in skills)
        {
            if (skill != null)
            {
                skill.Tick();
            }
        }
    }

    private void InterruptSkills()
    {
        foreach (PlayerSkill skill in skills)
        {
            if (skill != null)
            {
                skill.Interrupt();
            }
        }
    }

    private bool IsAnySkillActive()
    {
        foreach (PlayerSkill skill in skills)
        {
            if (skill != null && skill.IsActive)
            {
                return true;
            }
        }
        return false;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        animator = GetComponent<Animator>();
        // 如果 Animator 不在根对象上，尝试在子对象中查找（常见的美术模型层级）
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        jumpTriggerHash = Animator.StringToHash(jumpTrigger);
        lightAttackStateHash = Animator.StringToHash(lightAttackStateName);
        heavyAttackStateHash = Animator.StringToHash(heavyAttackStateName);
        lightAttackCombo2StateHash = Animator.StringToHash(lightAttackCombo2StateName);
        deathTriggerHash = Animator.StringToHash(deathTrigger);
        getHitTriggerHash = Animator.StringToHash(getHitTrigger);
        rb.freezeRotation = true;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        InitializeHealthSlider();
        ValidateAnimatorSetup();
        InitializeSkills();
    }

    private void InitializeHealthSlider()
    {
        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = maxHealth;
            healthSlider.value = currentHealth;
            OnHealthChanged += UpdateHealthSlider;
        }
    }

    private void UpdateHealthSlider(float health, float max)
    {
        if (healthSlider != null)
        {
            healthSlider.value = health;
        }
    }

    private void ValidateAnimatorSetup()
    {
        if (animator == null)
        {
            Debug.LogError("[Player] 未找到 Animator 组件。跳跃动画无法播放。请确保 Animator 在该 GameObject 或其子对象上。", this);
            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogError("[Player] Animator 的 Controller 未赋值。", this);
            return;
        }

        if (!animator.enabled)
        {
            Debug.LogWarning("[Player] Animator 组件未启用。", this);
        }

        if (!HasAnimatorParameter(jumpTriggerHash))
        {
            Debug.LogError($"[Player] Animator Controller 中缺少名为 '{jumpTrigger}' 的 Trigger 参数。跳跃动画将无法通过 Trigger 触发。", this);
        }

        if (!HasAnimatorParameter(ComboHash))
        {
            Debug.LogWarning($"[Player] Animator Controller 中缺少名为 'Combo' 的 Trigger 参数。第二段轻击连击将无法触发。", this);
        }
    }

    public bool HasAnimatorParameter(int hash)
    {
        if (animator == null)
        {
            return false;
        }

        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.nameHash == hash)
            {
                return true;
            }
        }
        return false;
    }

    void Update()
    {
        if (isDead)
        {
            return;
        }

        // 保底检测：血量被 Inspector 直接设为 0 时，延迟触发死亡
        if (currentHealth <= 0f && !isDead)
        {
            isDead = true;
            StartCoroutine(TriggerDeathDelayed());
            return;
        }

        GatherInput();
        UpdateJumpCooldown();
        UpdateAttackCooldown();
        UpdateAttackLock();
        UpdateHeavyAttackCooldown();
        UpdateLightComboTimer();
        UpdateInvincibilityTimer();
        UpdateGetHitTimer();
        CheckGround();
        HandleJump();
        HandleAttack();
        HandleLightCombo();
        HandleHeavyAttack();
        UpdateSkills();
        UpdateAnimator();
    }

    void FixedUpdate()
    {
        if (isDead)
        {
            return;
        }

        Move();
        ApplyJumpSpeedBoost();
        ApplyFallSpeed();
    }

    private void GatherInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        if (cameraTransform != null)
        {
            Vector3 forward = Vector3.Scale(cameraTransform.forward, new Vector3(1f, 0f, 1f)).normalized;
            Vector3 right = Vector3.Scale(cameraTransform.right, new Vector3(1f, 0f, 1f)).normalized;
            moveInput = (forward * v + right * h).normalized;
        }
        else
        {
            moveInput = new Vector3(h, 0f, v).normalized;
        }
    }

    private void UpdateJumpCooldown()
    {
        if (jumpCooldownTimer > 0f)
        {
            jumpCooldownTimer -= Time.deltaTime;
            if (jumpCooldownTimer < 0f)
            {
                jumpCooldownTimer = 0f;
            }
        }
    }

    private void UpdateAttackCooldown()
    {
        if (attackCooldownTimer > 0f)
        {
            attackCooldownTimer -= Time.deltaTime;
            if (attackCooldownTimer < 0f)
            {
                attackCooldownTimer = 0f;
            }
        }
    }

    private void UpdateAttackLock()
    {
        if (attackLockTimer > 0f)
        {
            attackLockTimer -= Time.deltaTime;
            if (attackLockTimer <= 0f)
            {
                EndAttack();
            }
        }
    }

    private void UpdateHeavyAttackCooldown()
    {
        if (heavyAttackCooldownTimer > 0f)
        {
            heavyAttackCooldownTimer -= Time.deltaTime;
            if (heavyAttackCooldownTimer < 0f)
            {
                heavyAttackCooldownTimer = 0f;
            }
        }
    }

    private void UpdateLightComboTimer()
    {
        if (lightComboTimer > 0f)
        {
            lightComboTimer -= Time.deltaTime;
            if (lightComboTimer <= 0f)
            {
                lightComboTimer = 0f;
                isInLightComboWindow = false;
                hasPerformedLightCombo2 = false;
                // 窗口期内输入了第二段但动画未触发时，残留的 Combo Trigger 需要清理，避免影响后续攻击。
                if (animator != null)
                {
                    animator.ResetTrigger(ComboHash);
                }
            }
        }
    }

    private void UpdateInvincibilityTimer()
    {
        if (invincibilityTimer > 0f)
        {
            invincibilityTimer -= Time.deltaTime;
            if (invincibilityTimer < 0f)
            {
                invincibilityTimer = 0f;
            }
        }
    }

    /// <summary>
    /// 受到伤害。已死亡或处于无敌时间时忽略。
    /// </summary>
    /// <param name="damage">伤害值</param>
    public void TakeDamage(float damage)
    {
        if (isDead || invincibilityTimer > 0f || damage <= 0f)
        {
            return;
        }

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0f)
        {
            isDead = true;
            StartCoroutine(TriggerDeathDelayed());
            OnDeath?.Invoke();
        }
        else
        {
            invincibilityTimer = invincibilityDuration;
            EnterGetHit();
            OnTakeDamage?.Invoke();
        }
    }

    /// <summary>
    /// 进入受击状态：打断当前攻击/重击/连击/跳跃助推，锁定移动与攻击，并触发受击动画。
    /// 期间只保留基础物理碰撞（重力、碰撞体），直到 getHitDuration 结束。
    /// </summary>
    private void EnterGetHit()
    {
        isGettingHit = true;
        getHitTimer = getHitDuration;

        // 打断攻击与重击逻辑锁定
        isAttacking = false;
        isHeavyAttacking = false;
        hasTriggeredLightAttackShake = false;
        hasTriggeredHeavyAttackShake = false;
        isInLightComboWindow = false;
        hasPerformedLightCombo2 = false;
        lightComboTimer = 0f;
        isAscending = false;

        // 打断正在进行的重击位移
        if (heavyAttackDashCoroutine != null)
        {
            StopCoroutine(heavyAttackDashCoroutine);
            heavyAttackDashCoroutine = null;
        }

        // 打断正在进行的技能释放
        InterruptSkills();

        // 清除攻击相关触发器，并触发受击动画
        if (animator != null)
        {
            animator.ResetTrigger(AttackHash);
            animator.ResetTrigger(ComboHash);
            animator.ResetTrigger(HeavyAttackHash);
            if (HasAnimatorParameter(getHitTriggerHash))
            {
                animator.ResetTrigger(getHitTriggerHash);
                animator.SetTrigger(getHitTriggerHash);
            }
        }

        // 清除水平速度，仅保留垂直速度，让重力自然作用
        if (rb != null)
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
        }

        // 在挂点处生成全部受击特效
        if (getHitEffectPrefabs == null || getHitEffectPrefabs.Length == 0)
        {
            Debug.LogWarning("[Player] 受击特效预制体列表为空，请在弹出的 Player 组件「受击」分组中给 Get Hit Effect Prefabs 拖入特效预制体。", this);
        }
        else
        {
            if (getHitEffectSpawnPoint == null)
            {
                Debug.LogWarning("[Player] 未设置受击特效挂点，特效将在玩家自身位置生成。", this);
            }

            Vector3 spawnPos = getHitEffectSpawnPoint != null ? getHitEffectSpawnPoint.position : transform.position;
            Quaternion spawnRot = getHitEffectSpawnPoint != null ? getHitEffectSpawnPoint.rotation : transform.rotation;

            for (int i = 0; i < getHitEffectPrefabs.Length; i++)
            {
                if (getHitEffectPrefabs[i] == null)
                {
                    Debug.LogWarning($"[Player] Get Hit Effect Prefabs[{i}] 为空，已跳过。", this);
                    continue;
                }

                GameObject fx = Instantiate(getHitEffectPrefabs[i], spawnPos, spawnRot);
                ParticleSystem[] parts = fx.GetComponentsInChildren<ParticleSystem>();
                for (int j = 0; j < parts.Length; j++)
                {
                    parts[j].Play();
                }

                if (getHitEffectLifetime > 0f)
                {
                    Destroy(fx, getHitEffectLifetime);
                }

                Debug.Log($"[Player] 生成受击特效[{i}]: {fx.name} @ {spawnPos}");
            }
        }
    }

    /// <summary>
    /// 受击硬直计时。计时结束后解除受击锁定，恢复移动与攻击。
    /// </summary>
    private void UpdateGetHitTimer()
    {
        if (!isGettingHit)
        {
            return;
        }

        getHitTimer -= Time.deltaTime;
        if (getHitTimer <= 0f)
        {
            getHitTimer = 0f;
            isGettingHit = false;
            if (animator != null && HasAnimatorParameter(getHitTriggerHash))
            {
                animator.ResetTrigger(getHitTriggerHash);
            }
        }
    }

    /// <summary>
    /// 恢复生命值。
    /// </summary>
    /// <param name="amount">恢复量</param>
    public void Heal(float amount)
    {
        if (isDead || amount <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    [ContextMenu("Kill")]
    public void Kill()
    {
        if (isDead) return;
        currentHealth = 0f;
        isDead = true;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        StartCoroutine(TriggerDeathDelayed());
        OnDeath?.Invoke();
    }

    private IEnumerator TriggerDeathDelayed()
    {
        // 等待一帧，确保 Animator 已完全初始化
        yield return null;

        if (animator != null && HasAnimatorParameter(deathTriggerHash))
        {
            animator.ResetTrigger(deathTriggerHash);
            animator.SetTrigger(deathTriggerHash);
        }
        else if (animator != null)
        {
            Debug.LogError($"[Player] Animator Controller 中缺少名为 '{deathTrigger}' 的 Trigger 参数，死亡动画无法播放。", this);
        }

        // 播放死亡音效
        if (deathSound != null)
        {
            AudioSource.PlayClipAtPoint(deathSound, transform.position);
        }

        // 死亡后清除水平速度，保留垂直速度让重力自然拉回地面
        rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
        // 保持非 Kinematic 和碰撞体启用，让尸体受重力下落并与地面碰撞
    }

    private void EndAttack()
    {
        // isAttacking 由 UpdateAnimator 根据 Animator 实际状态同步。
        // 若动画最终没有播放出来（例如状态名或 Trigger 不匹配），清理残留 Trigger，避免影响下一次输入。
        isAttacking = false;
        if (animator != null && !IsLightAttackAnimationPlaying())
        {
            animator.ResetTrigger(AttackHash);
            animator.ResetTrigger(ComboHash);
        }
    }

    private void CheckGround()
    {
        bool wasGrounded = isGrounded;

        // 起跳后的冷却窗口内，强制视为未落地。这是防止多段跳的关键。
        if (jumpCooldownTimer > 0f)
        {
            isGrounded = false;
        }
        else
        {
            Vector3 checkPos = GetGroundCheckPosition();
            isGrounded = Physics.CheckSphere(checkPos, groundCheckRadius, groundLayer, QueryTriggerInteraction.Ignore);
        }

        if (debugJump && wasGrounded != isGrounded)
        {
            Debug.Log($"[Player] Grounded changed: {wasGrounded} -> {isGrounded}");
        }

        // 着陆瞬间重置跳跃触发器，为下一次跳跃做准备。这是修复第二次跳跃动画不触发的重要逻辑。
        if (wasGrounded != isGrounded && isGrounded)
        {
            isJumpAnimating = false;
            if (animator != null)
            {
                animator.ResetTrigger(jumpTriggerHash);
            }

            if (debugJump)
            {
                Debug.Log("[Player] Jump trigger reset on landing.");
            }
        }
    }

    private Vector3 GetGroundCheckPosition()
    {
        // 从胶囊体真实底部向上偏移一点，避免嵌入地面
        float bottomY = capsuleCollider.bounds.min.y;
        return new Vector3(transform.position.x, bottomY + groundCheckOffsetY, transform.position.z);
    }

    private void HandleJump()
    {
        // 手动检测按键边沿，防止窗口焦点切换时遗留的按键状态导致误触发
        bool jumpKeyIsPressed = Input.GetKey(jumpKey);
        bool jumpKeyPressedThisFrame = jumpKeyIsPressed && !jumpKeyWasPressed;
        jumpKeyWasPressed = jumpKeyIsPressed;

        // 游戏窗口未聚焦时不响应跳跃输入，避免 Play Focused 模式下的自发触发
        if (jumpCooldownTimer > 0f || isJumpAnimating || isGettingHit || !Application.isFocused)
        {
            return;
        }

        if (debugJump && jumpKeyPressedThisFrame)
        {
            Debug.Log($"[Player] Jump input detected | Key={jumpKey} | Grounded={isGrounded} | Focused={Application.isFocused}");
        }

        if (isGrounded && jumpKeyPressedThisFrame)
        {
            jumpCooldownTimer = jumpCooldown;
            isGrounded = false;
            isAscending = true;
            jumpStartY = transform.position.y;

            // 跳跃高度由 jumpHeight 独立决定，jumpSpeed 仅影响上升阶段的手感
            float baseVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
            rb.velocity = new Vector3(rb.velocity.x, baseVelocity, rb.velocity.z);

            // 触发跳跃动画
            isJumpAnimating = true;
            TriggerJumpAnimation();

            if (debugJump)
            {
                Debug.Log($"[Player] Jump triggered. TargetHeight={jumpHeight:F2}, BaseVelocity={baseVelocity:F2}, JumpSpeed={jumpSpeed:F2}");
            }
        }
    }

    private void TriggerJumpAnimation()
    {
        if (animator == null)
        {
            return;
        }

        if (!HasAnimatorParameter(jumpTriggerHash))
        {
            Debug.LogError($"[Player] 无法触发跳跃动画：Animator Controller 中不存在 '{jumpTrigger}' Trigger 参数。", this);
            return;
        }

        animator.ResetTrigger(jumpTriggerHash);
        animator.SetTrigger(jumpTriggerHash);
        StartCoroutine(VerifyJumpAnimationState());
    }

    private IEnumerator VerifyJumpAnimationState()
    {
        // 等待 Animator 在下一帧评估状态机
        yield return null;

        if (animator == null)
        {
            yield break;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool isInJumpState = stateInfo.shortNameHash == jumpTriggerHash;

        if (debugJump)
        {
            Debug.Log($"[Player] Animator state after jump trigger: hash={stateInfo.shortNameHash}, IsJumpState={isInJumpState}, normalizedTime={stateInfo.normalizedTime:F3}");
        }

        if (!isInJumpState)
        {
            Debug.LogWarning($"[Player] 跳跃 Trigger 已设置，但 Animator 未进入 Jump 状态。当前状态 hash={stateInfo.shortNameHash}。请检查 Animator Controller 中是否存在从当前状态到 Jump 状态的过渡，且过渡条件为 '{jumpTrigger}' Trigger。", this);
        }
    }

    /// <summary>
    /// 从音效列表中随机播放一段音效。列表为空或全部为空时不播放。
    /// </summary>
    private void PlayRandomClip(AudioClip[] clips, Vector3 position, float volume = 1f)
    {
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip != null)
        {
            AudioSource.PlayClipAtPoint(clip, position, volume);
        }
    }

    private void HandleAttack()
    {
        bool inputPressed = Input.GetKeyDown(attackKey);

        // 冷却、锁定、受击或任意攻击动画仍在播放时，忽略新的轻击输入
        if (attackCooldownTimer > 0f || isAttacking || isHeavyAttacking || IsAnySkillActive() || isGettingHit || IsLightAttackAnimationPlaying() || IsHeavyAttackAnimationPlaying())
        {
            if (debugAttack && inputPressed)
            {
                Debug.Log($"[Player] 轻击输入被忽略 | Key={attackKey} | Cooldown={attackCooldownTimer:F2} | isAttacking={isAttacking} | isHeavyAttacking={isHeavyAttacking} | isGettingHit={isGettingHit} | IsLightPlaying={IsLightAttackAnimationPlaying()} | IsHeavyPlaying={IsHeavyAttackAnimationPlaying()}");
            }
            return;
        }

        if (inputPressed)
        {
            attackCooldownTimer = attackCooldown;
            attackLockTimer = attackLockDuration;
            isAttacking = true;
            attackStartTime = Time.time;
            isInLightComboWindow = true;
            lightComboTimer = lightComboWindow;
            lightComboStartFrame = Time.frameCount;
            hasPerformedLightCombo2 = false;
            if (animator != null)
            {
                animator.ResetTrigger(AttackHash);
                animator.SetTrigger(AttackHash);
                // 清除可能残留的 Combo Trigger，避免第一段开始时立即进入第二段
                animator.ResetTrigger(ComboHash);
            }

            // 听觉反馈（仅在地面时播放，跳跃中不播放）
            if (isGrounded)
            {
                PlayRandomClip(lightAttackSounds, transform.position);
            }

            // 根据配置决定是否验证动画状态后再通知订阅者
            if (verifyLightAttackAnimation)
            {
                StartCoroutine(VerifyLightAttackAnimationStarted(lightAttackStateName, lightAttackStateHash, false));
            }
            else
            {
                OnLightAttack?.Invoke();
            }

            if (debugAttack)
            {
                Debug.Log($"[Player] 轻击触发 | Key={attackKey} | Trigger=Attack | State={lightAttackStateName} | Verify={verifyLightAttackAnimation}");
            }
        }
    }

    /// <summary>
    /// 获取当前第一段轻击动画的播放进度（0-1）。
    /// 如果 Animator 当前不在第一段轻击状态，则返回 0。
    /// </summary>
    private float GetCurrentLightAttackProgress()
    {
        if (animator == null)
        {
            return 0f;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        if (stateInfo.shortNameHash == lightAttackStateHash)
        {
            // 非循环动画的 normalizedTime 可能大于 1，使用 Clamp01 保证阈值判断稳定
            return Mathf.Clamp01(stateInfo.normalizedTime);
        }

        return 0f;
    }

    /// <summary>
    /// 在第一段轻击后的输入窗口内，检测第二段轻击输入并触发 attack_05。
    /// 使用独立的 Combo Trigger，避免与第一段 Attack Trigger 在状态机中竞争。
    /// </summary>
    private void HandleLightCombo()
    {
        if (!isInLightComboWindow || hasPerformedLightCombo2 || isHeavyAttacking || IsAnySkillActive() || isGettingHit || IsHeavyAttackAnimationPlaying())
        {
            return;
        }

        // 忽略与第一段输入同一帧的按键事件，防止 Input.GetKeyDown 在同一帧内被重复消费。
        if (Time.frameCount == lightComboStartFrame)
        {
            return;
        }

        bool inputPressed = Input.GetKeyDown(attackKey);

        // 只有第一段动画播放到阈值后才允许触发第二段
        float progress = GetCurrentLightAttackProgress();
        if (progress < combo2ProgressThreshold)
        {
            if (debugAttack && inputPressed)
            {
                Debug.Log($"[Player] 第二段轻击输入被忽略 | 第一段动画进度 {progress:F2} 未到达阈值 {combo2ProgressThreshold:F2}");
            }
            return;
        }

        if (inputPressed)
        {
            hasPerformedLightCombo2 = true;
            // 允许第二段动画再次触发视角晃动
            hasTriggeredLightAttackShake = false;
            if (animator != null)
            {
                animator.ResetTrigger(ComboHash);
                animator.SetTrigger(ComboHash);
            }

            // 第二段听觉反馈（仅在地面时播放，跳跃中不播放）
            if (isGrounded)
            {
                PlayRandomClip(lightCombo2Sounds, transform.position);
            }

            if (verifyLightAttackAnimation)
            {
                StartCoroutine(VerifyLightAttackAnimationStarted(lightAttackCombo2StateName, lightAttackCombo2StateHash, true));
            }
            else
            {
                OnLightAttack?.Invoke();
            }

            if (debugAttack)
            {
                Debug.Log($"[Player] 第二段轻击触发 | Key={attackKey} | State={lightAttackCombo2StateName} | Trigger=Combo | Progress={progress:F2} | Verify={verifyLightAttackAnimation}");
            }
        }
    }

    private IEnumerator VerifyLightAttackAnimationStarted(string targetStateName, int targetStateHash, bool isCombo)
    {
        float elapsed = 0f;
        const float maxWait = 0.3f;
        int frameCount = 0;
        string prefix = isCombo ? "第二段轻击" : "轻击";

        while (elapsed < maxWait)
        {
            yield return null;
            elapsed += Time.deltaTime;
            frameCount++;

            if (animator == null)
            {
                yield break;
            }

            AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);

            if (debugAttack)
            {
                Debug.Log($"[Player] {prefix}动画验证 | Frame={frameCount} | CurrentHash={currentState.shortNameHash} | NextHash={nextState.shortNameHash} | TargetHash={targetStateHash} | TargetName={targetStateName}");
            }

            // 当前已处于目标状态，或正在过渡进入目标状态，都视为动画成功触发
            if (currentState.shortNameHash == targetStateHash || nextState.shortNameHash == targetStateHash)
            {
                // 单次动画周期内只允许触发一次视角晃动
                if (hasTriggeredLightAttackShake)
                {
                    if (debugAttack)
                    {
                        Debug.Log($"[Player] {prefix}动画仍在播放，跳过重复晃动触发 | State={targetStateName}");
                    }

                    yield break;
                }

                hasTriggeredLightAttackShake = true;

                if (debugAttack)
                {
                    Debug.Log($"[Player] {prefix}动画确认成功，触发视角晃动 | State={targetStateName}");
                }

                OnLightAttack?.Invoke();
                yield break;
            }
        }

        if (debugAttack)
        {
            Debug.LogWarning($"[Player] {prefix} Trigger 已设置，但在 {maxWait}s 内 Animator 未进入 '{targetStateName}' 状态，不触发视角晃动。请检查 Inspector 中的 {(isCombo ? "Light Attack Combo2 State Name" : "Light Attack State Name")} 是否与 Animator Controller 中的状态名完全一致（区分大小写）。", this);
        }
    }

    private IEnumerator VerifyHeavyAttackAnimationStarted()
    {
        float elapsed = 0f;
        const float maxWait = 0.3f;
        int frameCount = 0;

        if (debugAttack)
        {
            Debug.Log($"[Player] 开始重击动画验证 | TargetName={heavyAttackStateName} | TargetHash={heavyAttackStateHash}");
        }

        while (elapsed < maxWait)
        {
            yield return null;
            elapsed += Time.deltaTime;
            frameCount++;

            if (animator == null)
            {
                yield break;
            }

            AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);

            if (debugAttack)
            {
                Debug.Log($"[Player] 重击动画验证 | Frame={frameCount} | CurrentHash={currentState.shortNameHash} | NextHash={nextState.shortNameHash} | TargetHash={heavyAttackStateHash} | TargetName={heavyAttackStateName}");
            }

            if (currentState.shortNameHash == heavyAttackStateHash || nextState.shortNameHash == heavyAttackStateHash)
            {
                if (hasTriggeredHeavyAttackShake)
                {
                    if (debugAttack)
                    {
                        Debug.Log($"[Player] 重击动画仍在播放，跳过重复晃动触发 | State={heavyAttackStateName}");
                    }

                    yield break;
                }

                hasTriggeredHeavyAttackShake = true;

                if (debugAttack)
                {
                    Debug.Log($"[Player] 重击动画确认成功，触发视角晃动 | State={heavyAttackStateName}");
                }

                OnHeavyAttack?.Invoke();
                yield break;
            }
        }

        if (debugAttack)
        {
            Debug.LogWarning($"[Player] 重击 Trigger 已设置，但在 {maxWait}s 内 Animator 未进入 '{heavyAttackStateName}' 状态，不触发视角晃动。请检查 Inspector 中的 Heavy Attack State Name 是否与 Animator Controller 中的状态名完全一致（区分大小写）。", this);
        }
    }

    private void HandleHeavyAttack()
    {
        if (heavyAttackCooldownTimer > 0f || isAttacking || isHeavyAttacking || IsAnySkillActive() || isGettingHit || IsHeavyAttackAnimationPlaying() || IsLightAttackAnimationPlaying())
        {
            return;
        }

        if (Input.GetKeyDown(heavyAttackKey))
        {
            heavyAttackCooldownTimer = heavyAttackCooldown;
            isHeavyAttacking = true;
            if (animator != null)
            {
                animator.ResetTrigger(HeavyAttackHash);
                animator.SetTrigger(HeavyAttackHash);
            }
            heavyAttackDashCoroutine = StartCoroutine(HeavyAttackDashCoroutine());

            if (verifyHeavyAttackAnimation)
            {
                StartCoroutine(VerifyHeavyAttackAnimationStarted());
            }
            else
            {
                OnHeavyAttack?.Invoke();
            }

            if (debugAttack)
            {
                Debug.Log($"[Player] 重击触发 | Key={heavyAttackKey} | Trigger=HeavyAttack | Verify={verifyHeavyAttackAnimation}");
            }
        }
    }

    private IEnumerator HeavyAttackDashCoroutine()
    {
        float elapsed = 0f;
        Vector3 direction = transform.forward;
        float distance = heavyAttackDashDistance;
        float duration = heavyAttackDashDuration;
        float dashSpeed = distance / duration;

        // 听觉反馈（仅在地面时播放，跳跃中不播放）
        if (isGrounded && heavyAttackSound != null)
        {
            AudioSource.PlayClipAtPoint(heavyAttackSound, transform.position);
        }

        // 视觉反馈
        if (heavyAttackEffect != null)
        {
            heavyAttackEffect.Play();
        }

        while (elapsed < duration)
        {
            float dt = Time.deltaTime;
            float step = dashSpeed * dt;

            // 边缘碰撞检测：检测前方是否有障碍物
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, step + 0.1f, obstacleLayer, QueryTriggerInteraction.Ignore))
            {
                break;
            }

            // 使用 Rigidbody 移动，保证与物理系统兼容
            rb.MovePosition(rb.position + direction * step);

            elapsed += dt;
            yield return null;
        }

        EndHeavyAttack();
    }

    private void EndHeavyAttack()
    {
        isHeavyAttacking = false;
        if (animator != null)
        {
            animator.ResetTrigger(HeavyAttackHash);
        }
    }

    private void ApplyJumpSpeedBoost()
    {
        if (!isAscending || rb.velocity.y <= 0f)
        {
            isAscending = false;
            return;
        }

        float currentHeight = transform.position.y - jumpStartY;

        // 达到或接近目标高度后停止辅助推进，让重力自然拉回，确保最高点稳定
        if (currentHeight >= jumpHeight * 0.99f)
        {
            isAscending = false;
            return;
        }

        float boost = (jumpSpeed - 1f) * ascentBoostMultiplier;
        if (boost <= 0f)
        {
            return;
        }

        // 越接近目标高度，辅助力越小，避免 overshoot
        float heightRatio = 1f - Mathf.Clamp01(currentHeight / jumpHeight);
        Vector3 boostForce = Vector3.up * boost * heightRatio;
        rb.AddForce(boostForce, ForceMode.Acceleration);
    }

    private void Move()
    {
        // 攻击动画播放期间禁用玩家移动控制，但保留垂直速度（重力/跳跃）
        if (IsMovementLocked)
        {
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
            return;
        }

        // 根据是否在空中选择基础水平速度
        float baseSpeed = isGrounded ? moveSpeed : jumpMovementSpeed;

        // 按住奔跑键时提升速度
        bool isRunning = Input.GetKey(runKey);
        float targetSpeed = isRunning ? baseSpeed * runMultiplier : baseSpeed;

        if (moveInput.sqrMagnitude > 0.01f)
        {
            // 直接设置目标速度，确保按键响应即时、无启动延迟
            Vector3 targetVelocity = moveInput * targetSpeed;
            rb.velocity = new Vector3(targetVelocity.x, rb.velocity.y, targetVelocity.z);

            Quaternion targetRotation = Quaternion.LookRotation(moveInput);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }
        else
        {
            // 松键后立即停止水平移动，无惯性
            rb.velocity = new Vector3(0f, rb.velocity.y, 0f);
        }
    }

    private void UpdateAnimator()
    {
        if (animator == null)
        {
            return;
        }

        // 查询 Animator 实际播放状态，用于判断动画生命周期，避免与逻辑锁定标记混淆
        bool lightAttackPlaying = IsLightAttackAnimationPlaying();
        bool heavyAttackPlaying = IsHeavyAttackAnimationPlaying();

        // 输出移动锁定状态变化，便于验证攻击期间移动是否被正确禁用/恢复
        bool isMovementLocked = IsMovementLocked;
        if (isMovementLocked != wasMovementLocked)
        {
            if (debugAttack)
            {
                Debug.Log($"[Player] 移动锁定状态变化: {wasMovementLocked} -> {isMovementLocked}");
            }

            wasMovementLocked = isMovementLocked;
        }

        // 始终更新地面状态，供动画状态机判断是否在地面
        animator.SetBool(IsGroundedHash, isGrounded);

        // 轻击动画真正开始播放
        if (!wasLightAttackAnimationPlaying && lightAttackPlaying)
        {
            if (debugAttack)
            {
                Debug.Log($"[Player] 轻击动画进入播放状态 | State={lightAttackStateName} 或 {lightAttackCombo2StateName}");
            }
        }

        // 轻击动画真正结束后，解除逻辑锁定并清理本次周期的晃动标记和触发器
        if (wasLightAttackAnimationPlaying && !lightAttackPlaying)
        {
            isAttacking = false;
            hasTriggeredLightAttackShake = false;
            animator.ResetTrigger(AttackHash);
            animator.ResetTrigger(ComboHash);

            if (debugAttack)
            {
                Debug.Log("[Player] 轻击动画已结束，解除攻击锁定并重置晃动标记与触发器。");
            }
        }

        // 重击动画真正开始播放
        if (!wasHeavyAttackAnimationPlaying && heavyAttackPlaying)
        {
            if (debugAttack)
            {
                Debug.Log($"[Player] 重击动画进入播放状态 | State={heavyAttackStateName}");
            }
        }

        // 重击动画真正结束后，解除逻辑锁定并清理本次周期的晃动标记
        if (wasHeavyAttackAnimationPlaying && !heavyAttackPlaying)
        {
            isHeavyAttacking = false;
            hasTriggeredHeavyAttackShake = false;

            if (debugAttack)
            {
                Debug.Log("[Player] 重击动画已结束，解除攻击锁定并重置晃动标记。");
            }
        }

        wasLightAttackAnimationPlaying = lightAttackPlaying;
        wasHeavyAttackAnimationPlaying = heavyAttackPlaying;

        // 第二段连击已经执行且整个轻击动画序列结束时，关闭连击窗口
        if (hasPerformedLightCombo2 && !lightAttackPlaying)
        {
            isInLightComboWindow = false;
            hasPerformedLightCombo2 = false;
            lightComboTimer = 0f;
        }

        // 攻击/重击动画播放或逻辑锁定期间，暂停更新移动相关参数，避免 Idle/Walk/Run 条件干扰攻击状态
        if (IsMovementLocked)
        {
            return;
        }

        // 没有方向输入时强制 Speed 为 0，避免攻击结束后因刚体残留速度误切入 Walk
        float horizontalSpeed = 0f;
        bool isRunning = false;
        if (moveInput.sqrMagnitude > 0.01f)
        {
            horizontalSpeed = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
            isRunning = Input.GetKey(runKey);
        }

        animator.SetFloat(SpeedHash, horizontalSpeed);
        animator.SetBool(IsRunningHash, isRunning);
    }

    /// <summary>
    /// 检查轻击动画（包括第一段和第二段连击）是否正在播放或正在过渡进入/离开。
    /// </summary>
    private bool IsLightAttackAnimationPlaying()
    {
        if (animator == null)
        {
            return false;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        int currentHash = currentState.shortNameHash;
        int nextHash = nextState.shortNameHash;
        return currentHash == lightAttackStateHash || nextHash == lightAttackStateHash
            || currentHash == lightAttackCombo2StateHash || nextHash == lightAttackCombo2StateHash;
    }

    /// <summary>
    /// 检查重击动画是否正在播放或正在过渡进入/离开重击状态
    /// </summary>
    private bool IsHeavyAttackAnimationPlaying()
    {
        if (animator == null)
        {
            return false;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        return currentState.shortNameHash == heavyAttackStateHash || nextState.shortNameHash == heavyAttackStateHash;
    }

    private void ApplyFallSpeed()
    {
        if (fallMultiplier <= 1f)
        {
            return;
        }

        if (rb.velocity.y < 0)
        {
            rb.velocity += (fallMultiplier - 1f) * Physics.gravity * Time.fixedDeltaTime;
        }
    }

    void OnValidate()
    {
        // 边界检查
        jumpSpeed = Mathf.Clamp(jumpSpeed, 0.5f, 2f);
        runMultiplier = Mathf.Clamp(runMultiplier, 1.5f, 2f);
        attackCooldown = Mathf.Clamp(attackCooldown, 0.5f, 1f);
        attackLockDuration = Mathf.Clamp(attackLockDuration, 0.3f, 1f);
        heavyAttackCooldown = Mathf.Max(heavyAttackCooldown, 0.1f);
        heavyAttackDashDuration = Mathf.Max(heavyAttackDashDuration, 0.1f);
        heavyAttackDashDistance = Mathf.Clamp(heavyAttackDashDistance, 0f, 2f);
        heavyAttackDashDistance = Mathf.Round(heavyAttackDashDistance * 10f) / 10f;
        jumpMovementSpeed = Mathf.Max(jumpMovementSpeed, 0f);
        ascentBoostMultiplier = Mathf.Max(ascentBoostMultiplier, 0f);
        fallMultiplier = Mathf.Max(fallMultiplier, 0f);
        jumpCooldown = Mathf.Max(jumpCooldown, 0f);
        jumpHeight = Mathf.Max(jumpHeight, 0.01f);
        lightComboWindow = Mathf.Max(lightComboWindow, 0.05f);
        combo2ProgressThreshold = Mathf.Clamp01(combo2ProgressThreshold);
    }

    void OnGUI()
    {
        if (!showSpeedUI || rb == null)
        {
            return;
        }

        float horizontalSpeed = new Vector3(rb.velocity.x, 0f, rb.velocity.z).magnitude;
        bool isRunning = Input.GetKey(runKey) && moveInput.sqrMagnitude > 0.01f;
        string state = horizontalSpeed < 0.1f ? "Idle" : (isRunning ? "Running" : "Walking");
        GUI.Label(new Rect(10, 10, 240, 30), $"Speed: {horizontalSpeed:F1} [{state}]");

        if (showHealthUI)
        {
            string hpText = isDead ? "DEAD" : $"HP: {currentHealth:F0} / {maxHealth:F0}";
            GUI.Label(new Rect(10, 40, 240, 30), hpText);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (capsuleCollider == null)
        {
            return;
        }

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(GetGroundCheckPosition(), groundCheckRadius);
    }

    /// <summary>
    /// 生成攻击盒子的公共逻辑。由各动画事件入口方法调用，传入对应伤害值。
    /// </summary>
    private void SpawnAttackBoxInternal(float damage, bool enableDamageImmediately = false)
    {
        Debug.Log($"[Player] SpawnAttackBox 被调用 | prefab={attackBoxPrefab != null} | spawnPoint={attackBoxSpawnPoint != null} | damage={damage}");

        if (attackBoxPrefab == null)
        {
            Debug.LogWarning("[Player] attackBoxPrefab 未设置，无法生成攻击盒子。");
            return;
        }
        if (attackBoxSpawnPoint == null)
        {
            Debug.LogWarning("[Player] attackBoxSpawnPoint 未设置，无法生成攻击盒子。");
            return;
        }

        GameObject box = Instantiate(attackBoxPrefab, attackBoxSpawnPoint.position, attackBoxSpawnPoint.rotation, attackBoxSpawnPoint);
        currentAttackTrigger = box.GetComponent<AttackTrigger>();
        if (currentAttackTrigger != null)
        {
            currentAttackTrigger.damage = damage;
            if (enableDamageImmediately)
            {
                currentAttackTrigger.EnableDamage();
            }
            else
            {
                currentAttackTrigger.DisableDamage();
            }
        }
        Debug.Log($"[Player] 攻击盒子已生成（伤害未激活，伤害={damage}）: {box.name}");

        if (attackBoxLifetime > 0f)
        {
            Destroy(box, attackBoxLifetime);
        }
    }

    /// <summary>
    /// 动画事件：生成第一段轻击的攻击盒子（伤害=lightAttack1Damage）。
    /// 在 attack_02 动画的挥出中段添加此 Event。
    /// </summary>
    public void SpawnAttackBox()
    {
        SpawnAttackBoxInternal(lightAttack1Damage);
    }

    /// <summary>
    /// 动画事件：生成第二段连击的攻击盒子（伤害=lightAttack2Damage）。
    /// 在 attack_05 动画的挥出中段添加此 Event。
    /// </summary>
    public void SpawnLightComboAttackBox()
    {
        SpawnAttackBoxInternal(lightAttack2Damage);
    }

    /// <summary>
    /// 动画事件：生成重击的攻击盒子（伤害=heavyAttackDamage）。
    /// 在 attack_01 动画的挥出中段添加此 Event。
    /// </summary>
    public void SpawnHeavyAttackBox()
    {
        SpawnAttackBoxInternal(heavyAttackDamage);
    }

    /// <summary>
    /// 动画事件：激活当前攻击盒子的伤害判定。
    /// 需要在 SpawnAttackBox() 之后、攻击动画的关键命中帧上添加此 Event。
    /// </summary>
    public void ActivateAttackBox()
    {
        if (currentAttackTrigger != null)
        {
            currentAttackTrigger.EnableDamage();
            Debug.Log($"[Player] ActivateAttackBox 被调用，攻击盒子伤害已激活");
        }
        else
        {
            Debug.LogWarning("[Player] ActivateAttackBox 被调用，但没有当前攻击盒子，请确保 SpawnAttackBox 先于 ActivateAttackBox 执行");
        }
    }
}