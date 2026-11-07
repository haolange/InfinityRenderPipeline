# Mesh Drawing Pipeline 设计文档（v2 提案）

状态：提案，未实现。基于对当前 `Runtime/RendererCore/PrimitivePipeline/MeshPipeline/` 的源码审查和三轮设计讨论整理。实现后，本文件的"目标"部分应并入 `ARCHITECTURE.md` §7，规范变更并入 `AGENTS.md`，本文件删除。

---

## 1. 目标与差距

| 目标 | 现状 | 本提案要解决的 |
|---|---|---|
| 接口像 SRP `DrawRenderers` 一样简洁 | 三步 API 成立，但 Pass 是硬编码 enum + `PassRegistry` 表 | 改为参数申请式 `MeshPassDesc`，Pass 身份由 interning 产生 |
| Unreal 级性能（静态 / 动态 / 缓存） | 静态场景极好（10k 下 Proxy 0.10 ms）；动态很差（10k 全动 21.5 ms，50k 105 ms） | 动态 transform 路径整体 Burst 化；MeshDrawCommand 增量缓存；每帧只对可见集排序 |
| 面向数据：SoA、拆依赖、易并行 | 表结构是 DOD 的，执行是串行的（`.Complete()` / `.Run()` / managed 重建） | 所有每帧路径变成 JobHandle 链；热路径零 managed 集合 |
| 避免 UE 的 OOP 包袱 | 已无 Proxy / PassProcessor 虚继承 | 保持；Relevance 以 `EMeshPassMask` 数据形式存在 |
| 清晰的分类 / 分层 / 命名 | 一个概念 5～6 个名字；RG ↔ Mesh 双向依赖；死代码累积 | 按作用域分层，统一命名与索引空间约定 |
| CPU / GPU 双后端 | 两条路径都通，但无共同抽象，输出语义不一致 | 统一输出契约 `MeshInstancedDraw[]` + `instanceToEntity[]` |

性能数字来源：`Docs/History/MainThread-20261025/Report.md` 第 51–62 行。

---

## 2. 四条硬边界

这四条是整个设计的不变量。任何实现细节都可以变，这四条不能变。

1. **PassDesc 决定"用什么状态画"，View 决定"画谁"。** 凡随视图变化的量（layer mask、culling mask、LOD 距离、shadow slice）不进 PassDesc，只进 View 或 Visibility。这是 MeshDrawCommand 可缓存的充分条件。
2. **MeshDrawCommand 是 `f(Element, PassDesc)` 的纯函数，且不含任何 per-entity 数据。** 实例数据全部通过 `entityIndex` 间接寻址。因此不同 Entity 天然共享同一个 Command，缓存规模是"不同渲染状态的数量"。
3. **只有一条生成路径。** "动态"不是第二条代码路径，而是同一个函数的三个参数：transform 每帧写、element 是 transient 的、不做 merge。
4. **filter、grouping、sort 是三个操作。** Merge 判断 `commandIndex` 相等，不判断 sort key 的位段相等。Sort key 饱和只影响排序质量，不会合错。

---

## 3. 分层模型

按**作用域和生命周期**分层。看名字就能知道一个类型属于哪一层、什么时候写、什么时候失效。

```text
┌─ World（每场景一份；对象存活期间；结构事件 / transform 写入）──────────────────┐
│  MeshEntity[e]      transform, bounds, flags, layer, customData    ← 脏页 → GPU │
│  MeshElement[x]     entity, section, material, passMask, lod       ← 脏页 → GPU │
│  MeshSection / MeshMaterial / MeshShaderState   引用计数的共享资源（dense rank）  │
└────────────────────────────────────────────────────────────────────────────────┘
            │  f(Element, PassDesc) → Command      纯函数，增量
            ▼
┌─ Pass（World × PassDesc；PassDesc 被申请期间；结构事件时增量写）──────────────────┐
│  MeshPassCache[passHandle]                                                      │
│    elementToCommand[x] : int   （-1 = 不参与）                   ← 脏页 → GPU   │
│    MeshDrawCommand[c]  : (shaderState, material, section) + args  ← 脏页 → GPU   │
│    MeshDrawCommandBinding[c] : Mesh / Material / passIndex（managed，同 index）    │
│    refCount[c] = 成员数 = 该 command 的 instance 容量上限                          │
└────────────────────────────────────────────────────────────────────────────────┘
            │  每帧、每视图，Burst，O(visible)
            ▼
┌─ View（每视图；一帧）──────────────────────────────────────────────────────────┐
│  MeshView           frustum planes, viewKey, kind, masks, 视点                   │
│  MeshViewVisibility visible[entityIndex]，lod[entityIndex]，depth[entityIndex]   │
└────────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─ DrawList（View × Pass；一帧，submit 后回收）──────────────────────────────────┐
│  Gather : visible ∩ elementToCommand ≠ -1        → VisibleMeshDrawCommand[]     │
│  Sort   : 按 commandIndex 聚拢，再按 EMeshSortMode → 两级排序                      │
│  Merge  : 相邻同 commandIndex                     → MeshInstancedDraw[]          │
│  Emit   : instanceToEntity[]（draw 内按 depth）    → Submit / IndirectArgs        │
└────────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─ Backend（CPU direct / GPU indirect：同一契约的两种实现）───────────────────────┐
└────────────────────────────────────────────────────────────────────────────────┘
┌─ Graph（RGDrawListHandle 记录 / 编译 / 执行；RenderGraph 不依赖 Mesh 类型）─────┘
```

