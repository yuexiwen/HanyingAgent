# Models

## 功能与定位

定义用户身份关联键与轻量资料，不包含认证信息或运行时资源。

当前无子目录，包含：

| 类型 | 功能 |
|---|---|
| [UserId](./UserId.cs) | 只读 record struct，显式构造拒绝空白，并通过 JsonConstructor 保持只读 Value 的 JSON 往返。 |
| [UserInfo](./UserInfo.cs) | 表达 Id、DisplayName、CreatedAt 与 UpdatedAt 的资料快照。 |

## 字段约定

- Id 创建后不变，DisplayName 是可更新的显示名称，不是登录名或唯一标识。
- CreatedAt 由管理器生成且创建后不变；首次 UpdatedAt 与 CreatedAt 相同。
- 实际更新显示名称时刷新 UpdatedAt，不能早于 CreatedAt 或倒退；同名更新不改变时间。
- 查询返回快照，不自动复制或持续跟踪底层存储；record 的 with 表达式不等于更新持久化资料。
- 当前不定义 Version，不承诺通过资料快照检测并发冲突。

除 UserId 显式构造外，DTO 不自动维护全部业务不变量；default ID 仍需入口校验。
