# ZeroBuffer Documentation Index

**Purpose:** Central reference for all ZeroBuffer documentation to prevent duplication and ensure consistency.

## Primary Documentation

### Core References (Always Keep Updated)
- **[README.md](README.md)** - Project overview, installation, quick start
- **[PROTOCOL.md](PROTOCOL.md)** - Protocol specification v1.0.0 (authoritative)
- **[API.md](API.md)** - Complete API reference for all languages (authoritative)
- **[CHANGELOG.md](CHANGELOG.md)** - Version history and recent changes

### Language-Specific Implementation
- **[cpp/README.md](cpp/README.md)** - C++ build instructions and specifics
- **[csharp/README.md](csharp/README.md)** - C# package details and usage
- **[python/README.md](python/README.md)** - Python installation and usage
- **[python/DUPLEX_CHANNEL.md](python/DUPLEX_CHANNEL.md)** - Python duplex implementation guide

## Testing Documentation

### Test Specifications
- **[TEST_SCENARIOS.md](TEST_SCENARIOS.md)** - Complete test scenario definitions (authoritative)
- **[CROSS_PLATFORM_TESTS.md](CROSS_PLATFORM_TESTS.md)** - Cross-platform testing guide

### Test Implementation
- **[cpp/tests/README.md](cpp/tests/README.md)** - C++ test framework
- **[csharp/ZeroBuffer.Tests/README.md](csharp/ZeroBuffer.Tests/README.md)** - C# test suite
- **[python/tests/README.md](python/tests/README.md)** - Python test development guide

## Development Documentation

### Build and Release
- **[RELEASE.md](RELEASE.md)** - Release process for all packages
- **[cpp/BUILD_COMMANDS.md](cpp/BUILD_COMMANDS.md)** - C++ build reference
- **[VCPKG_SUBMISSION.md](VCPKG_SUBMISSION.md)** - vcpkg package submission

### Performance
- **[BENCHMARKING_GUIDE.md](BENCHMARKING_GUIDE.md)** - How to run benchmarks
- **[cpp/PERFORMANCE.md](cpp/PERFORMANCE.md)** - C++ performance analysis
- **[csharp/BENCHMARK_RESULTS.md](csharp/BENCHMARK_RESULTS.md)** - C# benchmark results
- **[python/BENCHMARK_RESULTS.md](python/BENCHMARK_RESULTS.md)** - Python benchmark results

## Future/Proposals

### Version 2.0.0 Proposals
- **[DUPLEX_PROTOCOL_PROPOSITION.md](DUPLEX_PROTOCOL_PROPOSITION.md)** - Shared buffer proposal (NOT IMPLEMENTED)
- **[IMPROVEMENTS_TODO.md](IMPROVEMENTS_TODO.md)** - Future enhancement ideas

## Historical/Reference

### Validation Reports (May be outdated)
- **[DOCUMENTATION_VALIDATION.md](DOCUMENTATION_VALIDATION.md)** - Documentation consistency check
- **[TEST_COVERAGE_AND_INCONSISTENCIES.md](TEST_COVERAGE_AND_INCONSISTENCIES.md)** - Historical test coverage

### Design Documents
- **[python/DESIGN.md](python/DESIGN.md)** - Python implementation design
- **[cpp/tests/DESIGN.md](cpp/tests/DESIGN.md)** - C++ test framework design
- **[csharp/ZeroBuffer.ProtocolTests/DESIGN.md](csharp/ZeroBuffer.ProtocolTests/DESIGN.md)** - C# protocol test design

## Documentation Guidelines

### When Adding New Documentation

1. **Check this index first** - Avoid creating duplicate documents
2. **Update existing docs** - Don't create new files for minor additions
3. **Cross-reference properly** - Link to related documents
4. **Mark status clearly** - Use "DRAFT", "CURRENT", "OUTDATED", "FUTURE"
5. **Add to this index** - Keep this file updated

### Authoritative Sources

For each topic, there should be ONE authoritative document:
- **Protocol details** → PROTOCOL.md
- **API reference** → API.md
- **Test scenarios** → TEST_SCENARIOS.md
- **Installation** → README.md (root) or language-specific README

### Avoiding Duplication

❌ **DON'T:**
- Create multiple protocol descriptions
- Duplicate API documentation
- Repeat installation instructions
- Create overlapping test documentation

✅ **DO:**
- Reference the authoritative document
- Add language-specific details to language folders
- Update existing documents
- Use this index to find the right document

## Maintenance

**Last Full Review:** 2024-08-15

**Review Checklist:**
- [ ] All links work
- [ ] No duplicate content across documents
- [ ] Status markers are current
- [ ] New documents added to index
- [ ] Outdated documents marked or removed