> Delivery correction (2026-10-23): this report and its evidence were previously present locally but ignored by Git. This S17 delivery adds the report and the explicitly indexed [EarlyStages evidence archive](EarlyStages-closure/manifest.json). Earlier failures and acceptance boundaries below remain historical facts.

# S5 本机验证报告

原始阶段 `b8a582d287fa0bd56c3c6bafb20f0db68aea2da9`，携带已接受的前序修复（main `279c336`）。平台 Unity 6000.6.0f1 / CoreRP 17.6 / macOS Metal / Apple M3 Max；日期 2026-10-21。全程使用原 InfinityExample，无 Player 构建，不操作或等待其他 session 的 Editor。

| 门禁 | 实际结果 |
|---|---|
| Unity 编译 | 原 S5、诊断回放和 main 最小候选均无 C# 编译错误。既有 Metal potentially-uninitialized 警告保留，没有抑制或冒充跨平台编译通过。 |
| 原阶段测试 | StochasticSSRNumeric 5/5、QualityPreset 9/9；全部 Example 1566 =1513 Passed /0 Failed /53 Skipped。 |
| 候选回放 | 全部 Example 1567 =1514 Passed /0 Failed /53 Skipped，reload/resume 2/2；唯一新增测试为 UInt32Statistics_PreserveIntegerRangeAndRoi。 |
| 完整性 | 稳定发现/开始/结束身份精确对应，53 skip 集合不变；原生 XML 1534 =1514/0/20，callback 补回33个实际 checkpointed skip。根仍 Skipped:Ignored，frameworkComplete=false、callbacksComplete=true，不称全部 PASS。 |
| main 子门禁 | SSR numeric 5/5；完整显式内存验证配置下 Capture 13/13。先前两轮 11/13 的 Profile 不完整和 cube filter 等必需资源缺失分别保留原始失败，未禁用路径。 |
| 蓝噪声 | 三张纹理实际 SingleChannel /Uncompressed /R8_UNorm /linear /mip1，三资源槽和 Utility 仅内存绑定，未保存 GS。 |
| 原生画面 | Tile Class 的蓝/橙/灰、Ray Hit 绿/红、Confidence 和 History Age 四张实际窗口图；只把下方实际 viewport 算作证据，上方可能保留旧像素。Debug View 通过公开运行时参数选择，未宣称自动点击下拉菜单成功。 |
| Frame Debugger | 原生 Custom 树8事件：Classify2、Trace3、Resolve1、Temporal1、Spatial1；Compute 详情面板为空，不能声称看到了其 kernel 参数。 |
| 被动捕获 | 新入口保持 DebugView=None 与普通 Beauty 守卫；R32_UInt 分类和 SSRMeta 正常 Transfer readback/退休。请求分类时仅改变同一纹理的 RG 生命周期，不增加渲染资源/dispatch，不改历史。 |
| A/B | main 四组匹配矩阵、当前/前一 jitter、phase129 mod256。稳态 Lighting、RayHit、Resolve、Temporal、SSR、Meta 六项逐字节一致；Display 有极小重复波动，未声称全部画面 bit-identical。分类真实值 {0:144,1:16,2:80}。 |
| 历史年龄 | 固定地板内部 ROI 512 像素，粗糙度10/255、重建世界 y≈0，年龄全部1/5/9/13/17。不是全图 max/mean 推断。 |
| 2×2轮换 | 依据当帧 Depth/GBuffer eligibility，与半分辨率 RayHit.w 有效响应比较：正确 phase1/2/3/0 偏移全部0 mismatch，错误偏移57–113。没有把 hitUV 当成发射像素。 |
| 显露重置 | Dynamic cube 的真实 Depth 更新在 captureIndex67，629个几何独立定义的新显露地板像素全部 age1、Temporal=Resolved；后两帧628像素 age2/3、1边缘 age1。控制区始终64，cameraReset全false。 |
| 移动物体残留 | 旧反射独立 ROI562像素，67/68/69帧 Temporal红比 Resolved红高>.01 的像素为45/30/25；未见明显整块旧反射尾巴，但保留小量历史延续，不称零残影。 |
| 运动与质量 | 正常帧小轨迹 orbit0/4/8/12度，镜面清晰并随几何移动。四质量 trace160×90/160×90/320×180/320×180；新增粗糙接收面实测115/255，噪声/模糊/覆盖差异可见，未见亮点爆发；不声称所有指标逐档单调改善或任意运动都无问题。 |
| Confidence | 两组各31,800镜面有效像素独立重建，MaxDistance50/5 最大误差.00048784/.00048744（容差.001）。36边缘衰减像素，163近最大距离像素，每组4个有效 sampled backface 命中均alpha0。 |
| Spatial路由 | 正常 Medium 16帧 CPU marker 有Spatial、Ultra16帧无Spatial，其他marker保持；Ultra SSR=Temporal逐字节相同，其他三档 rough fixture 有16430/17246/18662个标量变化。GPU marker计数全0，不能当GPU证明；原生Custom树另存。 |
| 性能 | 每质量128预热+三组各300唯一有效 CPU/GPU 帧时间，共3600样本；完整 median/P95见S5/timing-summary.json。属于整个 Editor 帧，不是SSR单pass。GPU中位数非单调，系统负载/温度未隔离，无0.5前对照，不宣称普遍加速或已证明单调性能权衡。 |
| 离线 | 最终 main static_check 0 errors /0 warnings、git diff --check通过；HLSL没有变动，未把纯C#诊断修复写成着色器算法修复。 |
| 恢复 | 最终70619所有 probe退休、FrameDebugger关闭、capture Completed/outstanding0；临时fixture、清洁untitled两根/Transform、background=false、Play options1与完整GS JSON恢复。42历史源码、21 main自有fixture、15受保护资产/meta均核对。仅本任务Editor正常退出。 |

