# MaidHome

Unity 游戏工程，需要和 Minecraft 模组（NeoForge 1.21.1 生态，例如 Touhou Little Maid、Sable）打交道。

## 工程事实

- Unity **2020.3.30f1c1**（Unity 中国版，`Unity.exe` 的 ProductVersion 是 `2020.3.30f1c1_b9cd9c36b8f8`，Hub 目录名仍叫 `2020.3.30f1`），Editor 位于 `C:\Program Files\Unity\Hub\Editor\2020.3.30f1\Editor\Unity.exe`；`ProjectVersion.txt` 已经是 c1，别再用国际版编辑器打开（会被来回"升级"）。模块、SDK/NDK/JDK 都要配 c1 那一套
- Api 兼容级别 .NET Standard 2.0，脚本后端默认 Mono，内置渲染管线，Gamma 色彩空间
- 已装包见 `Packages/manifest.json`（TMP 3.0.6、Test Framework 1.1.29、Timeline、uGUI）。没有 Newtonsoft.Json、没有 Addressables
- 改 `Packages/manifest.json` 会触发下载，需要先说明理由

## 平台目标

- Android + Windows 双平台，**Android 走 APK 分发，不发布 Google Play**。所以不考虑商店审核限制，`MANAGE_EXTERNAL_STORAGE` 可以用
- 涉及平台差异的代码必须双实现（Windows / Android），公共逻辑抽到平台无关层，不把 `#if` 散落到玩法代码里
- Android 构建前置：Android Build Support 模块**必须和编辑器同源同版本**（国际版配国际版，中国版 `c1` 配 `c1`）。踩过：从 unity.cn 下到的包文件名是国际版名字、内部却是 `Unity 2020.3.30f1c1 Android Support`，装进国际版编辑器后 Player Settings 直接 `MissingMethodException: PlayerSettings.GetSecurityBuildForPlatform`（该 API 不在国际版核心里）。下载后先 `(Get-Item <包>).VersionInfo.ProductName` 确认，别只看文件名
- Android Target API 用 30（Android 11）。Unity 2020.3 自带的 Gradle/AGP 版本老，调更高要改 gradle 模板，没必要
- 需要声明权限时要加自定义 `AndroidManifest.xml`

## 代码规范

- 注释从简：只在关键、非直觉、有坑的地方写；不写 "Start is called before the first frame update" 这类模板注释，不逐行复述代码
- 命名：类型/方法/属性/常量 PascalCase，局部变量和参数 camelCase，私有字段 `_camelCase`，接口 `I` 前缀
- Inspector 字段用 `[SerializeField] private`，不暴露 public 字段
- 一个文件一个 public 类型，文件名和类型名一致
- 需要注释时用中文；标识符用英文
- 不过度抽象：优先直接实现，不为"以后可能需要"提前分层

## 交互输入

- **全工程只认触摸，PC 和 Android 走同一条路**：点击/拖拽一律通过 `Assets/Scripts/Core/Input/PointerInput.cs`（内部是 `Input.touchCount` / `Input.GetTouch`），交互脚本不许直接读 `Input`、不许用 `OnMouseXxx`
- 单指交互用 `PointerInput.Primary`（先按下的那根手指）。需要多指（例如摸脸同时拽两只耳朵、同时戳两只眼睛）用 `PointerInput.CopyPointers(list)` 拿这一帧全部手指：顺序稳定（`[0]` 就是 Primary），每根手指的 `OverUi` 在它按下那一刻定死，抬起那一帧还会在列表里带 `Released`；系统把手指吞掉时会补一个 `Released`。`Primary` 的行为和以前一样，别的地方不用改
- 判断"手指是不是压在 UI 上"用 `PointerInput.IsOverUi`（自己发一次 GraphicRaycaster），**别用 `EventSystem.IsPointerOverGameObject`** —— 那份缓存要等 EventSystem 自己的 Update，和自己 Update 的先后顺序不定，按下那一帧会误判成没压在 UI 上
- `PointerInput.Pointer.OverUi` 在按下那一刻定死，整段手势沿用，拖到一半划到按钮上不算
- 编辑器里开 Device Simulator 的 Touch 模式模拟触摸（它的 legacy 后端就是 `Input.SimulateTouch`，`Input.touchCount` 照常工作）；不再维护鼠标分支
- Device Simulator 同时会把 `Application.platform` / `isMobilePlatform` / `isEditor` 伪装成 Android，凡是按平台分支的代码都别信这些运行时属性，见「存档约定」
- uGUI 的 Button/onClick 触摸和鼠标都吃，不用改；场景里的 `StandaloneInputModule` 在 Android 上也处理触摸
- 触摸没有 hover：需要预览的地方按"按住时更新 + 松手保留"，确认/取消/调朝向一律给按钮（例：`BagPlacementPanel`）
- 界面显隐一律代码驱动，不用 Animator（`Assets/Anim/Backpack` 那套已经删了）：`BagPanel` 用 `_slideRect` + `_hiddenOffset` 自己滑出/滑入，**场景里摆的位置就是显示位**，滑完才 `SetActive(false)`；`InvButton` 想跟着挪就接 `_toggleRect`
- 自动生成的占位 UI（摸尾巴底栏 `MaidTailPanel`、摸脸底栏 `MaidFacePanel`、放置条 `BagPlacementPanel`、女仆面板 `MaidInteractionPanel`）都留了自定义槽位：接上根节点/文字/按钮就用你的，一个都不接才自动生成；接了以后这里不碰布局
- 「摸脸」按钮是 `MaidInteractionPanel._faceButton`：自定义面板没接的话，运行时会照「摸尾巴」按钮的位置尺寸在面板底部中间补一个；接了自己的就不补
- 静态检查：`python Tools/touch_input_check.py`

## 目录约定

- `Assets/Scripts/Core`：基础设施（启动、存档、配置、事件、输入）
- `Assets/Scripts/Gameplay`：游戏玩法逻辑
- `Assets/Scripts/Interop`：与 Minecraft 侧的数据/进程交互（NBT/Anvil 解析、Bedrock 模型转换、外部进程客户端）
- `Assets/Scripts/Editor`：仅编辑器工具（资源导入器、代码生成器）
- `Assets/Shaders`：自定义 ShaderLab
- `Assets/Materials`：手调的材质资产（天空盒、方块样板等）
- `Assets/Scenes`：场景。新增场景要加进 `EditorBuildSettings`

## 数据存放约定

按"只读随包"和"可下载可写"两条线分开，别混。

- `Assets/StreamingAssets/mcdata`：随包发布、只读的原始数据（geo/animation/texture/json）。开发期调试用的本地副本放这里，Android 上必须用 `UnityWebRequest` 读，不能 `File.ReadAllBytes`
- `Application.persistentDataPath/mcdata`：运行时下载的数据，目录结构与 StreamingAssets 下的 `mcdata` 保持一致
- `Application.persistentDataPath/cache`：可再生的中间产物
- `Application.persistentDataPath/tmp`：下载中的半成品，写完再 `File.Move` 覆盖正式文件
- `Application.persistentDataPath/saves`：玩家存档，**独立目录，清理缓存的代码不许碰这里**
- persistentDataPath 在 Android 上**卸载会一起删除**（覆盖安装/更新则保留）。下载类数据丢了能重下，无所谓；存档虽然也放这里，但要额外做云同步或导出才能跨卸载保留
- 存档要跨设备/跨卸载保留只能靠云同步或用户手动导出，换本地目录解决不了

## 存档约定

