# RenderGraph 重构升级指南

## 概述

本次重构提升了 Render Graph (RG) 的自动化程度与执行效率，主要包含三大改进：

1. **修复异步计算Pass裁剪Bug** - 确保强制保留的异步计算Pass不会被错误裁剪
2. **实现颜色附件Load/Store Action自动推导** - 通过访问意图自动优化带宽使用
3. **自动化光栅Pass合并** - 利用Tile-Based渲染架构优势减少切换开销

## 主要变更

### 1. 新增访问标志枚举

```csharp
// 颜色附件访问标志
public enum EColorAccessFlag
{
    WriteAll,  // 全屏写入，不关心之前内容
    Write,     // 保留现有内容的写入（如混合）
    Discard,   // 写入但不加载旧值
    Read       // 作为输入附件读取
}

// 深度附件访问标志  
public enum EDepthAccessFlag
{
    ReadOnly,   // 只读深度测试
    ReadWrite   // 标准深度测试与写入
}
```

### 2. API 变更

#### 新的推荐API：

```csharp
// 设置颜色附件（新方式）
passRef.SetColorAttachment(colorTarget, index, EColorAccessFlag.WriteAll);

// 设置深度附件（新方式）  
passRef.SetDepthStencilAttachment(depthTarget, EDepthAccessFlag.ReadWrite);

// 设置输入附件（新增）
passRef.SetInputAttachment(inputTexture, index);

// Pass合并控制（新增）
passRef.AllowPassMerge(false);
```

#### 已废弃的API：

```csharp
// 旧方式 - 标记为 Obsolete 但仍可用
passRef.SetColorAttachment(colorTarget, index, loadAction, storeAction);
passRef.SetDepthStencilAttachment(depthTarget, loadAction, storeAction, flags);
```

## 迁移指南

### 步骤1：更新颜色附件设置

**之前的代码：**
```csharp
passData.colorTarget = passRef.SetColorAttachment(
    passData.colorTarget, 0, 
    RenderBufferLoadAction.Clear, 
    RenderBufferStoreAction.Store);
```

**迁移后的代码：**
```csharp
// 根据渲染意图选择合适的访问标志
passData.colorTarget = passRef.SetColorAttachment(
    passData.colorTarget, 0, 
    EColorAccessFlag.WriteAll); // 系统自动推导为Clear/DontCare + Store
```

### 步骤2：更新深度附件设置

**之前的代码：**
```csharp
passData.depthTarget = passRef.SetDepthStencilAttachment(
    passData.depthTarget,
    RenderBufferLoadAction.Clear,
    RenderBufferStoreAction.Store,
    EDepthAccess.Write);
```

**迁移后的代码：**
```csharp
passData.depthTarget = passRef.SetDepthStencilAttachment(
    passData.depthTarget,
    EDepthAccessFlag.ReadWrite); // 系统自动推导Load/Store Actions
```

### 步骤3：选择合适的访问标志

| 使用场景 | 推荐的访问标志 | 说明 |
|---------|---------------|------|
| 全屏清屏渲染 | `EColorAccessFlag.WriteAll` | 自动选择Clear或DontCare |
| 透明物体混合 | `EColorAccessFlag.Write` | 保留现有颜色进行混合 |
| 全屏后处理 | `EColorAccessFlag.Discard` | 不加载旧值，提高性能 |
| Input Attachment | `EColorAccessFlag.Read` | 移动端Tile-Based优化 |
| 标准深度测试 | `EDepthAccessFlag.ReadWrite` | 读写深度缓冲 |
| 只读深度测试 | `EDepthAccessFlag.ReadOnly` | 透明物体等场景 |

## 新功能使用

### 1. Pass合并优化

系统会自动识别可合并的连续光栅Pass：

