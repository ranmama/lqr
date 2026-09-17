using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 中世纪风格开始界面控制器
/// 负责：开始游戏（按钮内睁眼 → 触手缠住按钮 → 触手缠满屏幕 → 加载场景）、
///       设置面板开关、音量调节、退出游戏
/// </summary>
public class MainMenuController : MonoBehaviour
{
    /// <summary>一条触手的运行时数据与独立动画参数（由 MedievalMenuBuilder 在构建界面时填好）。
    /// 每根触手都有自己的时长、回弹、路径弯曲、钻出旋转和卷曲节奏，所以不会整齐划一地动。</summary>
    [System.Serializable]
    public class TentacleArm
    {
        public RectTransform rect;
        public Image image;
        public float baseAngle;      // 静止时的旋转角
        public Vector2 homePos;      // 静止时的锚点位置
        public Vector2 emergeOffset; // 生长起点相对 homePos 的偏移（从按钮/画面外钻出来）

        public float emergeDelay;    // 出场延迟（秒）
        public float growDuration;   // 生长时长（秒）——每根都不同
        public float fromScale;      // 起始缩放（从一个点挤出来）
        public float overshoot;      // 到位时的回弹强度（easeOutBack）
        public float pathBend;       // 路径弯曲量（像素，正负决定绕向）
        public float spinLead;       // 钻出时的旋转偏移（度，正负决定旋向）
        public float curlAmp;        // 生长途中的卷曲幅度（度）
        public float curlFreq;       // 卷曲频率
        public float swingAmp = 5f;  // 到位后的常态蠕动摆幅（度）
        public float swingFreq = 1.4f;
        public float phase = 0f;
    }

    [Header("场景设置")]
    [Tooltip("游戏主场景的名称，必须与 Build Settings 中的场景名一致")]
    public string gameSceneName = "Scene_A";

    [Header("面板引用")]
    [Tooltip("设置面板，初始为隐藏状态")]
    public GameObject settingsPanel;

    [Tooltip("淡入淡出用的黑色遮罩（带 CanvasGroup）")]
    public CanvasGroup fadeGroup;

    [Header("音量设置")]
    public Slider musicSlider;
    public Slider sfxSlider;

    [Header("过渡设置")]
    public float fadeDuration = 0.6f;

    [Tooltip("黑屏期间显示的“正在加载”提示文字（FadePanel 的子物体）")]
    public Text loadingText;

    [Tooltip("群眼注视加载界面控制器（留空则自动创建一个；黑屏期间由它接管加载演出）")]
    public LoadingEyesController loadingEyes;

    [Header("开始按钮「眼睛睁开」特效（限制在按钮内部）")]
    [Tooltip("开始游戏按钮")]
    public Button startButton;

    [Tooltip("眼睛特效根节点（开始按钮的子物体，带 RectMask2D，默认隐藏）")]
    public GameObject eyeRoot;

    [Tooltip("特效根节点的 CanvasGroup（点击瞬间把按钮文字化进眼皮）")]
    public CanvasGroup eyeGroup;

    [Tooltip("眼球整体容器（推入瞳孔时的缩放对象）")]
    public RectTransform eyeStage;

    [Tooltip("上眼睑（锚定按钮顶部，遮住上半只眼）")]
    public RectTransform lidTop;

    [Tooltip("下眼睑（锚定按钮底部，遮住下半只眼）")]
    public RectTransform lidBottom;

    [Tooltip("虹膜容器（虹膜+瞳孔+高光，扫视眼动用）")]
    public RectTransform irisPivot;

    [Tooltip("瞳孔（独立层，用于收缩/放大动画）")]
    public RectTransform pupil;

    [Tooltip("暗适应层：闭眼时的暗红薄雾，睁眼时淡出")]
    public Image adaptLayer;

    [Tooltip("点击后闭眼定格时长（秒）")]
    public float eyeClosedHold = 0.22f;

    [Tooltip("睁眼（眼睑分开）耗时（秒）")]
    public float eyeOpenDuration = 0.85f;

    [Tooltip("睁开后凝视停留时间（秒）")]
    public float eyeHoldDuration = 0.55f;

    [Tooltip("镜头推入瞳孔耗时（秒）")]
    public float eyeZoomDuration = 0.5f;

    [Tooltip("下眼睑下移距离（像素）。真人睁眼主要靠上眼睑，所以这个值远小于眼睑高度")]
    public float lowerLidTravel = 30f;