和现状的对应关系：

| 现在 | 新层 | 变化 |
|---|---|---|
| `MeshScene` + `GpuScene` + `MeshGpuSceneSnapshot` + `MeshWorld` | World | 四合一。`MeshWorld` 是唯一根，`RenderContext` 不再单独创建 scene |
| `PassBinStore`（全量重建 + 排序 span） | Pass | **核心重写**：增量 map + refCount，不再存排序好的成员列表 |
| `MeshCandidateTableStore` | Pass | 删除，并入 `elementToCommand` 的脏页上传 |
| `MeshView` + `MeshVisibilityShare` + `MeshGpuVisibilityCache` | View | 合并到一处；CPU/GPU visibility 使用同一个 key |
| `MeshDrawExtract` / `PassView` / `MeshDrawGpuPayload` / `RGDrawListRecord` | DrawList | 收敛为 `MeshDrawList`，四步流水线固定 |
| `MeshDrawPipeline` + static `MeshDrawGPUBackend` | Backend | `IMeshDrawBackend` 两个实例，归 `MeshWorld` 所有，无 static 状态 |
| `RenderGraph/RGDrawList.cs`、`RGMeshCompile.cs`、`RGMeshOcclusion.cs` | Graph | 搬到 `MeshPipeline/Graph/`；RenderGraph 只保留 `IRGCompileExtension` |

---

## 4. World 层数据布局

### 4.1 表

全部是固定 index 的 SparseArray（`NativeArray` + 并行 `generation[]` + 空闲链表 + 64-slot 脏页位图）。五张表用一个泛型 `SlotTable<T>` 实现，替代现在五份复制的 Alloc / Free / Restore / Write。

| 表 | 索引空间 | 列（SoA） | GPU 镜像 |
|---|---|---|---|
| `MeshEntity` | `entityIndex` | `float4x4 transform`、`FBound bounds`、`uint flags`、`uint layer`、`uint renderingLayer`、`uint2 sceneCullingMask`、`customData[N]` | 是，按列上传 |
| `MeshElement` | `elementIndex` | `entityIndex`、`sectionId`、`materialId`、`EMeshPassMask passMask`、`byte lodIndex`、`int renderQueue` | 是 |
| `MeshSection` | `sectionIndex` | `meshUnityId`、`subMesh`、`indexCount/startIndex/baseVertex`、`refCount`、`revision` | 否 |
| `MeshMaterial` | `materialIndex` | `materialUnityId`、`renderQueue`、`shaderStateId`、`refCount`、`revision` | 否 |
| `MeshShaderState` | `shaderStateIndex` | `shaderUnityId`、`passIndex`、`keywordHash`、`refCount`（不含 render state，见 D1） | 否 |
| `MeshLodChain` | `lodChainIndex` | `lodAssetUnityId`、`screenSize[]`、每级 LOD 的 Element 范围、`refCount` | 否 |

**决策（已确认）**：Entity 与 Transform 严格 1:1，`transformIndex == entityIndex`。删除 `TransformId`、`TransformRecord` 和 owner 映射。GPU 侧 `EntityTransforms[entityIndex]` 直接寻址。将来如需层级或共享 transform，重新引入独立表。

Section / Material / ShaderState 查找用 `NativeParallelHashMap<ulong, int>`，消除现在 `AllocOrUpdateSection/Material` 的线性扫描（`MeshScene.cs:751`、`:844`，O(N²) 加载）。

### 4.2 写入分两类

| 类别 | 例子 | 机制 |
|---|---|---|
| **结构性编辑** | 创建 / 销毁 Entity、增删 Element、换材质、换 mesh | `MeshWorldUpdate` 事务：undo log，`Dispose` 不 `Commit` 则回滚。refCount 归零的共享资源入队，`Commit` 时释放 |
| **每帧覆盖写** | transform、bounds、flags、customData | **绕过 undo log**（已确认的规范变更）。Burst job 直接写 `MeshEntity` 列和脏页位图。覆盖写幂等，失败只需丢弃本帧脏页 |

动态 transform 路径目标形态：

```text
TransformAccessArray ──IJobParallelForTransform──► 写 MeshEntity.transform / bounds / dirtyPage
                                                      （不回主线程，不逐 MonoBehaviour 处理）
主线程只处理：新增 / 删除 / Mobility 切换
```

现在的路径（`RenderContext.MeshTransforms.cs:75-108`）是 Burst 读完后回主线程逐个 `MarkTransformDirty` → `s_TransformUpdates` → 事务内逐个 `SetTransform`（两次胖 undo push）→ `AcceptTransform`。这是动态场景的主要瓶颈。

### 4.3 GPU 镜像 `MeshWorldGpu`

- 每列一个 `GraphicsBuffer`，64-slot 脏页 run 上传；静态场景上传 0 字节（保持现状）。
- 前一帧 transform 继续用 `MeshTransformVersionCache` 的不可变版本机制（motion vector 需要）。
- **Pass 层的 `elementToCommand` 与 `MeshDrawCommand` 表也走同一套脏页上传**，不再有单独的 candidate 表全量 `SetData`。

