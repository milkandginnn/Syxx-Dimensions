import Ccage
import Foundation
import SyxxKit
/// Serializes writes to the capture output file with batching to prevent I/O storms.
/// WS8-FIX: Batch writes to reduce per-packet I/O overhead during high-traffic captures.
private actor CaptureWriter {
    private let handle: FileHandle
    private var buffer: [String] = []
    private let batchSize: Int
    private let flushInterval: TimeInterval
    private var lastFlush: Date = Date()
    
    init(handle: FileHandle, batchSize: Int = 100, flushInterval: TimeInterval = 0.1) {
        self.handle = handle
        self.batchSize = batchSize
        self.flushInterval = flushInterval
    }
    
    func write(_ line: String) {
        buffer.append(line)
        // Flush if buffer is full or flush interval elapsed
        if buffer.count >= batchSize || Date().timeIntervalSince(lastFlush) >= flushInterval {
            flush()
        }
    }
    
    func flush() {
        guard !buffer.isEmpty else { return }
        let combined = buffer.joined(separator: "\n") + "\n"
        if let data = combined.data(using: .utf8) {
            handle.write(data)
        }
        buffer.removeAll()
        lastFlush = Date()
    }
    
    // C088: actors cannot perform async work in deinit (FileHandle.write is synchronous
    // but the actor's own buffer must be drained via flush(), which is async). The capture
    // loop calls `await writer.flush()` explicitly after the main loop, and the
    // flushInterval timer drains the buffer every 100 ms during capture. An abnormal exit
    // (SIGINT during a long lull) may lose up to batchSize-1 buffered lines — acceptable
    // for the bounded-duration capture use case.
    deinit {}
}
/// DNS-over-port-53 packet capture via libpcap.
///
/// macOS raw sockets (`SOCK_RAW` + `IPPROTO_UDP`) do not deliver UDP to userspace on
/// Darwin/BSD — only ICMP/IGMP.  This implementation uses libpcap (ships with macOS) and
/// a BPF filter `"udp port 53"` to capture real DNS traffic on the default interface,
/// IPv4 + IPv6 from a single pcap session.
struct SyxxCageCLI {
    private static func string(fromCStringBuffer buffer: [CChar]) -> String {
        let end = buffer.firstIndex(of: 0) ?? buffer.endIndex
        return String(decoding: buffer[..<end].map { UInt8(bitPattern: $0) }, as: UTF8.self)
    }

