import Foundation
import CoreGraphics

// Query only existing on-screen windows. Never activate or launch an application.
let projectName = CommandLine.arguments.dropFirst().first ?? ""
let playerPidArgument = CommandLine.arguments.dropFirst(2).first
let playerPid = playerPidArgument.flatMap(Int.init)
if playerPidArgument != nil && (playerPid == nil || playerPid! <= 0) {
    FileHandle.standardError.write(Data("Player PID must be a positive integer.\n".utf8))
    exit(2)
}
guard !projectName.isEmpty else {
    FileHandle.standardError.write(Data("Usage: UnityWindowId.swift <project-name>\n".utf8))
    exit(2)
}
guard let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else {
    FileHandle.standardError.write(Data("Cannot enumerate windows; check Screen Recording permission.\n".utf8))
    exit(1)
}
let candidates = windows.filter { window in
    guard let owner = window[kCGWindowOwnerName as String] as? String,
          let layer = window[kCGWindowLayer as String] as? Int else { return false }
    if let pid = playerPid {
        return (window[kCGWindowOwnerPID as String] as? Int) == pid && layer == 0
    }
    return owner == "Unity" && layer == 0
}
let matches = candidates.filter { window in
    guard let title = window[kCGWindowName as String] as? String else { return false }
    if playerPid != nil { return !title.isEmpty }
    // Unity's main title contains the project between scene and platform fields.
    return title.contains(" - " + projectName + " - ")
}
guard matches.count == 1,
      let identifier = matches[0][kCGWindowNumber as String] as? UInt32,
      identifier > 0 else {
    FileHandle.standardError.write(Data("Expected one visible render window for '\(projectName)' (Player PID: \(playerPidArgument ?? "Editor")), found \(matches.count). Window titles may require Screen Recording permission.\n".utf8))
    for window in candidates {
        let title = window[kCGWindowName as String] as? String ?? "<title unavailable>"
        let identifier = window[kCGWindowNumber as String] as? UInt32 ?? 0
        FileHandle.standardError.write(Data("Candidate CGWindowID=\(identifier) title=\(title)\n".utf8))
    }
    exit(1)
}
print(identifier)
