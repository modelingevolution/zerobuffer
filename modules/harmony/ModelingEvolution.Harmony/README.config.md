# Configuration Files

## harmony-config.json
This is the default configuration file used by the Harmony test runner. It contains settings for:
- Platform executables and arguments
- Working directories for each platform
- Feature file paths
- Timeout settings

## harmony-config.production.json
Production configuration for running actual test servers. This file contains paths to real executables.

## Test Configuration
For unit tests, a separate test configuration is used that doesn't start real processes. See:
- `ModelingEvolution.Harmony.Tests/harmony-config.test.json`
- `ModelingEvolution.Harmony.Tests/TestBase.cs`

## Environment-Specific Configuration
To use different configurations based on environment:
1. Set the `HARMONY_CONFIG_PATH` environment variable to point to your config file
2. Or use the `IConfigurationProvider` interface to inject configuration programmatically