---

## 5. Pass 层：参数申请式 + 增量 MeshDrawCommand 缓存

### 5.1 `MeshPassDesc`

```csharp
public struct MeshPassDesc : IEquatable<MeshPassDesc>   // field-level Equals
{
    public int               lightMode;      // InfinityPassIDs.*
    public RenderQueueRange  queueRange;
    public EMeshPassMask     requireMask;    // Depth | GBuffer | Forward | Motion | Shadow | Translucent...
    public EMeshSortMode     sortMode;       // 见 §7
    public bool              mergeInstances; // 半透明 = false
    public RenderStateBlock  stateOverride;  // 可选
}
```

内置 9 个 Pass 退化为 9 个预设常量（`MeshPassPresets.GBuffer` 等）。`MeshPassId` enum、`MeshPassTypes.cs`、`PassRegistry.cs` 删除。

### 5.2 Interning

```text
CreateDrawList(view, desc)
   → MeshWorld.Passes.Intern(desc)        规范化 + Equals 查表 → MeshPassHandle
   → 首次：对全部 Element 跑一次 Burst 并行 build（只发生一次）
   → 之后：结构事件增量维护
   → 淘汰：内置预设常驻；自定义 desc 进 LRU，只在超过容量上限时淘汰最久未用者（D3）
```

作者看到的是 `DrawRenderers` 的写法，引擎拿到的是 UE 的缓存。

### 5.3 增量维护

| 事件 | 操作 | 复杂度 |
|---|---|---|
| 新增 Element（K 个） | 对每个活跃 Pass：算 `passMask & requireMask`、queue 范围；命中则 hash 查 / 建 Command，写 `elementToCommand[x]`，`refCount++` | O(K × Passes) |
| 换材质 / 换 mesh | 旧 Command `refCount--`，新 Command `refCount++`，改写一个 slot | O(1) |
| 移动 | 不触碰 Pass 层 | 0 |
| 删除 Element | 写回 -1，`refCount--`；归零的 Command 延迟释放（同 Section 现有做法） | O(1) |

现在的 `PassBinStore.Rebuild`（`PassBinStore.cs:181-233`）在任何结构变化时让 9 个 Pass 全部变脏并全量重建（managed `List.Sort(lambda)` + `Dictionary`），这是流式加载的 O(scene) 开销。

### 5.4 `MeshDrawCommand` 拆成两张同 index 的表

Unity 的 API 层无法缓存 PSO 和 binding（每次 draw 都要传 `Mesh` / `Material` / `passIndex` / MPB），所以 Unity 下缓存 MDC 的实际收益是**消除每帧的 `EntityIdToObject`、`FindPass` 和 MPB 重建**。

```csharp
// Burst 可见：排序、合并、GPU 上传只用这张
struct MeshDrawCommand          // GPU-RECORD
{
    int  shaderStateIndex, materialIndex, sectionIndex;   // dense rank，用于 sort key
    uint indexCount, startIndex, baseVertex;              // indirect args 元数据
    uint flags;
}
// 仅主线程 Submit 使用，同 index，Command 创建时填一次
struct MeshDrawCommandBinding { Mesh mesh; int subMesh; Material material; int passIndex; }
```

Submit 时每个 draw 只做 `binding[c]` 数组索引；MPB 每个 DrawList 设置一次，每个 draw 只改 `instanceOffset`。`Material` 被销毁时在结构事件里检测（`binding.material == null`），不在每帧。

---

## 6. View 层

`MeshView` 只描述"画谁"：frustum planes、`viewKey`、`EMeshViewKind`、`layerMask`、`sceneCullingMask`、`renderingLayerMask`、`excludeFlags`、视点（用于 depth 与 LOD）。

`MeshViewVisibility` 每帧产出三列，索引空间都是 `entityIndex`：

| 列 | 产生者 | 消费者 |
|---|---|---|
| `visible[e]` | Burst frustum job（CPU）或 `CullFrustum` kernel（GPU）；主视图可叠加两阶段 Hi-Z | Gather |
| `lodIndex[e]` | 按视点距离选择 | Gather（`element.lodIndex == lodIndex[entity]` 才收） |
| `depth[e]` | 视空间深度 | Sort |

同一视图的多个 DrawList（Depth、GBuffer、Motion）共享同一份 Visibility。CPU 与 GPU 使用同一个 `MeshVisibilityKey`（field-level Equals，frustum hash 只做加速）。现在 `MeshVisibilitySignature.Equals` 比较的是量化后的 64 位 hash（`MeshVisibilityCache.cs:42-47`），hash 成了身份，与 AGENTS §4 冲突。

CPU 后端现无遮挡剔除（`RefineVisibilityHiZ` 为空方法，`MeshVisibilityCache.cs:285`）。本提案不新增 CPU Hi-Z，但删除这个空方法。

---

## 7. DrawList 层：四步流水线与 SortKey

### 7.1 四步

