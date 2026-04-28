#include "..\BackupEngine\BackupEngine.h"
#include <iostream>
#include <string>

namespace
{
    int g_progressCalls = 0;
    std::wstring g_lastMessage;

    void ProgressRecorder(int percentage, const wchar_t* message)
    {
        ++g_progressCalls;
        g_lastMessage = message == nullptr ? L"" : message;
        (void)percentage;
    }

    bool Expect(bool condition, const char* message)
    {
        if (!condition)
        {
            std::cerr << "FAILED: " << message << std::endl;
            return false;
        }

        return true;
    }
}

int wmain()
{
    bool allPassed = true;

    int major = 0;
    int minor = 0;
    int build = 0;
    allPassed &= Expect(GetWindowsVersion(&major, &minor, &build) == 0, "GetWindowsVersion should succeed with valid pointers");
    allPassed &= Expect(major > 0, "GetWindowsVersion should populate major version");

    allPassed &= Expect(GetWindowsVersion(nullptr, &minor, &build) != 0, "GetWindowsVersion should reject null major pointer");

    wchar_t errorBuffer[256] = {};
    GetLastErrorMessage(errorBuffer, 256);
    allPassed &= Expect(errorBuffer[0] != L'\0', "GetLastErrorMessage should return an error after invalid call");

    bool isBootVolume = false;
    int bootResult = IsBootVolume(L"", &isBootVolume);
    allPassed &= Expect(bootResult <= 0 || !isBootVolume, "IsBootVolume should fail cleanly or avoid marking an empty path as bootable.");

    wchar_t volumeBuffer[8] = {};
    int enumerateResult = EnumerateVolumes(volumeBuffer, 8);
    allPassed &= Expect(enumerateResult <= 0 || volumeBuffer[0] != L'\0', "EnumerateVolumes should either fail cleanly or populate buffer");

    std::wcout << L"Progress callback calls observed: " << g_progressCalls << std::endl;
    return allPassed ? 0 : 1;
}
