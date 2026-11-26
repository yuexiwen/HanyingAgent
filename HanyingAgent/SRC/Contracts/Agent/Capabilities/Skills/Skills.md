# Skills

定义与本地文件系统无关的 Skill 发现和加载契约，便于以后替换为远程服务实现。

| 文件 | 职责 |
|---|---|
| [SkillDefinition](./Models/SkillDefinition.cs) | Skill 名称与简短描述，用于发现和选择。 |
| [SkillDocument](./Models/SkillDocument.cs) | 元数据与完整方法说明正文。 |
| [ISkillManager](./Operations/ISkillManager.cs) | 列举定义，以及按名称加载完整文档。 |

ListDefinitionsAsync 返回按名称 Ordinal 排序的只读列表，没有 Skill 时返回空列表。
LoadAsync 要求有效名称；未知名称抛出 KeyNotFoundException，取消通过异常传播。
本地实现要求名称为 1–64 个小写 ASCII 字母、数字或连字符，连字符不能位于首尾或连续出现。
返回模型只包含字符串和不可变 record，不包含本地路径、文件流或 SDK 对象。

Skill 是任务方法说明，不是可执行工作流。接口不负责选择 Skill、组装 Context、
调用模型、执行工具或授予权限，不依赖 Prompt 模板模块。
本地实现与文件格式见 [Runtime Skills](../../../../Runtime/Agent/Capabilities/Skills/Skills.md)。
