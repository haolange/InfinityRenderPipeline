# InfinityRP 重构：本地验证 agent 交接说明

你是本地 Editor 侧的**验证者**。实现由云端会话完成并推送到分支 `claude/kind-knuth-d599st`，你负责在真实 Editor 里编译、测试、截图、测量，然后按本文标准给出 PASS 或 RETURN，并把报告交给用户转回云端。

配套文件：同目录的 `InfinityRP-Principles-and-Redesign.md`（原理、设计规则、全仓诊断、目标设计、批次表）。判定"实现是否符合设计"时以它为准；B0 合入后，仓库内的 `AGENTS.md` 与 `ARCHITECTURE.md` 成为正式依据。

---

## 1. 角色与边界

**你做的事**
- 编译、刷新、读新日志窗口、跑 EditMode 测试、跑静态检查、截图比对、测量 GC 与 GPU 时间、读取 Rendering Debugger 的值。
- **可以直接修**的机械性编译错误：漏了 `using`、批次内重命名漏改的引用、`.meta` 缺失、设计意图唯一的签名不匹配。每处修改都写进报告，附 diff。
- 发现运行时缺陷时，定位第一个真实的 owner、顺序或寿命缺陷，附证据回报。

**你不做的事**
- 不重新设计，不扩大批次范围，不"顺手"重构。
- 不得用以下方式让问题消失：加 fallback、`return false`、绑定默认或黑贴图、`if (x == null) return`、关掉 pass、`EnablePassCulling(false)` 作为修复、`try/catch` 吞异常、跳过或删除测试、降低断言、在 Console 里过滤错误。这些全部视为失败，见第 2 节原理 P1 和 P8。
- 不保存资产，不做迁移（本轮没有获批的序列化迁移），不构建 Player（除非用户另行授权），不修改序列化字段名。
- 不对另一个 Editor 正在占用的项目运行 `unity run`、batch build 或第二个 Editor。操作前确认项目路径和进程 ID。

---

## 2. 判定所依据的原理（摘要）

- **P1 单一权威**：每个事实一个写者。静默兜底就是伪造事实，必须判失败。
- **P2 引用带代际**：过期 handle 必须被拒绝（抛异常）。重构后新出现的"stale handle"异常往往是**暴露了旧缺陷**，应定位它的 owner，而不是压掉。
- **P3 原子发布**：先准备再发布，失败完整回滚。
- **P4 寿命 = 最后读者**：GPU 还在用的资源不能复用或销毁。
- **P5 依赖向下**：机制层不认识上层概念。
- **P6 优化不进语义接口**：occlusion、后端选择这类优化不应改变画面。
- **P7 频率匹配**：稳态成功路径没有托管分配，没有 setup 期事实的字符串查找。
- **P8 证据强度**：静态检查不能为运行时结案，日志干净不能证明画面正确。只有截图加日志才能结案。

---

## 3. 环境与安全规则（来自 AGENTS.md §12，必须遵守）

1. 确认你持有的 Editor 与项目路径；不要操作别的会话持有的 Editor。
2. 刷新：`Tools/RefreshUnityEditor.sh <project>` 或 `.ps1`（内部调用 `AssetDatabase.Refresh`）。等待编译与 domain reload 完成，确认候选 DLL 已加载。
3. 日志：先打标记，只读取 `Application.consoleLogPath` 在标记之后的新窗口。之前稳定的日志不算验收证据。
4. 截图：使用 `Tools/CaptureUnityWindow.sh`/`.ps1` 加 graph transfer capture。不要用 `com.unity.pipeline` 的 `screenshot`：它替换 `targetTexture` 并调用 `Camera.Render`，不是正常帧。
5. 活性：Game view 不重绘时截图都一样。必须在 Play mode 下截图，或至少拿到一对 Game 区域不同的截图，才能作为活性证据。
6. CLI 只连 `127.0.0.1`，保持 bearer 认证，不打印也不归档凭据。
7. Scene view 的创建、关闭、停靠只在 Editor update pump 上做，不要在其渲染或 `OnGUI` 栈里关闭。
8. 启动 Editor 时不要继承指向 intermediate 深路径的 `TMPDIR`：Bee 的 unix socket 路径上限 104 字节，超出即脚本编译失败。
9. Example 项目位于 exFAT 卷：macOS 会生成 AppleDouble `._*` 文件，`AssetImportWorker` 可能在 `Library Redirect Path` 后以
   `stoull: no conversion` 崩溃。清理只能用 `dot_clean -m <目录>`，且每次都需要用户授权；不要删除 `Library` 或缓存。
