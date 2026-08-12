# Language Specialists Subagents

Language Specialists are your expert guides for specific programming languages and their ecosystems. These subagents bring deep knowledge of language idioms, best practices, performance optimization techniques, and framework expertise. Whether you're working with modern web frameworks, system programming languages, or enterprise platforms, these specialists ensure you're writing idiomatic, efficient, and maintainable code.

## When to Use Language Specialists

Use these subagents when you need to:
- **Master language-specific features** and advanced patterns
- **Optimize performance** using language-specific techniques
- **Implement framework best practices** for production applications
- **Migrate or modernize** existing codebases
- **Solve language-specific challenges** with expert guidance
- **Learn advanced patterns** and idioms of a language
- **Build framework-specific applications** with confidence

## Available Subagents

### [**dotnet-mvvm-implementer**](dotnet-mvvm-implementer.md) - .NET desktop MVVM and architecture implementer
Owns the application architecture beneath the XAML - view-model design, dependency injection, navigation and messaging services, state ownership, validation, async command patterns, and modular project structure - keeping the view-model layer free of any UI-framework type so one architecture serves both WinUI 3 and Avalonia.

**Use when:** Logic is leaking into code-behind, dependencies are resolved by a service locator, the Windows and Linux UIs duplicate the same logic, or a command has no failure path.

**Prefer instead:** **winui3-xaml-implementer** / **avalonia-ui-implementer** for markup and theming; **dotnet-desktop-shell-implementer** for windowing and app lifetime.

### [**cpp-pro**](cpp-pro.md) - C++ performance expert
Systems programming specialist with deep knowledge of modern C++ standards, memory management, and performance optimization. Masters template metaprogramming, RAII patterns, and low-level optimizations.

**Use when:** Writing high-performance C++ code, implementing system-level software, optimizing memory usage, or working with embedded systems.

### [**csharp-developer**](csharp-developer.md) - .NET ecosystem specialist
Expert in C# language features and the entire .NET ecosystem. Proficient in ASP.NET Core, Entity Framework, and cross-platform development. Builds enterprise applications with clean architecture.

**Use when:** Developing .NET applications, building ASP.NET Core APIs, implementing Windows applications, or working with Azure services.

### [**django-developer**](django-developer.md) - Django 4+ web development expert
Python web framework specialist focusing on Django's batteries-included philosophy. Masters ORM optimization, async views, and Django's security features. Builds scalable web applications rapidly.

**Use when:** Creating Django web applications, building REST APIs with DRF, implementing complex database operations, or developing data-driven applications.

### [**dotnet-core-expert**](dotnet-core-expert.md) - .NET 8 cross-platform specialist
Modern .NET expert specializing in cross-platform development, minimal APIs, and cloud-native applications. Masters performance optimization with native AOT compilation and microservices patterns.

**Use when:** Building cross-platform .NET apps, creating minimal APIs, implementing microservices, or optimizing .NET performance.

### [**dotnet-framework-4.8-expert**](dotnet-framework-4.8-expert.md) - .NET Framework legacy enterprise specialist
Expert in maintaining and modernizing .NET Framework 4.8 enterprise applications. Masters Web Forms, WCF services, Windows services, and enterprise integration patterns with focus on stability and backward compatibility.

**Use when:** Maintaining legacy .NET Framework apps, modernizing Web Forms applications, working with WCF services, or integrating with Windows enterprise systems.

### [**elixir-expert**](elixir-expert.md) - Elixir and OTP specialist
Elixir language expert focusing on fault-tolerant, concurrent systems using OTP patterns. Masters Phoenix, LiveView, and distributed systems on the BEAM VM. Builds highly available applications with "let it crash" philosophy.

**Use when:** Building fault-tolerant systems, creating real-time apps with Phoenix LiveView, implementing distributed Elixir clusters, or leveraging OTP patterns for reliability.

### [**avalonia-ui-implementer**](avalonia-ui-implementer.md) - Avalonia UI implementer (cross-platform .NET desktop)
Builds cross-platform desktop interfaces in C# and XAML with Avalonia 11+ - the Skia rendering pipeline, styles and control themes, custom controls and drawing, compiled bindings, MVVM, and the X11/Wayland, Win32, and macOS platform backends.

