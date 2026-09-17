using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 恐怖风格「群眼注视」加载界面控制器（圆形眼球·持续增多版）。
/// 替换旧版“黑屏 + 居中正在加载文字”的加载效果：
/// 加载期间黑暗中的眼球从少量开始，随时间与加载进度**不断增多**——
/// 新的眼球接连浮现、先是看向别处，随即“注意到你”缓缓转向并死死盯住玩家；
/// 注视点默认跟随鼠标位置（玩家感到被一片眼睛追踪），
/// 数量越聚越多，营造越来越强的压迫感。
///
/// 圆形眼球 = 完整眼球悬浮在黑暗中（无眼睑、无眼眶），比带眼睑的正常眼睛更诡异。
///
/// 布局约束：所有眼球避开“正在加载”标题安全区，彼此不过度重叠，始终在屏幕内。
/// 加载完成：所有瞳孔骤然放大、逼近瞪住 → 整个界面连同眼球淡入黑暗 → 切入新场景
/// （无“缩小消失”演出，眼球随界面一起被正确清理）。
///
/// 用法 A（已接入 MainMenuController）：过渡序列结束后调用 BeginLoad(sceneName)。
/// 用法 B：把本脚本挂到任意物体上，调用 BeginLoad("Scene_A") 即可。
/// 眼部贴图可留空：留空时会在运行时程序化生成一套（巩膜/虹膜/瞳孔/高光）。
/// 所有动画使用 unscaledDeltaTime，加载期间即使 Time.timeScale 被暂停也照常演出。
/// </summary>
public class LoadingEyesController : MonoBehaviour
{
    [Header("加载设置")]
    [Tooltip("要加载的场景名（必须与 Build Settings 中的一致）")]
    public string gameSceneName = "Scene_A";

    [Tooltip("加载界面最少展示秒数（太快看不清眼球浮现过程）")]
    public float minShowDuration = 2.8f;

    [Tooltip("显示进度的爬升耗时：进度平滑逼近真实值")]
    public float progressPaceDuration = 2.4f;

    [Tooltip("加载失败时恢复菜单的回调（由调用方注入）")]
    public System.Action onFailed;

    [Header("眼球数量与尺寸")]
    [Tooltip("加载开始时的初始眼球数量")]
    public int minEyeCount = 8;

    [Tooltip("眼球总数上限（加载期间不断增殖，但最多这么多颗）")]
    public int maxEyeCount = 26;

    [Tooltip("加载前期：每颗新眼球浮现的间隔秒数")]
    public float spawnIntervalStart = 1.1f;

    [Tooltip("加载后期：每颗新眼球浮现的间隔秒数（越到后面冒出得越快，压迫感递增）")]
    public float spawnIntervalEnd = 0.32f;

    [Tooltip("最小的眼球直径（画布像素，参考分辨率 1920x1080）")]
    public float minEyeWidth = 100f;

    [Tooltip("最大的眼球直径")]
    public float maxEyeWidth = 340f;

    [Header("眼部贴图（留空则运行时程序化生成）")]
    [Tooltip("通用巩膜（圆形眼白，1024x1024，由 gen_sclera_orb.py 生成，" +
             "可复用于任意角色/场景的圆形眼球）")]
    public Sprite scleraSprite;

    [Tooltip("虹膜（圆形，1024x1024）")]
    public Sprite irisSprite;

    [Tooltip("瞳孔（黑色软边圆，256x256）")]
    public Sprite pupilSprite;

    [Tooltip("高光（柔光点，256x256）")]
    public Sprite highlightSprite;

    [Header("文字")]
    [Tooltip("文字字体（留空则尝试系统黑体，再退回内置字体）")]
    public Font font;

    [Tooltip("加载提示主文字")]
    public string loadingWord = "正 在 加 载";

    [Tooltip("加载中后期浮现的低语小字，留空则不显示")]
    public string whisperWord = "它 们 正 看 着 你 …";

    [Header("注视与动画")]
    [Tooltip("注视点跟随鼠标位置（玩家的移动会被所有眼球追踪；" +
             "无鼠标或关闭时使用下方固定注视点）")]
    public bool followMouseGaze = true;

    [Tooltip("固定注视点（画布中心坐标系，不跟随鼠标时的玩家位置）")]
    public Vector2 gazeTarget = new Vector2(0f, -60f);

    [Tooltip("扫视微颤幅度比例（0 = 死盯不动，更瘆人）")]
    public float saccadeScale = 1f;

    [Tooltip("加载期间随机瞥视别处的频率（次/分钟，0 = 关闭）")]
    public float glancePerMinute = 7f;

    // ===== 运行时状态 =====

    private enum Phase { Idle, Loading, SnapFocus, Fading, Failed }

