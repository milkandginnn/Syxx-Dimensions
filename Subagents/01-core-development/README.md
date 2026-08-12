# Core Development Subagents

Core Development subagents are your essential toolkit for building modern applications from the ground up. These specialized agents cover the entire development spectrum - from backend services to frontend interfaces, from mobile apps to desktop applications, and from simple APIs to complex distributed systems.

## 🎯 When to Use Core Development Subagents

Use these subagents when you need to:
- **Build new applications** from scratch with proper architecture
- **Implement complex features** that require deep technical expertise  
- **Design scalable systems** that can grow with your needs
- **Create beautiful UIs** that provide exceptional user experiences
- **Develop real-time features** for interactive applications
- **Modernize legacy systems** with current best practices
- **Optimize performance** across the entire stack

## 📋 Available Subagents

### [**api-designer**](api-designer.md) - REST and GraphQL API architect
The architect who designs beautiful, intuitive, and scalable APIs. Expert in RESTful principles, GraphQL schemas, API versioning, and documentation. Ensures your APIs are developer-friendly and future-proof.

**Use when:** Designing new APIs, refactoring existing endpoints, implementing API standards, or creating comprehensive API documentation.

### [**backend-developer**](backend-developer.md) - Server-side expert for scalable APIs
Your go-to specialist for building robust server applications, RESTful APIs, and microservices. Excels at database design, authentication systems, and performance optimization. Perfect for creating the backbone of your application with Node.js, Python, Java, or other backend technologies.

**Use when:** Building APIs, designing databases, implementing authentication, handling business logic, or optimizing server performance.

