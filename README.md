# 斗战者风扇控制器说明(损坏风扇属于个人行为，与开发者无关)

本文档说明 `BellatorConsole_Setup_V1.1.5.exe` 的逆向分析方式，以及 `BellatorFanControl.exe` 的使用方法。

## 文件位置

- 原始安装包：`D:\download\控制台\BellatorConsole_Setup_V1.1.5.exe`
- 自定义控制器：`D:\download\控制台\BellatorFanControl\BellatorFanControl.exe`
- 控制器源码：`D:\download\控制台\BellatorFanControl\BellatorFanControl.cs`
- PowerShell 版本脚本：`D:\download\控制台\AutoFanControl.ps1`

## 逆向使用的工具

### 1. PowerShell

用于查看安装包签名、哈希、PE 结构和文件信息。

主要用途：

- 校验安装包签名。
- 计算 SHA256。
- 判断安装器是否带有 overlay 数据。
- 编译最终的 WinForms 控制器。

### 2. strings.exe

用于静态扫描安装包和 DLL 字符串。

发现的信息包括：

- 安装包是 Inno Setup。
- 安装包内包含 `.NET 6` WPF 控制台程序。
- 存在 `BLDHotKeyService.exe`、`KaronOC32.dll`、`斗战者控制台.dll` 等关键文件。

### 3. innounp

用于解包 Inno Setup 安装包。

`innoextract 1.9` 对这个安装包的 Inno Setup 6.3.0 格式支持不完整，因此实际成功解包使用的是：

```text
innounp 2.67.9
```

解包后得到主要文件：

```text
斗战者控制台.exe
斗战者控制台.dll
BLDHotKeyService.exe
KaronOC32.dll
InstallService.bat
UninstallService.bat
install_script.iss
```

### 4. ILSpy / ilspycmd

用于反编译 `.NET` 程序。

反编译对象：

```text
D:\download\控制台\unpacked\{app}\斗战者控制台.dll
D:\download\控制台\unpacked\{app}\BLDHotKeyService.exe
```

关键发现：

原厂控制台通过 WMI 调用厂商 ACPI 接口，不是直接写 EC 端口。

核心接口：

```text
Namespace: root\WMI
Class instance: MICommonInterface.InstanceName='ACPI\\PNP0C14\\MIFS_0'
Method: MiInterface
Input: InData byte[32]
Output: OutData byte[]
```

核心协议字段：

```text
InData[1] = 方法类型
250 = Get
251 = Set

InData[3] = 方法编号
8  = SystemPerMode
13 = CPUGPUSYSFanSpeed
20 = MaxFanSpeedSwitch
21 = MaxFanSpeed
22 = CPUThermometer
```

性能模式编号：

```text
0 = 平衡模式
1 = 增强模式
2 = 静音模式
3 = 疯狂模式，也就是原厂斗战模式 / FullspeedMode
```

风扇通道：

```text
FanType 0 = 大风扇控制通道
FanType 1 = 小风扇控制通道
```

风扇转速读取：

```text
CPUGPUSYSFanSpeed 返回：
OutData[4..5]   = 大风扇 1 RPM
OutData[6..7]   = 大风扇 2 RPM
OutData[10..11] = 小风扇 RPM
```

### 5. objdump

用于查看 `KaronOC32.dll` 的导出表和基本 PE 信息。

发现 `KaronOC32.dll` 主要导出：

```text
ChangePstatesLevel0Settings
GetPstatesLevel0Settings
```

该 DLL 更偏向 NVIDIA/功耗相关功能，不是当前风扇控制的主路径。

### 6. csc.exe

使用 Windows 自带 .NET Framework 编译器生成最终 GUI 程序。

编译器路径：

```text
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

程序类型：

```text
WinForms x64 GUI 程序
管理员权限 manifest
```

## BellatorFanControl.exe 功能

`BellatorFanControl.exe` 是一个独立控制器，没有修改原厂控制台程序本体。

主要功能：

- 切换静音、平衡、增强、疯狂模式。
- 显示 CPU 温度、GPU 温度。
- 显示大风扇 1、大风扇 2、小风扇实时转速。
- 自定义风扇曲线。
- 支持拖动曲线点修改温度和转速。
- 支持保存曲线配置。
- 支持恢复固件风扇控制。
- 启动默认显示在桌面右下角，位于任务栏上方。

## 使用方法

### 1. 启动程序

右键运行：

```text
D:\download\控制台\BellatorFanControl\BellatorFanControl.exe
```

建议选择：

```text
以管理员身份运行
```

程序已经内置管理员 manifest，正常情况下会自动弹出 UAC。

### 2. 模式按钮

左侧性能模式区：

```text
静音模式
平衡模式
增强模式
疯狂模式
风扇 + 功率
```

说明：

- `静音模式`：写入原厂 QuietMode。
- `平衡模式`：写入原厂 BalanceMode。
- `增强模式`：写入原厂 PerformanceMode。
- `疯狂模式`：写入原厂斗战模式 / FullspeedMode。
- `风扇 + 功率`：启用自定义风扇曲线，并立即按当前温度写入一次。

### 3. 风扇转速显示

曲线图上方显示：

```text
大风扇 1
大风扇 2
小风扇
```

这些是从厂商 WMI 接口实时读取的 RPM。

### 4. 修改风扇曲线

右侧曲线图支持直接拖动点：

- 拖蓝色曲线点：修改大风扇曲线。
- 拖灰色曲线点：修改小风扇曲线。
- 左右拖动：修改触发温度。
- 上下拖动：修改目标转速。

吸附规则：

```text
温度按 5°C 吸附
转速按 100 RPM 吸附
```

下面表格会和曲线同步变化。

### 5. 表格含义

表格列：

```text
温度 °C
大风扇 x100RPM
小风扇 x100RPM
```

示例：

```text
温度 70
大风扇 35
小风扇 64
```

表示：

```text
当控制温度达到 70°C
大风扇目标约 3500 RPM
小风扇目标约 6400 RPM
```

### 6. 启用自动控制

勾选：

```text
应用自定义风扇曲线
```

程序会定时读取 CPU/GPU 温度，取更高值作为控制温度，然后根据曲线写入风扇上限。

默认参数：

```text
检查间隔：5 秒
降档回差：3°C
```

降档回差用于避免温度在临界点附近时频繁跳档。

### 7. 立即写入一次

点击：

```text
立即按曲线写入一次
```

程序会根据当前温度计算目标转速，并写入一次。

### 8. 恢复固件控制

点击：

```text
恢复固件风扇控制
```

程序会关闭手动风扇上限，让机器回到固件/原厂控制逻辑。

关闭程序时，如果当前启用了自定义曲线，也会尝试恢复固件控制。

### 9. 保存曲线

点击：

```text
保存曲线
```

配置保存到：

```text
%APPDATA%\BellatorFanControl\curve.ini
```

下次启动会自动读取。

## 注意事项

1. 这个程序依赖斗战者原厂 ACPI/WMI 接口。如果原厂相关驱动没有安装或 WMI 接口不存在，无法控制风扇。
2. 程序没有数字签名，Windows 可能提示未知发布者。
3. GPU 温度通过 `nvidia-smi.exe` 读取；如果没有 NVIDIA 独显或 `nvidia-smi` 不可用，GPU 温度会显示 `N/A`。
4. 自定义曲线本质是周期性写入风扇上限，不是直接改 BIOS 内部永久曲线。
5. 风扇控制涉及散热安全。不要把高温区转速设置得过低。

