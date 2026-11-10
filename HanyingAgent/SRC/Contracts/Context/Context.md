# Context

## 功能与定位

定义一次模型调用前的上下文构建协议，与 LanguageModels 的实际调用协议分开。
调用方提供可信系统指令、已排序的消息与明确的记忆归属，构建器返回已有 ModelInvocation。

当前只支持纯文本 User / Assistant 消息与工作记忆，不调用模型、不读取对话服务、不修改记忆或任务。

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Operations](./Operations/Operations.md) | 定义构建接口及请求。 |

## 设计边界

- 具体实现位于 [Runtime/Context](../../Runtime/Context/Context.md)，契约不引用 Runtime。
- 输出复用 [ModelInvocation](../LanguageModels/Operations/Requests/ModelInvocation.cs)，不另建 Provider 请求或 JSON 字符串接口。
- 消息序列已经包含当前用户输入，不再额外传入 Task.Input 或追加一次当前输入。
- MemoryOwners 来自可信执行上下文，不由模型自行指定；明确传空列表表示不读取记忆，不自动补齐其他归属。
- 本地实现通过构造配置提供最终消息正文总字符上限，默认 32,000；不改变公共请求，也不保证 Token 窗口。
- 本地纯文本与工作记忆的五步最小版已通过独立测试及真实组件组合验收；不代表工具历史、精确 Token 预算或 Agent 接入已实现。
- 后续远程适配仍需单独处理身份授权、大小限制、超时和传输错误。