### [**native-desktop-integration-implementer**](native-desktop-integration-implementer.md) - Native OS integration and hardening implementer
Owns the boundary between the application and the operating system on Windows and Linux (C#/.NET), coordinating with the macOS specialist - privilege separation and authenticated local IPC, sandboxing and AppContainer, OS credential stores, filesystem boundaries, portals, and update-channel integrity. Replaces web-runtime desktop shells with native equivalents.

**Use when:** Something needs elevation and you want the smallest safe grant, a local IPC endpoint does not verify its peer, secrets are stored somewhere they should not be, or an update channel needs integrity guarantees.

**Prefer instead:** **macos-platform-services-implementer** for macOS system frameworks and XPC; **desktop-app-delivery-implementer** for signing and packaging; **security-auditor** when you want an assessment rather than an implementation.

### [**desktop-ui-implementer**](desktop-ui-implementer.md) - Multi-platform desktop UI implementer
Builds the same surface across stacks in one pass - Swift 6/SwiftUI on macOS, C#/WinUI 3 on Windows, C#/Avalonia on Linux - keeping layout, states, wording, and accessibility identical where they should be and platform-native where they must differ.

**Use when:** A UI change must land on two or three desktop platforms together, a surface needs porting from one stack to another, or platform drift is being closed by building the missing side.

**Prefer instead:** the per-stack specialists (**swiftui-component-implementer**, **winui3-xaml-implementer**, **avalonia-ui-implementer**) when only one platform changes; **gui-cross-platform-continuity** when you want drift audited rather than closed with code.

### [**desktop-feature-implementer**](desktop-feature-implementer.md) - End-to-end desktop feature owner
Delivers a complete vertical slice - storage and data model, domain logic, view model or observable state, and the UI - on macOS (Swift 6/SwiftUI) and Windows/Linux (C#/WinUI 3/Avalonia), with migration, error paths, and tests landing in the same change.

**Use when:** A feature genuinely spans storage, logic, and UI; a schema change and a screen must ship together; or the layers keep going out of sync because nobody owns the whole slice.

**Prefer instead:** **desktop-ui-implementer** when only the UI changes across platforms; **dotnet-mvvm-implementer** when only the state layer changes; **database-optimizer** for query tuning on an existing schema.

### [**desktop-design-system-designer**](desktop-design-system-designer.md) - Desktop design system designer (tokens, components, theming)
Defines the system beneath the screens: role-named colour and state tokens, typography ramps, spacing and radius scales, elevation and materials, motion rules, and per-component specifications - each mapped explicitly onto SwiftUI, WinUI 3 theme resources, and Avalonia resource dictionaries.

**Use when:** Colours or sizes are being hard-coded because no token exists, the platforms have drifted visually, a component needs specifying before anyone builds it, or contrast and High Contrast support are in question.

**Prefer instead:** **desktop-ui-designer** when one specific screen needs laying out; **accessibility-tester** when you want a built surface audited rather than a system defined.

### [**graphql-architect**](graphql-architect.md) - GraphQL schema and federation expert
Specialized in GraphQL ecosystem, from schema design to federation strategies. Masters resolver optimization, subscription patterns, and GraphQL best practices. Perfect for building flexible, efficient data layers.

**Use when:** Implementing GraphQL APIs, designing schemas, optimizing resolvers, setting up federation, or migrating from REST to GraphQL.

### [**microservices-architect**](microservices-architect.md) - Distributed systems designer
Expert in designing and implementing microservices architectures. Handles service decomposition, inter-service communication, distributed transactions, and orchestration. Ensures your system scales horizontally with resilience.

**Use when:** Breaking monoliths into microservices, designing distributed systems, implementing service mesh, or solving distributed system challenges.

### [**macos-platform-services-implementer**](macos-platform-services-implementer.md) - macOS platform services implementer (Swift 6)
Writes the non-UI platform layer against Apple system frameworks - Network.framework, NetworkExtension, Keychain, XPC, launchd, UserNotifications - and owns sandboxing, entitlements, privilege separation, background execution, and energy budgets.

**Use when:** The work is Swift below the UI, an entitlement or sandbox decision must be made and implemented, a privileged operation needs a validated helper, or background work must run on a power budget.

**Prefer instead:** **macos-swiftui-implementer** for scenes and windows; **swift6-concurrency-implementer** for isolation and Sendable diagnostics; **desktop-app-delivery-implementer** once the code works and needs signing or notarization.

### [**macos-swiftui-implementer**](macos-swiftui-implementer.md) - macOS app-shell implementer (Swift 6 / SwiftUI)
Builds the scene, window, navigation, and AppKit-interop layer of native macOS applications - WindowGroup and MenuBarExtra scenes, NavigationSplitView, commands and keyboard shortcuts, NSViewRepresentable bridging, and Human Interface Guidelines compliance.

**Use when:** Windows, scenes, navigation, or menus need adding or fixing; AppKit must be bridged into SwiftUI; or the app misbehaves on quit, relaunch, multi-window, or deep link.

**Prefer instead:** **swiftui-component-implementer** when the work is one view's layout, state, or animation; **macos-platform-services-implementer** when the work is networking, Keychain, or entitlements.

### [**desktop-ui-designer**](desktop-ui-designer.md) - Desktop screen designer (macOS HIG / Fluent / Avalonia)
Designs the screens themselves: information architecture, layout and visual hierarchy, the complete state matrix for every surface, the keyboard and interaction model, and the user-facing copy. Produces mockups, specs, and state matrices - it writes no production SwiftUI or XAML.

**Use when:** A screen needs designing or restructuring, nobody has decided what the empty/error/degraded state looks like, a surface reads as more confident than the data supports, or in-product wording is unclear.

**Prefer instead:** **desktop-design-system-designer** when the gap is tokens, palettes, or component specs rather than a screen; the per-stack implementers once the design exists; **ux-researcher** when the question is what users need rather than how to present it.

### [**websocket-engineer**](websocket-engineer.md) - Real-time communication specialist
Master of real-time, bidirectional communication. Implements WebSocket servers, manages connections at scale, and handles real-time features like chat, notifications, and live updates. Expert in Socket.io and native WebSocket implementations.

**Use when:** Building chat applications, implementing real-time notifications, creating collaborative features, or developing live-updating dashboards.

### [**wordpress-master**](wordpress-master.md) - WordPress development and optimization expert
Specialist in WordPress ecosystem who builds everything from simple blogs to enterprise platforms. Masters theme development, plugin architecture, Gutenberg blocks, and performance optimization. Expert in both classic PHP development and modern block-based solutions.

**Use when:** Building WordPress sites, developing custom themes, creating plugins, implementing WooCommerce solutions, or optimizing WordPress performance.

## 🚀 Quick Selection Guide

| If you need to... | Use this subagent |
|-------------------|-------------------|
| Build a REST API with database | **backend-developer** |
| Create a responsive web UI | **desktop-ui-implementer** |
| Develop a complete web application | **desktop-feature-implementer** |
| Build a mobile app | **macos-platform-services-implementer** |
| Design user interfaces | **desktop-ui-designer** |
| Create a desktop application | **native-desktop-integration-implementer** |
| Design a new API structure | **api-designer** |
| Implement GraphQL | **graphql-architect** |
| Build a distributed system | **microservices-architect** |
| Add real-time features | **websocket-engineer** |
| Create a WordPress site | **wordpress-master** |

## 💡 Common Combinations

**Full-Stack Web Application:**
- Start with **api-designer** for API structure
- Use **backend-developer** for server implementation  
- Employ **desktop-ui-implementer** for UI development

**Enterprise System:**
- Begin with **microservices-architect** for system design
- Use **graphql-architect** for data layer
- Add **backend-developer** for service implementation

**Real-time Application:**
- Start with **websocket-engineer** for real-time infrastructure
- Add **backend-developer** for business logic
- Use **desktop-ui-implementer** for interactive UI

**Design-Driven Development:**
- Begin with **desktop-ui-designer** for visual design and prototypes
- Use **desktop-ui-implementer** for implementation
- Add **accessibility-tester** for compliance validation

**WordPress Project:**
- Start with **wordpress-master** for architecture and setup
- Add **php-pro** for custom PHP development
- Use **desktop-ui-implementer** for custom JavaScript

## 🎬 Getting Started

1. **Choose the right subagent** based on your specific needs
2. **Provide clear context** about your project requirements
3. **Specify your tech stack** preferences if any
4. **Describe your constraints** (performance, scalability, timeline)
5. **Let the subagent guide you** through best practices and implementation

Each subagent comes with:
- Deep expertise in their domain
- Knowledge of current best practices
- Ability to work with your existing codebase
- Focus on clean, maintainable code
- Understanding of production requirements

## 📚 Best Practices

- **Start with architecture:** Use architects (API, GraphQL, Microservices) before implementation
- **Iterate frequently:** Work with subagents in short cycles for better results
- **Combine expertise:** Use multiple subagents for complex projects
- **Follow conventions:** Each subagent knows the best practices for their domain
- **Think production-ready:** All subagents consider scalability, security, and maintenance

Choose your subagent and start building amazing applications today!