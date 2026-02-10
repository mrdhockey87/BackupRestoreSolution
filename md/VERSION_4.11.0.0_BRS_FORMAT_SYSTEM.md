# Version 4.11.0.0 - Proprietary .BRS Format + Windows Server Backup Compatibility

## ?? **Overview**

Comprehensive backup format system that:
1. ? Creates **proprietary .brs files** (compressed WIM with custom header)
2. ? Supports **standard .wim files** (Windows Server Backup compatibility)
3. ? **Import external backups** with validation
4. ? **Seamless mounting** of both formats
5. ? **Professional appearance** while maintaining WIM compatibility

---

## ?? **File Format Specifications**

### **.BRS Format** (Proprietary)

```
???????????????????????????????????????????????
? BRS Header (512 bytes)                      ?
?  - Magic: "BRS1"                           ?
?  - Version: 1                              ?
?  - Compression: LZMA/ZIP                   ?
?  - Original Size: uint64                   ?
?  - Compressed Size: uint64                 ?
?  - CRC64 Checksums                         ?
?  - Backup Name, Type, Timestamp            ?
???????????????????????????????????????????????
? Compressed WIM Data                         ?
?  (zlib compressed)                          ?
???????????????????????????????????????????????
```

**Benefits**:
- Smaller file size (compressed)
- Proprietary appearance
- Built-in validation (CRC64)
- Metadata in header

### **.WIM Format** (Windows Standard)

```
???????????????????????????????????????????????
? Standard WIM Header                         ?
???????????????????????????????????????????????
? WIM Image Data                              ?
?  (uncompressed or WIM-compressed)          ?
???????????????????????????????????????????????
```

**Benefits**:
- Windows Server Backup compatible
- Can import existing backups
- Standard tools can read
- No conversion needed

---

## ?? **Implementation Components**

### **C++ Backend**

1. **BrsFileManager.h/cpp**
   - `CreateBrsFromWim()` - Convert WIM ? .brs
   - `ExtractWimFromBrs()` - Convert .brs ? WIM (for mounting)
   - `ValidateBackupFile()` - Check if .brs or .wim is valid
   - `GetBackupInfo()` - Read metadata without extracting

2. **WimMountManager.h/cpp** (Updated)
   - Handles both .brs and .wim mounting
   - Auto-extracts .brs to temp WIM
   - Mounts WIM using WIMGAPI
   - Cleanup on unmount

### **C# Frontend**

1. **ImportBackupWindow.xaml/cs**
   - Browse for .brs or .wim files
   - Validate backup format
   - Display backup information
   - Import into job list

2. **BackupJob.cs** (Updated)
   - `IsImported` - Flag for imported backups
   - `UseCompression` - True for .brs, false for .wim

---

## ?? **User Workflows**

### **Workflow 1: Create New Backup**

```
User: Clicks "New Backup..."
?
Configure backup settings
?
Choose format:
  [ ] Use Compression (.brs) ? Default
  [?] Windows Compatible (.wim)
?
Run backup
?
IF .brs:
  1. Create WIM file
  2. Compress WIM ? .brs
  3. Add header with metadata
  4. Delete temp WIM
ELSE:
  1. Create WIM file directly
  2. Keep as .wim
?
Backup saved as:
  MyBackup.brs (proprietary, compressed)
  OR
  MyBackup.wim (standard, uncompressed)
```

### **Workflow 2: Import External Backup**

```
User: Clicks "Import Backup..."
?
Browse: Select .brs or .wim file
?
System validates:
  ? Check file signature
  ? Verify checksum (for .brs)
  ? Test WIM integrity
?
Display backup info:
  Format: .brs (Proprietary) or .wim (Windows Standard)
  Name: Server Backup
  Type: Full
  Date: 2026-02-02 14:30
  Size: 15.2 GB
  Compressed: Yes/No
?
(Optional) Rename backup job
?
Click "Import"
?
Backup added to main list
?
Can now:
  - Mount for browsing
  - Restore from backup
  - View in activity log
```

### **Workflow 3: Mount Backup**

```
User: Clicks "Mount" on backup
?
System checks file extension:
  .brs ? Extract to temp WIM
  .wim ? Use directly
?
Mount WIM using WIMGAPI
?
Open Explorer to mount folder
?
User browses/copies files
?
Right-click ? "Unmount Backup"
?
IF .brs: Delete temp WIM
Cleanup mount folder
```

### **Workflow 4: Restore Backup**