```text
Gather  visible[entity] ∩ elementToCommand[x] ≠ -1 ∩ lod 匹配
        → VisibleMeshDrawCommand { commandIndex, sortKey, entityIndex }
Sort    ① 按 commandIndex 聚拢（radix）
        ② MeshInstancedDraw 按 EMeshSortMode 的 64-bit key（代表深度取成员 min depth）
        ③ draw 内 instance 按 depth front-to-back   ← 不破坏 merge 的免费 Early-Z
Merge   相邻同 commandIndex → MeshInstancedDraw { commandIndex, instanceOffset, instanceCount }
Emit    instanceToEntity[]  → CPU: 上传 + DrawMeshInstancedProcedural
                            → GPU: 稳定 compaction + BuildIndirectArgs
```

### 7.2 `EMeshSortMode`

| 模式 | Key 布局（高 → 低） | merge | 用途 |
|---|---|---|---|
| `State` | queue │ shaderState │ material │ section | ✅ | GBuffer、Forward |
| `FrontToBack` | queue │ depthBucket(4–6 bit) │ shaderState │ material | ✅（draw 级） | Depth prepass、Shadow |
| `StateThenDepth` | queue │ shaderState │ depthBucket │ material | ✅ | PSO 切换昂贵时 |
| `BackToFront` | queue │ ~depth（全精度） | ❌（一 element 一 draw） | 半透明 |

规则：
- 位段来自 dense rank（`shaderStateIndex` 等），**不是** Unity InstanceID。
- 每段饱和后打包（AGENTS §4）。饱和只影响排序质量。
- `sortMode` 放在 `MeshPassDesc`（不随视图变），depth 来自 View。
- **后端声明自己支持的排序模式**（D2）。依赖每帧深度的 draw 级排序需要 CPU 知道可见集：
  - `BackToFront` 只由 CPU 后端实现。声明了它的 DrawList 选择 CpuDirect，这是 PassDesc 层面的显式规则，不是降级。
  - GPU 后端的 draw 顺序是 command 集合变化时确定的 `State` 顺序。`FrontToBack` / `StateThenDepth` 在 GPU 后端上以 `State` 顺序 + 两阶段 Hi-Z 实现同一意图（减少 overdraw），Rendering Debugger 显示实际排序模式。

### 7.3 后端契约

```csharp
interface IMeshDrawBackend
{
    // 输入：MeshPassCache + MeshViewVisibility；输出：固定契约
    void Prepare(in MeshDrawListRequest req, out MeshInstancedDraw[] draws, out instanceToEntity);
    void Submit(in MeshDrawList list, IRasterCommands cmd);
}
```

| | CPU direct | GPU indirect |
|---|---|---|
| Gather + Sort + Merge | Burst JobHandle 链（并行 gather、radix sort） | Compaction kernel 读 `elementToCommand` 与 `visible`；`CommandBase` 由 refCount 前缀和得到 |
| 第 ③ 级顺序 | 自然得到 | 不做。保留 `InterlockedAdd` compaction，draw 内实例顺序不确定（D2） |
| Emit | `SetBufferData` 上传 `instanceToEntity` | 直接在 GPU buffer 中 |
| Draw | `DrawMeshInstancedProcedural` 每个 `MeshInstancedDraw` 一次 | `DrawMeshInstancedIndirect` 每个一次 |

两者的 draw call 数相同（Unity 无 MDI）。GPU 后端的价值是去掉每实例 CPU 工作和上传。

GPU payload 容量按 `Σ refCount`（该 Pass 的成员数）分配，不再是现在的 `max(candidates, 全场景 InstanceHighWater)`（`MeshDrawGPUBackend.cs:434`）。

---

## 8. 边界情况：一条路径覆盖

| 场景 | 处理 | Command 缓存 |
|---|---|---|
| LOD | LOD 链来自 `MeshAsset`，World 侧为引用计数的 `MeshLodChain`；每级 LOD 的 section 各自是 Element（带 `lodIndex`）；每视图按投影尺寸对比 `screenSize` 选择，结果是 Visibility 的一列（D5） | ✅ |
| 半透明 | `mergeInstances = false`，`BackToFront`；一 element 一 draw | ✅ |
| Per-instance custom data | `MeshEntity.customData` 列，shader 用 `entityIndex` 寻址 | ✅ |
| 程序化 / 每帧几何 | 同一个 build 函数；Element 放帧作用域 transient 区段，每帧清空 | 状态 ✅，成员不缓存 |
| 多视图 / cascade | 共享 `MeshPassCache`，只有 Visibility 和 Sort 是每视图的 | ✅ |
| Mobility | 只决定是否进 `TransformAccessArray` 每帧读；不是独立代码路径 | ✅ |

Relevance 保留为**数据**：`EMeshPassMask` 在 Element 创建时由 material route 一次算好，存进 SoA；PassDesc 用 `requireMask` 筛，一条 AND。

---

## 9. 命名与索引空间

### 9.1 类型命名

原则：保留 UE 中描述"数据是什么"的词（DrawCommand、Visible…），丢掉描述"OOP 角色"的词（SceneProxy、SceneInfo、MeshBatch）。避开 Primitive（在图形 API、Filament、UE、本仓库目录中指四种粒度）。

