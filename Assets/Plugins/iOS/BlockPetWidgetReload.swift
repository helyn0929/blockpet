// 通知 iOS 重畫手機桌面小工具（WidgetCenter 只有 Swift 介面）。C# 端：Assets/Scripts/Widget/WidgetSync.cs
import Foundation
import WidgetKit

@_cdecl("_BlockPet_ReloadWidgets")
public func _BlockPet_ReloadWidgets() {
    if #available(iOS 14.0, *) {
        WidgetCenter.shared.reloadAllTimelines()
    }
}