- Windows 存档目录：`%USERPROFILE%\Documents\MaidHome\saves`（用 `Environment.GetFolderPath(SpecialFolder.MyDocuments)` 解析，不硬编码，玩家可能重定向到 OneDrive 或别的盘）
- **判断是不是 Android 真机只能用编译期的 `#if UNITY_ANDROID && !UNITY_EDITOR`，不能用 `Application.platform` / `isMobilePlatform` / `isEditor`**：Device Simulator 的 Application shim 会把这三个一起伪装成 Android（默认白名单就含 `Assembly-CSharp`），编辑器里的存档会悄悄落到 `persistentDataPath`（`AppData\LocalLow\<公司>\<产品>`），表现成"存档跑到 C:\Users\...\AppData 去了"
- Android 存档目录（拿到权限后）：`/storage/emulated/0/Documents/MaidHome/saves`；拿不到权限就退回 `persistentDataPath/saves`。**已经实现**：`PublicStorage`（唯一带 `#if` 的平台层，Android 走向 Java 桥，编辑器/Windows 直接给"我的文档"）+ `PublicStorageGate`（Android 真机 `[RuntimeInitializeOnLoadMethod]` 自建，启动自动申请一次权限、回前台再算一次）+ `AppPaths.Refresh()`（目录变化时**只补拷不覆盖、不删**，切换后所有 `AppPaths.*Root` 调用点都是现算的，天然跟得上）
- 存档文件名 `slot0.json`，同时维护 `slot0.bak`；配置放 `config/settings.json`
- 格式 JSON，UTF-8，带 `version` 字段，方便以后改结构时做迁移
- 写入必须原子化：先写 `slot0.tmp`，关文件流后再 `File.Move` 覆盖 `slot0.json`，并保留一份 `slot0.bak`
- 不用 `PlayerPrefs` 存存档，它只适合放开关、音量这类几 KB 的设置
- Android 上不能依赖 `OnApplicationQuit` 保存（进程可能被系统直接杀掉），要在 `OnApplicationPause(true)` / `OnApplicationFocus(false)` 和关键节点立即存
- 存档内容是不受信任的：读的时候要处理文件缺失、JSON 解析失败、字段越界，坏档退回 `.bak`
- 存档结构可以变，但必须靠 `version` 写迁移函数，**不许出现"旧版本存档读不出来"**；旧档升级失败时至少保留原文件不动
- 更新游戏（覆盖安装）保留存档，只有卸载会清空
- 包名（`applicationIdentifier`）要在发布前就定死，发布后改包名等于换了一个游戏：存档路径、云存档、商店评价全部断开
- Android 上 Unity 内置只有 `persistentDataPath`（应用专属外部目录，卸载即删）这一个可写路径；公共 `Documents` 没有内置 API。本工程不走 SAF/MediaStore（那是给要过 Google Play 审核的准备），而是直接要 **MANAGE_EXTERNAL_STORAGE**（APK 分发不审），Java 桥只负责「查权限 / 申请权限 / 返回能写的路径」，真正读写还是 C# 的 `File`/`Directory`。清单在 `Assets/Plugins/Android/AndroidManifest.xml`（`MANAGE_EXTERNAL_STORAGE` + `WRITE_EXTERNAL_STORAGE`(≤29) + `READ_EXTERNAL_STORAGE`(≤32) + `INTERNET`，Portal 走 loopback TCP 也需要它），里面 `package="com.maidhome.game"` 必须和 Player Settings 的包名一致
- 代码只接受一个"数据根目录"，启动时决定用本地还是远端，调用方不关心来源
- 不使用 `Assets/Resources` 放这些数据（唯一例外：moreanimation 的 `slap.ogg` 放在 `Assets/Resources/MoreAnimation/`，运行时 `Resources.Load("MoreAnimation/slap")` 兜底取，省得忘了在场景里接线就没声音）
- 模型走"下载原始 JSON/PNG + 运行时构建网格"，不走 AssetBundle/Addressables（跨平台 bundle 不通用，Android 和 Windows 要分别构建）
- 下载用 `UnityWebRequest` + `DownloadHandlerFile` 直接写盘，配 manifest（版本 + sha256）决定要不要重新下
- 路径一律 `Path.Combine`；Android 区分大小写，文件名大小写必须准确

## Minecraft 互操作约定

- 优先文件解耦：模组侧导出 JSON/PNG，Unity 侧只读文件，不直接依赖 Java 类
- 需要实时数据时走 loopback 上的 TCP/WebSocket，消息用 JSON，代码集中在 Interop 层，方便单测
- 已知要覆盖的能力：Bedrock 几何 + 动画播放、Java 方块/物品模型近似解析、NBT/Anvil（`.mca`、`level.dat`）读取、纹理映射
- 解析第三方格式的代码要留单元测试，格式细节以样本文件为准，不靠猜

## 验证

- **编译由用户自己在 Unity 里做，不要跑编译**（batchmode 因许可激活失败过，反复尝试只是浪费 token）
- 要验证就写纯 Python / 纯数据的脚本放到 `Tools/`（脚本的产物放 `Tools/out/`，已在 .gitignore 里）
- 纯 C# 工具类（解析器、转换器）优先写 EditMode 测试，Test Framework 已装
- 现成的静态检查：`python Tools/mc_style_check.py`（guid 冲突、场景/材质引用、材质属性 vs shader 声明、脚本括号配平）、`python Tools/enumerator_check.py`（协程里写了裸 `return;` 会直接编译不过，这个能提前抓出来，`--selftest` 证明它抓得到）、`python Tools/gltf_api_check.py`（核对 Interop/Gltf 用到的 glTFast API 在装好的包源码里是否存在）、`python Tools/gameplay_api_check.py`（核对 Gameplay 用到的工程内部 API）、`python Tools/maid_cache_check.py <maid.bin>`（校验缓存层级和动画通道路径能否对上）、`python Tools/maid_lazy_check.py <maid.bin>`（把 `BinStreamReader` 的缓冲/跳过逻辑照搬到 Python 跑真实缓存，校验按需加载的偏移算术和真实读盘量）、`python Tools/house_grid_check.py <house.json>`（校验可通行格数据、打印逐层平面图和连通块）、`python Tools/gltf_info.py <file.glb> --stats`（打印 glTF 的材质 / sampler / alphaMode / doubleSided / 顶点色取值 / 每张贴图的灰度程度——判断贴图是不是靠染色上色就看这个）
- **纯 Roslyn 类型检查**（不启动 Unity、不碰许可，1 秒出结果；改完 C# 先跑这个再交给用户，能省掉"用户点开 Unity 才发现编译不过"的来回）。参考集：`Editor\Data\MonoBleedingEdge\lib\mono\4.7.1-api`（含 Facades）+ `Editor\Data\Managed\UnityEngine`（**模块程序集都在这个子目录，不要再单独引用 `Managed\UnityEditor*.dll`**，会和 CoreModule 里的同名类型撞 CS0433）+ `Library\ScriptAssemblies` 里除 `Assembly-CSharp*` 之外的 dll；用 `dotnet <sdk>\Roslyn\bincore\csc.dll /target:library /langversion:8.0 /nostdlib+ @rsp`，把 `Assets/Scripts/**/*.cs` 全塞进 rsp。当前状态：**94 个源文件 / 0 错误**

## 渲染风格（MC 风格）

