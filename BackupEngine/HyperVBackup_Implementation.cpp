// HyperVBackup_Implementation.cpp
// Complete implementation of Hyper-V VM backup and cloning

#include "BackupEngine.h"
#include <Windows.h>
#include <comdef.h>
#include <Wbemidl.h>
#include <atlbase.h>
#include <string>
#include <vector>
#include <filesystem>
#include <fstream>
#include <shlwapi.h>
#include <cwchar>
#include <vector>

#pragma comment(lib, "wbemuuid.lib")
#pragma comment(lib, "shlwapi.lib")

namespace fs = std::filesystem;

extern void SetLastErrorMessage(const std::wstring& error);

namespace {
	struct HyperVBackupPointInfo {
		std::wstring Type;
		std::wstring PointId;
		std::wstring ParentPath;
		std::wstring VmName;
		std::wstring ExportPath;
		bool IsValid = false;
	};

	constexpr wchar_t HyperVMetadataFileName[] = L"hyperv_backup_info.txt";
	constexpr wchar_t OfflineSystemHiveName[] = L"OFFLINE_SYSTEM";

		std::wstring TrimWhitespace(const std::wstring& value) {
			size_t start = value.find_first_not_of(L" \t\r\n");
			if (start == std::wstring::npos) {
				return L"";
			}

			size_t end = value.find_last_not_of(L" \t\r\n");
			return value.substr(start, end - start + 1);
		}

