#undef UNICODE
#undef _UNICODE
#include <iostream>
#include <fstream>
#include <string>
#include <vector>
#include <Windows.h>
#include <TlHelp32.h>
#include <psapi.h>

using namespace std;

LPCSTR patchName = "LBEE_CHSMemoryPatch.exe";
LPCSTR gameBackup = "LITBUS_WIN32.exe.bak";
LPCSTR gameName = "LITBUS_WIN32.exe";
LPCSTR steamDllName = "gameoverlayrenderer.dll";
LPCSTR patchCommand = "--chspatch";
LPCSTR patchCommandSetup = "SETUP --chspatch";		// SETUP������ԭ��Ϸ�Դ��ģ�����������մ浵���ܵ�
LPCSTR patchEvent = "patchEvent";
LPCSTR gameNameSetupCommand = "LITBUS_WIN32.exe SETUP";
char buff[10000];
BYTE writeBuffer[10000];
const char* MemoryMapper = "mapper.txt";			// �洢�ı����ļ���
LPCSTR checkCommand = "--check";					// ���������ڴ�ӳ���ļ��Ƿ���ȷ

void errorMessageBox(const char* funcName) {
	const char* pattern = "������ִ��%sʱ��������LastError: %d";
	wsprintfA(buff, pattern, funcName, GetLastError());
	MessageBoxA(NULL, buff, "����", MB_ICONERROR);
}

bool writeCHSMemory(DWORD imageBase, HANDLE hProcess, DWORD rva, BYTE word[], UINT len) {
	// ����Ϸ��������̵�ĳ��ַ��д������
	DWORD change;
	if (!VirtualProtectEx(hProcess, (LPVOID)(imageBase + rva), len, PAGE_READWRITE, &change)) {
		cout << "VirtualProtect Fail! LastError:" << GetLastError() << endl;
		return false;
	}
	SIZE_T tmp;
	if (WriteProcessMemory(hProcess, (LPVOID)(imageBase + rva), word, len, &tmp)) {
		return true;
	}
	else {
		cout << "WriteProcess Fail! LastError:" << GetLastError() << endl;
		return false;
	}
}

DWORD getImageBase(HANDLE hProcess) {
	// ��ȡ��Ϸ������ģ��Ļ���ַ������ַÿ�β��̶�

	DWORD pid = GetProcessId(hProcess);
	if (pid == 0) {
		errorMessageBox("GetProcessId");
		return 0;
	}
	cout << "pid: " << pid << endl;
	HANDLE hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
	if (hSnapshot == INVALID_HANDLE_VALUE) {
		errorMessageBox("CreateToolhelp32Snapshot");
		return 0;
	}

	MODULEENTRY32 moduleEntry;
	moduleEntry.dwSize = sizeof(MODULEENTRY32);
	if (Module32First(hSnapshot, &moduleEntry)) {
		do
		{
			cout << "Module: " << moduleEntry.szModule << endl;
			if (_stricmp(moduleEntry.szModule, gameName) == 0)
			{
				CloseHandle(hSnapshot);
				return (DWORD)moduleEntry.modBaseAddr;
			}
		} while (Module32Next(hSnapshot, &moduleEntry));
	}
	CloseHandle(hSnapshot);
	return 0;
}

DWORD offsetToRVA(DWORD offset) {
	// �ļ�ƫ��תRVA������.rdata������Ч���ļ�ƫ��0x361000����0x3DF000��
	return 0x362000 + (offset - 0x361000);
}

void startMemoryPatch(HANDLE hProcess) {
	/*
	* ӳ���ļ��������ʽ���������־�Ϊ16���Ƶ���ʽ��
	* ������һ�����֣����������Ŀ
	* ÿ������ռ���У���һ����һ�����֣�Ϊ��������**�ļ�ƫ��**
	* �ڶ���ΪӢ��ԭ�ģ�������������ֽڳ���
	* ������Ϊ�������ģ�Ҫȷ�����ĵ�utf-8�ֽ���С�ڵ���Ӣ�ĵ�utf-8�ֽ��������򱾴����������д���
	*/

	DWORD imageBase = getImageBase(hProcess);
	if (imageBase == 0) {
		MessageBoxA(NULL, "��ȡ���̻���ַʧ�ܣ�", "��ʾ", MB_ICONWARNING);
		return;
	}
	cout << "ImageBase:" << hex << imageBase << endl;

	ifstream cin(MemoryMapper);
	if (!cin.is_open()) {
		MessageBoxA(NULL, "�ڴ�ӳ���ļ������ڣ�", "��ʾ", MB_ICONWARNING);
		return;
	}
	int t;
	cin >> std::hex >> t;
	while (t--) {
		int offset;
		cin >> std::hex >> offset;
		string eng, chs;
		cin.ignore();
		getline(cin, eng);
		getline(cin, chs);
		if (chs.length() > eng.length()) {
			cout << "offset=" << hex << offset << ": Chinese bytes are more than English" << endl;
			continue;
		}
		DWORD rva = offsetToRVA(offset);
		int len = eng.length();
		for (int i = 0; i < chs.length(); i++) {
			writeBuffer[i] = static_cast<unsigned char>(chs[i]);
		}
		for (int i = chs.length(); i < len; i++) {
			writeBuffer[i] = 0;
		}
		std::cout << "RVA=" << rva << ": patch" << (writeCHSMemory(imageBase, hProcess, rva, writeBuffer, len) ? "success" : "fail") << std::endl;
	}
}

