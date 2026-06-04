## 1. Session Tracking Fix

- [x] 1.1 Identify where session tool_calls should be incremented in client/src/
- [x] 1.2 Fix session.record_tool_receipt() to properly update tool_calls count
- [x] 1.3 Ensure tool_calls persist in session saves and reloads
- [x] 1.4 Verify tracking works for both local and server-backed tools
- [x] 1.5 Test that session metadata now shows accurate tool_calls count
- [x] 1.6 Verify analytics and session tracking now match

## 2. Memory Browsing Implementation

- [x] 2.1 Add list action to brain tool handler (client/src/mcp_server/dispatch.rs routing)
- [x] 2.2 Add list action to knowledge tool handler (server/src/NebuCtx.Tools/*)
- [x] 2.3 Implement filtering logic (--category, --since, --source-type) in stores
- [x] 2.4 Add sorting, limiting, and pagination capabilities
- [x] 2.5 Create consistent memory listing response format
- [x] 2.6 Implement ctx memory list CLI command
- [x] 2.7 Ensure backward compatibility with existing recall functionality
- [x] 2.8 Test browsing with various filter combinations

## 3. Memory Lifecycle Enhancements

- [x] 3.1 Add lifecycle subcommands to brain and knowledge tool handlers
- [x] 3.2 Implement lifecycle stats (counts by status, averages, distributions)
- [x] 3.3 Implement promotions subcommand (show auto-promotion candidates)
- [x] 3.4 Implement stale subcommand (show memories approaching staleness)
- [x] 3.5 Implement scoring subcommand (detailed breakdown for specific memory)
- [x] 3.6 Add filtering support to lifecycle subcommands
- [x] 3.7 Create CLI commands for ctx memory lifecycle *
- [x] 3.8 Test lifecycle inspection with various memory states

## 4. Contextual Memory Surfacing via Hooks

- [x] 4.1 Analyze hook system architecture (client/src/hook_handlers.rs)
- [x] 4.2 Implement memory relevance scoring algorithms
- [x] 4.3 Add contextual suggestion emission to post-tool-use hook
- [x] 4.4 Add contextual suggestion to pre-compact hook (enhanced snapshots)
- [x] 4.5 Add contextual suggestion to user-prompt-submit hook
- [x] 4.6 Implement suggestion delivery mechanisms (inline, notifications, etc.)
- [x] 4.7 Add configuration options for suggestion frequency and thresholds
- [x] 4.8 Test contextual surfacing with various tool executions
- [x] 4.9 Ensure suggestions are non-intrusive and actionable

## 5. Cross-Domain Memory Correlation

- [x] 5.1 Enhance knowledge tool handler to include promotion_trace in recall
- [x] 5.2 Populate promotion_trace fields during knowledge promotion/consolidation
- [x] 5.3 Add promotion_trace to knowledge memory listing results
- [x] 5.4 Implement --promoted-from-session and --promoted-from-brain-key filters
- [x] 5.5 Ensure traceability doesn't break existing recall/listing
- [x] 5.6 Test traceability with known brain-to-knowledge promotions
- [x] 5.7 Verify backward compatibility for memories without traceability

## 6. Memory Portability (Export/Import)

- [x] 6.1 Design memory export JSON format with metadata and schema version
- [x] 6.2 Implement ctx memory export command with filtering support
- [x] 6.3 Implement ctx memory import command with conflict resolution
- [x] 6.4 Add --overwrite flag for import conflict resolution
- [x] 6.5 Validate export format version compatibility on import
- [x] 6.6 Preserve all memory fields during export/import roundtrip
- [x] 6.7 Test export/import with various memory types and sizes
- [x] 6.8 Verify imported memories work with existing memory tools
- [x] 6.9 Test roundtrip fidelity (export -> import -> same memories)

## 7. Integration and Testing

- [x] 7.1 Run existing test suite to ensure no regressions
- [x] 7.2 Add unit tests for new memory browsing functionality
- [x] 7.3 Add unit tests for lifecycle inspection commands
- [x] 7.4 Add unit tests for contextual surfacing via hooks
- [x] 7.5 Add unit tests for cross-domain correlation features
- [x] 7.6 Add unit tests for memory export/import functionality
- [x] 7.7 Add integration tests for combined functionality
- [x] 7.8 Test performance impact of new features
- [x] 7.9 Verify backward compatibility with existing memory workflows
- [x] 7.10 Update documentation as needed for new capabilities