- **捏合缩放的方向**：`HouseCameraPanZoom` 传给 `HouseCameraFitter.ZoomBy()` 的是**正交尺寸的倍率**（尺寸越小画面越大），而"手指分开 = 拉近放大"，所以倍率要写 `_lastDistance / distance`（取反）。踩过：写成 `distance / _lastDistance` 会让捏合和滚轮的方向相反（手指分开反而缩小）。`ZoomBy` 内部那套锚点公式（`_pan += offset * wpp * (1-k)`）不受影响，因为它只认"尺寸倍率"
- 目标是在内置管线 + Gamma 空间下模仿原版观感：不用后处理、不用 HDR、不用环境反射
- 天空盒：`Assets/Materials/MinecraftSkybox.mat`（shader `MaidHome/MinecraftSkybox`），竖直渐变 + 太阳圆盘，不是贴图天空球。**地平线颜色必须等于 `RenderSettings.fogColor`**，否则远处地形和天空接不上。太阳圆盘方向取平行光的 `-forward`
- 方块/模型材质：shader `MaidHome/MinecraftBlock`，AlphaTest + 原版逐面亮度（顶 1.0、南北 0.8、东西 0.6、底 0.5）+ 线性雾 + 硬阴影，只吃一盏平行光。它注册进了 `GraphicsSettings` 的 Always Included Shaders，因为运行时是 `Shader.Find` 找的
- **必须乘顶点色**：原版草方块这类贴图本身是灰度的（`grass_block_top`、`grass_block_side_overlay`），生物群系染色被导出器烘进了顶点色 `COLOR_0`。实测导出器写的是 **sRGB 值**（例：`0.745,0.716,0.332` 正好是 `#BFB755` 草原草色），所以直接用、不做 `LinearToGamma`；哪天遇到按规范写线性值的模型再补转换。少了这一步草会渲染成灰色
- glTFast 自带的三个内置 shader（PbrMetallicRoughness / PbrSpecularGlossiness / Unlit）都**不吃顶点色**，所以 PBR 路径下草同样是灰的，不是换材质换坏的
- 原版草方块侧面由两张贴片叠成（`grass_block_side` + 灰度覆盖层），导出器会把覆盖层往外偏移 `0.001` 防共面，别手动挪
- 场景的雾 / 环境光 / 反射开关由 `McStyleRig`（`Assets/Scripts/Core/Rendering`）施加，参数在 `McStyleProfile` 上；rig 只保证渲染环境，不碰相机
- 相机走正交，`HDR` 和 `MSAA` 关掉，`Clear Flags = Skybox`。相机参数（正交尺寸、near/far）由相机自己定，rig 不改
- **相机全程正交**，不做正交↔透视切换；取景只靠"平移 + 改 size"（`HouseCameraFitter`，`MoveTo` 里只插值 position / rotation / orthographicSize / farClip）
- **看房子的拖动/缩放**：输入在 `Gameplay/House/HouseCameraPanZoom`（**单指拖动平移、两指捏合缩放**，PC/编辑器还能滚轮；滚轮和鼠标位置的读取也在 `PointerInput` 里）。状态和数学在 `HouseCameraFitter`：`_pan`（沿相机 right/up 的世界偏移）+ `_zoom`（相对房子取景的倍率，`_zoomMin/_zoomMax`），`PanByPixels` / `ZoomBy` / `ResetPanZoom`，`CanPanZoom` = 有房子取景且自己没在动画。规矩：① 换房子/重新 `FitToBounds` 时清零；② `RestoreHouseView` 要带上拖动缩放（点开女仆再关掉不该重置视角）；③ 点开女仆、背包放置、摸脸/摸尾期间整个让位（那些模式自己要用拖动，`MaidInteractionController.IsFocused` / `BagPlacementController.IsActive`）；④ 缩放是**按锚点**的——锚点离屏幕中心 d 像素，缩 k 倍后相机平移 `d * wpp * (1-k)`（`wpp = 2*size/Screen.height`）锚点底下的东西才不动，别只改 size；⑤ 平移夹在以房子中心为心、`_panLimit × 房子包围盒半对角线` 的圈里，别让镜头飞出房子
- **切场景过场（主相机上天再下来）**：`Core/Scene/SceneTransition`（自己 `DontDestroyOnLoad`，跨场景活着）。`SceneTransition.Go(scene)` = **先把当前场景根下的 Canvas 全 `SetActive(false)`** → 当前相机升到 `_liftHeight`（默认 100，世界 Y）→ `LoadSceneAsync(..., allowSceneActivation=false)`（**加载期间旧场景留在屏幕上，画面是天空**，不黑屏）→ 激活新场景（`sceneLoaded` 回调里把新相机顶到同样高度、并把新场景的 Canvas 也收起来）→ 降回正常取景 → 才把 Canvas 放出来。加载超过 `_loadingScreenDelay` 才亮加载条（`LoadingScreen.SceneJob`；加载条的 Canvas 会被跳过，`LoadingScreen.Canvas`）。两个场景各有自己的主相机，所以升降必须分两段，而且**"抬了多少"必须存在 `HouseCameraFitter` 里**（`SetLift` / `Lift`，`ApplyView` 每次都加 `Vector3.up * _lift`，`MoveTo`/`CaptureView` 存的是"没抬过"的机位）——不然过场期间房子/女仆重新取景会把相机一下拽回地面。新场景没有 fitter（例如 Portal）就直接平移相机 transform。入口：`SwitchScene.Switch()` 已经改成先走 `SceneTransition.Go`，失败（场景不在 Build Settings 里）才退回 `SceneManager.LoadScene` 硬切
- 点女仆的取景用 `HouseCameraFitter.FocusKeepingAngle`：**方位角不变 + 固定低头 `_focusPitchDegrees`（默认 45°）**推近，不按女仆朝向绕到她正面——绕正面时画面整个在转，看起来像女仆自己在转；正脸由女仆自己转身给（`MaidInteractionController.FaceCamera`，水平方向正对相机，不额外偏角）
- **摸尾巴模式相机位置完全不动**，靠女仆自己转过去把尾巴露给镜头（`MaidTailInteraction.TurnTailToCamera` / 退出时 `TurnToCamera`）；**「吸一口」的"凑近"用正交 size 代替**（`SizeRoutine`：0.3 秒把 `orthographicSize` 缩到 `_sniffZoomScale`（默认 0.6，画面放大 1.67 倍），下限 `_sniffMinSize`，吸完再涨回去）——正交下沿视线移动相机看不出远近，改位置等于白改。中断/退出必须在 `ReleaseAll` 里把 size 还原，否则相机会停在放大状态。转身统一走 `MaidWanderer.FaceDirection`（暂停时走逻辑不转，所以单独跑协程；一旦恢复游走就退出，免得和 `UpdateWalk` 抢 `transform.rotation`）
- **摸脸模式**（`MaidFaceInteraction` + `MaidFaceRig` + `MaidFacePanel`）：点女仆面板的「摸脸」进入，拽耳朵 / 戳眼睛戳脸颊 / 在脸上快速横挥扇耳光，机制和参数照抄 moreanimation 的 `FaceInteractionState`、`FaceHitProjection`、`FaceSlapStroke`。她打开面板时已经面向镜头，所以模式里不动相机，只在进入时把正交 size 推近到 `_faceOrthographicSize`。头部和耳朵的姿势是在动画写完之后叠一层旋转（和 `MaidTailChain` 一个套路：用「上次自己写的值」判断这帧动画有没有写过），退出时不用手动还原旋转，只要还原耳朵的 localScale
- 耳朵拉长沿哪个轴缩放**不能**照抄模组的 `longitudinalAxis`（它按骨骼旋转推，正交相机下会选到沿视线的那个轴，拉长完全看不出来），改成 `MaidFaceRig.StretchAxis`：按「网格局部尺寸 × 该轴在画面上的投影长度」挑。`python Tools/face_rig_check.py <geo.json>` 可以在真机前先看选到哪根骨头、哪个轴
- 摸脸的触发范围（`MaidFaceInteraction.TryBuildZones`）是一份屏幕矩形：头一块、眼睛/脸颊各两个椭圆、耳朵两块。命中判定和 F8 调试显示**共用同一份**，改比例只改这一处；画线用 `OnGUI` + 运行时生成的白纹理/圆纹理，不建场景物体。Unity 屏幕坐标 y 向上，和模组那套「上为正」的归一化一致，别再写成 `center.y - screen.y`
- **耳朵优先于脸**（2026-10 作者改的，之前反过来过）：`HitZone` 里先判左右耳朵椭圆，再判眼睛/脸颊/整张脸（脸区 = 头框放大 1.1 倍的内切椭圆，扇脸和戳脸都从这里起手）。代价是耳朵包围盒有三分之一压在头顶上（taisho 实测 33%），**那两块脸上的位置抓不到扇脸**——嫌耳朵区太大就调 `_earZoneScale`（默认 0.75，缩的就是这两块椭圆），F8 显示会跟着变
- **起手区域决定玩法，别写反（2026-10 作者更正过一次）**：起手落在**脸上**（`IsFaceZone`：`Face` / 眼睛 / 脸颊）——点一下 = 戳脸，快速横挥 = **扇耳光**；起手落在 `Zone.None`（脸框外，含空白处）——拖动 = **拖脸**（头跟手转）。也就是"脸上起手归扇脸/戳脸，空白处起手归拖脸"。横挥同时只认一根手指（`_strokeFinger` = 第一根按在脸上起手的），它松手后交给下一个"起手也在脸上"的手指（`FindStrokeCandidate`）。参数（距离 0.07 屏幕高 / 速度 0.3 屏幕高每秒 / 换向才计数 / 连击超时 2.5s）用 `python Tools/out/slap_sim.py` 模拟调过，返回值要跟 `MaidFaceInteraction.TrackPress` 保持一致
- **拖脸**（`MaidFaceInteraction` 的 `FaceDrag` / `StartFaceDrag` / `UpdateFaceDrag`，照抄模组 `FaceInteractionState` 的 FACE 分支）：**必须起手在脸框外（`Zone.None`）**，拖动超过 `0.012 × 屏幕高` 才开始（起手在脸上的那条手势归扇脸/戳脸，不拖脸）。位移**按抓取点到那条屏幕边的距离归一化**——拖到那条边就是满偏（分母 0.05 兜底），所以抓在靠边处轻轻一拖就满；角度走 `sign * max * (0.15t + 0.85t^2.4)` 的软曲线，yaw 最大 105°、pitch 最大 100°，**往右拖 yaw 要取负**才跟手（和扇耳光同一个符号约定），超过 65° 走 `Complain()` 喊疼（台词用模组那四条 face_overstretch）。松手按 300°/s 滑回 0，然后和耳光/戳脸的弹簧**相加**再套到 Head 骨骼上。这条手势**不判耳光**。`python Tools/out/face_drag_sim.py` 可以看"拖多少像素转多少度"的对照表；`MaidFacePanel._hint`（默认提示语）和 F8 那行说明也要跟着改
- **戳脸（眼睛/脸颊）的反方向按屏幕左右算，别照 Zone 名字取符号**：我们的 `Zone.LeftEye/RightEye`（还有左右脸颊）来自**模型骨骼名**，角色面对镜头时和屏幕左右正好相反；模组的 LEFT/RIGHT 是按归一化屏幕 x 分的（`FaceHitZone.at` 左眼在 x = -0.42、`side()` 屏幕左 = -1，impulse = `side * 幅度`）。所以 `ClickFace` 里按"点相对头中心在屏幕哪半边"定 ±yaw（`press.Origin.x < zones.Head.center.x` → -1）。参数：眼睛 ±24° yaw / 12° pitch + 喊疼台词，脸颊 ±12° / 5° 不吭声。同源坑：模组里耳朵的 `EarReference.outward()` 也是用 `rootX < headX` 定侧，别按骨骼名
- **戳脸有两层冷却，别搞混**：① `_pokeCooldownSeconds`（默认 0.2s，`_lastPokeAt`）在 `ClickFace` 开头挡**整个戳**（不弹也不喊），眼睛和脸颊共用；② `_pokeReactionCooldownSeconds`（默认 **0**，`_lastPokeReactionAt`）只管**戳的反应那套**——闪红 + 台词 + 语音 + 受伤动画，走 `ComplainNow()`（不判冷却直接演）。作者实测反馈：把 ① 设成 0 之后还觉得"有一秒延迟"，其实是当时反应和**拖脸掰疼**共用 `Complain()` 的 `_complainCooldownSeconds = 1s`，所以拆成了两层。拖耳朵拉疼 / 拖脸掰疼走的仍是 `Complain()` + `_complainCooldownSeconds`（它们俩互相共用）。想做"每根手指各自算戳的冷却"就把 `_lastPokeAt` 换成 fingerId → 时间的字典；共用冷却的代价是两根手指"同时"戳（抬起差几帧）只会生效一下
- 摸脸是**按手指各管一份状态**：`EarGrab` 左右各一份（可以两根手指同时拽），`PressState` 每根手指一份（可以同时戳两只眼睛）。横挥同时只认一根手指（`_strokeFinger` = 最先按下的那根），它松手后自动交给还在按的下一根。同一只耳朵被第二根手指按住时会把原来的手势转过去，转之前**必须先 `RestoreEarScale`**，否则新那份会把拉长后的比例当成静止比例记下来，耳朵就永远缩不回去了
- **连击显示在 `MaidSlapComboHud`**（照抄 moreanimation 的 SlapComboHud / SlapComboState / SlapMilestoneState）：顶部「连击 ×N」逐字彩虹 + 每加一下弹一下 + 超时前 0.35 秒淡出；每 100 连「N连抽」大字 + 两侧彩带 + 两侧各 64 个彩色粒子（1200ms），文字撑 1800ms。全部画在 `MaidFaceInteraction.OnGUI` 里，不建场景物体；面板的 `_comboText` 只是给自定义 UI 的副本，自动生成的底栏不再放它（免得和 HUD 重复）
- 「N连抽」用的是模组自带那张位图字体：`Assets/Resources/MoreAnimation/combo_display.png`（256x64、8 列 2 行、格子 32px，字形表 `01234567` / `89SLAP连抽` 写在 `MaidSlapComboHud.GlyphChars` 里，对应模组的 `font/combo_display.json`）。这份 `.meta` 是手写的：Point 过滤、不压缩、256 上限——改导入设置会糊。字体没导进来会自动退回普通彩虹字
- 100 连的彩蛋音是**现场合成**的（`AudioJingle.Milestone()`，C5-E5-G5-C6 上行琶音），因为模组那边用的是 Minecraft 自带的 UI toast 音，没法随包发。想换就在场景里挂一份 `MaidFaceInteraction` 并接 `_milestoneClip`
- 被拉疼 / 被戳 / 被扇的时候借女仆自己的「被打」动画演一下（TLM 里叫 `attacked`，winefox 和 taisho 都是 0.5 秒），演完换回 `BedrockAnimationPlayer.PlayingClipName` 记下的那条；退出模式时如果还在演，必须换回来，否则她会一直停在被打的姿势（记不到原动画时用 `MaidWanderer.InvalidateAnimation` 让状态机重播）

