# MaidHome

Unity 游戏工程，需要和 Minecraft 模组（NeoForge 1.21.1 生态，例如 Touhou Little Maid、Sable）打交道。

## 工程事实

- Unity 2020.3.30f1，Editor 位于 `C:\Program Files\Unity\Hub\Editor\2020.3.30f1\Editor\Unity.exe`
- Api 兼容级别 .NET Standard 2.0，脚本后端默认 Mono，内置渲染管线，Gamma 色彩空间
- 已装包见 `Packages/manifest.json`（TMP 3.0.6、Test Framework 1.1.29、Timeline、uGUI）。没有 Newtonsoft.Json、没有 Addressables
- 改 `Packages/manifest.json` 会触发下载，需要先说明理由

## 平台目标

- Android + Windows 双平台，**Android 走 APK 分发，不发布 Google Play**。所以不考虑商店审核限制，`MANAGE_EXTERNAL_STORAGE` 可以用
- 涉及平台差异的代码必须双实现（Windows / Android），公共逻辑抽到平台无关层，不把 `#if` 散落到玩法代码里
- Android 构建前置（尚未完成）：Unity 2020.3.30f1 目前只装了 Windows 构建模块，Android Build Support 需要先在 Unity Hub 里补装
- Android Target API 用 30（Android 11）。Unity 2020.3 自带的 Gradle/AGP 版本老，调更高要改 gradle 模板，没必要
- 需要声明权限时要加自定义 `AndroidManifest.xml`

## 代码规范

- 注释从简：只在关键、非直觉、有坑的地方写；不写 "Start is called before the first frame update" 这类模板注释，不逐行复述代码
- 命名：类型/方法/属性/常量 PascalCase，局部变量和参数 camelCase，私有字段 `_camelCase`，接口 `I` 前缀
- Inspector 字段用 `[SerializeField] private`，不暴露 public 字段
- 一个文件一个 public 类型，文件名和类型名一致
- 需要注释时用中文；标识符用英文
- 不过度抽象：优先直接实现，不为"以后可能需要"提前分层

## 目录约定

- `Assets/Scripts/Core`：基础设施（启动、存档、配置、事件）
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
- Android 存档目录（拿到权限后）：`/storage/emulated/0/Documents/MaidHome/saves`；拿不到权限就退回 `persistentDataPath/saves`
- 存档文件名 `slot0.json`，同时维护 `slot0.bak`；配置放 `config/settings.json`
- 格式 JSON，UTF-8，带 `version` 字段，方便以后改结构时做迁移
- 写入必须原子化：先写 `slot0.tmp`，关文件流后再 `File.Move` 覆盖 `slot0.json`，并保留一份 `slot0.bak`
- 不用 `PlayerPrefs` 存存档，它只适合放开关、音量这类几 KB 的设置
- Android 上不能依赖 `OnApplicationQuit` 保存（进程可能被系统直接杀掉），要在 `OnApplicationPause(true)` / `OnApplicationFocus(false)` 和关键节点立即存
- 存档内容是不受信任的：读的时候要处理文件缺失、JSON 解析失败、字段越界，坏档退回 `.bak`
- 存档结构可以变，但必须靠 `version` 写迁移函数，**不许出现"旧版本存档读不出来"**；旧档升级失败时至少保留原文件不动
- 更新游戏（覆盖安装）保留存档，只有卸载会清空
- 包名（`applicationIdentifier`）要在发布前就定死，发布后改包名等于换了一个游戏：存档路径、云存档、商店评价全部断开
- Android 上 Unity 只有 `persistentDataPath`（应用专属外部目录，卸载即删）这一个可写路径。要写公共目录（`Documents` 之类，卸载后仍在）Unity 没有内置 API，只能走 SAF 或 MediaStore，需要 Java 桥
- 代码只接受一个"数据根目录"，启动时决定用本地还是远端，调用方不关心来源
- 不使用 `Assets/Resources` 放这些数据
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
- 现成的静态检查：`python Tools/mc_style_check.py`（guid 冲突、场景/材质引用、材质属性 vs shader 声明、脚本括号配平）、`python Tools/gltf_api_check.py`（核对 Interop/Gltf 用到的 glTFast API 在装好的包源码里是否存在）、`python Tools/gameplay_api_check.py`（核对 Gameplay 用到的工程内部 API）、`python Tools/maid_cache_check.py <maid.bin>`（校验缓存层级和动画通道路径能否对上）、`python Tools/house_grid_check.py <house.json>`（校验可通行格数据、打印逐层平面图和连通块）、`python Tools/gltf_info.py <file.glb> --stats`（打印 glTF 的材质 / sampler / alphaMode / doubleSided / 顶点色取值 / 每张贴图的灰度程度——判断贴图是不是靠染色上色就看这个）

## 渲染风格（MC 风格）