    /// <summary>一颗眼球的全部运行时数据与独立动画参数。</summary>
    private class Eye
    {
        public RectTransform root;
        public RectTransform irisPivot;
        public RectTransform pupil;
        public CanvasGroup group;          // 浮现淡入用

        public Vector2 pos;                // 在眼球层中的位置
        public float diameter;

        public float turnDelay;            // 出生后多久“注意到你”（age 计）
        public float openDuration;         // 转向玩家耗时（每颗都不同）
        public bool openStarted;
        public float openStartAge;

        public float age;                  // 出生以来的时长
        public float fadeDuration;         // 浮现淡入时长
        public float nextGlanceAt;         // 下次瞥视时刻（age 计）
        public float glanceStart;          // -1 = 未在瞥视
        public Vector2 glanceDir;          // 瞥视方向

        public float saccadePhase;         // 扫视相位（每颗错开）
        public float saccadeFreq;

        public Vector2 lookAwayDir;        // 浮现初期“看向别处”的方向
        public float dilate;               // 瞳孔缩放
        public float scaleNow;             // 当前整体缩放（平滑逼近）
    }

    private Canvas _canvas;
    private RectTransform _layer;
    private CanvasGroup _rootGroup;
    private Text _text;
    private Text _whisper;

    private readonly List<Eye> _eyes = new List<Eye>();
    private readonly List<Object> _generated = new List<Object>();   // 程序化贴图，销毁用

    private Sprite _sclera, _iris, _pupil, _highlight;

    private Phase _phase = Phase.Idle;
    private float _phaseTime;
    private float _displayed;         // 0~1 平滑显示进度
    private bool _busy;

    private float _spawnTimer;        // 距上次新眼球浮现的时长
    private int _crowdFails;          // 连续放置失败次数（屏幕太挤则停)
    private bool _spawnDone;          // 停止增殖

    private Vector2 _gazePoint;       // 当前注视点（画布中心坐标系）

    private static readonly Color BoneWhite = new Color(0.90f, 0.89f, 0.84f);

    // 中央文字安全区（眼球避让）：主文字 (0,-140) 与低语 (0,-218) 的包围盒
    private static readonly Vector2 ExcludeCenter = new Vector2(0f, -170f);
    private static readonly Vector2 ExcludeHalf = new Vector2(560f, 225f);

    // ===== 公共入口 =====

    /// <summary>确保场景里存在一个加载控制器（没有就现场创建）。</summary>
    public static LoadingEyesController EnsureInstance()
    {
        var existing = FindObjectOfType<LoadingEyesController>();
        if (existing != null) return existing;
        var go = new GameObject("LoadingEyes");
        return go.AddComponent<LoadingEyesController>();
    }

    /// <summary>
    /// 开始加载：构建群眼加载界面并异步加载目标场景。
    /// 返回的 Coroutine 可被外部协程直接 yield，等待整个演出结束。
    /// </summary>
    public Coroutine BeginLoad(string sceneName)
    {
        if (_busy)
        {
            Debug.LogWarning("[LoadingEyes] 已在加载中，忽略重复调用");
            return null;
        }
        _busy = true;
        gameSceneName = sceneName;
        return StartCoroutine(LoadRoutine());
    }

    // ===== 主流程 =====

