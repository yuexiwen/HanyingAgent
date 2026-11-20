# Requests

## 功能与定位

定义会话管理操作的公共输入，不限定本地调用或远程传输方式。

当前无子目录，只有 [CreateSessionRequest](./CreateSessionRequest.cs)，通过 UserId 指定所属用户，通过 Name 指定会话显示名称。

## 设计边界

- Name 必须非空白，允许与其他会话重名；会话身份由生成的 SessionId 决定。
- UserId 必须有效且指向已有用户，由可信入口提供并校验创建权限，不由模型决定归属。
- 请求不接受客户端指定 SessionId、状态、CurrentTaskId 或时间戳。
- 创建请求不会自动启动 Agent Task。
- JSON 缺少 UserId 时显式报错，不兼容只有 Name 的旧创建请求；调用方需要传入明确的所属用户。
- DTO 不做完整名称、默认 ID 或用户存在性校验，后续 ISessionManager 实现必须显式拒绝无效输入。
- 获取和关闭只需要 SessionId，列举没有额外参数，不为它们创建无意义的请求包装。
