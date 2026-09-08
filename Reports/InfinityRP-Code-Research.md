# InfinityRP 代码研究报告

> 真源：本仓库 C# / HLSL / compute / shader / 测试 / 工具脚本。  
> 未使用 `Docs/`、`qoder` 或任何仓库文档作为依据。  
> 研究对象：`com.infinity.render-pipeline`，版本 `0.3.0`，目标 Unity `6000.5.3f1`。  
> 程序集：`Unity.RenderPipelines.Infinity.Runtime` / `.Editor` / `.Tests` / `.Shader`。

---

## 0. 怎么读这份报告

InfinityRP 不是“几个 Shader 拼出来的渲染效果包”，而是一条自研 Scriptable Render Pipeline：它自己管场景实例、自己编绘制列表、自己排 RenderGraph、自己声明资源寿命、自己做历史帧事务。

读的时候可以按三层来：

1. **一帧怎么走完**（第 4 节）——先建立时间线。
2. **谁拥有什么数据**（第 5–8 节）——RenderGraph、MeshScene、灯光、语义缓冲。
3. **图像怎么被算出来**（第 9–13 节）——GBuffer、光照、大气、屏幕空间、半透明、后处理。

文中出现的类型名、Pass 名、缓冲 ID、常量和断言，都直接来自源码。

---

## 1. 它是什么

一句话：一条面向现代 Compute 平台的高保真 Unity 渲染管线，核心是 **延迟着色 + Z-Binning 前向补充 + 自研 Mesh 绘制后端 + 自研 RenderGraph**。

`package.json` 自己的定位是：Physically Based Lighting、线性光照、HDR、可配置的 Z-Binning Forward+。依赖里最关键的是 CoreRP 17.5、Burst、Collections、Mathematics、Jobs；Shader Graph 与 VFX Graph 被列为依赖，但自定义 VFX 输出路径在当前代码里没有接到帧图上。

它和 URP / HDRP 的最大差别不在“有没有 Bloom”，而在所有权：

| 别人通常交给引擎或插件的事 | InfinityRP 自己做 |
|---|---|
| Unity Renderer 直接出画 | `MeshComponent` 关掉 `MeshRenderer` 出画，改走 `MeshScene` |
| Unity RenderGraph / 手工 CommandBuffer 串 Pass | 自研 `RGBuilder`，Pass 只跟能力接口说话 |
| 历史帧随便拿上一帧 RT | 只有 `HistoryCache` 能跨帧，提交后才提交换页 |
| 缺 Shader 就跳过、黑贴图顶上 | 必要生产者必须在 record 时失败 |

---

## 2. 仓库地图

按职责而不是按文件夹名：

```
Runtime/
  RenderPipeline/          帧编排、Pass、Volume、验证捕获
  RendererCore/
    RenderGraph/           自研图：记录 / 编译 / 执行 / 回收
    PrimitivePipeline/     MeshScene + GPU 间接绘制；地形/水是旁路
    LightPipeline/         灯光打包、阴影切片分配
    GPUResource/           描述符、资源池、HistoryCache
    Container/             Native 容器与 ID
  Component/               场景侧组件（Mesh / Light / Camera / Decal / Terrain…）
  PostProcess/             Volume 组件（曝光、SSR、体积雾…）
  RenderingFeature/        大气 Profile、TAA 抖动、硬件 RT 生成器（未接线）
Shaders/
  Surface/                 InfinityLit / Decal / Terrain
  ShaderLibrary/           BSDF、GBuffer、光照、大气、屏幕空间射线
  RenderingFeature/        各 Compute / Raytrace
  ColorGrading/            CombineLUT、ACES、ToneMap
Editor/                    Inspector、材质 GUI、验证菜单、迁移工具
Tests/Editor/              EditMode 契约测试
Tools/                     刷新编辑器、截窗、捕获对比、Player 构建
```

规模大致是：Runtime 约 150+ 个 C# 文件，Shaders 约 60 个着色器源，Editor 约 50+ 个编辑器文件，Tests 24 个测试文件。Pass 全部是 `InfinityRenderPipeline` 的 `partial`，模式固定为 `XxxPassData` + 管线方法。

命名从代码里能读出一套稳定习惯：

- 命名空间 `InfinityTech.<Domain>`
- 枚举 `E` 前缀（`EPassType`、`EFrameFeature`）
- 几何 / 句柄结构体 `F` 前缀（`FBound`、`FLightRecord`）
- RenderGraph 类型 `RG` 前缀
- Job 以 `*Job` 结尾
- 私有字段 `m_`
- Shader 绑定 `SRV_` / `UAV_` / `CBV_`

---

## 3. 从代码里能读出的设计哲学

这些不是口号，而是反复出现的实现约束。

### 3.1 实体数据属于实体，Draw 只引用它

一个逻辑物体拥有一个 `TransformId` 和一份 bounds。子网格、材质、Pass 只增加 `MeshDraw` / 模板引用。`CreateInstance` 发现 `TransformId` 已被别的实例占用会直接抛错。稳态不变量是 `MeshScene.MatrixDuplicateRatio == 1.0`：一份变换对应一个实例。

### 3.2 稳定模板可缓存，视图结果必须重算

`MeshPassDraw` 按材质 / section / revision 缓存。可见性、LOD、排序、压缩都是“这一次请求 / 这一台相机”的产物。摄像机动了不会去改场景表，只会作废或重用可见性 intern。

### 3.3 一套公共绘制 API，后端可换

调用方只看见 `MeshDrawRequest` / `RGDrawListRef`。CPU 直接绘制和 GPU 间接绘制藏在 `MeshDrawPipeline` / `MeshDrawGPUBackend` 里。`EMeshBackendPolicy.Auto` 在间接参数与 6 个 compute kernel 都在时走 GPU，否则回退 CPU。

### 3.4 图原生寿命，物理资源晚于 Submit 才退休

`DeclareDrawList` 只记录。结构级 Pass 剔除先跑，活着的 DrawList 才 Schedule。图结束做逻辑释放（`RetirePayload`），`ScriptableRenderContext.Submit()` 之后才 `FlushRetired*`。录制中途禁止 `EnsureCapacity` 重建已经绑到 CommandBuffer 上的 buffer。

### 3.5 一个物理量一个权威

大气只活在 `AtmosphericalProfile` 上，没有 Volume 覆盖，也没有 `AtmosphereParameter.Default()`。默认 Volume 值只来自 RP Asset 的 `volumeProfile`。屏幕上最终编码只由 `OutputTransform` 负责。语义缓冲只认最后一个注册它的生产者（例如呈现读 `DisplayColorBuffer`，不猜 `AntiAliasingBuffer` 的别名）。

### 3.6 不允许用静默换正确

必要 Pass 缺 kernel 在 record 时抛。`FrameFeatureSet.EnsureRequiredProducers` 检查 Depth / GBuffer / DeferredShading / Display，以及未开超分时的 TAA。执行函数里看不到“shader 为空就 return”。无效 `RGTextureRef` 不会被黑贴图顶掉。

### 3.7 哈希只加速查找，相等才是身份

`GroupingKey`、`MeshPassDrawCacheKey`、`BufferDescriptor`、`TextureDescriptor`、`HistoryCache` 重分配，全部是字段级 `Equals` 说了算。`GetHashCode` 只负责进桶。

---

## 4. 一帧是怎么跑完的

