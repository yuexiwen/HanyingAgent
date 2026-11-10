# LanguageModels

## 功能与定位

将统一模型协议适配到具体 Provider，实现请求发送、响应解析及后续流式输出。

当前为目录骨架，没有子目录或业务实现；与 [Contracts/LanguageModels](../../Contracts/LanguageModels/LanguageModels.md) 的协议声明不同。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| LanguageModelRuntime | 实现 ILanguageModelRuntime，提供统一非流式模型调用能力并协调 Provider 适配。 |
| Provider Client | 调用 OpenAI、Azure OpenAI 等实际服务并转换协议。 |
| MockModelClient | 返回可预测响应，支持离线验证执行循环和工具调用。 |

## 设计边界

不自行检索记忆、构建业务 Context 或修改任务状态。
工具调用的 ID、参数、结果关联和结束信息必须正确保留，失败与取消不可转为成功回答。
密钥来自安全配置，不写入源码或原始日志；具体服务依赖仅在实际实现时引入。
ModelToolMessage 复用 ToolObservation，适配器将其映射为正确工具角色和调用 ID，并在内容中保留状态、错误和必要输出引用。
统一 ModelResponse 使用 Text 而非 FinalAnswer，保留截断、拒绝、未知原因及未知用量；流式能力仍待后续设计。
