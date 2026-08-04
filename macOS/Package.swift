// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Syxx",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .library(name: "SyxxKit", targets: ["SyxxKit"]),
        .library(name: "SyxxApp", targets: ["SyxxApp"]),
        .executable(name: "syxx", targets: ["SyxxCLI"]),
        .executable(name: "syxx-gui", targets: ["SyxxGUI"]),
        .executable(name: "DeepSyxx", targets: ["DeepSyxx"])
    ],
    dependencies: [
        .package(url: "https://github.com/open-telemetry/opentelemetry-swift", from: "2.5.0"),
        .package(url: "https://github.com/open-telemetry/opentelemetry-swift-core.git", from: "2.5.1"),
        .package(url: "https://github.com/getsentry/sentry-cocoa", from: "8.43.0")
    ],
    targets: [
        .target(
            name: "SyxxKit",
            dependencies: [
                .product(name: "OpenTelemetryApi", package: "opentelemetry-swift-core"),
                .product(name: "OpenTelemetrySdk", package: "opentelemetry-swift-core")
            ]
        ),
        .testTarget(
            name: "SyxxKitTests",
            dependencies: [
                "SyxxKit",
                // Needed to register a real TracerProviderSdk and read finished spans, so the
                // telemetry-honesty tests assert on what is actually EXPORTED rather than on the
                // return value. Without this the S1 regression (a timeout-carrying result scored
                // as a successful measurement) is invisible to the suite.
                .product(name: "OpenTelemetrySdk", package: "opentelemetry-swift-core")
            ]
        ),
        .target(
            name: "SyxxTelemetry",
            dependencies: [
                "SyxxKit",
                .product(name: "OpenTelemetryApi", package: "opentelemetry-swift-core"),
                .product(name: "OpenTelemetrySdk", package: "opentelemetry-swift-core"),
                .product(name: "OpenTelemetryProtocolExporterHTTP", package: "opentelemetry-swift"),
                .product(name: "ResourceExtension", package: "opentelemetry-swift"),
                .product(name: "Sentry", package: "sentry-cocoa")
            ]
        ),
        .executableTarget(
            name: "SyxxCLI",
            dependencies: ["SyxxKit", "SyxxTelemetry", "Ccage"]
        ),
        .target(
            name: "Ccage",
            dependencies: [],
            linkerSettings: [.linkedLibrary("pcap")]
        ),
        .target(
            name: "SyxxApp",
            dependencies: [
                "SyxxKit",
                "SyxxTelemetry"
            ],
            resources: [.copy("Resources")],
            linkerSettings: [
                .linkedFramework("NetworkExtension"),
                .linkedFramework("Security"),
                .linkedFramework("ServiceManagement")
            ]
        ),
        .executableTarget(
            name: "SyxxGUI",
            dependencies: ["SyxxApp"]
        ),
        .testTarget(
            name: "SyxxAppTests",
            dependencies: ["SyxxApp", "SyxxKit"]
        ),
        .executableTarget(
            name: "DeepSyxx",
            dependencies: ["SyxxKit"],
            // These three are consumed by the linker (-sectcreate, below) and by codesign in
            // bundle-app.sh, never by SwiftPM. Excluding them silences the "unhandled files"
            // warning WITHOUT declaring them as resources — copying them next to the binary
            // is precisely the sidecar mistake that caused D079.
            exclude: [
                "DeepSyxx-Info.plist",
                "com.milkandgin.deepsyxx.plist",
                "DeepSyxx.entitlements"
            ],
            linkerSettings: [
                // SMJobBless requires BOTH plists to be embedded in the helper executable
                // itself: __TEXT,__info_plist (SMAuthorizedClients + CFBundleIdentifier) and
                // __TEXT,__launchd_plist (Label + MachServices + ProgramArguments). A sidecar
                // .plist file next to the binary is NOT read by SMJobBless; omitting the
                // embedded launchd plist makes the bless fail with kSMErrorJobPlistNotFound
                // (CFErrorDomainLaunchd 8). See D079.
                .unsafeFlags([
                    "-Xlinker", "-sectcreate",
                    "-Xlinker", "__TEXT",
                    "-Xlinker", "__info_plist",
                    "-Xlinker", "Sources/DeepSyxx/DeepSyxx-Info.plist",
                    "-Xlinker", "-sectcreate",
                    "-Xlinker", "__TEXT",
                    "-Xlinker", "__launchd_plist",
                    "-Xlinker", "Sources/DeepSyxx/com.milkandgin.deepsyxx.plist"
                ])
            ]
        )
    ]
)