int startChecker() {
	// ע���ļ���UTF-8�ı��뱣��
	ifstream cin(MemoryMapper);
	if (!cin.is_open()) {
		MessageBoxA(NULL, "�ڴ�ӳ���ļ������ڣ�", "��ʾ", MB_ICONWARNING);
		return 3;
	}
	int t;
	cin >> std::hex >> t;
	while (t--) {
		int offset;
		cin >> std::hex >> offset;
		string eng, chs;
		cin.ignore();
		getline(cin, eng);
		getline(cin, chs);
		if (chs.length() > eng.length()) {
			cout << "offet=" << hex << offset << ": Chinese bytes are more than English" << endl;
		}
		/*cout << "rva=" << hex << rva << ": ";
		for (int i = 0; i < chs.length(); i++) {
			cout << hex << static_cast<int>(static_cast<unsigned char>(chs[i])) << ' ';
		}
		cout << endl;*/
	}
	cout << "all checked!" << endl;
	getchar();
	return 0;
}

int startProcess1(int argc, char* argv[]) {
	// ��һ�����׶Σ��û���steam��������Ϸ����

	HMODULE hModule = GetModuleHandleA(steamDllName);
	if (hModule == NULL) {
		MessageBoxA(NULL, "����Steam��������", "��ʾ", MB_ICONWARNING);
		return 1;
	}

	// ����������ΪLBEE_CHSMemoryPatch.exe
	if (!CopyFileA(gameName, patchName, FALSE)) {
		errorMessageBox("CopyFileA");
		return 1;
	}

	// ����LBEE_CHSMemoryPatch.exe --chspatch
	STARTUPINFOA si = { sizeof(si) };
	PROCESS_INFORMATION pi;
	if (!CreateProcessA(patchName, (LPSTR)((argc > 1 && !strcmp(argv[1], "SETUP")) ? patchCommandSetup : patchCommand), NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi)) {
		errorMessageBox("CreateProcessA");
		return 1;
	}

	CloseHandle(pi.hProcess);
	CloseHandle(pi.hThread);
	return 0;
}

int startProcess2(int argc, char* argv[]) {
	// �ڶ������׶Σ���ʱ����������ΪLBEE_CHSMemoryPatch.exe����LITBUS_WIN32.exe.bak����ΪLITBUS_WIN32.exe

	if (!CopyFileA(gameBackup, gameName, FALSE)) {
		errorMessageBox("CopyFileA-2");
		return 2;
	}

	// ����LITBUS_WIN32.exe
	STARTUPINFOA si = { sizeof(si) };
	PROCESS_INFORMATION pi;

	auto rollback = [&]() {
			// ��LBEE_CHSMemoryPatch.exe�ٸ��ƻ�LITBUS_WIN32.exe
			if (!CopyFileA(patchName, gameName, FALSE)) {
				errorMessageBox("CopyFileA-3");
				CloseHandle(pi.hProcess);
				CloseHandle(pi.hThread);
			}
		};

	if (!CreateProcessA(NULL, (argc > 0 && !strcmp(argv[0], "SETUP")) ? (LPSTR)gameNameSetupCommand : (LPSTR)gameName, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi)) {
		errorMessageBox("CreateProcessA-2");
		//CloseHandle(hEvent);
		rollback();
		return 2;
	}

	// �ȴ����̳�ʼ��������Ѱ�һ���ַʱ�ᱻOS��Ϊ��64bit���̣�
	DWORD result = WaitForInputIdle(pi.hProcess, INFINITE);
	if (result) {
		errorMessageBox("WaitForInputIdle");
	}

	// �����ڴ油��
	startMemoryPatch(pi.hProcess);

	// �ȴ���Ϸ���̽���
	DWORD waitResult = WaitForSingleObject(pi.hProcess, INFINITE);
	if (waitResult != WAIT_OBJECT_0) {
		errorMessageBox("WaitForSingleObject");
		CloseHandle(pi.hProcess);
		CloseHandle(pi.hThread);
		rollback();
		return 2;
	}

	rollback();
	CloseHandle(pi.hProcess);
	CloseHandle(pi.hThread);
	//CloseHandle(hEvent);
	return 0;
}

int main(int argc, char* argv[]) {

	cout << "argc:" << argc << endl;
	for (int i = 0; i < argc; i++) {
		cout << "argv[" << i << "]: " << argv[i] << endl;
	}

	if (argc>0 && !strcmp(argv[argc-1], checkCommand)) {
		return startChecker();
	}
	else if (argc == 0 || strcmp(argv[argc - 1], patchCommand)) {
		return startProcess1(argc, argv);
	}
	else {
		return startProcess2(argc, argv);
	}

	return 0;
}