修复是有界的验证入口问题：SSR 分类原为 pass-local temporary，无法合法跨 pass 被读回；新增按请求提升至 graph-owned 的同一资源，普通帧仍走原生命周期。SSRMeta 使用现有图输出。UInt32统计原未支持真实分类格式，新数值测试覆盖 uint.MaxValue、完整范围及ROI，避免把整数位误读为float。没有改SSR追踪、时域或空间滤波算法。

原始失败与搭建问题分开保留：DebugView模式请求正常Beauty捕获被守卫正确拒绝，零帧/零readback，未放宽守卫；首轮运动物体默认Static，实际GPU几何未动，因此是无效harness对照；改Dynamic后按真实Depth更新帧判定。临时向量JSON递归导致一次Finish保存失败，同一份已完成raw修正序列化后保存，没有重录抹掉产品失败；后续Finish finally及driver取消/排空/退休已补全。一次冷启动在ready后正常关闭，没有崩溃栈；最后退出的delayCall未触发结束，使用同一公共EditorApplication.Exit(0)直接正常退出，预期CLI断连与实际正常shutdown分别记录。复用初始绑定文件名损失了64172的initial receipt；70619初始回执明确另存，不冒充原件，原测试/源码/其余恢复证据仍在。

持久集成仍开放：原 GlobalSettings/Profile 字节没有保存升级。main 的默认Profile、APV与新增资源等最终集成需要在对应后续阶段和最终HEAD完成。S5的内存配置通过不等于这些资产已经修好。既有天空黑色水平带未在本次定位，不归为S5修复成果。D3D12/Vulkan/Player/HDR、整项目画质和0.5前性能对照没有通过声明。

证据为 S5/evidence-index.json、稳定身份判定与33条原始skip checkpoint摘要、原始/候选XML、main-subgate原始失败与通过XML、关键原生图、正确Present方向的行为图、独立残留审查和计时回执。已消费raw已分批退休；索引中的原始路径是来源身份，不代表仍保留可打开缓冲。最终精简bundle保持小规模，精确大小见清理回执，源输入/活跃Library/DataStore不复制。

验收状态：源码、测试、行为/画面、报告与精简证据已获独立有界接受，全部producer停止并完成本轮清理；仅留下两份S6 replay输入。源码提交为ecd3c7b，远端身份在实际推送后补入，核对后解锁S6。所有阶段及最终HEAD完成前PR#11维持Draft，FeatureStatus和pending测试标记不作提前清除。

实际清理：最后一次完成清理780个剩余scratch文件、40,361,661 logical bytes，分配占用237056→768KiB；两份carry共160,905字节，唯一消费者S6构造，冻结后立即删除。首轮因AppleDouble误计raw而在删除前停止；随后shell仍产生源码提交。下一次尝试删除budget.json及其自动消失的侧车后遇到失效目录项停止；本回执只计最后剩余文件，不编造前次小量删除的字节数。新增收尾提交记录事实，不改写源码提交。此前分批退休main112,129,462B、age/quad/Static38,707,200B、dynamic16,128,000B、后续raw51,148,800B，不与本次重复计数。证据包40份payload、14,936,939字节，未保留完整临时协议archive。总任务未完成；root preimage/input-manifest及S1小型环境审计输入由最终源审计消费。

S5 publication: source `ecd3c7b`; cleanup ledger `98012a125f3e53a8d53119dfcca844fb0d76c404`. Explicit PR-branch push verified at that SHA; remote main remains c92be7707f45a1789688e5cc16e0845f183b2910. Package worktree clean after push. S6 unlocked.
