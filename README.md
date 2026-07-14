# StarRailShaderEditor

一个面向 `MMDStarRail4Fun_v2.1` 的非官方固定管线材质编辑器。它提供节点导航、完整可视参数、只读源码差异预览，以及 `fun_controller.pmx` Morph 预设和帧 0 VMD 导出。

## 项目边界

本仓库只包含编辑器源码和参数元数据，不包含、下载或解包任何 FX、HLSL、PMX、VMD 或纹理文件。程序启动时由用户选择自己合法取得的 `MMDStarRail4Fun` 文件夹，并只在用户明确保存或导出时写入目标内容。

本项目与 MMD、MME、MMDStarRail4Fun 及相关权利人没有隶属或官方合作关系。外部 Shader、模型和纹理不受本仓库 MIT 协议覆盖，使用者需要自行遵守它们各自的授权条款。

## 功能

- 固定 StarRail 渲染管线节点导航，不生成无效自由连线
- 六份角色材质、`Shadow.fx` 和 `Shadow_zbuffer.fx` 的可视参数编辑
- 中文参数说明、常用/完整模式、逐分量范围与纹理诊断
- 只读源码预览、修改行标记和参数源码定位
- 原子保存、最近 10 次备份、撤销与重做
- `fun_controller.pmx` Morph 读取、预设保存和帧 0 VMD 导出
- 便携资源包预检与相对路径导出

## 环境

- Windows 10 或 Windows 11
- .NET 8 SDK
- Visual Studio 2022（可选）

项目只依赖 .NET 8 自带的 WinForms、WPF 和 Windows Desktop Runtime，没有第三方 NuGet 运行时依赖。

## 构建

```powershell
dotnet restore StarRailShaderEditor.csproj
dotnet build StarRailShaderEditor.csproj -c Release
```

## 运行

正常运行会弹出 Shader 文件夹选择器：

```powershell
dotnet run --project StarRailShaderEditor.csproj
```

开发和自动化验证可以通过 `--root` 跳过选择器：

```powershell
dotnet run --project StarRailShaderEditor.csproj -- --root "D:\MMD\MMDStarRail4Fun"
```

## 验证

以下命令要求 `--root` 指向一份完整的本地 Shader 文件夹。测试不会写入原始 FX 或 PMX：

```powershell
dotnet run --project StarRailShaderEditor.csproj -c Release -- --self-test --root "D:\MMD\MMDStarRail4Fun"
dotnet run --project StarRailShaderEditor.csproj -c Release -- --ui-smoke-test --root "D:\MMD\MMDStarRail4Fun"
dotnet run --project StarRailShaderEditor.csproj -c Release -- --benchmark --root "D:\MMD\MMDStarRail4Fun"
```

## 发布

生成自包含的 Windows x64 单文件：

```powershell
dotnet publish StarRailShaderEditor.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o release\win-x64
```

发布前应确认输出目录不含 `.fx`、`.hlsl`、`.pmx`、`.vmd` 或纹理文件。

## 目录

```text
Controls/    WinForms 画布、源码预览和 WPF 参数检查器
Models/      参数、节点、会话与控制器模型
Resources/   嵌入式参数定义
Services/    FX/PMX/VMD 读写、导出和自测
```

详细操作见 [GUIDE.md](GUIDE.md)，界面约束见 [DESIGN.md](DESIGN.md)。

## 作者与鸣谢

- 编辑器作者：**克里斯提亚娜**
- 特别鸣谢：**给你柠檬椰果养乐多你会跟我玩吗**，她负责了配套仿渲 Shader 的编写

配套 Shader 不包含在本仓库中，也不自动适用本仓库的 MIT 协议；其使用和再分发条件以 Shader 作者的单独授权为准。

## License

编辑器源码和本仓库自有参数元数据使用 [MIT License](LICENSE)。