	bool EnablePrivilege(const wchar_t* privilegeName) {
		if (privilegeName == nullptr || *privilegeName == L'\0') {
			return false;
		}

		HANDLE tokenHandle = nullptr;
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &tokenHandle)) {
			return false;
		}

		TOKEN_PRIVILEGES privileges = {};
		bool success = false;
		if (LookupPrivilegeValueW(nullptr, privilegeName, &privileges.Privileges[0].Luid)) {
			privileges.PrivilegeCount = 1;
			privileges.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
			if (AdjustTokenPrivileges(tokenHandle, FALSE, &privileges, sizeof(privileges), nullptr, nullptr) && GetLastError() == ERROR_SUCCESS) {
				success = true;
			}
		}

		CloseHandle(tokenHandle);
		return success;
	}

	std::wstring GetRegistryErrorMessage(const wchar_t* operation, LONG status) {
		LPWSTR buffer = nullptr;
		DWORD flags = FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS;
		std::wstring message = operation == nullptr ? L"Registry operation failed" : std::wstring(operation);

		if (FormatMessageW(flags, nullptr, static_cast<DWORD>(status), 0, reinterpret_cast<LPWSTR>(&buffer), 0, nullptr) != 0 && buffer != nullptr) {
			message += L": ";
			message += TrimWhitespace(buffer);
			LocalFree(buffer);
		}
		else {
			message += L". Error code: ";
			message += std::to_wstring(status);
		}

		return message;
	}

	std::wstring GetTimestampId() {
		SYSTEMTIME localTime = {};
		GetLocalTime(&localTime);

		wchar_t buffer[32] = {};
		swprintf_s(
			buffer,
			L"%04u%02u%02u_%02u%02u%02u",
			localTime.wYear,
			localTime.wMonth,
			localTime.wDay,
			localTime.wHour,
			localTime.wMinute,
			localTime.wSecond);

		return buffer;
	}

	std::wstring CombinePath(const std::wstring& root, const std::wstring& child) {
		return (fs::path(root) / child).wstring();
	}

	std::wstring GetMetadataPath(const std::wstring& backupPointPath) {
		return CombinePath(backupPointPath, HyperVMetadataFileName);
	}

	std::wstring GetExportRootPath(const std::wstring& backupPointPath) {
		return CombinePath(backupPointPath, L"Export");
	}

	bool IsHyperVBackupPointDirectory(const fs::path& path) {
		if (!fs::is_directory(path)) {
			return false;
		}

		std::error_code ec;
		return fs::exists(path / HyperVMetadataFileName, ec);
	}

	bool TryReadHyperVBackupPointInfo(const std::wstring& backupPointPath, HyperVBackupPointInfo& info) {
		std::wifstream stream(GetMetadataPath(backupPointPath));
		if (!stream.is_open()) {
			return false;
		}

		info = {};
		info.ExportPath = GetExportRootPath(backupPointPath);

		std::wstring line;
		while (std::getline(stream, line)) {
			size_t separatorIndex = line.find(L'=');
			if (separatorIndex == std::wstring::npos) {
				continue;
			}

			std::wstring key = TrimWhitespace(line.substr(0, separatorIndex));
			std::wstring value = TrimWhitespace(line.substr(separatorIndex + 1));

			if (_wcsicmp(key.c_str(), L"Type") == 0) {
				info.Type = value;
			}
			else if (_wcsicmp(key.c_str(), L"PointId") == 0) {
				info.PointId = value;
			}
			else if (_wcsicmp(key.c_str(), L"ParentPath") == 0) {
				info.ParentPath = value;
			}
			else if (_wcsicmp(key.c_str(), L"VmName") == 0) {
				info.VmName = value;
			}
		}

		info.IsValid = !info.Type.empty() && !info.PointId.empty();
		return info.IsValid;
	}

	bool WriteHyperVBackupPointInfo(
		const std::wstring& backupPointPath,
		const std::wstring& backupType,
		const std::wstring& pointId,
		const std::wstring& vmName,
		const std::wstring& parentPath) {

		std::wofstream stream(GetMetadataPath(backupPointPath), std::ios::trunc);
		if (!stream.is_open()) {
			return false;
		}

		stream << L"Type=" << backupType << L'\n';
		stream << L"PointId=" << pointId << L'\n';
		stream << L"VmName=" << vmName << L'\n';
		stream << L"ExportPath=" << GetExportRootPath(backupPointPath) << L'\n';
		if (!parentPath.empty()) {
			stream << L"ParentPath=" << parentPath << L'\n';
		}

		return stream.good();
	}

	std::wstring FindNewestHyperVBackupPoint(const std::wstring& backupRootPath) {
		if (!fs::exists(backupRootPath) || !fs::is_directory(backupRootPath)) {
			return L"";
		}

		std::vector<fs::directory_entry> entries;
		for (const auto& entry : fs::directory_iterator(backupRootPath)) {
			if (IsHyperVBackupPointDirectory(entry.path())) {
				entries.push_back(entry);
			}
		}

		if (entries.empty()) {
			return L"";
		}

		std::sort(
			entries.begin(),
			entries.end(),
			[](const fs::directory_entry& left, const fs::directory_entry& right) {
				return fs::last_write_time(left) > fs::last_write_time(right);
			});

		return entries.front().path().wstring();
	}

	std::wstring FindLatestHyperVBackupPointByType(const std::wstring& backupRootPath, const std::wstring& desiredType) {
		if (!fs::exists(backupRootPath) || !fs::is_directory(backupRootPath)) {
			return L"";
		}

		std::vector<fs::directory_entry> entries;
		for (const auto& entry : fs::directory_iterator(backupRootPath)) {
			if (!IsHyperVBackupPointDirectory(entry.path())) {
				continue;
			}

			HyperVBackupPointInfo info;
			if (TryReadHyperVBackupPointInfo(entry.path().wstring(), info) && _wcsicmp(info.Type.c_str(), desiredType.c_str()) == 0) {
				entries.push_back(entry);
			}
		}

		if (entries.empty()) {
			return L"";
		}

		std::sort(
			entries.begin(),
			entries.end(),
			[](const fs::directory_entry& left, const fs::directory_entry& right) {
				return fs::last_write_time(left) > fs::last_write_time(right);
			});

		return entries.front().path().wstring();
	}

	int ExecuteHyperVBackupPoint(
		const wchar_t* vmName,
		const wchar_t* backupRootPath,
		const std::wstring& backupType,
		const std::wstring& parentPath,
		ProgressCallback callback);

	class OfflineHiveScope {
	public:
		OfflineHiveScope() = default;

		~OfflineHiveScope() {
			Unload();
		}

		bool Load(const std::wstring& hivePath) {
			if (hivePath.empty()) {
				SetLastErrorMessage(L"The offline SYSTEM hive path is empty.");
				return false;
			}

			if (!EnablePrivilege(SE_RESTORE_NAME) || !EnablePrivilege(SE_BACKUP_NAME)) {
				SetLastErrorMessage(L"Failed to enable the privileges required to load the offline SYSTEM hive.");
				return false;
			}

			LONG unloadStatus = RegUnLoadKeyW(HKEY_LOCAL_MACHINE, OfflineSystemHiveName);
			if (unloadStatus != ERROR_SUCCESS && unloadStatus != ERROR_FILE_NOT_FOUND) {
				SetLastErrorMessage(GetRegistryErrorMessage(L"Failed to unload an existing OFFLINE_SYSTEM hive", unloadStatus));
				return false;
			}

			LONG loadStatus = RegLoadKeyW(HKEY_LOCAL_MACHINE, OfflineSystemHiveName, hivePath.c_str());
			if (loadStatus != ERROR_SUCCESS) {
				SetLastErrorMessage(GetRegistryErrorMessage(L"Failed to load the offline SYSTEM hive as OFFLINE_SYSTEM", loadStatus));
				return false;
			}

			_loaded = true;
			return true;
		}

		bool Unload() {
			if (!_loaded) {
				return true;
			}

			LONG unloadStatus = RegUnLoadKeyW(HKEY_LOCAL_MACHINE, OfflineSystemHiveName);
			if (unloadStatus != ERROR_SUCCESS) {
				SetLastErrorMessage(GetRegistryErrorMessage(L"Failed to unload the OFFLINE_SYSTEM hive", unloadStatus));
				return false;
			}

			_loaded = false;
			return true;
		}

	private:
		bool _loaded = false;
	};

}

