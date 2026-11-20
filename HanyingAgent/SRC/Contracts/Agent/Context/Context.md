# Context

## 功能与定位

定义一次模型调用前的上下文构建协议，与 LanguageModels 的实际调用协议分开。
调用方提供可信系统指令、已排序的消息与已经读取的记忆数据，构建器返回已有 ModelInvocation。

当前只支持纯文本 User / Assistant 消息与记忆参考数据，不调用模型、不读取对话或记忆服务、不修改记忆或任务。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Operations](./Operations/Operations.md) | 定义构建接口及请求。 |

## 设计边界

- 具体实现位于 [Runtime/Agent/Context](../../../Runtime/Agent/Context/Context.md)，契约不引用 Runtime。
- 输出复用 [ModelInvocation](../LanguageModels/Operations/Requests/ModelInvocation.cs)，不另建 Provider 请求或 JSON 字符串接口。
- 消息序列已经包含当前用户输入，不再额外传入 Task.Input 或追加一次当前输入。
- Memories 是编排层已经完成归属授权与有效性筛选的候选数据；空列表表示不包含记忆，不自动读取其他服务。
- 原 MemoryOwners 字段已替换为 Memories；调用方必须升级请求，旧 JSON 请求缺少必需字段会失败。
- Context 统一管理模型级输入预算，预留输出与安全余量后，为历史和记忆保留份额并按需借用闲置额度。
  当前计数器使用明确标记的保守 Token 估算，不是精确 tokenizer。
- 最终消息正文另受字符保护上限约束，默认 32,000；请求可收紧。候选内容可整条省略，
  系统指令与当前输入不可截断，本身超限明确失败。
- 本地纯文本与记忆组装已通过独立测试及真实组件组合验收；对话入口显式读取 User 记忆后调用构建器，
  不代表工具历史、精确 Token 预算或长期记忆已实现。
- 后续远程适配仍需单独处理身份授权、大小限制、超时和传输错误。