入口是 `InfinityRenderPipeline.Render`。可以把它想成“一次邮局作业”：先收集信件（场景更新），再按相机分别分拣装车（Record），再统一发车（Execute + Submit），最后才允许拆掉旧车厢（Flush）。

### 4.1 帧头（每个 `Render()` 一次）

1. 把 Unity 的 `ScriptableRenderContext` 挂到 `RenderContext`。
2. `RGBuilder.RecoverUnjoinedAsync`：上一帧异步计算如果没 join 完，先恢复。
3. 三个验证泵：`RenderCaptureService`、`RenderFaultValidation`、`PostEffectValidation`。
4. `InvokeProxyUpdate`：组件通过 `FGraphics` 任务队列注册 / 注销 / 刷脏。
5. `MeshSceneResidency.Update`：脏范围上传变换、前一帧变换、包围盒。
6. `BeginContextRendering`。

### 4.2 每台相机

1. 用 `GetCameraID` 取或建 `CameraFrameState`（Preview 且 `pixelHeight == 64` 有单独 ID，避免材质图标和预览抢状态）。
2. 判断是否强制历史重置：新建状态，或 `Time.frameCount - lastSeenFrame > 1`。
3. `historyCache` / `atmosphereViewCache` / `combineLutCache` 各自 `BeginFrame`。
4. `VolumeManager.Update`：用 `CameraComponent` 的 trigger 和 layer mask。
5. `CameraUniform` 更新全部矩阵族并上传。
6. Unity 裁剪、地形 LOD、`LightContext.Build`、`VFXManager.ProcessCameraCommand`。
7. `ConfigureFrameFeatures`：决定本帧请求哪些特征、哪些 kernel 支持。
8. **RecordRG**：Phase 0–8 只记录，不真正画。
9. **ExecuteRG**：`RGBuilder.Execute`。成功则 `executeSucceeded = true`。
10. `finally`：清图、放可见性句柄、把当前矩阵存成“上一帧”。

一台相机失败不会立刻拆掉整帧：它 `RollbackFrame`，记下第一例外，其它已成功的相机仍可能进入 Submit。异步队列提交失败则立刻上抛。

### 4.3 帧尾

`FrameSubmission.Execute` 把图命令 `Queue` 进 context 再 `Submit`。然后：

- `CompleteFrameTransaction`：共享大气缓存和每台相机的 History / LUT 缓存，按“提交成功且该相机 execute 成功”来 Commit，否则 Rollback。
- 只有 `submitted && CanRetireSubmittedResources` 才 `FlushFrameRetirement`。
- 长时间看不见的相机状态回收：Game / Preview 8 帧，SceneView 120 帧。
- 若有第一例外，帧结束再抛出。

### 4.4 九个 Phase 的时间线

这是 `Render()` 里的权威顺序，不是文档想象：

| Phase | 做什么 | 为什么在这里 |
|---|---|---|
| **0** | 上传灯光、CombineLUT、大气 LUT | 零 RG 资源输入，可与后面几何重叠 |
| **1** | Depth → DBuffer → GBuffer → Motion | 共享深度链的几何光栅 |
| **2** | HiZ、半分辨率下采样、Z-Bin | 吃深度，给后面的 AO / 分簇 / 体积 |
| **3** | 阴影裁剪 → CSM → 局部阴影 | 最长的 ROP 窗口 |
| **4** | 预留（体积雾已挪到 7） | — |
| **5** | GTAO、拷历史 AO、接触阴影 | 需要 HiZ / HalfRes |
| **6** | Deferred → Forward → SSS → 大气合成 → **捕获 Lighting** → 不透明颜色金字塔 → SSR/SSGI → Composite → `OpaqueSceneColor` | 不透明光照闭环 |
| **7** | 半透明深度 → ReactiveMask → 体积云/雾 → FogComposite → T0 → ColorPyramid → T1 → T2 | 体积必须先于玻璃，折射金字塔必须后于 T0 |
| **8** | 超分 **或** TAA → 后处理 → DebugView → Gizmo → OutputTransform → 捕获 → Present | 时间滤波吃雾和半透明之后的场景色 |

颜色语义的接力也很死：

```
LightingBuffer
  → (SSR/SSGI Composite) OpaqueSceneColor
  → (FogComposite) FoggedSceneColor
  → (T0/T1/T2 MRT) 仍写 FoggedSceneColor + ReactiveMask + Motion
  → TAA: AntiAliasingBuffer  或  SR: SuperResolutionBuffer
  → PostProcessBuffer（线性、已调色）
  → DebugView 可覆盖 PostProcessBuffer
  → Gizmo 画在这块线性缓冲上
  → OutputTransform → DisplayColorBuffer → Present
```

---

## 5. 自研 RenderGraph

位置：`Runtime/RendererCore/RenderGraph/`。这不是 Unity 官方 RenderGraph 的薄封装，而是一条完整的记录-编译-执行-回收管道。

### 5.1 四种 Pass，四种能力接口

`EPassType`：`Transfer`、`Compute`、`RayTracing`、`Raster`。

记录 API 与执行委托一一对应。Pass 的 execute lambda 拿到的是编码器，不是裸 `CommandBuffer`：

- `ITransferCommands`：上传、拷贝、异步读回
- `IComputeCommands`：绑定、Dispatch
- `IRaytracingCommands`：AS / DispatchRays
- `IRasterCommands`：全局量、视口、Draw、`Draw(RGDrawListRef)`

图外的合法出口只有 `CommandBufferCommands`。这个边界在代码里被守得很严：特征类不直接碰 `CommandBuffer`。

Pass 类型不是“为了绕 API 而调的旋钮”。拷贝走 Transfer，Dispatch 走 Compute，Draw 走 Raster。Gizmo / WireOverlay / Present 也是 Raster，因为它们最终都是画。执行函数里不 `SetRenderTarget`——附件在记录时声明，由 `RGBuilder` 绑定。

### 5.2 资源模型

`ERGResourceType` 只有 Buffer、Texture、AccelerationStructure。**DrawList 不是资源类型**，它活在单独的 `RGDrawListContext` 里，带 `generation`，图清空后旧句柄失效。

创建与导入是两种寿命：

- `CreateTexture` / `CreateBuffer`：记录时只有描述符，第一次写入的 Pass 才分配。
- `ImportTexture` / `ImportBuffer` / `ImportBackbuffer`：外部已有对象。写入导入资源会把 Pass 标成 `hasSideEffect`，即使没人读也不能被剔。
- Backbuffer 没有所属 `RenderTexture`，所以 Present 必须关 Native Render Pass。

描述符相等是池命中和 History 重分配的唯一权威。`TextureDescriptor.Equals` 故意不比较 `name`、`enableMSAA`、`msaaSamples`、`clearBuffer`、`clearColor`——这些是用法，不是身份。

### 5.3 语义注册：`RGScoper`

图认句柄，管线认语义 ID。`RGScoper` 把 `InfinityShaderIDs.DepthBuffer` 这类整数映射到本帧的 `RGTextureRef`。`Query*` 找不到就抛。`MoveTexture` 把所有权从一个语义名转到另一个（`OpaqueSceneColor` → `FoggedSceneColor` 在没有体积时就是这样，省一次 Dispatch）。

这避免了“下游猜别名”。Present 只问 `DisplayColorBuffer`。

### 5.4 编译四步

`Execute` 开头跑 `CompilePass`：

