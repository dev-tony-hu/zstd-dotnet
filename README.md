# ZstdDotnet.NativeAssets

为 .NET 提供 zstd (Zstandard) 原生动态库的打包项目。目标是将预先编译好的 `libzstd` 动态库以 NuGet 包形式分发，方便托管代码直接引用，无需让最终使用者再自行编译 C 源码。

当前版本对应的上游 zstd 版本: **1.5.7**  (请在升级时同步修改 `ZstdDotnet.NativeAssets.csproj` 中的 `<PackageVersion>`)。

## 🎯 项目目标
1. 提供稳定、版本明确的 zstd 原生库给 .NET 项目使用。
2. 简化跨平台发布流程（目前包含 Windows x64 与 Linux x64，后续扩展更多 RID）。
3. 保留上游 License，并在 NuGet 包中携带。

## 📦 NuGet 包结构 (示意)
```
ZstdDotnet.NativeAssets.<version>.nupkg
 ├─ runtimes/
 │   ├─ win-x64/native/libzstd.dll
 │   └─ linux-x64/native/libzstd.so
 ├─ licenses/
 │   ├─ license.txt          (本项目 MIT License)
 │   └─ zstdlicense.txt      (上游 zstd License)
 └─ ... nuspec 元数据
```

消费项目在运行时由 .NET 运行时/加载器根据 RID 自动加载对应的原生库。

## 📁 仓库目录结构
```
root
 ├─ src/ZstdDotnet.NativeAssets/         # NuGet 打包用 .csproj
 ├─ bin/                                 # 需要手工放置已编译的 libzstd 动态库
 │   ├─ libzstd.dll (Windows x64)
 │   └─ libzstd.so  (Linux x64)
 └─ licenses/                            # 许可证
```

`ZstdDotnet.NativeAssets.csproj` 中通过：
```
<None Include="../../bin/libzstd.dll" Pack="true" PackagePath="runtimes/win-x64/native" />
<None Include="../../bin/libzstd.so"  Pack="true" PackagePath="runtimes/linux-x64/native" />
```
将根目录下 `bin` 中的文件打进包里。

## ⚙️ 构建原生 zstd 动态库

你可以选择使用 zstd 官方提供的 `make` / `cmake` 方式。以下为最常见流程。

### 通用前置依赖
- Git
- C 编译工具链
	- Windows: Visual Studio (含 MSVC) 或 LLVM/MinGW
	- Linux: gcc 或 clang
- CMake (推荐 3.18+)

### 1. 获取上游源码
```
git clone https://github.com/facebook/zstd.git
cd zstd
git checkout v1.5.7
```

### 2A. 使用 CMake (推荐)
```
mkdir build && cd build
cmake -DZSTD_BUILD_SHARED=ON -DZSTD_BUILD_STATIC=OFF -DCMAKE_BUILD_TYPE=Release ..
cmake --build . --config Release --target zstd
```
构建完成后：
- Windows: `build\lib\Release\zstd.dll` 或 `build\lib\zstd.dll`
- Linux: `build/lib/libzstd.so`

复制生成文件到本仓库根目录的 `bin/` 并重命名：
```
<repo_root>/bin/libzstd.dll   (Windows)
<repo_root>/bin/libzstd.so    (Linux)
```

### 2B. 使用 make (Linux/macOS)
```
make -C build/cmake install  # 或直接在顶层 make
```
生成的 `libzstd.so` 放入 `<repo_root>/bin/`。

### 验证导出 (可选)
Windows:
```
dumpbin /exports bin\libzstd.dll | more
```
Linux:
```
nm -D bin/libzstd.so | grep ZSTD_version
```

## 🧪 快速打包 NuGet
在仓库根目录：
```
dotnet pack src/ZstdDotnet.NativeAssets/ZstdDotnet.NativeAssets.csproj -c Release -o out
```
生成的 `.nupkg` 位于 `out/` 目录。发布：
```
dotnet nuget push out/ZstdDotnet.NativeAssets.1.5.7.nupkg -k <API_KEY> -s https://api.nuget.org/v3/index.json
```

## 🔗 在项目中使用
在目标项目 `.csproj` 中添加：
```
<ItemGroup>
	<PackageReference Include="ZstdDotnet.NativeAssets" Version="1.5.7" />
</ItemGroup>
```
运行时会自动解析 RID 并加载对应 `libzstd`。

