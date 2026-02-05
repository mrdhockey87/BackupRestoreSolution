# BRS Format System - Build Dependencies

## Required Libraries

### 1. Windows Imaging API (WIMGAPI)
- **Header**: `wimgapi.h`
- **Library**: `wimgapi.lib`
- **Location**: Windows SDK
- **Installation**: Included with Windows SDK (already installed for Windows development)

### 2. zlib Compression Library
- **Header**: `zlib.h`
- **Library**: `zlib.lib`
- **Installation Options**:

#### Option A: vcpkg (Recommended)
```cmd
vcpkg install zlib:x64-windows
vcpkg integrate install
```

#### Option B: NuGet Package
```xml
<ItemGroup>
  <PackageReference Include="zlib-msvc-x64" Version="1.2.11.8900" />
</ItemGroup>
```

#### Option C: Manual Download
1. Download from https://www.zlib.net/
2. Extract to `ThirdParty/zlib`
3. Add to project include/lib paths

## Project Configuration

### BackupEngine.vcxproj

```xml
<PropertyGroup>
  <IncludePath>$(VC_IncludePath);$(WindowsSDK_IncludePath);$(VCPKG_ROOT)\installed\x64-windows\include</IncludePath>
  <LibraryPath>$(VC_LibraryPath_x64);$(WindowsSDK_LibraryPath_x64);$(VCPKG_ROOT)\installed\x64-windows\lib</LibraryPath>
</PropertyGroup>

<ItemGroup>
  <Link>
    <AdditionalDependencies>wimgapi.lib;zlib.lib;%(AdditionalDependencies)</AdditionalDependencies>
  </Link>
</ItemGroup>
```

## Quick Fix (If Libraries Not Available)

If you don't have these libraries yet, you can:

1. **Comment out BRS files temporarily**:
   - Comment out BrsFileManager.h/cpp includes
   - Use only .wim format for now
   - Add .brs support later

2. **Install dependencies**:
   ```cmd
   # Install vcpkg if not installed
   git clone https://github.com/Microsoft/vcpkg.git
   cd vcpkg
   bootstrap-vcpkg.bat
   
   # Install zlib
   vcpkg install zlib:x64-windows
   vcpkg integrate install
   ```

3. **Rebuild project**:
   - Libraries will be automatically linked
   - BRS format will work

## Current Build Status

The project will build **without** .brs format if:
- BrsFileManager files are excluded from build
- Only .wim support is used
- Import feature validates .wim files only

To enable full .brs support:
- Install dependencies above
- Include BRS files in build
- Full functionality will work