10. 长时间运行后 Editor 可能在 `Library/ShaderCache.db` 上耗尽低位文件描述符（Bee 套接字超过 1024 即失败），只能重启 Editor，
    重启需要用户授权。
11. 画面比对必须与基线同尺寸（d7cef36 基线为 1445×813）。改变布局（例如关闭 Scene view）会改变 Game view 尺寸；比对前恢复
    基线布局，或在 Game view 分辨率菜单选择固定的 1445×813（这是 Editor 偏好，不是项目资产），不要设置 `EditorWindow.position`。

---

## 4. 中间数据（AGENTS.md §1 Intermediate data）

- 所有输出放在 `<Example 项目根>/intermediate/<task-id>/<run-id>/`，不要放在 `/tmp`、home 目录或包根目录。
- **基线是唯一允许跨批次保留的数据**：`intermediate/irp-refactor/baseline/`。它的消费者是 B1–B9 的比对，在 B9 通过后删除。在报告里写明它的路径、大小和原因。
- 每批结束：停止 producer，排空 readback，保留报告所需的摘要，删除该批的其余数据。报告中写明"已删除的路径、回收的空间、遗留阻塞"。

---

## 5. 开工前：在当前提交上建立基线（只做一次）

在批次 B0 之前的提交（`d7cef36` 或用户指定的提交）上：

1. 选定一组固定场景，写明路径，之后每批都用同一组：
   - 主展示场景；
   - decal、local light、translucency 三个验证场景（已知有 sky band / 暗三角 / cube 斑块，属于未诊断的既有缺陷，记录现状即可）；
   - 一个含大量 `MeshComponent` 和 LOD 的场景，用于 B7。
2. 每个场景采集以下内容：
   - Play mode 下固定相机截图，加一张移动相机截图；
   - Scene view 截图；
   - 开启 TAA 时的第 30 帧截图（temporal 稳定后）。
3. Rendering Debugger 的值：mesh requested/actual backend 与 reason、light 计数、各 feature 的 blocked reason。注意：基线里的 "Actual Backend" 已知是错的（总显示最后声明的 T2 = CPUDirect），B7 之后应变为正确值。
4. GC：Play mode，暖机 120 帧之后连续 300 帧。在 Profiler 里按管线根 marker（`InfinityRenderPipeline.Render` 及其子树）统计 GC Alloc，静止相机、移动相机各一组。Editor 自身的分配不计。
5. GPU 时间：`ComputeMeshCulling`、`ComputeMeshCompaction`、`UploadMeshData`、`RenderLoop.DrawMesh*`、`Shadows.DrawMesh*` 的 GPU 时间（Profiler GPU 模块或 Frame Timing），各取 300 帧均值。
6. 测试：`Tests/Editor` 全量 EditMode 结果（通过、失败的清单）。
7. 静态检查：`python3 -B Tools/StaticCheck/static_check.py --repo <package>`。

---

## 6. 每批流程

1. 拉取分支，记录批次提交哈希。提交信息里包含批次号和该批的验收清单。
2. 跑静态检查；失败直接 RETURN。
3. 刷新 Editor，等待编译。有编译错误时：机械错误自己修并记录；其余错误 RETURN，附上第一条错误及其上下文。
4. 读新日志窗口：不允许出现 InfinityRP 的异常洪泛，也不允许新增 warning。
5. 跑 EditMode 测试：与基线对比。该批明确改写或删除的测试，以提交信息里的清单为准。
6. 画面类批次（见第 7 节表格）：按基线的场景和机位截图，用 `python3 Tools/FramePairDiff.py` 与基线比对，生成 JSON 报告。
7. 性能类批次：GC 和 GPU 时间与基线比对。
8. 给出 PASS 或 RETURN，清理本批数据，写报告。

**画面判定**：FramePairDiff 的 SSIM 与 diff 阈值采用工具默认值（`DIFF_THRESHOLD = 8/255`）。凡是**结构性差异**都判失败：缺物体、缺阴影、黑块、全屏偏色、深度破洞。只有第 7 节表格里列为"预期变化"的才允许不同，而且必须在报告里逐项确认。

---

## 7. 各批次的验收重点