extern "C" BACKUPENGINE_API int ScheduleOfflineSystemSetupCl(const wchar_t* systemHivePath) {
	if (systemHivePath == nullptr || *systemHivePath == L'\0') {
		SetLastErrorMessage(L"The offline SYSTEM hive path is required.");
		return -1;
	}

	try {
		std::wstring hivePath(systemHivePath);
		if (!fs::exists(hivePath) || fs::is_directory(hivePath)) {
			SetLastErrorMessage(L"The offline SYSTEM hive path does not exist or is not a file.");
			return -2;
		}

		OfflineHiveScope hiveScope;
		if (!hiveScope.Load(hivePath)) {
			return -3;
		}

		HKEY pendingRequestKey = nullptr;
		DWORD disposition = 0;
		LONG status = RegCreateKeyExW(
			HKEY_LOCAL_MACHINE,
			L"OFFLINE_SYSTEM\\Setup\\SetupCl\\PendingRequest",
			0,
			nullptr,
			REG_OPTION_NON_VOLATILE,
			KEY_QUERY_VALUE | KEY_SET_VALUE,
			nullptr,
			&pendingRequestKey,
			&disposition);

		if (status != ERROR_SUCCESS || pendingRequestKey == nullptr) {
			SetLastErrorMessage(GetRegistryErrorMessage(L"Failed to open OFFLINE_SYSTEM\\Setup\\SetupCl\\PendingRequest", status));
			hiveScope.Unload();
			return -4;
		}

		DWORD operationFlags = 0x00000004;
		status = RegSetValueExW(
			pendingRequestKey,
			L"OperationFlags",
			0,
			REG_DWORD,
			reinterpret_cast<const BYTE*>(&operationFlags),
			static_cast<DWORD>(sizeof(operationFlags)));

		if (status != ERROR_SUCCESS) {
			RegCloseKey(pendingRequestKey);
			SetLastErrorMessage(GetRegistryErrorMessage(L"Failed to set SetupCl OperationFlags", status));
			hiveScope.Unload();
			return -5;
		}

		status = RegDeleteValueW(pendingRequestKey, L"SidAccountDomainNew");
		if (status != ERROR_SUCCESS && status != ERROR_FILE_NOT_FOUND) {
			RegCloseKey(pendingRequestKey);
			SetLastErrorMessage(GetRegistryErrorMessage(L"Failed to remove SetupCl SidAccountDomainNew", status));
			hiveScope.Unload();
			return -6;
		}

		RegFlushKey(pendingRequestKey);
		RegCloseKey(pendingRequestKey);

		if (!hiveScope.Unload()) {
			return -7;
		}

		return 0;
	}
	catch (const std::exception& ex) {
		SetLastErrorMessage(L"Failed to schedule SetupCl in the offline SYSTEM hive.");
		return -99;
	}
	catch (...) {
		SetLastErrorMessage(L"An unknown error occurred while scheduling SetupCl in the offline SYSTEM hive.");
		return -100;
	}
}

// Helper to execute WMI method
HRESULT ExecuteWMIMethod(IWbemServices* pSvc, const std::wstring& objectPath,
	const std::wstring& methodName, IWbemClassObject* pInParams,
	IWbemClassObject** ppOutParams) {

	return pSvc->ExecMethod(
		CComBSTR(objectPath.c_str()),
		CComBSTR(methodName.c_str()),
		0,
		NULL,
		pInParams,
		ppOutParams,
		NULL);
}

std::wstring GetWmiErrorMessage(HRESULT hr) {
	_com_error error(hr);
	const wchar_t* description = error.ErrorMessage();
	if (description == nullptr || *description == L'\0') {
		return L"Unknown WMI error";
	}

	return description;
}

bool GetHyperVExportSettingInstance(
	IWbemServices* pSvc,
	const std::wstring& vmPath,
	IWbemClassObject** ppSettingInstance,
	std::wstring& errorMessage) {

	if (!pSvc || !ppSettingInstance || vmPath.empty()) {
		errorMessage = L"Hyper-V export setting lookup parameters are invalid.";
		return false;
	}

	*ppSettingInstance = nullptr;

	std::wstring query = L"ASSOCIATORS OF {" + vmPath + L"} WHERE ResultClass=Msvm_VirtualSystemExportSettingData";
	CComPtr<IEnumWbemClassObject> pEnumerator;
	HRESULT hr = pSvc->ExecQuery(
		CComBSTR(L"WQL"),
		CComBSTR(query.c_str()),
		WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
		NULL,
		&pEnumerator);

	if (FAILED(hr)) {
		errorMessage = L"Failed to query Hyper-V export settings: " + GetWmiErrorMessage(hr);
		return false;
	}

	ULONG uReturn = 0;
	CComPtr<IWbemClassObject> pSettingInstance;
	hr = pEnumerator->Next(WBEM_INFINITE, 1, &pSettingInstance, &uReturn);
	if (FAILED(hr) || uReturn == 0 || !pSettingInstance) {
		errorMessage = L"Failed to get Hyper-V export settings for the virtual machine.";
		return false;
	}

	*ppSettingInstance = pSettingInstance.Detach();
	return true;
}