1. **CountPassReference**：统计读写，导入写入 = 副作用。
2. **CullingUnusedPass**：从无人读的资源倒着走，`refCount==0 && !hasSideEffect && enablePassCulling` 才剔。
3. **CompileDrawLists**：只有没被剔的 Pass 用到的 DrawList 才 `Schedule`。
4. **UpdateResource**：第一次有效写处分资源，最后一次有效读处释放；跨队列时把释放推到 fence 之后。

### 5.5 Native Render Pass 与异步计算

Raster 默认 `enableNativeRenderPass = true`，走 `BeginRenderPass`。只有两处被允许关掉：Gizmo / WireOverlay（Unity 禁止在 `BeginRenderPass` 里画 Gizmo），以及 Present（backbuffer 无法填 `AttachmentDescriptor.graphicsFormat`）。颜色-only 目标用单参数 `SetRenderTarget`，绝不绑一个空的 depth identifier——Unity 会报 `temporary render texture not found`。

GPU DrawList 的 `SetBufferData` / `DispatchCompute` 必须发生在 Native RP 之外，注释写明这是 D3D12 限制。

Compute 可 `EnableAsyncCompute(true)`。第一条异步 Pass 先在图形队列建 fence，异步队列等待后再 `ExecuteCommandBufferAsync(..., ComputeQueueType.Background)`。跨队列读写会插 `GraphicsFence`。帧结束等 `lastAsyncFence`。异步提交不可恢复时，`CanRetireSubmittedResources` 为假，禁止把资源还回池。

### 5.6 失败怎么传播

`RGBuilder.Execute` 捕获例外，`finally` 里清图、还临时分配，然后用 `ExceptionDispatchInfo` 原样再抛。没有“返回 false 假装成功”。缺 execute 函数直接抛。`RenderGraphFailureTests` 覆盖：Load 动作算一次读；失败后未归还分配进入退休队列。

编辑器里的 `RenderGraphWindow` **没有接到实况图**，只是占位节点。真正的编译快照是 `DescribeCompiledGraph()` 文本表，给捕获会话写文件用。

---

## 6. Mesh Drawing Pipeline

这是 InfinityRP 最“引擎化”的一块。它把“场景里有哪些网格”从 Unity Renderer 里抽出来，变成可事务、可回滚、可 GPU 剔除的 SoA 表。

### 6.1 五张表，带代次的 ID

`MeshScene` 维护五张并行表：Instance、Transform、Draw、Section、Material。每张表有数据数组、generation 数组、空闲链表、`highWater`、活计数。

ID 都是 `(Index, Generation)`，`Generation == 0` 非法。复用槽位时 generation 递增且永不回到 0。过期句柄在 `Is*Alive` 上失败，而不是默默打到别人身上。

修订分三本账：

- `StructuralRevision`：增删实例 / 变换 / draw / section / 材质
- `ContentRevision`：写变换、改材质优先级等
- `VisibilityRevision`：影响剔除的 bounds / flags / layer

### 6.2 事务必须能完整撤销

对外入口是 `scene.BeginUpdate()` → `MeshSceneUpdate`。构造时先压一条 `RestoreRevisions`。之后每个突变都先压逆操作。

- `Commit`：清 undo，刷新待回收的 section/material。
- `Dispose` 而未 Commit：倒序 `ApplyUndo`，恢复修订和脏范围。**不截断空闲链表，不回退 highWater**——这两样归 Free/Restore/延迟回收管。
- 事务中 section/material 的 `refCount` 降到 0 只进 pending 队列；Commit 才真释放，Rollback 在 undo 恢复 refCount 后丢掉 pending。

`MeshScene.Dispose` 若发现还有打开的 update，会先 Rollback。测试 `MeshSceneTests` 把这件事钉死。

### 6.3 过滤 / 分组 / 排序是三件事

不要用一个哈希同时做三件事。

1. **Filter**（`MeshPassFilterJob`）：实例活着、CPU 可见、Pass 资格、Queue 范围、Unity layer、可选 rendering layer、运动类型。输出 `VisibleMeshDraw`。
2. **Grouping**（`MeshGroupingKey`）：`mesh + section + material + pipelinePass`，用来合并成实例化批次。缓存键 `MeshPassDrawCacheKey` 另算，Equals 才是命中。
3. **Sort**（`MeshSortPlan`）：最多 4 个字段压进 64-bit 键，每段 16 bit，**显式饱和**，禁止静默截断。距离、Queue、稳定 DrawId 都是独立语义。

内置 Pass 的默认排序：Depth / Shadow / Motion 偏距离，GBuffer / Forward 偏 RenderQueue。Motion 还排除纯摄像机运动实例。

### 6.4 CPU 剔除与 GPU 剔除的索引域不同

这是整条管线最容易写错的合同：

| 数组 | 索引含义 | 谁用 |
|---|---|---|
| `instanceSlotIndices` | `MeshInstanceId.Index` | GPU 候选、可见性、包围盒 |
| `instanceIndices` | `TransformId.Index` | CPU 提交、着色器查矩阵 |

`MeshSceneResidency` 里变换缓冲按 transform 索引，包围盒按 instance 索引，中间用 `InstanceTransformIndexBuffer` 做映射。

GPU 路径（`Compute_MeshDrawPipeline.compute`）六步：

1. `CullInstances`：对所有 instance 槽做 AABB–视锥
2. `ClearCommandCounts`
3. `CompactCommandInstances`：可见实例 → **写出 transform 索引**
4. `PrefixSumCommands`
5. `ScatterVisibleInstances`
6. `BuildIndirectArgs`

CPU 路径则 `DrawMeshInstancedProcedural`，绑定同一套 `InstanceIndexBuffer`（transform 索引）+ `TransformBuffer`。

GPU payload **按 DrawList 租还**，没有进程级共享的间接参数缓冲。录制前一次 `ComputePayloadBudget` + `EnsureCapacity`；录制中只 `RequireCapacity`，不够就整单回退 CPU，并增加 `GpuOverflowCount`。单批次上限：1024 条命令、65536 个实例。

### 6.5 可见性 intern

`MeshVisibilityShare` 用签名

`sceneId + viewKey + frustumHash + VisibilityRevision + policyId`

把剔除结果在同一帧里共享。主相机的 Depth / GBuffer / Forward / Motion 共用一个 handle。级联阴影和局部阴影用不同 `viewKey` 和 `policyId`。视锥平面量化到 1e-3 再 FNV。图结束 `ReleaseAll` 配对释放；`EndFrame` 清掉漏网的。

### 6.6 组件怎么进场景

`MeshComponent` 继承 `EntityComponent`：

- `OnEnable` → 禁用 `MeshRenderer` 出画 → `FGraphics` 任务里 `CreateTransform` / `CreateInstance` / 每子网格 `CreateDraw`。
- 每帧动态网格走 `EventUpdate`；静态网格在 Editor 里可按 `updateProxy` 刷，Player 里初始化一次。
- 结构变化（网格/材质个数或身份变了）整表重注册；轻量变化只改 flags / layer / 资格；位移只 `SetTransform` + `SetBounds`。

Pass 资格由材质路由推导：半透明只进 Transparent；前向不透明进 Depth|Forward；否则 Depth|GBuffer。运动向量要物体运动；阴影要非半透明且投射开。

### 6.7 旁路：地形、水、植被、贴花

| 模块 | 状态 |
|---|---|
| `TerrainComponent` + `TerrainSector` + LOD Job | 有实现，`ProcessLOD` 已接线 |
| `TerrainPassProcessor.DispatchDraw` | 有代码，**管线从不调用** |
| `OceanSector` / `WaterSector` | 空 `MonoBehaviour` |
| `FoliageComponent` | 空生命周期，字段被注释 |
| `ProbeComponent` | 空桩 |
| `DecalComponent` | 只往世界登记数量；`RenderDBuffer` 走 Unity `RendererList`，不进 MeshScene |