- 目标是在内置管线 + Gamma 空间下模仿原版观感：不用后处理、不用 HDR、不用环境反射
- 天空盒：`Assets/Materials/MinecraftSkybox.mat`（shader `MaidHome/MinecraftSkybox`），竖直渐变 + 太阳圆盘，不是贴图天空球。**地平线颜色必须等于 `RenderSettings.fogColor`**，否则远处地形和天空接不上。太阳圆盘方向取平行光的 `-forward`
- 方块/模型材质：shader `MaidHome/MinecraftBlock`，AlphaTest + 原版逐面亮度（顶 1.0、南北 0.8、东西 0.6、底 0.5）+ 线性雾 + 硬阴影，只吃一盏平行光。它注册进了 `GraphicsSettings` 的 Always Included Shaders，因为运行时是 `Shader.Find` 找的
- **必须乘顶点色**：原版草方块这类贴图本身是灰度的（`grass_block_top`、`grass_block_side_overlay`），生物群系染色被导出器烘进了顶点色 `COLOR_0`。实测导出器写的是 **sRGB 值**（例：`0.745,0.716,0.332` 正好是 `#BFB755` 草原草色），所以直接用、不做 `LinearToGamma`；哪天遇到按规范写线性值的模型再补转换。少了这一步草会渲染成灰色
- glTFast 自带的三个内置 shader（PbrMetallicRoughness / PbrSpecularGlossiness / Unlit）都**不吃顶点色**，所以 PBR 路径下草同样是灰的，不是换材质换坏的
- 原版草方块侧面由两张贴片叠成（`grass_block_side` + 灰度覆盖层），导出器会把覆盖层往外偏移 `0.001` 防共面，别手动挪
- 场景的雾 / 环境光 / 反射开关由 `McStyleRig`（`Assets/Scripts/Core/Rendering`）施加，参数在 `McStyleProfile` 上；rig 只保证渲染环境，不碰相机
- 相机走正交，`HDR` 和 `MSAA` 关掉，`Clear Flags = Skybox`。相机参数（正交尺寸、near/far）由相机自己定，rig 不改

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
- 没有 `animation_length` 又没有关键帧的动画，TLM 里长度是「无限」（`Double.MAX_VALUE`）：被 `anim_time` 驱动的（computer / chair 这种尾巴摆动）按 `360/乘数` 的公倍数烘一个循环，其余按静止姿势处理。把它们当 0 秒会变成疯狂循环，速度明显不对
- Molang 只实现了子集（四则运算 / 比较 / 三元 / `math.*`）：`query.anim_time` 有效，其余变量（`ysm.*`、`query.ground_speed` 等）默认 0，认不出的函数按 0 处理并记警告
- 预览用 `AnimationMode`，点「暂停」会恢复场景原姿势；想真的播给玩家看就用 `BedrockAnimationPlayer` + legacy `Animation` 组件

## 女仆存档 -> Unity 资源

- 存档结构：`saves/maid/<uuid>/maid.json`，字段 `model` / `texture` / `anim` 都是**相对本文件夹**的文件名；同目录还有 MC 侧塞进来的二进制 `maid_data.maid`，Unity 不碰。`saves/maid.animation.json` 是共用的基础动画
- 解析：`Assets/Scripts/Interop/Maid/MaidSaveData.cs`（`ScanRoot` 扫目录）
- 加载：`MaidAssetLoader.Load(maid)` —— 缓存新鲜就读缓存，否则转一遍（网格 + AnimationClip）并写缓存
- 动画合并：先铺共用的 `maid.animation.json`，再用模型自带的那份**按同名覆盖**（TLM 的行为）；一根骨骼都对不上的动画跳过并记警告
- 缓存：`MaidAssetCache` 写 `persistentDataPath/cache/maid/<uuid>/`，里面是 `maid.bin`（网格 + 烘焙好的动画采样）、`manifest.json`（源文件 sha256 + 统计，人可读）、`texture.png`。`manifest` 里 sha256 一变就重转；缓存是可再生的，删了只会慢一次
- 编辑器入口：`Tools/MaidHome/女仆存档`（扫描 / 单个或批量转换 / 生成到场景 / 清缓存 / 打开缓存目录）
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

