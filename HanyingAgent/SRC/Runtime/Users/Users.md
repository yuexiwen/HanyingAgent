# Users

[LocalUserManager](./LocalUserManager.cs) 实现 IUserManager，提供资料创建、查询、列举和名称更新，
通过 [IUserStore](./IUserStore.cs) 隔离存储；名称允许重复，更新不改变身份和创建时间。

[LocalUserContext](./LocalUserContext.cs) 在本地可信入口首次使用时创建一个 Local user 资料，
同一容器内并发初始化只创建一次。它不接受模型或命令传入的 UserId，不支持切换身份。
用户资料 API 属于可信进程内管理能力，不是公开多用户端点；资料创建不等于认证。
当前没有登录、密码、角色、远程授权或磁盘持久化。未来多用户入口必须替换身份来源和资料访问政策。