`MeshAsset` 这个 ScriptableObject 存在，但 `MeshComponent` 用的是原始 `Mesh` + `Material[]`，没有接到它。

---

## 7. 灯光、阴影与分簇

### 7.1 打包合同

`LightContext.Build` 两趟扫描可见光：先全部 Directional，再 Point / Spot / Rect。`RenderSettings.sun` 若在列表里记为 `SunRecordIndex`，否则用第一盏方向光。

`FLightRecord` 与 HLSL 逐字段对齐。最重要的一句在结构体注释里：

> `radiance.rgb = Light.color * Light.intensity` 在 CPU 上乘一次；`radiance.a = 1`。

着色器统一走 `LightRadiance()` 只读 `.rgb`。体积光再乘 `attenuation.z` 且要求 `LIGHT_FLAG_VOLUMETRIC`。`LightComponent` 额外提供 `diffuse` / `specular`（塞进 `axisX.w` / `axisY.w`）、接触阴影开关、体积开关、`lightLayer`。

`g_SunRecordIndex` 在 HLSL 里声明、在 C# 里计算，**上传路径没有绑到 GPU**。接触阴影实际读的是 `g_LightRecordBuffer[0]`。

不支持的 Unity 灯光形状直接 `NotSupportedException`。

### 7.2 Z-Binning / Forward+

仅当 `LocalLightCount > 0` 才记录。常量：

- 32 个深度 bin
- 每 tile 最多 64 盏灯
- tile 16×16 像素
- 三个 kernel：`LightCount`（清零 + 计数）、`PrefixSum`、`Fill`

Fill 写出的是**绝对 record 下标**（局部灯从 `directionalCount` 起）。溢出写入 `ZBinOverflowBuffer`。

消费不对称：

- **Deferred** 用 tile ∩ 像素深度 bin。
- **Forward** 只用 tile 列表。
- 没有局部灯时，消费者绑 LightContext 的空占位缓冲，而不是“假装有一份列表”。

### 7.3 阴影

`ShadowAllocator`：

- CSM 固定 4 级，2×2 图集，单级分辨率来自 Asset（默认 2048）。
- 默认 cascade 比例 `(0.067, 0.2, 0.467)`。
- 局部阴影：16 盏上限，4×4 图集，Spot 1 面、Point 6 面、Rect 0 面。
- 预算按 `shadowStrength / distance` 打分。

采样在 `ShadowSampling.hlsl`：级联和局部都是 3×3 PCF。级联下标用 `viewDepth < _CascadeSplitDistances[i]`。记录里的 `EShadowType`（Hard/PCF）**没有改变采样核**——着色器永远 PCF。

接触阴影是 R8 屏幕空间沿光行军，特征请求只看 kernel 在不在，**不看 Volume 是否 override**。强度乘进延迟方向光：`shadow *= contactShadow`。

代码里能直接看见的分裂：

- `AllocateCascade` 用 `settings.cascadeRatios` 问 Unity 要矩阵，但 `CadeSplitDistances` 来自另一套写死比例 × `shadowDistance`。两套一旦分叉，PCF 选级和矩阵会对不上。
- 管线裁剪参数里 `shadowDistance = 128` 写死，分配器却读 `pipelineAsset.shadowDistance`。
- Forward 的 `InfinityLit` 不采动态阴影图，只在 `LIGHTMAP_ON` 时用 lightmap 阴影。延迟走 CSM + 局部 + 接触。同一物体走两条路由，阴影权威不同。

---

## 8. GBuffer、延迟与前向

### 8.1 GBuffer 布局

| 目标 | 格式 | 内容 |
|---|---|---|
| GBufferA | R8G8B8A8_UNorm | YCoCg 反照率（棋盘 rg/rb）+ Roughness + Reflectance |
| GBufferB | R8G8B8A8_UNorm | BestFit 法线 + Specular |
| GBufferC | R8G8B8A8_UNorm | 着色模型+flags、SSS profile、厚度、rendering layer |
| LightingBuffer | R16G16B16A16_SFloat | GBuffer Pass 先写自发光，延迟再累加光照 |

YCoCg 棋盘的奇偶来自 `SV_POSITION.xy`。解码用 3×3 EdgeFilter 修色度。法线走 BestFit LUT（`g_BestFitNormal_LUT`，Asset 创建管线时设成全局）。`GBufferContractTests` 用 GPU 往返测反照率夹具和法线角度误差。

着色模型目前只接了两个：`DEFAULT_LIT = 0`、`SUBSURFACE = 1`。`ShadingModel.hlsl` 里还有 Skin / ClearCoat / Cotton / Silk / Hair，**InfinityLit 没有接线**。

DBuffer 在 `WorldDecalCount > 0` 时记录。三张目标（反照率 / 法线 / 粗糙反射），GBuffer 带 `_DBUFFER` 时 `ApplyDBuffer` 按通道 alpha 混合。当前仓库没有 `DBufferPass` LightMode 的业务材质时，这个 Pass 会记下来但可能 0 draw——Frame Debugger 会省略它，这不等于没记录。

### 8.2 延迟是必要生产者

`ComputeDeferredShading` 16×16 线程。输入 GBuffer、深度、CSM、局部阴影、接触阴影、可选 AO（关键字 `DEFERRED_AO`）、大气 GGX 预过滤和 L2 SH。

着色：`DefultLit`（GGX + Smith joint）。IBL `EvaluateAtmosphereIBL * ao` 加一次。矩形灯：Frostbite 形状因子（漫）+ Karis 代表点（高光）。自发光已经在 LightingBuffer 里，延迟不覆盖它。

AO 的所有权也在这里钉死：GTAO 只生产遮挡图，**应用发生在延迟对 IBL 的那一次相乘**。屏幕空间合成不再乘一遍 AO（`ScreenSpaceDenoiseTests` 断言这一点）。

### 8.3 前向是补充，不是第二套延迟

`RenderForward`：Load 深度（只读）和 Lighting（累加）。走 `ForwardPass`，Queue 0–2999。绑灯光记录、IBL、tile 列表。它负责 lightmap 物体和被路由到 Forward 的不透明表面。

`MaterialRouteUtility` 是路由权威：

- `_SurfaceRoute`：0 Deferred，1 Forward
- `_TranslucentStage`：0 不透明，1 T0，2 T1，3 T2
- `_Subsurface > 0.5` 必须是不透明延迟

`ApplyPassState` 显式开关 10 个 Pass 名，并改 `RenderType` / `renderQueue`。`ShaderGUI.ValidateMaterial` 只读——Inspector 重绘不得偷偷改序列化状态。运行时改路由必须自己调 `ApplyPassState`。

---

## 9. 大气与 IBL

大气是这条管线里“物理量单一权威”的样板。

### 9.1 只认 Profile

`AtmosphereParameter.FromProfile(null)` 抛。`ThrowIfInvalid` 查的是物理范围，不只是“是不是零”：大气厚度、Hillaire 散射/Mie/高度/臭氧、太阳张角。几何尺寸用米。散射/吸收系数在 Profile 里按**每公里**存，绑定到 GPU 时乘 `ScatterPerKmToPerMeter = 0.001`。两边单位不能混进同一份 compute。