- `house.json`（MC 侧导出，现在是手工标，以后自动生成）：`size` / `name` / `origin` / `model` / `walkable`。`model` 是同目录 glTF 的文件名（不带扩展名）
- **格语义（必须记住）**：`walkable[y]` 里 `'1'` 表示"实体能站在这一格的**底面**上，脚底高度 = 这一格的 y 坐标"。层序 y 递增，层内行按 z 递增、字符按 x 递增。索引 `(y * SizeZ + z) * SizeX + x`
- **能不能站由 MC 侧判断完**：下面是不是实心、头顶够不够 1.8 格、楼梯还是台阶桌子，Unity 侧一概不重新推——两边各推一次必然对不齐，而且 Unity 侧没有方块语义。格子表的原点固定是模型局部 `(0,0,0)`，`origin` 字段目前不用
- 解析：`Assets/Scripts/Interop/House/HouseSaveData.cs`（缺层/行数不对/非 0/1 字符都记警告并按不可走处理）；格视图 `HouseGrid`（`IsWalkable` / `Index` / `CellFeet` / `FindWalkableY`）
- 寻路：**曾经**是 `Gameplay/Navigation/GridPathfinder.cs`（格上 A*），2026-10 换成 NavMesh 时整个文件已删除。当初不用 NavMesh 的理由是"可通行数据只能由 MC 导出、几何表达不了台阶桌子"，后来发现 NavMesh 也能按数据挖洞（见下），而且烘焙时按角色半径收缩过、转角不蹭墙，所以换了过去
- 导入：`HouseSpawner`（测试入口）→ `HouseImporter.LoadAsync`：glTF 用 Interop 的 `GltfModelLoader` 加载，然后**必须给每个 MeshFilter 补 MeshCollider**（glTF 只有 MeshRenderer，不补碰撞体女仆会直接穿到地板下面），最后挂 `HouseGridView`（带格表 + 世界↔格换算）
- 女仆：`MaidNavigator` 沿拐点走；`MaidWanderer` 场景里有 `HouseGridView` 就按格寻路，没有就退回 `WanderArea` 在平面直走
- **寻路现在是 NavMesh**（2026-10 换的）：`HouseNavMesh`（`Assets/Scripts/Gameplay/House`）在房子加载完、加好 MeshCollider 之后烘一次（`CollectSources` → `BuildNavMeshData` → `NavMesh.AddNavMeshData`），随后 `MaidNavigator` 用 `NavMesh.CalculatePath` 取 corners，仍然喂给原来那套"沿拐点走"的逻辑；目的地从房子包围盒随机采点 + `NavMesh.SamplePosition`
- **烘出来的"能走"只是几何意义上的能走**：台阶拼的桌子照走不误，所以必须按数据挖洞。`HouseGrid.MarkEnclosedNonWalkable` 只标"被走道围住的不可走格"——外墙靠几何本来就挡住了，挖了反而会吃掉贴墙的走道；挖洞用 `NavMeshObstacle`（Box + Carving）
- 换 NavMesh 时把退役的一并清了：`GridPathfinder.cs`（整个文件）、`HouseGrid.FindWalkableY`、`HouseGrid.CellFeet`、`HouseGridView.WorldToCell`/`TrySnapFeet`/`TryFindNearestCell`/`Describe`、`MaidNavigator.Describe`。`HouseGridView` 现在只剩 `Grid` / `Axis` / `CellFeet`（挖洞、gizmo、房子对齐检查在用）
- 对齐检查仍然有用：挖洞位置是 `HouseGridMapper.ToLocal(view.Axis, ...)` 算的，Axis 错了洞就挖在别处
- 调试：`HouseGridGizmoDrawer`（`Assets/Scripts/Editor`）把可走格画在 Scene 视图里，**只用于调试**，顺便用来确认"行到底是 z 还是 x"这种朝向问题；`HouseAlignCheckWindow`（菜单 `Tools/MaidHome/房子对齐检查`）用向下射线验证 8 种朝向里哪一种能让格子落在楼板上
- **glTFast 导入时会把顶点的 x 取反**（右手系转左手系，见包里的 `Jobs.cs`：`new float3(-(float)off[0], off[1], off[2])`），所以 Unity 里看到的模型相对 glTF 文件是沿 X **镜像**的。格数据是方块/文件坐标系（不镜像），映射到世界时必须跟着取反，否则整张格表偏 W 格、形状左右翻（看起来像转了 90°）。换算**只在 `HouseGridMapper` 里写一份**，`HouseGridView` / `MaidNavigator` / gizmo 都调它
- 格到世界取的是**格中心**（`CellCenter`），不是角点：取角点女仆会贴着墙走
- 女仆落点有三级兜底：先找同一列最近的可走格 → 整张表水平最近 → 如果落点在一个小连通块里（比如和地板连不上的半格床）就换到主区域。少了这一步，她会落在床那两格上出不来，表现为"站着不动"
- 挑随机目标最多试 8 次：随机挑到的格子可能是走不到的（孤岛），一次失败就回待机的话她会频繁发呆。8 次都算不出路径时每 10 秒打一条警告，别让它静默卡住
- 方向是可调的：`HouseGridView.Axis` 有 8 种候选（旋转 0/90/180/270 × 是否镜像 X），默认 `MirrorX`（glTFast 的行为）。用菜单 `Tools/MaidHome/房子对齐检查` 量出来再定。换算实现**只有 `HouseGridMapper` 一份**，也不要再加"写死默认方向"的便捷重载——之前同一个规则抄了四份，改一处漏三处
- 排查顺序：先看 Console 有没有编译错误（有的话代码根本没生效）→ 跑一次「房子对齐检查」确认 Axis → 再看女仆日志是"找不到能走到的目标"（格数据/连通性问题）还是"走不动"（被几何卡住，碰撞体问题）
- **日志约定**：只留两类——一次性摘要（加载成功、NavMesh 烘焙结果）和真失败警告（限流，比如 5~10 秒一条）。排查用的 trace / 状态跟踪 / 逐帧打印用完就删，别留在代码里