| 层 | 类型 | 替换 |
|---|---|---|
| World | `MeshWorld`、`MeshWorldUpdate`、`MeshWorldGpu` | `MeshScene`、`MeshSceneUpdate`、`GpuScene`、`MeshGpuSceneSnapshot` |
| World | `MeshEntity`、`MeshEntityId` | `MeshInstanceRecord`、`MeshInstanceId`、`TransformId` |
| World | `MeshElement`、`MeshElementId` | `MeshDraw`、`MeshDrawId` |
| World | `MeshSection`、`MeshMaterial`、`MeshShaderState`、`MeshLodChain` | `MeshSectionRecord`、`MaterialDataRecord`、（新）、（新） |
| Pass | `MeshPassDesc`、`MeshPassHandle`、`MeshPassPresets`、`MeshPassCache` | `MeshPassId`、`MeshPassTypes`、`PassRegistry`、`PassBinStore` |
| Pass | `MeshDrawCommand`、`MeshDrawCommandBinding` | `MeshDrawCommandKey`、`MeshPassCommand` |
| Pass | `EMeshPassMask`、`EMeshSortMode` | `EPassEligibility`、`EMeshSortPolicy`/`MeshSortKey`/`MeshSortPlan` |
| View | `MeshView`、`MeshViewVisibility`、`MeshVisibilityKey` | `MeshView`、`MeshViewCullingResult`、`MeshVisibilitySignature`、`MeshGpuVisibilityKey` |
| DrawList | `MeshDrawList`、`VisibleMeshDrawCommand`、`MeshInstancedDraw` | `MeshDrawExtract`、`PassView`、`MeshDrawCommand`（旧语义） |
| Backend | `IMeshDrawBackend`、`MeshCpuBackend`、`MeshGpuBackend`、`MeshDrawPayload` | `MeshDrawPipeline`、`MeshDrawGPUBackend`、`MeshDrawGpuPayload`、`MeshDrawGpuStaging` |
| Graph | `RGDrawListHandle`（保留）、`RGDrawListContext`、`RGMeshCompile` | 位置从 `RenderGraph/` 移到 `MeshPipeline/Graph/` |

后缀约定：World 层实体名词；Pass 层 `MeshPass*` 或 `*DrawCommand`；View 层 `MeshView*`；DrawList 层 `Visible*`、`*Draw`、`*DrawList`。Enum 一律 `E` 前缀（`MeshPassId` 现在违规）。

**ID 约定**：自有类型化 ID 以 `Id` 结尾（`MeshEntityId`）；Unity 对象身份一律是 `ulong` 字段、以 `UnityId` 结尾（`meshUnityId`），不做成类型，避免与 Unity `EntityId` 碰撞。

### 9.2 索引空间约定（解决 Instance 歧义）

现在 "instance" 同时指逻辑对象、GPU instancing、cull slot 三样东西（AGENTS §5.6 专门写了一条规则区分 slot 和 transform index，就是在付这个歧义的成本）。

1. **"Instance" 只表示 GPU instancing**：一次 draw 内的序号。只出现在 `instanceCount`、`instanceOffset`、`instanceIndex`、`MeshInstancedDraw`。
2. 全系统只有 5 个索引空间，整数索引一律带空间后缀：

| 空间 | 含义 | 变量名 |
|---|---|---|
| entity | World 固定 index（= transform index） | `entityIndex` |
| element | World 固定 index | `elementIndex` |
| command | PassCache 固定 index | `commandIndex` |
| draw | DrawList 中第几个 `MeshInstancedDraw` | `drawIndex` |
| instance | 一次 draw 内的序号 | `instanceIndex` |

3. 跨空间映射数组命名为 `<From>To<To>`：`elementToCommand`、`elementToEntity`、`instanceToEntity`。

Shader 侧：

```hlsl
// 现在
transformBuffer[instanceIndexBuffer[SV_InstanceID + instanceIndexOffset]]
// 之后
uint entityIndex = InstanceToEntity[InstanceOffset + SV_InstanceID];
float4x4 world   = EntityTransforms[entityIndex];
```

### 9.3 目录

```text
Runtime/RendererCore/PrimitivePipeline/MeshPipeline/
  World/     MeshWorld, MeshWorldUpdate, MeshWorldGpu, SlotTable<T>, MeshEntity, MeshElement, MeshSection, MeshMaterial, MeshShaderState, MeshLodChain, MeshTransformVersionCache
  Pass/      MeshPassDesc, MeshPassHandle, MeshPassPresets, MeshPassCache, MeshDrawCommand, MeshDrawCommandBinding, EMeshPassMask, EMeshSortMode
  View/      MeshView, MeshViewVisibility, MeshVisibilityKey, MeshFrustumCullJob, MeshOcclusion
  DrawList/  MeshDrawList, VisibleMeshDrawCommand, MeshInstancedDraw, MeshDrawListJobs
  Backend/   IMeshDrawBackend, MeshCpuBackend, MeshGpuBackend, MeshDrawPayload
  Graph/     RGDrawListContext, RGMeshCompile, RGMeshOcclusion
Runtime/RendererCore/RenderGraph/   不 using InfinityTech.Rendering.MeshPipeline；只保留 IRGCompileExtension
```