地球缺省（代码常量）：行星半径 6 360 000 m，大气高 60 000 m，Rayleigh `(0.00580, 0.01356, 0.03310)` /km，Rayleigh 高 8000 m，Mie 散射 0.003996、吸收 0.000444、高 1200 m、各向异性 0.8，太阳张角 0.5°。

太阳方向和照度来自 `LightContext.ResolveSun`：方向是 `directionSpot`，照度已经是打包好的 `radiance.rgb`。

### 9.2 三层缓存

| 缓存 | 键 | 资源 |
|---|---|---|
| 共享 `AtmosphereSharedCache` | `AtmosphereParameter` | 透过率 LUT、多次散射 LUT |
| 每视图 `AtmosphereViewCache` | 参数 + 太阳方向 + 相机位置（取整到米） | SkyView、AerialPerspective、SunBuffer |
| 共享 IBL | 参数 + 太阳方向 | Cubemap、GGX 预过滤、9 个 L2 SH 系数 |

全命中时对应 Pass **零 Dispatch**。未生产的 pending 在相机失败时 `DiscardUnproducedPending`；共享缓存的有效性跟“队列是否接受过生产者”走，不跟单台相机成功绑死。

IBL miss 顺序：6 面 Cubemap → SH 投影 + reduce 成 9 系数 → 按 mip 做 GGX 预过滤。运行时 `EvaluateSplitSumIBL`：SH 漫反射 + cubemap-array 高光 × Lazarov 近似 envBRDF。没有单独的 IBL BRDF LUT 贴图。

大气合成 kernel `AtmosphereComposite`：天空像素（深度 == `SampledFarDepth`）采 SkyView + 日盘；场景像素 `scene * aerial.a + aerial.rgb` 写回 `LightingBuffer`。

---

## 10. 屏幕空间、时间滤波与运动向量

### 10.1 矩阵合同（整条屏幕空间的根）

`CameraUniform` 维护两套投影：`GL.GetGPUProjectionMatrix(..., true)` 和 `(..., false)`。代码里 **FlipY = `renderIntoTexture: false`**。光栅深度 / GBuffer 用抖动 VP（`UNITY_MATRIX_VP = Matrix_ViewJitterProj`）。从 `(screenUV, depth)` 重建世界位置，计算侧必须用抖动的逆 VP，也就是 `matrix_*FlipYJitter*`。

运动向量必须去抖动：`SV_POSITION` 仍用抖动 VP 以对齐覆盖，但算速度的 clip 位置用非抖动 VP。否则静止画面每个 Halton 步都会长出 `j_prev - j_curr`，历史查找会抖，TAA 会 bicubic 重采样整幅累加。

位置移动超过 2 m 或旋转超过 20°，`historyReset = true`。Preview 相机即使 reset 也继续加抖动——这是代码里明确的不对称。

### 10.2 HiZ 与颜色金字塔

HiZ：一张 R32 mip 链，**一次 compute 循环打 4 级**。颜色金字塔（不透明光照、折射）2 级一批。RG 跟踪的是资源不是子资源，按 mip 拆 Pass 只会制造假 hazard。`PyramidMipBatchTests` 锁的就是这两套数学。

半分辨率深度/法线给 GTAO。不透明光照金字塔给 SSR/SSGI 当 HiC。

### 10.3 GTAO

半分辨率五段：`OcclusionTrace → SpatialX → SpatialY → Temporal → BilateralUpsample`。Volume `ScreenSpaceAmbientOcclusion` 有 override 且 kernel 齐才记录。历史是半分辨率 AO + 半分辨率深度。时间权重用共享的 8 帧爬坡：`configured * saturate(validFrames / 8)`。

### 10.4 SSR / SSGI

共用骨架：`RayMarch → Spatial(循环) → Temporal → optional Bilateral`。

- SSR：`RayCast_Specular` 走 HiZ；命中色取 HiC mip 0。
- SSGI：`RayCast_Diffuse` 走金字塔深度，步数抬 mip；辐照取金字塔色。

历史各自三份：Radiance、Moments、DepthNormal。时间滤波用运动缓冲 + 双重重投影（命中运动 + 深度运动），权重同样 8 帧爬坡。

合成：SSGI `lighting += albedo * ssgi`；SSR 用 envBRDF 加权。输出 `MoveTexture` 到 `OpaqueSceneColor`。

代码里能看到的合同裂缝：

- `GraphicsUtility.ComputeUsesRenderIntoTextureProjection` 返回 `false`，计算绑定 FlipY。若某条重建仍按 `renderIntoTexture:true` 理解 Y，天空/深度会错。
- SSR 的层级变量在涨，但活跃路径采 HiZ 仍偏 mip 0，锥追踪不完整。
- `TAAJitter` 仍被全局上传，活跃 TAA kernel 不再用它去“反抖动采样”。

### 10.5 TAA 与超分互斥

`pipelineAsset.enableSuperResolution` 二选一。超分开则 TAA 不是必要生产者，`TAAConfidence` DebugView 也不可用。

TAA 要点（`Compute_TemporalAntiAliasing` + `TemporalAntiAliasingGenerator`）：

- Halton (2,3)，8 帧，spread 0.75。reset / 丢帧当帧抖动为零。
- 当前帧 **点采样精确 texel** `(id + 0.5) * texelSize`，不用 `screenUV - TAAJitter`。
- 历史 bicubic，`screenUV - velocity`。
- 深度拒绝：历史深度 3×3 邻域转线性眼空间，当前值必须落在 `[min,max]`，区间再按 `max(|max-min|, ε) * 0.5` 加 1% 相对垫。
- 屏幕外重投影权重清零。
- ReactiveMask：`weight *= 1 - reactive`（半透明写它）。
- 锐化只动 YCoCg 的 Y，并夹回邻域 min/max。强度常量 `0.35`。
- `TAA_BlendParameter.x = 0.97` 是质量旋钮，代码里原样留下。
- 输入场景色是 `FoggedSceneColor`（半透明之后）。
- 帧末 Transfer 拷颜色历史和 Depth32 历史。

超分 kernel 仍收 `SR_Jitter`，但当前样本是双线性 `screenUV`，没有 TAA 那套点采样合同。

Preview 相机没有 SceneView 那种“丢帧就关抖动”的门控。

---

## 11. 半透明、体积与后处理

### 11.1 T0 / T1 / T2

半透明不是“一个 Transparent Queue”，而是三档 LightMode：

| 档 | 名字 | 典型用途 | 额外输入 |
|---|---|---|---|
| T0 | `TranslucentT0Pass` | 玻璃 / 体积参与 | 体积雾 3D、Aerial LUT |
| T1 | `TranslucentT1Pass` | 折射 | T0 之后建的 `ColorPyramid` |
| T2 | `TranslucentT2Pass` | 粒子 / 叠加 | — |

三档都画在 `FoggedSceneColor` 上，MRT 同时写 ReactiveMask 和 Motion，深度只读。之前先跑 `TranslucentDepthPass`。体积云/雾和 FogComposite **必须在 T0 之前**，这样玻璃能采到体积；颜色金字塔 **必须在 T0 之后、T1 之前**，这样折射看到的是带玻璃的场景。

没有体积时 `ResolveFoggedSceneColor` 只做 `MoveTexture`，不跑 compute。

### 11.2 体积雾 / 云

雾：`ScatterDensity → Integrate → Temporal`，froxel 分辨率 `ceil(w/8) × ceil(h/8) × DepthSlices`，3D R16G16B16A16。体素 `lerp(current, history, weight)`，reset 时 weight = 0。绑灯光、阴影、tile/zbin、可选 SkyView / Aerial。