如果你有托管层绑定（例如一个托管 `ZstdDotnet` 包）可通过 `DllImport("libzstd")` 访问。示例：
```csharp
namespace System.IO.Compression.Zstd;

using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class ZstdInterop
{
    // Version captured once; format: (major * 100 * 100 + minor * 100 + patch) as returned by ZSTD_versionNumber
    private static readonly uint VersionNumber = ZSTD_versionNumber();
    // ZSTD_resetCStream introduced in v1.4.0 (per zstd changelog). 1.4.0 -> 1*100*100 + 4*100 + 0 = 10400
    internal static bool SupportsCStreamReset => VersionNumber >= 10400u;    
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern uint ZSTD_versionNumber();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern int ZSTD_maxCLevel();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr ZSTD_createCStream();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_initCStream(IntPtr zcs, int compressionLevel);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_freeCStream(IntPtr zcs);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_CStreamInSize();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_CStreamOutSize();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_compressStream(IntPtr zcs, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer inputBuffer);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr ZSTD_createDStream();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_initDStream(IntPtr zds);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_freeDStream(IntPtr zds);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_DStreamInSize();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_DStreamOutSize();
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_decompressStream(IntPtr zds, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer inputBuffer);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_flushStream(IntPtr zcs, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_endStream(IntPtr zcs, [MarshalAs(UnmanagedType.LPStruct)] ZstdBuffer outputBuffer);
    // Frame inspection (size discovery)
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_findFrameCompressedSize(IntPtr src, UIntPtr srcSize);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern ulong ZSTD_getFrameContentSize(IntPtr src, UIntPtr srcSize);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_getFrameHeader(out ZSTD_frameHeader zfh, IntPtr src, UIntPtr srcSize);
    // Reset APIs (ZSTD v1.4.0+) - More efficient reuse than free/create. Mode constants: 0 = reset session only, 1 = reset parameters, 2 = reset session + params
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_resetCStream(IntPtr zcs, uint resetMode);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr ZSTD_resetDStream(IntPtr zds);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] internal static extern bool ZSTD_isError(UIntPtr code);
    [DllImport("libzstd", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ZSTD_getErrorName(UIntPtr code);
    public static void ThrowIfError(UIntPtr code)
    {
        if (ZSTD_isError(code))
        {
            var errorPtr = ZSTD_getErrorName(code);
            var errorMsg = Marshal.PtrToStringAnsi(errorPtr);
            throw new IOException(errorMsg);
        }
    }

    public static void ResetCStream(IntPtr zcs, bool resetParameters)
    {
        if (zcs == IntPtr.Zero) throw new ArgumentNullException(nameof(zcs));
        if (!SupportsCStreamReset)
            throw new NotSupportedException("libzstd version does not support ZSTD_resetCStream (requires >= 1.4.0)");
        // 0 = session only, 1 = parameters only, 2 = both. We choose 0 or 2 so parameters optionally re-applied by re-init.
        uint mode = resetParameters ? 2u : 0u;
        ThrowIfError(ZSTD_resetCStream(zcs, mode));
    }

    public static void ResetDStream(IntPtr zds)
    {
        if (zds == IntPtr.Zero) throw new ArgumentNullException(nameof(zds));
        ThrowIfError(ZSTD_resetDStream(zds));
    }

    public static ulong FindFrameCompressedSize(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) throw new ArgumentException("Empty span", nameof(data));
        unsafe
        {
            fixed (byte* ptr = data)
            {
                UIntPtr size = ZSTD_findFrameCompressedSize((IntPtr)ptr, (UIntPtr)(uint)data.Length);
                ThrowIfError(size);
                return (ulong)size; // full width (platform dependent); caller treats as ulong
            }
        }
    }

    // Helpers for content size retrieval (may be unknown)
    internal const ulong ZSTD_CONTENTSIZE_UNKNOWN = ulong.MaxValue - 0UL; // per zstd docs: (unsigned long long)(-1)
    internal const ulong ZSTD_CONTENTSIZE_ERROR = ulong.MaxValue - 1UL;   // per zstd docs: (unsigned long long)(-2)

    public static ulong? GetFrameContentSize(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return null;
        unsafe
        {
            fixed (byte* ptr = data)
            {
                ulong v = ZSTD_getFrameContentSize((IntPtr)ptr, (UIntPtr)(uint)data.Length);
                if (v == ZSTD_CONTENTSIZE_ERROR) return null;
                if (v == ZSTD_CONTENTSIZE_UNKNOWN) return null;
                return v; // exact decompressed size
            }
        }
    }

    public static bool TryGetFrameHeader(ReadOnlySpan<byte> data, out ZSTD_frameHeader header)
    {
        header = default;
        if (data.Length < 4) return false; // need at least magic
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var code = ZSTD_getFrameHeader(out header, (IntPtr)ptr, (UIntPtr)(uint)data.Length);
                if (ZSTD_isError(code)) return false; // treat as not ready / invalid
                return true;
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct ZSTD_frameHeader
{
    public ulong frameContentSize; // 0 => unknown
    public ulong windowSize;       // 0 => not referenced
    public uint blockSizeMax;
    public ZSTD_frameType_e frameType;
    public uint headerSize;
    public uint dictID;
    public uint checksumFlag; // 1 if present
}

internal enum ZSTD_frameType_e : uint
{
    ZSTD_frame = 0,
    ZSTD_skippableFrame = 1
}

[StructLayout(LayoutKind.Sequential)]
internal class ZstdBuffer
{
    public IntPtr Data = IntPtr.Zero;
    public UIntPtr Size = UIntPtr.Zero;
    public UIntPtr Position = UIntPtr.Zero;
}


Console.WriteLine($"zstd version: {Native.ZSTD_versionNumber():X}");
```

