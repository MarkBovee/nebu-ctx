## Why

The nebu-ctx brain/memory system has proven valuable with 18,343+ tool calls and detailed technical findings stored (including root cause analyses for issues #22-25). However, observations reveal friction points: session tool call tracking is inconsistent (sessions show 0 calls despite high usage), memory recall requires specific queries making discovery difficult, and there's limited visibility into memory lifecycle and cross-domain relationships. Enhancing these aspects will increase the system's utility for maintaining project context and institutional memory without changing the core client-server architecture.

## What Changes

- Fix session tool call tracking to accurately reflect usage in session metadata
- Add memory browsing/listing capabilities (by category, time, source type) alongside existing recall
- Implement contextual memory surfacing via hooks to suggest relevant memories during related work
- Add memory lifecycle transparency commands to show promotion candidates and stale memories
- Implement cross-domain memory correlation showing traceability between brain session events and knowledge facts
- Add memory export/import functionality for JSON-based knowledge transfer

## Capabilities

### New Capabilities
- `memory-browsing`: Enable listing/browsing memories with filtering options
- `memory-lifecycle`: Commands to inspect memory lifecycle state and promotion candidates
- `memory-correlation`: Traceability links between brain session events and knowledge facts
- `memory-portability`: Export/import capabilities for knowledge transfer

### Modified Capabilities
- `memory-core`: Enhance session tracking accuracy and add contextual surfacing via hooks
- `memory-brain`: Improve brain tool handler to support listing and lifecycle inspection
- `memory-knowledge`: Enhance knowledge tool handler to show promotion traceability

## Impact

- Client: session tracking fixes, hook enhancements for contextual surfacing
- Server: Enhanced brain/knowledge tool handlers with new listing/lifecycle/export capabilities
- API: New tool command arguments and return formats for listing/lifecycle features
- Storage: No changes required; leverages existing PostgreSQL-backed stores
- CLI: New `ctx memory` subcommands for browsing, lifecycle, and portability features
