// Ported into SyxxKit from the sibling LyxTools project (LyxToolsKit/Network).
// LAN cartography: discover on-link devices to surface a rogue DNS/DHCP server.
import Foundation
import Darwin

public struct CommandResult: Hashable, Codable, Sendable {
    public let stdout: String
    public let stderr: String
    public let exitCode: Int32?
    public let timedOut: Bool

    public init(stdout: String, stderr: String, exitCode: Int32?, timedOut: Bool) {
        self.stdout = stdout
        self.stderr = stderr
        self.exitCode = exitCode
        self.timedOut = timedOut
    }
}

public protocol CommandRunning: Sendable {
    func run(
        _ executable: String,
        arguments: [String],
        timeout: TimeInterval
    ) throws -> CommandResult
}

public enum CommandRunnerError: Error, LocalizedError {
    case launchFailed(String)
    case invalidTimeout
    case pipeCreationFailed(Int32)
    case spawnFailed(String, Int32)

    public var errorDescription: String? {
        switch self {
        case .launchFailed(let path):
            return "Unable to launch \(path)."
        case .invalidTimeout:
            return "Command timeout must be positive."
        case .pipeCreationFailed(let code):
            return "Unable to create command output pipes (errno \(code))."
        case .spawnFailed(let path, let code):
            return "Unable to launch \(path) (error \(code))."
        }
    }
}

public struct SystemCommandRunner: CommandRunning {
    public init() {}

    public func run(
        _ executable: String,
        arguments: [String],
        timeout: TimeInterval
    ) throws -> CommandResult {
        guard timeout > 0 else { throw CommandRunnerError.invalidTimeout }

        let stdoutPipe = try makePipe()
        let stderrPipe: (read: Int32, write: Int32)
        do {
            stderrPipe = try makePipe()
        } catch {
            Darwin.close(stdoutPipe.read)
            Darwin.close(stdoutPipe.write)
            throw error
        }

        var fileActions: posix_spawn_file_actions_t?
        var attributes: posix_spawnattr_t?
        guard posix_spawn_file_actions_init(&fileActions) == 0 else {
            close(stdoutPipe, stderrPipe)
            throw CommandRunnerError.launchFailed(executable)
        }
        defer { posix_spawn_file_actions_destroy(&fileActions) }
        guard posix_spawnattr_init(&attributes) == 0 else {
            close(stdoutPipe, stderrPipe)
            throw CommandRunnerError.launchFailed(executable)
        }
        defer { posix_spawnattr_destroy(&attributes) }

        let fileActionResults = [
            posix_spawn_file_actions_adddup2(&fileActions, stdoutPipe.write, STDOUT_FILENO),
            posix_spawn_file_actions_adddup2(&fileActions, stderrPipe.write, STDERR_FILENO),
            posix_spawn_file_actions_addclose(&fileActions, stdoutPipe.read),
            posix_spawn_file_actions_addclose(&fileActions, stderrPipe.read),
            posix_spawn_file_actions_addclose(&fileActions, stdoutPipe.write),
            posix_spawn_file_actions_addclose(&fileActions, stderrPipe.write)
        ]
        if let actionError = fileActionResults.first(where: { $0 != 0 }) {
            close(stdoutPipe, stderrPipe)
            throw CommandRunnerError.spawnFailed(executable, actionError)
        }

        let flags = Int16(POSIX_SPAWN_SETPGROUP)
        let attributeResults = [
            posix_spawnattr_setflags(&attributes, flags),
            posix_spawnattr_setpgroup(&attributes, 0)
        ]
        if let attributeError = attributeResults.first(where: { $0 != 0 }) {
            close(stdoutPipe, stderrPipe)
            throw CommandRunnerError.spawnFailed(executable, attributeError)
        }

        let duplicatedArguments = ([executable] + arguments).map { strdup($0) }
        guard duplicatedArguments.allSatisfy({ $0 != nil }) else {
            duplicatedArguments.forEach { pointer in
                if let pointer { free(pointer) }
            }
            close(stdoutPipe, stderrPipe)
            throw CommandRunnerError.launchFailed(executable)
        }
        defer {
            duplicatedArguments.forEach { pointer in
                if let pointer { free(pointer) }
            }
        }
        var argv = duplicatedArguments + [nil]
        var processIdentifier: pid_t = 0
        let spawnCode = executable.withCString { executablePointer in
            argv.withUnsafeMutableBufferPointer { argumentsPointer in
                posix_spawn(
                    &processIdentifier,
                    executablePointer,
                    &fileActions,
                    &attributes,
                    argumentsPointer.baseAddress,
                    environ
                )
            }
        }
        guard spawnCode == 0 else {
            close(stdoutPipe, stderrPipe)
            throw CommandRunnerError.spawnFailed(executable, spawnCode)
        }

        Darwin.close(stdoutPipe.write)
        Darwin.close(stderrPipe.write)
        let stdoutHandle = FileHandle(fileDescriptor: stdoutPipe.read, closeOnDealloc: true)
        let stderrHandle = FileHandle(fileDescriptor: stderrPipe.read, closeOnDealloc: true)

        let stdout = LockedCommandData()
        let stderr = LockedCommandData()
        let drains = DispatchGroup()
        let terminated = DispatchSemaphore(value: 0)
        let processStatus = LockedProcessStatus()
        let spawnedProcessIdentifier = processIdentifier

        drains.enter()
        DispatchQueue.global().async {
            stdout.replace(with: stdoutHandle.readDataToEndOfFile())
            drains.leave()
        }
        drains.enter()
        DispatchQueue.global().async {
            stderr.replace(with: stderrHandle.readDataToEndOfFile())
            drains.leave()
        }
        DispatchQueue.global().async {
            var waitStatus: Int32 = 0
            var waitResult: pid_t
            repeat {
                waitResult = Darwin.waitpid(spawnedProcessIdentifier, &waitStatus, 0)
            } while waitResult == -1 && errno == EINTR
            processStatus.replace(with: waitResult == spawnedProcessIdentifier ? waitStatus : nil)
            terminated.signal()
        }

        let didTimeOut = terminated.wait(timeout: .now() + timeout) == .timedOut
        if didTimeOut {
            Darwin.kill(-spawnedProcessIdentifier, SIGTERM)
            _ = terminated.wait(timeout: .now() + 0.25)
            Darwin.kill(-spawnedProcessIdentifier, SIGKILL)
            _ = terminated.wait(timeout: .now() + 0.25)
        }

        if drains.wait(timeout: .now() + 0.5) == .timedOut {
            stdoutHandle.closeFile()
            stderrHandle.closeFile()
            _ = drains.wait(timeout: .now() + 0.1)
        }

        let exitCode = didTimeOut ? nil : processStatus.status.map(decodeExitCode)
        return CommandResult(
            stdout: stdout.string,
            stderr: stderr.string,
            exitCode: exitCode,
            timedOut: didTimeOut
        )
    }

