using System.Collections;
using UnityEngine;

/// <summary>
/// 玩家技能模块：封装一段技能的完整生命周期——
/// 冷却、按键触发、动画驱动多段命中、顺序音效、锁定超时兜底、动画结束自动解锁。
/// 将本组件添加到 Player 物体（或其子物体）后会被自动识别并驱动，无需改动 Player 代码。
///
/// 添加新技能：在 Player 上再挂一个 PlayerSkill 组件，配置不同的 Key / Damages / Sounds / 动画参数即可。
/// 注意：若多个技能都用动画事件驱动命中，请为各自的动画事件使用不同的方法名（或拆分子类重写），避免方法重名冲突。
/// </summary>
public class PlayerSkill : MonoBehaviour
{
    [Header("基础")]
    [Tooltip("技能按键，按下释放技能")]
    public KeyCode key = KeyCode.U;
    [Tooltip("技能冷却时间（秒）")]
    public float cooldown = 3f;
    [Tooltip("技能锁定最大允许时长（秒）。超时仍未结束会强制解除锁定，防止动画未触发导致玩家永久无法移动攻击。")]
    public float maxLockTime = 2f;

    [Header("动画")]
    [Tooltip("技能动画 Trigger 名称，用于进入技能动画状态。留空则不播放技能动画")]
    public string trigger = "";
    [Tooltip("技能动画状态名，用于检测动画结束并自动解除锁定。留空时需在动画最后一帧添加 EndSkill 动画事件手动结束。")]
    public string stateName = "";

    [Header("伤害")]
    [Tooltip("每段伤害值。动画事件 SpawnSkillAttackBox(i) 会按索引 i 结算第 i 段（从 0 开始）。")]
    public float[] damages = new float[] { 20f, 20f, 25f, 25f, 35f, 50f };

    [Header("音效")]
    [Tooltip("释放技能时按顺序依次播放的音效，前一播放完再播下一个。留空则跳过。")]
    public AudioClip[] sounds;

    private Player player;
    private int triggerHash;
    private int stateHash;
    private float cooldownTimer;
    private float lockTimer;
    private bool isActive;
    private bool wasAnimationPlaying;

    /// <summary>当前是否正在释放该技能。</summary>
    public bool IsActive => isActive;

    /// <summary>由 Player 初始化时注入宿主引用并预计算动画 hash。</summary>
    public void Initialize(Player owner)
    {
        player = owner;
        triggerHash = Animator.StringToHash(trigger);
        stateHash = Animator.StringToHash(stateName);
    }

    /// <summary>由 Player 每帧驱动。</summary>
    public void Tick()
    {
        if (player == null)
        {
            return;
        }

        if (player.IsDead)
        {
            if (isActive)
            {
                EndSkill();
            }
            return;
        }

        UpdateCooldown();
        UpdateLock();
        TryCast();
        UpdateAnimationLifecycle();
    }

    /// <summary>受击时打断技能，解除锁定并重置动画 Trigger。</summary>
    public void Interrupt()
    {
        if (isActive)
        {
            EndSkill();
        }
    }

    /// <summary>
    /// 动画事件：在技能动画第 segmentIndex 段命中帧生成伤害盒子（0 起）。
    /// segmentIndex 会按索引结算 Damages 数组中的对应段伤害。
    /// </summary>
    public void SpawnSkillAttackBox(int segmentIndex)
    {
        if (player == null)
        {
            return;
        }
        player.SpawnSkillHit(GetDamage(segmentIndex));
    }

    private float GetDamage(int index)
    {
        if (damages == null || damages.Length == 0)
        {
            return 0f;
        }
        return damages[Mathf.Clamp(index, 0, damages.Length - 1)];
    }