**Use when:** The UI must run from one codebase on Linux and at least one other desktop platform, or Avalonia styling, control themes, or custom rendering need writing or debugging.

**Prefer instead:** **winui3-xaml-implementer** when the surface is Windows-only and should be natively Fluent; **gtkqt-architect** when the project targets GTK 4 or Qt 6 rather than Avalonia.

### [**golang-pro**](golang-pro.md) - Go concurrency specialist
Go language expert focusing on concurrent programming, channels, and goroutines. Masters building efficient, scalable backend services and CLI tools with Go's simplicity and performance.

**Use when:** Building concurrent systems, creating microservices in Go, developing CLI tools, or implementing high-performance network services.

### [**java-architect**](java-architect.md) - Enterprise Java expert
Java ecosystem master with expertise in Spring, Jakarta EE, and enterprise patterns. Specializes in building robust, scalable applications with modern Java features and frameworks.

**Use when:** Developing enterprise Java applications, implementing Spring Boot services, designing Java architectures, or modernizing legacy Java code.

### [**csharp-language-implementer**](csharp-language-implementer.md) - Modern C# language and runtime implementer
Framework-agnostic C# specialist - records and pattern matching, generics and generic math, Span and Memory, source generators and analyzers, async and channels, nullable reference types, benchmark-driven performance, and native interop.

**Use when:** A type should be modelled so a bad state cannot compile, something allocates too much or blocks a thread, reflection must become a source generator for AOT, or native code needs calling safely.

**Prefer instead:** **dotnet-mvvm-implementer** for view-model structure and DI; **dotnet-core-expert** when the target is a web service or cloud workload rather than a desktop app.

### [**kotlin-specialist**](kotlin-specialist.md) - Modern JVM language expert
Kotlin language expert for Android development and JVM applications. Masters coroutines, DSL creation, and Kotlin's expressive features. Builds safe, concise applications.

**Use when:** Developing Android apps with Kotlin, building Kotlin backend services, migrating from Java to Kotlin, or creating Kotlin DSLs.

### [**laravel-specialist**](laravel-specialist.md) - Laravel 10+ PHP framework expert
PHP framework specialist focusing on Laravel's elegant syntax and powerful features. Masters Eloquent ORM, queue systems, and Laravel's extensive ecosystem.

**Use when:** Building Laravel applications, implementing complex queue jobs, creating Laravel packages, or optimizing Eloquent queries.

### [**dotnet-desktop-shell-implementer**](dotnet-desktop-shell-implementer.md) - .NET desktop shell and app-lifetime implementer
Owns process and window lifetime for WinUI 3 and Avalonia apps - startup and shutdown, window management and restoration, single-instance and activation redirection, tray and notification integration, protocol and file associations, hosted background services, settings persistence, and cold-start performance.

**Use when:** The app opens twice, window state is not restored, tray or notification activation misbehaves, startup is slow, or shutdown leaves work unfinished.

**Prefer instead:** **dotnet-mvvm-implementer** for view-model and DI structure; **desktop-app-delivery-implementer** when the association must be declared in the installer manifest.

### [**php-pro**](php-pro.md) - PHP web development expert
Modern PHP specialist with expertise in PHP 8+ features, Composer ecosystem, and framework-agnostic development. Builds secure, performant PHP applications.

**Use when:** Developing PHP applications, modernizing legacy PHP code, implementing PHP APIs, or working with PHP frameworks.

### [**powershell-5.1-expert**](powershell-5.1-expert.md) - Windows PowerShell 5.1 automation specialist  
Expert in PowerShell 5.1 scripting for Windows infrastructure, RSAT modules, and legacy .NET Framework environments. Ensures compatibility, stability, and safe automation across AD, DNS, DHCP, and GPO.

**Use when:** Working with Windows-only automation, legacy modules, on-prem infrastructure, or scripts requiring compatibility with older servers and full .NET Framework.

