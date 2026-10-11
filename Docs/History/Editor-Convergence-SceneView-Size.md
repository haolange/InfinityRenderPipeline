# SceneView 左上局部显示修复回执

本回执对应 `PLAN.md` 的 SceneView 尺寸专项。功能修复、资源退休和最终 Infinity EditMode 已通过独立 Astra 复核；GPU 性能与最终清理尚在收尾时，不签专项整体 PASS。原 C0–C7 全量收敛仍是独立的未完成范围。

## 根因与最终实现

失败时，SceneView 的正常帧源、实际目标和 Present viewport 都是 1638×1768。实际目标纹理中四个象限均有完整内容，但 Editor 窗口只显示左上部分。改为显式导入 `camera.targetTexture` 后，这个窗口现象仍存在，因此不能把目标身份修正单独当作根因闭环。

真正触发现象的是 `RenderUIOverlay` 错误接受了 SceneView。该 Unity 原生显示 Overlay list 只属于没有 targetTexture 的 Game 显示相机。当前 HDRP 的 `hdCamera.isMainGameView` 使用同样的边界。最终条件为：

```csharp
if (camera.cameraType != CameraType.Game || camera.targetTexture != null)
    return;
```

恢复精确旧条件进行有界对照，会再次出现同样的左上局部显示；恢复此修复后，窗口再次完整覆盖。此对照证明错误 Overlay 消费者范围是触发原因，不推测未经定位的 Unity 原生 GUI 内部变量。Camera/World Canvas 的 T2 路径没有删除。

相邻的批准改动一并收敛：

- `CameraDimensionDescriptor` 在 BeginCameraRendering 回调完成后冻结，SceneView/Preview 使用自身原生尺寸，Game SR 状态与实际缩小状态分开。
- 显式目标使用 Unity 已拥有的 RenderTexture identifier；RG 保存真实 descriptor，只拥有导入包装。Editor 测试证明 Dispose 不释放 Unity 的目标纹理。
- 中间附件使用内部区域，Present 使用输出区域；viewport、源尺寸和 UV 在 record 阶段绑定，并在实际执行/提交后留下证据。
- DisplayColorBuffer 保持显示线性；颜色编码只在最终 Raster Present 中完成。曝光、Tonemap、Volume authoring 数值及颜色转换算法没有为了修尺寸而调整。
- 独立 OutputTransform pass、kernel、marker、资源字段、对应 meta 和失效测试调用已退休。共享颜色转换函数仍是唯一算法实现。
- 临时 SceneViewSizeProbe 已删除，并确认新加载程序集没有这个类型。没有保留对照开关或另一个生产输出分支。

## 功能与图像证据

精选证据位于 `Documentation~/Evidence/SceneView-Size-2026-09-13/`，避免将报告图片作为 Unity 项目纹理导入。该目录不包含整个临时数据树或重复 raw captures。

| 验收内容 | 证据与结果 |
|---|---|
| 同帧尺寸与目标 | 最终三帧 source/target/viewport 均 1638×1768，目标身份 `RenderTexture:33075:256`，UV (1,1,0,0)，Present executed，Submit/读回/退休完成 |
| 当前颜色保护 | 与原始失败基线位姿、旋转、projection、完整 Volume 快照相同；差异超过 8/255 的像素比例为 0.11004895%、0.17306725%、0.14730744%，均小于 0.5% |
| 停靠/最大化/浮动 | 实际窗口图均完整覆盖；最大化相机 3456×1764；两个独立 SceneView 验证了 1638×1768 与 1588×1108、以及后续 1404×936 |
| 非整尺寸 | Present 实际 GPU 测试覆盖 1×1、17×13、1919×1079 的五种策略与完整 padding 检查；本机原生窗口把 641.5×503.5 points 取整为 642×504，未硬编码或篡改 camera rect |
| Game SR 隔离 | 0.5/0.75/1/Camera Off 正常帧内部为 819×889、1229×1334、1638×1778、1638×1778；显示均 1638×1778；切回 SceneView 保持原生尺寸 |
| Preview | 正常 Inspector 预览有 64×64、902×360 两种实际提交身份，成功计数 1→2→3，尺寸对应正确；没有调用 Camera.Render 制造验收帧 |
| 交互 | 旋转停止、post off、Gizmos/TexturedWire、15 次有界原生窗口尺寸变化后保持覆盖；post off 三帧为 1284×956；临时窗口已关闭 |
| 时序 | 最终配对三帧 historyReset=false；没有加入持续重绘作为生产修复。性能/捕获回调均是有界验证生产者，结束后注销 |
| 测试 | 最终 `editmode-results.xml` 共 301 个 Infinity test-case，301 Passed、0 Failed、0 Skipped、0 Inconclusive；对应 runtime MVID `f991f011-48b4-494c-b0cb-d0574efb91e3` |

一次最终配对尝试使用了交互后未恢复的视角，得到约 81% 图像差异。该无效比较保留在 `final-color-comparison.json`；恢复开工时保存的 original-view 后才得到上表的有效配对。没有用错误位姿的结果签通过。

## 迁移与身份

GlobalSettings 在加载/编译期间没有自动保存。先备份当前字节并记录完整语义及 GlobalObjectId 引用图，删除唯一资源字段后进行显式定向保存。除 outputTransformShader 及其引用外，其他结构、GUID/local ID、引用及参数保持相等。重开、二次保存和独立 no-op 的字节一致。