## 外部 glTF 模型

- 依赖 `com.unity.cloud.gltfast`，**版本写死 5.2.0**：这是 Unity 2020.3.30f1 上能装的最后一版，6.x 起步要求 2020.3.48f1。带 Burst 1.6.6 + Mathematics 1.2.6，没有 Collections
- 运行时加载入口：`Assets/Scripts/Interop/Gltf/GltfModelLoader.LoadAsync(path, parent, scale)`，只在运行时调（失败靠 `Object.Destroy` 清理）。`persistentDataPath` 下的文件可以直接 `File.ReadAllBytes`，`StreamingAssets` 不行
- 材质：`McGltfMaterialGenerator` 实现 glTFast 的 `IMaterialGenerator`，只取 baseColor 贴图和因子，直接生成 `MaidHome/MinecraftBlock`，金属度 / 粗糙度 / 法线 / 遮蔽全丢；贴图强制点采样，**wrapMode 保留 glTF sampler 自己的设置**——方块贴图靠 `REPEAT` 平铺，一律改 Clamp 会把平铺弄坏（女仆图集那种才要 Clamp）
- `OPAQUE` 材质的 `_Cutoff` 必须设 0：不少导出的贴图 alpha 通道整条是 0，按 0.5 裁会把模型整个裁没
- 生命周期：网格 / 贴图 / 材质都挂在那次 `GltfImport` 上，`Dispose()` 会把它们一起销毁。模型活着的时候由 `GltfImportHolder` 持有 import，模型销毁时再释放
- 编辑器里直接拖进来的 `.glb` 走 glTFast 的 `ScriptedImporter`，材质是 `glTF/PbrMetallicRoughness`（PBR），和本工程的 MC 平光对不上，只能当预览；要在场景里正常显示就走运行时加载
- 编辑器里要把已经拖进来的模型的材质换掉，用 `Tools/MaidHome/MC 材质`：选中物体 → 按钮，材质会存到 `Assets/Materials/Generated/`，同一张贴图复用同一个材质
- `.glb` 单文件最省事；`.gltf` 配外部 `.bin` / `.png` 时按文件所在目录当 baseUri