依赖方向：`Graph → DrawList → Backend → View/Pass → World`。不拆 asmdef（D4）。先在代码上断开 RG → Mesh 引用，再由 `static_check.py` 按命名空间检查依赖方向。

---

## 10. 规范变更（AGENTS.md / ARCHITECTURE.md）

实现时需要同步修改的规范条目：

| 条目 | 现状 | 变更 |
|---|---|---|
| AGENTS §5.1 | "`MeshSceneUpdate` undoes every mutation" | 改为"结构性编辑走事务与回滚；transform / bounds / flags / customData 的覆盖写不入 undo log"（**已确认**） |
| AGENTS §5.6 | "GPU culling indexes instance slots. Compacted indices … are transform indices" | 删除；替换为 §9.2 的五个索引空间约定 |
| AGENTS §5.10 | "`CreateInstance` rejects an owned transform" | 删除（Transform 表不再存在） |
| AGENTS §5 表 | 模块表 | 按 §3 重写 |
| AGENTS §5.13–14 | `NotifyInstanceChanged` 等公开 API | 公开 API 改名（`NotifyEntityChanged`）放在**单独一批**，带序列化迁移验证 |
| AGENTS §5.14 | `NotifyMaterialRoutingChanged` 用于 queue / pass 编辑 | 扩展到运行时 keyword 编辑；未通知只影响排序分组，不影响正确性（D1） |
| AGENTS §5.13 | `MeshComponent` 字段 | 新增可选字段 `lodAsset`（`MeshAsset`），与 `meshAsset` 互斥；两者同时赋值在注册时抛出（D5） |
| AGENTS 新增 | — | `MeshPassCache` 淘汰规则：预设常驻，自定义 desc LRU，超容量才淘汰（D3）；依赖方向由 `static_check.py` 检查（D4） |
| AGENTS §13 | 缺陷清单 | 新增"半透明 `MeshComponent` 无绘制路径"（实现 Translucent DrawList 后删除）；删除"bins sort only by CompareStructural" |
| ARCHITECTURE §7 | 整节 | 按本文件 §3–§9 重写；"Out of scope: GPU LOD" 改为 "LOD 是 Visibility 的一列" |
| 命名规则 | Enum `E` 前缀 | `MeshPassId` 消失，不再违规 |

---

## 11. 对现有代码的处置

| 动作 | 对象 |
|---|---|
| **删除**（死代码 / 空实现） | `MeshSortKey`、`MeshSortPlan`、`MeshSortField`、`defaultSort`、`defaultFilter`、`EMeshSortSemantic`、`ESortDirection`、`RefineVisibilityHiZ`、`MeshVisibilityDispatch`、`GpuScene.Clear/Update(block)`、`MeshDrawGpuStaging.Build`、`MeshComponent.SetCustomPrimitiveData` 空壳（由 customData 列替代）、`OnTransformChange` 及无调用者的 `EventUpdate`、`MeshAsset.Tree`（`FMesh` 与顶层 `meshes/materials/lODInfos` 重复权威） |
| **删除**（被替代） | `MeshCandidateTableStore`、`PassRegistry`、`MeshPassTypes`、`MeshPassId` enum、`TransformId`/`TransformRecord`、`MeshGpuSceneSnapshot` |
| **重写** | `PassBinStore` → `MeshPassCache`；`MeshPassExtractJob` → DrawList 并行 jobs；`RenderContext.MeshTransforms` → 直写 World 的 job；GPU `Compact` → 稳定 compaction |
| **改名 + 搬目录** | §9.1 表 |
| **修正** | 静默 fallback 改 throw（`ResolvePassIndex < 0` continue、`UseDrawList` 返回 Invalid、`Draw` 对 Invalid no-op、强制 GpuIndirect 静默降级）；`MeshVisibilityKey` field-level Equals；拼写（`movebility`、`RenderTransfrom`、`CaculateWorldBound`、`BuiltinMeshesPasses`）；`MeshLODInfo` / `FMesh` 的 `Equals` 按引用比较数组；补 `Documentation~/mesh.md` |

---

## 12. 实施顺序与验收

按"先验证核心假设、再收敛命名"的顺序。改名放最后，因为它触及序列化字段和测试，等结构定型后返工最少。

| 批次 | 内容 | 验收 |
|---|---|---|
| **B0 正确性** | 半透明 DrawList 接入 `TranslucentPass`；删除死代码与空实现；静默 fallback 改 throw；补文档 | EditMode 通过；半透明 `MeshComponent` 在 T0/T1/T2 可见（截图） |
| **B1 World 热路径** | `SlotTable<T>`；transform = entity 合并；hash 查找；transform job 直写 + 绕过 undo；`MeshWorldGpu` 列式脏页 | 10k 全动 Proxy p50 **< 2 ms**（现 21.5 ms）；静态仍 0 字节上传；`MatrixDuplicateRatio` 指标删除 |
| **B2 Pass 缓存** | `MeshPassDesc` + interning；`MeshPassCache` 增量；`MeshDrawCommand` 双表；`elementToCommand` 脏页上传；删 `MeshCandidateTableStore` | spawn 1 物体 O(K)，不再全量重建；Submit 每 draw 零 `EntityIdToObject` |
| **B3 DrawList** | 四步并行 jobs；`EMeshSortMode` 四种 key；GPU 稳定 compaction；`IMeshDrawBackend` 实例化 | CPU / GPU 的 draw 序列一致，且每个 draw 的实例**集合**一致（GPU draw 内顺序不比较，D2）；CPU 后端 Depth prepass 有 front-to-back |
| **B4 收敛** | 改名、搬目录、断 RG ↔ Mesh 循环、AGENTS / ARCHITECTURE 同步 | `static_check.py` 依赖方向检查通过；公开 API 改名单独批次 + 迁移验证 |