- 原始 asset SHA-256：`3ef43263a8cc1d3c94b739d7aceddde51ec8ce573c06c8bb9349411e03213c42`
- 迁移后 asset SHA-256：`cabd163193f185a27d3564ea735abcbaee81de103a49fdbf7dcbcb818be7ab3f`
- 不变 meta SHA-256：`09677976e5af1df19fa63616630ee2236159ea8f543cae07c74e93dbe12bbf38`
- 最终源码/资产清单 SHA-256：`f0c799741015ccad0507907b7ff9caea0dcb0912e9dce7a62669730dc038e423`

独立 Astra 逐文件核对该清单中的 1318 个文件，全部一致。范围包括 Infinity Runtime/Editor/Shaders/Tests、Example Assets/ProjectSettings 和 package/解析 manifest；报告与历史回执单独绑定，文件系统 AppleDouble 不作为 Unity 源码/资产。

## 性能与未关闭边界

精确 Overlay gate 反事实 CPU 对照，两侧均预热 120 个正常上下文后采集 3×300 帧，分辨率全为 1638×1768。三组 median 没有回退，P95 最大增加 0.6573%。这证明该精确尺寸故障修复的 CPU 范围结果，不扩展为原 C0–C7 的整体性能结论。

GPU 候选侧已取得 919 个合格原生帧，使用同一补强分析器重算，按分段及段内开始时间顺序取前 900 帧，分为 3×300。不是无中断的连续 900 帧。对照侧未完成，因此没有相对 GPU 性能 PASS。Unity 自带 GPU recorder 在 Metal 下没有返回样本，零值不作为通过证据。默认沙箱中的 Xcode 枚举曾不完整；获准只读枚举后确认工具可用，已纠正“工具缺失”的判断。一次 65 秒 System Trace 在结束处理时异常膨胀，已取消且删除约 15.6 GB 失败输出，Unity Editor 没有被结束。后续使用必要 instruments、短分段及明确内存/磁盘/结束时限检查；未完成的采样不计入通过结果。

最终 Console 不是零错误：同一 PID 启动早期已经出现 readonly-database 报错，最终 TestRunner 新建 bootstrap scene 又出现三条。独立调查确认是既有复现，但尚未得到实际数据库连接路径、打开模式和 SQLite 扩展错误码，不能说已根治。捕获持久化负例的 UnauthorizedAccessException 则由 LogAssert.Expect 明确要求，二者不能混为一谈。本候选无新增 Infinity 编译/Shader 警告。

原生运动/TAA 单 Dispatch、其他 Editor 画质、旧启动/缓存异常及 C0–C7 其余 gate 不由此回执关闭。没有进行 Player 构建、打包、提交或分支操作。

## 清理

本专项拥有 `intermediate/editor-convergence/sceneview-size-20260912-2315/`。精选证据已从 raw、重复截图和试验工具中分离，已退休输出路径及临时诊断源码已删除。最终生产者停机、全部专项路径删除、实际卷空间与回收字节将在最终清理回执中核验。旧 C0/其他专项的仍有消费者基线、Unity Library、用户日志及历史交付物不做顺带删除。


## 当前阻塞与恢复边界

GPU 对照的重编译触发了 PID 20238 的原生 AssetDatabase 崩溃：`LMDB MDB_BAD_RSLOT`、`ShaderGraphImporter.GetIcon -> MatchAsset`、SIGTRAP。对照源码已由保护流程恢复，崩溃后重新核对 1318 个源码/资产文件全部一致。此前功能验收保持为此前已取得的证据，不能冒充当前实例已经恢复。

同版本 Editor 在原项目路径正常重开为 PID 58885 后，CLI 和窗口均无响应。3 秒采样中主线程 2168/2168 次位于 Search 资产索引的异步导入及 Unity job 分配路径。当前实例未保存状态无法读取；崩溃前可读取的场景 dirty=false。未强制结束该实例，需按已批准计划的异常处理约定先取得用户确认。

此时结论为：**尺寸功能修复及资源退休已通过；专项整体未完成，原生 Editor 恢复、GPU 对照及待验收原始数据和两个活动日志的最后清理仍阻塞。** 当前恢复日志位于任务目录，由该实例持有，不能虚报任务路径已删除。

最后一批已完成中间数据清理删除 4026 个文件，逻辑大小 722,318,080 字节、分配大小 1,719,402,496 字节；目的卷可用空间增加 1,816,133,632 字节（卷上其他活动可影响差值，不能将其等同于文件逻辑大小）。准确清单见精选证据中的 partial-cleanup-receipt.json。任务目录只保留 pending-gpu-native 原始 GPU 输入及其分析器和两个当前 Editor 持有的日志；GPU 对照及独立原始输入审查仍是这些数据的消费者，因此未删除任务目录，未宣称清理全部完成。


用户要求全量中间清理后的现状核验：项目 intermediate/ 已不存在。该目录在本次只读盘点与删除前置校验之间消失，删除脚本在 assertion 处停止，未执行删除；不能归因于本 agent，不能报告本次回收字节。此前 pending-gpu-native 及两个恢复日志保留状态已失效，GPU 对照仍为 NOT PASS。PID 58885 已不存在，本次未发送结束信号，未启动任何采样。详见 Documentation~/Evidence/SceneView-Size-2026-09-13/user-requested-cleanup.json。
