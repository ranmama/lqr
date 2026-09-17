using UnityEngine;

/// <summary>
/// 阵营枚举，用于区分伤害来源和伤害目标。
/// Enemy 阵营的触发器只会伤害 Player，Player 阵营的触发器不会伤害友军（避免误伤）。
/// </summary>
public enum Faction
{
    /// <summary>玩家阵营</summary>
    Player,
    /// <summary>敌人阵营</summary>
    Enemy
}

/// <summary>
/// 伤害触发器。
/// 挂载到 GameObject 上后，当其他物体进入触发范围时会检测阵营关系，
/// 若为敌对阵营则对其调用 TakeDamage 造成伤害。
/// 支持通过 <see cref="Create"/> 静态方法在代码中动态创建，也支持在 Inspector 中手动挂载。
/// </summary>
public class AttackTrigger : MonoBehaviour
{
    [Header("伤害")]
    [Tooltip("单次伤害值，目标进入触发范围时一次性结算")]
    public float damage = 10f;

    [Tooltip("伤害来源阵营。Enemy 阵营的触发器只会伤害 Player，Player 阵营的触发器不会伤害 Player")]
    public Faction faction = Faction.Enemy;

    [Header("触发器")]
    [Tooltip("球形触发器半径（米）。仅在当前 GameObject 上没有任何 Collider 时自动创建一个 SphereCollider 并使用此值")]
    public float radius = 0.5f;

    [Tooltip("生命周期（秒），从创建开始计时，超过后自动销毁此 GameObject。设为 0 表示不自动销毁，需手动管理（静态放置的触发器建议设为 0）")]
    public float lifetime = 0f;

    [Tooltip("命中目标后是否立即销毁此 GameObject。适用于子弹、箭矢等一次性伤害判定")]
    public bool destroyOnHit = true;

    /// <summary>当前伤害是否已启用。</summary>
    private bool damageEnabled;

    /// <summary>缓存的 Collider 引用，用于启用/禁用。</summary>
    private Collider cachedCollider;

    /// <summary>
    /// 在 Awake 中确保 GameObject 上至少有一个触发器 Collider。
    /// 若已手动挂载 Collider（如 BoxCollider、CapsuleCollider），自动将其设为触发器；
    /// 若没有 Collider，则自动添加一个球形触发器。
    /// </summary>
    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col == null)
        {
            var sphereCol = gameObject.AddComponent<SphereCollider>();
            sphereCol.isTrigger = true;
            sphereCol.radius = radius;
            cachedCollider = sphereCol;
        }
        else
        {
            if (!col.isTrigger)
                col.isTrigger = true;
            cachedCollider = col;
        }

        // 确保有 Rigidbody，否则 OnTriggerEnter 不会触发
        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 默认启用伤害；若需初始禁用，由调用方在 Instantiate 后调用 DisableDamage()
        damageEnabled = true;

        Debug.Log($"[AttackTrigger] 初始化完成 | isTrigger={cachedCollider != null && cachedCollider.isTrigger} | Rigidbody={rb != null} | layer={LayerMask.LayerToName(gameObject.layer)} | faction={faction} | damage={damage}");
    }

    /// <summary>
    /// 生命周期计时：若 lifetime > 0，在指定秒数后自动销毁此 GameObject。
    /// 用于避免遗留未回收的触发器（如飞行道具未命中目标时）。
    /// </summary>
    private void Start()
    {
        if (lifetime > 0f)
        {
            Destroy(gameObject, lifetime);
        }
    }

    /// <summary>
    /// 当其他物体的 Collider 进入触发范围时调用。
    /// 根据阵营判断目标类型：Enemy 阵营伤害 Player，Player 阵营伤害 Enemy。
    /// </summary>
    /// <param name="other">进入触发范围的目标 Collider</param>
    private void OnTriggerEnter(Collider other)
    {
        if (!damageEnabled)
            return;

        bool dealtDamage = false;

        if (faction == Faction.Enemy)
        {
            // 敌人阵营的触发器：只伤害玩家
            var player = other.GetComponentInParent<Player>();
            if (player != null && !player.IsDead)
            {
                float hpBefore = player.currentHealth;
                player.TakeDamage(damage);
                dealtDamage = player.currentHealth < hpBefore;
            }
        }
        else
        {
            // 玩家阵营的触发器：只伤害敌人
            var enemy = other.GetComponentInParent<Enemy>();
            if (enemy != null && !enemy.IsDead)
            {
                float hpBefore = enemy.currentHealth;
                enemy.TakeDamage(damage);
                dealtDamage = enemy.currentHealth < hpBefore;
            }
            else
            {
                // 鹅企（couqie）也视为敌人：命中时对其造成伤害。
                // 注意用 else 而非并列，避免同个物体同时挂 Enemy 和 couqie 时被结算两次。
                var penguin = other.GetComponentInParent<couqie>();
                if (penguin != null && !penguin.IsDead)
                {
                    float hpBefore = penguin.currentHealth;
                    penguin.TakeDamage(damage);
                    dealtDamage = penguin.currentHealth < hpBefore;
                }
            }
        }

        // 只有真正造成伤害后才销毁，避免无敌期间浪费攻击盒子
        if (destroyOnHit && dealtDamage)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 动画事件：启用伤害判定。
    /// 在 Animation 窗口中添加 Event，选择此方法（无参数），攻击盒子即可在指定帧开始造成伤害。
    /// </summary>
    public void EnableDamage()
    {
        damageEnabled = true;
        if (cachedCollider != null)
            cachedCollider.enabled = true;
        Debug.Log($"[AttackTrigger] EnableDamage 被调用，攻击盒子伤害已激活 | {gameObject.name}");
    }

    /// <summary>
    /// 立即禁用伤害判定（关闭 Collider）。
    /// 用于 Instantiate 后 Awake 已执行、需要重新禁用伤害的场景。
    /// </summary>
    public void DisableDamage()
    {
        damageEnabled = false;
        if (cachedCollider != null)
            cachedCollider.enabled = false;
        Debug.Log($"[AttackTrigger] DisableDamage 被调用，攻击盒子伤害已禁用 | {gameObject.name}");
    }

    /// <summary>
    /// 在指定位置动态创建一个伤害触发器。
    /// 会自动创建一个带有 SphereCollider 的 GameObject 并完成所有初始化。
    /// </summary>
    /// <param name="position">触发器的世界坐标位置</param>
    /// <param name="damage">单次伤害值</param>
    /// <param name="faction">伤害来源阵营（Player 或 Enemy）</param>
    /// <param name="radius">球形触发器半径（米）</param>
    /// <param name="lifetime">生命周期（秒），超过后自动销毁</param>
    /// <returns>已初始化完成的 AttackTrigger 实例</returns>
    public static AttackTrigger Create(Vector3 position, float damage, Faction faction, float radius = 0.5f, float lifetime = 0.5f)
    {
        var go = new GameObject($"AttackTrigger_{faction}");
        go.transform.position = position;
        var trigger = go.AddComponent<AttackTrigger>();
        trigger.damage = damage;
        trigger.faction = faction;
        trigger.radius = radius;
        trigger.lifetime = lifetime;
        return trigger;
    }
}