云：半分辨率 2D，核内时间混合，同样吃透过率 LUT 和阴影 / 分簇。

FogComposite：`scene * fog.a + fog.rgb`（云同样）。雾切片用 `sqrt(linearDepth / MaxDistance)`。

### 11.3 后处理链

场景色优先级：`AntiAliasingBuffer` → `SuperResolutionBuffer` → `FoggedSceneColor`。

**曝光**

- 手动：用缓存的 EV，`PP_ExposureMultiplier = EvToMultiplier(evCompensation)`。
- 自动：256 bin，对数范围 [-10, 4]，10–90 百分位（Volume 可改），`1 - exp(-adaptSpeed * dt)` 适应，目标中灰 0.18。EV 存在 HistoryCache。

**Bloom**：仅 `intensity > 0`。半分辨率最多 6 级。第一级在**已乘曝光的场景线性**上做阈值，Karis 13 tap。

**CombineLUT**：Phase 0 就建。键是调色 + 胶片 + 输出模式。命中零 Dispatch。32³ R16G16B16A16。LUT **只写 Rec.709 线性**，不含 sRGB/PQ/HLG。FinalCombine 在曝光+Bloom 之后 `LinToLog` 采样 3D LUT。默认 Profile 的胶片曲线（slope 0.88 / toe 0.55 / shoulder 0.26 / white clip 0.04）被写成“压高光但保住中灰”，不是 Identity 开关。

然后 Vignette、FilmGrain，写出线性 `PostProcessBuffer`。

**DebugView** 在 FinalCombine 之后、Gizmo 之前，用线性量覆盖 `PostProcessBuffer`。它不是第二套编码器。`TAAConfidenceBuffer` 只在 DebugView 非 None 或捕获明确要求时创建。捕获不得改 DebugView，也不得改历史。

**OutputTransform 是唯一传输编码所有者。** 政策：0 shader sRGB，1 硬件 sRGB 直通线性，2 PQ，3 HLG，4 scRGB。Present 只 blit `DisplayColorBuffer`，硬件 sRGB 时不再编第二次。格式从**活的呈现目标**解析，不猜缓存、不猜 HDR。

---

## 12. 表面着色器与 Shader Library

### 12.1 InfinityLit

主表面 `InfinityPipeline/InfinityLit`，LightMode 必须对齐 `InfinityPassIDs`：

`ShadowPass` / `ShadowCaster` / `DepthPass` / `GBufferPass` / `ForwardPass` / `MotionPass` / `TranslucentDepthPass` / `T0` / `T1` / `T2` / `Meta`

另有一套 `InfinityLit-Instanced`，顶点通过 `GPUScene.hlsl` 用 `instanceIndexBuffer[InstanceId + offset]` 取 **transform 下标**，再查 `transformBuffer` / `previousTransformBuffer` / `renderingLayerBuffer`。

BRDF 属性是经典微表面：BaseColor、Roughness、Reflectance、Specular、Normal、Emission。SSS 走 `_Subsurface` + profile 下标 + 厚度。`ShadingModel.hlsl` 的其它模型是库存货。

`InfinityDecal` 单 Pass 投单位立方体，写三张 DBuffer。

`TerrainLit` 有 GBuffer/Forward 和一套 splat include。它的 tag 写成 `"RenderPipeline" = "InfinityPipeline"`，而 InfinityLit 用 `"InfinityRenderPipeline"`——两套字符串同时存在于代码里。

MotionPass 在 `unity_MotionVectorsParams.y == 0` 时 discard，靠 CameraMotion（stencil != 5）补洞。移动物体可能留覆盖缝，这是着色器里看得见的限制。

### 12.2 库分层

```
ShaderVariables  (抖动 / 非抖动 / FlipY 矩阵族, UnityPerDraw)
    → Common
        → BSDF / ShadingModel / Lighting / GBufferPack
        → DBuffer / TranslucentCommon / GPUScene
        → IBL / Lightmap / Atmosphere / ShadowSampling
        → RayTracing (SSRT, Variance, 硬件 RT 公共头)
```

`DefultLit`（拼写保持源码）= Lambert 漫 + GGX 高光，这是 InfinityLit 实际用的模型。

---

## 13. 组件、渲染层、Volume

### 13.1 世界注册

`RenderContext` 是世界登记处：相机、灯、地形、静态/动态网格、贴花计数，外加一盏 `LightContext` 和唯一的 `MeshScene`。组件不在 `OnEnable` 里直接改表，而是丢进 `FGraphics` 任务，由 `InvokeProxyUpdate` 统一消化。

| 组件 | 作用 |
|---|---|
| `MeshComponent` | 完整 MeshScene 代理 |
| `LightComponent` | 灯光扩展字段 + 世界灯表 |
| `CameraComponent` | Volume mask/trigger + 可选 profiler |
| `DecalComponent` | 单位立方 + 计数 |
| `TerrainComponent` | 扇区 LOD |
| `RayTraceEnvironment` | `enableRayTrace` 时建 AS |
| `Foliage` / `Probe` / Water / Ocean | 桩 |

### 13.2 渲染层是 8 bit 位掩码

`ERenderingLayer : byte` + `[Flags]`，`Everything = 0xFF`。`RenderingLayerUtility.Validate` 遇到 Unity “Everything” 的高位直接抛，禁止静默截断。网格实例和灯光各持一份 mask，着色器 `(light.lightLayer & renderingLayer) == 0` 则跳过。相机可见性仍是 Unity layer，两套不要混。

### 13.3 Volume：注册表 vs 开关

构造管线时：

```csharp
VolumeManager.instance.Initialize(asset.volumeProfile, null);
```

Player **只注册这份全局默认 Profile 里出现过的类型**。Editor 靠反射能看见更多类型，这段代码故意不依赖那种行为。缺类型时 `GetComponent` 返回 null，管线不会用硬编码默认值顶上。

默认 Profile 合同（`DefaultVolumeProfileFactory`）：

- **必要且激活、参数全 override：** `Exposure`、`FilmTonemap`、`ColorGrading`
- **必须存在、默认关掉 override：** Bloom、Vignette、FilmGrain、SSR、SSGI、SSAO、VolFog、VolCloud、ContactShadow、SSS

可选特征记录条件：`component.active && 至少一个参数 overrideState`，并且 kernel 齐。例外：ContactShadow 只看 kernel；DBuffer 看世界贴花计数；大气 / Deferred / TAA 不是 Volume 特征。

`RayTracingAmbientOcclusion` Volume 存在，**不在默认 Profile 工厂的名单里**，也没有 RG 生产者。

Asset `CreatePipeline` 三道硬门槛：必须有 `AtmosphericalProfile`，必须有 `volumeProfile`，必须通过 `HasRequiredDefaultComponents`。Editor 分配 compute 时，**已序列化的引用也要过 kernel 名检查**——注释写明“引用存在 ≠ 编译成功”。

---

## 14. 资源寿命、历史事务、容错

### 14.1 HistoryCache

每个语义 ID 双槽：committed / pending。

- 读历史：`GetTexture` / `GetBuffer` → 导入进本帧图
- 写历史：`GetWriteTexture` + 帧末 Transfer `CopyHistory*` + `MarkProduced`
- `CommitFrame`：生产过的才交换
- `RollbackPending`：丢掉未提交写入
- 描述符 `Equals` 失败才重分配；重分配会让下一次 `Get` 报 `created=true`，从而强制时间滤波重置

