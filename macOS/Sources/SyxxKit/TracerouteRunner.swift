import Foundation

/// Single source of truth for the system traceroute wrapper shared by the `syxx route` command, the
/// guided Portal flow, and the GUI Portal surface. Runs `/usr/sbin/traceroute` and returns the real,
/// trimmed, non-empty output lines.
///
/// Honesty: an empty return means the tool could not launch, timed out, or produced no output. Callers
/// MUST treat an empty result as a failure/inconclusive path, never as a clean or complete trace.
public enum TracerouteRunner {
    /// - Parameters:
    ///   - target: hostname or IP to trace (callers are expected to validate it first).
    ///   - maxHops: maximum TTL (`-m`). Default 30.
    ///   - timeout: seconds before the process is force-terminated. Default 45.
    /// - Returns: the traceroute's real output lines (trimmed, empties dropped), or `[]` on
    ///   launch failure / no output.
    public static func trace(target: String, maxHops: Int = 30, timeout: TimeInterval = 45) async -> [String] {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/sbin/traceroute")
        // -q 1: one probe per hop. -w 1: 1s wait per probe so a silent hop does not stall the trace.
        process.arguments = ["-q", "1", "-w", "1", "-m", String(maxHops), target]
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        do { try process.run() } catch { return [] }

        // E041: stream output lines as they arrive instead of blocking on waitUntilExit()
        // before reading the pipe. The old pattern — await waitTask.value then
        // readDataToEndOfFile() — risks a pipe-fill deadlock if traceroute emits more data
        // than the OS pipe buffer (64 KB) can hold before the process exits. Although traceroute
        // output is typically small (< 64 KB for 30 hops), streaming is correct practice and
        // provides partial hops on timeout, matching the Windows async-read behavior.
        //
        // F047: Swift 6.3 strict concurrency rejects mutable captures in the readability handler
        // closure; wrap the mutable state in a final class so only the reference is captured.
        final class StreamState: @unchecked Sendable {
            var buffer = Data()
            var lines: [String] = []
        }
        let collectedLines: [String] = await withCheckedContinuation { continuation in
            let state = StreamState()
            let readHandle = pipe.fileHandleForReading

            let timeoutTask = Task {
                try? await Task.sleep(nanoseconds: UInt64(timeout * 1_000_000_000))
                if process.isRunning { process.terminate() }
            }

            readHandle.readabilityHandler = { handle in
                let chunk = handle.availableData
                if chunk.isEmpty {
                    // EOF: process closed its stdout.
                    handle.readabilityHandler = nil
                    // Flush any remaining buffered data.
                    if !state.buffer.isEmpty, let tail = String(data: state.buffer, encoding: .utf8) {
                        let trimmed = tail.trimmingCharacters(in: .whitespacesAndNewlines)
                        if !trimmed.isEmpty { state.lines.append(trimmed) }
                    }
                    timeoutTask.cancel()
                    continuation.resume(returning: state.lines)
                } else {
                    state.buffer.append(chunk)
                    // Split on newlines; keep the last partial line in the buffer.
                    while let nlRange = state.buffer.range(of: Data([0x0A])) {
                        let lineData = state.buffer.subdata(in: state.buffer.startIndex..<nlRange.lowerBound)
                        state.buffer.removeSubrange(state.buffer.startIndex...nlRange.lowerBound)
                        if let line = String(data: lineData, encoding: .utf8) {
                            let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
                            if !trimmed.isEmpty { state.lines.append(trimmed) }
                        }
                    }
                }
            }
        }

        return collectedLines
    }
}