    private IEnumerator LoadRoutine()
    {
        // 1. 构建加载界面（黑底 + 眼球层 + 文字），等一帧让 Canvas 完成布局
        BuildCanvas();
        yield return null;
        SpawnInitialEyes();

        _phase = Phase.Loading;
        _phaseTime = 0f;
        _displayed = 0f;
        _spawnTimer = 0f;
        _crowdFails = 0;
        _spawnDone = false;

        // 2. 整个界面淡入（此时所有眼球尚未浮现，只有黑屏与文字）
        float t = 0f;
        while (t < 0.35f)
        {
            t += Time.unscaledDeltaTime;
            if (_rootGroup != null)
                _rootGroup.alpha = Mathf.Clamp01(t / 0.35f);
            yield return null;
        }
        if (_rootGroup != null) _rootGroup.alpha = 1f;

        // 3. 异步加载（先不激活，等收尾演出结束再切入）
        AsyncOperation op = SceneManager.LoadSceneAsync(gameSceneName);
        if (op == null)
        {
            Debug.LogError("[LoadingEyes] 无法加载场景 \"" + gameSceneName + "\"，请确认它已加入 Build Settings。");
            yield return FailedRoutine();
            yield break;
        }
        op.allowSceneActivation = false;

        float elapsed = 0f;
        int dots = 0;
        float dotTimer = 0f;
        bool whisperOn = false;

        while (true)
        {
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            float actual = Mathf.Clamp01(op.progress / 0.9f);
            _displayed = Mathf.MoveTowards(_displayed, actual,
                dt / Mathf.Max(0.1f, progressPaceDuration));

            // 加载文字：省略号轮转 + 平滑百分比
            if (_text != null)
            {
                dotTimer += dt;
                if (dotTimer >= 0.35f) { dotTimer = 0f; dots = (dots + 1) % 4; }
                _text.text = loadingWord + new string('.', dots) + "  "
                           + Mathf.RoundToInt(_displayed * 100f) + "%";
            }

            // 低语小字：加载进行到三成后浮现
            if (!whisperOn && _whisper != null && _displayed > 0.3f)
            {
                whisperOn = true;
                StartCoroutine(FadeTextTo(_whisper, 0.55f, 1.4f));
            }

            // 眼球增殖：随进度加快冒出新的眼球；接近加载完成时停止增援，
            // 让最终这一批完成“转向注视”，收尾画面整齐
            if (!_spawnDone && _eyes.Count < maxEyeCount && actual < 0.95f)
            {
                _spawnTimer += dt;
                float interval = Mathf.Lerp(spawnIntervalStart, spawnIntervalEnd, _displayed);
                if (_spawnTimer >= interval)
                {
                    _spawnTimer = 0f;
                    if (TrySpawnEye())
                        _crowdFails = 0;
                    else if (++_crowdFails >= 12)
                        _spawnDone = true;              // 屏幕太挤，放不下了
                }
            }

            // 所有眼球都完成转向后才允许收尾（附超时保护，避免极端情况下卡住）
            bool allOpen = true;
            for (int i = 0; i < _eyes.Count; i++)
            {
                var e = _eyes[i];
                if (!e.openStarted || e.age - e.openStartAge < e.openDuration)
                { allOpen = false; break; }
            }

            if (actual >= 1f && _displayed >= 0.999f && elapsed >= minShowDuration &&
                (allOpen || elapsed >= minShowDuration + 5f))
                break;

            yield return null;
        }

        // 4. 收尾：所有瞳孔骤然放大、逼近瞪住（注视点仍跟随鼠标），
        //    随后整个界面淡入黑暗、切入新场景——眼球随界面一起被清理
        _phase = Phase.SnapFocus; _phaseTime = 0f;
        yield return new WaitForSecondsRealtime(0.5f);

        _phase = Phase.Fading; _phaseTime = 0f;
        if (_text != null) StartCoroutine(FadeTextTo(_text, 0f, 0.22f));
        if (_whisper != null) StartCoroutine(FadeTextTo(_whisper, 0f, 0.18f));
        float fadeT = 0f;
        while (fadeT < 0.26f)
        {
            fadeT += Time.unscaledDeltaTime;
            if (_rootGroup != null)
                _rootGroup.alpha = 1f - Mathf.Clamp01(fadeT / 0.26f);
            yield return null;
        }

        // 5. 激活新场景（本界面随旧场景一起销毁，眼球元素一并清理）
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;
    }

    /// <summary>加载失败：文字报错、界面整体淡出拆除，回调恢复菜单。</summary>
    private IEnumerator FailedRoutine()
    {
        _phase = Phase.Failed; _phaseTime = 0f;
        if (_text != null) _text.text = "加 载 失 败";
        yield return new WaitForSecondsRealtime(0.55f);

        if (_text != null) StartCoroutine(FadeTextTo(_text, 0f, 0.3f));
        if (_whisper != null) StartCoroutine(FadeTextTo(_whisper, 0f, 0.25f));

        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.unscaledDeltaTime;
            if (_rootGroup != null)
                _rootGroup.alpha = 1f - Mathf.Clamp01(t / 0.6f);
            yield return null;
        }

