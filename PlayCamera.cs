using UnityEngine;

public class PlayCamera : MonoBehaviour
{
    [Header("目标")]
    [Tooltip("相机跟随的目标，通常为主角")]
    public Transform target;

    [Header("距离与高度")]
    [Tooltip("相机与目标的水平距离")]
    public float distance = 5f;
    [Tooltip("目标上方的高度偏移")]
    public float height = 2f;
    [Tooltip("最小缩放距离")]
    public float minDistance = 2f;
    [Tooltip("最大缩放距离")]
    public float maxDistance = 12f;

    [Header("旋转")]
    [Tooltip("水平旋转速度")]
    public float horizontalSpeed = 120f;
    [Tooltip("垂直旋转速度")]
    public float verticalSpeed = 80f;
    [Tooltip("最小垂直俯角")]
    public float minVerticalAngle = -30f;
    [Tooltip("最大垂直仰角")]
    public float maxVerticalAngle = 60f;

    [Header("平滑")]
    [Tooltip("位置跟随平滑系数")]
    public float positionSmooth = 8f;
    [Tooltip("旋转看向平滑系数")]
    public float lookSmooth = 10f;

    [Header("碰撞")]
    [Tooltip("是否启用相机与障碍物的碰撞检测")]
    public bool enableCollision = true;
    [Tooltip("碰撞检测层级")]
    public LayerMask collisionLayer = ~0;

    [Header("轻击视角晃动")]
    [Tooltip("轻击时相机水平方向（左右）晃动的最大幅度（度）。正值越大，从左下角到右上角的水平位移越明显。")]
    public float lightAttackShakeYawAmplitude = 14f;
    [Tooltip("轻击时相机垂直方向（上下）晃动的最大幅度（度）。正值越大，从左下角到右上角的垂直位移越明显。")]
    public float lightAttackShakePitchAmplitude = 10f;
    [Tooltip("轻击晃动的总时长（秒）。值越小晃动越快。")]
    public float lightAttackShakeDuration = 0.12f;
    [Tooltip("轻击晃动的冷却时间（秒）。冷却期间再次轻击不会触发新的晃动，避免过度频繁。")]
    public float lightAttackShakeCooldown = 0.35f;
    [Tooltip("轻击晃动曲线：X 为归一化时间（0-1），Y 为方向倍数（-1 表示左下角，1 表示右上角）。默认从左下角快速扫到右上角。")]
    public AnimationCurve lightAttackShakeCurve;

    [Header("重击视角晃动")]
    [Tooltip("重击时相机水平旋转（左右）晃动的最大幅度（度）。正值越大，左右旋转冲击感越强。")]
    public float heavyAttackShakeYawAmplitude = 10f;
    [Tooltip("重击时相机垂直旋转（上下）晃动的最大幅度（度）。正值越大，上下旋转冲击感越强。配合 Yaw 实现从左上角到右下角的对角线晃动。")]
    public float heavyAttackShakePitchAmplitude = 8f;
    [Tooltip("重击晃动的总时长（秒）。建议比重击动画关键帧冲击时段稍长，保证自然收尾。")]
    public float heavyAttackShakeDuration = 0.25f;
    [Tooltip("重击晃动的冷却时间（秒）。冷却期间再次重击不会触发新的晃动。")]
    public float heavyAttackShakeCooldown = 0.5f;
    [Tooltip("重击晃动曲线：X 为归一化时间（0-1），Y 为方向倍数（-1 表示左上角，1 表示右下角）。默认从左上角快速甩到右下角再回正。")]
    public AnimationCurve heavyAttackShakeCurve;

    [Header("受击视角震动")]
    [Tooltip("受击时相机水平旋转（左右）震动的最大幅度（度）。正值越大，左右震动越强。")]
    public float damageShakeYawAmplitude = 6f;
    [Tooltip("受击时相机垂直旋转（上下）震动的最大幅度（度）。正值越大，上下震动越强。")]
    public float damageShakePitchAmplitude = 5f;
    [Tooltip("受击震动的总时长（秒）。建议短促有力，避免拖沓。")]
    public float damageShakeDuration = 0.4f;
    [Tooltip("受击震动的冷却时间（秒）。冷却期间连续受击不会重复触发震动。")]
    public float damageShakeCooldown = 0.4f;
    [Tooltip("受击震动曲线：X 为归一化时间（0-1），Y 为方向倍数（-1 表示左/下，1 表示右/上）。默认快速衰减震荡，模拟冲击感。")]
    public AnimationCurve damageShakeCurve;

