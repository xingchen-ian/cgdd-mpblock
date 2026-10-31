# MPBlocks (`com.cgdd.mpblocks`)

Unity **2022.3** 的多人游戏积木库（Netcode for GameObjects 1.15.1），用于 CGDD 课程与局域网展会。
本仓库**只包含 UPM 包**（Runtime/Editor）；教学场景与模板由包里的菜单在各自工程中生成。

## 学生安装（已有 Unity 工程，Mode B）

1. Unity 2022.3.62f3c1 打开你的工程；
2. `Window > Package Manager` → 左上 `+` → **Add package from git URL...**，粘贴：

   ```
   https://github.com/xingchen-ian/cgdd-mpblock.git#v0.3.0
   ```

   或直接编辑 `Packages/manifest.json`：

   ```json
   {
     "dependencies": {
       "com.cgdd.mpblocks": "https://github.com/xingchen-ian/cgdd-mpblock.git#v0.3.0"
     }
   }
   ```

3. 依赖 `com.unity.netcode.gameobjects 1.15.1`、`com.unity.ugui 1.0.0` 会自动解析；
4. 编译完成后，顶部出现 **`MPBlocks`** 菜单。

> 需要系统装了 git；仓库是 public 的不需要登录。国内网络不稳时可用 Gitee 镜像，URL 写法相同。

## 安装后生成教学内容

包里没有场景和 prefab（这是设计约束），第一次用菜单生成：

| 菜单 | 作用 |
|---|---|
| `MPBlocks > Generate Teaching Templates` | 生成 Bootstrap 演示场景 + 三张教学模板 + 共享 prefab + NetConfig |
| `MPBlocks > New Game Scene...` | 生成你自己的多人游戏场景（独立 NetConfig，自动排到 Build Settings 第一位） |
| `MPBlocks > Setup Bootstrap Scene` | 只重建 Bootstrap 演示场景 |
| `MPBlocks > Refresh Project Setup` | 补齐共享 prefab / NetConfig 字段（不重建场景） |

生成的资产都在**你的** `Assets/` 下，可以随意修改；包本体在 `Library/PackageCache` 中只读。

## 包内容

```
Runtime/Core/          NetConfig, NetBootstrap, NetSessionUI, PlayerSpawnPoint
Runtime/Sync/          SyncedTransform, SyncedBool/Int/Float, NetEvent
Runtime/Gameplay/      Health, Interactable, CoopDoor, Scoreboard, Spawner,
                       InteractToggle, PlayerShooter, Projectile, PlayerRespawn,
                       SimplePlayerMover, RigidbodyPlayerMover, ...
Runtime/Diagnostics/   LatencySimulator, NetDiag, StateHash
Editor/                场景/prefab 生成器、BlockValidator、MPBlocks 菜单
```

## 版本与更新

- `package.json` 的 `version` 与 git tag 保持一致（当前 `v0.3.0`）；
- 更新包：把 `Packages/manifest.json` 里的 tag 换成新版本号，或 Package Manager 中重新添加；
- 包里的 `.meta` 文件会随仓库提交，场景/prefab 引用在升级后仍然有效。

## 注意

- 本仓库不是完整的 Unity 工程，不能直接用 Unity Hub 打开；它只通过 Package Manager / git URL 被引用。
- 课程用的完整工程（场景、模板、`tools/` 构建与验收脚本）在教师仓库，不在此处。
