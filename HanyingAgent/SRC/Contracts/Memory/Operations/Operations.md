# Operations

## 功能与定位

集中组织记忆能力接口及调用协议，共享变更方法但保留工作记忆和长期记忆不同的读取与一致性语义。

## 能力接口

| 接口 | 功能 |
|---|---|
| [IMemoryWriter](./IMemoryWriter.cs) | 声明 WriteAsync、UpdateAsync、ForgetAsync 三个共同变更操作。 |
| [IWorkingMemoryManager](./IWorkingMemoryManager.cs) | 继承变更能力，并通过 ListRecentAsync 按归属直接读取近期记忆。 |
| [ILongTermMemoryManager](./ILongTermMemoryManager.cs) | 继承变更能力，并通过 SearchAsync 召回与 Query 相关的长期记忆。 |

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义写入、更新、直接读取和搜索的输入数据。 |
| [Responses](./Responses/Responses.md) | 定义操作特有的结果包装，当前包含搜索命中与分数。 |

## 行为约定

- 接口均为异步调用并接收 CancellationToken；当前没有具体实现。
- 工作记忆变更成功后，后续直接读取应反映变更；长期变更成功表示权威数据已更新，不保证索引已刷新。
- 读取仅匹配明确 Owner，并按约定过滤权限、状态和有效期；不存在的变更目标及非法操作必须显式报错。
- 不提供仅修改 Scope 的 Promote；短期到长期需要整合并创建新记忆。
- 只依赖 IMemoryWriter 的流程仍须显式选择目标记忆源，不能因共享接口而混用两个存储。