        CleanupUI();
        if (onFailed != null) onFailed();
    }

    /// <summary>彻底拆除本次加载界面（眼球、文字、画布全部销毁，状态复位）。</summary>
    private void CleanupUI()
    {
        if (_canvas != null) Destroy(_canvas.gameObject);
        _canvas = null;
        _layer = null;
        _rootGroup = null;
        _text = null;
        _whisper = null;
        _eyes.Clear();
        _phase = Phase.Idle;
        _displayed = 0f;
        _spawnDone = false;
        _busy = false;
    }

    // ===== 每帧驱动：浮现、增殖、转向注视（跟随鼠标）、瞥视、瞳孔 =====

    private void Update()
    {
        if (_phase == Phase.Idle || _canvas == null) return;
        float dt = Time.unscaledDeltaTime;
        _phaseTime += dt;
        bool snapping = _phase != Phase.Loading;   // 收尾各阶段：锁定注视

        UpdateGazePoint();

        for (int i = 0; i < _eyes.Count; i++)
        {
            Eye e = _eyes[i];
            e.age += dt;

            // 1) 触发“注意到你”（出生后延时驱动；初始一批错峰，营造一颗颗注意到的节奏）
            if (!e.openStarted && e.age >= e.turnDelay)
            {
                e.openStarted = true;
                e.openStartAge = e.age;
            }

            // 2) 浮现淡入（出生即开始，营造从黑暗里渗出来的感觉）
            if (e.group != null)
                e.group.alpha = Mathf.Clamp01(e.age / e.fadeDuration);

            // 3) 浮现转向进度：easeInCubic——先是漫不经心，最后一刻猛地转过来定住
            float p = 0f;
            if (e.openStarted)
                p = Mathf.Clamp01((e.age - e.openStartAge) / e.openDuration);

            // 4) 当前注视方向：从“看向别处”转向注视点（鼠标位置/玩家位置）
            Vector2 toGaze = _gazePoint - e.pos;
            Vector2 gazeDir = toGaze.sqrMagnitude > 1f ? toGaze.normalized : Vector2.zero;
            Vector2 dir = Vector2.Lerp(e.lookAwayDir, gazeDir, EaseInCubic(p));

            // 5) 随机瞥视（仅正常加载阶段且已完成转向）：快速看向别处再回来
            if (_phase == Phase.Loading && p >= 1f && glancePerMinute > 0f &&
                e.age >= e.nextGlanceAt)
            {
                e.glanceStart = e.age;
                float a = Random.Range(0f, Mathf.PI * 2f);
                e.glanceDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                e.nextGlanceAt = e.age + 60f / glancePerMinute + Random.Range(-1.5f, 2.5f);
            }
            bool glancing = false;
            if (e.glanceStart >= 0f)
            {
                float gp = (e.age - e.glanceStart) / 0.55f;
                if (gp >= 1f) e.glanceStart = -1f;
                else
                {
                    glancing = true;
                    // 瞥视期间方向大部分时间停在别处，末段快速归位
                    if (gp < 0.7f) dir = Vector2.Lerp(dir, e.glanceDir, 0.85f);
                    else dir = Vector2.Lerp(e.glanceDir, dir, (gp - 0.7f) / 0.3f);
                }
            }

            // 6) 扫视微颤（收尾与瞥视时停用：死盯更瘆人）
            float saccadeAmp = (snapping || glancing) ? 0f : 0.30f * saccadeScale;
            float sx = Mathf.Sin(e.age * e.saccadeFreq) * 0.6f
                     + Mathf.Sin(e.age * e.saccadeFreq * 2.31f + e.saccadePhase) * 0.4f;
            float sy = Mathf.Sin(e.age * e.saccadeFreq * 0.83f + e.saccadePhase * 0.6f) * 0.6f
                     + Mathf.Sin(e.age * e.saccadeFreq * 1.9f + e.saccadePhase) * 0.4f;
            float travel = e.diameter * 0.16f;            // 虹膜移动半径
            Vector2 gazeTargetPos = new Vector2(
                dir.x * travel + sx * saccadeAmp * travel,
                dir.y * travel + sy * saccadeAmp * travel);
            float follow = snapping ? 1f - Mathf.Exp(-28f * dt) : 1f - Mathf.Exp(-9f * dt);
            if (e.irisPivot != null)
                e.irisPivot.anchoredPosition = Vector2.Lerp(
                    e.irisPivot.anchoredPosition, gazeTargetPos, follow);

            // 7) 整体缩放：浮现时从黑暗中挤出来（0.55→1，带轻微回弹），之后保持
            float targetScale = 1f;
            if (!e.openStarted)
                targetScale = 0.55f;
            else if (p < 1f)
                targetScale = Mathf.Lerp(0.55f, 1f, EaseOutBack(p, 0.8f));

            // 8) 收尾阶段修饰：逼近瞪大（无缩小演出；淡出由整界面负责）
            if (snapping)
                targetScale = Mathf.Max(targetScale, 1f) + 0.14f * Mathf.Clamp01(_phaseTime / 0.35f);

            e.scaleNow = Mathf.Lerp(e.scaleNow, targetScale,
                1f - Mathf.Exp(-30f * dt));              // 平滑，避免跳变
            if (e.root != null)
                e.root.localScale = new Vector3(e.scaleNow, e.scaleNow, 1f);

            // 9) 瞳孔：平时呼吸，锁定时骤然放大
            float dilate = 1f + 0.045f * Mathf.Sin(e.age * 2.3f + e.saccadePhase);
            if (snapping)
            {
                float targetDilate = _phase == Phase.SnapFocus ? 1.6f : 1.15f;
                e.dilate = Mathf.MoveTowards(e.dilate, targetDilate, dt * 2.4f);
                dilate = e.dilate;
            }
            else
            {
                e.dilate = 1f;
            }
            if (e.pupil != null)
                e.pupil.localScale = new Vector3(dilate, dilate, 1f);
        }
    }

    /// <summary>
    /// 计算当前注视点（画布中心坐标系，与眼球层同系）：
    /// 默认跟随鼠标位置（玩家的移动会被所有眼球追踪），无鼠标时退回固定注视点。
    /// </summary>
    private void UpdateGazePoint()
    {
        if (followMouseGaze && Input.mousePresent && _canvas != null)
        {
            // ScreenSpaceOverlay 画布：camera 参数传 null
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.transform as RectTransform, Input.mousePosition, null,
                    out Vector2 local))
            {
                _gazePoint = local;
                return;
            }
        }
        _gazePoint = gazeTarget;
    }

    // ===== 界面构建 =====

    private void BuildCanvas()
    {
        if (_canvas != null) return;

        var go = new GameObject("LoadingEyesCanvas", typeof(Canvas), typeof(CanvasScaler));
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;                      // 菜单(10)、触手(60) 之上

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRT = go.GetComponent<RectTransform>();

        _rootGroup = go.AddComponent<CanvasGroup>();
        _rootGroup.alpha = 0f;
        _rootGroup.blocksRaycasts = true;               // 加载期间吞掉所有输入

        // 纯黑背景
        var bg = CreateImage("Background", rootRT, null, Color.black, true);
        Stretch(bg.rectTransform, 0, 0, 0, 0);

        // 眼球层
        var layer = new GameObject("EyesLayer", typeof(RectTransform));
        layer.transform.SetParent(rootRT, false);
        _layer = layer.GetComponent<RectTransform>();
        Stretch(_layer, 0, 0, 0, 0);

        // 加载主文字：保留旧版的“正 在 加 载”，融入布局（眼球避开其周围）
        _text = CreateText("LoadingText", rootRT, 44,
                           new Color(BoneWhite.r, BoneWhite.g, BoneWhite.b, 0.92f), loadingWord);
        Center(_text.rectTransform, new Vector2(0f, -140f), new Vector2(900f, 80f));
        _text.raycastTarget = false;

        // 低语小字
        if (!string.IsNullOrEmpty(whisperWord))
        {
            _whisper = CreateText("Whisper", rootRT, 26,
                                  new Color(0.55f, 0.42f, 0.40f, 0f), whisperWord);
            Center(_whisper.rectTransform, new Vector2(0f, -218f), new Vector2(900f, 50f));
            _whisper.raycastTarget = false;
        }
    }

    // ===== 眼球生成与增殖 =====

    /// <summary>
    /// 初始撒下第一批大小、位置、节奏都不同的眼球（加载期间还会不断增殖）。
    /// 尺寸用“均分错位打乱”保证初始一批彼此直径明显不同。
    /// </summary>
    private void SpawnInitialEyes()
    {
        EnsureSprites();

        int count = Mathf.Clamp(minEyeCount, 1, maxEyeCount);

        // 差异化尺寸：把 [0,1] 均分错位后打乱
        var slots = new List<float>();
        for (int i = 0; i < count; i++)
            slots.Add((i + 0.15f + Random.value * 0.7f) / count);
        for (int i = slots.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }

        // 错峰转向延时：一颗颗“注意到你”（配合进度爬升的节奏）
        var delays = new List<float>();
        for (int i = 0; i < count; i++)
            delays.Add(Mathf.Lerp(0.15f, 2.6f, (float)i / Mathf.Max(1, count - 1))
                       + Random.Range(-0.12f, 0.12f));
        for (int i = delays.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (delays[i], delays[j]) = (delays[j], delays[i]);
        }

        for (int i = 0; i < count; i++)
        {
            float diameter = Mathf.Lerp(minEyeWidth, maxEyeWidth, slots[i]);
            if (!TryFindPlacement(diameter, out Vector2 pos)) continue;   // 放不下就少一颗
            CreateEye(pos, diameter, Mathf.Max(0.1f, delays[i]));
        }
    }

    /// <summary>加载期间增殖出一颗新眼球：位置/尺寸随机，偏向更小以填补空隙。</summary>
    private bool TrySpawnEye()
    {
        // 越到后期新生眼球越偏小：既像“暗处又冒出一双”，也更容易塞进空隙
        float sizeK = Random.value * Mathf.Lerp(1f, 0.62f, _displayed);
        float diameter = Mathf.Lerp(minEyeWidth, maxEyeWidth, sizeK);

        if (!TryFindPlacement(diameter, out Vector2 pos)) return false;
        CreateEye(pos, diameter, Random.Range(0.45f, 1.5f));
        return true;
    }

    /// <summary>
    /// 为直径 diameter 的眼球找一个合规位置：屏幕可视范围内、避开标题安全区、
    /// 与既有眼球不过度重叠（圆-圆检测，允许少量贴近更密集压抑）。
    /// </summary>
    private bool TryFindPlacement(float diameter, out Vector2 pos)
    {
        pos = Vector2.zero;
        if (_layer == null) return false;

        float halfW = _layer.rect.width * 0.5f;
        float halfH = _layer.rect.height * 0.5f;
        if (halfW < 10f) halfW = 960f;                  // Canvas 尚未就绪时的兜底
        if (halfH < 10f) halfH = 540f;

        float radius = diameter * 0.5f;
        float m = radius + 40f;                          // 屏幕边缘留白
        float xMin = -halfW + m, xMax = halfW - m;
        float yMin = -halfH + m, yMax = halfH - m;
        if (xMax <= xMin || yMax <= yMin) return false;  // 眼球比屏幕还大

        for (int attempt = 0; attempt < 70; attempt++)
        {
            Vector2 cand = new Vector2(Random.Range(xMin, xMax), Random.Range(yMin, yMax));

            // 避开中央文字安全区（含安全边距）
            if (Mathf.Abs(cand.x - ExcludeCenter.x) < ExcludeHalf.x + radius + 24f &&
                Mathf.Abs(cand.y - ExcludeCenter.y) < ExcludeHalf.y + radius + 24f)
                continue;

            // 与已放置的眼球保持距离
            bool ok = true;
            for (int k = 0; k < _eyes.Count; k++)
            {
                Vector2 d = cand - _eyes[k].pos;
                if (d.magnitude < (radius + _eyes[k].diameter * 0.5f) * 0.86f + 8f)
                { ok = false; break; }
            }
            if (ok) { pos = cand; return true; }
        }
        return false;                                    // 试遍了都放不下
    }

    /// <summary>创建一颗眼球：眼球本体 + 虹膜/瞳孔/高光，无眼睑无眼眶，全部程序化组装。</summary>
    private void CreateEye(Vector2 pos, float diameter, float turnDelay)
    {
        var root = new GameObject("Eye_" + _eyes.Count, typeof(RectTransform));
        root.transform.SetParent(_layer, false);
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(diameter, diameter);
        rt.localScale = Vector3.one * 0.55f;            // 初始小，浮现时挤出来

        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        // 眼球本体：通用巩膜贴图（圆形眼白，自带球体明暗与血丝），虹膜叠加在中心
        var ball = CreateImage("Ball", root.transform, _sclera, Color.white, false);
        Stretch(ball.rectTransform, 0, 0, 0, 0);

        // 虹膜容器（注视时整体平移）
        var pivot = new GameObject("IrisPivot", typeof(RectTransform));
        pivot.transform.SetParent(root.transform, false);
        RectTransform pivotRT = pivot.GetComponent<RectTransform>();
        pivotRT.anchorMin = pivotRT.anchorMax = pivotRT.pivot = new Vector2(0.5f, 0.5f);
        pivotRT.anchoredPosition = Vector2.zero;
        pivotRT.sizeDelta = Vector2.zero;

        float irisSize = diameter * Random.Range(0.40f, 0.47f);
        var iris = CreateImage("Iris", pivot.transform, _iris, Color.white, false);
        Center(iris.rectTransform, Vector2.zero, new Vector2(irisSize, irisSize));

        float pupilSize = irisSize * 0.42f;
        var pupilImg = CreateImage("Pupil", pivot.transform, _pupil, Color.white, false);
        Center(pupilImg.rectTransform, Vector2.zero, new Vector2(pupilSize, pupilSize));

        var hi = CreateImage("Highlight", pivot.transform, _highlight, Color.white, false);
        Center(hi.rectTransform, new Vector2(-irisSize * 0.18f, irisSize * 0.20f),
               new Vector2(irisSize * 0.24f, irisSize * 0.24f));

        // “看向别处”的初始方向：大部分刻意背向注视点（转过身去），一部分随机
        Vector2 toGaze = gazeTarget - pos;
        Vector2 gazeDir = toGaze.sqrMagnitude > 1f ? toGaze.normalized : Vector2.zero;
        Vector2 away = -gazeDir;
        if (Random.value < 0.3f)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            away = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        // 加一点抖动避免所有眼球初始方向完全对称
        away = (away + new Vector2(Random.Range(-0.35f, 0.35f),
                                   Random.Range(-0.35f, 0.35f))).normalized;

        _eyes.Add(new Eye
        {
            root = rt,
            irisPivot = pivotRT,
            pupil = pupilImg.rectTransform,
            group = group,
            pos = pos,
            diameter = diameter,
            turnDelay = turnDelay,
            openDuration = Random.Range(0.6f, 1.25f),
            openStarted = false,
            openStartAge = 0f,
            age = 0f,
            fadeDuration = Random.Range(0.4f, 0.6f),
            nextGlanceAt = Random.Range(2.2f, 8f),
            glanceStart = -1f,
            glanceDir = Vector2.zero,
            saccadePhase = Random.Range(0f, 6.28f),
            saccadeFreq = Random.Range(0.7f, 1.6f),
            lookAwayDir = away,
            dilate = 1f,
            scaleNow = 0.55f,
        });
    }

    // ===== 程序化贴图（未接线时的兜底）=====

    private void EnsureSprites()
    {
        // 优先使用通用巩膜贴图（sclera_orb.png）；未接线时回退到程序化生成
        _sclera = scleraSprite != null ? scleraSprite : GenEyeballSprite();
        _iris = irisSprite != null ? irisSprite : GenIrisSprite();
        _pupil = pupilSprite != null ? pupilSprite : GenPupilSprite();
        _highlight = highlightSprite != null ? highlightSprite : GenHighlightSprite();
    }

    private Sprite MakeSprite(Texture2D tex)
    {
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        _generated.Add(tex);
        var s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                              new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        _generated.Add(s);
        return s;
    }

    /// <summary>
    /// 程序化巩膜兜底（未接线 sclera_orb.png 时使用）：
    /// 圆形充血眼球——中心淡黄、边缘泛红发暗，爬满从边缘钻向中心的血丝。
    /// 正式素材请运行项目根目录的 gen_sclera_orb.py 生成 sclera_orb.png 并接线。
    /// </summary>
    private Sprite GenEyeballSprite()
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S * 0.5f) / (S * 0.5f);
                float dy = (y - S * 0.5f) / (S * 0.5f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                int i = y * S + x;
                if (r >= 1f) { px[i] = Color.clear; continue; }

                float a = 1f - Mathf.SmoothStep(0.96f, 1f, r);       // 边缘极窄软化

                // 球体底色：中心苍白微黄，边缘充血暗红（巩膜外露的病态感）
                float rim = Mathf.SmoothStep(0.45f, 1.0f, r);
                float cr = Mathf.Lerp(0.91f, 0.72f, rim);
                float cg = Mathf.Lerp(0.89f, 0.42f, rim);
                float cb = Mathf.Lerp(0.84f, 0.36f, rim);

                // 上下球体投影：让圆有立体感，像悬浮在黑暗里的一颗球
                float sh = Mathf.SmoothStep(0.35f, 1.0f, Mathf.Abs(dy));
                float shade = 1f - 0.30f * sh;

                // 眼球后缘暗环：把球从黑背景里“压”出来
                float back = Mathf.SmoothStep(0.86f, 1.0f, r);
                float dark = 1f - 0.42f * back;

                px[i] = new Color(cr * shade * dark, cg * shade * dark, cb * shade * dark, a);
            }
        }

        // 血丝：从眼球边缘向中心蜿蜒钻入，越靠中心越细越淡
        for (int v = 0; v < 22; v++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float a0 = ang + Random.Range(-0.5f, 0.5f);
            float a1 = ang + Random.Range(-1.1f, 1.1f);
            float r0 = 0.97f;
            float r1 = Random.Range(0.42f, 0.72f);
            float wob = Random.Range(0.06f, 0.16f);
            float freq = Random.Range(1.5f, 4f);
            float ph = Random.Range(0f, 6.28f);

            for (int s = 0; s <= 160; s++)
            {
                float tt = s / 160f;
                float rr = Mathf.Lerp(r0, r1, tt);
                float aa = Mathf.Lerp(a0, a1, tt)
                         + Mathf.Sin(tt * freq * Mathf.PI + ph) * wob * (0.3f + 0.7f * tt);
                float vx = S * (0.5f + Mathf.Cos(aa) * rr * 0.5f);
                float vy = S * (0.5f + Mathf.Sin(aa) * rr * 0.5f);
                int ix = Mathf.RoundToInt(vx), iy = Mathf.RoundToInt(vy);

                // 越靠近中心笔刷越细
                int brush = tt < 0.6f ? 1 : 0;
                for (int dy2 = -brush; dy2 <= brush; dy2++)
                {
                    for (int dx2 = -brush; dx2 <= brush; dx2++)
                    {
                        int xx = ix + dx2, yy = iy + dy2;
                        if (xx < 0 || xx >= S || yy < 0 || yy >= S) continue;
                        var c = px[yy * S + xx];
                        if (c.a <= 0f) continue;
                        float k = (dx2 == 0 && dy2 == 0) ? 1f : 0.4f;
                        c.r = Mathf.Min(1f, c.r + 0.04f * k);       // 血丝偏红
                        c.g = Mathf.Max(0f, c.g - 0.24f * k);
                        c.b = Mathf.Max(0f, c.b - 0.28f * k);
                        px[yy * S + xx] = c;
                    }
                }
            }
        }

        tex.SetPixels(px);
        return MakeSprite(tex);
    }

    /// <summary>虹膜：病态琥珀色，放射状纤维纹 + 角膜缘暗环。</summary>
    private Sprite GenIrisSprite()
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S * 0.5f) / (S * 0.5f);
                float dy = (y - S * 0.5f) / (S * 0.5f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                int i = y * S + x;
                if (r >= 1f) { px[i] = Color.clear; continue; }

                float a = Mathf.SmoothStep(1f, 0.93f, r);
                float ang = Mathf.Atan2(dy, dx);
                float streak = Mathf.PerlinNoise(Mathf.Cos(ang) * 2.2f + 7.3f,
                                                 Mathf.Sin(ang) * 2.2f + r * 6.5f);
                float k = 0.72f + 0.55f * streak;
                float cr = 0.64f * k, cg = 0.38f * k, cb = 0.13f * k;

                float ring = Mathf.SmoothStep(0.78f, 0.97f, r);        // 角膜缘暗环
                cr = Mathf.Lerp(cr, 0.10f, ring);
                cg = Mathf.Lerp(cg, 0.05f, ring);
                cb = Mathf.Lerp(cb, 0.03f, ring);

                float core = 1f - Mathf.SmoothStep(0.10f, 0.26f, r);   // 中心偏暗
                cr = Mathf.Lerp(cr, 0.05f, core * 0.85f);
                cg = Mathf.Lerp(cg, 0.03f, core * 0.85f);
                cb = Mathf.Lerp(cb, 0.02f, core * 0.85f);

                px[i] = new Color(cr, cg, cb, a);
            }
        }
        tex.SetPixels(px);
        return MakeSprite(tex);
    }

    /// <summary>瞳孔：软边黑圆。</summary>
    private Sprite GenPupilSprite()
    {
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S * 0.5f) / (S * 0.5f);
                float dy = (y - S * 0.5f) / (S * 0.5f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                int i = y * S + x;
                float a = r >= 1f ? 0f : 1f - Mathf.SmoothStep(0.80f, 1f, r);
                px[i] = new Color(0.02f, 0.01f, 0.01f, a);
            }
        }
        tex.SetPixels(px);
        return MakeSprite(tex);
    }

    /// <summary>高光：柔光点。</summary>
    private Sprite GenHighlightSprite()
    {
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S * 0.5f) / (S * 0.5f);
                float dy = (y - S * 0.5f) / (S * 0.5f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                int i = y * S + x;
                float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.1f) * 0.92f;
                px[i] = new Color(0.98f, 0.98f, 0.95f, a);
            }
        }
        tex.SetPixels(px);
        return MakeSprite(tex);
    }

    // ===== 小工具 =====

    private Font ResolveFont()
    {
        if (font != null) return font;
        font = Font.CreateDynamicFontFromOSFont("SimHei", 44);
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 44);
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font;
    }

    private Image CreateImage(string name, Transform parent, Sprite sprite, Color color, bool raycast)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    private Text CreateText(string name, Transform parent, int size, Color color, string content)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = ResolveFont();
        t.fontSize = size;
        t.color = color;
        t.text = content;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private static void Center(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static float EaseInCubic(float x)
    {
        return x * x * x;
    }

    private static float EaseOutBack(float x, float overshoot)
    {
        const float c1 = 1.70158f;
        float c3 = c1 * overshoot;
        float xm1 = x - 1f;
        return 1f + (c3 + 1f) * xm1 * xm1 * xm1 + c3 * xm1 * xm1;
    }

    private static IEnumerator FadeTextTo(Text t, float targetAlpha, float dur)
    {
        if (t == null) yield break;
        float from = t.color.a;
        float time = 0f;
        while (time < dur)
        {
            time += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(time / dur);
            Color c = t.color;
            c.a = Mathf.Lerp(from, targetAlpha, k);
            t.color = c;
            yield return null;
        }
        Color finalC = t.color;
        finalC.a = targetAlpha;
        t.color = finalC;
    }

    private void OnDestroy()
    {
        // 程序化贴图随脚本销毁一起释放；若加载界面画布仍在（异常路径），一并拆除
        for (int i = 0; i < _generated.Count; i++)
            if (_generated[i] != null) Destroy(_generated[i]);
        _generated.Clear();
        if (_canvas != null)
        {
            Destroy(_canvas.gameObject);
            _canvas = null;
        }
    }
}