每批次结束按 AGENTS §1 清理 intermediate 数据，截图验证记入 `Docs/FeatureStatus.md`。

---

## 13. 决策汇总

| # | 决策 | 结论 |
|---|---|---|
| — | Transform 表 | 与 Entity 1:1 合并，删除 `TransformId` |
| — | 逻辑对象名 | `MeshEntity`（配合 §9.1 的 ID 约定） |
| — | GPU 镜像名 | `MeshWorldGpu` |
| — | 动态写入与 undo | 覆盖写绕过 undo log；AGENTS §5.1 相应修改 |
| — | Pass 定义 | 参数申请式 `MeshPassDesc` + interning，删除 enum / Registry |
| — | 半透明 | 接入 DrawList（不是注册时拒绝） |
| D1 | `MeshShaderState` 的键与时机 | 键 = `(shaderUnityId, passIndex, keywordHash)`，不含 render state；只在结构事件时捕获，复用 `MaterialRouteUtility` 的 per-Material 缓存与 version |
| D2 | GPU compaction | 保留 `InterlockedAdd`；GPU 后端不做 draw 内排序；依赖深度的 draw 级排序是 CPU 能力 |
| D3 | Pass 淘汰 | 预设常驻；自定义 desc 进 LRU，按容量上限淘汰，不按帧数 |
| D4 | asmdef 拆分 | 不拆；先在代码上断开 RG → Mesh，再用 `static_check.py` 强制依赖方向 |
| D5 | LOD 作者入口 | 保留并清理 `MeshAsset` 作为 LOD 链资产；`MeshComponent` 新增互斥字段 `lodAsset` |

## 14. 决策记录

每条按"问题本质 → 约束 → 结论 → 代价与回退"记录。

### D1 `MeshShaderState` 的键与捕获时机

- **本质**：`MeshShaderState` 只服务排序分组，用来近似 PSO 以减少 SetPass。它**不参与正确性**：merge 判断 `commandIndex`，绘制时 Unity 依据 `Material` 自己选 variant。
- **约束**：
  - Unity 运行时据我所知没有公开 API 读取某个 pass 的 render state（ZWrite、Cull、Blend 由 shader 中的属性引用决定），无法通用地算 render state hash。
  - 读 `material.enabledKeywords` 会分配，只能放在结构事件里。
  - `MaterialRouteUtility` 已有 per-Material 的 `ConditionalWeakTable` 缓存和 `version`，`ApplyPassState` / `NotifyRoutingChanged` 会递增 version（`MaterialRouteUtility.cs:23-34`）。
- **结论**：
  - 键 = `(shaderUnityId, passIndex, keywordHash)`。render state 差异落到下一级 `material` 分组里，排序仍然把它们聚在一起。
  - 捕获时机：Material 记录创建时，以及 drain 时发现 route `version` 变化时。keyword hash 作为 `MeshRoute` 的一个字段一起缓存。
- **代价**：运行时直接 `EnableKeyword` 而不调 `NotifyMaterialRoutingChanged`，ShaderState 会过期。后果只是排序分组变差，画面不受影响。AGENTS §5.14 的通知规则扩展到 keyword 编辑。
- **回退**：将来如果 Unity 提供 variant / PSO 标识 API，替换 `keywordHash` 即可，键结构不变。

### D2 GPU compaction：不需要稳定 compaction

- **本质**：原问题"prefix scan 还是 per-draw sort"的前提有误。稳定 compaction 给出的是 **element 顺序**，不是深度顺序，它买不到 draw 内 front-to-back。
- **约束**：
  - 固定 index 的 `elementToCommand` 是散布的。要得到 command 内的稳定序号，就必须维护按 command 排好的成员列表（CSR），而这正是 §5.3 为了增量更新而删掉的东西。
  - 确定性只影响同一 command 内**共面**实例的 z-fight 顺序。不同 command 之间的顺序由 CPU 确定，本来就稳定。
  - GPU 后端的价值是去掉每实例 CPU 工作，overdraw 由两阶段 Hi-Z 处理，效果强于排序。
- **结论**：
  - 保留现有 `InterlockedAdd` compaction：一次 dispatch，每 element O(1)。
  - GPU 后端不做 draw 内排序。draw 级顺序是 command 集合变化时确定的 `State` 顺序。
  - `BackToFront` 只由 CPU 后端实现，声明它的 DrawList 选 CpuDirect（显式规则）。`FrontToBack` / `StateThenDepth` 在 GPU 上以 `State` + Hi-Z 实现同一意图，Debugger 显示实际模式。
  - B3 的 CPU / GPU 一致性验收比较 draw 序列和每个 draw 的实例集合，不比较 draw 内顺序。
