---
title: Development Tasks
description: Current development priorities and implementation todos
versionIntroduced: 5.0.0
experimental: false
authoritative: true
aiTags: [roadmap, development, tasks, implementation]
---

# Development Tasks

## Current Development Priorities

### High Priority

- [ ] **Models for nested JSON objects**: Extend output models to handle nested JSON structures properly
  - [ ] Implement code generation for nested JSON (generateNestedModels/autoDeserialize flags)
  - [ ] Update README/documentation with examples for nested payloads

- [ ] **Output DataContext enhancements**: Extend C# DataContext with "SET NO COUNT" option
  - [ ] Make configurable via spocr.json and AppDbContextOptions
  - [ ] Plan implementation with session scope considerations

### Medium Priority

- [ ] **Optional JSON deserialization**: Models should not be implicitly deserialized at C# level
  - [ ] Implement concept for optional deserialization process
  - [ ] Document these adjustments

- [ ] **Nested object support in output models**: Handle JSON structures with nested objects
  - [ ] Inventory all Output Models under src/Output for existing JSON properties
  - [ ] Design strategy for nested JSON (separate payload class or dynamic structure)
  - [ ] Adapt code generation for correct nested JSON serialization/deserialization
  - [ ] Add documentation for consumers on using nested JSON fields

### Future Considerations

- [ ] **Performance optimizations**: `SpocR.DataContext.Queries.StoredProcedureQueries` optimization
  - [ ] Analyze current implementation for redundant ObjectId queries
  - [ ] Extend StoredProcedureListAsync to include ObjectId, Definition, Inputs and Outputs
  - [ ] Update all call sites to use extended results

- [ ] **Naming conventions flexibility**: Make naming conventions configurable
  - [ ] Document existing naming conventions
  - [ ] Evaluate which conventions should be optional
  - [ ] Implement configurable variant
  - [ ] Document how users can customize naming configuration

- [ ] **CRUD vs Result-Set procedures**: Distinguish procedure types
  - [ ] Define criteria for CRUD, Single-Result and Multi-Result stored procedures
  - [ ] Implement classification based on result set metadata and JSON settings
  - [ ] Use classification to derive appropriate output models or generation logic

### Integration Testing

- [ ] **Docker-based testing**: Multi-tier testing approach
  - [ ] Set up docker-compose for MSSQL test database with init scripts
  - [ ] Automate stored procedure and custom type deployment in container
  - [ ] Generate reference spocr.json from Docker setup for comparisons
  - [ ] Implement integration tests that verify model generation against reference
  - [ ] Integrate new tests into CI/Build pipeline

### Security & Modernization

- [ ] **AppDbContext improvements**: Assess need for updates
  - [ ] Check transaction safety requirements
  - [ ] Review general security considerations
  - [ ] Evaluate improved configuration pipeline (current C# .NET patterns)

## Implementation Guidelines

### Testing Strategy
- Use Docker containers with MSSQL DB containing testable stored procedures
- Generate reference outputs (spocr.json) for validation
- Implement multi-stage tests for model generation verification

### Development Process
- Work on tasks incrementally 
- Maintain backward compatibility where possible
- Document all changes thoroughly
- Include performance benchmarks for significant changes

## Status Tracking

Tasks marked with checkboxes indicate completion status:
- [ ] Not started
- [x] Completed

Last updated: October 2025