    [Header("低血量/死亡屏幕特效")]
    [Tooltip("屏幕特效 Shader（Hidden/HitAndDeathEffect）。留空时会自动通过 Shader.Find 查找。")]
    public Shader hitDeathEffectShader;
    [Tooltip("低血量触发边框泛红的阈值（0-1）。血百分比低于此值时开始泛红，默认 0.3 即 30%。")]
    [Range(0f, 1f)]
    public float lowHealthThreshold = 0.3f;
    [Tooltip("边框泛红的最大强度（0-1）。血量越低越接近此强度。")]
    [Range(0f, 1f)]
    public float maxVignetteStrength = 0.5f;
    [Tooltip("边框泛红的颜色。")]
    public Color lowHealthVignetteColor = new Color(0.8f, 0.05f, 0.05f, 1f);
    [Tooltip("死亡时画面向灰白渐变的速度（每秒）。值越小渐变越慢。")]
    [Range(0.01f, 2f)]
    public float deathDesaturateSpeed = 0.4f;

    private Material hitDeathEffectMaterial;
    private Player player;
    private bool hitDeathEffectEnabled;
    private float currentDesaturate;

    private float currentYaw;
    private float currentPitch;
    private float currentDistance;
    private float shakeYawOffset;
    private float shakePitchOffset;
    private float shakeTimer;
    private float shakeCooldownTimer;
    private bool isShaking;
    private AnimationCurve currentShakeCurve;
    private float currentShakeYawAmplitude;
    private float currentShakePitchAmplitude;
    private float currentShakeDuration;

    void Start()
    {
        if (target == null)
        {
            Debug.LogWarning("[PlayCamera] 未设置 Target，请在 Inspector 中指定跟随目标。");
            return;
        }

        // 根据当前相对位置初始化角度
        Vector3 offset = transform.position - (target.position + Vector3.up * height);
        currentDistance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);
        currentYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        currentPitch = Mathf.Asin(Mathf.Clamp(offset.y / Mathf.Max(offset.magnitude, 0.001f), -1f, 1f)) * Mathf.Rad2Deg;

        InitializeDefaultShakeCurve();