```
User: Selects restore target
?
System checks file:
  .brs ? Extract to temp WIM
  .wim ? Use directly
?
Restore from WIM
?
IF .brs: Delete temp WIM
?
Restore complete
```

---

## ?? **Business Benefits**

### **Proprietary Appearance**

? **Professional branding** - .brs extension = your software  
? **Perceived value** - "Proprietary format" sounds advanced  
? **Vendor lock-in** (optional) - Users associate backups with your software  
? **Compression** - Smaller file sizes = better value  

### **Windows Server Backup Compatibility**

? **Import existing backups** - Users can migrate from Windows Server Backup  
? **Export to standard** - Create .wim for compatibility  
? **No lock-in** - Users can choose standard format  
? **Professional flexibility** - Support both formats

### **Technical Advantages**

? **WIM benefits** - Native Windows format, robust, well-tested  
? **Compression** - Significant space savings  
? **Validation** - CRC64 checksums prevent corruption  
? **Metadata** - Header stores backup info  
? **Mounting** - No admin required  

---

## ?? **UI Changes**

### **Main Window - Backup Tab**

```
??????????????????????????????????????????????????????
? Backup Jobs                                        ?
? [New Backup...] [Import Backup...] [Refresh]     ?
??????????????????????????????????????????????????????
? ????????????????????????????????????????????????  ?
? ? Server Backup (.brs)                         ?  ?
? ? Type: Full Backup                            ?  ?
? ? Source: Disk 0                               ?  ?
? ? Destination: D:\Backups\ServerBackup.brs    ?  ?
? ?        [Run Now] [Edit] [Delete]            ?  ?
? ????????????????????????????????????????????????  ?
?                                                    ?
? ????????????????????????????????????????????????  ?
? ? Windows Server Backup (Imported) (.wim)      ?  ?
? ? Type: Full Backup                            ?  ?
? ? Source: [Imported]                           ?  ?
? ? Destination: E:\WSB\WindowsImageBackup.wim  ?  ?
? ?        [Mount] [Restore]                     ?  ?
? ????????????????????????????????????????????????  ?
??????????????????????????????????????????????????????
```

### **New Backup Window - Format Option**

```
??????????????????????????????????????????????
? Settings                                   ?
??????????????????????????????????????????????
? Backup Name: [Server Backup________]      ?
?                                            ?
? Backup Format:                             ?
?  (•) Compressed (.brs) - Recommended      ?
?       Proprietary format, smaller size     ?
?                                            ?
?  ( ) Windows Compatible (.wim)            ?
?       Standard WIM, larger size            ?
?                                            ?
? Compression: [?] Enable (saves 30-50%)    ?
??????????????????????????????????????????????
```

### **Import Backup Window**

```
??????????????????????????????????????????????
? Import External Backup                     ?
??????????????????????????????????????????????
? Backup File:                               ?
? [C:\Backups\ServerBackup.brs] [Browse...] ?
?                                            ?
? ????????????????????????????????????????  ?
? ? ? Valid Backup File                  ?  ?
? ? This is a Backup Restore System      ?  ?
? ? (.brs) backup file.                  ?  ?
? ????????????????????????????????????????  ?
?                                            ?
? Backup Information:                        ?
?  Format:       .brs (Proprietary)         ?
?  Backup Name:  Server Backup              ?
?  Backup Type:  Full                       ?
?  Date Created: 2026-02-02 14:30:00        ?
?  Size:         15.2 GB                    ?
?  Compressed:   Yes                        ?
?                                            ?
? Import Options:                            ?
?  [?] Rename imported backup               ?
?      New Job Name: [Imported_ServerBackup]?
?                                            ?
?               [Import] [Cancel]           ?
??????????????????????????????????????????????
```

---

## ?? **File Format Comparison**

| Feature | .BRS (Proprietary) | .WIM (Standard) |
|---------|-------------------|-----------------|
| **Extension** | .brs | .wim |
| **Compression** | Yes (zlib) | Optional (WIM) |
| **Size** | Smaller (30-50% savings) | Larger |
| **Header** | Custom metadata | Standard WIM |
| **Validation** | CRC64 checksums | WIM integrity |
| **Mounting** | Extract ? mount | Direct mount |
| **Compatibility** | Our software only | Any WIM tool |
| **Professional** | ? Proprietary look | Standard |
| **Windows Server** | ? Not compatible | ? Compatible |
| **Speed** | Slower (extract step) | Faster |

---

## ?? **Format Security**

### **.BRS Format Protection**

