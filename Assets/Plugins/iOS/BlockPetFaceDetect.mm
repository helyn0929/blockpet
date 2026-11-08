// 照片人臉偵測（設計文件 5.21：有人臉的照片不能寄）：Vision 的 VNDetectFaceRectanglesRequest，在背景執行，
// 完成後 UnitySendMessage(物件, "OnFaceResult", "檔名|1／0／-1")。另外提供手機的地區代碼（抽好友卡片上的國家）。
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <Vision/Vision.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

extern "C" void _BlockPet_DetectFaces(const char* gameObjectName, const char* requestId, const char* path)
{
    NSString* goName = [NSString stringWithUTF8String:gameObjectName ?: ""];
    NSString* rid = [NSString stringWithUTF8String:requestId ?: ""];
    NSString* file = [NSString stringWithUTF8String:path ?: ""];
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        int result = -1;
        @autoreleasepool {
            UIImage* img = [UIImage imageWithContentsOfFile:file];
            if (img != nil && img.CGImage != nil) {
                CGImagePropertyOrientation orient = kCGImagePropertyOrientationUp;
                switch (img.imageOrientation) {
                    case UIImageOrientationDown: orient = kCGImagePropertyOrientationDown; break;
                    case UIImageOrientationLeft: orient = kCGImagePropertyOrientationLeft; break;
                    case UIImageOrientationRight: orient = kCGImagePropertyOrientationRight; break;
                    default: break;
                }
                VNDetectFaceRectanglesRequest* req = [[VNDetectFaceRectanglesRequest alloc] init];
                VNImageRequestHandler* handler = [[VNImageRequestHandler alloc] initWithCGImage:img.CGImage orientation:orient options:@{}];
                NSError* err = nil;
                if ([handler performRequests:@[req] error:&err]) result = req.results.count > 0 ? 1 : 0;
            }
        }
        NSString* msg = [NSString stringWithFormat:@"%@|%d", rid, result];
        dispatch_async(dispatch_get_main_queue(), ^{
            UnitySendMessage(goName.UTF8String, "OnFaceResult", msg.UTF8String);
        });
    });
}

extern "C" char* _BlockPet_CountryCode()
{
    NSString* code = [[NSLocale currentLocale] objectForKey:NSLocaleCountryCode] ?: @"";
    const char* s = code.UTF8String;
    char* out = (char*)malloc(strlen(s) + 1);
    strcpy(out, s);
    return out; // Unity 會 free
}
