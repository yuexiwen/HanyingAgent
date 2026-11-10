# LanguageModels

## 功能与定位

定义 Runtime 与语言模型能力之间的公共协议，隔离 OpenAI、Azure OpenAI 等具体 Provider 的 SDK 差异。
LanguageModels 表示语言模型能力，与其他模块内部用于数据结构分类的 Models 区分。

当前已定义文本与工具消息、非流式调用及响应契约，没有模型客户端或 Provider SDK 依赖。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Models](./Models/Models.md) | 定义类型化消息、模型选项、结束原因与可空 Token 用量。 |
| [Operations](./Operations/Operations.md) | 定义一次非流式模型调用的接口、输入和响应。 |

## 一次调用与整个任务

一个 Task 可以多次调用语言模型；ModelResponse.Text 只是本次输出，不叫 FinalAnswer，
是否结束任务由 Runtime 判断。模型提出工具调用后由 Runtime 交给 Tools 模块执行，
再将工具 Observation 作为工具消息加入下一轮请求。

## 设计边界

完整保留 Tool Call ID、参数和对应工具结果，不用普通文本摘要替代工具协议；
Provider 客户端实现放在 [Infrastructure/LanguageModels](../../Infrastructure/LanguageModels/LanguageModels.md)。
System、User、Assistant、Tool 采用不同消息类型，JSON 使用 role 判别字段，不使用 Provider SDK 消息类型。
工具消息复用已有 ToolObservation，保留状态、错误与大输出引用，避免另建一套工具结果字段。
Context 负责消息选择与预算，模型模块不召回记忆、不保存对话、不执行工具、不修改任务状态。
当前不定义流式、多模态、Embedding、动态模型目录或 Provider 专用参数包。