| 批次 | 内容 | 必查 | 预期变化（允许） | 重点怀疑 |
|---|---|---|---|---|
| B0 | 文档重构 | 静态检查（链接）；AGENTS 体积 ≤ 15KB；同一事实不在两份文档里重复 | 无代码变化 | 文档与代码不符的陈述 |
| B1 | 按 R15 删除死代码 | 编译、测试、画面与基线一致 | 无 | 被反射、场景或 asmref 字符串引用的类型（Shader Graph target 通过字符串引用 Infinity） |
| B2 | 分层修复、Validation 独立程序集 | 编译（Editor 与 Development 构建配置下）；`Editor/Cli` 命令仍可用；capture 正常 | 无 | Validation 程序集在 Editor 中是否被加载并注册了观察者；release 配置下（只编译，不构建 Player）是否已无任何 Validation 符号 |
| B3 | setup 期解析表 | 画面一致；GC 下降或持平；kernel 缺失时在 setup 阶段抛异常（而不是 record 时静默跳过） | 无 | kernel index 和 thread group size 是否和以前一致（DeferredShading 8×8、LightTileMask 16×16） |
| B4 | 资源池代际、fence 前置条件、HistoryCache 基于池 | 画面一致（含 TAA 第 30 帧）；连续运行 5 分钟无泄漏（池计数稳定）；改变窗口大小时 history 重建正常 | 无 | 设备是否支持 graphics fence（Metal 应支持，不支持会在构造时抛异常）；history 第一帧 / reset |
| B5 | RG 核心 | 画面一致；Frame Debugger 里 native render pass 分组和 segment 数与基线相近；renderer list 失效 handle 现在会抛异常 | segment 拆分来自池的 release 事件，计数可能略有不同 | 新出现的 stale-handle / 未声明读写异常：定位 owner 并回报，不要压掉 |
| B6 | 每相机流程、相机 cbuffer、Settings 快照、renderer list helper | 画面一致；Scene view、Preview 相机、带 targetTexture 的相机都要截图；关闭 SRP Batcher 后 GPU Instancing / Dynamic Batching 开关真正影响 Frame Debugger 里的批次 | `camera.depthTextureMode` 不再被每帧改写 | jitter（TAA 抖动）是否一致；history reset 原因是否正确（窗口缩放、相机跳切、capture） |
| B7 | Mesh 重构 | 画面一致（重点：LOD 过渡区、阴影 cascade、local shadow cube face、occlusion 首帧和相机快速转动）；Debugger 的 Actual Backend 现在正确；GC 为 0；**GPU 时间证伪条件**：主视图所有 list 的 culling + compaction GPU 时间之和若高于基线 → 报告 RETURN-PERF（设计里预留了退路，API 不变） | profiling marker 名称变化 | LOD 边界上 Depth 和 GBuffer 选到不同级别造成的深度破洞（验证 shader 里的 `precise`）；recovery 余量为空的帧；CPU 后端（T0/T1/T2 半透明） |
| B8 | Pass/Lighting/Atmosphere | 画面一致（重点：SSR、SSGI、GTAO、体积雾、体积云、大气、Combine LUT、subsurface）；无太阳光场景；无 AO 场景；capture 功能 | 无太阳时不再伪造头顶白光：天空少了太阳项，属于预期且正确的变化，需截图确认 | cbuffer 布局与 HLSL 是否匹配（static check 的 GPU-RECORD 检查 + 画面）；history 有效性 |
| B9 | 文档同步、FeatureStatus | 静态检查；把基线与最终的对比摘要写进 FeatureStatus | — | 删除基线目录，确认 `intermediate/` 已清空 |

---

## 8. 报告模板（交给用户转回云端）

```
批次: Bx   提交: <hash>   Editor: <版本>   图形 API: <Metal/D3D12/...>
静态检查: PASS/FAIL（失败时附输出）
编译: PASS/FAIL（第一条错误与上下文；本地机械修复的 diff）
新日志窗口: <InfinityRP 异常/警告条目，无则写"无">
EditMode: 通过 N / 失败 M（失败列表；与基线的差异）
画面: <场景/机位 → SSIM、diff 比例、JSON 路径>；结构性差异: 有/无；预期变化确认: <逐项>
Debugger: <backend、计数、blocked reason 的变化>
GC（暖机后 300 帧）: 静止 x B/帧，移动 y B/帧（基线 a/b）
GPU（B7）: culling+compaction 总和 x ms（基线 y ms）
结论: PASS / RETURN（原因、第一个 owner、证据路径）
中间数据: 已删除 <路径>，回收 <大小>；保留 <基线路径、大小、原因>
```

**RETURN 时**：已经通过的批次保持不变；依赖被退回批次的后续批次锁定（AGENTS §1 Verification）。
