#include "rapidjson/document.h"
#include <Windows.h>
#include <iostream>

#include "UltimateProxyDLL.h"

void DoHook(HINSTANCE hinstDLL);
BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    if (fdwReason == DLL_PROCESS_ATTACH)
    {
        try
        {
#if _DEBUG
            UPD::OpenDebugTerminal();
#endif
            UPD::CreateProxy(hinstDLL);
            DoHook(hinstDLL);
        }
        catch (std::runtime_error e)
        {
            std::cout << e.what() << std::endl;
            return FALSE;
        }
    }
    return TRUE;
}

PIMAGE_SECTION_HEADER GetRdataSection(HMODULE hModule) {
    auto dosHeader = reinterpret_cast<IMAGE_DOS_HEADER*>(hModule);
    auto ntHeaders = reinterpret_cast<IMAGE_NT_HEADERS*>(reinterpret_cast<BYTE*>(hModule) + dosHeader->e_lfanew);
    auto sectionHeader = IMAGE_FIRST_SECTION(ntHeaders);

    for (int i = 0; i < ntHeaders->FileHeader.NumberOfSections; ++i, ++sectionHeader) {
        std::string sectionName(reinterpret_cast<char*>(sectionHeader->Name), 8);
        if (sectionName.find(".rdata") != std::string::npos) {
            std::cout << ".rdata section found:" << std::endl;
            std::cout << "Virtual Address: 0x" << std::hex << sectionHeader->VirtualAddress << std::endl;
            std::cout << "Size: 0x" << std::hex << sectionHeader->Misc.VirtualSize << std::endl;
            return sectionHeader;
        }
    }
    return nullptr;
}


void DoHook(HINSTANCE hinstDLL)
{
    auto SectionHeader = GetRdataSection(GetModuleHandle(NULL));
    HRSRC hResource = FindResource(hinstDLL, MAKEINTRESOURCE(101), TEXT("JSON"));
    if (!hResource)
    {
        std::cout << "Failed to load Translation Json" << std::endl;
        return;
    }
    HGLOBAL hLoadedResource = LoadResource(hinstDLL, hResource);
    LPVOID pLockedResource = LockResource(hLoadedResource);
    DWORD resourceSize = SizeofResource(hinstDLL, hResource);
    rapidjson::Document TranslationDoc;
    TranslationDoc.Parse<rapidjson::kParseCommentsFlag>(reinterpret_cast<const char*>(pLockedResource), resourceSize);
    if (TranslationDoc.HasParseError())
    {
        std::cout << "Failed to parse Translation Json" << std::endl;
        return;
    }
    char* RDataStart = (char*)(SectionHeader->VirtualAddress + (DWORD)GetModuleHandle(NULL));
    auto TranslationItemArray = TranslationDoc.GetArray();
    for (size_t i = 0; i < TranslationItemArray.Size(); i++)
    {
        if (TranslationItemArray[i].IsObject() == false)
        {
            std::cout << "Translation Json Error: Translation Item is not an object" << std::endl;
            continue;
        }
        const char* Source = TranslationItemArray[i]["Source"].GetString();
        int SourceLen = strlen(Source)+1;
        const char* Target = TranslationItemArray[i]["Target"].GetString();
        int TargetLen = strlen(Target)+1;
        if (Source == nullptr || Target == nullptr || 
            strcmp(Source, "") == 0 || strcmp(Target, "") == 0 || 
            strcmp(Source, Target) == 0)
        {
            continue;
        }
        bool Strict = TranslationItemArray[i].HasMember("Strict") ? TranslationItemArray[i]["Strict"].GetBool() : false;
        bool SkipSingleByteStr = TranslationItemArray[i].HasMember("SkipSingleByte") ? TranslationItemArray[i]["SkipSingleByte"].GetBool() : false;
        // 逐字节扫描UTF16
        // 将Source转换为UTF16编码的字节数组
        int SourceU16Len = MultiByteToWideChar(CP_UTF8, 0, Source, -1, nullptr, 0);
        wchar_t* SourceU16 = new wchar_t[SourceU16Len];
        char* SourceU16SBPtr = (char*)SourceU16;
        MultiByteToWideChar(CP_UTF8, 0, Source, -1, SourceU16, SourceU16Len);
        // 将Target转换为UTF16编码的字节数组
        int TargetU16Len = MultiByteToWideChar(CP_UTF8, 0, Target, -1, nullptr, 0);
        wchar_t* TargetU16 = new wchar_t[TargetU16Len];
        char* TargetU16SBPtr = (char*)TargetU16;
        MultiByteToWideChar(CP_UTF8, 0, Target, -1, TargetU16, TargetU16Len);
        // 扫描.rdata段
        if (TargetU16Len <= SourceU16Len)
        {
            for (int SearchOffset = 0; SearchOffset <= 1; SearchOffset++)
            {
                for (DWORD j = SearchOffset; j < SectionHeader->Misc.VirtualSize; j += 2)
                {
                    bool Matched = true;
                    for (int k = 0; k < SourceU16Len; k++)
                    {
                        if (RDataStart[j + k * 2] != SourceU16SBPtr[k * 2] ||
                            RDataStart[j + k * 2 + 1] != SourceU16SBPtr[k * 2 + 1])
                        {
                            Matched = false;
                            break;
                        }
                    }
                    if (Matched && (!Strict||(RDataStart[j-1]==0&& RDataStart[j - 2] == 0)))
                    {
                        DWORD OldProtect;
                        VirtualProtect(&RDataStart[j], TargetU16Len * 2, PAGE_READWRITE, &OldProtect);
                        memcpy(&RDataStart[j], TargetU16SBPtr, TargetU16Len * 2);
                    }
                }
            }
        }
        else
        {
			std::cout << "Translation Error: Target is longer than Source:"<< Source << std::endl;
        }
        if (!SkipSingleByteStr)
        {
            for (DWORD j = 0; j < SectionHeader->Misc.VirtualSize; j++)
            {
                bool Matched = true;
                for (int k = 0; k < SourceLen; k++)
                {
                    if (RDataStart[j + k] != Source[k])
                    {
                        Matched = false;
                        break;
                    }
                }
                if (Matched && (!Strict || RDataStart[j - 1] == 0))
                {
                    if (TargetLen > SourceLen)
                    {
                        if (Strict)
                        {
                            break;
                        }
                        // 检查多出来的字节是否都为00
                        // 如果都是00的话，直接覆写大概也OK
                        bool AllEmpty = true;
                        for (int overFlowIndex = SourceLen; overFlowIndex < TargetLen; overFlowIndex++)
                        {
                            if (RDataStart[j + overFlowIndex] != '\0')
                            {
                                AllEmpty = false;
                                break;
                            }
                        }
                        if (!AllEmpty)
                        {
                            break;
                        }
                    }
                    DWORD OldProtect;
                    VirtualProtect(&RDataStart[j], TargetLen, PAGE_READWRITE, &OldProtect);
                    memcpy(&RDataStart[j], Target, TargetLen);
                }
            }
        }
    }
}