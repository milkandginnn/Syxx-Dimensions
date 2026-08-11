// swift-tools-version: 6.0
import PackageDescription

// Root-level Swift package for the Syxx repository.
//
// WHY THIS EXISTS
// The Swift sources live under `macOS/`, and `macOS/Package.swift` is the canonical package that
// CI builds. That works, but it means Xcode (and `swift build` from the repo root) can only see a
// subtree: open the repository root and you get no Swift project at all, so the Python server,
// the .NET twins, the corpora, the ledgers and the scripts are invisible from the IDE that has
// the Swift code open — on a project whose central rule is that a macOS change needs its Windows
// twin, that is exactly the wrong default.
//
// This manifest declares the SAME products and targets, with `path:` pointing down into
// `macOS/`, so the repo root is openable as one project and every sibling tree is visible beside
// the Swift code.
//
// RELATIONSHIP TO macOS/Package.swift — read before editing either file:
//   • `macOS/Package.swift` remains the CANONICAL manifest. CI builds it
//     (.github/workflows/ci.yml), `macOS/scripts/build_dmg.sh` builds it, and every command in
//     the docs is `cd macOS && swift build`. Nothing here changes that.
//   • This file is a SUPERSET VIEW over the identical sources. It compiles the same files from
//     the same paths; it does not copy, symlink, or fork them.
//   • The two manifests MUST be kept in step. A target, dependency, resource or linker setting
//     added to one and not the other produces a package that builds from `macOS/` and fails from
//     the root (or worse, builds both ways with different settings — a divergence that would
//     surface as an unreproducible bug). `RootPackageParityTests` in SyxxKitTests asserts the two
//     manifests declare the same target set and dependencies, so drift fails the suite rather
//     than waiting to be discovered.
//   • Build artifacts land in `.build/` at the root, separate from `macOS/.build/`. Both are
//     gitignored. Expect a full rebuild the first time you switch between them.
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
        .package(url: "https://github.com/getsentry/sentry-cocoa", from: "9.0.0")
    ],
    targets: [
        .target(
            name: "SyxxKit",
            dependencies: [
                .product(name: "OpenTelemetryApi", package: "opentelemetry-swift-core"),
                .product(name: "OpenTelemetrySdk", package: "opentelemetry-swift-core")
            ],
            path: "macOS/Sources/SyxxKit"
        ),
        .testTarget(
            name: "SyxxKitTests",
            dependencies: [
                "SyxxKit",
                // Registers a real TracerProviderSdk and reads finished spans, so the
                // telemetry-honesty tests assert on what is actually EXPORTED rather than on the
                // return value.
                .product(name: "OpenTelemetrySdk", package: "opentelemetry-swift-core")
            ],
            path: "macOS/Tests/SyxxKitTests"
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
            ],
            path: "macOS/Sources/SyxxTelemetry"
        ),
        .executableTarget(
            name: "SyxxCLI",
            dependencies: ["SyxxKit", "SyxxTelemetry", "Ccage"],
            path: "macOS/Sources/SyxxCLI"
        ),
        .target(
            name: "Ccage",
            dependencies: [],
            path: "macOS/Sources/Ccage",
            linkerSettings: [.linkedLibrary("pcap")]
        ),
        .target(
            name: "SyxxApp",
            dependencies: [
                "SyxxKit",
                "SyxxTelemetry"
            ],
            path: "macOS/Sources/SyxxApp",
            resources: [.copy("Resources")],
            linkerSettings: [
                .linkedFramework("NetworkExtension"),
                .linkedFramework("Security"),
                .linkedFramework("ServiceManagement")
            ]
        ),
        .executableTarget(
            name: "SyxxGUI",
            dependencies: ["SyxxApp"],
            path: "macOS/Sources/SyxxGUI"
        ),
        .testTarget(
            name: "SyxxAppTests",
            dependencies: ["SyxxApp", "SyxxKit"],
            path: "macOS/Tests/SyxxAppTests"
        ),
        .executableTarget(
            name: "DeepSyxx",
            dependencies: ["SyxxKit"],
            path: "macOS/Sources/DeepSyxx",
            linkerSettings: [
                .unsafeFlags([
                    "-Xlinker", "-sectcreate",
                    "-Xlinker", "__TEXT",
                    "-Xlinker", "__info_plist",
                    "-Xlinker", "macOS/Sources/DeepSyxx/DeepSyxx-Info.plist"
                ])
            ]
        )
    ]
)