bool BuildHyperVExportSettingData(
	IWbemServices* pSvc,
	const std::wstring& vmPath,
	const std::wstring& backupType,
	bool isVmRunning,
	const std::wstring& snapshotPath,
	std::wstring& exportSettingData,
	std::wstring& errorMessage) {

	CComPtr<IWbemClassObject> pSettingInstance;
	if (!GetHyperVExportSettingInstance(pSvc, vmPath, &pSettingInstance, errorMessage)) {
		return false;
	}

	HRESULT hr = S_OK;

	CComVariant varCopyVmStorage(VARIANT_TRUE);
	hr = pSettingInstance->Put(L"CopyVmStorage", 0, &varCopyVmStorage, 0);
	if (FAILED(hr)) {
		errorMessage = L"Failed to set CopyVmStorage export setting: " + GetWmiErrorMessage(hr);
		return false;
	}

	CComVariant varCopyRuntime(isVmRunning ? VARIANT_TRUE : VARIANT_FALSE);
	hr = pSettingInstance->Put(L"CopyVmRuntimeInformation", 0, &varCopyRuntime, 0);
	if (FAILED(hr)) {
		errorMessage = L"Failed to set CopyVmRuntimeInformation export setting: " + GetWmiErrorMessage(hr);
		return false;
	}

	CComVariant varCaptureLiveState(static_cast<unsigned char>(0));
	hr = pSettingInstance->Put(L"CaptureLiveState", 0, &varCaptureLiveState, 0);
	if (FAILED(hr)) {
		errorMessage = L"Failed to set CaptureLiveState export setting: " + GetWmiErrorMessage(hr);
		return false;
	}

	// CopySnapshotConfiguration:
	//   0 = ExportAllSnapshots
	//   1 = ExportNoSnapshots
	//   2 = ExportOneSnapshot           - use for Full backups of running VMs with a snapshot
	//   3 = ExportOneSnapshotForBackup  - use for Incremental/Differential backups of running VMs
	//
	// When a VM is running it holds an exclusive lock on its differencing AVHDX.
	// CopySnapshotConfiguration = 1 (ExportNoSnapshots) forces Hyper-V to merge the AVHDX chain
	// inline during export, which fails with 0x80070020 because the running VM has it locked.
	// Any running VM that has a current snapshot must use snapshot-based export to avoid this.
	// Stopped VMs have no AVHDX lock so ExportNoSnapshots (1) is safe.
	const bool isRunningWithSnapshot = isVmRunning && !snapshotPath.empty();
	const bool isIncrementalOrDifferential =
		_wcsicmp(backupType.c_str(), L"Incremental") == 0 ||
		_wcsicmp(backupType.c_str(), L"Differential") == 0;

	unsigned char copySnapshotConfiguration;
	if (isRunningWithSnapshot && isIncrementalOrDifferential) {
		copySnapshotConfiguration = 3;  // ExportOneSnapshotForBackup
	}
	else if (isRunningWithSnapshot) {
		copySnapshotConfiguration = 2;  // ExportOneSnapshot (Full backup of running VM)
	}
	else {
		copySnapshotConfiguration = 1;  // ExportNoSnapshots (stopped VM, or running with no snapshot)
	}

	CComVariant varCopySnapshotConfiguration(copySnapshotConfiguration);
	hr = pSettingInstance->Put(L"CopySnapshotConfiguration", 0, &varCopySnapshotConfiguration, 0);
	if (FAILED(hr)) {
		errorMessage = L"Failed to set CopySnapshotConfiguration export setting: " + GetWmiErrorMessage(hr);
		return false;
	}

	if (isRunningWithSnapshot) {
		CComVariant varSnapshotVirtualSystem(snapshotPath.c_str());
		hr = pSettingInstance->Put(L"SnapshotVirtualSystem", 0, &varSnapshotVirtualSystem, 0);
		if (FAILED(hr)) {
			errorMessage = L"Failed to set SnapshotVirtualSystem export setting: " + GetWmiErrorMessage(hr);
			return false;
		}
	}

	CComVariant varCreateSubdirectory(VARIANT_TRUE);
	hr = pSettingInstance->Put(L"CreateVmExportSubdirectory", 0, &varCreateSubdirectory, 0);
	if (FAILED(hr)) {
		errorMessage = L"Failed to set CreateVmExportSubdirectory export setting: " + GetWmiErrorMessage(hr);
		return false;
	}

	if (_wcsicmp(backupType.c_str(), L"Incremental") == 0) {
		CComVariant varBackupIntent(static_cast<unsigned char>(0));
		hr = pSettingInstance->Put(L"BackupIntent", 0, &varBackupIntent, 0);
		if (FAILED(hr)) {
			errorMessage = L"Failed to set BackupIntent export setting: " + GetWmiErrorMessage(hr);
			return false;
		}
	}
	else if (_wcsicmp(backupType.c_str(), L"Differential") == 0) {
		CComVariant varBackupIntent(static_cast<unsigned char>(1));
		hr = pSettingInstance->Put(L"BackupIntent", 0, &varBackupIntent, 0);
		if (FAILED(hr)) {
			errorMessage = L"Failed to set BackupIntent export setting: " + GetWmiErrorMessage(hr);
			return false;
		}
	}
	// Full backup: do not set BackupIntent - leave unset (default behavior, matches original working full exports)

	// Use IWbemObjectTextSrc with WMI DTD 2.0 format instead of GetObjectText() (MOF).
	// GetObjectText() produces full MOF with qualifiers/system properties that ExportSystemDefinition rejects (error 32773).
	CComPtr<IWbemObjectTextSrc> pTextSrc;
	hr = CoCreateInstance(
		CLSID_WbemObjectTextSrc,
		nullptr,
		CLSCTX_INPROC_SERVER,
		IID_IWbemObjectTextSrc,
		reinterpret_cast<void**>(&pTextSrc));

	if (FAILED(hr) || !pTextSrc) {
		errorMessage = L"Failed to create WMI object text source: " + GetWmiErrorMessage(hr);
		return false;
	}

	BSTR bstrObjectText = NULL;
	hr = pTextSrc->GetText(0, pSettingInstance, WMI_OBJ_TEXT_WMI_DTD_2_0, nullptr, &bstrObjectText);
	if (FAILED(hr) || bstrObjectText == NULL) {
		if (bstrObjectText != NULL) {
			SysFreeString(bstrObjectText);
		}

		errorMessage = L"Failed to serialize export setting data: " + GetWmiErrorMessage(hr);
		return false;
	}

	exportSettingData.assign(bstrObjectText, SysStringLen(bstrObjectText));
	SysFreeString(bstrObjectText);
	return true;
}