```csharp
// Pass1 和 Pass2 如果使用相同的渲染目标，会被自动合并
using (var pass1 = builder.AddRasterPass<PassData>(sampler1))
{
    // 使用相同的colorTarget和depthTarget
    pass1.SetColorAttachment(colorTarget, 0, EColorAccessFlag.WriteAll);
    pass1.SetDepthStencilAttachment(depthTarget, EDepthAccessFlag.ReadWrite);
}

using (var pass2 = builder.AddRasterPass<PassData>(sampler2))  
{
    // 相同的渲染目标 -> 自动合并到单个NativeRenderPass
    pass2.SetColorAttachment(colorTarget, 0, EColorAccessFlag.Write);
    pass2.SetDepthStencilAttachment(depthTarget, EDepthAccessFlag.ReadWrite);
}
```

### 2. 禁用Pass合并

```csharp
// 对于特殊Pass，可以禁用自动合并
using (var specialPass = builder.AddRasterPass<PassData>(sampler))
{
    specialPass.AllowPassMerge(false); // 禁用合并
    // ... 设置渲染目标
}
```

### 3. 异步计算Pass修复

```csharp
using (var computePass = builder.AddComputePass<PassData>(sampler))
{
    computePass.EnableAsyncCompute(true);
    computePass.EnablePassCulling(false); // 强制保留
    
    // 即使输出没有被读取，Pass也不会被错误裁剪
    passData.output = computePass.WriteTexture(outputTexture);
}
```

## 性能优化建议

### 1. 合理使用访问标志

- **优先使用 `WriteAll`**：当完全重写附件内容时
- **谨慎使用 `Write`**：只在需要混合或保留内容时使用
- **利用 `Discard`**：全屏效果时可提高性能
- **善用 `Read`**：移动端可获得显著的带宽优化

### 2. 优化Pass合并

- **保持渲染目标一致**：连续Pass使用相同分辨率和格式的RT
- **避免不必要的合并禁用**：只在确实需要时使用 `AllowPassMerge(false)`
- **考虑Pass顺序**：相关的Pass放在一起以提高合并概率

### 3. 移动端特殊优化

```csharp
// 延迟渲染的移动端优化示例
using (var gbufferPass = builder.AddRasterPass<GBufferData>(gbufferSampler))
{
    // GBuffer Pass
    gbuffer.SetColorAttachment(albedoRT, 0, EColorAccessFlag.WriteAll);
    gbuffer.SetColorAttachment(normalRT, 1, EColorAccessFlag.WriteAll);
    gbuffer.SetDepthStencilAttachment(depthRT, EDepthAccessFlag.ReadWrite);
}

using (var lightingPass = builder.AddRasterPass<LightingData>(lightingSampler))
{
    // 使用Input Attachment读取GBuffer（Tile-Based优化）
    lighting.SetInputAttachment(albedoRT, 0);
    lighting.SetInputAttachment(normalRT, 1);
    lighting.SetDepthStencilAttachment(depthRT, EDepthAccessFlag.ReadOnly);
    lighting.SetColorAttachment(finalColorRT, 0, EColorAccessFlag.WriteAll);
    
    // 这两个Pass可能会被合并，实现完整的on-chip延迟渲染
}
```

## 兼容性说明

- **向后兼容**：所有现有代码继续正常工作
- **渐进迁移**：可以逐步迁移到新API，无需一次性修改所有代码
- **性能提升**：即使不修改代码，异步计算修复也会立即生效
- **废弃警告**：旧API会显示编译器警告，但不会破坏构建

## 注意事项

1. **测试验证**：迁移后请彻底测试渲染结果，特别是透明物体和后处理效果
2. **性能分析**：使用Profiler验证Pass合并是否按预期工作
3. **移动端测试**：在目标移动设备上验证Input Attachment优化效果
4. **异步计算**：检查之前被错误裁剪的异步计算Pass是否恢复正常

## 示例代码

完整的使用示例请参考 `RGExamples.cs` 文件，其中包含：

- 新旧API对比示例
- 不同访问标志的使用场景
- Pass合并控制示例  
- 异步计算Pass的正确用法
- 移动端优化示例

通过这些改进，RenderGraph系统在保持易用性的同时，显著提升了渲染性能和资源利用效率。