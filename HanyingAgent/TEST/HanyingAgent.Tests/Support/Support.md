# Support

## 功能与定位

存放测试专用辅助设施，不作为生产服务或公共契约。

## 当前组件

| 文件 | 职责 |
|---|---|
| [ManualTimeProvider.cs](./ManualTimeProvider.cs) | 继承 TimeProvider，通过 GetUtcNow 返回指定时刻。 |
| [ManualTimeProviderTests.cs](./ManualTimeProviderTests.cs) | 验证 UTC 归一化、固定读取、精确推进、零推进、拒绝负推进和时钟回退。 |

## 时钟约定

- 需要控制时间的 Manager 测试创建自己的时钟并通过构造注入；生产组合使用 TimeProvider.System。
- Advance 只允许非负时长；SetUtcNow 可以回退，用于验证 UpdatedAt 不倒退。
- 时钟读写使用短锁，避免并发测试读取到部分更新时间。
- 仅控制 GetUtcNow，不控制计时器、单调时间戳、Task.Delay 或后台调度。
- 日期超范围等错误直接抛出，不静默修正。
