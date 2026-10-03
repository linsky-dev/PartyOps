-- 仅处理本轮已截图确认的 WPS 首次许可欢迎页；未知控件树一律拒绝操作。
on describeElement(theElement)
    tell application "System Events"
        set roleText to ""
        set nameText to ""
        set descriptionText to ""
        set valueText to ""
        try
            set roleText to (role of theElement) as text
        end try
        try
            set nameText to (name of theElement) as text
        end try
        try
            set descriptionText to (description of theElement) as text
        end try
        try
            set valueText to (value of theElement) as text
        end try
        return {roleText, nameText, descriptionText, valueText}
    end tell
end describeElement

on inspectWelcome(theProcess)
    tell application "System Events"
        set windowList to windows of theProcess
        set treeText to "window_count=" & (count of windowList) & linefeed
        set matches to {}
        repeat with theWindow in windowList
            set windowName to ""
            try
                set windowName to (name of theWindow) as text
            end try
            set treeText to treeText & "window=" & windowName & linefeed
            set checkboxCandidates to {}
            set startButtons to {}
            set hasLicenseText to false
            set elementList to entire contents of theWindow
            set elementCount to count of elementList
            if elementCount > 500 then error "WPS_WELCOME_TREE_TOO_LARGE"
            repeat with theElement in elementList
                set details to my describeElement(theElement)
                set roleText to item 1 of details
                set nameText to item 2 of details
                set descriptionText to item 3 of details
                set valueText to item 4 of details
                set treeText to treeText & roleText & " | " & nameText & " | " & descriptionText & " | " & valueText & linefeed
                set combinedText to nameText & " " & descriptionText & " " & valueText
                if combinedText contains "License Agreements" or combinedText contains "Privacy Policies" then set hasLicenseText to true
                if roleText is "AXCheckBox" then set end of checkboxCandidates to theElement
                if roleText is "AXButton" and (nameText is "Start Now" or descriptionText is "Start Now") then set end of startButtons to theElement
            end repeat
            if (count of checkboxCandidates) is 1 and (count of startButtons) is 1 and hasLicenseText then
                set end of matches to {theWindow, item 1 of checkboxCandidates, item 1 of startButtons}
            end if
        end repeat
        return {treeText, matches}
    end tell
end inspectWelcome

on run argv
    if (count of argv) is not 1 then error "WPS_WELCOME_MODE_REQUIRED"
    set operation to item 1 of argv
    if operation is not "inspect" and operation is not "accept" and operation is not "bounds-click" and operation is not "state" then error "WPS_WELCOME_MODE_INVALID"
    tell application "System Events"
        if UI elements enabled is false then error "WPS_WELCOME_ACCESSIBILITY_UNAVAILABLE"
        set processList to application processes whose name is "wpsoffice"
        if (count of processList) is not 1 then error "WPS_WELCOME_PROCESS_NOT_UNIQUE"
        set theProcess to item 1 of processList
        set frontmost of theProcess to true
        set discovery to my inspectWelcome(theProcess)
        set treeText to item 1 of discovery
        set matches to item 2 of discovery
        if operation is "inspect" then return treeText & "matching_welcome_windows=" & (count of matches) & linefeed
        if (count of matches) is not 1 then error "WPS_WELCOME_CONTROLS_NOT_UNIQUE"
        set target to item 1 of matches
        set theWindow to item 1 of target
        set theCheckBox to item 2 of target
        set theStartButton to item 3 of target
        set beforeValue to ""
        try
            set beforeValue to (value of theCheckBox) as text
        end try
        if operation is "state" then
            set startEnabled to enabled of theStartButton
            return "checkbox_value=" & beforeValue & linefeed & "start_enabled=" & startEnabled & linefeed
        end if
        if operation is "bounds-click" then
            if beforeValue is not "1" and beforeValue is not "true" then error "WPS_WELCOME_CHECKBOX_NOT_SELECTED"
            if (frontmost of theProcess) is not true then error "WPS_WELCOME_PROCESS_NOT_FRONTMOST"
            if (enabled of theStartButton) is not true then error "WPS_WELCOME_BUTTON_DISABLED"
            set buttonPosition to position of theStartButton
            set buttonSize to size of theStartButton
            set windowPosition to position of theWindow
            set windowSize to size of theWindow
            set leftEdge to (item 1 of buttonPosition) as integer
            set topEdge to (item 2 of buttonPosition) as integer
            set buttonWidth to (item 1 of buttonSize) as integer
            set buttonHeight to (item 2 of buttonSize) as integer
            set windowLeft to (item 1 of windowPosition) as integer
            set windowTop to (item 2 of windowPosition) as integer
            set windowWidth to (item 1 of windowSize) as integer
            set windowHeight to (item 2 of windowSize) as integer
            if buttonWidth < 1 or buttonHeight < 1 or windowWidth < 1 or windowHeight < 1 then error "WPS_WELCOME_INVALID_BOUNDS"
            set centerX to leftEdge + (buttonWidth div 2)
            set centerY to topEdge + (buttonHeight div 2)
            if centerX < windowLeft or centerX >= windowLeft + windowWidth or centerY < windowTop or centerY >= windowTop + windowHeight then error "WPS_WELCOME_BUTTON_OUTSIDE_WINDOW"
            tell theProcess to click at {centerX, centerY}
            return "button_bounds=" & leftEdge & "," & topEdge & "," & buttonWidth & "," & buttonHeight & linefeed & "window_bounds=" & windowLeft & "," & windowTop & "," & windowWidth & "," & windowHeight & linefeed & "bounds_click_center=" & centerX & "," & centerY & linefeed & "bounds_click_once=true" & linefeed
        end if
        if beforeValue is "0" or beforeValue is "false" then
            click theCheckBox
            delay 0.3
        else if beforeValue is not "1" and beforeValue is not "true" then
            error "WPS_WELCOME_CHECKBOX_VALUE_UNKNOWN"
        end if
        set afterValue to ""
        try
            set afterValue to (value of theCheckBox) as text
        end try
        if afterValue is not "1" and afterValue is not "true" then error "WPS_WELCOME_CHECKBOX_NOT_SELECTED"
        if (enabled of theStartButton) is not true then error "WPS_WELCOME_BUTTON_DISABLED"
        click theStartButton
        return "welcome_checkbox_before=" & beforeValue & linefeed & "welcome_checkbox_after=" & afterValue & linefeed & "start_now_clicked=true" & linefeed
    end tell
end run