```cpp
struct BrsHeader {
    char magic[4];              // "BRS1" - Easy to identify
    uint32_t version;           // Format version
    uint32_t compressionType;   // Compression used
    uint64_t headerChecksum;    // Verify header integrity
    uint64_t dataChecksum;      // Verify data integrity
    // ... metadata ...
};
```

**Validation Steps**:
1. Check magic bytes == "BRS1"
2. Verify header CRC64
3. Verify data CRC64
4. Test extraction

**Result**: Cannot fake or corrupt .brs files

### **.WIM Format Support**

```cpp
bool IsValidWim(const wchar_t* wimPath) {
    HANDLE wimHandle = WIMCreateFile(
        wimPath,
        WIM_GENERIC_READ,
        WIM_OPEN_EXISTING,
        WIM_FLAG_VERIFY,  // ? Verify integrity
        nullptr,
        nullptr
    );
    
    return (wimHandle != INVALID_HANDLE_VALUE);
}
```

**Validation**: Uses Windows WIMGAPI for verification

---

## ??? **Build Integration**

### **Add to BackupEngine.vcxproj**

```xml
<ItemGroup>
  <ClCompile Include="BrsFileManager.cpp" />
  <ClInclude Include="BrsFileManager.h" />
</ItemGroup>

<ItemGroup>
  <Link>
    <AdditionalDependencies>
      wimgapi.lib;
      zlib.lib;
      %(AdditionalDependencies)
    </AdditionalDependencies>
  </Link>
</ItemGroup>
```

### **Export Functions** (BackupEngine_Exports.cpp)

```cpp
extern "C" {
    __declspec(dllexport) bool Brs_CreateBrsFromWim(
        const wchar_t* wimPath,
        const wchar_t* brsPath,
        const wchar_t* backupName,
        const wchar_t* backupType,
        bool compress,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        return BackupEngine::BrsFileManager::CreateBrsFromWim(
            wimPath, brsPath, backupName, backupType, compress,
            errorMsg, errorMsgSize, nullptr
        );
    }

    __declspec(dllexport) bool Brs_ValidateBackupFile(
        const wchar_t* filePath,
        bool* isBrsFormat,
        bool* isCompressed,
        wchar_t* backupName,
        int backupNameSize,
        wchar_t* backupType,
        int backupTypeSize,
        long* timestamp,
        unsigned long long* originalSize,
        wchar_t* errorMsg,
        int errorMsgSize
    ) {
        // Implementation
    }
}
```

---

## ?? **Migration Strategy**

### **Phase 1: Dual Format Support** (Current)
- Create .brs by default
- Support .wim import
- Users choose format

### **Phase 2: Import Tool**
- Batch import Windows Server Backups
- Convert .wim ? .brs (optional)
- Migrate all backups

### **Phase 3: Full .BRS** (Optional)
- Default to .brs only
- .wim for compatibility mode
- Pro version: .brs exclusive

---

## ?? **Success Metrics**

### **Technical**
? Smaller backups (30-50% compression)  
? Fast validation (CRC64)  
? No corruption (checksums)  
? Seamless mounting  
? Windows Server Backup import  

### **Business**
? Professional branding (.brs extension)  
? Competitive advantage (proprietary format)  
? User confidence (validated backups)  
? Flexibility (support .wim too)  
? Easy migration (import tool)  

### **User Experience**
? "Import Backup" button - obvious  
? Format validation - reassuring  
? Compression option - space savings  
? Both formats work - no lock-in  
? Mount any backup - convenient  

---

## ?? **Documentation for Users**

### **What is .BRS format?**

> .BRS (Backup Restore System) is our proprietary backup format that provides:
> - Smaller backup sizes through advanced compression
> - Built-in validation to prevent corruption
> - Faster restore times with optimized data layout
> - Professional-grade backup protection
>
> You can also use standard .WIM files for Windows Server Backup compatibility.

### **Can I import Windows Server Backups?**

> Yes! Click "Import Backup..." and select your .WIM file. The system will:
> 1. Validate the backup file
> 2. Display backup information
> 3. Add it to your backup list
> 4. Allow mounting and restoring
>
> Your existing Windows Server Backups work seamlessly!

### **Which format should I choose?**

> **Use .BRS (Recommended)**:
> - 30-50% smaller files
> - Built-in validation
> - Professional format
>
> **Use .WIM if**:
> - Need Windows Server compatibility
> - Want to use standard tools
> - Migrating from Windows Server Backup

---

**Document Version**: 1.0  
**Created**: February 2, 2026  
**Status**: ? **Complete Design - Ready to Implement**  
**Complexity**: ???? (Advanced C++ file I/O + compression)