bool IsVmRunning(IWbemClassObject* pVM, bool& isRunning) {
	if (!pVM) {
		return false;
	}

	CComVariant varState;
	HRESULT hr = pVM->Get(L"EnabledState", 0, &varState, 0, 0);
	if (FAILED(hr) || varState.vt != VT_I4) {
		return false;
	}

	isRunning = varState.intVal == 2;
	return true;
}

bool TryGetCurrentSnapshotPath(
	IWbemServices* pSvc,
	const std::wstring& vmPath,
	std::wstring& snapshotPath,
	std::wstring& errorMessage) {

	snapshotPath.clear();
	if (!pSvc || vmPath.empty()) {
		errorMessage = L"Hyper-V snapshot lookup parameters are invalid.";
		return false;
	}

	std::wstring query = L"ASSOCIATORS OF {" + vmPath + L"} WHERE AssocClass=Msvm_MostCurrentSnapshotInBranch ResultClass=Msvm_VirtualSystemSettingData";
	CComPtr<IEnumWbemClassObject> pEnumerator;
	HRESULT hr = pSvc->ExecQuery(
		CComBSTR(L"WQL"),
		CComBSTR(query.c_str()),
		WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
		NULL,
		&pEnumerator);

	if (FAILED(hr)) {
		errorMessage = L"Failed to query current Hyper-V snapshot: " + GetWmiErrorMessage(hr);
		return false;
	}

	CComPtr<IWbemClassObject> pSnapshot;
	ULONG uReturn = 0;
	hr = pEnumerator->Next(WBEM_INFINITE, 1, &pSnapshot, &uReturn);
	if (FAILED(hr) || uReturn == 0) {
		errorMessage = L"No current Hyper-V snapshot was found for the running VM.";
		return false;
	}

	CComVariant varPath;
	hr = pSnapshot->Get(L"__PATH", 0, &varPath, 0, 0);
	if (FAILED(hr) || varPath.vt != VT_BSTR || varPath.bstrVal == nullptr) {
		errorMessage = L"Failed to read current Hyper-V snapshot path.";
		return false;
	}

	snapshotPath.assign(varPath.bstrVal, SysStringLen(varPath.bstrVal));
	return true;
}

// Get Hyper-V management service
HRESULT GetManagementService(IWbemServices* pSvc, IWbemClassObject** ppManagementService, std::wstring& servicePath) {
	CComPtr<IEnumWbemClassObject> pEnumerator;

	HRESULT hr = pSvc->ExecQuery(
		CComBSTR(L"WQL"),
		CComBSTR(L"SELECT * FROM Msvm_VirtualSystemManagementService"),
		WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
		NULL,
		&pEnumerator);

	if (FAILED(hr)) return hr;

	CComPtr<IWbemClassObject> pclsObj;
	ULONG uReturn = 0;

	hr = pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn);
	if (uReturn == 0) return E_FAIL;

	// Get __PATH
	CComVariant varPath;
	hr = pclsObj->Get(L"__PATH", 0, &varPath, 0, 0);
	if (SUCCEEDED(hr)) {
		servicePath = varPath.bstrVal;
		*ppManagementService = pclsObj.Detach();
	}

	return hr;
}

