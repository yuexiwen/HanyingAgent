# Requests

## 功能与定位

定义用户资料创建和显示名称更新的可序列化输入。

当前无子目录，包含：

| 请求 | 功能 |
|---|---|
| [CreateUserRequest](./CreateUserRequest.cs) | 只携带 DisplayName，Id 和时间由后续管理器生成。 |
| [UpdateUserRequest](./UpdateUserRequest.cs) | 携带目标 UserId 和新的 DisplayName，不允许替换身份或创建时间。 |

## 设计边界

- DisplayName 必须非空白，允许重名，不作为认证凭据或用户检索键。
- 请求不携带密码、角色、令牌或 SessionId，不能借资料更新修改权限或会话归属。
- UserId 必须有效，存在性与访问权限由后续管理器检查。
- record 只提供数据结构，入口仍须拒绝非法请求。

Get 只需要 UserId，List 只需要 CancellationToken，不添加无意义的请求包装。
