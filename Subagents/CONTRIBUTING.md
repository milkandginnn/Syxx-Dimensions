# Contributing to Awesome OpenCode CLI Subagents

Thank you for your interest in contributing to this collection!

## 🤝 How to Contribute

### Adding a New Subagent

1. **Choose the right category** - Place your subagent in the most appropriate category folder
2. **Test your subagent** - Ensure it works with OpenCode CLI
3. **Update required files** - When adding a new agent, you must update:
   - **Main README.md**: Add your agent to the appropriate category section
   - **Category README.md**: Add detailed description
   - **Your agent .md file**: Create the actual agent definition following the template
4. **Submit a PR** - Include a clear description of the subagent's purpose

### Agent Template Format

All agents must follow this YAML frontmatter structure:

```markdown
---
description: When and why this agent should be invoked
mode: subagent
tools:
  write: true
  edit: true
  bash: true
temperature: 0.1
steps: 20
---
```

### Temperature Guidelines

Choose appropriate temperature based on agent task type:

| Task Type | Temperature | Steps | Examples |
|-----------|-------------|-----------|----------|
| Code Generation | 0.1 | 20 | backend-developer, python-pro |
| Debugging | 0.05 | 25 | debugger, error-detective |
| Code Review | 0.15 | 25 | code-reviewer, security-auditor |
| Infrastructure | 0.15 | 20 | devops-engineer, terraform-engineer |
| Architecture | 0.35 | 25 | api-designer, microservices-architect |
| Documentation | 0.35 | 15 | documentation-engineer |
| Research | 0.45 | 20 | market-researcher, trend-analyst |
| Strategy/Product | 0.5 | 20 | product-manager, business-analyst |
| Creative | 0.55 | 20 | ui-designer, content-marketer |

### Pull Request Process

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/new-subagent`)
3. Add your subagent following the template
4. Submit a pull request with a clear description

## 📝 License

By contributing, you agree that your contributions will be licensed under the MIT License.