// Get VM by name
HRESULT GetVMByName(IWbemServices* pSvc, const wchar_t* vmName, IWbemClassObject** ppVM, std::wstring& vmPath) {
	wchar_t query[512];
	swprintf_s(query, L"SELECT * FROM Msvm_ComputerSystem WHERE ElementName='%s' AND Caption='Virtual Machine'", vmName);

	CComPtr<IEnumWbemClassObject> pEnumerator;
	HRESULT hr = pSvc->ExecQuery(
		CComBSTR(L"WQL"),
		CComBSTR(query),
		WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
		NULL,
		&pEnumerator);

	if (FAILED(hr)) return hr;

	CComPtr<IWbemClassObject> pclsObj;
	ULONG uReturn = 0;

	hr = pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn);
	if (uReturn == 0) {
		SetLastErrorMessage(std::wstring(L"Virtual machine '") + vmName + L"' not found");
		return E_FAIL;
	}

	// Get __PATH
	CComVariant varPath;
	hr = pclsObj->Get(L"__PATH", 0, &varPath, 0, 0);
	if (SUCCEEDED(hr)) {
		vmPath = varPath.bstrVal;
		*ppVM = pclsObj.Detach();
	}

	return hr;
}

// Export VM (Hyper-V native export)
extern "C" BACKUPENGINE_API int BackupHyperVVM(
	const wchar_t* vmName,
	const wchar_t* destPath,
	ProgressCallback callback) {

	return ExecuteHyperVBackupPoint(vmName, destPath, L"Full", L"", callback);
}

extern "C" BACKUPENGINE_API int BackupHyperVVMIncremental(
	const wchar_t* vmName,
	const wchar_t* destPath,
	ProgressCallback callback) {

	if (!vmName || !destPath) {
		SetLastErrorMessage(L"Invalid parameters");
		return -1;
	}

	std::wstring latestPoint = FindNewestHyperVBackupPoint(destPath);
	return ExecuteHyperVBackupPoint(vmName, destPath, latestPoint.empty() ? L"Full" : L"Incremental", latestPoint, callback);
}

extern "C" BACKUPENGINE_API int BackupHyperVVMDifferential(
	const wchar_t* vmName,
	const wchar_t* destPath,
	ProgressCallback callback) {

	if (!vmName || !destPath) {
		SetLastErrorMessage(L"Invalid parameters");
		return -1;
	}

	std::wstring latestFullPoint = FindLatestHyperVBackupPointByType(destPath, L"Full");
	return ExecuteHyperVBackupPoint(vmName, destPath, latestFullPoint.empty() ? L"Full" : L"Differential", latestFullPoint, callback);
}

