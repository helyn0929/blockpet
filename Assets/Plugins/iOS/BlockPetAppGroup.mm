// App Group 共用資料夾（手機桌面小工具讀 App 寫的資料）。C# 端：Assets/Scripts/Widget/WidgetSync.cs
#import <Foundation/Foundation.h>
#include <string.h>

extern "C" char* _BlockPet_AppGroupPath(const char* groupId)
{
    if (groupId == NULL) return NULL;
    NSURL* url = [[NSFileManager defaultManager] containerURLForSecurityApplicationGroupIdentifier:[NSString stringWithUTF8String:groupId]];
    if (url == nil) return NULL;
    // 回傳的字串由 Unity 的 marshaling 用 free() 釋放
    return strdup([url.path UTF8String]);
}
