# GitHub Copilot Instructions for Azure Pipelines Agent

This repository contains the Azure Pipelines Agent, a cross-platform build and release agent written in C# for .NET Core. When working with this codebase, follow these guidelines and use the provided development tools.

## Project Structure

- **Source Code**: All source code is in the `src/` directory
- **Build Scripts**: Use `src/dev.sh` (Linux/macOS) or `src/dev.cmd` (Windows)
- **Agent Layout**: Built agent is placed in `{root}/{runtime_id}/_layout`
- **Documentation**: Available in `docs/` directory

## Development Workflow

### Initial Setup
```bash
# Clone and navigate to source
git clone https://github.com/microsoft/azure-pipelines-agent
cd ./src

# First time setup - creates full agent layout
./dev.sh layout
```

### Build Commands

All commands should be run from the `src/` directory:

#### Linux/macOS
```bash
./dev.sh {command} [target_framework] [build_config] [runtime_id] [test_filters]
```

#### Windows
```cmd
dev {command} [target_framework] [build_config] [runtime_id] [test_filters]
```

### Available Commands

| Command | Short | Description |
|---------|-------|-------------|
| `layout` | `l` | Create full agent layout in `{root}/{runtime_id}/_layout` (run first time) |
| `build` | `b` | Build everything and update agent layout folder |
| `test` | `t` | Build agent binaries and run unit tests for current platform |
| `testl0` | `l0` | Run L0 (unit) tests only |
| `testl1` | `l1` | Run L1 (integration) tests only |
| `package` | `p` | Create distribution packages |
| `hash` | | Generate hash files |
| `report` | | Generate test reports |
| `lint` | | Run code linting |
| `lint-verify` | | Verify linting rules |

### Normal Development Flow
```bash
# 1. Initial layout (first time only)
./dev.sh layout

# 2. Make code changes
# ... edit files ...

# 3. Build and test
./dev.sh build
./dev.sh test

# 4. Commit changes
git add .
git commit -m "Your changes"
git push
```

## Project Details

### Technology Stack
- **Language**: C# (.NET Core)
- **Target Framework**: net6.0 (default), net8.0 supported
- **Platforms**: Windows (x64, x86, ARM64), macOS (x64, ARM64), Linux (x64, ARM)
- **Build System**: MSBuild with custom scripts

### Key Components
- **Agent.Listener**: Main agent listener service
- **Agent.Worker**: Task execution worker
- **Agent.PluginHost**: Plugin hosting environment
- **Agent.Plugins**: Built-in plugins
- **Agent.Sdk**: SDK for agent development

### Dependencies
- **.NET SDK**: Automatically downloaded if not present
- **Git**: Required for development (Git for Windows on Windows)
- **Node.js**: For certain pipeline tasks

## Testing

### Test Types
- **L0 Tests**: Unit tests (`./dev.sh testl0`)
- **L1 Tests**: Integration tests (`./dev.sh testl1`)

### Test Filters
Add custom test filters as the 5th parameter:
```bash
./dev.sh test net6.0 Debug osx-arm64 "TestCategory=YourCategory"
```

## Debugging

### Debug Mode
Run the agent with debug mode:
```bash
./config.sh --debug
./run.sh --debug
```

### Environment Variables for Debugging
- `VSTSAGENT_DEBUG_TASK`: Debug specific tasks by ID or name+version

## Build Configurations

### Available Configurations
- **Debug**: Development builds with debug symbols
- **Release**: Production builds optimized for performance

### Runtime IDs
Common runtime identifiers:
- `win-x64`, `win-x86`, `win-arm64`
- `osx-x64`, `osx-arm64`  
- `linux-x64`, `linux-arm`, `linux-arm64`
- `rhel.6-x64`

### Package Types
- `agent` (default): Full agent package
- `pipelines-agent`: Pipelines-specific package (excludes Node 6/10)

## Code Style

Follow the [.NET Foundation coding guidelines](https://github.com/dotnet/corefx/blob/master/Documentation/coding-guidelines/coding-style.md):

- Use PascalCase for public members
- Use camelCase for private fields
- Use meaningful names
- Keep methods focused and small
- Include XML documentation for public APIs

## Common Tasks

### Adding New Features
1. Create/modify source files in appropriate `src/` subdirectories
2. Add unit tests in `src/Test/`
3. Build and test: `./dev.sh build && ./dev.sh test`
4. Update documentation if needed

### Troubleshooting Build Issues
- **"unzip not found"**: Install unzip (`sudo apt install unzip` on WSL)
- **Missing dependencies**: Run `./dev.sh layout` to restore
- **Test failures**: Check platform-specific test filters

### Working with Agent Layout
The built agent in `_layout/` can be used for:
- Local testing and debugging
- Manual agent installation
- Testing pipeline integration

To configure and run the built agent:
```bash
cd {root}/{runtime_id}/_layout
./config.sh
./run.sh
```

## Best Practices

1. **Always run layout first** when starting development
2. **Build before testing** to ensure latest changes are included
3. **Run tests** before committing to catch regressions
4. **Use appropriate runtime ID** for your target platform
5. **Follow the normal dev flow** for consistent results

## Additional Resources

- [Contributing Guide](../docs/contribute.md)
- [Azure DevOps Agent Documentation](https://docs.microsoft.com/azure/devops/pipelines/agents/)
- [Self-hosted Agent Installation](https://learn.microsoft.com/en-us/azure/devops/pipelines/agents/windows-agent?view=azure-devops)