        player = target.GetComponent<Player>();
        InitializeHitDeathEffect();
    }

    void OnEnable()
    {
        Player.OnLightAttack += OnLightAttackTriggered;
        Player.OnHeavyAttack += OnHeavyAttackTriggered;
        Player.OnTakeDamage += OnTakeDamageTriggered;
    }

    void OnDisable()
    {
        Player.OnLightAttack -= OnLightAttackTriggered;
        Player.OnHeavyAttack -= OnHeavyAttackTriggered;
        Player.OnTakeDamage -= OnTakeDamageTriggered;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        UpdateShakeCooldown();
        HandleInput();
        UpdateShake();
        UpdatePosition();
        UpdateHitDeathEffect();
    }

    /// <summary>
    /// 处理鼠标输入：旋转与缩放
    /// </summary>
    private void HandleInput()
    {
        currentYaw += Input.GetAxis("Mouse X") * horizontalSpeed * Time.deltaTime;
        currentPitch -= Input.GetAxis("Mouse Y") * verticalSpeed * Time.deltaTime;
        currentPitch = Mathf.Clamp(currentPitch, minVerticalAngle, maxVerticalAngle);

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            currentDistance -= scroll * 5f;
            currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        }
    }

    /// <summary>
    /// 更新相机位置与朝向
    /// </summary>
    private void UpdatePosition()
    {
        Quaternion rotation = Quaternion.Euler(currentPitch + shakePitchOffset, currentYaw + shakeYawOffset, 0f);
        Vector3 targetPos = target.position + Vector3.up * height;
        Vector3 desiredOffset = rotation * new Vector3(0f, 0f, -currentDistance);
        Vector3 desiredPosition = targetPos + desiredOffset;

        // 相机碰撞检测，避免穿墙
        if (enableCollision)
        {
            if (Physics.Linecast(targetPos, desiredPosition, out RaycastHit hit, collisionLayer, QueryTriggerInteraction.Ignore))
            {
                desiredPosition = hit.point;
            }
        }

        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionSmooth * Time.deltaTime);

        // 平滑看向目标点
        Quaternion targetRotation = Quaternion.LookRotation(targetPos - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lookSmooth * Time.deltaTime);
    }

    /// <summary>
    /// 轻击触发时的回调，冷却结束后才开始视角晃动
    /// </summary>
    private void OnLightAttackTriggered()
    {
        if (shakeCooldownTimer > 0f)
        {
            Debug.Log($"[PlayCamera] 收到轻击事件，但晃动冷却中 ({shakeCooldownTimer:F2}s)，跳过本次晃动。");
            return;
        }

        Debug.Log("[PlayCamera] 收到轻击事件，启动视角晃动。");
        TriggerLightAttackShake();
        shakeCooldownTimer = lightAttackShakeCooldown;
    }

    /// <summary>
    /// 重击触发时的回调，冷却结束后才开始视角晃动
    /// </summary>
    private void OnHeavyAttackTriggered()
    {
        if (shakeCooldownTimer > 0f)
        {
            Debug.Log($"[PlayCamera] 收到重击事件，但晃动冷却中 ({shakeCooldownTimer:F2}s)，跳过本次晃动。");
            return;
        }

        Debug.Log("[PlayCamera] 收到重击事件，启动视角晃动。");
        TriggerHeavyAttackShake();
        shakeCooldownTimer = heavyAttackShakeCooldown;
    }

    /// <summary>
    /// 受击触发时的回调，冷却结束后才开始视角震动
    /// </summary>
    private void OnTakeDamageTriggered()
    {
        if (shakeCooldownTimer > 0f)
        {
            Debug.Log($"[PlayCamera] 收到受击事件，但震动冷却中 ({shakeCooldownTimer:F2}s)，跳过本次震动。");
            return;
        }

        Debug.Log("[PlayCamera] 收到受击事件，启动视角震动。");
        TriggerDamageShake();
        shakeCooldownTimer = damageShakeCooldown;
    }

    /// <summary>
    /// 手动触发轻击视角晃动，也可由其他系统调用
    /// 默认曲线 -1 -> 1，配合负 Yaw + 正 Pitch 实现视角从左下角甩到右上角。
    /// </summary>
    public void TriggerLightAttackShake()
    {
        currentShakeCurve = lightAttackShakeCurve;
        currentShakeYawAmplitude = -lightAttackShakeYawAmplitude;
        currentShakePitchAmplitude = lightAttackShakePitchAmplitude;
        currentShakeDuration = lightAttackShakeDuration;
        shakeTimer = 0f;
        isShaking = true;
    }

    /// <summary>
    /// 手动触发重击视角晃动，也可由其他系统调用
    /// 默认曲线 -1 -> 1 -> 0，配合负 Yaw + 正 Pitch 实现视角从左上角甩到右下角再回正。
    /// </summary>
    public void TriggerHeavyAttackShake()
    {
        currentShakeCurve = heavyAttackShakeCurve;
        currentShakeYawAmplitude = -heavyAttackShakeYawAmplitude;
        currentShakePitchAmplitude = heavyAttackShakePitchAmplitude;
        currentShakeDuration = heavyAttackShakeDuration;
        shakeTimer = 0f;
        isShaking = true;
    }

    /// <summary>
    /// 手动触发受击视角震动，也可由其他系统调用。
    /// 默认曲线为快速衰减震荡，配合正负 Yaw/Pitch 实现冲击感。
    /// </summary>
    public void TriggerDamageShake()
    {
        currentShakeCurve = damageShakeCurve;
        currentShakeYawAmplitude = damageShakeYawAmplitude;
        currentShakePitchAmplitude = damageShakePitchAmplitude;
        currentShakeDuration = damageShakeDuration;
        shakeTimer = 0f;
        isShaking = true;
    }

    /// <summary>
    /// 更新晃动冷却计时器
    /// </summary>
    private void UpdateShakeCooldown()
    {
        if (shakeCooldownTimer > 0f)
        {
            shakeCooldownTimer -= Time.deltaTime;
            if (shakeCooldownTimer < 0f)
            {
                shakeCooldownTimer = 0f;
            }
        }
    }

    /// <summary>
    /// 每帧更新晃动偏移，使用 Time.deltaTime 保证帧率无关
    /// </summary>
    private void UpdateShake()
    {
        if (!isShaking)
        {
            return;
        }

        shakeTimer += Time.deltaTime;
        float t = Mathf.Clamp01(shakeTimer / Mathf.Max(currentShakeDuration, 0.001f));
        float curveValue = currentShakeCurve != null ? currentShakeCurve.Evaluate(t) : 0f;

        // curveValue: -1 = 左/下，1 = 右/上
        shakeYawOffset = curveValue * currentShakeYawAmplitude;
        shakePitchOffset = curveValue * currentShakePitchAmplitude;

        if (t >= 1f)
        {
            shakeYawOffset = 0f;
            shakePitchOffset = 0f;
            isShaking = false;
        }
    }

    /// <summary>
    /// 初始化默认晃动曲线。
    /// 轻击：-1 -> 1，配合负 Yaw + 正 Pitch 实现从左下角甩到右上角。
    /// 重击：-1 -> 1 -> 0，配合负 Yaw + 正 Pitch 实现从左上角甩到右下角再回正。
    /// 关键点使用平滑切线，保证加速、减速过程自然流畅。
    /// </summary>
    private void InitializeDefaultShakeCurve()
    {
        if (lightAttackShakeCurve == null || lightAttackShakeCurve.length == 0)
        {
            lightAttackShakeCurve = new AnimationCurve(
                new Keyframe(0f, -1f, 0f, 2f),
                new Keyframe(1f, 1f, 2f, 0f)
            );
        }

        if (heavyAttackShakeCurve == null || heavyAttackShakeCurve.length == 0)
        {
            heavyAttackShakeCurve = new AnimationCurve(
                new Keyframe(0f, -1f, 0f, 4f),
                new Keyframe(0.5f, 1f, 0f, 0f),
                new Keyframe(1f, 0f, -4f, 0f)
            );
        }

        if (damageShakeCurve == null || damageShakeCurve.length == 0)
        {
            damageShakeCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 22f),
                new Keyframe(0.1f, 1f, 0f, 0f),
                new Keyframe(0.25f, -0.8f, 0f, 0f),
                new Keyframe(0.45f, 0.5f, 0f, 0f),
                new Keyframe(0.65f, -0.3f, 0f, 0f),
                new Keyframe(0.85f, 0.15f, 0f, 0f),
                new Keyframe(1f, 0f, 0f, 0f)
            );
        }
    }

    /// <summary>
    /// 初始化屏幕特效材质。Shader 未手动指定时自动查找 Hidden/HitAndDeathEffect。
    /// </summary>
    private void InitializeHitDeathEffect()
    {
        if (hitDeathEffectShader == null)
        {
            hitDeathEffectShader = Shader.Find("Hidden/HitAndDeathEffect");
        }

        if (hitDeathEffectShader != null)
        {
            hitDeathEffectMaterial = new Material(hitDeathEffectShader);
        }
        else
        {
            Debug.LogWarning("[PlayCamera] 未找到屏幕特效 Shader（Hidden/HitAndDeathEffect）。请确认 HitAndDeathEffect.shader 已放入项目 Assets 目录。", this);
        }
    }

    /// <summary>
    /// 每帧读取玩家血量并更新屏幕特效参数：低血量边框泛红、死亡灰白。
    /// </summary>
    private void UpdateHitDeathEffect()
    {
        if (hitDeathEffectMaterial == null)
        {
            return;
        }

        // 兜底：target 若在运行时才设置，补一次玩家引用
        if (player == null && target != null)
        {
            player = target.GetComponent<Player>();
        }

        float desaturateTarget = 0f;
        float vignette = 0f;

        if (player != null)
        {
            if (player.IsDead)
            {
                desaturateTarget = 1f;
            }
            else
            {
                float hp = player.HealthPercent;
                if (hp < lowHealthThreshold)
                {
                    vignette = Mathf.InverseLerp(lowHealthThreshold, 0f, hp) * maxVignetteStrength;
                }
            }
        }

        // 死亡时画面向灰白匀速渐变，而非瞬间切换
        currentDesaturate = Mathf.MoveTowards(currentDesaturate, desaturateTarget, deathDesaturateSpeed * Time.deltaTime);

        hitDeathEffectMaterial.SetFloat("_Desaturate", currentDesaturate);
        hitDeathEffectMaterial.SetFloat("_VignetteStrength", vignette);
        hitDeathEffectMaterial.SetColor("_VignetteColor", lowHealthVignetteColor);

        hitDeathEffectEnabled = currentDesaturate > 0.001f || vignette > 0.001f;
    }

    /// <summary>
    /// 屏幕后处理：低血量边框泛红、死亡灰白。
    /// </summary>
    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (hitDeathEffectMaterial == null || !hitDeathEffectEnabled)
        {
            Graphics.Blit(src, dst);
            return;
        }

        Graphics.Blit(src, dst, hitDeathEffectMaterial);
    }

    void OnDestroy()
    {
        if (hitDeathEffectMaterial != null)
        {
            Destroy(hitDeathEffectMaterial);
            hitDeathEffectMaterial = null;
        }
    }
}