### [**powershell-7-expert**](powershell-7-expert.md) - Cross-platform PowerShell 7 automation specialist  
Expert in modern PowerShell 7+, .NET 6/7 APIs, cross-platform scripting, CI/CD integration, and cloud automation using Az and Microsoft Graph.

**Use when:** Building modern automation tools, cross-platform scripts, Azure integrations, CI/CD cmdlets, or modernization projects moving off Windows PowerShell.

### [**python-pro**](python-pro.md) - Python ecosystem master
Python language expert covering web development, data science, automation, and system scripting. Masters Pythonic code patterns and the vast Python ecosystem.

**Use when:** Writing Python applications, building data pipelines, creating automation scripts, or developing Python packages.

### [**rails-expert**](rails-expert.md) - Rails 8.1 rapid development expert
Ruby on Rails specialist focusing on convention over configuration and rapid development. Masters Active Record, Hotwire, and Rails' comprehensive feature set.

**Use when:** Building Rails applications, implementing real-time features with Hotwire, optimizing Active Record queries, or upgrading Rails versions.

### [**swiftui-component-implementer**](swiftui-component-implementer.md) - SwiftUI component and state implementer (Swift 6)
Owns the individual view: composition, the Observation framework, layout protocols, Canvas drawing, animation and phase transitions, list and table performance, and per-view accessibility - with every state in the matrix rendered distinctly.

**Use when:** One view looks wrong, updates wrong, or performs badly; a custom control or drawn visualisation needs building; or a component's empty, error, and unknown states need implementing properly.

**Prefer instead:** **macos-swiftui-implementer** for scenes, windows, menus, and navigation; **desktop-ui-designer** when the component has not been designed yet.

### [**rust-engineer**](rust-engineer.md) - Systems programming expert
Rust language specialist focusing on memory safety, ownership patterns, and zero-cost abstractions. Builds reliable, efficient systems software.

**Use when:** Writing systems software in Rust, building performance-critical applications, implementing safe concurrent code, or developing WebAssembly modules.

### [**spring-boot-engineer**](spring-boot-engineer.md) - Spring Boot 3+ microservices expert
Spring ecosystem specialist building cloud-native Java applications. Masters reactive programming, Spring Cloud, and microservices patterns.

**Use when:** Creating Spring Boot microservices, implementing reactive applications, building cloud-native Java apps, or working with Spring Cloud.

### [**sql-pro**](sql-pro.md) - Database query expert
SQL language master optimizing complex queries across different database systems. Expert in query optimization, indexing strategies, and advanced SQL features.

**Use when:** Writing complex SQL queries, optimizing database performance, designing database schemas, or troubleshooting query performance.

### [**swift6-language-implementer**](swift6-language-implementer.md) - Swift 6 language implementer (macOS and server-side)
Specialises in the language itself - generics and existentials, protocol design, macros, typed throws, noncopyable types and ownership, result builders, C and Objective-C interop, Swift Package Manager, and ABI-level performance.

**Use when:** A generic signature, protocol design, or existential question is the blocker; a macro, result builder, or ownership annotation needs writing; or Swift performance needs improving at the allocation or dispatch level.

**Prefer instead:** **swift6-concurrency-implementer** whenever the diagnostic involves actors, Sendable, or a data race; **swiftui-component-implementer** when the symptom is a view rendering wrong.

### [**swift6-concurrency-implementer**](swift6-concurrency-implementer.md) - Swift 6 concurrency and data-race-safety implementer
Specialises in compile-time data-race safety - actor isolation, Sendable conformance, global actors, isolated parameters and regions, structured concurrency and cancellation, AsyncSequence design, and migrating legacy GCD and callback code into Swift 6 language mode without suppressing diagnostics.

**Use when:** The compiler reports an isolation or Sendable error, a target needs migrating to Swift 6 language mode, work does not cancel cleanly, or a runtime data race is suspected.

**Prefer instead:** **swift6-language-implementer** when the problem is generics, macros, or ownership with no concurrency element; **performance-engineer** when the concern is throughput rather than safety.

