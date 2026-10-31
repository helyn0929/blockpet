// 觸覺回饋（選空間頁的搖桿：換寵物＝Light、進去＝Medium）。C# 端：Assets/Scripts/Haptics.cs
#import <UIKit/UIKit.h>

extern "C" void _BlockPet_Haptic(int style)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UIImpactFeedbackStyle s = style >= 1 ? UIImpactFeedbackStyleMedium : UIImpactFeedbackStyleLight;
        UIImpactFeedbackGenerator* g = [[UIImpactFeedbackGenerator alloc] initWithStyle:s];
        [g prepare];
        [g impactOccurred];
    });
}
