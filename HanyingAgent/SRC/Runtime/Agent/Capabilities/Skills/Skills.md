# Skills

## 功能与定位

管理任务方法说明，提供元数据发现与按名称加载完整正文。
当前已实现 [LocalSkillManager](./LocalSkillManager.cs)，实现公共
[ISkillManager](../../../../Contracts/Agent/Capabilities/Skills/Operations/ISkillManager.cs)。
不依赖 Prompt 模板模块，不接入 Agent、Context 或模型工具循环。

## 配置与文件

[appsettings.json](../../../../../appsettings.json) 默认配置：

```json
{
  "Skills": {
    "Directory": null,
    "MaxFileBytes": 65536,
    "MaxSkillCount": 100
  }
}
```

Directory 为 null 表示没有来源，初始化后返回空目录；空白字符串非法。
指定不存在或不可读的目录会导致启动失败，不伪装成空目录。
相对路径由 Host 按 ContentRootPath（默认应用输出目录）解析，不依赖终端当前目录。
Skill 文件不自动复制到构建输出，应部署到配置目录，或配置已有可信目录的绝对路径。

只扫描根目录的直属子目录，每个目录必须含一个 UTF-8 的 SKILL.md：

```text
Skills
├─ document-analysis
│  └─ SKILL.md
└─ code-review
   └─ SKILL.md
```

示例：

```markdown
---
name: document-analysis
description: 分析文档，核对证据并整理结论。
---

# 文档分析

1. 明确分析目标。
2. 阅读相关文档。
3. 区分事实和推断。
4. 输出结论和来源。
```

通过内部 [LocalMarkdownFiles](../LocalMarkdownFiles.cs) 与 Prompt 模块共用
有界 UTF-8 读取、路径检查及 YamlDotNet front matter 解析；
正文保留为文本，不解释为执行步骤，两个模块的业务契约仍然独立。
开头和结束分隔行都必须是独立的 `---`，支持 UTF-8 BOM、LF 和 CRLF。
元数据只允许 name、description 两个非空标量字段；重复字段、额外字段、
非法 YAML、空正文和缺失文件都明确失败。
参考 Agent Skills 的文件组织，本期仅支持该子集，不宣称完整兼容。

name 必须与目录名按 Ordinal 完全一致，长度 1–64，只含小写 ASCII 字母、数字及连字符；
连字符不能位于首尾或连续出现。名称必须唯一。
MaxFileBytes 是包括头部和 BOM 的实际字节上限，等于阈值允许，超过则拒绝；
读取过程也检查限制，不仅检查初始文件长度。MaxSkillCount 限制 Skill 数量。
两项限制必须为正数，MaxFileBytes 还必须小于 Int32.MaxValue。

## 初始化与访问

[ApplicationHost](../../../../Host/ApplicationHost.cs) 注册同一个 LocalSkillManager /
ISkillManager 单例。其私有嵌套类 SkillInitializationService
在 Host 启动时异步初始化，不在构造函数中同步等待文件 I/O。

```text
绑定配置 → 解析目录 → 扫描与校验全部文件 → 一次发布只读快照
```

- InitializeAsync 通过异步锁协调，成功后重复调用不重新加载。
- 初始化失败或取消不发布部分目录；尚未初始化时调用查询接口明确抛出异常。
- ListDefinitionsAsync 返回按名称排序的独立只读列表，不返回正文。
- LoadAsync 按名称查询快照，返回元数据和完整 Instructions；未知名称明确报错。
- 加载时不拼接调用方提供的路径，也不重新读磁盘。
- 文件修改在重启后生效；没有热更新、注册、修改或删除接口。
- 日志记录初始化结果及错误，不主动记录完整正文；解析库异常可能携带出错位置或片段。

启动读取全部文件不等于将正文全部发送给模型。未来可以先向模型提供元数据，
再根据结构化加载请求将选中 Skill 正文交给 Context 处理信任层级与预算。
本期没有实现这条 Agent 链路。

测试见 [LocalSkillManagerTests](../../../../../TEST/HanyingAgent.Tests/Agent/Capabilities/Skills/LocalSkillManagerTests.cs)。

## 设计边界

Skill 是任务方法，不是工具实现或执行引擎，加载成功也不代表步骤已执行。
目录必须来自可信部署；本期拒绝路径中的符号链接、目录联接和其他 ReparsePoint，
包括根目录的祖先路径，不跟随它们读取内容。这不是对恶意并发替换文件系统的沙箱防护。
普通嵌套资料、脚本及目录内其他资源不会被加载或执行。
Skill 提到某工具不等于授权，权限必须由实际工具执行边界落实。
自动选择、参考资料加载、脚本执行和 Agent 接入均留待后续独立设计。