- **代价**：GPU 路径的同 command 共面实例可能有逐帧不同的 z-fight 顺序。
- **回退**：实测 overdraw 或共面闪烁成为问题时，加一个可选的 per-draw bitonic sort，键为 `(depth16 << 16 | entityIndex & 0xFFFF)`，只对 ≤ 2048 实例的 draw 在 groupshared 中单 pass 完成。它不改变任何数据结构。

### D3 Pass 淘汰：按容量，不按帧数

- **本质**：淘汰要回答的是"缓存太多了怎么办"，不是"多久没用了"。
- **约束**：
  - 一个活跃 Pass 的维护成本：每个结构事件 O(K)；内存约 4 B × element 数（CPU + GPU 各一份）。10 万 element 约 400 KB 每份。
  - 重建成本：一次 Burst 并行遍历，量级是毫秒以下到毫秒级。
  - Scene view、Preview 相机按需重绘，间隔不固定。按帧数淘汰会让它们反复全量重建。
  - 内置 9 个预设每帧都在用。
- **结论**：内置预设常驻，从不淘汰。自定义 desc 进 LRU，容量上限默认 16，只在 interning 时超过上限才淘汰最久未用者。淘汰发生在 record 阶段，结果确定，无计时器。
- **代价**：如果有代码每帧构造不同的 desc（误用），LRU 会抖动。Debugger 暴露活跃 Pass 数与淘汰计数，让误用可见。
- **回退**：上限是一个常量；若需要可改成资产配置项。

### D4 asmdef：不拆，用 static check 强制

- **本质**：要的是"依赖方向不被破坏"，asmdef 是实现手段之一，不是目标。
- **约束**：
  - 现在 RG ↔ Mesh 是双向引用（`RGDrawList.cs:5`、`MeshDrawGPUBackend.cs:8`）。拆 asmdef 之前必须先在代码上断开，这一步与是否拆分无关。
  - 跨程序集访问 `internal` 需要 `InternalsVisibleTo`，或者把大量 internal 改成 public。后者违反"backend 保持 internal"。
  - `static_check.py` 已是仓库的离线门禁，按命名空间检查 `using` 方向的成本很低。
- **结论**：B4 先把 `RGDrawListContext` / `RGMeshCompile` / `RGMeshOcclusion` 搬到 `MeshPipeline/Graph/`，让 `RenderGraph/` 只通过 `IRGCompileExtension` 被扩展；再在 `static_check.py` 加依赖方向检查。不拆 asmdef。
- **代价**：保护依赖的是离线门禁而不是编译器，绕过门禁的提交仍能编译通过。
- **回退**：依赖已经是单向时，拆 asmdef 只是机械操作。等 RenderGraph 需要被其他包复用时再拆。

### D5 LOD 作者入口：保留 `MeshAsset`

- **修正**：上一版把 `MeshAsset` 列为删除对象，这是错的。它已经是一个 LOD 链资产（`meshes[]`、`materials[]`、`lODInfos[]` 含 `screenSize`），Editor 有从 `LODGroup` 生成它的流程（`MeshAsset.cs:148`、`Editor/Action/AssetAction/Primitive/MeshAssetAction.cs`），只是 Runtime 从不消费它。
- **本质**：LOD 链是几何资产的属性，所有摆放共享，不是每个组件的属性。按 DOP 它属于 World 的共享资源表，和 `MeshSection` 一样引用计数。
- **约束**：
  - AGENTS §5.13 要求 `MeshComponent` 保持现有序列化字段身份，`meshAsset`（类型 `Mesh`）不能改名或改类型。
  - AGENTS §11 建议新增字段而不是改名。
  - 不能出现"同一个量有两个权威"。
  - `MeshComponent` 不能发现或修改 Renderer，所以不能直接读 `LODGroup`。
- **结论**：
  - `MeshComponent` 新增可选字段 `lodAsset`（`MeshAsset`），与 `meshAsset` **互斥**：Inspector 只允许赋其一，两者同时赋值时注册抛出。这是二选一，不是优先级规则，所以没有双重权威。
  - World 新增 `MeshLodChain` 表。每级 LOD 的 section 各自生成 Element 并带 `lodIndex`。Visibility 按投影尺寸与 `screenSize` 比较，写出 `lodIndex[entity]`，Gather 只收匹配的 Element。
  - 清理 `MeshAsset`：删除 `Tree`（`FMesh` 与顶层字段是同一份数据的两个权威），修正 `MeshLODInfo` / `FMesh` 按引用比较数组的 `Equals`。
- **代价**：新增一个序列化字段属于 schema 变更，按 AGENTS §11 需要写明意图、做一次保存与 no-op 验证。LOD 过渡（cross-fade / dither）不在本提案内。
- **回退**：`lodAsset` 为空时行为与现在完全相同。

## 15. 仍开放的问题

1. LOD 过渡：cross-fade / dither 是否需要，需要时它是 Visibility 的第二列（`lodFade`）还是 shader 侧处理。
2. GPU 后端的 LOD 选择：D5 的投影尺寸计算放在 `CullFrustum` kernel 中还是 CPU。倾向 kernel 内，与 frustum 测试共用 bounds 读取。