namespace {
	int ExecuteHyperVBackupPoint(
		const wchar_t* vmName,
		const wchar_t* backupRootPath,
		const std::wstring& backupType,
		const std::wstring& parentPath,
		ProgressCallback callback) {

		if (!vmName || !backupRootPath) {
			SetLastErrorMessage(L"Invalid parameters");
			return -1;
		}

		std::wstring backupRoot = backupRootPath;
		std::wstring pointId = GetTimestampId();
		std::wstring backupPointName = backupType + L"_" + pointId + L".ssb";
		std::wstring backupPointPath = CombinePath(backupRoot, backupPointName);
		std::wstring exportPath = GetExportRootPath(backupPointPath);

		HRESULT hr = CoInitializeEx(0, COINIT_MULTITHREADED);
		bool coinitCalled = SUCCEEDED(hr);

		CComPtr<IWbemLocator> pLoc;
		CComPtr<IWbemServices> pSvc;

		try {
			// Create WMI locator
			hr = CoCreateInstance(
				CLSID_WbemLocator,
				0,
				CLSCTX_INPROC_SERVER,
				IID_IWbemLocator,
				(LPVOID*)&pLoc);

			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to create WMI locator");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			// Connect to Hyper-V namespace
			hr = pLoc->ConnectServer(
				CComBSTR(L"ROOT\\virtualization\\v2"),
				NULL,
				NULL,
				0,
				NULL,
				0,
				0,
				&pSvc);

			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to connect to Hyper-V WMI namespace. Is Hyper-V installed?");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			// Set security levels
			hr = CoSetProxyBlanket(
				pSvc,
				RPC_C_AUTHN_WINNT,
				RPC_C_AUTHZ_NONE,
				NULL,
				RPC_C_AUTHN_LEVEL_CALL,
				RPC_C_IMP_LEVEL_IMPERSONATE,
				NULL,
				EOAC_NONE);

			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to set WMI security");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			if (callback) callback(10, L"Connecting to Hyper-V...");

			// Get VM
			CComPtr<IWbemClassObject> pVM;
			std::wstring vmPath;
			hr = GetVMByName(pSvc, vmName, &pVM, vmPath);

			if (FAILED(hr)) {
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			if (callback) callback(20, L"Found virtual machine");

			bool isVmRunning = false;
			IsVmRunning(pVM, isVmRunning);

			// Get management service
			CComPtr<IWbemClassObject> pMgmtService;
			std::wstring mgmtPath;
			hr = GetManagementService(pSvc, &pMgmtService, mgmtPath);

			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to get Hyper-V management service");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			if (callback) {
				std::wstring status = L"Preparing " + backupType + L" Hyper-V backup point...";
				callback(30, status.c_str());
			}

			// Create destination directory
			try {
				fs::create_directories(exportPath);
			}
			catch (const std::exception& e) {
				std::wstring error = L"Failed to create destination directory: ";
				error += std::wstring(e.what(), e.what() + strlen(e.what()));
				SetLastErrorMessage(error);
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			// Get ExportSystemDefinition method
			CComPtr<IWbemClassObject> pClass;
			hr = pSvc->GetObject(
				CComBSTR(L"Msvm_VirtualSystemManagementService"),
				0,
				NULL,
				&pClass,
				NULL);

			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to get management service class");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			CComPtr<IWbemClassObject> pInParamsDefinition;
			CComPtr<IWbemClassObject> pInParams;

			hr = pClass->GetMethod(CComBSTR(L"ExportSystemDefinition"), 0, &pInParamsDefinition, NULL);
			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to get ExportSystemDefinition method");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			hr = pInParamsDefinition->SpawnInstance(0, &pInParams);
			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to spawn parameters instance");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			// Set ComputerSystem parameter
			CComVariant varVM(vmPath.c_str());
			hr = pInParams->Put(L"ComputerSystem", 0, &varVM, 0);
			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to set ComputerSystem parameter");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			// Set ExportDirectory parameter
			CComVariant varPath(exportPath.c_str());
			hr = pInParams->Put(L"ExportDirectory", 0, &varPath, 0);
			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to set ExportDirectory parameter");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			std::wstring snapshotPath;
			if (isVmRunning) {
				std::wstring snapshotError;
				if (!TryGetCurrentSnapshotPath(pSvc, vmPath, snapshotPath, snapshotError)) {
					// No current snapshot found - fall back to exporting without snapshots
					snapshotPath.clear();
				}
			}

			std::wstring exportSettingData;
			std::wstring exportSettingError;
			if (!BuildHyperVExportSettingData(pSvc, vmPath, backupType, isVmRunning, snapshotPath, exportSettingData, exportSettingError)) {
				SetLastErrorMessage(exportSettingError);
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			CComVariant varExportSettingData(exportSettingData.c_str());
			hr = pInParams->Put(L"ExportSettingData", 0, &varExportSettingData, 0);
			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to set ExportSettingData parameter: " + GetWmiErrorMessage(hr));
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			if (callback) {
				std::wstring status = L"Starting " + backupType + L" export...";
				callback(40, status.c_str());
			}

			// Execute export
			CComPtr<IWbemClassObject> pOutParams;
			hr = pSvc->ExecMethod(
				CComBSTR(mgmtPath.c_str()),
				CComBSTR(L"ExportSystemDefinition"),
				0,
				NULL,
				pInParams,
				&pOutParams,
				NULL);

			if (FAILED(hr)) {
				SetLastErrorMessage(L"Failed to execute export method: " + GetWmiErrorMessage(hr));
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			// Guard against WMI returning S_OK but no output params (observed on some Hyper-V builds).
			// Dereferencing a null pOutParams is an access violation that bypasses catch(...) and
			// kills the service process.
			if (!pOutParams) {
				SetLastErrorMessage(L"ExportSystemDefinition returned no output parameters");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			// Get return value - check VARTYPE before reading to avoid misreading VT_I4 as VT_UI4
			CComVariant varReturnValue;
			hr = pOutParams->Get(L"ReturnValue", 0, &varReturnValue, NULL, 0);

			if (SUCCEEDED(hr)) {
				UINT32 returnValue = 0;
				if (varReturnValue.vt == VT_UI4) {
					returnValue = varReturnValue.uintVal;
				}
				else if (varReturnValue.vt == VT_I4) {
					returnValue = (UINT32)varReturnValue.intVal;
				}
				else if (varReturnValue.vt == VT_UI2) {
					returnValue = varReturnValue.uiVal;
				}
				else if (varReturnValue.vt == VT_I2) {
					returnValue = (UINT32)(SHORT)varReturnValue.iVal;
				}

				if (returnValue == 0) {
					// Success
					if (callback) callback(95, L"Finalizing Hyper-V backup metadata...");
				}
				else if (returnValue == 4096) {
					// Async job started - WMI returns VT_BSTR containing the job object path
					CComVariant varJob;
					hr = pOutParams->Get(L"Job", 0, &varJob, NULL, 0);

					if (SUCCEEDED(hr) && varJob.vt == VT_BSTR && varJob.bstrVal != nullptr) {
						std::wstring jobPath(varJob.bstrVal, SysStringLen(varJob.bstrVal));

						CComPtr<IWbemClassObject> pJob;
						hr = pSvc->GetObject(CComBSTR(jobPath.c_str()), 0, NULL, &pJob, NULL);
						if (FAILED(hr) || !pJob) {
							SetLastErrorMessage(L"Failed to get export job object");
							if (coinitCalled) CoUninitialize();
							return -1;
						}

						bool jobComplete = false;

						// Allow up to 90 minutes for large VM exports; poll every 2 seconds.
						const int kMaxPollSeconds = 90 * 60;
						const int kPollIntervalMs = 2000;
						int elapsedSeconds = 0;

						while (!jobComplete) {
							Sleep(kPollIntervalMs);
							elapsedSeconds += kPollIntervalMs / 1000;

							if (elapsedSeconds >= kMaxPollSeconds) {
								SetLastErrorMessage(L"Hyper-V export timed out after 90 minutes");
								if (coinitCalled) CoUninitialize();
								return -1;
							}

							// Refresh job object by path before reading state
							pJob.Release();
							hr = pSvc->GetObject(CComBSTR(jobPath.c_str()), 0, NULL, &pJob, NULL);
							if (FAILED(hr) || !pJob) {
								SetLastErrorMessage(L"Lost contact with export job");
								if (coinitCalled) CoUninitialize();
								return -1;
							}

							CComVariant varJobState;
							hr = pJob->Get(L"JobState", 0, &varJobState, NULL, 0);

							if (SUCCEEDED(hr)) {
								UINT32 jobState = varJobState.uintVal;

								// Hyper-V CIM_ConcreteJob states:
								//  2=New, 3=Starting, 4=Running, 5=Suspended
								//  6=ShuttingDown, 7=Completed, 8=Terminated, 9=Killed
								// 10=Exception, 32768=CompletedWithWarnings
								if (jobState == 7 || jobState == 32768) {
									jobComplete = true;
									if (callback) callback(95, L"Export completed. Finalizing metadata...");
								}
								else if (jobState == 8 || jobState == 9 || jobState == 10) {
									// Terminal failure states - read ErrorDescription for details
									CComVariant varErrorDesc;
									std::wstring jobError = L"Export job failed";
									if (SUCCEEDED(pJob->Get(L"ErrorDescription", 0, &varErrorDesc, NULL, 0)) &&
										varErrorDesc.vt == VT_BSTR && varErrorDesc.bstrVal != nullptr) {
										jobError += L": ";
										jobError.append(varErrorDesc.bstrVal, SysStringLen(varErrorDesc.bstrVal));
									}
									SetLastErrorMessage(jobError);
									if (coinitCalled) CoUninitialize();
									return -1;
								}
								else {
									// Still running - read PercentComplete from the job object
									CComVariant varPct;
									int reportedPct = 40;
									if (SUCCEEDED(pJob->Get(L"PercentComplete", 0, &varPct, NULL, 0)) &&
										(varPct.vt == VT_UI2 || varPct.vt == VT_I2 || varPct.vt == VT_UI4)) {
										UINT32 wmipct = (varPct.vt == VT_UI4) ? varPct.uintVal
											: (varPct.vt == VT_UI2) ? varPct.uiVal
											: (UINT32)(SHORT)varPct.iVal;
										// Map WMI 0-100 → UI 40-94 to leave room for finalize step
										reportedPct = 40 + (int)(wmipct * 54 / 100);
										reportedPct = min(94, reportedPct);
									}
									if (callback) callback(reportedPct, L"Exporting VM...");
								}
							}
						}
					}
					else {
						SetLastErrorMessage(L"Failed to get export job");
						if (coinitCalled) CoUninitialize();
						return -1;
					}
				}
				else {
					wchar_t error[256];
					swprintf_s(error, L"Export failed with code: %u", returnValue);
					SetLastErrorMessage(error);
					if (coinitCalled) CoUninitialize();
					return -1;
				}
			}

			if (!WriteHyperVBackupPointInfo(backupPointPath, backupType, pointId, vmName, parentPath)) {
				SetLastErrorMessage(L"Failed to write Hyper-V backup metadata");
				if (coinitCalled) CoUninitialize();
				return -1;
			}

			if (coinitCalled) CoUninitialize();
			if (callback) {
				std::wstring status = backupType + L" Hyper-V backup completed successfully";
				callback(100, status.c_str());
			}
			return 0;
		}
		catch (const std::exception& e) {
			std::wstring error = L"Exception during Hyper-V export: ";
			error += std::wstring(e.what(), e.what() + strlen(e.what()));
			SetLastErrorMessage(error);
			if (coinitCalled) CoUninitialize();
			return -1;
		}
		catch (...) {
			SetLastErrorMessage(L"Unknown exception during Hyper-V export");
			if (coinitCalled) CoUninitialize();
			return -1;
		}
	}

} // namespace