    [Header("触手收尾（睁眼之后）")]
    [Tooltip("全屏触手层的根节点（独立 Canvas，默认隐藏）")]
    public GameObject screenTentacleRoot;

    [Tooltip("从画面四周探入的触手")]
    public TentacleArm[] screenArms;

    [Tooltip("触手合拢后的黑幕（覆盖全屏，负责把画面彻底吞掉）")]
    public Image screenVeil;

    [Tooltip("触手彻底缠满屏幕的耗时（秒）")]
    public float strangleDuration = 0.75f;

    private bool isTransitioning = false;

    private void Start()
    {
        // 关闭设置面板
        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        // 淡入遮罩归零
        if (fadeGroup != null)
        {
            fadeGroup.alpha = 0f;
            fadeGroup.blocksRaycasts = false;
            fadeGroup.gameObject.SetActive(true);
        }

        // 读取已保存的音量
        float music = PlayerPrefs.GetFloat("MusicVolume", 0.8f);
        float sfx = PlayerPrefs.GetFloat("SfxVolume", 0.8f);

        if (musicSlider != null)
        {
            musicSlider.value = music;
            musicSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }
        if (sfxSlider != null)
        {
            sfxSlider.value = sfx;
            sfxSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
        }

        // 解锁鼠标（从游戏场景返回菜单时很有用）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 恢复被暂停的时间
        Time.timeScale = 1f;
    }

    // ===== 按钮事件 =====

    /// <summary>开始游戏：按钮内播放真人睁眼特效 → 淡出 → 异步加载游戏场景</summary>
    public void StartGame()
    {
        if (isTransitioning) return;
        isTransitioning = true;
        StartCoroutine(StartGameSequence());
    }

    /// <summary>打开设置面板</summary>
    public void OpenSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    /// <summary>关闭设置面板</summary>
    public void CloseSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    /// <summary>退出游戏（编辑器内停止播放，打包后真正退出）</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ===== 按钮内「真人睁眼」特效 =====

    private System.Collections.IEnumerator StartGameSequence()
    {
        // 整个过渡期间屏蔽菜单输入（FadePanel 已设为可吞点击，特效层自身也遮挡）
        if (fadeGroup != null)
            fadeGroup.blocksRaycasts = true;

        yield return PlayEyeOpen();
        yield return PlayTentacles();
        yield return FadeAndLoad();
    }

    /// <summary>按钮内的真人式睁眼过场；特效未接线时直接跳过</summary>
    private System.Collections.IEnumerator PlayEyeOpen()
    {
        if (eyeRoot == null || lidTop == null || lidBottom == null)
            yield break;

        if (startButton != null)
            startButton.interactable = false;

        // ---- 初始：闭眼状态（上下眼睑合拢；眼皮色 = 按钮填充色，看上去就是按钮本身） ----
        eyeRoot.SetActive(true);
        if (eyeStage != null)
            eyeStage.localScale = Vector3.one;
        lidTop.anchoredPosition = Vector2.zero;
        lidBottom.anchoredPosition = Vector2.zero;
        if (irisPivot != null)
        {
            irisPivot.anchoredPosition = Vector2.zero;
            irisPivot.localScale = Vector3.one;
        }
        if (pupil != null)
            pupil.localScale = Vector3.one * 1.28f;          // 刚睡醒：瞳孔放大
        if (adaptLayer != null)
        {
            Color c0 = adaptLayer.color;
            c0.a = 0.92f;                                    // 闭眼时一片暗红
            adaptLayer.color = c0;
        }

        // 眼皮淡入：让按钮文字平滑地"化"进眼皮，而不是闪一下空白
        if (eyeGroup != null)
        {
            float ft = 0f;
            while (ft < 0.12f)
            {
                ft += Time.unscaledDeltaTime;
                eyeGroup.alpha = Mathf.Clamp01(ft / 0.12f);
                yield return null;
            }
            eyeGroup.alpha = 1f;
        }

        // 闭眼定格：让玩家意识到"按钮表面长出了一只闭合的眼睛"
        yield return new WaitForSecondsRealtime(eyeClosedHold);

        // 上眼睑要完全退出按钮（自身高度 + 余量）；下眼睑只小幅下移，像真人一样
        float openTop = lidTop.rect.height + 14f;
        float openBottom = lowerLidTravel;

        // ---- 阶段 1：眼睑分开（带轻微回弹，像真人睁眼） ----
        float t = 0f;
        while (t < eyeOpenDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / eyeOpenDuration);

            // easeOutBack：先快后慢，末端轻微过冲再回落（眼睑的弹性感）
            float k = EaseOutBack(p, 0.9f);
            lidTop.anchoredPosition = new Vector2(0f, openTop * k);
            lidBottom.anchoredPosition = new Vector2(0f, -openBottom * k);

            // 暗适应：进入光线的头 70% 时间内，暗红薄雾逐渐退去
            if (adaptLayer != null)
            {
                Color c = adaptLayer.color;
                c.a = 0.92f * (1f - Mathf.Clamp01(p * 1.45f));
                adaptLayer.color = c;
            }

            // 瞳孔遇光收缩：睁到 30% 后开始
            if (pupil != null)
            {
                float constrict = Mathf.Clamp01((p - 0.3f) / 0.7f);
                pupil.localScale = Vector3.one * Mathf.Lerp(1.28f, 1f, EaseOutCubic(constrict));
            }

            yield return null;
        }
        lidTop.anchoredPosition = new Vector2(0f, openTop);
        lidBottom.anchoredPosition = new Vector2(0f, -openBottom);
        if (adaptLayer != null)
        {
            Color c = adaptLayer.color; c.a = 0f; adaptLayer.color = c;
        }
        if (pupil != null) pupil.localScale = Vector3.one;

