# Prompts

## 功能与定位

[LocalPromptManager](./LocalPromptManager.cs) 实现
[IPromptManager](../../../../Contracts/Agent/Capabilities/Prompts/Operations/IPromptManager.cs)，
负责参数化本地 Prompt 的发现与渲染。不依赖 Skill，不是 MCP Prompts，
不接入 Agent、Context、Console 命令或模型调用。

## 配置与文件

[appsettings.json](../../../../../appsettings.json) 默认配置：

```json
{
  "Prompts": {
    "Directory": null,
    "MaxFileBytes": 65536,
    "MaxPromptCount": 100,
    "MaxOutputCharacters": 65536
  }
}
```

Directory 为 null 表示空目录；空白字符串非法。
指定不存在或不可读的目录会导致启动失败，不退化为空列表。
相对路径基于 Host ContentRootPath（默认应用输出目录），不依赖终端当前目录。
模板不自动复制到输出目录，需显式部署到可信目录或配置其绝对路径。

每个直属子目录提供一个 UTF-8 的 PROMPT.md，不递归扫描：

```text
Prompts
└─ translate
   └─ PROMPT.md
```

示例：

```markdown
---
name: translate
description: 将文本翻译成指定语言。
arguments:
  - name: language
    description: 输出语言。
  - name: text
    description: 待翻译文本。
---
请将以下文本翻译成 {{language}}：

{{text}}
```

- 元数据仅支持 name、description、arguments，前两项必须为非空文本。
- 名称与目录名按 Ordinal 完全一致，长度 1–64，包含小写 ASCII 字母、数字和连字符，
  连字符不能位于首尾或连续出现。
- arguments 可省略或为 `[]`，表示无参数；每项仅包含非空的 name、description。
- 参数名为 1–64 个字符，以小写 ASCII 字母开头，后接小写字母、数字或下划线，不能重复。
- 本期所有参数都是必填字符串，不支持默认值、可选参数或嵌套对象。
- 正文必须非空。仅支持精确的 `{{name}}` 占位符，不支持占位符内空格、
  条件、循环、表达式、模板包含或双花括号的转义语法。
- 声明的每个参数都必须在模板中使用，每个占位符都必须有声明；可以重复使用同一参数。
- 非法 YAML、重复或额外字段、名称冲突、空正文和不匹配的占位符都使启动失败。
- front matter 开始和结束行必须为独立的 `---`，支持 UTF-8 BOM、LF 和 CRLF。

## 初始化与快照

[ApplicationHost](../../../../Host/ApplicationHost.cs) 注册同一个 LocalPromptManager /
IPromptManager 单例，并通过内部私有 PromptInitializationService 异步初始化。
全部模板解析和校验完成后，才一次发布只读快照；失败或取消不发布部分结果。
重复初始化不会重新加载，初始化前访问明确报错，文件修改在重启后生效。

与 Skill 共用内部 [LocalMarkdownFiles](../LocalMarkdownFiles.cs)，
复用有界 UTF-8 读取、链接路径拒绝和 YAML front matter 解析，不引入通用 Manager 基类。
根目录及祖先路径、子目录和文件不能包含 ReparsePoint（包括符号链接和目录联接）。
这是可信部署约束，不是对恶意并发替换文件系统的沙箱防护。
默认单文件最多 65,536 字节、最多 100 个模板，等于阈值允许，超过则拒绝。

## 发现与渲染

```csharp
var definitions = await prompts.ListDefinitionsAsync(cancellationToken);
var result = await prompts.RenderAsync(
    "translate",
    new Dictionary<string, string>
    {
        ["language"] = "English",
        ["text"] = "Hello"
    },
    cancellationToken);
```

ListDefinitionsAsync 只返回名称、描述和参数列表，所有列表均只读，不泄露可变内部集合。
RenderAsync 验证名称和参数，将参数字典快照化，再按模板一次替换。
参数值按字面值插入，即使含有 `{{language}}`、`$1` 或换行，也不会被再次解释。
没有参数的固定模板也可通过空参数字典渲染，不另外暴露 LoadAsync。

MaxOutputCharacters 限制最终文本的 UTF-16 字符单元数量，默认 65,536，
不是 Token 数，也不是文件字节数。重复占位符的每次替换都计入，
每次追加前检查，避免先分配巨大的完整结果再拒绝；超限抛出异常，不截断。
输出限制不是模型 Context 预算，未来仍需要由 Context 决定消息结构和预算。

当前渲染与查询均从内存快照同步完成，使用 Task.FromResult 保留可替换的异步接口；
并不启动后台线程。初始化才涉及真正的异步文件读取。

## 边界与验证

只加载受信任的模板来源，不在日志中主动输出模板或参数正文；
解析器异常可能携带出错位置或片段。
渲染结果只是普通文本，不自动进入模型，不自动提升为 System 指令。
将参数插入受信任模板并不使参数可信，也不隔离模型可能服从的恶意内容；
未来调用方和 Context 应区分指令与数据，本模块不声称解决提示注入。

测试见 [LocalPromptManagerTests](../../../../../TEST/HanyingAgent.Tests/Agent/Capabilities/Prompts/LocalPromptManagerTests.cs)，
覆盖发现、渲染、参数错误、非法模板、字面值替换、并发与快照、
文件及输出的精确边界、取消和 Host 启动。