## 基岩模型转换

- 转换逻辑：`Assets/Scripts/Interop/Bedrock`（解析 / 生成），编辑器工具：`Assets/Scripts/Editor/BedrockModelToolWindow.cs`
- 不带 Unity 的验证脚本：`python Tools/bedrock_preview.py <model.json> --report --ascii [--png out.png]`
- 已确认的坐标约定：基岩模型面朝 -Z，转换后朝 Unity 的 +Z；位置取 `(-x, y, -z)`
- 旋转用 `BedrockModelBuilder.RotationSigns`（三个轴各自 ±1）+ `RotationOrder` 控制，默认 `(1,1,1) + ZYX`（和 Blockbench / TLM 一致，角度不取反）。脚本层面能筛的指标（多轴骨骼落点、包围盒左右对称）已经筛过一轮，但薄片的**朝向**（法线朝里还是朝外）光靠数据判断不出来，只能靠眼睛
- 坐标换算统一走 `BedrockModelBuilder.ConvertPosition / ConvertDirection / ComposeRotation`，模型和动画必须用同一套，别各写一份
- 编辑器窗口里有「翻转 X/Y/Z」三个勾选框、「旋转顺序」下拉，以及 **「生成 48 种旋转组合对比」** 按钮（8 种符号 × 6 种顺序，按 8 列 6 行摆开，名字形如 `+X-Y+Z_XZY`）。遇到姿势明显不对的模型用它让用户指认，定下来后把调试项删掉
- 盒式 UV 布局（和原版皮肤一致）：`north=(u+d, v+d)`、`south=(u+2d+w, v+d)`、`west=(u, v+d)`、`east=(u+d+w, v+d)`、`up=(u+d, v)`、`down=(u+d+w, v)`
- 盒式 UV 的 `w/h/d` 要用**向下取整**后的尺寸（Minecraft / TLM / Blockbench 都是这么算的），用原始小数会让 UV 岛错位；per-face UV 不受影响，按 `uv` + 带符号 `uv_size` 直接映射
- 每个面的贴图方向（基岩坐标系，u 增大方向 / 矩形上沿方向）：north `-X / +Y`、south `+X / +Y`、east `-Z / +Y`、west `+Z / +Y`、up `-X / +Z`、down `-X / +Z`。曾经按 TLM 源码把所有面的 u 一起翻转，实机验证后发现反而错，已回退；**别再一次翻六个面**，真要改就单独改一个面
- UV 按模型声明的 `texture_width/height` 换算，不按 PNG 实际尺寸；两者不一致时编辑器工具会警告
- 材质用 `MaidHome/MinecraftBlock`（AlphaTest + 逐面亮度），透明像素必须剔除（不透明区域可能只占贴图一小部分）；找不到该 shader 时依次退回 Standard Cutout / Unlit Cutout
- 厚度为 0 的方块（裙摆片、眼睛片、装饰片等）要双面渲染：副本用反向缠绕 + 反向法线，UV 不变（背面看到的是镜像贴图，符合薄片的表现）
- 如果以后遇到某个模型的姿势明显不对，先在编辑器里删掉重生成确认，再考虑给该模型加按骨骼的符号覆盖表；不要改动上面的全局约定

## 基岩动画导入

- 逻辑：`Assets/Scripts/Interop/Bedrock/BedrockAnimation.cs`（解析）、`BedrockAnimationChannel.cs`（通道采样）、`BedrockAnimationClipBuilder.cs`（烘成 AnimationClip）、`MolangExpression.cs`（Molang 子集）、`BedrockAnimationPlayer.cs`（运行时播放）。编辑器按钮在「基岩模型转换」窗口的下半部分
- 不带 Unity 的验证：`python Tools/animation_check.py <animation.json> --summary`（要看某帧姿势就加 `--model <geometry.json> --anim <名字> --time <秒> --bone <骨骼>`）
- 旋转：骨骼静止欧拉角 + 动画欧拉角**逐轴相加**，再走和模型同一个 `BedrockModelBuilder.ComposeRotation`（不取反 + ZYX）
- 位置：动画 position 的单位是像素，映射和模型一样是 `(-x, y, -z) / PixelsPerUnit`
- 插值：段 [i, i+1] 用第 i 帧的 `lerp_mode`；如果第 i+1 帧是 catmullrom，这一段也按 catmullrom（TLM 的 `BoneKeyFrameProcessor` 就是这么定的）。**读 pre/post 成对出现的关键帧时要一起读 `lerp_mode`，漏了就会把 catmullrom 段当线性插值**
- 烘焙：按 30fps + 所有关键帧时刻采样，写成 `localRotation.x/y/z/w` 四条曲线；相邻采样点的四元数要放在同一个半球，否则线性插值会绕一大圈
- 每条 clip 都给模型里**所有**骨骼写曲线，没有动画的骨骼写一条常量曲线钉在静止姿势上，否则切换动画会残留上一条动画的姿势
- **`pre_parallel*` 是常驻并行层**（酒狐里 `pre_parallel0` 管尾巴 8 节 + 眉毛眼皮、`pre_parallel1` 管长发），烘焙和运行两头要配套：
  - 烘焙：把常驻动画动的骨骼从**别的**动画里剔掉（`BedrockAnimationClipBuilder.SkipBones`，主动画自己动到的除外，例如 `sleep`/`run` 会动尾巴）；常驻动画自己也只烘"它动过的骨骼"，不然它铺满全骨骼会把别的常驻层（尾巴）盖成静止姿势
  - 运行：**不走 legacy 的 `Animation` 分层**（`Play` 到底停哪些层、层号算不算数都不可靠，踩过：尾巴完全不动），改由 `BedrockAnimationPlayer.LateUpdate` 按 `BedrockClipData` 的采样直接写骨骼，主动画覆盖到的骨骼跳过——次序和 TLM 一致（主动画优先）
  - 摸尾巴期间 `BedrockAnimationPlayer.ParallelEnabled = false`，尾巴那几根让给弹簧链写，两边都写会互相盖
  - 改了剔除规则要 bump `MaidAssetCache.FormatVersion`