    /// B253: 0=capture completed, 2=aborted/incomplete evidence, 3=operational failure.
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx cage",
            usage: "Usage: syxx cage [--duration <seconds>] [--keep]   (captures port-53 DNS packets; requires sudo)") {
            return CLIExitStatus.completed.rawValue
        }
        let args = argv
        var duration: Double = 10.0
        var keepRaw = false
        var ifaceOverride: String? = nil
        var outputOverride: String? = nil
        var ownerUid: uid_t? = nil
        var ownerGid: gid_t? = nil
        var idx = 1
        while idx < args.count {
            if args[idx] == "--duration" || args[idx] == "-d" {
                // F028: reject negative, non-finite, zero, and unbounded values.
                // 86400s = 24h capture ceiling (matches Windows CageCommand).
                if idx + 1 < args.count, let val = Double(args[idx + 1]),
                   val.isFinite, val > 0, val <= 86400 {
                    duration = val
                    idx += 1
                } else if idx + 1 < args.count {
                    print("[ERROR] Invalid duration '\(args[idx + 1])'. Must be a finite number between 0 (exclusive) and 86400 (24h).")
                    return CLIExitStatus.failure.rawValue
                }
            } else if args[idx] == "--interface" || args[idx] == "-i" {
                if idx + 1 < args.count {
                    ifaceOverride = args[idx + 1]
                    idx += 1
                }
            } else if args[idx] == "--output" || args[idx] == "-o" {
                // A caller (e.g. the GUI) names the exact live-stream file it will tail.
                if idx + 1 < args.count {
                    outputOverride = args[idx + 1]
                    idx += 1
                }
            } else if args[idx] == "--owner-uid" {
                // The GUI escalates via the SMJobBless privileged helper over XPC, not sudo, so
                // SUDO_UID/GID are unset. It passes its real uid/gid so we can hand the live file
                // back. (Corrected 2026-07-29: this said `osascript ... with administrator
                // privileges`, which the GUI has not used since the helper landed.)
                if idx + 1 < args.count, let val = uid_t(args[idx + 1]) {
                    ownerUid = val
                    idx += 1
                }
            } else if args[idx] == "--owner-gid" {
                if idx + 1 < args.count, let val = gid_t(args[idx + 1]) {
                    ownerGid = val
                    idx += 1
                }
            } else if args[idx] == "--keep" {
                keepRaw = true
            } else if !args[idx].hasPrefix("-"), let val = Double(args[idx]),
                      val.isFinite, val > 0, val <= 86400 {
                duration = val
            }
            idx += 1
        }
        // C076 Fix 3: validate --owner-uid/--owner-gid against SUDO_UID when running under sudo.
        // Root will hand any file it just touched to any uid the caller names — without validation,
        // an attacker-influenced --owner-uid can exfiltrate a root-created file to an arbitrary user.
        if let sudoUidStr = ProcessInfo.processInfo.environment["SUDO_UID"],
           let sudoUid = uid_t(sudoUidStr) {
            if let uid = ownerUid, uid != sudoUid && uid != 0 {
                print("🚨 ERROR: --owner-uid \(uid) does not match SUDO_UID (\(sudoUid)) and is not root (0).")
                print("    Refusing to chown to an unvalidated uid under sudo escalation.")
                return CLIExitStatus.failure.rawValue
            }
        }
        let startTime = Date()
        let fileDateFormatter = DateFormatter()
        fileDateFormatter.dateFormat = "yyyy-MM-dd HH.mm.ss"
        let dateStamp = fileDateFormatter.string(from: startTime)
        let outputFilename = "Syxx Cage Capture \(dateStamp).txt"
        let reportFilename = "Syxx Cage Capture Report \(dateStamp).txt"
        let queriesFilename = "Syxx Cage Queries \(dateStamp).json"
        let effectiveUser: String
        if let sudoUser = ProcessInfo.processInfo.environment["SUDO_USER"], !sudoUser.isEmpty {
            effectiveUser = sudoUser
        } else {
            effectiveUser = NSUserName()
        }
        let homeBase = FileManager.default.homeDirectoryForUser(effectiveUser)
            ?? FileManager.default.homeDirectoryForCurrentUser
        let capturesDir = homeBase
            .appendingPathComponent(".syxx/captures", isDirectory: true)
        try? FileManager.default.createDirectory(at: capturesDir,
            withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let outputFilePath: String
        if let override = outputOverride {
            // C076 Fix 2: reject --output paths that do not resolve inside the validated captures
            // directory. A path in /tmp or another world-writable directory makes the symlink
            // race trivially winnable by any local process.
            let resolved = (override as NSString).standardizingPath
            let capturesDirPath = capturesDir.path
            if !resolved.hasPrefix(capturesDirPath + "/") && resolved != capturesDirPath {
                print("🚨 ERROR: --output path must be inside the captures directory (\(capturesDirPath))")
                print("    Got: \(resolved)")
                return CLIExitStatus.failure.rawValue
            }
            outputFilePath = resolved
        } else {
            outputFilePath = capturesDir.appendingPathComponent(outputFilename).path
        }
        let reportFilePath = capturesDir.appendingPathComponent(reportFilename).path
        let queriesFilePath = capturesDir.appendingPathComponent(queriesFilename).path
        print("==================================================")
        print("           Syxx Cage DNS Query Capturer           ")
        print("==================================================")
        print("Target: Sniffing port 53 DNS Queries (IPv4 + IPv6)...")
        print("Duration: \(duration) seconds")
        print("Output logs will be written to: \(outputFilePath)")
        var errbuf = [CChar](repeating: 0, count: 256)
        let devName: String
        if let override = ifaceOverride {
            devName = override                                   // explicit --interface wins
        } else if let defRoute = Self.defaultRouteInterface() {
            devName = defRoute                                   // the NIC carrying real traffic
        } else if let device = cage_pcap_lookupdev(&errbuf) {
            devName = String(cString: device)                    // last-resort pcap default
            free(device)
        } else {
            print("\n🚨 ERROR: Could not find a capture device: \(Self.string(fromCStringBuffer: errbuf))")
            print("Please run with sudo, or pass --interface <name>.")
            return CLIExitStatus.failure.rawValue
        }
        print("Capturing on interface: \(devName)")
        // D107/D031: prefer a PKTAP-mode capture (pcap_set_want_pktap, what `tcpdump -k` does)
        // so every packet carries the kernel's per-packet metadata header — including the
        // originating process pid/name, which replaces the post-hoc lsof race. Fall back to a
        // plain open if pktap mode can't be established, and say so — never silently.
        // (Instrumented 2026-07-23: opening "pktap,<if>" via plain pcap_open_live yields
        // DLT_RAW with NO metadata, so pktap mode must be requested pre-activation and
        // verified by the actual datalink — cage_pcap_open_pktap does both.)
        var usingPktap = false
        var handle: OpaquePointer? = nil
        var pktapErrbuf = [CChar](repeating: 0, count: 256)
        // Instrumented findings (2026-07-23): want_pktap on the bare interface name activates
        // with DLT_EN10MB (no metadata), and the pktap pseudo-device WITHOUT want_pktap yields
        // DLT_RAW (no metadata, and inbound-only in the live run). Only the combination —
        // pktap pseudo-device restricted to the interface, plus want_pktap pre-activation —
        // delivers DLT_PKTAP with kernel attribution.
        let pktapDevice = devName.hasPrefix("pktap") ? devName : "pktap,\(devName)"
        if let p = Self.openPktapFiltered(device: pktapDevice, errbuf: &pktapErrbuf) {
            handle = p
            usingPktap = true
        } else {
            print("⚠️  pktap metadata capture unavailable (\(Self.string(fromCStringBuffer: pktapErrbuf))); falling back to plain interface capture — process attribution will use a post-hoc port lookup (less reliable).")
            handle = Self.openFiltered(device: devName, errbuf: &errbuf)
        }
        guard let pcap = handle else {
            print("\n🚨 ERROR: Could not open \(devName) for capture: \(Self.string(fromCStringBuffer: errbuf))")
            print("Please run with sudo.")
            return CLIExitStatus.failure.rawValue
        }
        defer { cage_pcap_close(pcap) }
        let dlt = cage_pcap_datalink(pcap)
        // Attribution mode must track the ACTUAL datalink, never the open path we asked for.
        usingPktap = usingPktap && dlt == CAGE_DLT_PKTAP
        // D107 Fix 1 instrumentation: record the real datalink type so a zero-record capture
        // is diagnosable instead of conflated with "no traffic occurred".
        print("Datalink type: \(dlt) (\(usingPktap ? "pktap metadata capture — kernel process attribution" : "plain interface capture — port-lookup process attribution"))")
        // Stream records to disk immediately (M30).
        // D065: Use O_CREAT|O_EXCL|O_NOFOLLOW to prevent symlink race attacks.
        // A local process could replace the path with a symlink between GUI path generation
        // and privileged file creation, causing root to overwrite an arbitrary file.
        // C076 Fix 1: keep the safe fd open — never discard it and re-resolve the pathname.
        // D065 opened with O_CREAT|O_EXCL|O_NOFOLLOW to prevent symlink races, but then
        // immediately closed the fd and reopened by path, re-introducing the race window.
        // Now: create with the safe flags, verify via fstat, then use the fd directly for
        // fchmod/fchown and FileHandle(fileDescriptor:) — the path is never touched again.
        let fd = open(outputFilePath, O_CREAT | O_EXCL | O_NOFOLLOW | O_WRONLY, 0o600)
        guard fd >= 0 else {
            print("🚨 ERROR: Could not create output file (symlink race prevented): \(outputFilePath)")
            return CLIExitStatus.failure.rawValue
        }
        // fstat-verify: must be a regular file, owned by root (we just created it).
        var st = stat()
        guard fstat(fd, &st) == 0, (st.st_mode & S_IFMT) == S_IFREG else {
            print("🚨 ERROR: Output file is not a regular file after creation: \(outputFilePath)")
            close(fd)
            return CLIExitStatus.failure.rawValue
        }
        // C076: fchmod on the fd — never path-based chmod.
        fchmod(fd, 0o600)
        // C076: fchown on the fd — never path-based chown (which follows symlinks).
        fchownToOwner(fd: fd, ownerUid: ownerUid, ownerGid: ownerGid)
        let outputHandle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        defer { try? outputHandle.close() }
        // WS8-FIX: Use batched writer with 100-packet batches and 100ms flush interval
        let writer = CaptureWriter(handle: outputHandle, batchSize: 100, flushInterval: 0.1)
        var captured = false
        // D107 Fix 3: count what the BPF filter delivered vs. what actually decoded, so
        // "captured but unparseable" is disclosed instead of masquerading as a quiet network.
        var packetsSeen = 0
        var recordsWritten = 0
        var unsupportedLinkPackets = 0
        var unsupportedLinkTypeValue: Int32? = nil
        var malformedFrames = 0
        var encodingFailures = 0                          // C040: JSON encode failures (lost evidence)
        var dnsParseFailures = 0                          // C075: DNS payload parse failures (genuine evidence loss)
        var captureAborted = false                        // B253: pcap error is incomplete evidence
        let captureEndTime = Date().addingTimeInterval(duration)
        // D055: record the parent PID at launch. When the GUI or helper terminates the
        // XPC/osascript runner, the parent PID becomes 1 (re-parented to launchd). The
        // capture self-terminates on the next loop iteration instead of running until the
        // --duration elapses as a root orphan.
        let launchParentPID = getppid()
        while Date() < captureEndTime {
            // D055: parent-liveness check — if the parent died, the kernel re-parented us
            // to PID 1 (launchd). Exit cleanly so the capture file is flushed and closed.
            if launchParentPID != 1 && getppid() != launchParentPID {
                print("\n⚠️  Parent process (PID \(launchParentPID)) exited — self-terminating to prevent orphaned capture.")
                break
            }
            var data: UnsafePointer<UInt8>?
            var len: Int32 = 0
            let ret = cage_pcap_next_ex(pcap, &data, &len)
            if ret == 1, let data, len > 0 {
                if !captured {
                    print("\n[SUCCESS] Sniffer started. Capturing packet streams...")
                    captured = true
                }
                packetsSeen += 1
                let outcome = await processPacket(data: Data(bytes: data, count: Int(len)),
                                                  writer: writer, dlt: dlt, usingPktap: usingPktap)
                switch outcome {
                case .recorded:
                    recordsWritten += 1
                case .skipped:
                    break
                case .malformedFrame:
                    malformedFrames += 1
                case .unsupportedLinkType(let value):
                    unsupportedLinkPackets += 1
                    unsupportedLinkTypeValue = value
                case .encodingFailure:
                    encodingFailures += 1
                case .dnsParseFailed:
                    dnsParseFailures += 1
                }
            } else if ret == -1 {
                // C044: emit a distinct marker so the GUI/synthesis can detect capture abort
                // and refuse to render a normal partial report. Previously, the error was printed
                // but the function fell through to ordinary reporting — an aborted capture was
                // presented as "Captured N DNS packet(s)." with no degraded/aborted state.
                print("\n⚠️  pcap error: \(String(cString: cage_pcap_geterr(pcap)))")
                print("CAGE_CAPTURE_ABORTED")
                captureAborted = true
                break
            } else if ret == 0 {
                try? await Task.sleep(nanoseconds: 10_000_000)
            }
            await Task.yield()
        }
        // WS8-FIX: Ensure final flush before closing to capture any buffered data
        await writer.flush()
        if !captured {
            print("\n⚠️  No packets captured in \(duration) second(s).")
        }
        // D107 Fix 3: an unsupported link-layer type must never read as "no traffic".
        if packetsSeen > 0, recordsWritten == 0, unsupportedLinkPackets > 0,
           let linkType = unsupportedLinkTypeValue {
            print("\n⚠️  \(packetsSeen) packets seen, 0 parseable — unsupported link-layer type \(linkType)")
        } else if malformedFrames > 0, !usingPktap {
            // Only meaningful under a BPF-filtered capture, where every frame SHOULD be
            // port-53 UDP. A pktap capture is unfiltered (BPF cannot compile for DLT_PKTAP),
            // so non-IP frames (ARP/EAPOL/...) are expected there, not a decode failure.
            print("\n⚠️  \(malformedFrames) of \(packetsSeen) captured frames were malformed/truncated for link-layer type \(dlt) and could not be decoded.")
        }
        // C040: surface encoding failures — packets that were captured but lost during JSON serialization.
        if encodingFailures > 0 {
            print("\n⚠️  \(encodingFailures) of \(packetsSeen) captured packets could not be JSON-encoded and were dropped from evidence. Capture is INCOMPLETE.")
        }
        // C043: explicit transport-coverage statement — Cage only observes cleartext UDP/TCP port 53.
        // DoH (port 443) and DoT (port 853) are structurally invisible to this capture. An empty
        // capture on a DoH-only host must not read as "no DNS activity occurred".
        print("\n📡 Transport coverage: cleartext DNS (UDP/TCP port 53) only.")
        print("   Encrypted DNS (DoH port 443, DoT port 853, DoQ port 8853) is NOT captured.")
        print("   An empty capture means no cleartext DNS was observed — not that no DNS occurred.")
        print("\n--------------------------------------------------")
        print("🔎 GENERATING FORENSIC INTERPRETATION REPORT...")
        print("--------------------------------------------------")
        // C075: pass capture-loss counters to the report so they appear in PARSE SUMMARY.
        let reportOutput = DNSCaptureReportGenerator.generateReport(
            inputFile: outputFilePath,
            outputFile: outputFilePath,
            reportFile: reportFilePath,
            startTime: startTime,
            durationSeconds: duration,
            interface: devName,
            captureCounters: DNSCaptureReportGenerator.CaptureCounters(
                packetsSeen: packetsSeen,
                recordsWritten: recordsWritten,
                malformedFrames: malformedFrames,
                unsupportedLinkPackets: unsupportedLinkPackets,
                dnsParseFailures: dnsParseFailures,
                encodingFailures: encodingFailures
            )
        )
        print(reportOutput.report)
        // C037: Only claim the report was saved when the write actually succeeded.
        // Previously this was printed unconditionally, even when `try?` silently swallowed
        // a write failure — a critical honesty violation (DELTAS.md §5).
        switch reportOutput.writeResult {
        case .success:
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: reportFilePath)
            chownToOwner(path: reportFilePath, ownerUid: ownerUid, ownerGid: ownerGid)
            print("✅ Diagnosis Report Saved (owner-only): \(reportFilePath)")
        case .failure(let error):
            print("⚠️  Report write FAILED: \(error)")
            print("    The report content was printed above; saving to a fallback path...")
            let fallbackPath = reportFilePath + ".fallback"
            if (try? reportOutput.report.write(toFile: fallbackPath, atomically: true, encoding: .utf8)) != nil {
                try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: fallbackPath)
                chownToOwner(path: fallbackPath, ownerUid: ownerUid, ownerGid: ownerGid)
                print("    Fallback report saved (owner-only): \(fallbackPath)")
            } else {
                print("    Fallback write also failed; the report content above is your only record.")
            }
        case .unknown:
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: reportFilePath)
            chownToOwner(path: reportFilePath, ownerUid: ownerUid, ownerGid: ownerGid)
            print("⚠️  Report write status UNKNOWN; attempting to confirm file presence...")
            if FileManager.default.fileExists(atPath: reportFilePath) {
                print("    Report file exists (owner-only): \(reportFilePath)")
            } else {
                print("    Report file NOT found; the content printed above is your only record.")
            }
        }
        // Raw-data companion (interpretation first, raw evidence second): the user's own captured DNS
        // queries as JSON — a privacy-considered view (process NAME only, no path), always kept, so it
        // survives even when the full forensic dump (with process paths) is erased below.
        var queriesSaved = false
        if let queriesJSON = DNSQueryExport.arrayJSON(fromCaptureFile: outputFilePath),
           (try? queriesJSON.write(toFile: queriesFilePath, atomically: true, encoding: .utf8)) != nil {
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: queriesFilePath)
            chownToOwner(path: queriesFilePath, ownerUid: ownerUid, ownerGid: ownerGid)
            queriesSaved = true
        }
        chownToOwner(path: outputFilePath, ownerUid: ownerUid, ownerGid: ownerGid)
        if keepRaw {
            print("\n🗄  Full forensic capture retained (contains process paths): \(outputFilePath)")
        } else if !queriesSaved {
            // C074: never delete the raw capture when the replacement queries export failed —
            // the raw file is the only surviving evidence artifact. Previously, deletion
            // proceeded regardless, destroying the only copy with no warning.
            print("\n⚠️  Raw queries export failed — retaining the raw capture as the only evidence artifact: \(outputFilePath)")
            print("    Re-run with --keep or fix the export path to produce the queries JSON.")
        } else {
            // C039: Verify absence after deletion and report success/failure/unknown.
            // Previously `try?` swallowed the error and printed "erased" unconditionally —
            // a privacy/honesty violation: sensitive raw capture could remain on disk.
            var deletionSucceeded = false
            do {
                try FileManager.default.removeItem(atPath: outputFilePath)
                deletionSucceeded = true
            } catch {
                print("\n⚠️  Raw capture deletion FAILED: \(error.localizedDescription)")
                print("    The capture file remains on disk with broad permissions: \(outputFilePath)")
                print("    Remove it manually or run: chmod 600 \"\(outputFilePath)\" && rm \"\(outputFilePath)\"")
            }
            if deletionSucceeded && !FileManager.default.fileExists(atPath: outputFilePath) {
                print("\n🧹 Full forensic capture erased after reporting (pass --keep to retain it).")
            } else if deletionSucceeded {
                print("\n⚠️  Deletion returned success but file still exists: \(outputFilePath)")
                print("    This may indicate a filesystem or permission issue.")
            }
        }
        if queriesSaved {
            print("🗂  Your raw queries saved as JSON (owner-only): \(queriesFilePath)")
        }
        print("==================================================")
        // B253: aborted or total evidence loss is inconclusive, not clean.
        if captureAborted || encodingFailures > 0 {
            return CLIExitStatus.inconclusive.rawValue
        }
        if packetsSeen > 0, recordsWritten == 0, unsupportedLinkPackets > 0 {
            return CLIExitStatus.inconclusive.rawValue
        }
        return CLIExitStatus.completed.rawValue
    }
    /// The interface carrying the default route — the one real DNS traffic egresses. pcap's device
    /// enumeration lists up+addressed pseudo-interfaces (ap1/awdl0/utun) first, so ask the routing
    /// table instead of guessing from interface flags.
    static func defaultRouteInterface() -> String? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/sbin/route")
        p.arguments = ["-n", "get", "default"]
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        guard let out = String(data: data, encoding: .utf8) else { return nil }
        for line in out.split(separator: "\n") {
            let t = line.trimmingCharacters(in: .whitespaces)
            if t.hasPrefix("interface:") {
                let name = t.dropFirst("interface:".count).trimmingCharacters(in: .whitespaces)
                return name.isEmpty ? nil : name
            }
        }
        return nil
    }
    /// Open `device` for live capture and apply the port-53 BPF filter. Returns nil (with the
    /// failing stage's message in `errbuf` — cage_pcap_compile_filter prefixes it) on any failure,
    /// closing the half-opened handle so the caller can retry another device cleanly.
    private static func openFiltered(device: String, errbuf: inout [CChar]) -> OpaquePointer? {
        guard let handle = cage_pcap_open_live(device, 65535, 0, 250, &errbuf) else { return nil }
        // C019: capture both UDP and TCP port 53 — TCP DNS is used for large responses
        // (DNSSEC, zone transfers) and when UDP is truncated (TC bit set).
        guard cage_pcap_compile_filter(handle, "port 53", 1, 0, &errbuf) == 0 else {
            cage_pcap_close(handle)
            return nil
        }
        return handle
    }
    /// PKTAP-mode open: requests the kernel metadata header pre-activation and only succeeds
    /// when the activated handle's datalink really is DLT_PKTAP. NO BPF filter is applied —
    /// Apple's libpcap cannot compile filters against DLT_PKTAP (live finding 2026-07-23:
    /// `pcap_compile: Apple DLT_PKTAP link-layer type filtering not implemented`), so port-53
    /// selection happens in userspace via `processPacket`'s existing port guard. All interface
    /// traffic crosses to userspace for the capture window; acceptable for Cage's bounded
    /// durations, and the price of kernel-attributed packets.
    private static func openPktapFiltered(device: String, errbuf: inout [CChar]) -> OpaquePointer? {
        return cage_pcap_open_pktap(device, 65535, 250, &errbuf)
    }
    // MARK: - Packet processing
    /// What became of one captured frame — accumulated by the capture loop so undecodable
    /// traffic is disclosed (D107 Fix 3) instead of silently vanishing.
    private enum PacketOutcome {
        case recorded                       // a DNS record was parsed and written
        case skipped                        // decoded fine but not a recordable DNS packet (or pktap metadata)
        case malformedFrame                 // truncated/garbled for the claimed link type
        case unsupportedLinkType(Int32)     // no decoder for this DLT — never Ethernet-assumed
        case encodingFailure                // C040: JSON encode failed — record lost from evidence
        case dnsParseFailed                 // C075: IP/UDP header parsed but DNS payload failed to parse — genuine evidence loss
    }
    /// Process one raw link-layer frame from pcap. Frame location is delegated to
    /// `DNSCapture.decodeLinkFrame` (NULL/Ethernet/PPP/RAW/PKTAP; unknown DLTs are surfaced,
    /// never assumed to be Ethernet — D107). Attribution: on a pktap capture the kernel's
    /// in-flow originator pid/name is used (D031); the post-hoc port lookup remains only as
    /// the explicit last resort for plain (non-pktap) captures.
    private static func processPacket(data: Data, writer: CaptureWriter, dlt: Int32,
                                      usingPktap: Bool) async -> PacketOutcome {
        let ipPayload: Data
        let ipVersion: UInt8
        let attribution: PktapHeader?
        switch DNSCapture.decodeLinkFrame(data, dlt: dlt) {
        case .frame(let payload, let version, let meta):
            ipPayload = payload
            ipVersion = version
            attribution = meta
        case .nonPacketMetadata:
            return .skipped
        case .malformed:
            return .malformedFrame
        case .unsupportedLinkType(let value):
            return .unsupportedLinkType(value)
        }
        guard let (context, payloadOffset, dnsPayloadLength) = DNSCapture.parseIPUDPHeader(ipPayload),
              context.ipVersion == Int(ipVersion),
              context.sourcePort == 53 || context.destPort == 53,
              payloadOffset + dnsPayloadLength <= ipPayload.count
        else { return .skipped }
        // C024/C029: slice exactly the validated DNS payload length, not the buffer tail —
        // this excludes Ethernet padding and any trailing bytes from the DNS payload.
        let dnsPayload = ipPayload.subdata(in: payloadOffset..<(payloadOffset + dnsPayloadLength))
        // C075: a port-53 UDP packet whose DNS payload fails to parse is genuine evidence loss,
        // not a routine skip. Surface it as a distinct outcome so the counter reaches the report.
        guard let dnsReport = DNSCapture.parseDNSPacket(dnsPayload) else { return .dnsParseFailed }
        let proc: ProcessInfoReport
        if usingPktap {
            // Kernel-supplied attribution captured in-flow with the packet — no socket-lifetime
            // race. A nil pid (kernel could not attribute) resolves to .unknown, never a guess.
            proc = DNSCapture.pidToProcessInfo(pid: attribution?.originatorPID,
                                               name: attribution?.originatorName)
        } else {
            // Last-resort fallback for plain captures only: post-hoc port lookup (D031's race —
            // it can miss or misattribute a short-lived socket; pktap mode avoids it entirely).
            let localPort = context.sourcePort == 53 ? context.destPort : context.sourcePort
            proc = DNSCapture.getProcessInfo(forPort: localPort)
        }
        // C042: determine direction from the PORT (which endpoint is port 53), not the
        // attacker-controlled QR header bit. The port is a property of the transport layer
        // (not forgeable in the DNS payload); QR is untrusted payload metadata.
        // A QR/port conflict (e.g. QR says "Query" but destPort != 53) means the packet is
        // malformed/spoofed — mark direction unknown rather than trusting either signal alone.
        let portSaysOutbound = context.destPort == 53
        let portSaysInbound = context.sourcePort == 53
        let direction: String
        if portSaysOutbound && !portSaysInbound {
            direction = "Outbound Query"
        } else if portSaysInbound && !portSaysOutbound {
            direction = "Inbound Response"
        } else {
            // Neither or both ports are 53 — ambiguous; do not trust QR as a tiebreaker.
            direction = "Unknown Direction"
        }
        let record = DNSCaptureRecord(
            process: proc,
            timestamp: formattedTimestamp(),
            packet: dnsReport,
            network: NetworkContextReport(
                ipVersion: context.ipVersion,
                sourceIP: context.sourceIP,
                destIP: context.destIP,
                sourcePort: context.sourcePort,
                destPort: context.destPort,
                direction: direction
            )
        )
        let encoder = JSONEncoder()
        // C040: count encoding failures instead of silently dropping the record.
        // A failed encode means a captured packet disappears from evidence.
        guard let jsonData = try? encoder.encode(record),
              let jsonLine = String(data: jsonData, encoding: .utf8) else { return .encodingFailure }
        let procLabel = proc.name ?? "unknown"
        let resolverIP = context.sourcePort == 53 ? context.sourceIP : context.destIP
        print("  • [\(record.timestamp)] \(direction): \(dnsReport.questions.first?.questionName ?? "unknown") (via \(procLabel) | Rcode: \(dnsReport.rcode) | Resolver: \(resolverIP))")
        // C088: pass the line without a trailing newline — flush() already joins with "\n"
        // and appends a final "\n", so the old "+ "\n"" produced a blank line after every record.
        await writer.write(jsonLine)
        return .recorded
    }
    private static func formattedTimestamp() -> String {
        let fmt = DateFormatter()
        fmt.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
        return fmt.string(from: Date())
    }
    /// Chown a file back to the invoking user. Explicit `--owner-uid/--owner-gid` win (the GUI's
    /// privileged-helper escalation leaves SUDO_* unset); otherwise fall back to SUDO_UID/SUDO_GID for a
    /// sudo-launched run. A no-op when neither is available (e.g. an unprivileged run owns its files).
    private static func chownToOwner(path: String, ownerUid: uid_t?, ownerGid: gid_t?) {
        if let uid = ownerUid, let gid = ownerGid {
            _ = chown(path, uid, gid)
            return
        }
        guard let sudoUid = ProcessInfo.processInfo.environment["SUDO_UID"],
              let sudoGid = ProcessInfo.processInfo.environment["SUDO_GID"],
              let uid = uid_t(sudoUid),
              let gid = gid_t(sudoGid) else { return }
        _ = chown(path, uid, gid)
    }
    /// C076 Fix 1: fd-based chown — uses fchown() which does NOT follow symlinks, unlike
    /// chown() which resolves the path. Used with the safe fd from O_CREAT|O_EXCL|O_NOFOLLOW.
    private static func fchownToOwner(fd: Int32, ownerUid: uid_t?, ownerGid: gid_t?) {
        if let uid = ownerUid, let gid = ownerGid {
            _ = fchown(fd, uid, gid)
            return
        }
        guard let sudoUid = ProcessInfo.processInfo.environment["SUDO_UID"],
              let sudoGid = ProcessInfo.processInfo.environment["SUDO_GID"],
              let uid = uid_t(sudoUid),
              let gid = gid_t(sudoGid) else { return }
        _ = fchown(fd, uid, gid)
    }
}
private extension FileManager {
    func homeDirectoryForUser(_ user: String) -> URL? {
        let home = NSHomeDirectoryForUser(user)
        return home.map { URL(fileURLWithPath: $0) }
    }
}