### [**winui3-xaml-implementer**](winui3-xaml-implementer.md) - WinUI 3 / XAML implementer (Windows 11)
Builds native Windows surfaces on the Windows App SDK - controls and templates, x:Bind compiled bindings, Fluent Design and Mica materials, custom controls and automation peers, theme resources across Light/Dark/High Contrast, and the Composition and Win2D layers.

**Use when:** A Windows-native surface needs building or fixing, Mica or Fluent theming needs applying correctly, or the Windows build diverges from macOS at the view level.

**Prefer instead:** **avalonia-ui-implementer** when the surface must also run on Linux from one codebase; **dotnet-mvvm-implementer** when the problem is view-model structure rather than markup.

##   Quick Selection Guide

| Language/Framework | Subagent | Best For |
|-------------------|----------|----------|
| Angular | **dotnet-mvvm-implementer** | Enterprise web apps, complex SPAs |
| C++ | **cpp-pro** | Systems programming, performance-critical code |
| C#/.NET | **csharp-developer** | Windows apps, enterprise software |
| Django | **django-developer** | Python web apps, REST APIs |
| .NET Core | **dotnet-core-expert** | Cross-platform .NET, microservices |
| .NET Framework | **dotnet-framework-4.8-expert** | Legacy enterprise apps, Windows services |
| Elixir | **elixir-expert** | Fault-tolerant systems, Phoenix/LiveView |
| Flutter | **avalonia-ui-implementer** | Cross-platform mobile apps |
| Go | **golang-pro** | Concurrent systems, microservices |
| Java | **java-architect** | Enterprise applications |
| JavaScript | **csharp-language-implementer** | Web development, Node.js |
| Kotlin | **kotlin-specialist** | Android apps, modern JVM |
| Laravel | **laravel-specialist** | PHP web applications |
| Next.js | **dotnet-desktop-shell-implementer** | Full-stack React apps |
| PHP | **php-pro** | Web development, APIs |
| Python | **python-pro** | General purpose, data science |
| Rails | **rails-expert** | Rapid web development |
| React | **swiftui-component-implementer** | Modern web UIs |
| Rust | **rust-engineer** | Systems software, WebAssembly |
| Spring Boot | **spring-boot-engineer** | Java microservices |
| SQL | **sql-pro** | Database queries, optimization |
| Swift | **swift6-language-implementer** | iOS/macOS development |
| TypeScript | **swift6-concurrency-implementer** | Type-safe JavaScript |
| Vue | **winui3-xaml-implementer** | Progressive web apps |

##   Common Technology Stacks

**Modern Web Application:**
- **swiftui-component-implementer** + **swift6-concurrency-implementer** + **dotnet-desktop-shell-implementer**
- **winui3-xaml-implementer** + **swift6-concurrency-implementer** + **laravel-specialist**
- **dotnet-mvvm-implementer** + **spring-boot-engineer**

**Mobile Development:**
- **avalonia-ui-implementer** for cross-platform
- **swift6-language-implementer** for iOS native
- **kotlin-specialist** for Android native

**Enterprise Backend:**
- **java-architect** + **spring-boot-engineer**
- **csharp-developer** + **dotnet-core-expert**
- **python-pro** + **django-developer**

**Systems Programming:**
- **rust-engineer** for safety-critical systems
- **cpp-pro** for performance-critical applications
- **golang-pro** for concurrent systems

**Real-time & Distributed:**
- **elixir-expert** for fault-tolerant distributed systems
- **elixir-expert** + Phoenix LiveView for real-time web apps

##    Getting Started

1. **Identify your technology stack** and choose the appropriate specialist
2. **Describe your project context** including existing code and constraints
3. **Specify your goals** (learning, optimization, implementation)
4. **Share relevant code** for context-aware assistance
5. **Follow the specialist's guidance** for best practices

##   Best Practices

- **Use language idioms:** Each specialist knows the idiomatic way to write code
- **Leverage ecosystem tools:** Specialists understand the full ecosystem
- **Follow framework conventions:** Each framework has its own best practices
- **Consider performance early:** Language-specific optimizations matter
- **Think about maintenance:** Write code that future developers will understand

Choose your language specialist and write better code today!