- **AnimationClip 不在加载时烘**：`MaidAssetLoader` / `MaidAssetCache` 只出采样数据（`ClipData`），真正变成 `AnimationClip` 是 `BedrockAnimationPlayer.EnsureClip` 在**第一次 Play 到那条动画**时做。理由是可量化的：护士酒狐 178 条动画 × 197 骨骼 × 10 条曲线 = **35 万次 `SetCurve`**，一次全建出来就是进游戏卡好几秒的主因，而且那些 clip 的曲线数据要几百 MB 常驻。动画条数一律看 `MaidAssets.ClipData.Count`，`Clips` 运行时是空的（只有编辑器显式传现成 clip 时才有内容）
- **采样数据也是按需读的**：`MaidAssetCache.LoadBin` 只读网格那段（护士酒狐是 385 KB / 29.7 MB），动画只记名字 / 长度 / 包裹模式 + 文件偏移（挂在 `BedrockClipData.TrackLoader` 上），第一次 Play 到才从 `maid.bin` 里单独把那一条读出来（`LazyClipSource`）。常驻并行层（`pre_parallel*`）例外，`SetClips` 时就要读——它们每帧都要用。校验偏移算术跑 `python Tools/maid_lazy_check.py <maid.bin>`（索引读完的位置正好等于文件长度，跳过采样算错长度这里就会红）
- 读 `maid.bin` 用自己写的 `BinStreamReader`：`Skip` 越过采样直接 `seek`（不把几十 MB 读进来）、数组整块读 + `BlockCopy`。缓冲给 4 KB 就够（实测同一份缓存 64 KB 反而要读 13.6 MB、4 KB 只要 7.6 MB），FileStream 的 `bufferSize` 传 1 关掉它自己那层预读。**别换回 `BinaryReader`**：它读字符串走内部缓冲，`stream.Position` 会跑到逻辑位置前面，算出来的动画偏移全是错的
- 缓存对象重新写盘前要先 `EnsureTracks()`（`MaidAssetCache.Write` 里已做）：按需加载的采样是从**旧文件**读的，先删文件再写就永远读不回来了。读失败在 `BedrockClipData.EnsureTracks` 里吞掉并警告，只让那一条动画失效，不要让它把游戏搞崩
- 没有 `animation_length` 又没有关键帧的动画，TLM 里长度是「无限」（`Double.MAX_VALUE`）：被 `anim_time` 驱动的（computer / chair 这种尾巴摆动）按 `360/乘数` 的公倍数烘一个循环，其余按静止姿势处理。把它们当 0 秒会变成疯狂循环，速度明显不对
- Molang 只实现了子集（四则运算 / 比较 / 三元 / `math.*`）：时间有 `query.anim_time` / `q.anim_time`（官方简写，包里会用，例如 `swing$minecraft:snowball`）/ `anim_time`，其余变量默认 0，认不出的函数按 0 处理并记警告
- **没有数据源的变量按 TLM 的实际取值固定，不能一律当 0**（`MolangContext.FixedValues`，改这里要同步 `Tools/animation_check.py` 的 `MolangContext.FIXED` 和 `Tools/molang_usage.py` 的 `FIXED`）：`ysm.food_level = 20`、`query.player_level = 0`、`query.has_cape = 0`（false）、`ysm.rendering_in_inventory = 0`（false）、`ysm.texture_name = 0`（模组里是空字符串，但这套 Molang 只有数值——`== '名字'` 全是 false，反过来 `!= '名字'` 也会是 false，这一处不一致，真遇到要加字符串字面量支持）；`ysm.bone_pivot_abs(...)` 在 TLM 里是空函数，注册成恒 0 的函数（不刷"不支持的函数"警告）。**踩过：`ysm.food_level` 当 0 会让 护士酒狐/JK 酒狐 的 idle 永远走"饿了捂肚子"那一支**（`ysm.food_level<=6?-25:0` 评估成 -25），固定成 20 后才是正常 idle
- 查某个包的动画依赖了哪些变量：`python Tools/molang_usage.py <animation.json>`（汇总，带 `*` 的是真缺口）/ `--anim idle`（逐骨骼列出表达式）
- 预览用 `AnimationMode`，点「暂停」会恢复场景原姿势；想真的播给玩家看就用 `BedrockAnimationPlayer` + legacy `Animation` 组件

## 女仆存档 -> Unity 资源

- 存档结构：`saves/maid/<uuid>/maid.json`，字段 `model` / `texture` / `anim` 都是**相对本文件夹**的文件名；同目录还有 MC 侧塞进来的二进制 `maid_data.maid`，Unity 不碰。`saves/maid.animation.json` 是共用的基础动画
- 解析：`Assets/Scripts/Interop/Maid/MaidSaveData.cs`（`ScanRoot` 扫目录）
- 加载：`MaidAssetLoader.Load(maid)` —— 缓存新鲜就读缓存，否则转一遍（网格 + AnimationClip）并写缓存
- 动画合并：先铺共用的 `maid.animation.json`，再用模型自带的那份**按同名覆盖**（TLM 的行为）；一根骨骼都对不上的动画跳过并记警告
- 缓存：`MaidAssetCache` 写 `persistentDataPath/cache/maid/<uuid>/`，里面是 `maid.bin`（网格 + 烘焙好的动画采样）、`manifest.json`（源文件 sha256 + 统计，人可读）、`texture.png`。`manifest` 里 sha256 一变就重转；缓存是可再生的，删了只会慢一次
- 编辑器入口：`Tools/MaidHome/存档编辑器`（女仆页能改 maid.json 字段、切背包状态、转模型写缓存、复制、删除；房子页能设为当前房子；音效包页能看事件数、删除。删除和覆盖前都会弹确认，`maid.json` 覆盖前自动留一份 `.bak`）
- 采样：只按采样率铺到**最后一个关键帧**为止，之后的值是常量（`maid.animation.json` 里有 `animation_length = 1000` 的动画，整条铺会炸）；被 `anim_time` 表达式驱动的动画才整条铺
- **硬编码：名叫 `FOX`（忽略大小写）的节点一律 `SetActive(false)`** —— 模组里狐狸是单独实体，模型里那份只是占位（winefox 里 `MRoot/Root/FOX/AllBody2/...` 是一整只狐狸，含 bow2 / 耳朵 / 手脚）。规则在 `MaidAssetLoader.HiddenNodes` + `ApplyModelRules`，**建完模型要调一次、读缓存回来也要再调一次**（缓存不存 active 状态），两处调用点都不能删
- `maid.json` 的 `scale`（倍率，缺省 1）缩放的是**模型根节点的 localScale**，和 FOX 规则一起在 `ApplyModelRules` 里套用。它**不进缓存**（改了不该重转），所以同样靠"建完 / 读缓存后各调一次"保证最新。CharacterController 的 height/radius 是局部值，会跟着根节点缩放自动变小，不用另算；`HouseNavMesh` 的 agent 半径是烘房子用的，跟女仆缩放无关
- 缓存格式：`maid.bin` 里的 **0 号节点就是模型根自己**，读回来时必须复用已经建好的 root，不能再 new 一个（踩过：会变成模型和组件挂在两个平级对象上，动画曲线路径 `MRoot/...` 一条都对不上，表现为"模型和移动的对象是两个东西"）。校验：`python Tools/maid_cache_check.py <maid.bin>`
- Molang 已知缺口：`v.x = ...` 赋值语句不支持（`swim` / `swing:sword` 两条会不准），`条件 ? 值` 缺 else 时按 `else = 0` 处理
- 参考数据：winefox 这个女仆，合并后 98 条动画，其中 84 条能对上模型，骨骼 181 / 顶点 9792

## 女仆走动（Gameplay）

