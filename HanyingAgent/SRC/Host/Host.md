# Host

## 功能与定位

把用户交互适配成 Runtime 的调用，负责输入解析、会话视图和输出展示。

当前仅为目录骨架，没有子目录或业务实现。

## 后续组件规划

| 组件 | 一句话概述 |
|---|---|
| AgentShell | 读取输入、维护当前会话视图并展示结果和增量事件。 |
| CommandParser | 用确定性代码识别 create、select、cancel 等显式命令。 |
| 输出渲染 | 将 Runtime 结果与事件转换成用户可理解的展示。 |

## 设计边界

显式命令不调用模型；Host 不拼接模型 Context、不执行记忆检索算法，也不推进 ReAct。
Session 生命周期的实际协调位于 Runtime/Orchestration，Host 只发起相关操作。
