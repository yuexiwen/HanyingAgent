# Operations

## 功能与定位

定义独立的用户资料管理协议，不负责登录、授权或 Session 归属。

## 能力接口

[IUserManager](./IUserManager.cs) 提供：

| 操作 | 行为约定 |
|---|---|
| CreateAsync | 接受非空白显示名称，分配新的 UserId，返回创建后的 UserInfo。 |
| GetAsync | 返回指定用户的资料快照，不存在时返回 null。 |
| ListAsync | 返回当前调用方允许读取的全部用户资料快照，第一版不分页。 |
| UpdateAsync | 替换已存在用户的显示名称，返回最新快照，未知用户显式报错，不自动创建。 |

## 直属子模块

| 子模块 | 一句话概述 |
|---|---|
| [Requests](./Requests/Requests.md) | 定义创建资料和更新显示名称的输入。 |

## 行为与错误约定

- 空请求、非法默认 ID、空白名称显式报输入错误，不静默使用默认名称。
- 名称允许重名，不通过名称查询并复用已有用户；每次成功 Create 都创建新身份。
- List 返回非 null 列表，没有可见用户时返回空列表；按 UpdatedAt 降序、Id.Value 的 Ordinal 升序排序。
- Get 和 List 不更新资料时间；Update 不修改 Id、CreatedAt、用户级记忆或任何 Session。
- 显示名称相同的 Update 是无变化操作，可原样返回；实际变更与时间更新需要原子保存。
- 更新没有 ExpectedVersion，是无条件的名称替换，不提供过期快照冲突检测。
- 权限不足与未知更新目标需要显式报错，不能把请求中的 UserId 当作调用者身份。
- CancellationToken 控制本次调用或等待，不保证撤销已经保存的变更；创建没有幂等键，超时后不能盲目重试。

这些行为仍待具体管理器实现。直接返回 UserInfo 或列表，不建立没有用途的 Responses 目录。