- 代码在 `Assets/Scripts/Gameplay/Maid`：`MaidLoader`（存档 → MaidAssets）、`MaidPlacement`（MaidAssets → 世界里的实例）、`MaidWanderer`（随机走动）、`WanderArea`（活动范围）、`MaidSpawner`（测试入口，进 Play 直接放一只）
- **加载和放置分两段**：`MaidLoader.Load` 出来的模型是 `SetActive(false)` 的"背包里"状态，玩家决定放置时再调 `MaidPlacement.Place(assets, pos, rot)`。以后加"放置 / 留在背包"的选择界面时，中间状态就是这份 MaidAssets
- `MaidWanderer` 用 `CharacterController`（不用刚体），状态机只有 Idle / Walk：待机 2~6 秒 → 随机挑点 → 走过去 → 再待机，带卡住判定（1.5 秒位移小于 0.15 就换目标）
- 走路动画播放速率 = `_moveSpeed * clip.length / _stridePerCycle`，`_stridePerCycle`（一个循环走多远）只能肉眼标；模型转换后朝 +Z，转向目标即可
- 活动范围是 xz 平面上的方盒，运行时把中心设成出生点。以后换成"模组侧导出的可通行格"时，替换 `WanderArea` 即可，`MaidWanderer` 只依赖它的 `TryPick`
- 已验证的事实：walk / run 都**没有根位移**（是身体摆动），可以直接移动 transform；根骨骼 `MRoot` pivot 是 `(0,0,0)`，脚底就在原点；`walk` 烘出来是 `WrapMode.Loop`，`idle` 是 `ClampForever`
- **还没有 EditMode 测试**：工程里一个 asmdef 都没有，测试程序集引用不到 Assembly-CSharp，要加测试得先把纯逻辑拆到自己的 asmdef 里

## 房子导入与格寻路

