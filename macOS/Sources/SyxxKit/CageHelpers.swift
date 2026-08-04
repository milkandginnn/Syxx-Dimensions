import Foundation

/// Shared helpers for cage capture file management, used by both the CLI and the GUI.
public enum CageHelpers {
    /// The per-user captures directory syxx-cage writes to (`~/.syxx/captures`). When we ourselves run
    /// under sudo, the child writes to the invoking user's home (SUDO_USER); mirror that so we read the
    /// same directory the capture landed in.
    public static func cageCapturesDir() -> URL {
        let env = ProcessInfo.processInfo.environment
        let base: URL
        if let sudoUser = env["SUDO_USER"], !sudoUser.isEmpty, let home = NSHomeDirectoryForUser(sudoUser) {
            base = URL(fileURLWithPath: home)
        } else {
            base = FileManager.default.homeDirectoryForCurrentUser
        }
        return base.appendingPathComponent(".syxx/captures", isDirectory: true)
    }

    /// The newest raw NDJSON capture file (`Syxx Cage Capture <stamp>.txt`, excluding the human-readable
    /// "…Report…" file) produced at or after `start`. Nil when none is found.
    public static func newestCaptureFile(in dir: URL, after start: Date) -> String? {
        let fm = FileManager.default
        guard let items = try? fm.contentsOfDirectory(at: dir,
            includingPropertiesForKeys: [.contentModificationDateKey]) else { return nil }
        var best: (path: String, mod: Date)? = nil
        for url in items {
            let n = url.lastPathComponent
            guard n.hasPrefix("Syxx Cage Capture "), !n.contains("Report"), n.hasSuffix(".txt") else { continue }
            let mod = (try? url.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast
            guard mod >= start.addingTimeInterval(-2) else { continue }   // small clock-skew tolerance
            if best == nil || mod > best!.mod { best = (url.path, mod) }
        }
        return best?.path
    }
}
