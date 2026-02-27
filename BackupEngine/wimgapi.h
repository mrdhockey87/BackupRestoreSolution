// Minimal wimgapi.h for build compatibility
// This is a stub - install full Windows SDK for production use
#pragma once

#ifndef _WIMGAPI_H_
#define _WIMGAPI_H_

#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

// WIM file access modes
#define WIM_GENERIC_READ            0x80000000
#define WIM_GENERIC_WRITE           0x40000000

// WIM creation/open modes  
#define WIM_CREATE_NEW              1
#define WIM_CREATE_ALWAYS           2
#define WIM_OPEN_EXISTING           3
#define WIM_OPEN_ALWAYS             4

// WIM flags
#define WIM_FLAG_VERIFY             0x00000002
#define WIM_FLAG_INDEX              0x00000004
#define WIM_FLAG_NO_APPLY           0x00000008
#define WIM_FLAG_NO_DIRACL          0x00000010
#define WIM_FLAG_NO_FILEACL         0x00000020
#define WIM_FLAG_SHARE_WRITE        0x00000040
#define WIM_FLAG_REFERENCE          0x00020000  // For incremental backups
#define WIM_FLAG_COMPRESS_FAST      0x00000001  // Fast compression
#define WIM_FLAG_COMPRESS_NONE      0x00000000  // No compression

// WIM compression types
#define WIM_COMPRESS_NONE           0
#define WIM_COMPRESS_XPRESS         1
#define WIM_COMPRESS_LZX            2
#define WIM_COMPRESS_LZMS           3  // Best compression

// WIM messages
#define WIM_MSG_TEXT                0x00000001
#define WIM_MSG_PROGRESS            0x00000002
#define WIM_MSG_PROCESS             0x00000003
#define WIM_MSG_SCANNING            0x00000004
#define WIM_MSG_SETRANGE            0x00000006
#define WIM_MSG_SETPOS              0x00000007
#define WIM_MSG_STEPIT              0x00000008
#define WIM_MSG_COMPRESS            0x00000009
#define WIM_MSG_ERROR               0x0000000A
#define WIM_MSG_ALIGNMENT           0x0000000B
#define WIM_MSG_RETRY               0x0000000C
#define WIM_MSG_SPLIT               0x0000000D
#define WIM_MSG_FILEINFO            0x0000000E
#define WIM_MSG_INFO                0x0000000F
#define WIM_MSG_WARNING             0x00000010
#define WIM_MSG_CHK_PROCESS         0x00000011

// Message return values
#define WIM_MSG_SUCCESS             ERROR_SUCCESS
#define WIM_MSG_DONE                0xFFFFFFF0
#define WIM_MSG_SKIP_ERROR          0xFFFFFFF1
#define WIM_MSG_ABORT_IMAGE         0xFFFFFFF2

// Callback function type
typedef DWORD (WINAPI *WIMMessageCallback)(
    DWORD dwMessageId,
    WPARAM wParam,
    LPARAM lParam,
    PVOID pvUserData
);

// WIM API functions
HANDLE WINAPI WIMCreateFile(
    LPCWSTR pszWimPath,
    DWORD dwDesiredAccess,
    DWORD dwCreationDisposition,
    DWORD dwFlagsAndAttributes,
    DWORD dwCompressionType,
    PDWORD pdwCreationResult
);

BOOL WINAPI WIMCloseHandle(HANDLE hObject);

BOOL WINAPI WIMGetImageCount(HANDLE hWim, PDWORD pdwImageCount);

HANDLE WINAPI WIMLoadImage(HANDLE hWim, DWORD dwImageIndex);

BOOL WINAPI WIMMountImage(
    LPCWSTR pszMountPath,
    LPCWSTR pszWimFileName,
    DWORD dwImageIndex,
    LPCWSTR pszTempPath
);

BOOL WINAPI WIMUnmountImage(
    LPCWSTR pszMountPath,
    LPCWSTR pszWimFileName,
    DWORD dwImageIndex,
    BOOL bCommitChanges
);

HANDLE WINAPI WIMCaptureImage(
    HANDLE hWim,
    LPCWSTR pszPath,
    DWORD dwCaptureFlags
);

DWORD WINAPI WIMRegisterMessageCallback(
    HANDLE hWim,
    WIMMessageCallback fpMessageProc,
    PVOID pvUserData
);

BOOL WINAPI WIMUnregisterMessageCallback(
    HANDLE hWim,
    WIMMessageCallback fpMessageProc
);

BOOL WINAPI WIMSetTemporaryPath(
    HANDLE hWim,
    LPCWSTR pszPath
);

BOOL WINAPI WIMSetImageInformation(
    HANDLE hImage,
    LPCWSTR pszImageInfo
);

#ifdef __cplusplus
}
#endif

#endif // _WIMGAPI_H_