- **第一次进游戏（一栋房子都没有）会自动生成"示例房间"**：`HouseStarter.Ensure(houseRoot)` 写一份 `saves/house/builtin-starter-room/house.json`（8×3×8，外面一圈墙不可走、里面 6×6 可走，`model` 留空）+ 一个 `.starter-done` 标记（**只补一次，玩家删了不会再冒出来**）。`HouseImporter` 看到 `model` 为空（或真房子的模型文件丢了）就按格表程序化搭地板 + 四面墙：用 `GameObject.CreatePrimitive(Cube)`（自带网格和 BoxCollider，所以 `AddColliders` 要跳过**已经有任意 Collider** 的对象，不然会多加一层 MeshCollider），位置全部走 `HouseGridMapper.ToLocal`，这样和 `HouseGridView` 认的格子（含镜像/旋转）严格对齐；材质用 `MaidHome/MinecraftBlock` + 白 1×1 贴图 + 米色。纯代码，不依赖随包资源，Android 上也不用碰 StreamingAssets 读取。`HouseSpawner.Start` 和 `HouseSwitcher.Refresh` 各调一次 `Ensure`（幂等）。示例房间的格数据可以用 `python Tools/house_grid_check.py` 验（8×3×8 → 可走 36 格、1 个连通块、origin 可站）
- `house.json`（MC 侧导出，现在是手工标，以后自动生成）：`size` / `name` / `origin` / `model` / `walkable`。`model` 是同目录 glTF 的文件名（不带扩展名）
- **格语义（必须记住）**：`walkable[y]` 里 `'1'` 表示"实体能站在这一格的**底面**上，脚底高度 = 这一格的 y 坐标"。层序 y 递增，层内行按 z 递增、字符按 x 递增。索引 `(y * SizeZ + z) * SizeX + x`
- **能不能站由 MC 侧判断完**：下面是不是实心、头顶够不够 1.8 格、楼梯还是台阶桌子，Unity 侧一概不重新推——两边各推一次必然对不齐，而且 Unity 侧没有方块语义。格子表的原点固定是模型局部 `(0,0,0)`，`origin` 字段目前不用
- 解析：`Assets/Scripts/Interop/House/HouseSaveData.cs`（缺层/行数不对/非 0/1 字符都记警告并按不可走处理）；格视图 `HouseGrid`（`IsWalkable` / `Index` / `CellFeet` / `FindWalkableY`）
- 寻路：**曾经**是 `Gameplay/Navigation/GridPathfinder.cs`（格上 A*），2026-10 换成 NavMesh 时整个文件已删除。当初不用 NavMesh 的理由是"可通行数据只能由 MC 导出、几何表达不了台阶桌子"，后来发现 NavMesh 也能按数据挖洞（见下），而且烘焙时按角色半径收缩过、转角不蹭墙，所以换了过去
- 导入：`HouseSpawner`（测试入口）→ `HouseImporter.LoadAsync`：glTF 用 Interop 的 `GltfModelLoader` 加载，然后**必须给每个 MeshFilter 补 MeshCollider**（glTF 只有 MeshRenderer，不补碰撞体女仆会直接穿到地板下面），最后挂 `HouseGridView`（带格表 + 世界↔格换算）
- 女仆：`MaidNavigator` 沿拐点走；`MaidWanderer` 场景里有 `HouseGridView` 就按格寻路，没有就退回 `WanderArea` 在平面直走
- **寻路现在是 NavMesh**（2026-10 换的）：`HouseNavMesh`（`Assets/Scripts/Gameplay/House`）在房子加载完、加好 MeshCollider 之后烘一次（`CollectSources` → `BuildNavMeshData` → `NavMesh.AddNavMeshData`），随后 `MaidNavigator` 用 `NavMesh.CalculatePath` 取 corners，仍然喂给原来那套"沿拐点走"的逻辑；目的地从房子包围盒随机采点 + `NavMesh.SamplePosition`
- **烘出来的"能走"只是几何意义上的能走**：台阶拼的桌子照走不误，所以必须按数据挖洞。`HouseGrid.MarkEnclosedNonWalkable` 只标"被走道围住的不可走格"——外墙靠几何本来就挡住了，挖了反而会吃掉贴墙的走道；挖洞用 `NavMeshObstacle`（Box + Carving）
- 换 NavMesh 时把退役的一并清了：`GridPathfinder.cs`（整个文件）、`HouseGrid.FindWalkableY`、`HouseGrid.CellFeet`、`HouseGridView.WorldToCell`/`TrySnapFeet`/`TryFindNearestCell`/`Describe`、`MaidNavigator.Describe`。`HouseGridView` 现在只剩 `Grid` / `Axis` / `CellFeet`（挖洞和 gizmo 在用）
- 对齐检查仍然有用：挖洞位置是 `HouseGridMapper.ToLocal(view.Axis, ...)` 算的，Axis 错了洞就挖在别处
- 调试：`HouseGridGizmoDrawer`（`Assets/Scripts/Editor`）把可走格画在 Scene 视图里，**只用于调试**，顺便用来确认"行到底是 z 还是 x"这种朝向问题
- **glTFast 导入时会把顶点的 x 取反**（右手系转左手系，见包里的 `Jobs.cs`：`new float3(-(float)off[0], off[1], off[2])`），所以 Unity 里看到的模型相对 glTF 文件是沿 X **镜像**的。格数据是方块/文件坐标系（不镜像），映射到世界时必须跟着取反，否则整张格表偏 W 格、形状左右翻（看起来像转了 90°）。换算**只在 `HouseGridMapper` 里写一份**，`HouseGridView` / `MaidNavigator` / gizmo 都调它
- 格到世界取的是**格中心**（`CellCenter`），不是角点：取角点女仆会贴着墙走
- 女仆落点有三级兜底：先找同一列最近的可走格 → 整张表水平最近 → 如果落点在一个小连通块里（比如和地板连不上的半格床）就换到主区域。少了这一步，她会落在床那两格上出不来，表现为"站着不动"
- 挑随机目标最多试 8 次：随机挑到的格子可能是走不到的（孤岛），一次失败就回待机的话她会频繁发呆。8 次都算不出路径时每 10 秒打一条警告，别让它静默卡住
- 方向是可调的：`HouseGridView.Axis` 有 8 种候选（旋转 0/90/180/270 × 是否镜像 X），默认 `MirrorX`（glTFast 的行为）。用 `python Tools/house_align_check.py <house.gltf> <house.json>` 量出来再定（原来的 `HouseAlignCheckWindow` 已删除）。换算实现**只有 `HouseGridMapper` 一份**，也不要再加"写死默认方向"的便捷重载——之前同一个规则抄了四份，改一处漏三处
- 排查顺序：先看 Console 有没有编译错误（有的话代码根本没生效）→ 跑一次 `python Tools/house_align_check.py` 确认 Axis → 再看女仆日志是"找不到能走到的目标"（格数据/连通性问题）还是"走不动"（被几何卡住，碰撞体问题）
- **换房子**：`HouseSwitcher`（扫 `saves/house`）负责切换——切之前调 `MaidManager.PutAllAway()` 把场上女仆**全部收回背包**（位置先记进存档），然后**等两帧**让 NavMesh 数据卸掉再加载新的（否则新旧两张 NavMesh 会重叠一帧），最后写回 `slot0.json` 的 `house_id` 并重新取景；UI 是 `HousePanel`（滑出式列表，和 `BagPanel` 同一套 `_slideRect` + `_hiddenOffset`，场景摆位就是显示位），门口按钮接它的 `Toggle()` / `Expand()`，行预制体可选（挂 `HouseRow`）。启动时如果场景里已经有房子（`HouseSpawner` 加载的）就接管、不重复加载
- **进游戏加载哪栋看 `slot0.json` 的 `house_id`**：`HouseSpawner._houseId` 留空时会读存档（`ResolveHouseId`），那栋不在列表里才退回第一栋并警告。以前是直接取 `houses[0]`（`ScanRoot` 按 uuid 排序），只是当前存档里那栋恰好排第一才没暴露——一旦不一致，`MaidManager.RestorePlacedMaids` 会把 house_id 对不上的女仆全部标成"在背包"**并写进存档**，表现就是女仆一进游戏全消失。`HouseSwitcher.AdoptCurrent` 接管现成房子时会把它自己的 `_position`/`_yaw` 改成那栋的（不然切换时的落点和 `HouseSpawner` 摆的对不上，整栋会跳位置）
- **切走的房子默认挂起不销毁**（`HouseSwitcher._parkedLimit`，默认 1）：`SetActive(false)` + `HouseNavMesh.SetNavMeshActive(false)`（把 `NavMeshData` 从系统里摘掉、数据留着），切回来时 `SetNavMeshActive(true)` + `SetActive(true)` 就能立刻用——这才是"预制体缓存"在运行时的可行形态（`PrefabUtility` 是 `UnityEditor` 的，打包后的游戏里建不了真预制体）。超上限的老房子才 `Destroy`。注意挂着的房子仍然占内存（那栋房子的图集有 128 MB），上限别调大
- **删除房子**：`HouseSwitcher.Delete(id, out error)` 删整个存档目录。规矩：① **正在住的那栋不给删**（先切走再删，`IsCurrent` 挡掉）——不然场上那栋的对象/NavMesh/女仆位置都得现场收拾；② 删之前用 `Path.GetFullPath` 确认目录真的在 `AppPaths.HouseSaveRoot` 下面（house.json 里的路径不可信，递归删必须验）；③ 挂起（`_parked`）的那份一起 `Destroy`；④ 删完通知 `MaidManager.OnHouseDeleted(houseId)`——把那栋里的女仆收回背包并清掉 `house_id`（不清的话存档里会留着指向已删房子的记录，下次进游戏还会被判"在别栋房子"再收一次）；⑤ `slot0.json` 的 house_id 万一指向它，改写成当前房子。UI 在 `HouseRow`：预制体接了 `_deleteButton`（可选 `_deleteLabel`）就用你的，不接就在行右侧自动生成一个红按钮；删除是**点两下确认**（第一下变"再点一次"，`_confirmSeconds` 内再点才删），当前那栋的按钮直接隐藏
- **日志约定**：只留两类——一次性摘要（加载成功、NavMesh 烘焙结果）和真失败警告（限流，比如 5~10 秒一条）。排查用的 trace / 状态跟踪 / 逐帧打印用完就删，别留在代码里（进游戏卡顿那轮加的耗时日志已经删干净，现在看进度条就知道卡在哪一步）
- **已知的房子加载热点**：`minecraft:textures/atlas/blocks.png` 那张是**整张 8192×4096 的 MC 方块图集**（PNG 7 MB → 解出来 128 MB），而房子里只用到其中几个 sprite（导出报告 `report.json` 里有 `ATLAS_SPRITE_RESOLUTION_FAILED`，说明导出器没解析出 sprite 就把整张图集塞进来了）。要再快，优先在模组侧只导用到的 sprite；Unity 侧理论上能按 UV 裁图集 + 重映射 UV，但对 REPEAT 平铺的面有风险，动手前先确认
- **加载进度条**：`Assets/Scripts/Core/UI/LoadingScreen.cs`。要加载的地方 `LoadingScreen.Register(key, weight, label)`、完事 `Complete(key)`——房子是 `HouseJob`（权重 3，`HouseSpawner` 和 `HouseSwitcher` 都用它）、女仆是 `MaidJob`（权重 1）。有 job 才显示，全部完成 + 过最短 0.45 秒才淡出，30 秒超时也会放行；没有 job 时它在画面上完全不存在，所以 `[RuntimeInitializeOnLoadMethod]` 在每个场景建一个也无所谓。自定义 UI 就接 Root / Label / Fill，不接就在运行时生成盖满全屏的（自带 Canvas，sortingOrder 32000，全屏那层吃射线，加载期间点画面不会误触下面的 UI）。`HouseSwitcher` 只在实际要解析 glTF 的那条路注册（挂起来直接复用的那条不注册，不然会白闪一下）
- **房子面板和背包面板互斥**（不能同时摊开）：两个都开了 `static Instance`，各自在 `SetVisible(true)` 里把对方 `SetVisible(false)`。别改成"另一个开着就不给开"——那也是互斥，但点对方按钮没反应会显得像坏了；现在点谁就切到谁。两边都是滑动动画，一个进一个出同时播，互不干扰
- `MaidManager.RestoreMaidsRoutine` 是**分帧**放人的（每只之间 `yield return null` 并 `Report` 进度）：一帧放完的话进度条根本没机会画，女仆多的时候还会顿一下。房子加载失败要调 `MaidManager.OnHouseFailed()`，否则女仆那边会白等满 `_restoreWaitSeconds`（20 秒）才放行
- **从背包里放置也走进度条**（`LoadingScreen.PlaceJob`）：重活藏在 `IBagItemProvider.TryPlaceAt` 里（女仆第一次要整条转换，好几秒），所以 `BagPlacementController.Confirm` 改成了协程——先把进度条亮出来、`yield return null` 让这一帧画完，下一帧才真正开始加载。**同一帧里 Register 完就干重活的话，玩家只会看到画面卡住，进度条要等卡完才出现**，这个顺序不能省。只在"真要加载"时才亮：provider 可以实现可选的 `IBagSlowPlacement.NeedsLoading(id)`（女仆：资产没加载 **且** 缓存不新鲜才算），否则几十毫秒的缓存读取也亮一下就是闪屏