    private func makePipe() throws -> (read: Int32, write: Int32) {
        var descriptors = [Int32](repeating: 0, count: 2)
        let result = descriptors.withUnsafeMutableBufferPointer { buffer in
            Darwin.pipe(buffer.baseAddress!)
        }
        guard result == 0 else {
            throw CommandRunnerError.pipeCreationFailed(errno)
        }
        return (read: descriptors[0], write: descriptors[1])
    }

    private func close(
        _ stdoutPipe: (read: Int32, write: Int32),
        _ stderrPipe: (read: Int32, write: Int32)
    ) {
        Darwin.close(stdoutPipe.read)
        Darwin.close(stdoutPipe.write)
        Darwin.close(stderrPipe.read)
        Darwin.close(stderrPipe.write)
    }

    private func decodeExitCode(_ waitStatus: Int32) -> Int32 {
        let terminatingSignal = waitStatus & 0x7f
        if terminatingSignal == 0 {
            return (waitStatus >> 8) & 0xff
        }
        return 128 + terminatingSignal
    }
}

private final class LockedCommandData: @unchecked Sendable {
    private let lock = NSLock()
    private var data = Data()

    var string: String {
        lock.lock()
        defer { lock.unlock() }
        return String(data: data, encoding: .utf8) ?? ""
    }

    func replace(with newData: Data) {
        lock.lock()
        data = newData
        lock.unlock()
    }
}

private final class LockedProcessStatus: @unchecked Sendable {
    private let lock = NSLock()
    private var storedStatus: Int32?

    var status: Int32? {
        lock.lock()
        defer { lock.unlock() }
        return storedStatus
    }

    func replace(with status: Int32?) {
        lock.lock()
        storedStatus = status
        lock.unlock()
    }
}
