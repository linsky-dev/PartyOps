// 本轮一次性 QA：只对唯一的 WPS 首次许可勾选框发送真实鼠标按下/抬起。
import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

func fail(_ code: String) -> Never {
    fputs("\(code)\n", stderr)
    exit(2)
}

func attribute(_ element: AXUIElement, _ key: String) -> CFTypeRef? {
    var result: CFTypeRef?
    guard AXUIElementCopyAttributeValue(element, key as CFString, &result) == .success else { return nil }
    return result
}

func label(_ element: AXUIElement) -> String {
    let title = attribute(element, kAXTitleAttribute as String) as? String ?? ""
    let description = attribute(element, kAXDescriptionAttribute as String) as? String ?? ""
    return title.isEmpty ? description : title
}

func point(_ element: AXUIElement, _ key: String) -> CGPoint? {
    guard let raw = attribute(element, key), CFGetTypeID(raw) == AXValueGetTypeID() else { return nil }
    var result = CGPoint.zero
    guard AXValueGetValue(raw as! AXValue, .cgPoint, &result) else { return nil }
    return result
}

func size(_ element: AXUIElement) -> CGSize? {
    guard let raw = attribute(element, kAXSizeAttribute as String), CFGetTypeID(raw) == AXValueGetTypeID() else { return nil }
    var result = CGSize.zero
    guard AXValueGetValue(raw as! AXValue, .cgSize, &result) else { return nil }
    return result
}

func descendants(_ root: AXUIElement) -> [AXUIElement] {
    var seen = 0
    var pending = [root]
    var result: [AXUIElement] = []
    while !pending.isEmpty {
        let current = pending.removeFirst()
        seen += 1
        if seen > 500 { fail("WPS_CONSENT_TREE_TOO_LARGE") }
        result.append(current)
        if let children = attribute(current, kAXChildrenAttribute as String) as? [AXUIElement] {
            pending.append(contentsOf: children)
        }
    }
    return result
}

guard CommandLine.arguments.count == 1 else { fail("WPS_CONSENT_ARGUMENTS_REJECTED") }
guard AXIsProcessTrusted() else { fail("WPS_CONSENT_ACCESSIBILITY_UNAVAILABLE") }
guard CGPreflightPostEventAccess() else { fail("WPS_CONSENT_INPUT_PERMISSION_UNAVAILABLE") }

let apps = NSWorkspace.shared.runningApplications.filter { $0.bundleIdentifier == "com.kingsoft.wpsoffice.mac" }
guard apps.count == 1, let app = apps.first else { fail("WPS_CONSENT_PROCESS_NOT_UNIQUE") }
guard NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier else {
    fail("WPS_CONSENT_PROCESS_NOT_FRONTMOST")
}
let root = AXUIElementCreateApplication(app.processIdentifier)
guard let windows = attribute(root, kAXWindowsAttribute as String) as? [AXUIElement] else {
    fail("WPS_CONSENT_WINDOWS_UNAVAILABLE")
}
var targets: [(AXUIElement, AXUIElement)] = []
for window in windows {
    let elements = descendants(window)
    let checkboxes = elements.filter {
        (attribute($0, kAXRoleAttribute as String) as? String) == (kAXCheckBoxRole as String)
            && label($0) == "I have read and agree to the"
    }
    let starts = elements.filter {
        (attribute($0, kAXRoleAttribute as String) as? String) == (kAXButtonRole as String)
            && label($0) == "Start Now"
    }
    let licenseCount = elements.filter { label($0) == "License Agreements" }.count
    let privacyCount = elements.filter { label($0) == "Privacy Policies" }.count
    if checkboxes.count == 1 && starts.count == 1 && licenseCount == 1 && privacyCount == 1 {
        targets.append((window, checkboxes[0]))
    }
}
guard targets.count == 1 else { fail("WPS_CONSENT_WINDOW_NOT_UNIQUE") }
let (window, checkbox) = targets[0]
guard let before = attribute(checkbox, kAXValueAttribute as String) as? NSNumber, before.intValue == 0 else {
    fail("WPS_CONSENT_CHECKBOX_NOT_UNCHECKED")
}
guard let enabled = attribute(checkbox, kAXEnabledAttribute as String) as? NSNumber, enabled.boolValue else {
    fail("WPS_CONSENT_CHECKBOX_DISABLED")
}
guard let origin = point(checkbox, kAXPositionAttribute as String), let extent = size(checkbox),
      let windowOrigin = point(window, kAXPositionAttribute as String), let windowSize = size(window),
      extent.width > 0, extent.height > 0, windowSize.width > 0, windowSize.height > 0 else {
    fail("WPS_CONSENT_BOUNDS_INVALID")
}
let center = CGPoint(x: origin.x + extent.width / 2, y: origin.y + extent.height / 2)
guard center.x >= windowOrigin.x, center.x < windowOrigin.x + windowSize.width,
      center.y >= windowOrigin.y, center.y < windowOrigin.y + windowSize.height else {
    fail("WPS_CONSENT_CHECKBOX_OUTSIDE_WINDOW")
}
guard let down = CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown,
                         mouseCursorPosition: center, mouseButton: .left),
      let up = CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp,
                       mouseCursorPosition: center, mouseButton: .left) else {
    fail("WPS_CONSENT_MOUSE_EVENT_CREATE_FAILED")
}
print("checkbox_bounds=\(origin.x),\(origin.y),\(extent.width),\(extent.height)")
print("window_bounds=\(windowOrigin.x),\(windowOrigin.y),\(windowSize.width),\(windowSize.height)")
print("mouse_center=\(center.x),\(center.y)")
fflush(stdout)
down.post(tap: .cghidEventTap)
usleep(80_000)
up.post(tap: .cghidEventTap)
print("mouse_down_up_posted=true")