    private void TryCast()
    {
        if (cooldownTimer > 0f || isActive || !Input.GetKeyDown(key) || !player.CanCastSkill())
        {
            return;
        }

        Animator animator = player.PlayerAnimator;
        if (animator == null || string.IsNullOrEmpty(trigger) || !player.HasAnimatorParameter(triggerHash))
        {
            Debug.LogWarning($"[PlayerSkill] 技能触发失败：Trigger 未配置，或 Animator 中缺少名为 '{trigger}' 的 Trigger 参数。请检查 PlayerSkill 组件的 Trigger 与 Animator 的 Parameters 面板。", this);
            return;
        }

        if (string.IsNullOrEmpty(stateName))
        {
            Debug.LogWarning("[PlayerSkill] State Name 为空：技能动画结束后无法自动解除锁定，将依赖 Max Lock Time 超时兜底。建议填入 State Name，或在技能动画最后一帧添加动画事件 EndSkill()。", this);
        }

        cooldownTimer = cooldown;
        lockTimer = maxLockTime;
        isActive = true;

        animator.ResetTrigger(triggerHash);
        animator.SetTrigger(triggerHash);

        player.NotifySkillTriggered();
        StartCoroutine(PlaySounds());

        if (player.debugAttack)
        {
            Debug.Log($"[PlayerSkill] 技能触发 | Key={key} | 伤害段数={(damages != null ? damages.Length : 0)} | 伤害时机由动画事件 SpawnSkillAttackBox(0..N) 控制");
        }
    }

    private void UpdateCooldown()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer < 0f)
            {
                cooldownTimer = 0f;
            }
        }
    }

    private void UpdateLock()
    {
        if (!isActive)
        {
            return;
        }

        lockTimer -= Time.deltaTime;
        if (lockTimer <= 0f)
        {
            EndSkill();
            if (player.debugAttack)
            {
                Debug.LogWarning("[PlayerSkill] 技能锁定超时，已强制解除。请检查 State Name 是否与 Animator 状态名一致，以及进入技能状态的过渡条件是否为对应 Trigger。", this);
            }
        }
    }

    private void UpdateAnimationLifecycle()
    {
        bool playing = IsAnimationPlaying();
        if (!wasAnimationPlaying && playing)
        {
            if (player.debugAttack)
            {
                Debug.Log($"[PlayerSkill] 技能动画进入播放状态 | State={stateName}");
            }
        }
        if (wasAnimationPlaying && !playing)
        {
            EndSkill();
            if (player.debugAttack)
            {
                Debug.Log("[PlayerSkill] 技能动画已结束，解除技能锁定。");
            }
        }
        wasAnimationPlaying = playing;
    }

    private bool IsAnimationPlaying()
    {
        Animator animator = player != null ? player.PlayerAnimator : null;
        if (animator == null || string.IsNullOrEmpty(stateName))
        {
            return false;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        return currentState.shortNameHash == stateHash || nextState.shortNameHash == stateHash;
    }

    private IEnumerator PlaySounds()
    {
        if (sounds == null || sounds.Length == 0)
        {
            yield break;
        }

        Vector3 position = player != null ? player.transform.position : transform.position;
        for (int i = 0; i < sounds.Length; i++)
        {
            AudioClip clip = sounds[i];
            if (clip == null)
            {
                continue;
            }
            AudioSource.PlayClipAtPoint(clip, position);
            yield return new WaitForSeconds(clip.length);
        }
    }

    /// <summary>
    /// 结束技能：解除技能锁定并重置技能 Trigger。
    /// 若 State Name 已配置，动画结束时会自动调用；否则请在动画最后一帧用动画事件调用此方法。
    /// </summary>
    public void EndSkill()
    {
        isActive = false;
        Animator animator = player != null ? player.PlayerAnimator : null;
        if (animator != null && !string.IsNullOrEmpty(trigger))
        {
            animator.ResetTrigger(triggerHash);
        }
    }

    private void OnValidate()
    {
        cooldown = Mathf.Max(cooldown, 0.1f);
        maxLockTime = Mathf.Max(maxLockTime, 0.1f);
    }
}