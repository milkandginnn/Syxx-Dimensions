# Dependencies Audit for Syxx

This document tracks all external wrappers over unix/system commands, homebrew/github/python repositories, or external binaries used in the Syxx codebase for both macOS and Windows platforms.

## Dependency Specifications

**Dependencies**

*lsof (macOS)*

- Source: Standard Darwin/macOS operating system diagnostic utility.
- Publisher: Apple Inc. / BSD Community (originally authored by Victor Abell).
- License: BSD License.
- Wrapped or edited status: Wrapped. The system binary is executed as a subprocess to map open UDP ports to PIDs. Original code is unmodified.
- Modules used: [macOS/Sources/SyxxKit/DNSCapture.swift](file:///Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxKit/DNSCapture.swift#L368-L385).
- Audit Date: 2026-07-05.

*ps (macOS)*

- Source: Standard Darwin/macOS operating system utility.
- Publisher: Apple Inc. / BSD Community.
- License: BSD License.
- Wrapped or edited status: Wrapped. Executed as a subprocess to map PIDs to executable binary paths. Original code is unmodified.
- Modules used: [macOS/Sources/SyxxKit/DNSCapture.swift](file:///Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxKit/DNSCapture.swift#L391-L409).
- Audit Date: 2026-07-05.

*scutil (macOS)*

- Source: Standard Darwin/macOS system configuration utility.
- Publisher: Apple Inc.
- License: Apple Public Source License (APSL) / Proprietary.
- Wrapped or edited status: Wrapped. Executed as a subprocess to fetch current active DNS resolvers. Original code is unmodified.
- Modules used: [macOS/Sources/SyxxKit/NetworkKeyState.swift](file:///Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxKit/NetworkKeyState.swift#L114-L116).
- Audit Date: 2026-07-05.

*traceroute (macOS)*

- Source: Standard Darwin/macOS network routing utility.
- Publisher: Apple Inc. / LBNL.
- License: BSD License.
- Wrapped or edited status: Wrapped. Executed as a subprocess to trace paths and latency. Original code is unmodified.
- Modules used: 
  - [macOS/Sources/SyxxRoute/SyxxRoute.swift](file:///Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxRoute/SyxxRoute.swift#L23-L54)
  - [macOS/Sources/SyxxApp/AppModel.swift](file:///Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxApp/AppModel.swift#L224-L234)
  - [macOS/Sources/SyxxCLI/SyxxCLI.swift](file:///Users/masterrabbit/Software/Syxx/macOS/Sources/SyxxCLI/SyxxCLI.swift#L229-L239)
- Audit Date: 2026-07-05.

*netstat (Windows)*

- Source: Standard Windows operating system networking utility.
- Publisher: Microsoft Corporation.
- License: Microsoft Software License Terms (Windows OS).
- Wrapped or edited status: Wrapped. Executed via `cmd.exe` to map open UDP ports to PIDs. Original code is unmodified.
- Modules used: [Windows/Syxx.Kit/DNSCapture.cs](file:///Users/masterrabbit/Software/Syxx/Windows/Syxx.Kit/DNSCapture.cs#L410-L435).
- Audit Date: 2026-07-05.

*arp (Windows)*

- Source: Standard Windows operating system networking utility.
- Publisher: Microsoft Corporation.
- License: Microsoft Software License Terms (Windows OS).
- Wrapped or edited status: Wrapped. Executed to parse local MAC-to-IP entries for LAN scanning. Original code is unmodified.
- Modules used: [Windows/Syxx.Kit/LAN/LanCartography.cs](file:///Users/masterrabbit/Software/Syxx/Windows/Syxx.Kit/LAN/LanCartography.cs#L316-L344).
- Audit Date: 2026-07-05.

*tracert (Windows)*

- Source: Standard Windows operating system network diagnostic utility.
- Publisher: Microsoft Corporation.
- License: Microsoft Software License Terms (Windows OS).
- Wrapped or edited status: Wrapped. Executed as a subprocess to trace network hops. Original code is unmodified.
- Modules used:
  - [Windows/SyxxRoute/Program.cs](file:///Users/masterrabbit/Software/Syxx/Windows/SyxxRoute/Program.cs#L29-L79)
  - [Windows/Syxx/Program.cs](file:///Users/masterrabbit/Software/Syxx/Windows/Syxx/Program.cs#L265-L296)
- Audit Date: 2026-07-05.
