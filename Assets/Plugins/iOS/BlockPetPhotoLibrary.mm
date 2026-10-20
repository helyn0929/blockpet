// 取得相簿最新的相機照片（排除截圖、螢幕錄影），轉成 JPG 存到暫存資料夾，
// 完成後用 UnitySendMessage 把結果（JSON）傳回 C# 的 PhotoLibrary。
#import <Foundation/Foundation.h>
#import <Photos/Photos.h>
#import <UIKit/UIKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static NSString* BPSafe(NSString* s) { return s ?: @""; }

extern "C" void _BlockPet_FetchRecentPhotos(const char* gameObjectName, int limit, double sinceUnixSeconds, int maxEdge)
{
    NSString* goName = [NSString stringWithUTF8String:gameObjectName ?: ""];

    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        PHFetchOptions* opts = [[PHFetchOptions alloc] init];
        opts.sortDescriptors = @[[NSSortDescriptor sortDescriptorWithKey:@"creationDate" ascending:NO]];
        // 只取使用者自己的圖庫（排除 iCloud 共享相簿）
        opts.includeAssetSourceTypes = PHAssetSourceTypeUserLibrary;
        NSDate* since = [NSDate dateWithTimeIntervalSince1970:sinceUnixSeconds];
        opts.predicate = [NSPredicate predicateWithFormat:@"creationDate >= %@", since];

        PHFetchResult<PHAsset*>* assets = [PHAsset fetchAssetsWithMediaType:PHAssetMediaTypeImage options:opts];

        PHImageRequestOptions* reqOpts = [[PHImageRequestOptions alloc] init];
        reqOpts.synchronous = YES;
        reqOpts.networkAccessAllowed = YES; // 照片只在 iCloud 時也能取
        reqOpts.deliveryMode = PHImageRequestOptionsDeliveryModeHighQualityFormat;
        reqOpts.resizeMode = PHImageRequestOptionsResizeModeFast;

        NSString* dir = [NSTemporaryDirectory() stringByAppendingPathComponent:@"blockpet_hand"];
        [[NSFileManager defaultManager] createDirectoryAtPath:dir withIntermediateDirectories:YES attributes:nil error:nil];

        NSMutableArray* items = [NSMutableArray array];
        for (PHAsset* asset in assets)
        {
            if ((NSInteger)items.count >= limit) break;
            // 過濾截圖（PhotoScreenshot）；Live Photo、HDR、人像等相機拍的子類型都保留
            if (asset.mediaSubtypes & PHAssetMediaSubtypePhotoScreenshot) continue;

            NSString* safeId = [[asset.localIdentifier componentsSeparatedByString:@"/"] firstObject];
            NSString* path = [dir stringByAppendingPathComponent:[NSString stringWithFormat:@"%@_%d.jpg", safeId, maxEdge]];

            if (![[NSFileManager defaultManager] fileExistsAtPath:path])
            {
                __block NSData* jpg = nil;
                CGSize target = CGSizeMake(maxEdge, maxEdge);
                [[PHImageManager defaultManager] requestImageForAsset:asset
                                                           targetSize:target
                                                          contentMode:PHImageContentModeAspectFit
                                                              options:reqOpts
                                                        resultHandler:^(UIImage* img, NSDictionary* info) {
                    if (img) jpg = UIImageJPEGRepresentation(img, 0.85);
                }];
                if (!jpg) continue;
                [jpg writeToFile:path atomically:YES];
            }

            double takenMs = asset.creationDate ? asset.creationDate.timeIntervalSince1970 * 1000.0 : 0;
            [items addObject:@{ @"id": BPSafe(asset.localIdentifier),
                                @"path": path,
                                @"takenAtMs": @((long long)takenMs) }];
        }

        NSData* json = [NSJSONSerialization dataWithJSONObject:@{ @"items": items } options:0 error:nil];
        NSString* str = json ? [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding] : @"{\"items\":[]}";

        dispatch_async(dispatch_get_main_queue(), ^{
            UnitySendMessage([goName UTF8String], "OnNativePhotosFetched", [str UTF8String]);
        });
    });
}

// ── 相簿讀取權限 ─────────────────────────────────────────────────
// NativeGallery 在 iOS 11+ 的 PermissionFreeMode 下不會真的要求讀取權限（它用 PHPicker 不需要），
// 但手牌直接用 PHAsset 讀圖庫，必須自己要求 ReadWrite 權限。「選取的照片」（Limited）也算可讀。

// 1 = 可讀（全部或部分照片）、0 = 拒絕／受限、2 = 尚未詢問
extern "C" int _BlockPet_PhotoAuthStatus()
{
    PHAuthorizationStatus status;
    if (@available(iOS 14.0, *))
    {
        status = [PHPhotoLibrary authorizationStatusForAccessLevel:PHAccessLevelReadWrite];
        if (status == PHAuthorizationStatusLimited) return 1;
    }
    else
    {
        status = [PHPhotoLibrary authorizationStatus];
    }
    if (status == PHAuthorizationStatusAuthorized) return 1;
    if (status == PHAuthorizationStatusNotDetermined) return 2;
    return 0;
}

// 跳出系統的相簿權限詢問，結果用 UnitySendMessage(gameObjectName, "OnNativePermission", "1"/"0") 傳回
extern "C" void _BlockPet_RequestPhotoAuth(const char* gameObjectName)
{
    NSString* goName = [NSString stringWithUTF8String:gameObjectName ?: ""];
    void (^reply)(PHAuthorizationStatus) = ^(PHAuthorizationStatus status) {
        BOOL ok = status == PHAuthorizationStatusAuthorized;
        if (@available(iOS 14.0, *)) ok = ok || status == PHAuthorizationStatusLimited;
        dispatch_async(dispatch_get_main_queue(), ^{
            UnitySendMessage([goName UTF8String], "OnNativePermission", ok ? "1" : "0");
        });
    };
    if (@available(iOS 14.0, *))
        [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelReadWrite handler:reply];
    else
        [PHPhotoLibrary requestAuthorization:reply];
}