失败的相机/帧会 `requiresHistoryReset = true`，并把 SSR/SSGI/GTAO/TAA 的 `*ValidFrames` 清零。

### 14.2 灯光与 Mesh 缓冲同样“先退休后释放”

`LightContext.EnsureCapacity` 可能换新 `GraphicsBuffer`，旧的进 `m_RetiredBuffers`，Submit 后 `FlushRetiredBuffers`。导入的 GraphicsBuffer 保持原生所有权，不会被默默克隆成 ComputeBuffer。

Mesh GPU payload 和 CPU 租用的 index buffer 同一纪律。

### 14.3 深度清除的两个数

`ClearRenderTarget` / `AttachmentDescriptor.clearDepth` 在 Unity 里被归一化：**1.0 永远是远平面**，reversed-Z 由后端翻转。清深度必须用 `GraphicsUtility.ClearDepthFar = 1.0`。

着色器里和采样深度比远平面，必须用 `SampledFarDepth`（reversed-Z 为 0，否则 1）。把其中一个喂给另一个，会把深度清到近平面，所有光栅 Pass 的几何会静默消失——Console 仍可能是 0 error。

### 14.4 验证基础设施是一等公民

不是调试残留，而是和管线同帧挂钩：

- **`RenderCaptureService`**：`-infinityCaptureRequest` 或 API。在 Lighting 所有权被场景色阶段拿走之前抓 Lighting；帧末抓 Display / Depth / GBuffer / SSR…。RG Transfer 拷到 staging，异步读回 `.bin` + `capture.json`。不改 DebugView，不改历史。
- **`RenderFaultValidation`**：`-infinityFaultRequest`。五类注入：第一相机记录失败、第二相机记录失败、图形队列失败、异步队列失败、Submit 后失败。验证历史 generation、reset、退休资源是否还活着。
- **`PostEffectValidation`**：分阶段套 Bloom / Vignette / Grain，每阶段一次捕获。

Editor 菜单 `Infinity/Validation/...` 覆盖夹具场景、Burst 重编译、材质路由迁移、灯光 schema 退役、程序集身份迁移、macOS Player、Metal 显示探针导入。这些工具的存在说明这条管线把“场景字节和程序集身份”当成可证伪的合同，而不是靠打开场景碰运气。

---

## 15. 测试覆盖了什么

`InternalsVisibleTo` 只给 Tests 和 Editor。测试是 EditMode，断言的是合同而不是“看起来像”。

| 主题 | 代表文件 | 钉住的东西 |
|---|---|---|
| Mesh 事务 | `MeshSceneTests` | 1:1 变换所有权、回滚、排序饱和、可见性 intern、payload 退休 |
| 渲染层 | `RenderingLayerTests` | 8 bit 往返、高位抛、阴影层走原生 mask |
| 材质路由 | `MaterialRouteTests` | 每路由只开该开的 Pass；Validate 不突变 |
| 图失败 | `RenderGraphFailureTests` | Load=读；例外保留；分配进退休队列 |
| 历史 | `HistoryCacheCommitTests` | Equals 驱动重分配；回滚保 committed |
| 灯光 | `LightRecordTests` / `LightResourceLifetimeTests` | radiance 一次打包；ZBin 门槛；buffer 退休 |
| 帧提交 | `FrameSubmissionTests` | 先记第一例外，仍尝试 Submit |
| 屏幕空间 | `ScreenSpaceModeTests` / `ScreenSpaceDenoiseTests` / `ScreenSpaceNumericContractTests` | 模式解析、AO 只应用一次、kernel 有限写入 |
| TAA / 相机状态 | `TemporalAntiAliasingTests` / `CameraFrameStateTests` | 丢帧重置、抖动为零、SceneView 120 / Game 8 |
| 大气 | `AtmosphereParameterTests` / `AtmosphereCacheTests` | null 抛、范围、键量化、共享/每相机事务 |
| 输出 | `DefaultVolumeOutputTests` / `ExposureOutputTests` / `ColorOutputGpuTests` | 默认 Profile、EV、GPU 上 grain/bloom |
| GBuffer | `GBufferContractTests` | 光栅↔compute 往返 |
| 捕获 | `RenderCaptureTests` | 会话取消、超时、证据不可变 |
| 雾 / 半透明 ID | `FogTranslucentFeatureTests` | Opaque vs Fogged 语义不混 |

没有测到、代码也标明未接线的：硬件 RT 出图、地形真正画出、水/植被、Forward 动态阴影、Metal HDR 实机编码、移动相机的时间滤波画质。

---

## 16. 明确未接线或未完成的部分

这些不是“文档说以后做”，而是代码结构自己说的。

### 16.1 硬件光线追踪

存在的东西：

- `RayGen_RayTracingAmbientOcclusion.raytrace`（完整 cosine hemisphere）
- `RayGen_RayTracingIndirectDiffuse.raytrace`（只写调试渐变）
- `RayTracingAmbientOcclusionGenerator`（能 bind/dispatch）
- `RayTracingIndirectDiffuseGenerator`（空构造）
- `RayTraceEnvironment` 可建 AS
- `IRaytracingCommands` 和 `AddRayTracingPass` 图基础设施
- InfinityLit 里被整段注释掉的 RTAO Pass
- Asset 上的 `enableRayTrace`

不存在的东西：`Runtime/RenderPipeline/Pass/` 下没有任何类型引用上述 Generator。GTAO / SSR / SSGI 全部是屏幕空间 compute。

### 16.2 几何旁路

地形 LOD 算了，画出函数没人调。水、海、植被、探针是空壳。贴花计数能打开 DBuffer Pass，但没有业务 DBuffer 材质时就是空记录。

### 16.3 VFX

`Render()` 调用 `VFXManager.ProcessCameraCommand`，asmdef 引用 VFX Runtime。没有自定义 VFX 绑定器、没有 GraphSRP 模板接到 MeshScene 或 RG DrawList。标准 `ParticleSystem` 若要出画，只能走半透明 RendererList 那条 Unity 路径（T2 的设计意图），不是 Mesh 后端。

### 16.4 着色模型库存

Skin / ClearCoat / Fabric / Hair 停在 `ShadingModel.hlsl`。运行时 GBuffer 只区分 DefaultLit 与 Subsurface。

### 16.5 编辑器图视窗

`RenderGraphWindow` 是假节点。实况诊断靠文本 `DescribeCompiledGraph` 和捕获目录。

---

## 17. 代码里看得见的契约裂缝

按“会在画面或寿命上出问题”而不是“风格不喜欢”排列。

1. **太阳下标分裂**：`SunRecordIndex` 跟 `RenderSettings.sun`，接触阴影跟 buffer[0]。多盏方向光时接触阴影可能打在错的光上。
2. **Cascade 比例双源**：Unity 矩阵用 `settings.cascadeRatios`，分割距离用另一份写死数组。
3. **裁剪距离双源**：culling 128 vs Asset `shadowDistance`。
4. **Forward 不采动态阴影**，延迟采。路由到 Forward 的物体和延迟物体阴影语言不同。
5. **Forward 只用 tile，延迟用 tile ∩ z-bin**。深度不连续处局部灯集合不同。
6. **`EShadowType` 不驱动采样核**，永远 3×3 PCF。
7. **`g_SunRecordIndex` 未上传**。
8. **HiZ / FlipY 重建约定**：compute 固定 FlipY；若某条射线仍按 `renderIntoTexture:true` 解 Y，命中会漂。
9. **SSR 层级追踪不完整**（mip 0 为主）。
10. **TAAJitter 仍上传、活跃核不用**；超分收 jitter 却双线性采当前帧。
11. **Preview 相机时间门控缺失**。
12. **MotionPass 依赖 stencil 补洞**，运动物体可能缺覆盖。
13. **地形 / 水 / 植被 / 硬件 RT / 自定义 VFX** 未闭合成帧图消费者。
14. **`RGBuilder.Execute` 的 `ResourcePool` 参数当前未被图使用**——图瞬态走工厂内部 cache，Mesh/Light 的长寿命池是另一套。两套池并存，读代码时不要当成同一个对象。