        // ---- 阶段 2：凝视——真实的眼球扫视微动 + 瞳孔呼吸（幅度已按按钮尺寸缩小） ----
        t = 0f;
        while (t < eyeHoldDuration)
        {
            t += Time.unscaledDeltaTime;
            if (irisPivot != null)
            {
                // 两组不同频率的正弦叠加，模拟不规则的注视微颤（微扫视）
                float x = Mathf.Sin(t * 6.8f) * 3.0f * Mathf.Sin(t * 2.1f + 0.7f);
                float y = Mathf.Sin(t * 3.4f) * 1.3f;
                irisPivot.anchoredPosition = new Vector2(x, y);
            }
            if (pupil != null)
            {
                float s = 1f + 0.025f * Mathf.Sin(t * 2.6f);  // 瞳孔轻微呼吸
                pupil.localScale = Vector3.one * s;
            }
            yield return null;
        }
        if (irisPivot != null) irisPivot.anchoredPosition = Vector2.zero;

        // 睁眼后保持定格：眼睛留在按钮里，交给触手过场接管（不再推入瞳孔）
        yield break;
    }

    // ===== 触手收尾：全屏伸出 -> 彻底缠满 =====

    private System.Collections.IEnumerator PlayTentacles()
    {
        // A. 整个开始界面的四周伸出触手
        if (screenTentacleRoot != null)
        {
            screenTentacleRoot.SetActive(true);
            if (screenVeil != null)
            {
                Color c = screenVeil.color; c.a = 0f; screenVeil.color = c;
            }
            ResetArms(screenArms);
            yield return PlayArms(screenArms);
        }

        // B. 触手收紧、向中心合拢，彻底缠满屏幕，最后被黑幕吞没
        yield return Strangle(strangleDuration);

        // C. 把黑屏交接给普通的淡出面板，然后收起触手层
        if (screenVeil != null)
        {
            Color c = screenVeil.color; c.a = 1f; screenVeil.color = c;
        }
        if (fadeGroup != null)
            fadeGroup.alpha = 1f;
        if (screenTentacleRoot != null)
            screenTentacleRoot.SetActive(false);
        if (eyeRoot != null)
            eyeRoot.SetActive(false);
    }

    /// <summary>把所有触手摆回"尚未钻出"的初始态（每根用各自的起始缩放）</summary>
    private static void ResetArms(TentacleArm[] arms)
    {
        if (arms == null) return;
        foreach (var a in arms)
        {
            if (a == null || a.rect == null) continue;
            float s = a.fromScale > 0f ? a.fromScale : 0.08f;
            a.rect.localScale = new Vector3(s, s, 1f);
            a.rect.anchoredPosition = a.homePos + a.emergeOffset;
            a.rect.localEulerAngles = new Vector3(0f, 0f, a.baseAngle + a.spinLead);
            if (a.image != null)
            {
                Color c = a.image.color; c.a = 0f; a.image.color = c;
            }
        }
    }

    /// <summary>这一组触手全部就位所需的总时长（取「延迟 + 时长」的最大值）</summary>
    private static float ArmsTotalTime(TentacleArm[] arms)
    {
        float m = 0f;
        if (arms == null) return m;
        foreach (var a in arms)
        {
            if (a == null) continue;
            m = Mathf.Max(m, a.emergeDelay + Mathf.Max(0.05f, a.growDuration));
        }
        return m;
    }

    /// <summary>二次贝塞尔取点：让触手沿弧线钻出，而不是笔直滑进来</summary>
    private static Vector2 QuadBezier(Vector2 p0, Vector2 c, Vector2 p1, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * c + t * t * p1;
    }

    /// <summary>
    /// 每根触手按「自己的」动画参数独立演出：
    /// 各自的延迟 / 时长 / 起始缩放 / 回弹 / 弧线路径 / 钻出旋向 / 卷曲节奏。
    /// 因此它们不会整齐划一地冒出来，而是一只只以不同姿态扭动着钻出。
    /// </summary>
    private System.Collections.IEnumerator PlayArms(TentacleArm[] arms)
    {
        if (arms == null || arms.Length == 0) yield break;

        float total = ArmsTotalTime(arms);
        float t = 0f;
        while (t < total)
        {
            t += Time.unscaledDeltaTime;

            foreach (var a in arms)
            {
                if (a == null || a.rect == null) continue;

                float localT = t - a.emergeDelay;
                if (localT <= 0f)                       // 还没轮到它出场
                {
                    float s0 = a.fromScale > 0f ? a.fromScale : 0.08f;
                    a.rect.localScale = new Vector3(s0, s0, 1f);
                    a.rect.anchoredPosition = a.homePos + a.emergeOffset;
                    if (a.image != null)
                    {
                        Color c0 = a.image.color; c0.a = 0f; a.image.color = c0;
                    }
                    continue;
                }

                float dur = Mathf.Max(0.05f, a.growDuration);
                float pa = Mathf.Clamp01(localT / dur);

                // 1) 缩放：easeOutBack，到位时按各自强度轻微过冲回弹
                float s = Mathf.Lerp(a.fromScale, 1f, EaseOutBack(pa, a.overshoot));

                // 2) 位置：沿二次贝塞尔弧线钻出（各自的绕向与弯度）
                Vector2 from = a.homePos + a.emergeOffset;
                Vector2 to = a.homePos;
                Vector2 dir = to - from;
                Vector2 perp = dir.sqrMagnitude > 0.001f
                             ? new Vector2(-dir.y, dir.x).normalized * a.pathBend
                             : Vector2.zero;
                Vector2 ctrl = (from + to) * 0.5f + perp;
                float move = pa < 0.5f ? 2f * pa * pa
                                       : 1f - Mathf.Pow(-2f * pa + 2f, 2f) * 0.5f;
                a.rect.anchoredPosition = QuadBezier(from, ctrl, to, move);
                a.rect.localScale = new Vector3(s, s, 1f);

                // 3) 旋转：钻出旋向收敛 + 生长途中的卷曲 + 到位后的常态蠕动
                float spin = a.spinLead * (1f - EaseOutCubic(pa));
                float curl = Mathf.Sin(localT * a.curlFreq + a.phase) * a.curlAmp * (1f - pa);
                float swing = Mathf.Sin(t * a.swingFreq * 2.4f + a.phase) * a.swingAmp * pa;
                a.rect.localEulerAngles = new Vector3(0f, 0f, a.baseAngle + spin + curl + swing);

                // 4) 只在起手极短时间内淡入，之后保持完全不透明
                if (a.image != null)
                {
                    Color c = a.image.color;
                    c.a = Mathf.Clamp01(pa / 0.06f);
                    a.image.color = c;
                }
            }
            yield return null;
        }

        // 收尾定格：完全不透明、回到 homePos、回到静止角度
        foreach (var a in arms)
        {
            if (a == null || a.rect == null) continue;
            a.rect.localScale = Vector3.one;
            a.rect.anchoredPosition = a.homePos;
            a.rect.localEulerAngles = new Vector3(0f, 0f, a.baseAngle);
            if (a.image != null)
            {
                Color c = a.image.color; c.a = 1f; a.image.color = c;
            }
        }
    }

    /// <summary>触手彻底缠满屏幕：整体放大 + 向中心收紧 + 扭动加剧，最后黑幕落下</summary>
    private System.Collections.IEnumerator Strangle(float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);
            float e = p * p * (3f - 2f * p);            // smoothstep
            float s = Mathf.Lerp(1f, 2.6f, e);

            if (screenArms != null)
            {
                foreach (var a in screenArms)
                {
                    if (a == null || a.rect == null) continue;
                    a.rect.localScale = new Vector3(s, s, 1f);
                    a.rect.anchoredPosition = Vector2.Lerp(a.homePos, a.homePos * 0.32f, e);   // 向画面中心收紧
                    float swing = Mathf.Sin(t * a.swingFreq * 3.2f + a.phase) * a.swingAmp * 1.8f;
                    a.rect.localEulerAngles = new Vector3(0f, 0f, a.baseAngle + swing);
                }
            }

            // 触手合拢到一半后，黑幕开始吞没画面
            if (screenVeil != null)
            {
                Color c = screenVeil.color;
                c.a = Mathf.Clamp01((p - 0.55f) / 0.45f);
                screenVeil.color = c;
            }
            yield return null;
        }
    }

    private static float EaseOutCubic(float x)
    {
        return 1f - Mathf.Pow(1f - x, 3f);
    }

    private static float EaseOutBack(float x, float overshoot)
    {
        const float c1 = 1.70158f;
        float c3 = c1 * overshoot;
        float xm1 = x - 1f;
        return 1f + (c3 + 1f) * xm1 * xm1 * xm1 + c3 * xm1 * xm1;
    }

    /// <summary>加载失败时恢复开始按钮原状</summary>
    private void RestoreStartButton()
    {
        if (eyeRoot != null)
        {
            if (eyeGroup != null) eyeGroup.alpha = 1f;
            eyeRoot.gameObject.SetActive(false);
        }
        if (eyeStage != null)
            eyeStage.localScale = Vector3.one;
        if (lidTop != null) lidTop.anchoredPosition = Vector2.zero;
        if (lidBottom != null) lidBottom.anchoredPosition = Vector2.zero;
        if (screenTentacleRoot != null) screenTentacleRoot.SetActive(false);
        if (screenVeil != null)
        {
            Color c = screenVeil.color; c.a = 0f; screenVeil.color = c;
        }
        if (startButton != null)
            startButton.interactable = true;
    }

    // ===== 音量回调 =====

    public void OnMusicVolumeChanged(float value)
    {
        PlayerPrefs.SetFloat("MusicVolume", value);
    }

    public void OnSfxVolumeChanged(float value)
    {
        PlayerPrefs.SetFloat("SfxVolume", value);
    }

    // ===== 淡出并加载 =====

    private System.Collections.IEnumerator FadeAndLoad()
    {
        if (fadeGroup != null)
            fadeGroup.blocksRaycasts = true;

        // 旧版“黑屏 + 居中正在加载文字”已由 LoadingEyesController 的群眼加载界面取代
        // （加载文字由新界面自己携带并融入布局，这里关闭旧文字避免重复）
        if (loadingText != null)
            loadingText.gameObject.SetActive(false);

        // 1. 淡出到黑屏（若睁眼特效已把画面推进到全黑，则从这里直接继续，不会闪回菜单）
        float startAlpha = fadeGroup != null ? fadeGroup.alpha : 1f;
        float t = 0f;
        while (t < fadeDuration && startAlpha < 1f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            if (fadeGroup != null)
                fadeGroup.alpha = Mathf.Lerp(startAlpha, 1f, k);
            yield return null;
        }

        if (fadeGroup != null)
            fadeGroup.alpha = 1f;

        // 2. 交给群眼加载界面：眼睛浮现睁开、注视玩家、按进度收尾并切入新场景
        var loader = loadingEyes != null ? loadingEyes : LoadingEyesController.EnsureInstance();
        loader.onFailed = HandleLoadFailed;
        yield return loader.BeginLoad(gameSceneName);
    }

    /// <summary>加载失败：收起加载界面并恢复菜单可交互</summary>
    private void HandleLoadFailed()
    {
        if (fadeGroup != null)
        {
            fadeGroup.alpha = 0f;
            fadeGroup.blocksRaycasts = false;
        }
        RestoreStartButton();
        isTransitioning = false;
    }
}
