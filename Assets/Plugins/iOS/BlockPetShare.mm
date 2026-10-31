// 打開 iOS 的分享面板（LINE、訊息…），給「空間資訊」的「分享給朋友」用。C# 端：Assets/Scripts/ShareText.cs
#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();

extern "C" void _BlockPet_ShareText(const char* text)
{
    if (text == NULL) return;
    NSString* message = [NSString stringWithUTF8String:text];
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController* root = UnityGetGLViewController();
        if (root == nil) return;
        UIActivityViewController* vc = [[UIActivityViewController alloc] initWithActivityItems:@[message] applicationActivities:nil];
        // iPad 要指定彈出位置
        vc.popoverPresentationController.sourceView = root.view;
        vc.popoverPresentationController.sourceRect = CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height, 1, 1);
        [root presentViewController:vc animated:YES completion:nil];
    });
}

// 分享一張圖片（照片檢視的「分享」：原始照片＋BlockPet 浮水印）。C# 端：Assets/Scripts/UI/Album/PhotoExport.cs
extern "C" void _BlockPet_ShareImage(const char* path)
{
    if (path == NULL) return;
    NSString* file = [NSString stringWithUTF8String:path];
    dispatch_async(dispatch_get_main_queue(), ^{
        UIImage* image = [UIImage imageWithContentsOfFile:file];
        UIViewController* root = UnityGetGLViewController();
        if (image == nil || root == nil) return;
        UIActivityViewController* vc = [[UIActivityViewController alloc] initWithActivityItems:@[image] applicationActivities:nil];
        // iPad 要指定彈出位置
        vc.popoverPresentationController.sourceView = root.view;
        vc.popoverPresentationController.sourceRect = CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height, 1, 1);
        [root presentViewController:vc animated:YES completion:nil];
    });
}