## 🔄 升级流程（已自动化）
本项目通过 GitHub Actions (`Build Native Package` workflow) 自动：
1. 读取仓库根目录 `VERSION.txt` 中的版本号（例如 `1.5.7`）。
2. 从 GitHub Releases 下载对应 `zstd-${version}.tar.gz`（Linux 源码）与 `zstd-v${version}-win64.zip`（Windows 预编译包）。
3. 在 Linux Runner 上编译（`make`）获取 `libzstd.so`，并从 Windows zip 中提取 `libzstd.dll`，复制到仓库 `bin/`。
4. 执行 `dotnet pack` 生成 nupkg，并使用密钥 (`NUGET_DEPLOY_KEY` secret) 推送到 NuGet，自动跳过已存在版本。

### 升级步骤
1. 修改根目录 `VERSION.txt`，填入新的上游 zstd 版本号（不要带前缀 `v`）。
2. （可选）同步更新 `README.md` 顶部“当前版本”文字。
3. 提交并推送到 `main`。
4. 在 GitHub 仓库 Actions 页，手动触发 `Build Native Package` 的 `workflow_dispatch`（后续可改成 tag 触发）。
5. 等待工作流完成，确认 NuGet 上出现新版本。

### 失败回滚
如果发现新版本有问题：
- 删除问题版本的 NuGet 列表（若还未被大量引用）。
- 还原 `VERSION.txt` 回旧值再触发一次工作流。

### 未来改进（计划）
- 支持基于 Tag 命名（例如 `v1.5.8`）自动触发，而非手动 dispatch。
- 在工作流中自动写入 `PackageVersion`，无需手改 csproj。
- 增加多架构（arm64 等）并行编译矩阵。

## 🚀 规划 / TODO
- [ ] 增加 `linux-arm64` (runtimes/linux-arm64/native)
- [ ] 增加 `osx-x64` 与 `osx-arm64`
- [ ] 增加 `win-arm64`
- [ ] 自动化 CI (GitHub Actions) 交叉编译并生成 nupkg
- [ ] 提供符号文件 (PDB / dbg)

## ❗ 常见问题
| 问题 | 说明 / 解决 |
|------|-------------|
| 运行时报找不到 `libzstd` | 确认目标平台是否在当前包的 RID 支持列表中；确认包已被正确还原。|
| Linux 提示权限或无法加载 | 确认文件有执行权限：`chmod 755 bin/libzstd.so`；发布后在应用输出目录检查。|
| 版本不一致 | 检查编译出来的库 `strings libzstd.so | grep 1.5` 或调用 `ZSTD_versionNumber`。|
| nuget push 失败 | 确认 API Key 是否正确 / 是否已登录；或版本号是否被占用。|

## 📝 License
本项目使用 MIT License；同时包含并再分发上游 zstd License（见 `licenses/` 目录）。

## 🤝 致谢
感谢 Facebook / Meta 以及 zstd 社区的高性能压缩算法实现。

---
如果你需要添加更多平台或希望自动化构建，欢迎提交 Issue 或 PR。