这些裂缝的共同点是：**两处权威各写各的**。这条管线在其它地方花了大量测试去消灭双权威（radiance 打包、History Equals、Volume 注册表、OutputTransform）。上表是还没收敛完的同类问题。

---

## 18. 一帧数据流总图

把前面拆开的块再收成一张“谁写、谁读”：

```
MeshComponent ──事务──► MeshScene ──脏范围──► MeshSceneResidency (GPU 变换/包围盒)
Unity Light + LightComponent ──Build──► LightContext ──Transfer──► GPU 灯/阴影切片
AtmosphericalProfile ──FromProfile──► Shared/View/IBL Cache ──Compute──► LUT / Cubemap / SH
VolumeStack ──override 检测──► FrameFeatureSet ──ShouldRecord──► 各 Pass 是否进图

相机裁剪 ──► MeshVisibilityShare.Acquire ──► 多 Pass 共享的 instanceVisibility
DeclareDrawList ──编译剔除──► Schedule Filter/Sort/Build ──► CPU Draw 或 GPU Indirect

Depth/GBuffer/Motion ──► HiZ / HalfRes / ZBin
CSM + LocalShadow + Contact + GTAO
Deferred (必要) + Forward (补充) + SSS + AtmosphereComposite
        │
        ▼
LightingBuffer ──capture──► (验证)     ──pyramid──► SSR/SSGI ──► OpaqueSceneColor
        │
        ▼
VolCloud / VolFog ──FogComposite──► FoggedSceneColor ── T0 / Pyramid / T1 / T2
        │
        ▼
TAA 或 SuperResolution ──HistoryCache Copy──► 下一帧
        │
        ▼
Exposure × Bloom × CombineLUT × Vignette × Grain ──► PostProcessBuffer
        │
        ├─ DebugView（线性覆盖）
        ├─ Gizmo / WireOverlay（仍线性）
        ▼
OutputTransform ──► DisplayColorBuffer ──► Present(backbuffer)
        │
        ▼
Submit ──► Commit History ──► FlushRetired (图 / 灯 / Mesh payload / 大气)
```

---

## 19. 结论：这是一条什么样的管线

InfinityRP 0.3.0 是一条**已经把“所有权”当成产品本身**的延迟管线。

它在这些地方表现出成熟引擎的纪律：

- 场景实例有事务和代次，而不是每帧从 `MeshRenderer` 现场拼。
- 绘制列表按图画寿命租还，GPU 间接参数不搞进程单例。
- 历史帧是可提交 / 可回滚的事务，失败相机不会污染下一帧 TAA。
- 必要生产者在记录期失败，而不是执行期失踪。
- 大气、默认 Volume、最终编码、语义缓冲，各自只有一个写入权威。
- EditMode 测试盯的是这些合同，而不是截图像不像。

它在这些地方仍然是研究 / 产品化中段：

- 地形画出、水体、植被、硬件 RT、自定义 VFX 没有闭包。
- 前向路径的阴影和分簇完整度低于延迟。
- 屏幕空间时间滤波、体积、半透明的**画质**在测试里没有被图像门收口（测试锁的是数值有限、模式开关、缓冲 ID）。
- 若干双源常量（阴影距离、cascade 分割、太阳下标）还活在代码里。

如果只用一个比喻：这是一条把邮局、账本和仓库都自建了的渲染管线。信件（Draw）不再由每个 Renderer 自己扔进引擎邮箱；账本（History / MeshScene / Volume 注册表）按事务过账；仓库（GPU 资源）必须等货车（Submit）离开才能拆货架。效果列表（SSR、体积云、SSS）是货架上的商品——商品可以缺货，账本不能做假。

---

## 附录 A. 关键类型速查

| 类型 | 文件 | 一句话 |
|---|---|---|
| `InfinityRenderPipeline` | `Runtime/RenderPipeline/InfinityRenderPipeline.cs` | 帧编排与 Phase 记录 |
| `InfinityRenderPipelineAsset` | 同目录 | 创建门槛、kernel 引用、阴影/SSS/输出 |
| `RGBuilder` | `Runtime/RendererCore/RenderGraph/RGBuilder.cs` | 记录 / 编译 / 执行 / 失败再抛 |
| `RGScoper` | `RGScoper.cs` | 语义 ID → 本帧句柄 |
| `RGDrawListContext` | `RGDrawList.cs` | DrawList 代次与 Schedule/Resolve/Release |
| `MeshScene` / `MeshSceneUpdate` | `PrimitivePipeline/MeshPipeline/` | SoA + 事务 |
| `MeshSceneResidency` | 同目录 | GPU 变换/包围盒上传 |
| `MeshVisibilityShare` | `MeshVisibilityCache.cs` | 可见性 intern |
| `MeshDrawPipeline` | `MeshDrawPipeline.cs` | Schedule / Resolve / Submit 门面 |
| `MeshDrawGPUBackend` | `MeshDrawGPUBackend.cs` | 间接参数与 Auto 回退 |
| `LightContext` | `LightPipeline/LightContext.cs` | 打包、上传、退休 |
| `ShadowAllocator` | `ShadowAllocator.cs` | CSM / 局部切片 |
| `HistoryCache` | `GPUResource/HistoryCache.cs` | 跨帧双槽 |
| `AtmosphereSharedCache` | `Utility/AtmosphereCache.cs` | 共享 LUT / IBL |
| `CameraFrameState` | `Context/CameraFrameState.cs` | 每相机历史、Volume、特征、reset |
| `FrameFeatureSet` | `Context/FrameFeatureSet.cs` | requested / supported / produced |
| `MaterialRouteUtility` | `Utility/MaterialRouteUtility.cs` | 表面路由唯一入口 |
| `DefaultVolumeProfileFactory` | 同目录 Utility | Player 类型注册表合同 |
| `RenderCaptureService` | `Validation/` | 多缓冲确定性捕获 |

## 附录 B. 必要 vs 可选特征

**每帧必须生产（`EnsureRequiredProducers`）：** Depth、GBuffer、DeferredShading、Display；未开超分时还有 TAA。

**默认请求、kernel 在才支持：** Motion、HiZ、ColorPyramid、PostProcess、OpaqueLightingPyramid。

**Volume override + kernel：** GTAO、SSR、SSGI、VolumetricFog、VolumetricCloud。

**特殊门槛：** DBuffer ← 世界贴花计数；ContactShadow ← 仅 kernel；SuperResolution ← Asset 开关，与 TAA 互斥。

## 附录 C. 本报告未覆盖的东西

- 具体美术调参是否“好看”（需要实机帧，不在本次代码阅读范围内）。
- Unity 工程侧的场景资产、RP Asset 二进制内容。
- 未编译进本包的工程本地 `Library/` / `PackageCache` 补丁。

以上全部以本仓库源码为准。
